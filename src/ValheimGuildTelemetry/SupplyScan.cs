using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
namespace ValheimGuildTelemetry;

public partial class Plugin
{
    private SupplyCache supplies;
    private List<SupplyItem> carried=new List<SupplyItem>();
    private Container openChest;
    private float nextSupplyScan, nextSupplySave;
    private string supplyIdentity, supplyPath, supplyWarning;
    private Task supplyWrite;
    private bool supplyDirty, showSupplies;
    private Vector2 supplyScroll;
    private string supplyFilter="";
    private List<SupplyItem> supplyItems=new List<SupplyItem>();
    private Dictionary<string,int> carriedCounts=new Dictionary<string,int>(), chestCounts=new Dictionary<string,int>();
    private Dictionary<string,List<ChestSeen>> sources=new Dictionary<string,List<ChestSeen>>();
    private int supplyPage;
    private void IndexSupplies()
    {
        supplyItems=carried.Concat(supplies.chests.SelectMany(c=>c.items)).Concat(supplies.targets.Select(t=>new SupplyItem {key=t.key,name=t.name ?? t.key})).GroupBy(i=>i.key).Select(g=>g.First()).OrderBy(i=>i.name).ToList();
        carriedCounts=carried.GroupBy(i=>i.key).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.count));
        chestCounts=supplies.chests.SelectMany(c=>c.items).GroupBy(i=>i.key).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.count));
        sources=supplies.chests.SelectMany(c=>c.items.Select(i=>i.key).Distinct().Select(key=>new {key,chest=c})).GroupBy(x=>x.key).ToDictionary(g=>g.Key,g=>g.Select(x=>x.chest).ToList());
    }

    private void TickSupplies()
    {
        if(!CanShowQuests())
        {
            SaveSupplies();openChest=null;if(supplyDirty && supplyWrite!=null && !supplyWrite.IsCompleted) return;supplies=null;supplyIdentity=null;carried.Clear();return;
        }
        if(Time.realtimeSinceStartup<nextSupplyScan) return;
        nextSupplyScan=Time.realtimeSinceStartup+2;
        try
        {
            long playerId=Player.m_localPlayer.GetPlayerID();
            if(playerId==0) return;
            string identity=expectedUid.Value+":"+playerId;
            if(identity!=supplyIdentity)
            {
                // A pending write owns an immutable snapshot. Wait before switching cache files.
                if(supplyWrite!=null && !supplyWrite.IsCompleted) return;
                SaveSupplies();
                if(supplyWrite!=null && !supplyWrite.IsCompleted) return;
                supplyIdentity=identity;supplies=new SupplyCache {identity=identity};supplyWarning=null;
                using(var hash=SHA256.Create())
                    supplyPath=Path.Combine(BepInEx.Paths.ConfigPath,"ValheimGuildSupplies",BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","")+".json");
                try
                {
                    if(File.Exists(supplyPath))
                    {
                        if(new FileInfo(supplyPath).Length>8388608) throw new InvalidDataException();
                        var loaded=QuestJson.Read<SupplyCache>(File.ReadAllText(supplyPath));
                        if(!loaded.Valid(identity,DateTimeOffset.UtcNow.ToUnixTimeSeconds())) throw new InvalidDataException();
                        supplies=loaded;
                    }
                }
                catch(Exception) { supplyWarning="Starý záznam zásob sa nedal načítať. Otvor truhly znova."; }
            }
            carried=ReadItems(Player.m_localPlayer.GetInventory());
            ScanNearbySupplies();
            if(openChest!=null && InventoryGui.IsVisible()) ObserveOpenChest();
            IndexSupplies();
            if(Time.realtimeSinceStartup>=nextSupplySave) { nextSupplySave=Time.realtimeSinceStartup+10;SaveSupplies(); }
        }
        catch(Exception) { supplyWarning="Sken zásob sa zopakuje pri ďalšom obnovení."; }
    }
    private static List<SupplyItem> ReadItems(Inventory inventory)
    {
        if(inventory==null) return new List<SupplyItem>();
        var result=inventory.GetAllItems().Where(i=>i!=null && i.m_shared!=null && i.m_stack>0).Select(i=>new SupplyItem
        {
            key=i.m_dropPrefab!=null ? i.m_dropPrefab.name : i.m_shared.m_name,
            name=i.m_shared.m_name,count=i.m_stack,quality=i.m_quality,equipped=i.m_equipped
        }).ToList();
        if(!SupplyCache.ValidItems(result)) throw new InvalidDataException();
        return result;
    }
    internal void OpenSupplyChest(Container chest)
    {
        // This runs after InventoryGui.Show, once the game has granted access.
        if(!CanShowQuests()) return;
        openChest=chest;nextSupplyScan=0;
    }
    internal void CloseSupplyChest()
    {
        try { if(CanShowQuests() && openChest!=null) ObserveOpenChest();SaveSupplies(); }
        catch(Exception) { /* A disappearing container leaves a clearly dated observation. */ }
        openChest=null;
    }
    private void ObserveOpenChest()
    {
        if(supplies==null || openChest==null) return;
        ObserveChest(openChest);
        carried=ReadItems(Player.m_localPlayer.GetInventory());
    }
    private void ObserveChest(Container chest)
    {
        var view=chest.GetComponent<ZNetView>();
        if(view==null || !view.IsValid()) return;
        var id=view.GetZDO().m_uid.ToString();
        supplies.Observe(new ChestSeen {id=id,name="Truhla ("+Mathf.RoundToInt(chest.transform.position.x)+", "+Mathf.RoundToInt(chest.transform.position.z)+")",checked_at=DateTimeOffset.UtcNow.ToUnixTimeSeconds(),items=ReadItems(chest.GetInventory())});
        // Refresh both sides of a transfer. Storage is never added to the carried readiness count.
        supplyDirty=true;
    }
    private void SaveSupplies()
    {
        if(supplyWrite!=null && supplyWrite.IsFaulted)
        { var ignored=supplyWrite.Exception;supplyWarning="Zásoby sa nepodarilo uložiť na disk.";supplyWrite=null;supplyDirty=true; }
        if(!supplyDirty || supplies==null || supplyPath==null || (supplyWrite!=null && !supplyWrite.IsCompleted)) return;
        var snapshot=supplies.Copy();var path=supplyPath;supplyDirty=false;
        supplyWrite=Task.Run(()=>
        {
            var json=QuestJson.Write(snapshot);
            if(Encoding.UTF8.GetByteCount(json)>8388608) throw new InvalidDataException();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path+".tmp",json);
            if(File.Exists(path)) File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);
        });
    }
    private static string Age(long stamp)
    {
        var minutes=Math.Max(0,(DateTimeOffset.UtcNow.ToUnixTimeSeconds()-stamp)/60);
        return minutes<1 ? L("pred chvíľou") : minutes<60 ? L("pred ")+minutes+L(" min") : L("pred ")+(minutes/60)+L(" h");
    }
    private void DrawSupplies()
    {
        GUILayout.Label(L("OSOBNÁ PRÍPRAVA A ZÁSOBY"),section);
        GUILayout.Label(L("V dosahu sa obsah obnovuje automaticky z hernej synchronizácie. Vzdialené truhly ostávajú starším záznamom. Cache je lokálna pre postavu a svet."),muted);
        GUILayout.Label(L("Rádius: ")+Mathf.Clamp(scanRadius.Value,5f,100f)+L(" m • prístupné truhly: ")+nearbyCount+(nearbyLimited ? L(" • limit 100 najbližších") : ""),muted);
        if(supplyWarning!=null) GUILayout.Label(L(supplyWarning),muted);
        if(supplies==null) { GUILayout.Label(L("Čakám na inventár postavy…"),body);return; }
        GUI.SetNextControlName("GuildSupplySearch");
        supplyFilter=GUILayout.TextField(supplyFilter,80);
        GUILayout.Label(L("Vyhľadávanie podľa názvu • tlačidlami −/+ nastav cieľový počet do batoha"),muted);
        supplyScroll=GUILayout.BeginScrollView(supplyScroll);
        var items=supplyItems.Where(i=>supplyFilter.Length==0 || Localization.instance.Localize(i.name).IndexOf(supplyFilter,StringComparison.OrdinalIgnoreCase)>=0).ToList();
        supplyPage=Math.Min(supplyPage,Math.Max(0,(items.Count-1)/30));
        GUILayout.BeginHorizontal();
        if(GUILayout.Button(L("Predchádzajúce"),button)) supplyPage=Math.Max(0,supplyPage-1);
        GUILayout.Label((supplyPage+1)+" / "+Math.Max(1,(items.Count+29)/30),muted);
        if(GUILayout.Button(L("Ďalšie"),button)) supplyPage=Math.Min(Math.Max(0,(items.Count-1)/30),supplyPage+1);
        GUILayout.EndHorizontal();
        foreach(var item in items.Skip(supplyPage*30).Take(30))
        {
            var label=Localization.instance.Localize(item.name);
            if(supplyFilter.Length>0 && label.IndexOf(supplyFilter,StringComparison.OrdinalIgnoreCase)<0) continue;
            int have=carriedCounts.TryGetValue(item.key,out var h)?h:0,seen=chestCounts.TryGetValue(item.key,out var c)?c:0;
            int target=supplies.targets.FirstOrDefault(t=>t.key==item.key)?.count ?? 0;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();GUILayout.Label(label,section);
            if(GUILayout.Button("−",button,GUILayout.Width(32))) { supplies.SetTarget(item.key,target-1,item.name);supplyDirty=true; }
            GUILayout.Label(L("Cieľ: ")+target,muted,GUILayout.Width(65));
            if(GUILayout.Button("+",button,GUILayout.Width(32))) { supplies.SetTarget(item.key,target+1,item.name);supplyDirty=true; }
            if(GUILayout.Button("+5",button,GUILayout.Width(40))) { supplies.SetTarget(item.key,target+5,item.name);supplyDirty=true; }
            GUILayout.EndHorizontal();
            GUILayout.Label(L("Pri tebe: ")+have+L(" • V záznamoch truhlíc: ")+seen,body);
            foreach(var worn in carried.Where(i=>i.key==item.key && i.equipped)) GUILayout.Label(L("Nasadené • kvalita ")+worn.quality,muted);
            if(target>0)
            {
                int needed=supplies.Needed(carried,item.key,target);
                GUILayout.Label(needed==0 ? L("Cieľový počet máš pri sebe") : seen>0 ? L("Chýba pribaliť ")+needed+L(". Over zásoby v truhlách skôr, než vyrobíš ďalšie.") : L("Chýba získať alebo vyrobiť ")+needed+".",body);
            }
            foreach(var chest in sources.TryGetValue(item.key,out var locations) ? locations : new List<ChestSeen>())
            {
                int count=chest.items.Where(i=>i.key==item.key).Sum(i=>i.count);
                GUILayout.Label(L(chest.name)+": "+count+" • "+Age(chest.checked_at)+(nearbyIds.Contains(chest.id)?L(" • v dosahu"):L(" • starší záznam")),muted);
            }
            GUILayout.EndVertical();
        }
        if(items.Count==0) GUILayout.Label(L("Inventár je prázdny. Zásoby sa pridajú po otvorení truhlice."),body);
        GUILayout.EndScrollView();
    }
}
[HarmonyPatch(typeof(InventoryGui),"Show")]
internal static class SupplyOpenPatch
{
    private static void Postfix(Container __0) { try { Plugin.Instance?.OpenSupplyChest(__0); } catch(Exception) { } }
}
[HarmonyPatch(typeof(InventoryGui),"Hide")]
internal static class SupplyClosePatch
{
    private static void Prefix() { try { Plugin.Instance?.CloseSupplyChest(); } catch(Exception) { } }
}
