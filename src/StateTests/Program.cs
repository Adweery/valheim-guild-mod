using System;
using ValheimGuildTelemetry;
var s=new ProgressState();var id=Guid.NewGuid().ToString("N");
void Check(bool value) { if(!value) throw new Exception("Assertion failed"); }
Check(s.Apply("steam1","Huso",id,1,"kill","$enemy_troll",1,100));
Check(!s.Apply("steam1","Huso",id,1,"kill","$enemy_troll",1,100));
Check(s.players[0].counters[0].count==1);
Check(s.Apply("steam1","Huso",id,2,"biome","Swamp",1,101));
Check(s.Apply("steam1","Huso",id,3,"biome","Swamp",1,102));
Check(s.players[0].counters[1].count==1);
Check(s.Apply("steam1","Huso",id,4,"craft","category:arrows",20,103));
Check(s.players[0].counters[2].count==20);
Check(!s.Apply("steam1","Huso",id,5,"craft","category:arrows",-1,104));
Check(!s.Apply("steam1","Huso",id,5,"unknown","x",1,104));
Console.WriteLine("10 telemetry state assertions passed.");

var restored=JsonCodec.Read(JsonCodec.Write(s));
if(restored.players[0].counters.Count!=s.players[0].counters.Count) throw new Exception("JSON roundtrip");
Console.WriteLine("JSON counters and sessions roundtrip passed");

Check(!WorldGuard.Matches(null,"1208923522"));
Check(!WorldGuard.Matches(new ZNet(),"1208923522"));
Check(WorldGuard.Matches(new ZNet {Name="RavensOath",Uid=1208923522},"1208923522"));
Check(!WorldGuard.Matches(new ZNet {Name="Other",Uid=99},"1208923522"));
Console.WriteLine("Handshake and world identity regression passed");

QuestSnapshot Make(long stamp, bool done=false) => new QuestSnapshot {uid="world",status="ok",generated_at=stamp,profile_name="Viking",quests=new System.Collections.Generic.List<GuildQuest>{new GuildQuest {id=1,title="Swamp",chapter="II",xp=25,completed=done}}};
var feed=new QuestFeedData {schema=1,uid="world",generated_at=1000,players=new System.Collections.Generic.List<QuestProfile>{new QuestProfile {account_id="private-id",profile_name="Viking",quests=Make(1000).quests}}};
Check(feed.Select("stranger","world",1001).status=="unlinked");
Check(feed.Select("private-id","other",1001).status=="unavailable");
Check(feed.Select("private-id","world",1200).status=="unavailable");
var wire=QuestJson.Write(feed.Select("private-id","world",1001));
Check(!wire.Contains("private-id") && !wire.Contains("account_id"));
var client=new QuestClientState();
Check(client.Accept(QuestJson.Read<QuestSnapshot>(wire),"world",0));
Check(client.Completions.Count==0);
Check(!client.Accept(Make(999),"world",1));
Check(!client.Accept(Make(1001),"other",1));
Check(client.Accept(Make(1001,true),"world",1));
Check(client.Completions.Count==1);
Check(client.Accept(Make(1001,true),"world",2));
Check(client.Completions.Count==1);
Check(!client.Stale(3,1002));Check(client.Stale(35,1002));
Check(!client.Accept(new QuestSnapshot {uid="world",status="unavailable"},"world",4));
Check(client.Snapshot!=null);
Check(client.Accept(new QuestSnapshot {uid="world",status="unlinked"},"world",5));
Check(client.Snapshot==null && client.Completions.Count==0);
Check(client.Accept(Make(1002,true),"world",6));Check(client.Completions.Count==0);
var malformed=Make(1003);malformed.quests.Add(malformed.quests[0]);
Check(!client.Accept(malformed,"world",7));
Console.WriteLine("Quest privacy, freshness, cache, validation and completion replay checks passed.");
var tracking=Make(2000);
tracking.quests.Clear();
for(int i=1;i<=8;i++) tracking.quests.Add(new GuildQuest {id=i,title="Quest "+i,chapter=i<4?"Chapter I - A New World":"Chapter II - Into the Swamp"});
Check(QuestTracker.Select(tracking).Count==5);
Check(QuestTracker.Select(tracking)[0]==1);
tracking.quests[3].completed=true;
Check(QuestTracker.Select(tracking)[0]==5);
tracking.quests[0].automatic=true;tracking.quests[0].current=40;tracking.quests[0].target=100;
Check(QuestTracker.Select(tracking)[0]==1);
tracking.quests[0].completed=true;
Check(!QuestTracker.Select(tracking).Contains(1));
Check(QuestTracker.Select(tracking).Count==5);
foreach(var q in tracking.quests) q.completed=true;
Check(QuestTracker.Select(tracking).Count==0);
Console.WriteLine("Automatic tracker: partial progress, chapter advancement, refill and all-completed passed.");
var stock=new SupplyCache {identity="world:character"};
SupplyItem Potion(int count) => new SupplyItem {key="potion",name="Potion",count=count,quality=1};
stock.Observe(new ChestSeen {id="chest1",name="Chest",checked_at=1000,items=new System.Collections.Generic.List<SupplyItem>{Potion(8)}});
var pack=new System.Collections.Generic.List<SupplyItem>();
Check(stock.LastSeen("potion")==8);
Check(stock.Needed(pack,"potion",2)==2); // Stored items cannot claim readiness.
stock.Observe(new ChestSeen {id="chest1",name="Chest",checked_at=1001,items=new System.Collections.Generic.List<SupplyItem>{Potion(6)}});
pack.Add(Potion(2));
Check(stock.chests.Count==1 && stock.LastSeen("potion")==6 && stock.Needed(pack,"potion",2)==0);
stock.SetTarget("potion",2,"Potion");
var frozen=stock.Copy();
stock.SetTarget("potion",5,"Potion");
Check(frozen.targets[0].count==2); // Background writer gets an immutable projection.
stock.Observe(new ChestSeen {id="chest1",name="Chest",checked_at=1002});
pack.Clear();Check(stock.LastSeen("potion")==0 && stock.Needed(pack,"potion",2)==2);
Check(stock.targets[0].count==5); // Target survives empty storage and inventory.
var persisted=QuestJson.Read<SupplyCache>(QuestJson.Write(stock));
Check(persisted.Valid("world:character",1003));
Check(!persisted.Valid("other:character",1003));
Check(!persisted.Valid("world:other-character",1003));
persisted.chests[0].checked_at=999999;Check(!persisted.Valid("world:character",1003));
Check(!SupplyCache.ValidItems(new System.Collections.Generic.List<SupplyItem>{Potion(-1)}));
for(int i=0;i<110;i++) stock.Observe(new ChestSeen {id="c"+i,name="Chest",checked_at=1100+i});
Check(stock.chests.Count==100);
Check(stock.Valid("world:character",1300));
Console.WriteLine("Supply scan: transfers, empty chests, no false readiness, cache identity, persistence and limits passed.");

