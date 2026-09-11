using System;
using System.Collections.Generic;
using System.Reflection;
namespace BepInEx.Configuration
{
    public class ConfigEntry<T> { public T Value; }
    public class ConfigFile { public ConfigEntry<T> Bind<T>(string a,string b,T value,string description)=>new ConfigEntry<T>{Value=value}; }
}
namespace HarmonyLib
{
    public class HarmonyPatch:Attribute { public HarmonyPatch(Type t,string method){} }
    public static class AccessTools
    {
        public static MethodInfo Method(Type t,string name)=>t.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
        public static FieldInfo Field(Type t,string name)=>t.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
    }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x;
        public float sqrMagnitude=>x*x;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3{x=a.x-b.x};
    }
    public class Transform { public Vector3 position; }
    public class GameObject { public bool activeInHierarchy=true; }
    public static class Mathf { public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b); }
}
public class Player { public static Player m_localPlayer=new Player(); public UnityEngine.Transform transform=new UnityEngine.Transform();public long GetPlayerID()=>1; }
public class ZDO { public string m_uid; }
public class ZNetView { public ZDO data=new ZDO(); public bool IsValid()=>true;public ZDO GetZDO()=>data; }
public static class PrivateArea { public static bool Allowed=true;public static bool CheckAccess(UnityEngine.Vector3 p,float r,bool flash,bool ward)=>Allowed; }
public class Container
{
    public UnityEngine.GameObject gameObject=new UnityEngine.GameObject();
    public UnityEngine.Transform transform=new UnityEngine.Transform();
    public bool m_checkGuardStone=true, Accessible=true;
    public int Latest=8,Current,LoadCalls;
    public ZNetView view=new ZNetView();
    private bool CheckAccess(long player)=>Accessible;
    private void Load(){Current=Latest;LoadCalls++;}
    public T GetComponent<T>() where T:class=>view as T;
}
namespace ValheimGuildTelemetry
{
    public partial class Plugin
    {
        public static Plugin Instance;
        public BepInEx.Configuration.ConfigFile Config=new BepInEx.Configuration.ConfigFile();
        public SupplyCache supplies=new SupplyCache();
        public bool supplyDirty;
        public string supplyWarning;
        public float nextSupplyScan;
        private bool CanShowQuests()=>true;
        private void ObserveChest(Container c)=>supplies.Observe(new ChestSeen{id=c.view.data.m_uid,name="Chest",checked_at=1,items=c.Current>0?new List<SupplyItem>{new SupplyItem{key="potion",name="Potion",count=c.Current}}:new List<SupplyItem>()});
        public void Init()=>ConfigureNearbySupplies();
        public void Scan()=>ScanNearbySupplies();
    }
}
