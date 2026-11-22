using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using Landfall.TABS;
using Landfall.TABS.GameMode;
using Landfall.TABS.GameState;
using UnityEngine;

[BepInPlugin("ptv.tabs.tiktok", "PTV TikTok Arena", "1.4.1")]
public sealed class TabsArena : BaseUnityPlugin
{
    public static TabsArena I;
    public PTVTikTokLive Live;
    public TabsGiftDirector Director;
    public ArenaSettings Settings;
    public bool ArenaEnabled = true;
    Harmony harmony;
    public static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
    public static string ConfigPath { get { return Path.Combine(Root, "TikTokArena.json"); } }
    void Awake()
    {
        I = this;
        TabsCardFont.Prepare();
        ReloadSettings();
        Director = gameObject.AddComponent<TabsGiftDirector>();
        Live = gameObject.AddComponent<PTVTikTokLive>();
        Live.TestMode = Environment.GetCommandLineArgs().Contains("--tiktok-test");
        gameObject.AddComponent<TabsArenaUI>();
        gameObject.AddComponent<TabsRoundFlow>();
        gameObject.AddComponent<TabsGiftSettingsUI>();
        harmony = new Harmony("ptv.tabs.tiktok");
        harmony.Patch(AccessTools.Method(typeof(BaseGameMode), "DecideWinner"), new HarmonyMethod(typeof(TabsArena), "AllowWinner"));
        harmony.Patch(AccessTools.Method(typeof(PlacementUI), "SideHasUnits"), postfix:new HarmonyMethod(typeof(TabsArena), "HasArenaUnits"));
        harmony.Patch(AccessTools.Method(typeof(PlacementUI), "RedrawFactionUnits"), postfix:new HarmonyMethod(typeof(TabsArena), "FactionChanged"));
        Logger.LogInfo("PTV TikTok Arena loaded; PVZ bridge; F8 opens controls.");
    }
    public void ReloadSettings()
    {
        try { Settings = File.Exists(ConfigPath) ? JsonConvert.DeserializeObject<ArenaSettings>(File.ReadAllText(ConfigPath)) : new ArenaSettings(); }
        catch { Settings = new ArenaSettings(); }
        if (Settings == null || Settings.giftRules == null || Settings.giftRules.Length < 7) Settings = new ArenaSettings();
        if(Settings.giftRules.Length!=7)Settings.giftRules=Settings.giftRules.Take(7).ToArray();
        if(Settings.giftSettingsVersion<1 || Settings.giftRules.Any(r=>r==null||string.IsNullOrEmpty(r.gift))) {
            string[] gifts={"Rose","Finger Heart","Doughnut","Cap","Paper Crane","TikTok","Ice Cream Cone","GG","Hand Hearts","Galaxy"};
            for(int i=0;i<7;i++){if(string.IsNullOrEmpty(Settings.giftRules[i].gift)){Settings.giftRules[i].gift=gifts[i];Settings.giftRules[i].triggerGift=true;}Settings.giftRules[i].likesRequired=Math.Max(1,Settings.giftRules[i].likesRequired);}
            Settings.giftSettingsVersion=1;Settings.avatarSize=32;
            File.WriteAllText(ConfigPath,JsonConvert.SerializeObject(Settings,Formatting.Indented));
        }
        Settings.avatarSize=Mathf.Clamp(Settings.avatarSize,24,32);
        Settings.maxAlivePerTeam = Mathf.Clamp(Settings.maxAlivePerTeam, 20, 1000);
        Settings.spawnIntervalSeconds = Mathf.Clamp(Settings.spawnIntervalSeconds, .05f, 5f);
        if (!File.Exists(ConfigPath)) File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(Settings,Formatting.Indented));
    }
    public static BaseGameMode Mode
    {
        get { var s = ServiceLocator.GetService<GameModeService>(); return s ? s.CurrentGameMode : null; }
    }
    static void FactionChanged(Faction __0){if(I&&I.Director&&__0)I.Director.UseFaction(__0);}
    static bool AllowWinner(BaseGameMode __instance)
    { return !(I && I.ArenaEnabled && __instance is SandboxGameMode); }
    static void HasArenaUnits(Landfall.TABS.Team __0,ref bool __result)
    { if(I && I.ArenaEnabled && Mode is SandboxGameMode) __result |= I.Director.Alive((int)__0)>0; }
    void OnDestroy() { if (harmony != null) harmony.UnpatchSelf(); if (I == this) I = null; }
}

