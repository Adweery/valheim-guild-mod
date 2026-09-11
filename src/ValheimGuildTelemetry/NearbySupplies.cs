using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
namespace ValheimGuildTelemetry;

public partial class Plugin
{
    internal static readonly HashSet<Container> LoadedChests=new HashSet<Container>();
    private static readonly MethodInfo LoadChest=AccessTools.Method(typeof(Container),"Load");
    private static readonly MethodInfo AccessChest=AccessTools.Method(typeof(Container),"CheckAccess");
    private static readonly FieldInfo GuardedChest=AccessTools.Field(typeof(Container),"m_checkGuardStone");
    private ConfigEntry<float> scanRadius;
    private int nearbyCursor, nearbyCount;
    private bool nearbyLimited;
    private readonly HashSet<string> nearbyIds=new HashSet<string>();

    private void ConfigureNearbySupplies()
    {
        scanRadius=Config.Bind("Supplies","ScanRadius",30f,"Scan loaded accessible containers within this many metres (5-100). No extra network requests or ownership changes.");
    }
    private bool AllowedChest(Container chest)
    {
        if(chest==null || LoadChest==null || AccessChest==null || GuardedChest==null) return false;
        try
        {
            if((bool)GuardedChest.GetValue(chest) && !PrivateArea.CheckAccess(chest.transform.position,0f,false,false)) return false;
            return (bool)AccessChest.Invoke(chest,new object[]{Player.m_localPlayer.GetPlayerID()});
        }
        catch(Exception) { return false; }
    }
    private void ScanNearbySupplies()
    {
        if(supplies==null) return;
        LoadedChests.RemoveWhere(c=>c==null);
        float radius=Mathf.Clamp(scanRadius.Value,5f,100f);
        Vector3 position=Player.m_localPlayer.transform.position;
        var candidates=LoadedChests.Where(c=>c.gameObject.activeInHierarchy && (c.transform.position-position).sqrMagnitude<=radius*radius).OrderBy(c=>(c.transform.position-position).sqrMagnitude).ToList();
        nearbyLimited=candidates.Count>100;
        var accessible=new List<Container>();nearbyIds.Clear();
        foreach(var chest in candidates.Take(100))
        {
            var view=chest.GetComponent<ZNetView>();
            if(view==null || !view.IsValid()) continue;
            string id=view.GetZDO().m_uid.ToString();
            if(!AllowedChest(chest))
            {
                // A previously accessible chest may have become private or warded.
                if(supplies.chests.RemoveAll(c=>c.id==id)>0) supplyDirty=true;
                continue;
            }
            nearbyIds.Add(id);accessible.Add(chest);
        }
        nearbyCount=accessible.Count;
        if(nearbyCount==0) return;
        for(int i=0;i<Math.Min(32,nearbyCount);i++)
        {
            var chest=accessible[(nearbyCursor+i)%nearbyCount];
            try
            {
                // Load only imports the latest already-replicated ZDO. Never request ownership,
                // open the chest, or call CheckForChanges (which can auto-destroy empty objects).
                LoadChest.Invoke(chest,null);
                ObserveChest(chest);
            }
            catch(Exception) { supplyWarning="Niektorú truhlu sa nepodarilo obnoviť. Skús ju otvoriť."; }
        }
        nearbyCursor=(nearbyCursor+Math.Min(32,nearbyCount))%nearbyCount;
    }
    internal void ForgetDestroyedChest(Container chest)
    {
        LoadedChests.Remove(chest);
        if(!CanShowQuests() || supplies==null || chest==null) return;
        var view=chest.GetComponent<ZNetView>();
        if(view==null || !view.IsValid()) return;
        string id=view.GetZDO().m_uid.ToString();
        if(supplies.chests.RemoveAll(c=>c.id==id)>0) { supplyDirty=true;nextSupplyScan=0; }
    }
}
[HarmonyPatch(typeof(Container),"Awake")]
internal static class NearbyChestLoadedPatch
{
    private static void Postfix(Container __instance) { if(__instance!=null) Plugin.LoadedChests.Add(__instance); }
}
[HarmonyPatch(typeof(Container),"OnDestroyed")]
internal static class NearbyChestDestroyedPatch
{
    private static void Prefix(Container __instance) { try { Plugin.Instance?.ForgetDestroyedChest(__instance); } catch(Exception) { } }
}
