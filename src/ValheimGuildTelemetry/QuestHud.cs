using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimGuildTelemetry;

public partial class Plugin
{
    internal static bool JournalOpen;
    private QuestBridge quests;
    private ConfigEntry<bool> trackerEnabled, autoTrack;
    private QuestSnapshot lastQuestSnapshot;
    private ConfigEntry<string> pinSetting;
    private ConfigEntry<KeyCode> journalKey, trackerKey;
    private ConfigEntry<float> panelScale;
    private readonly HashSet<int> pinned=new HashSet<int>();
    private GUIStyle heading, body, muted, section, button, panel;
    private Texture2D backdrop;
    private Rect journalRect=new Rect(0,0,760,580);
    private Vector2 scroll;
    private string chapter="Všetky", toast;
    private bool showCompleted;
    private float toastUntil;
    private CursorLockMode previousLock;
    private bool previousCursor;

    private void ConfigureQuests()
    {
        var feedPath=Config.Bind("Server","QuestFeedPath",System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"guild-quests.json"),"Server-only quest projection exported by the Discord bot.");
        autoTrack=Config.Bind("Quests","AutoTrackNext",true,"Automatically track the next five unfinished goals from verified progress.");
        trackerEnabled=Config.Bind("Quests","ShowTracker",true,"Show tracked quests while in RavensOath.");
        pinSetting=Config.Bind("Quests","PinnedQuestIds","auto","Up to five tracked quest IDs; empty means none.");
        journalKey=Config.Bind("Quests","JournalKey",KeyCode.F8,"Open/close quest journal.");
        trackerKey=Config.Bind("Quests","TrackerKey",KeyCode.F9,"Show/hide quest tracker.");
        panelScale=Config.Bind("Quests","Scale",1f,"Quest UI scale, from 0.75 to 1.5.");
        foreach(var text in pinSetting.Value.Split(',')) if(int.TryParse(text,out var id) && pinned.Count<5) pinned.Add(id);
        quests=new QuestBridge(expectedUid.Value,feedPath.Value);
    }
    private bool CanShowQuests() => CorrectWorld() && !ZNet.instance.IsServer() && Player.m_localPlayer!=null;
    private void CloseJournal()
    {
        if(!JournalOpen) return;
        JournalOpen=false;Cursor.lockState=previousLock;Cursor.visible=previousCursor;
    }
    private void ToggleJournal()
    {
        if(JournalOpen) { CloseJournal();return; }
        previousLock=Cursor.lockState;previousCursor=Cursor.visible;JournalOpen=true;
        float scale=Scale();
        journalRect=new Rect(Math.Max(8,(Screen.width/scale-760)/2),Math.Max(8,(Screen.height/scale-580)/2),Math.Min(760,Screen.width/scale-16),Math.Min(580,Screen.height/scale-16));
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
    }
    private float Scale() => Mathf.Clamp(panelScale.Value,0.75f,1.5f)*Math.Min(1f,Screen.height/720f);
    private void LateUpdate()
    {
        if(quests==null) return;
        if(!CanShowQuests()) { CloseJournal();return; }
        if(JournalOpen) { Cursor.lockState=CursorLockMode.None;Cursor.visible=true; }
        var snapshot=quests.Client.Snapshot;
        if(snapshot!=null && snapshot!=lastQuestSnapshot)
        {
            lastQuestSnapshot=snapshot;
            var active=snapshot.quests.Where(q=>!q.completed).Select(q=>q.id).ToHashSet();
            bool changed=pinned.RemoveWhere(id=>!active.Contains(id))>0;
            if(autoTrack.Value)
            {
                var next=QuestTracker.Select(snapshot).ToHashSet();
                changed |= !pinned.SetEquals(next);
                pinned.Clear();pinned.UnionWith(next);
            }
            if(changed) SavePins();
        }
        if(quests.Client.Completions.Count>0 && Time.realtimeSinceStartup>=toastUntil)
        {
            toast=quests.Client.Completions.Dequeue();toastUntil=Time.realtimeSinceStartup+6;
        }
    }
    private void SavePins() { pinSetting.Value=string.Join(",",pinned.OrderBy(x=>x)); }
    private void Styles()
    {
        if(body!=null) return;
        backdrop=new Texture2D(1,1);backdrop.SetPixel(0,0,new Color(0.055f,0.06f,0.065f,0.94f));backdrop.Apply();
        body=new GUIStyle(GUI.skin.label) {fontSize=15,wordWrap=true,richText=false};body.normal.textColor=new Color(0.93f,0.90f,0.82f);
        heading=new GUIStyle(body) {fontSize=21,fontStyle=FontStyle.Bold};heading.normal.textColor=new Color(0.90f,0.70f,0.36f);
        section=new GUIStyle(heading) {fontSize=16};
        muted=new GUIStyle(body) {fontSize=12};muted.normal.textColor=new Color(0.70f,0.72f,0.70f);
        button=new GUIStyle(GUI.skin.button) {fontSize=13,richText=false,padding=new RectOffset(8,8,7,7)};
        panel=new GUIStyle(GUI.skin.box) {normal={background=backdrop},padding=new RectOffset(16,16,12,12)};
    }
    private string StatusText()
    {
        if(!string.IsNullOrEmpty(quests.Client.Status)) return quests.Client.Status;
        return quests.Client.Stale(Time.realtimeSinceStartup,DateTimeOffset.UtcNow.ToUnixTimeSeconds()) ? "Staršie údaje • čakám na synchronizáciu" : "Synchronizované s guildou";
    }
    private static string Progress(GuildQuest q) => q.completed ? "Dokončené" : q.automatic ? q.current+" / "+q.target : "Ručné potvrdenie v Discorde";
    private void OnGUI()
    {
        if(quests==null || !CanShowQuests()) return;
        Styles();
        var ev=Event.current;
        bool typing=(Chat.instance!=null && Chat.instance.HasFocus()) || global::Console.IsVisible() || TextInput.IsVisible();
        if(ev.type==EventType.KeyDown && !typing)
        {
            if(ev.keyCode==journalKey.Value) { ToggleJournal();ev.Use(); }
            else if(ev.keyCode==trackerKey.Value) { trackerEnabled.Value=!trackerEnabled.Value;ev.Use(); }
        }
        var previous=GUI.matrix;
        float scale=Scale();GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
        try
        {
            if(trackerEnabled.Value && !JournalOpen && !InventoryGui.IsVisible() && !Menu.IsVisible()) DrawTracker(Screen.width/scale);
            if(JournalOpen) journalRect=GUI.Window(732819,journalRect,DrawJournal,"",panel);
            if(toast!=null && Time.realtimeSinceStartup<toastUntil)
            {
                float width=Math.Min(560,Screen.width/scale-32);
                GUILayout.BeginArea(new Rect((Screen.width/scale-width)/2,80,width,100),panel);
                GUILayout.Label("QUEST DOKONČENÝ",section);GUILayout.Label(toast,body);GUILayout.EndArea();
            }
        }
        finally { GUI.matrix=previous; }
    }
    private void DrawTracker(float screenWidth)
    {
        float width=330, height=440;
        float y=Math.Min(245,Math.Max(80,Screen.height/Scale()-height-20));
        GUILayout.BeginArea(new Rect(screenWidth-width-18,y,width,height),panel);
        GUILayout.Label("RAVENSOATH",heading);
        GUILayout.Label(journalKey.Value+" denník   •   "+trackerKey.Value+" skryť",muted);
        GUILayout.Space(6);
        var snapshot=quests.Client.Snapshot;
        if(snapshot==null) GUILayout.Label(StatusText(),body);
        else
        {
            GUILayout.Label(snapshot.profile_name+" • "+snapshot.renown+" Renown",section);
            var selected=snapshot.quests.Where(q=>pinned.Contains(q.id) && !q.completed).Take(5).ToList();
            if(selected.Count==0) GUILayout.Label("Žiadne sledované questy. Otvor denník a pripni si cieľ.",body);
            foreach(var q in selected)
            {
                GUILayout.Space(8);GUILayout.Label(q.title,body);
                GUILayout.Label(Progress(q)+"   •   +"+q.xp+" Renown",muted);
            }
            GUILayout.FlexibleSpace();GUILayout.Label(StatusText(),muted);
        }
        GUILayout.EndArea();
    }
    private void DrawJournal(int id)
    {
        GUILayout.BeginHorizontal();GUILayout.Label("DENNÍK GUILDY",heading);
        if(GUILayout.Button("Zavrieť ["+journalKey.Value+"]",button,GUILayout.Width(145))) CloseJournal();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("Questy",button)) showSupplies=false;
        if(GUILayout.Button("Zásoby a výbava",button)) showSupplies=true;
        GUILayout.EndHorizontal();
        if(showSupplies) { DrawSupplies();GUI.DragWindow(new Rect(0,0,journalRect.width-160,40));return; }
        GUILayout.Label(StatusText(),muted);
        var snapshot=quests.Client.Snapshot;
        if(snapshot==null) { GUILayout.Label("Questy sa načítajú po prepojení herného účtu s Discordom.",body);return; }
        GUILayout.Label(snapshot.profile_name+" • "+snapshot.class_name+" • Level "+snapshot.level+" • "+snapshot.renown+" Renown",section);
        GUILayout.BeginHorizontal();
        if(GUILayout.Button(chapter=="Všetky" ? "Kapitola: všetky" : chapter,button))
        {
            var names=new List<string>{"Všetky"};names.AddRange(snapshot.quests.Select(q=>q.chapter).Distinct().OrderBy(x=>x));
            chapter=names[(names.IndexOf(chapter)+1)%names.Count];scroll=Vector2.zero;
        }
        if(GUILayout.Button(showCompleted?"Dokončené":"Aktívne",button,GUILayout.Width(110))) { showCompleted=!showCompleted;scroll=Vector2.zero; }
        if(GUILayout.Button(trackerEnabled.Value?"Skryť panel":"Zobraziť panel",button,GUILayout.Width(115))) trackerEnabled.Value=!trackerEnabled.Value;
        GUILayout.EndHorizontal();
        if(GUILayout.Button(autoTrack.Value ? "Automatický výber cieľov: zapnutý" : "Zapnúť automatický výber cieľov",button))
        { autoTrack.Value=!autoTrack.Value;lastQuestSnapshot=null; }
        var filtered=snapshot.quests.Where(q=>q.completed==showCompleted && (chapter=="Všetky" || q.chapter==chapter)).ToList();
        var existing=snapshot.quests.Select(q=>q.id).ToHashSet();
        pinned.RemoveWhere(x=>!existing.Contains(x));
        GUILayout.Label("Sledované: "+pinned.Count+"/5 • ručné úlohy potvrdíš cez /complete v Discorde",muted);
        scroll=GUILayout.BeginScrollView(scroll);
        foreach(var q in filtered)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();GUILayout.Label(q.title,section);
            bool selected=pinned.Contains(q.id);
            bool old=GUI.enabled;GUI.enabled=!q.completed && (selected || pinned.Count<5);
            if(GUILayout.Button(selected?"Odopnúť":"Sledovať",button,GUILayout.Width(95)))
            { autoTrack.Value=false;if(selected) pinned.Remove(q.id);else pinned.Add(q.id);SavePins(); }
            GUI.enabled=old;GUILayout.EndHorizontal();
            GUILayout.Label(q.chapter+" • "+q.owner+" • #"+q.id,muted);
            GUILayout.Label(Progress(q)+" • +"+q.xp+" Renown",body);
            if(!string.IsNullOrEmpty(q.assessment)) GUILayout.Label(q.assessment,muted);
            if(!string.IsNullOrEmpty(q.note)) GUILayout.Label(q.note,body);
            GUILayout.EndVertical();GUILayout.Space(5);
        }
        if(filtered.Count==0) GUILayout.Label("V tomto výbere nie sú žiadne questy.",body);
        GUILayout.EndScrollView();
        if(snapshot.truncated) GUILayout.Label("Ďalšie questy sú dostupné cez Discord /quests.",muted);
        GUI.DragWindow(new Rect(0,0,journalRect.width-160,40));
    }
}
[HarmonyPatch(typeof(Player),"TakeInput")]
internal static class JournalInputPatch
{
    private static void Postfix(ref bool __result) { if(Plugin.JournalOpen) __result=false; }
}
[HarmonyPatch(typeof(GameCamera),"UpdateCamera")]
internal static class JournalCameraPatch
{
    private static bool Prefix() => !Plugin.JournalOpen;
}
