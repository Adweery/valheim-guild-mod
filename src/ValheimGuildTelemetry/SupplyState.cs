using System;
using System.Collections.Generic;
using System.Linq;
namespace ValheimGuildTelemetry;

[Serializable] public class SupplyItem
{
    public string key, name;
    public int count, quality;
    public bool equipped;
}
[Serializable] public class ChestSeen
{
    public string id, name;
    public long checked_at;
    public List<SupplyItem> items=new List<SupplyItem>();
}
[Serializable] public class SupplyTarget { public string key, name; public int count; }
[Serializable] public class SupplyCache
{
    public int schema=1;
    public string identity;
    public List<ChestSeen> chests=new List<ChestSeen>();
    public List<SupplyTarget> targets=new List<SupplyTarget>();
    public bool Valid(string expected,long now)
    {
        return schema==1 && identity==expected && chests!=null && chests.Count<=100 && targets!=null && targets.Count<=512 &&
            chests.All(c=>c!=null && !string.IsNullOrEmpty(c.id) && c.id.Length<=120 && c.name!=null && c.name.Length<=160 && c.checked_at>0 && c.checked_at<=now+120 && ValidItems(c.items)) &&
            chests.Sum(c=>c.items.Count)<=4000 &&
            chests.Select(c=>c.id).Distinct().Count()==chests.Count &&
            targets.All(t=>t!=null && !string.IsNullOrEmpty(t.key) && t.key.Length<=160 && (t.name==null || t.name.Length<=160) && t.count>=1 && t.count<=9999) &&
            targets.Select(t=>t.key).Distinct().Count()==targets.Count;
    }
    public static bool ValidItems(List<SupplyItem> items) => items!=null && items.Count<=512 && items.All(i=>i!=null && !string.IsNullOrEmpty(i.key) && i.key.Length<=160 && i.name!=null && i.name.Length<=160 && i.count>=1 && i.count<=100000 && i.quality>=0 && i.quality<=100);
    public void Observe(ChestSeen chest)
    {
        if(chest==null || !ValidItems(chest.items)) return;
        chests.RemoveAll(c=>c.id==chest.id);
        chests.Add(chest);
        chests=chests.OrderByDescending(c=>c.checked_at).Take(100).ToList();
        while(chests.Sum(c=>c.items.Count)>4000) chests.RemoveAt(chests.Count-1);
    }
    public int Carried(IEnumerable<SupplyItem> inventory,string key) => inventory.Where(i=>i.key==key).Sum(i=>i.count);
    public int LastSeen(string key) => chests.Sum(c=>c.items.Where(i=>i.key==key).Sum(i=>i.count));
    public int Needed(IEnumerable<SupplyItem> inventory,string key,int target) => Math.Max(0,target-Carried(inventory,key));
    public void SetTarget(string key,int count,string name=null)
    {
        name=name ?? targets.FirstOrDefault(t=>t.key==key)?.name ?? key;
        targets.RemoveAll(t=>t.key==key);
        if(count>0 && targets.Count<512) targets.Add(new SupplyTarget {key=key,name=name,count=Math.Min(9999,count)});
    }
    public SupplyCache Copy() => new SupplyCache {identity=identity,chests=new List<ChestSeen>(chests),targets=targets.Select(t=>new SupplyTarget {key=t.key,name=t.name,count=t.count}).ToList()};
}
