using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ValheimGuildTelemetry;

[Serializable] public class GuildQuest
{
    public int id;
    public string title, chapter, owner, note, assessment;
    public int xp;
    public bool completed, automatic;
    public long current, target;
}
[Serializable] public class QuestProfile
{
    public string account_id, profile_name, class_name, rank;
    public int renown, level;
    public bool bound, truncated;
    public List<GuildQuest> quests;
}
[Serializable] public class QuestSnapshot
{
    public int schema=1;
    public string uid, status, profile_name, class_name, rank;
    public long generated_at;
    public int renown, level;
    public bool truncated;
    public List<GuildQuest> quests=new List<GuildQuest>();
}
[Serializable] public class QuestFeedData
{
    public int schema;
    public string uid;
    public long generated_at;
    public List<QuestProfile> players;

    public QuestSnapshot Select(string account, string world, long now)
    {
        var result=new QuestSnapshot {uid=world,status="unavailable",generated_at=generated_at};
        if (schema!=1 || uid!=world || generated_at<now-180 || generated_at>now+120 || players==null) return result;
        var p=players.SingleOrDefault(x=>x.account_id==account);
        if (p==null) { result.status="unlinked";return result; }
        result.status="ok";result.profile_name=p.profile_name;result.class_name=p.class_name;
        result.rank=p.rank;result.renown=p.renown;result.level=p.level;
        result.quests=p.quests;result.truncated=p.truncated;
        return result;
    }
}
public static class QuestJson
{
    public static string Write<T>(T value)
    {
        using(var stream=new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
    public static T Read<T>(string value)
    {
        using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(value)))
            return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
    }
}
public class QuestClientState
{
    public QuestSnapshot Snapshot;
    public double receivedAt;
    public string Status="Čakám na questy zo servera…";
    public readonly Queue<string> Completions=new Queue<string>();

    public bool Accept(QuestSnapshot next,string uid,double now)
    {
        if (next==null || next.schema!=1 || next.uid!=uid) return false;
        if(next.status=="unavailable") { Status="Bot je dočasne nedostupný"; return false; }
        if(next.status=="unlinked") { Snapshot=null;Status="Prepoj účet cez /linkgame v Discorde";Completions.Clear();return true; }
        if(next.status!="ok" || next.quests==null || next.quests.Count>256 || next.profile_name==null || next.profile_name.Length>80) return false;
        var ids=new HashSet<int>();
        foreach(var q in next.quests)
            if(q==null || q.id<1 || !ids.Add(q.id) || q.title==null || q.title.Length>150 || q.chapter==null || q.chapter.Length>100 || (q.note!=null && q.note.Length>2000) || (q.assessment!=null && q.assessment.Length>200) || q.current<0 || q.target<0 || q.xp<0) return false;
        if(Snapshot!=null && Snapshot.profile_name==next.profile_name)
        {
            if(next.generated_at<Snapshot.generated_at) return false;
            var old=Snapshot.quests.ToDictionary(x=>x.id);
            foreach(var q in next.quests)
                if(q.completed && old.TryGetValue(q.id,out var previous) && !previous.completed && Completions.Count<10)
                    Completions.Enqueue(q.title+"  •  +"+q.xp+" Renown");
        }
        else Completions.Clear();
        Snapshot=next;receivedAt=now;Status="";return true;
    }
    public bool Stale(double now,long utcNow) => Snapshot==null || now-receivedAt>30 || utcNow-Snapshot.generated_at>90;
}