Check(JournalInputPolicy.Decide("J","J",false,false,false,false)==JournalAction.Open);
Check(JournalInputPolicy.Decide("J","J",true,false,false,false)==JournalAction.Close);
Check(JournalInputPolicy.Decide("Escape","J",true,false,true,false)==JournalAction.Close);
Check(JournalInputPolicy.Decide("Escape","J",false,false,false,false)==JournalAction.None);
Check(JournalInputPolicy.Decide("J","J",false,true,false,false)==JournalAction.None);
Check(JournalInputPolicy.Decide("J","J",true,false,true,false)==JournalAction.None);
Check(JournalInputPolicy.Decide("J","J",false,false,false,true)==JournalAction.None);
Check(JournalInputPolicy.Decide("F8","K",true,false,true,false)==JournalAction.Close);
Check(JournalInputPolicy.Decide("K","K",false,false,false,false)==JournalAction.Open);
Console.WriteLine("Journal input: J, Escape, F8 fallback, chat/search guards and custom keys passed.");

// Reject missing localization and wrong-language fallback, including a language change.
var textType=typeof(QuestSnapshot).Assembly.GetType("ValheimGuildTelemetry.GuildText");
if(textType==null) throw new Exception("Language-aware journal text is missing");
var translate=(Func<string,string,string>)Delegate.CreateDelegate(typeof(Func<string,string,string>),textType.GetMethod("Translate"));
foreach(var lang in new[]{"English","en","German","Czech","",null})
    if(translate("DENNÍK GUILDY",lang)!="GUILD JOURNAL") throw new Exception("English fallback failed: "+lang);
foreach(var lang in new[]{"Slovak","sk","SK","Slovenčina","sk-SK"})
    if(translate("DENNÍK GUILDY",lang)!="DENNÍK GUILDY") throw new Exception("Slovak selection failed: "+lang);
Check(translate("Zabiť Eldera","English")=="Defeat The Elder");
Check(translate("Zabiť Eldera","Slovak")=="Zabiť Eldera");
Check(translate("Zabiť Eldera  •  +50 Renown","French")=="Defeat The Elder  •  +50 Renown");
Check(translate("Truhla (10, -5)","English")=="Chest (10, -5)");
Check(translate("Custom quest by a player","English")=="Custom quest by a player");
Check(translate(null,"English")==null);
Console.WriteLine("Language selection, fallback, live switching and legacy text localization passed.");
Check(translate("Chapter II - Into the Swamp","Slovak")=="Kapitola II - Do močiarov");
Check(translate("Zásoby a výbava","Slovenian")=="Supplies and equipment");
Check(translate("Tím","en-US")=="Team");
Check(translate("Tomáš","English")=="Tomáš");
Check(translate("pred ","English")+5+translate(" min","English")=="5 min ago");
Check(translate("pred ","Slovak")+5+translate(" min","Slovak")=="pred 5 min");
Check(translate("Prepoj účet cez /linkgame v Discorde","English")=="Link your account with /linkgame in Discord");
Check(translate("Priprav funkčný návratový portál","English")=="Prepare a working return portal");
Check(translate("Priprav Poison resistance mead a rozdeľ aspoň 2 hotové dávky každému. Nestačí mead base; musí prejsť fermentáciou. Vypiť ešte pred otrávením, nie až ako liek na existujúci jed.","English").StartsWith("Prepare Poison resistance mead and give each player at least 2 finished doses."));
var beforeLanguageChange=QuestJson.Write(client.Snapshot);
foreach(var quest in client.Snapshot.quests) { translate(quest.title,"English");translate(quest.title,"Slovak"); }
Check(QuestJson.Write(client.Snapshot)==beforeLanguageChange);
Console.WriteLine("Quest descriptions, chapter translation, identity and progress preservation passed.");
