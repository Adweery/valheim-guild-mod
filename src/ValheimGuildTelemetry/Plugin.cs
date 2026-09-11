using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimGuildTelemetry;

[BepInPlugin("adwery.valheim.guildtelemetry","Valheim Guild Telemetry","1.3.2")]
public partial class Plugin : BaseUnityPlugin
{
    private const string RpcName="AdweryGuildTelemetryV1";
    internal static Plugin Instance;
    private ZRoutedRpc registered;
    private ConfigEntry<string> expectedUid, output;
    private readonly string session=Guid.NewGuid().ToString("N");
    private long sequence;
    private float nextHello, nextUpdateRetry;
    private ProgressState state;
    private bool loadFailed;
    private Harmony harmony;

    private void Awake()
    {
        Instance=this;
        expectedUid=Config.Bind("Server","WorldUid","1208923522","Only this world is tracked.");
        output=Config.Bind("Server","OutputPath",Path.Combine(Paths.ConfigPath,"guild-telemetry.json"),"Server-only cumulative progress file.");
        ConfigureQuests();
        ConfigureNearbySupplies();
        harmony=new Harmony("adwery.valheim.guildtelemetry");
        harmony.PatchAll();
        Logger.LogInfo("Guild telemetry loaded: kill credit, successful crafting and biome entry hooks ready.");
    }

    private void OnDestroy() { SaveSupplies(); CloseJournal(); if(backdrop!=null) Destroy(backdrop); harmony?.UnpatchSelf(); if (Instance==this) Instance=null; }
    private bool CorrectWorld() => WorldGuard.Matches(ZNet.instance,expectedUid.Value);

    private void Update()
    {
        if (Time.realtimeSinceStartup < nextUpdateRetry) return;
        try
        {
            if (ZRoutedRpc.instance != null && registered != ZRoutedRpc.instance)
            {
                ZRoutedRpc.instance.Register<ZPackage>(RpcName,Receive);
                registered=ZRoutedRpc.instance;
                quests.Register(registered);
            }
            TickSupplies();
            quests.Tick();
            if (!CorrectWorld()) return;
            if (ZNet.instance.IsServer())
            {
                EnsureState();
            }
            else if (Player.m_localPlayer != null && Time.realtimeSinceStartup>=nextHello)
            {
                nextHello=Time.realtimeSinceStartup+30;
                Emit("hello","",0);
            }
        }
        catch (Exception e) { Logger.LogWarning("Telemetry update will retry in 30 seconds: "+e.GetType().Name); nextUpdateRetry=Time.realtimeSinceStartup+30; }
    }

    private void EnsureState()
    {
        if (state!=null || loadFailed) return;
        try
        {
            if (File.Exists(output.Value))
            {
                state=JsonCodec.Read(File.ReadAllText(output.Value));
                if (state==null || state.schema!=1 || state.uid!=expectedUid.Value || state.players==null) throw new InvalidDataException();
            }
            else
            {
                state=new ProgressState {world=ZNet.instance.GetWorldName(),uid=expectedUid.Value};
                Persist();
            }
        }
        catch (Exception e) { state=null;loadFailed=true;Logger.LogError("Telemetry state requires review: "+e.GetType().Name); }
    }

    private void Persist()
    {
        var full=Path.GetFullPath(output.Value);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        var tmp=full+".tmp";
        File.WriteAllText(tmp,JsonCodec.Write(state));
        if (File.Exists(full)) File.Replace(tmp,full,null); else File.Move(tmp,full);
    }

    private void Receive(long sender,ZPackage package)
    {
        if (!CorrectWorld() || !ZNet.instance.IsServer() || package.Size()>1024) return;
        try
        {
            // The identity comes from the authenticated game connection, never from the payload.
            var peer=ZNet.instance.GetPeer(sender);
            if (peer==null || peer.m_socket==null || !peer.m_socket.IsConnected()) return;
            var uid=package.ReadString();
            var eventSession=package.ReadString();
            var seq=package.ReadLong();
            var kind=package.ReadString();
            var key=package.ReadString();
            var amount=package.ReadInt();
            if (uid!=expectedUid.Value) return;
            EnsureState();
            if (state==null || loadFailed) return;
            if (state.Apply(peer.m_socket.GetHostName(),peer.m_playerName,eventSession,seq,kind,key,amount,DateTimeOffset.UtcNow.ToUnixTimeSeconds())) Persist();
        }
        catch (Exception e) { Logger.LogWarning("Telemetry event rejected: "+e.GetType().Name); }
    }

    internal void Emit(string kind,string key,int amount)
    {
        try
        {
            if (!CorrectWorld() || ZNet.instance.IsServer() || Player.m_localPlayer==null || registered==null) return;
            var package=new ZPackage();
            package.Write(expectedUid.Value);package.Write(session);package.Write(++sequence);
            package.Write(kind);package.Write(key);package.Write(amount);
            var peer=ZNet.instance.GetServerPeer();
            if (peer!=null) registered.InvokeRoutedRPC(peer.m_uid,RpcName,package);
        }
        catch (Exception e) { Logger.LogWarning("Telemetry event not sent: "+e.GetType().Name); }
    }
}

[HarmonyPatch(typeof(Game),"RPC_RegisterKill")]
internal static class KillPatch
{
    private static void Postfix(string enemyName,bool cheatsUsed)
    {
        if (!cheatsUsed) Plugin.Instance?.Emit("kill",enemyName,1);
    }
}

[HarmonyPatch(typeof(Player),"AddKnownBiome")]
internal static class BiomePatch
{
    private static void Postfix(Player __instance,object __0)
    {
        if (__instance!=Player.m_localPlayer || __0==null) return;
        try
        {
            var value=AccessTools.Field(__0.GetType(),"Biome")?.GetValue(__0);
            if (value!=null) Plugin.Instance?.Emit("biome",value.ToString(),1);
        }
        catch (Exception) { /* Telemetry must never interrupt gameplay. */ }
    }
}

[HarmonyPatch(typeof(InventoryGui),"DoCrafting")]
internal static class CraftPatch
{
    internal class Before { public string name; public int count; public bool food; }
    private static int Count(Player player,string name) => player.GetInventory().GetAllItems().Where(x=>x.m_shared.m_name==name).Sum(x=>x.m_stack);
    private static void Prefix(Player player,Recipe ___m_craftRecipe,ItemDrop.ItemData ___m_craftUpgradeItem,out Before __state)
    {
        __state=null;
        if (player!=Player.m_localPlayer || ___m_craftRecipe==null || ___m_craftUpgradeItem!=null || player.NoCostCheat()) return;
        try
        {
            var name=___m_craftRecipe.m_item.m_itemData.m_shared.m_name;
            __state=new Before {name=name,count=Count(player,name),food=___m_craftRecipe.m_item.m_itemData.m_shared.m_food>0};
        }
        catch (Exception) { __state=null; }
    }
    private static void Postfix(Player player,Before __state)
    {
        if (__state==null || player==null) return;
        try
        {
        var amount=Count(player,__state.name)-__state.count;
        if (amount>0)
        {
            Plugin.Instance?.Emit("craft",__state.name,amount);
            if (__state.name.StartsWith("$item_arrow",StringComparison.Ordinal)) Plugin.Instance?.Emit("craft","category:arrows",amount);
            if (__state.name.StartsWith("$item_staff",StringComparison.Ordinal)) Plugin.Instance?.Emit("craft","category:staff",amount);
            if (__state.food) Plugin.Instance?.Emit("craft","category:food",amount);
        }
        }
        catch (Exception) { /* The game action already succeeded. */ }
    }
}
