using Newtonsoft.Json;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Landfall.TABS;
using Landfall.TABS.GameState;

public sealed class TabsRoundFlow : MonoBehaviour
{
    public static TabsRoundFlow I;
    public bool BetweenRounds { get;private set; }
    public int Countdown { get;private set; } = -1;
    public int RedWins,BlueWins,Round=1;
    int[] alive=new int[2];float nextScan,nextTick,nextHide;bool wasBattle,armed;int losing=-1,phase;
    Canvas hud;Text red,blue,banner,redWinsLabel,blueWinsLabel;GameObject redBox,blueBox,battleButton;
    [Serializable] class Score { public int red,blue,round=1; }
    string ScorePath { get { return Path.Combine(TabsArena.Root,"TikTokArenaScore.json"); } }
    void Awake(){I=this;try{if(File.Exists(ScorePath)){var s=JsonConvert.DeserializeObject<Score>(File.ReadAllText(ScorePath));RedWins=s.red;BlueWins=s.blue;Round=s.round;}}catch{}}
    void Start() {
        hud=ArenaUI.Canvas("TikTok Battle Score",150);
        hud.gameObject.AddComponent<GraphicRaycaster>();
        var r=WoodlandAssets.Art(hud.transform,"red-banner",new Vector2(-194,383),new Vector2(342,104));redBox=r.gameObject;
        var b=WoodlandAssets.Art(hud.transform,"blue-banner",new Vector2(194,383),new Vector2(342,104));blueBox=b.gameObject;
        red=ArenaUI.Text(r.transform,"",new Vector2(20,9),new Vector2(250,38),25,WoodlandAssets.Cream);
        blue=ArenaUI.Text(b.transform,"",new Vector2(-20,9),new Vector2(250,38),25,WoodlandAssets.Cream);
        var rw=ArenaUI.Image(r.transform,new Vector2(20,-40),new Vector2(152,30),new Color(.2f,.1f,.03f));WoodlandAssets.Skin(rw,"wood-panel");redWinsLabel=ArenaUI.Text(rw.transform,"",new Vector2(0,2),new Vector2(130,24),16,WoodlandAssets.Cream);
        var bw=ArenaUI.Image(b.transform,new Vector2(-20,-40),new Vector2(152,30),new Color(.2f,.1f,.03f));WoodlandAssets.Skin(bw,"wood-panel");blueWinsLabel=ArenaUI.Text(bw.transform,"",new Vector2(0,2),new Vector2(130,24),16,WoodlandAssets.Cream);
        var sword=WoodlandAssets.Art(hud.transform,"battle-shield",new Vector2(0,386),new Vector2(102,116));battleButton=sword.gameObject;sword.raycastTarget=true;
        var click=sword.gameObject.AddComponent<Button>();click.targetGraphic=sword;click.onClick.AddListener(()=>{if(PlacementUI.Instance)PlacementUI.Instance.StartBattle();});
        banner=ArenaUI.Text(hud.transform,"",new Vector2(0,210),new Vector2(900,170),43,WoodlandAssets.Cream);banner.lineSpacing=1.25f;
        var shadow=banner.gameObject.AddComponent<UnityEngine.UI.Shadow>();shadow.effectColor=new Color(.15f,.05f,0,.8f);shadow.effectDistance=new Vector2(1,-2);

    }
    public int Count(int team){return alive[team];}
    public static bool IsLiving(Unit u){return u&&!u.dead&&!u.IsInPool&&u.gameObject.activeInHierarchy&&(!u.data||!u.data.Dead);}
    bool Battle {get{var mode=TabsArena.Mode;return mode!=null&&mode.GameStateManager.GameState==GameState.BattleState;}}
    void Update() {
        if(Time.unscaledTime>=nextScan){nextScan=Time.unscaledTime+.15f;Scan();}
        bool active=TabsArena.I&&TabsArena.I.Director.CanSpawn;
        if(hud){redBox.SetActive(active&&!(BetweenRounds&&phase==1));blueBox.SetActive(active&&!(BetweenRounds&&phase==1));battleButton.SetActive(active&&!(BetweenRounds&&phase==1));red.text="แดง  "+alive[0]+" คน";blue.text="น้ำเงิน  "+alive[1]+" คน";redWinsLabel.text="ชนะ "+RedWins;blueWinsLabel.text="ชนะ "+BlueWins;}
        if(!active){ResetFlow();if(banner)banner.text="";return;}
        if(Time.unscaledTime>=nextHide){nextHide=Time.unscaledTime+1;HideHints();}
        bool battle=Battle;
        if(BetweenRounds){AdvanceRound();wasBattle=battle;return;}
        if(!battle){armed=false;losing=-1;Countdown=-1;if(banner)banner.text="";wasBattle=false;return;}
        if(!wasBattle){armed=false;losing=-1;Countdown=-1;}
        wasBattle=true;
        if(alive[0]+alive[1]>0)armed=true;
        int empty=alive[0]==0?(alive[1]==0?2:0):alive[1]==0?1:-1;
        if(!armed||empty<0){losing=-1;Countdown=-1;banner.text="";return;}
        if(empty!=losing){losing=empty;Countdown=10;nextTick=Time.unscaledTime+1;}
        if(Time.unscaledTime>=nextTick){Countdown--;nextTick=Time.unscaledTime+(Countdown<=3?1.6f:1);}
        banner.color=Countdown<=3?new Color(1,.78f,.2f):Color.white;
        banner.text=(empty==2?"ทั้งสองฝั่งหมด":empty==0?"ฝั่งแดงหมด":"ฝั่งน้ำเงินหมด")+"\n"+Math.Max(0,Countdown);
        if(Countdown<=0){
            if(empty==0)BlueWins++;else if(empty==1)RedWins++;
            BetweenRounds=true;phase=1;nextTick=Time.unscaledTime+2.5f;
            banner.color=empty==0?new Color(.4f,.7f,1):empty==1?new Color(1,.4f,.4f):Color.white;
            if(empty!=2&&TabsArena.Mode.CurtainController){TabsArena.Mode.CurtainController.PeekCurtains(empty==0?"LABEL_BLUE_VICTORY":"LABEL_RED_VICTORY","");banner.text="";}else banner.text=empty==2?"เสมอ!":empty==0?"น้ำเงินชนะ!":"แดงชนะ!";
            try{File.WriteAllText(ScorePath,JsonConvert.SerializeObject(new Score{red=RedWins,blue=BlueWins,round=Round+1},Formatting.Indented));}catch{}
        }
    }
    void Scan(){alive[0]=alive[1]=0;if(!TabsArena.I||!TabsArena.I.Director.CanSpawn)return;foreach(var u in FindObjectsOfType<Unit>())if(IsLiving(u))alive[(int)u.Team]++;}
    void AdvanceRound(){
        if(Time.unscaledTime<nextTick)return;
        if(phase==1){
            if(TabsArena.Mode.CurtainController)TabsArena.Mode.CurtainController.OpenCurtains();
            foreach(var u in FindObjectsOfType<Unit>())if(u)u.DestroyUnit();
            Round++;Countdown=3;phase=2;nextTick=Time.unscaledTime+1;
            banner.color=Color.white;banner.text="แดง  VS  น้ำเงิน\nรอบ "+Round+" · 3";
        }else{
            Countdown--;
            if(Countdown>0){banner.text="แดง  VS  น้ำเงิน\nรอบ "+Round+" · "+Countdown;nextTick=Time.unscaledTime+1;}
            else{BetweenRounds=false;armed=false;losing=-1;Countdown=-1;banner.text="";TabsArena.Mode.MatchOver=false;TabsArena.I.Director.StartBattle();}
        }
    }
    void HideHints(){
        foreach(var t in FindObjectsOfType<NewTutorial>())if(t.anim)t.anim.gameObject.SetActive(false);
        foreach(var t in FindObjectsOfType<Landfall.TABS_Input.TABSTutorial>())if(t.anim)t.anim.gameObject.SetActive(false);
    }
    void ResetFlow(){BetweenRounds=false;wasBattle=false;armed=false;losing=-1;Countdown=-1;}
    public void ResetForClear(){if(TabsArena.Mode!=null&&TabsArena.Mode.CurtainController)TabsArena.Mode.CurtainController.OpenCurtains();ResetFlow();if(banner)banner.text="";}
    void OnDestroy(){if(hud)Destroy(hud.gameObject);if(I==this)I=null;}
}


