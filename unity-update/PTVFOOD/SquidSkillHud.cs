using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>PVZ-style skill stickers: stable gift slots, three movable groups and separate orientation layouts.</summary>
public sealed class SquidSkillHud : MonoBehaviour
{
    [Serializable] public sealed class Spot { public bool moved; public float x,y,scale=1; }
    [Serializable] public sealed class Profile { public Spot helpers=new Spot(),orders=new Spot(),random=new Spot(); public bool frames=true; }
    public static SquidSkillHud I { get; private set; }
    public const string PrefsPrefix="PTVFOOD.SkillHUD.Sets13.";
    public Profile Layout { get; private set; }
    public bool SettingsOpen => settingsRoot && settingsRoot.activeSelf;
    SquidGiftDirector director; SquidGame game;
    Canvas canvas; RectTransform root;
    readonly RectTransform[] groups=new RectTransform[3],tiles=new RectTransform[SquidGiftDirector.SlotCount],effects=new RectTransform[SquidGiftDirector.SlotCount];
    readonly UnityEngine.UI.Image[] frames=new UnityEngine.UI.Image[SquidGiftDirector.SlotCount],gifts=new UnityEngine.UI.Image[SquidGiftDirector.SlotCount],glows=new UnityEngine.UI.Image[SquidGiftDirector.SlotCount];
    readonly UnityEngine.UI.Text[] labels=new UnityEngine.UI.Text[SquidGiftDirector.SlotCount],amounts=new UnityEngine.UI.Text[SquidGiftDirector.SlotCount];
    readonly float[] pulses=new float[SquidGiftDirector.SlotCount];
    readonly UnityEngine.UI.Text[] scaleLabels=new UnityEngine.UI.Text[3];
    GameObject settingsRoot; UnityEngine.UI.Text frameLabel;
    bool portrait; string Key => PrefsPrefix+(portrait?"Portrait":"Landscape");
    static Sprite rounded,glow;
    static readonly Color Mint=new Color(.70f,.91f,.79f),Coral=new Color(1f,.72f,.62f),Gold=new Color(1f,.87f,.49f);

