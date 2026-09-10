using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ValheimGuildTelemetry;

internal class QuestBridge
{
    private const string Request="AdweryGuildQuestsRequestV1", Reply="AdweryGuildQuestsReplyV1";
    private readonly string uid, path;
    private readonly Dictionary<long,float> requests=new Dictionary<long,float>();
    private float nextPoll, nextRead;
    private QuestFeedData feed;
    private ZRoutedRpc router;
    public readonly QuestClientState Client=new QuestClientState();
    public QuestBridge(string uid,string path) { this.uid=uid;this.path=path; }
    public void Register(ZRoutedRpc rpc)
    {
        router=rpc;feed=null;nextRead=0;nextPoll=0;requests.Clear();
        Client.Snapshot=null;Client.Status="Čakám na questy zo servera…";Client.Completions.Clear();
        rpc.Register<ZPackage>(Request,OnRequest);
        rpc.Register<ZPackage>(Reply,OnReply);
    }
    public void Tick()
    {
        if(!WorldGuard.Matches(ZNet.instance,uid) || ZNet.instance.IsServer() || Player.m_localPlayer==null || router==null) return;
        if(Time.realtimeSinceStartup<nextPoll) return;
        nextPoll=Time.realtimeSinceStartup+5;
        var peer=ZNet.instance.GetServerPeer();
        if(peer==null) return;
        var p=new ZPackage();p.Write(uid);p.Write(1);
        router.InvokeRoutedRPC(peer.m_uid,Request,p);
    }
    private void OnRequest(long sender,ZPackage package)
    {
        try
        {
            if(!WorldGuard.Matches(ZNet.instance,uid) || !ZNet.instance.IsServer() || package==null || package.Size()>128) return;
            var peer=ZNet.instance.GetPeer(sender);
            if(peer?.m_socket==null || !peer.m_socket.IsConnected()) return;
            if(package.ReadString()!=uid || package.ReadInt()!=1) return;
            float now=Time.realtimeSinceStartup;
            if(requests.TryGetValue(sender,out var last) && now-last<2) return;
            if(requests.Count>100) requests.Clear();
            requests[sender]=now;
            if(now>=nextRead)
            {
                nextRead=now+2;feed=null;
                if(File.Exists(path) && new FileInfo(path).Length<=1048576)
                    feed=QuestJson.Read<QuestFeedData>(File.ReadAllText(path));
            }
            // Only the profile belonging to the authenticated peer is sent.
            var snapshot=feed?.Select(peer.m_socket.GetHostName(),uid,DateTimeOffset.UtcNow.ToUnixTimeSeconds()) ?? new QuestSnapshot {uid=uid,status="unavailable"};
            var json=QuestJson.Write(snapshot);
            if(System.Text.Encoding.UTF8.GetByteCount(json)>131072) return;
            var response=new ZPackage();response.Write(json);
            router.InvokeRoutedRPC(sender,Reply,response);
        }
        catch(Exception) { /* Transient file/socket errors cannot interrupt the game. */ }
    }
    private void OnReply(long sender,ZPackage package)
    {
        try
        {
            if(!WorldGuard.Matches(ZNet.instance,uid) || ZNet.instance.IsServer() || package==null || package.Size()>131080) return;
            var server=ZNet.instance.GetServerPeer();
            if(server==null || server.m_uid!=sender) return;
            Client.Accept(QuestJson.Read<QuestSnapshot>(package.ReadString()),uid,Time.realtimeSinceStartup);
        }
        catch(Exception) { /* Discard malformed or mismatched responses. */ }
    }
}
