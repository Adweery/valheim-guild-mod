using System;
using ValheimGuildTelemetry;
void Check(bool value,string why){if(!value)throw new Exception(why);}
var p=new Plugin();p.Init();
Container Chest(string id,float distance){var c=new Container();c.view.data.m_uid=id;c.transform.position=new UnityEngine.Vector3{x=distance};Plugin.LoadedChests.Add(c);return c;}
var near=Chest("near",10);var far=Chest("far",40);var locked=Chest("locked",5);locked.Accessible=false;
p.Scan();Check(near.LoadCalls==1 && far.LoadCalls==0 && locked.LoadCalls==0,"radius and permissions");
Check(p.supplies.LastSeen("potion")==8,"replicated contents loaded without opening");
near.Latest=3;p.Scan();Check(p.supplies.LastSeen("potion")==3,"other player removed items");
near.Latest=12;p.Scan();Check(p.supplies.LastSeen("potion")==12,"other player added items");
near.Latest=0;p.Scan();Check(p.supplies.LastSeen("potion")==0,"emptied chest clears old stock");
near.Latest=9;p.Scan();near.Accessible=false;p.Scan();Check(p.supplies.chests.Count==0,"revoked privacy removes cached contents");
near.Accessible=true;p.Scan();PrivateArea.Allowed=false;p.Scan();Check(p.supplies.chests.Count==0,"ward revocation removes cached contents");
PrivateArea.Allowed=true;p.Scan();p.ForgetDestroyedChest(near);Check(p.supplies.chests.Count==0,"destroyed chest removed");
Plugin.LoadedChests.Clear();
for(int i=0;i<80;i++) Chest("batch"+i,10);
p.Scan();p.Scan();p.Scan();Check(p.supplies.chests.Count==80,"bounded batches eventually refresh every nearby chest");
Console.WriteLine("Nearby scanner integration passed: radius, access, wards, replicated changes, empty/destroyed chests and batch coverage.");