    void Awake(){I=this;}
    void Start()
    {
        director=GetComponent<SquidGiftDirector>(); if(!director){enabled=false;return;}
        canvas=PTVUI.MakeCanvas("PTV_SkillHUD",120,transform); root=(RectTransform)canvas.transform;
        rounded=rounded?rounded:PTVGiftNotice.MakeRounded(64,14,true);
        glow=glow?glow:MakeGlow();
        BuildGroup(0,"Helpers","ผู้ช่วย",Mint,180,774);
        BuildGroup(1,"Orders","แกล้ง",Coral,180,774);
        BuildGroup(2,"Random","สุ่มช่วย/แกล้ง",Gold,540,206);
        for(int slot=0;slot<SquidGiftDirector.SlotCount;slot++)BuildTile(slot);
        director.SettingsChanged+=Refresh;director.SlotTriggered+=Pulse;
        PTVScreenLayout.Changed+=OnScreenChanged;
        portrait=PTVScreenLayout.IsPortrait;Load();Refresh();
    }
    void OnDestroy(){if(I==this)I=null;if(director){director.SettingsChanged-=Refresh;director.SlotTriggered-=Pulse;}if(game)game.WheelResolved-=PulseResult;PTVScreenLayout.Changed-=OnScreenChanged;}
    Spot SpotAt(int group)=>group==0?Layout.helpers:group==1?Layout.orders:Layout.random;
    static int GroupOf(int slot)=>slot<5?0:slot<10?1:2;
    static string Icon(SquidAction action)=>action==SquidAction.Wheel?"icon_wheel":"icon_grill";
    static string ShortName(SquidAction action)=>action==SquidAction.HelpSet?"ช่วยทำชุด":action==SquidAction.Customer?"เพิ่มออเดอร์":action==SquidAction.ServeAll?"ทำออเดอร์ทั้งหมด":"สุ่มช่วย/แกล้ง";
    void BuildGroup(int index,string name,string title,Color color,float width,float height)
    {
        var group=PTVUI.Rect(name,root,Vector2.zero,new Vector2(width,height));groups[index]=group;
        var head=PTVUI.Img("Header",group,null,Vector2.zero,new Vector2(width-8,52),color,0,true);
        head.sprite=rounded;head.type=UnityEngine.UI.Image.Type.Sliced;
        head.rectTransform.anchorMin=head.rectTransform.anchorMax=new Vector2(.5f,1);head.rectTransform.anchoredPosition=new Vector2(0,-26);
        var text=PTVUI.Txt("Title",head.transform,title,Vector2.zero,new Vector2(width-16,48),25,PTVUI.Ink,TextAnchor.MiddleCenter,false);
        text.resizeTextForBestFit=true;text.resizeTextMinSize=14;text.resizeTextMaxSize=25;text.verticalOverflow=VerticalWrapMode.Truncate;
        var drag=group.gameObject.AddComponent<SquidSkillDrag>();drag.owner=this;drag.group=index;
    }
    void BuildTile(int slot)
    {
        var group=GroupOf(slot);var image=PTVUI.Img("Skill"+slot,groups[group],null,Vector2.zero,new Vector2(164,130),PTVUI.Cream,0,true);
        image.sprite=rounded;image.type=UnityEngine.UI.Image.Type.Sliced;frames[slot]=image;
        var rect=image.rectTransform;tiles[slot]=rect;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);
        var edge=image.gameObject.AddComponent<UnityEngine.UI.Outline>();edge.effectColor=new Color(.37f,.23f,.13f,.55f);edge.effectDistance=new Vector2(2,-3);
        var action=SquidGiftDirector.SlotAction(slot);
        var effect=PTVUI.Img("Effect",rect,Icon(action),new Vector2(-15,22),new Vector2(106,96));effect.preserveAspect=true;effects[slot]=effect.rectTransform;
        var glowImage=PTVUI.Img("GiftGlow",rect,null,new Vector2(51,37),new Vector2(72,72));glowImage.sprite=glow;glows[slot]=glowImage;
        var gift=PTVUI.Img("Gift",rect,null,new Vector2(51,37),new Vector2(54,54));gift.preserveAspect=true;gifts[slot]=gift;
        if(slot<4||(slot>=5&&slot<10)){var badge=PTVUI.Img("Level",rect,null,new Vector2(-57,43),new Vector2(30,28),group==0?Mint:Coral);badge.sprite=rounded;badge.type=UnityEngine.UI.Image.Type.Sliced;PTVUI.Txt("Tier",badge.transform,SquidGiftDirector.SlotLevel(slot).ToString(),Vector2.zero,new Vector2(28,26),19,PTVUI.Ink,TextAnchor.MiddleCenter,false);}
        labels[slot]=PTVUI.Txt("Name",rect,slot>=10?SquidGiftDirector.RandomSize(slot):ShortName(action),new Vector2(0,-32),new Vector2(158,32),24,PTVUI.Ink,TextAnchor.MiddleCenter,false);
        labels[slot].resizeTextForBestFit=true;labels[slot].resizeTextMinSize=12;labels[slot].resizeTextMaxSize=24;labels[slot].verticalOverflow=VerticalWrapMode.Truncate;
        amounts[slot]=PTVUI.Txt("Amount",rect,"",new Vector2(0,-56),new Vector2(158,22),20,PTVUI.Ink,TextAnchor.MiddleCenter,false);
        foreach(var label in new[]{labels[slot],amounts[slot]}){var nameEdge=label.gameObject.AddComponent<UnityEngine.UI.Outline>();nameEdge.effectColor=new Color(1,1,1,.95f);nameEdge.effectDistance=new Vector2(1.5f,-1.5f);nameEdge.enabled=false;}
    }
    public void Refresh()
    {
        if(!director||!canvas)return;director.Validate();
        for(int slot=0;slot<SquidGiftDirector.SlotCount;slot++){
            var config=director.settings.slots[slot];gifts[slot].sprite=PTVGiftCatalog.Icon(config.gift);gifts[slot].enabled=gifts[slot].sprite;glows[slot].enabled=gifts[slot].enabled;
            amounts[slot].text=config.min.ToString();amounts[slot].enabled=slot!=4;
            if(slot==4){labels[slot].rectTransform.anchoredPosition=new Vector2(0,-42);labels[slot].rectTransform.sizeDelta=new Vector2(158,48);labels[slot].resizeTextMinSize=14;}
        }
        for(int group=0;group<3;group++){
            int index=0;var ordered=Enumerable.Range(0,SquidGiftDirector.SlotCount).Where(s=>GroupOf(s)==group);
            foreach(int slot in ordered){int row=group==2?0:index;float x=group==2?(index-1)*180:0;tiles[slot].anchoredPosition=new Vector2(x,-126-row*142);index++;}
        }
        Apply();
    }
    void Load()
    {
        try{Layout=JsonUtility.FromJson<Profile>(PlayerPrefs.GetString(Key,""));}catch{Layout=null;}
        if(Layout==null)Layout=new Profile();if(Layout.helpers==null)Layout.helpers=new Spot();if(Layout.orders==null)Layout.orders=new Spot();if(Layout.random==null)Layout.random=new Spot();Apply();
    }
    public void Save(){PlayerPrefs.SetString(Key,JsonUtility.ToJson(Layout));PlayerPrefs.Save();}
    // Edits are saved at their action; resizing only reads the other orientation.
    // It must still reflow when the OS makes preferences read-only.
    void OnScreenChanged(){bool value=PTVScreenLayout.IsPortrait;if(value!=portrait){portrait=value;Load();}Apply();}
    Vector2 Default(int group)=>portrait?(group==0?new Vector2(.12f,.76f):group==1?new Vector2(.90f,.76f):new Vector2(.5f,.17f)):(group==0?new Vector2(.07f,.52f):group==1?new Vector2(.93f,.52f):new Vector2(.5f,.10f));
    void Apply()
    {
        if(Layout==null||!root)return;Canvas.ForceUpdateCanvases();
        for(int i=0;i<3;i++){
            var spot=SpotAt(i);spot.scale=Mathf.Clamp(spot.scale>0?spot.scale:1,.5f,1.6f);
            var anchor=spot.moved?new Vector2(spot.x,spot.y):Default(i);
            groups[i].anchorMin=groups[i].anchorMax=anchor;groups[i].anchoredPosition=Vector2.zero;
            groups[i].localScale=Vector3.one*(i==2?.85f:portrait?.70f:.90f)*spot.scale;Clamp(i);
        }
        for(int s=0;s<frames.Length;s++)if(frames[s]){var c=frames[s].color;c.a=Layout.frames?1:0;frames[s].color=c;var outline=frames[s].GetComponent<UnityEngine.UI.Outline>();if(outline)outline.enabled=Layout.frames;labels[s].GetComponent<UnityEngine.UI.Outline>().enabled=!Layout.frames;amounts[s].GetComponent<UnityEngine.UI.Outline>().enabled=!Layout.frames;}
        RefreshSettings();
    }
    public void Clamp(int group)
    {
        var rect=groups[group];if(!rect||!root)return;
        var size=Vector2.Scale(rect.sizeDelta,new Vector2(rect.localScale.x,rect.localScale.y));
        var pivot=Vector2.Scale(rect.anchorMin,root.rect.size)+rect.anchoredPosition;
        var half=size*.5f;var clamped=new Vector2(Mathf.Clamp(pivot.x,half.x+8,Mathf.Max(half.x+8,root.rect.width-half.x-8)),Mathf.Clamp(pivot.y,half.y+8,Mathf.Max(half.y+8,root.rect.height-half.y-8)));
        rect.anchoredPosition+=clamped-pivot;
    }
    public void CommitPosition(int group)
    {
        Clamp(group);var rect=groups[group];var spot=SpotAt(group);var p=Vector2.Scale(rect.anchorMin,root.rect.size)+rect.anchoredPosition;
        spot.moved=true;spot.x=p.x/Mathf.Max(1,root.rect.width);spot.y=p.y/Mathf.Max(1,root.rect.height);Save();
    }
    public void ResetPosition(int group){SpotAt(group).moved=false;Apply();Save();}
    public void SetScale(int group,float value){SpotAt(group).scale=Mathf.Clamp(value,.5f,1.6f);Apply();Save();}
    public void ResetLayout(){Layout=new Profile();Apply();Save();}
    public void Pulse(int slot){if(slot>=0&&slot<pulses.Length)pulses[slot]=1;}
    void PulseResult(SquidAction action,int amount,int source){int level=Mathf.Clamp(SquidGiftDirector.SlotLevel(source),1,4);if(action==SquidAction.HelpSet)Pulse(level-1);else if(action==SquidAction.Customer)Pulse(5+level-1);}

    void Update()
    {
        if(!canvas)return;if(!game){game=UnityEngine.Object.FindFirstObjectByType<SquidGame>();if(game)game.WheelResolved+=PulseResult;}
        canvas.enabled=game&&!game.IsWaiting&&!PTVStartMenu.UIBlocking&&!SettingsOpen;
        for(int slot=0;slot<tiles.Length;slot++){
            pulses[slot]=Mathf.MoveTowards(pulses[slot],0,Time.unscaledDeltaTime*1.8f);tiles[slot].localScale=Vector3.one*(1+.13f*Mathf.Sin(pulses[slot]*Mathf.PI));
            float wave=Time.unscaledTime*2+slot*.6f;effects[slot].anchoredPosition=new Vector2(-15,22+2*Mathf.Sin(wave));effects[slot].localRotation=Quaternion.Euler(0,0,2*Mathf.Sin(wave*.8f));
            glows[slot].color=new Color(1f,.81f,.26f,.6f+.2f*Mathf.Sin(wave*1.6f));
        }
        if(SettingsOpen&&(!PTVLive.I||!PTVLive.I.menu||!PTVLive.I.menu.SettingsOpen))CloseSettings();
    }
    public void OpenSettings()
    {
        if(!settingsRoot)BuildSettings();settingsRoot.SetActive(true);RefreshSettings();
    }
    public void CloseSettings(){if(settingsRoot)settingsRoot.SetActive(false);}
    void BuildSettings()
    {
        var overlay=PTVUI.MakeCanvas("PTV_HudSettings",30000,transform);
        settingsRoot=PTVUI.Rect("HudSettings",overlay.transform,Vector2.zero,Vector2.zero).gameObject;PTVUI.Stretch((RectTransform)settingsRoot.transform);
        var dim=settingsRoot.AddComponent<UnityEngine.UI.Image>();dim.color=new Color(.02f,.08f,.06f,.82f);
        var card=PTVUI.Img("Card",settingsRoot.transform,"fr_skill_card",Vector2.zero,new Vector2(1240,870),null,1,true);
        PTVUI.Txt("Title",card.transform,"แถบสกิล",new Vector2(0,330),new Vector2(1050,90),52,PTVUI.Ink,TextAnchor.MiddleCenter,false);
        PTVUI.Txt("Hint",card.transform,"ลากแถบสกิลในหน้าเล่นเพื่อย้าย • แตะสองครั้งเพื่อคืนตำแหน่ง",new Vector2(0,245),new Vector2(1120,85),26,PTVUI.Ink,TextAnchor.MiddleCenter,false);
        string[] names={"ผู้ช่วย","แกล้ง","สุ่มช่วย/แกล้ง"};
        for(int i=0;i<3;i++){
            int index=i;float y=125-i*112;
            PTVUI.Txt("Group"+i,card.transform,names[i],new Vector2(-355,y),new Vector2(420,66),32,PTVUI.Ink,TextAnchor.MiddleLeft,false);
            PTVUI.Btn("Minus"+i,card.transform,"fr_button_close","−",new Vector2(80,y),new Vector2(100,76),38,()=>SetScale(index,SpotAt(index).scale-.1f));
            scaleLabels[i]=PTVUI.Txt("Scale"+i,card.transform,"",new Vector2(220,y),new Vector2(160,66),32,PTVUI.Ink,TextAnchor.MiddleCenter,false);
            PTVUI.Btn("Plus"+i,card.transform,"fr_button_close","+",new Vector2(360,y),new Vector2(100,76),38,()=>SetScale(index,SpotAt(index).scale+.1f));
        }
        var toggle=PTVUI.Btn("Frames",card.transform,"fr_button_close",null,new Vector2(0,-230),new Vector2(660,76),28,()=>{Layout.frames=!Layout.frames;Apply();Save();});
        frameLabel=PTVUI.Txt("Label",toggle.transform,"",Vector2.zero,new Vector2(620,66),28,PTVUI.Ink,TextAnchor.MiddleCenter,false);
        PTVUI.Btn("Reset",card.transform,"fr_button_close","คืนค่าแถบสกิล",new Vector2(-290,-345),new Vector2(440,90),32,ResetLayout);
        PTVUI.Btn("Back",card.transform,"fr_button_reset","กลับ",new Vector2(290,-345),new Vector2(360,90),34,CloseSettings);
        PTVScreenLayout.Fit((RectTransform)settingsRoot.transform,1320,950);
    }
    void RefreshSettings(){for(int i=0;i<3;i++)if(scaleLabels[i])scaleLabels[i].text=Mathf.RoundToInt(SpotAt(i).scale*100)+"%";if(frameLabel)frameLabel.text=Layout.frames?"กรอบสกิล: เปิด":"กรอบสกิล: ปิด";}
    static Sprite MakeGlow()
    {
        const int n=64;var t=new Texture2D(n,n,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
        for(int y=0;y<n;y++)for(int x=0;x<n;x++){float r=Vector2.Distance(new Vector2(x+.5f,y+.5f),Vector2.one*n*.5f)/(n*.5f);float a=Mathf.Clamp01(1-r);t.SetPixel(x,y,new Color(1,1,1,a*a));}t.Apply();
        return Sprite.Create(t,new Rect(0,0,n,n),new Vector2(.5f,.5f),100);
    }
}

public sealed class SquidSkillDrag:MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler,IPointerClickHandler
{
    public SquidSkillHud owner;public int group;Vector2 offset;RectTransform rect;
    public void OnBeginDrag(PointerEventData e){rect=(RectTransform)transform;Vector2 p;RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rect.parent,e.position,e.pressEventCamera,out p);offset=(Vector2)rect.localPosition-p;}
    public void OnDrag(PointerEventData e){Vector2 p;if(rect&&RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rect.parent,e.position,e.pressEventCamera,out p)){rect.localPosition=p+offset;owner.Clamp(group);}}
    public void OnEndDrag(PointerEventData e){owner.CommitPosition(group);}
    public void OnPointerClick(PointerEventData e){if(e.clickCount>=2)owner.ResetPosition(group);}
}