[Serializable] public sealed class HeroRule
{
    public int slot;
    public string unit;
    public string[] giftIds = new string[0];
    public int minCoins, maxCoins;
    public string gift;
    public bool triggerGift=true, triggerLikes, triggerFollow, triggerHeart;
    public int likesRequired=100;
    public HeroRule(int n, string name, int min, int max) { slot=n; unit=name; minCoins=min; maxCoins=max; }
}
[Serializable] public sealed class ArenaSettings
{
    public string username="plan.b4383";
    public int maxAlivePerTeam=250;
    public float spawnIntervalSeconds=.25f;
    public int avatarSize=32;
    public int giftSettingsVersion;
    public HeroRule[] giftRules = {
        new HeroRule(1,"Clubber",1,1),new HeroRule(2,"Protector",2,4),
        new HeroRule(3,"Spear Thrower",5,9),new HeroRule(4,"Stoner",10,19),
        new HeroRule(5,"Bone Mage",20,49),new HeroRule(6,"Chieftain",50,99),
        new HeroRule(7,"Mammoth",100,199)
    };
}

public sealed class TabsGiftDirector : MonoBehaviour
{
    sealed class Batch
    { public PTVTikTokLive.Message sender; public int team, slot, count; }
    [Serializable] sealed class SavedTeams { public List<TeamEntry> entries=new List<TeamEntry>(); }
    [Serializable] sealed class TeamEntry { public string id; public int team; }
    readonly Dictionary<string,int> teams=new Dictionary<string,int>();
    readonly Dictionary<string,List<Batch>> awaiting=new Dictionary<string,List<Batch>>();
    readonly Queue<Batch>[] queues = {new Queue<Batch>(),new Queue<Batch>()};
    readonly List<Unit>[] spawned = {new List<Unit>(),new List<Unit>()};
    UnitBlueprint[] blueprints;
    public Faction SelectedFaction {get;private set;}
    public int RosterVersion {get;private set;}
    public UnitBlueprint Hero(int slot){return blueprints!=null&&slot>=0&&slot<blueprints.Length?blueprints[slot]:null;}
    public string FactionName {get{return SelectedFaction?DisplayName(SelectedFaction.Entity.Name):"Tribal";}}
    public static string DisplayName(string value){
        try{string localized=Localizer.GetSinglePhrase(value,new string[0]);if(!string.IsNullOrEmpty(localized)&&localized!=value)return localized;}catch{}
        string s=(value??"").Replace("UNIT_","").Replace("FACTION_","").Replace("LABEL_","").Replace("_"," ");
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
    }
    public void UseFaction(Faction faction){
        if(!faction)return;
        var units=faction.GetRearrangedUnits().Where(x=>x).Take(7).ToArray();
        if(units.Length!=7){Hint="เลือกชุดที่มีตัวละครอย่างน้อย 7 ตัว";return;}
        if(SelectedFaction==faction&&blueprints!=null)return;
        SelectedFaction=faction;blueprints=units;RosterVersion++;
        for(int i=0;i<7;i++)TabsArena.I.Settings.giftRules[i].unit=DisplayName(units[i].Entity.Name);
        File.WriteAllText(TabsArena.ConfigPath,JsonConvert.SerializeObject(TabsArena.I.Settings,Formatting.Indented));
        Hint="เลือกชุด "+FactionName+" · 7 ตัว ใช้เหมือนกันทั้งสองฝั่ง";
    }
    float nextSpawn, nextReport;
    int turn;
    readonly Dictionary<string,int[]> likeProgress=new Dictionary<string,int[]>();
    readonly HashSet<string> followed=new HashSet<string>();
    public int TotalSpawned { get; private set; }
    public int ProfileCount { get { return FindObjectsOfType<TabsNameplate>().Length; } }
    public string LastLabel { get; private set; }
    public string Hint="เลือก Sandbox แล้วเปิดแผนที่ก่อนทดสอบ";
    public int WaitingForTeam { get { return awaiting.Values.Sum(x=>x.Sum(y=>y.count)); } }
    public int Pending(int team) { return queues[team].Sum(x=>x.count); }
    public int Alive(int team) { if(!CanSpawn)return 0;if(TabsRoundFlow.I)return TabsRoundFlow.I.Count(team); spawned[team].RemoveAll(u=>!u || u.dead); return spawned[team].Count; }
    string TeamPath { get { return Path.Combine(TabsArena.Root,"TikTokTeams.json"); } }
    string Key(PTVTikTokLive.Message m) { return string.IsNullOrEmpty(m.userId) ? m.uniqueId : m.userId; }
    void Start()
    {
        try { if (File.Exists(TeamPath)) foreach(var e in JsonConvert.DeserializeObject<SavedTeams>(File.ReadAllText(TeamPath)).entries) if(e.team==0||e.team==1) teams[e.id]=e.team; } catch { }
    }
    void SaveTeams()
    {
        var saved=new SavedTeams(); foreach(var p in teams) saved.entries.Add(new TeamEntry{id=p.Key,team=p.Value});
        File.WriteAllText(TeamPath,JsonConvert.SerializeObject(saved,Formatting.Indented));
    }
    public void SelectTeam(PTVTikTokLive.Message m)
    {
        string text=(m.comment??"").Trim(); if(text!="1"&&text!="2") return;
        string key=Key(m); if(string.IsNullOrEmpty(key)) return;
        int team=text=="1"?0:1; teams[key]=team; SaveTeams();
        Hint=m.DisplayName+" เลือกฝั่ง "+text;
        List<Batch> waiting;
        if(awaiting.TryGetValue(key,out waiting)) {
            foreach(var b in waiting) { b.team=team; queues[team].Enqueue(b); }
            awaiting.Remove(key);
        }
    }
    public int Enqueue(PTVTikTokLive.Message m)
    {
        if(m.@event=="gift") { AssignGiftTeam(m);StartCoroutine(TabsGiftCatalog.Match(m,(matched)=>ApplyTriggers(m,matched))); return 1; }
        return ApplyTriggers(m,null);
    }
    int ApplyTriggers(PTVTikTokLive.Message m,string matched)
    {
        string key=Key(m); if(string.IsNullOrEmpty(key))return 0;
        if(m.@event=="follow"&&!followed.Add(key))return 0;
        var rules=TabsArena.I.Settings.giftRules;int total=0;
        for(int slot=0;slot<rules.Length;slot++) {
            var rule=rules[slot];int count=0;
            if(m.@event=="gift") {
                bool id=rule.giftIds!=null&&rule.giftIds.Contains(m.giftId);
                bool gift=TabsGiftCatalog.Key(rule.gift)==TabsGiftCatalog.Key(matched??m.giftName);
                bool heart=TabsGiftCatalog.Key(matched??m.giftName)==TabsGiftCatalog.Key("Heart Me");
                if((rule.triggerGift&&(id||gift))||(rule.triggerHeart&&heart))count=Math.Max(1,m.repeatCount);
            }
            if(m.@event=="follow"&&rule.triggerFollow)count=1;
            if(m.@event=="like"&&rule.triggerLikes&&m.likeCount>0) {
                int[] progress;if(!likeProgress.TryGetValue(key,out progress))likeProgress[key]=progress=new int[7];
                long sum=(long)progress[slot]+m.likeCount;int threshold=Math.Max(1,rule.likesRequired);
                count=(int)(sum/threshold);progress[slot]=(int)(sum%threshold);
            }
            if(count>0) { QueueReward(m,slot,count);total+=count; }
        }
        return total;
    }
    void AssignGiftTeam(PTVTikTokLive.Message m)
    {
        string key=Key(m);int team;if(string.IsNullOrEmpty(key))return;
        if(!teams.TryGetValue(key,out team)) {
            team=UnityEngine.Random.Range(0,2);teams[key]=team;SaveTeams();
            List<Batch> waiting;
            if(awaiting.TryGetValue(key,out waiting)){
                foreach(var pending in waiting){pending.team=team;queues[team].Enqueue(pending);}
                awaiting.Remove(key);
            }
            Hint=m.DisplayName+" สุ่มเข้าฝั่ง "+(team+1)+" · พิมพ์ 1 หรือ 2 เพื่อเปลี่ยนฝั่ง";
        }
    }
    void QueueReward(PTVTikTokLive.Message m,int slot,int count)
    {
        string key=Key(m);int team;var rules=TabsArena.I.Settings.giftRules;
        var batch=new Batch{sender=m,slot=slot,count=count}; LastLabel=rules[slot].unit;
        if(teams.TryGetValue(key,out team)) { batch.team=team; queues[team].Enqueue(batch); }
        else {
            List<Batch> list; if(!awaiting.TryGetValue(key,out list)) awaiting[key]=list=new List<Batch>(); list.Add(batch);
            Hint=m.DisplayName+": รอพิมพ์ 1 หรือ 2 ของขวัญถูกเก็บรอไว้";
        }
    }
    public void ReloadHeroes() { blueprints=null; TabsArena.I.ReloadSettings(); }
    public void SpawnManual(int slot,int team)
    {
        if(slot<0||slot>=7||team<0||team>1)return;
        if(!CanSpawn) { Hint="เปิดแผนที่ Sandbox ก่อนลงตัวละคร";return; }
        var sender=new PTVTikTokLive.Message { userId="manual-"+team,uniqueId="manual-"+team,nickname="ทดสอบฝั่ง "+(team+1) };
        queues[team].Enqueue(new Batch{sender=sender,team=team,slot=slot,count=1});
        nextSpawn=0;
        Hint="ลง "+TabsArena.I.Settings.giftRules[slot].unit+" ฝั่ง "+(team+1);
    }
    bool ResolveHeroes()
    {
        if(blueprints!=null) return true;
        if(PlacementUI.Instance){var f=AccessTools.Field(typeof(PlacementUI),"m_selectedFaction").GetValue(PlacementUI.Instance) as Faction;if(f){UseFaction(f);if(blueprints!=null)return true;}}
        var all=Resources.FindObjectsOfTypeAll<UnitBlueprint>();
        if(all.Length==0) return false;
        File.WriteAllLines(Path.Combine(TabsArena.Root,"TikTokAvailableUnits.txt"),all.Where(x=>x).Select(x=>x.name+" | "+x.Entity.Name).OrderBy(x=>x).ToArray());
        var result=new UnitBlueprint[7];
        for(int i=0;i<7;i++) {
            string wanted=TabsArena.I.Settings.giftRules[i].unit;
            if(wanted=="Clubber")wanted="Club";
            if(wanted=="Viking")wanted="Brawler";
            if(wanted=="King")wanted="TheKing";
            result[i]=all.FirstOrDefault(x=>string.Equals(x.name,wanted,StringComparison.OrdinalIgnoreCase)||string.Equals(x.Entity.Name,wanted,StringComparison.OrdinalIgnoreCase)||string.Equals(x.Entity.Name,"UNIT_"+wanted.ToUpperInvariant(),StringComparison.OrdinalIgnoreCase));
            if(!result[i]) { Hint="ไม่พบยูนิต "+wanted+" ดูรายชื่อใน TikTokAvailableUnits.txt"; return false; }
        }
        blueprints=result; return true;
    }
    public bool CanSpawn { get { return TabsArena.Mode is SandboxGameMode && MapSettings.Instance && PlacementUI.Instance; } }
    void Update()
    {
        if(Time.unscaledTime>=nextReport) { nextReport=Time.unscaledTime+2; Report(); }
        if(!TabsArena.I.ArenaEnabled||!CanSpawn||Time.unscaledTime<nextSpawn||(TabsRoundFlow.I&&TabsRoundFlow.I.BetweenRounds)) return;
        if(!ResolveHeroes()) { nextSpawn=Time.unscaledTime+3; return; }
        for(int n=0;n<2;n++) {
            int team=(turn+n)%2;
            if(queues[team].Count==0 || Alive(team)>=TabsArena.I.Settings.maxAlivePerTeam) continue;
            var batch=queues[team].Peek();
            try {
                Unit unit=Spawn(batch);
                if(!unit) { nextSpawn=Time.unscaledTime+1; return; }
                spawned[team].Add(unit); TotalSpawned++;
                if(--batch.count<=0) queues[team].Dequeue();
                Hint=batch.sender.DisplayName+" → "+TabsArena.I.Settings.giftRules[batch.slot].unit+" / ฝั่ง "+(team+1);
                turn=1-team; nextSpawn=Time.unscaledTime+TabsArena.I.Settings.spawnIntervalSeconds;
            } catch(Exception e) { Hint="เกิดตัวละครไม่สำเร็จ: "+e.Message; nextSpawn=Time.unscaledTime+3; Debug.LogError("TikTok spawn: "+e); }
            return;
        }
    }
    Unit Spawn(Batch b)
    {
        var map=MapSettings.Instance;
        Vector3 center=map.m_mapCenter;
        float radius=Mathf.Clamp(map.m_mapRadius*.3f,6,18);
        Vector3 point=new Vector3(map.teamBorder+(b.team==0?-1:1)*UnityEngine.Random.Range(4,radius),center.y+80,center.z+UnityEngine.Random.Range(-radius*.6f,radius*.6f));
        RaycastHit hit;
        if(!Physics.Raycast(point,Vector3.down,out hit,180,~0,QueryTriggerInteraction.Ignore)) { Hint="หาพื้นที่เกิดไม่เจอ รอแผนที่โหลด"; return null; }
        point=hit.point+Vector3.up*.2f;
        Unit unit;
        blueprints[b.slot].Spawn(point,Quaternion.Euler(0,b.team==0?90:270,0),(Landfall.TABS.Team)b.team,out unit,1,false,false,0,null);
        if(!unit) return null;
        unit.gameObject.AddComponent<TabsNameplate>().Initialize(unit,b.sender,b.team);
        return unit;
    }
    public void StartBattle()
    {
        if(!CanSpawn) { Hint="เปิดแผนที่ Sandbox ก่อน"; return; }
        TabsArena.Mode.MatchOver=false;
        var state=ServiceLocator.GetService<GameStateManager>();
        if(state.GameState!=GameState.BattleState)state.EnterBattleState();
    }
    public void ClearArena()
    {
        if(TabsRoundFlow.I)TabsRoundFlow.I.ResetForClear();
        foreach(var list in spawned) { foreach(var u in list) if(u&&!u.IsInPool) u.DestroyUnit(); list.Clear(); }
        foreach(var q in queues) q.Clear(); awaiting.Clear(); Hint="ล้างสนามและคิวแล้ว";
    }
    void Report()
    {
        try {
            File.WriteAllText(Path.Combine(TabsArena.Root,"TikTokArena.status.json"),
                "{\"spawned\":"+TotalSpawned+",\"redAlive\":"+Alive(0)+",\"blueAlive\":"+Alive(1)+",\"redPending\":"+Pending(0)+",\"bluePending\":"+Pending(1)+",\"waitingForTeam\":"+WaitingForTeam+",\"profiles\":"+ProfileCount+",\"sandbox\":"+(CanSpawn?"true":"false")+",\"round\":"+(TabsRoundFlow.I?TabsRoundFlow.I.Round:0)+",\"countdown\":"+(TabsRoundFlow.I?TabsRoundFlow.I.Countdown:-1)+",\"redWins\":"+(TabsRoundFlow.I?TabsRoundFlow.I.RedWins:0)+",\"blueWins\":"+(TabsRoundFlow.I?TabsRoundFlow.I.BlueWins:0)+"}");
        } catch { }
    }
}


