using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Landfall.TABS;
using HarmonyLib;

public static class ArenaUI
{
    static Font font;
    static Sprite disc;
    public static Font Font { get { if(!font) font=Font.CreateDynamicFontFromOSFont(new[]{"Leelawadee UI","Tahoma","Arial"},32); return font; } }
    public static Sprite Disc {
        get {
            if(disc) return disc;
            var t=new Texture2D(128,128,TextureFormat.RGBA32,false);
            for(int y=0;y<128;y++) for(int x=0;x<128;x++) {
                float d=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(64,64));
                t.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(64-d)));
            }
            t.Apply(); disc=Sprite.Create(t,new Rect(0,0,128,128),new Vector2(.5f,.5f)); return disc;
        }
    }
    public static RectTransform Rect(string name,Transform parent,Vector2 pos,Vector2 size) {
        var g=new GameObject(name,typeof(RectTransform)); g.transform.SetParent(parent,false);
        var r=(RectTransform)g.transform; r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f); r.anchoredPosition=pos; r.sizeDelta=size; return r;
    }
    public static Text Text(Transform parent,string text,Vector2 pos,Vector2 size,int fontSize,Color color) {
        var t=Rect("Text",parent,pos,size).gameObject.AddComponent<TabsCrispText>(); t.font=Font;t.fontSize=fontSize;t.color=color;t.text=text;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;t.supportRichText=false;t.verticalOverflow=VerticalWrapMode.Truncate;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.gameObject.AddComponent<PTVThaiShift>();t.gameObject.AddComponent<ArenaTextCenter>();if(!string.IsNullOrEmpty(text)&&text.StartsWith("@loc:"))TabsLanguage.Bind(t,text.Substring(5)); return t;
    }
    public static Image Image(Transform parent,Vector2 pos,Vector2 size,Color color) {
        var im=Rect("Panel",parent,pos,size).gameObject.AddComponent<Image>(); im.color=color; im.raycastTarget=false; if(size.x>40&&size.y>40&&color.a>.5f)WoodlandAssets.Skin(im,color.r>.7f&&color.g>.5f?"parchment-card":"wood-panel"); return im;
    }
    public static Button Button(Transform parent,string label,Vector2 pos,Vector2 size,Color color,UnityEngine.Events.UnityAction action) {
        var im=Image(parent,pos,size,color); im.raycastTarget=true;
        WoodlandAssets.Skin(im,"wood-panel");var b=im.gameObject.AddComponent<Button>();b.targetGraphic=im;b.onClick.AddListener(action);
        var caption=Text(im.transform,label,new Vector2(0,2),size-new Vector2(18,8),17,WoodlandAssets.Cream);caption.resizeTextForBestFit=true;caption.resizeTextMinSize=11;caption.resizeTextMaxSize=17;var colors=b.colors;colors.highlightedColor=new Color(1,.9f,.67f);colors.pressedColor=new Color(.7f,.52f,.3f);b.colors=colors;return b;
    }
    public static Canvas Canvas(string name,int order) {
        var go=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        UnityEngine.Object.DontDestroyOnLoad(go);var c=go.GetComponent<Canvas>(); c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=order;c.pixelPerfect=true;
        var s=go.GetComponent<CanvasScaler>();s.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;s.referenceResolution=TabsDisplay.Portrait?new Vector2(900,1600):new Vector2(1600,900);s.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;go.AddComponent<TabsResponsiveCanvas>();return c;
    }
}

public sealed class TabsNameplate : MonoBehaviour
{
    static Canvas canvas;
    Unit unit; Renderer[] modelRenderers; RectTransform root; RawImage portrait; Text fallback; Texture retained;
    public bool HasPortrait { get { return retained; } }
    public void Initialize(Unit owner,PTVTikTokLive.Message sender,int team)
    {
        unit=owner;modelRenderers=owner.GetComponentsInChildren<Renderer>();
        if(!canvas) { canvas=ArenaUI.Canvas("TikTok Head Profiles",120);canvas.GetComponent<CanvasScaler>().uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize; }
        float size=Mathf.Clamp(TabsArena.I.Settings.avatarSize,24,32);
        root=ArenaUI.Rect("Profile "+sender.DisplayName,canvas.transform,Vector2.zero,new Vector2(size,size));
        Color tint=team==0?new Color(1,.25f,.32f):new Color(.24f,.6f,1);
        var border=ArenaUI.Image(root,Vector2.zero,new Vector2(size+3,size+3),Color.black);border.sprite=ArenaUI.Disc;
        var frame=ArenaUI.Image(root,Vector2.zero,new Vector2(size,size),Color.white);frame.sprite=ArenaUI.Disc;
        frame.gameObject.AddComponent<Mask>().showMaskGraphic=true;
        portrait=ArenaUI.Rect("Photo",frame.transform,Vector2.zero,new Vector2(size,size)).gameObject.AddComponent<RawImage>();portrait.raycastTarget=false;portrait.enabled=false;
        fallback=ArenaUI.Text(frame.transform,string.IsNullOrEmpty(sender.DisplayName)?"?":System.Globalization.StringInfo.GetNextTextElement(sender.DisplayName),Vector2.zero,new Vector2(size,size),24,new Color(.1f,.15f,.22f));
        var name=ArenaUI.Text(root,sender.DisplayName,new Vector2(0,size*.5f+12),new Vector2(110,22),12,Color.white);
        var outline=name.gameObject.AddComponent<UnityEngine.UI.Outline>();outline.effectColor=Color.black;outline.effectDistance=new Vector2(1,-1);

        var urls=new List<string>();if(!string.IsNullOrEmpty(sender.profilePictureUrl))urls.Add(sender.profilePictureUrl);
        if(sender.profilePictureUrls!=null)foreach(var url in sender.profilePictureUrls)if(!urls.Contains(url))urls.Add(url);
        if(urls.Count>0) StartCoroutine(PTVProfileCache.Get().Fetch(urls,texture=>{
            if(!this||!root||!texture)return;
            retained=texture;PTVProfileCache.Get().Retain(texture);portrait.texture=texture;
            float aspect=(float)texture.width/texture.height;
            portrait.uvRect=aspect>1?new Rect((1-1/aspect)/2,0,1/aspect,1):new Rect(0,(1-aspect)/2,1,aspect);
            portrait.enabled=true;fallback.enabled=false;
        }));
    }
    void LateUpdate()
    {
        if(!root)return;
        var cam=Camera.main;
        if(!TabsRoundFlow.IsLiving(unit)||!cam) { root.gameObject.SetActive(false);return; }
        Vector3 pos=(unit.Head?unit.Head.position:unit.transform.position+Vector3.up*2)+Vector3.up*.32f;
        var p=cam.WorldToScreenPoint(pos);root.gameObject.SetActive(p.z>0&&p.x>-100&&p.x<Screen.width+100&&p.y>-100&&p.y<Screen.height+100);
        float top=p.y;
        foreach(var renderer in modelRenderers)if(renderer&&renderer.enabled&&(renderer is SkinnedMeshRenderer||renderer is MeshRenderer)){
            var bounds=renderer.bounds;var min=bounds.min;var max=bounds.max;
            for(int corner=0;corner<8;corner++){
                var screen=cam.WorldToScreenPoint(new Vector3((corner&1)==0?min.x:max.x,(corner&2)==0?min.y:max.y,(corner&4)==0?min.z:max.z));
                if(screen.z>0)top=Mathf.Max(top,screen.y);
            }
        }
        root.position=new Vector3(p.x,top+root.sizeDelta.y*.5f+18,0);
    }
    void OnDestroy() { if(root)Destroy(root.gameObject);if(retained&&PTVProfileCache.Existing)PTVProfileCache.Existing.Release(retained); }
}

[DefaultExecutionOrder(32000)]
public sealed class TabsArenaUI : MonoBehaviour
{
    Canvas canvas;GameObject panel;Text status,hint;InputField username;
    EventSystem ownEvents;readonly List<EventSystem> suspendedEvents=new List<EventSystem>();
    readonly List<Behaviour> suspendedCameras=new List<Behaviour>();
    readonly Text[] heroLabels=new Text[7],giftNames=new Text[7];
    readonly Image[] giftIcons=new Image[7],unitIcons=new Image[7],unitGifts=new Image[7];
    readonly Text[] giftCaptions=new Text[7];
    readonly Text[] unitLabels=new Text[7];
    readonly RectTransform[] unitCards=new RectTransform[7];
    Vector2 lastLayoutSize;bool lastLayoutPortrait,layoutDirty=true;
    GameObject dock;Transform factionStrip;int rosterVersion=-1,lastFactionCount=-1;float nextDock;
    readonly Dictionary<CanvasGroup,float> nativeGroups=new Dictionary<CanvasGroup,float>();
    readonly List<GameObject> factionTiles=new List<GameObject>();
    Color dark=new Color(.06f,.09f,.15f,.97f),red=new Color(.76f,.17f,.25f),blue=new Color(.13f,.37f,.76f),green=new Color(.09f,.43f,.35f);
    TabsArena Arena { get { return TabsArena.I; } }
    public void SetVisible(bool value){if(panel)panel.SetActive(value);}
    void Start() { Build(); }
    void Build()
    {
        canvas=ArenaUI.Canvas("TABS TikTok Controls",200);canvas.gameObject.AddComponent<GraphicRaycaster>();
        if(!EventSystem.current) { var go=new GameObject("TikTok EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));DontDestroyOnLoad(go); }
        var r=ArenaUI.Image(canvas.transform,new Vector2(-630,92),new Vector2(320,660),dark);panel=r.gameObject;r.raycastTarget=true;
        ArenaUI.Text(r.transform,"TIKTOK LIVE",new Vector2(-65,295),new Vector2(136,36),19,WoodlandAssets.Cream);
        ArenaUI.Button(r.transform,"EN / ไทย",new Vector2(54,295),new Vector2(102,32),green,TabsLanguage.Toggle);
        ArenaUI.Button(r.transform,"‹",new Vector2(129,295),new Vector2(32,34),blue,()=>panel.SetActive(false));
        var field=ArenaUI.Image(r.transform,new Vector2(0,250),new Vector2(282,40),new Color(.19f,.09f,.04f));field.raycastTarget=true;
        username=field.gameObject.AddComponent<InputField>();var txt=ArenaUI.Text(field.transform,"",Vector2.zero,new Vector2(260,38),20,WoodlandAssets.Cream);txt.alignment=TextAnchor.MiddleLeft;username.textComponent=txt;
        username.text=Arena.Settings.username;
        ArenaUI.Button(r.transform,"@loc:ui.connect",new Vector2(-71,204),new Vector2(140,42),green,()=>Connect(false));
        ArenaUI.Button(r.transform,"@loc:ui.test",new Vector2(74,204),new Vector2(134,42),blue,()=>Connect(true));
        status=ArenaUI.Text(r.transform,"",new Vector2(0,160),new Vector2(280,39),13,new Color(.65f,1,.45f));
        ArenaUI.Text(r.transform,"@loc:ui.gift_hero",new Vector2(0,120),new Vector2(288,30),15,WoodlandAssets.Cream);
        for(int i=0;i<7;i++){
            int slot=i;float y=75-i*42;
            var card=ArenaUI.Image(r.transform,new Vector2(-33,y),new Vector2(218,40),new Color(.94f,.81f,.59f));WoodlandAssets.Skin(card,"parchment-card");
            var pick=card.gameObject.AddComponent<Button>();card.raycastTarget=true;pick.targetGraphic=card;pick.onClick.AddListener(()=>GetComponent<TabsGiftSettingsUI>().Open());
            giftIcons[i]=ArenaUI.Image(card.transform,new Vector2(-88,0),new Vector2(28,28),Color.white);giftIcons[i].preserveAspect=true;
            giftNames[i]=ArenaUI.Text(card.transform,"",new Vector2(14,10),new Vector2(158,18),15,WoodlandAssets.Ink);
            heroLabels[i]=ArenaUI.Text(card.transform,"",new Vector2(14,-9),new Vector2(158,18),15,WoodlandAssets.Ink);
            foreach(var caption in new[]{giftNames[i],heroLabels[i]}){caption.resizeTextForBestFit=true;caption.resizeTextMinSize=13;caption.resizeTextMaxSize=15;caption.fontStyle=FontStyle.Bold;caption.horizontalOverflow=HorizontalWrapMode.Overflow;}

            ArenaUI.Button(r.transform,"1",new Vector2(94,y),new Vector2(30,30),red,()=>Demo(slot,0));
            ArenaUI.Button(r.transform,"2",new Vector2(128,y),new Vector2(30,30),blue,()=>Demo(slot,1));
        }
        hint=ArenaUI.Text(r.transform,"",new Vector2(0,-214),new Vector2(280,35),12,WoodlandAssets.Cream);
        ArenaUI.Button(r.transform,"@loc:ui.settings",new Vector2(0,-246),new Vector2(284,34),green,()=>GetComponent<TabsGiftSettingsUI>().Open());
        ArenaUI.Button(r.transform,"@loc:ui.clear",new Vector2(-73,-282),new Vector2(138,28),red,()=>Arena.Director.ClearArena());
        ArenaUI.Button(r.transform,"@loc:ui.hide",new Vector2(73,-282),new Vector2(138,28),blue,()=>panel.SetActive(false));
        ArenaUI.Button(r.transform,"TikTok 9:16",new Vector2(0,-315),new Vector2(284,26),green,TabsDisplay.TikTok);
        var chip=ArenaUI.Button(canvas.transform,"LIVE · F8",Vector2.zero,new Vector2(145,42),blue,()=>panel.SetActive(!panel.activeSelf));
        var cr=(RectTransform)chip.transform;cr.anchorMin=cr.anchorMax=new Vector2(1,1);cr.anchoredPosition=new Vector2(-88,-30);
        BuildDock();
    }
    void BuildDock(){
        var bg=ArenaUI.Image(canvas.transform,new Vector2(0,-345),new Vector2(1260,210),dark);dock=bg.gameObject;bg.raycastTarget=true;
        factionStrip=ArenaUI.Rect("FactionTabs",bg.transform,new Vector2(0,70),new Vector2(1180,44));
        for(int i=0;i<7;i++){
            int slot=i;float x=-519+i*173;
            var card=ArenaUI.Button(bg.transform,"",new Vector2(x,-18),new Vector2(158,126),new Color(.94f,.81f,.59f),()=>SelectUnit(slot));WoodlandAssets.Skin(card.GetComponent<Image>(),"parchment-card");
            unitCards[i]=(RectTransform)card.transform;
            unitIcons[i]=ArenaUI.Image(card.transform,new Vector2(0,14),new Vector2(148,96),Color.white);unitIcons[i].type=Image.Type.Simple;unitIcons[i].preserveAspect=true;
            unitLabels[i]=ArenaUI.Text(card.transform,"",new Vector2(-12,-45),new Vector2(115,24),14,WoodlandAssets.Ink);unitLabels[i].font=TabsCardFont.Font;unitLabels[i].fontStyle=FontStyle.Bold;unitLabels[i].fontSize=17;unitLabels[i].resizeTextForBestFit=true;unitLabels[i].resizeTextMinSize=11;unitLabels[i].resizeTextMaxSize=17;
            var giftFrame=ArenaUI.Image(card.transform,new Vector2(59,-43),new Vector2(30,30),new Color(.24f,.11f,.035f));giftFrame.sprite=ArenaUI.Disc;
            unitGifts[i]=ArenaUI.Image(giftFrame.transform,Vector2.zero,new Vector2(26,26),Color.white);unitGifts[i].preserveAspect=true;
            giftCaptions[i]=ArenaUI.Text(card.transform,"",new Vector2(0,-72),new Vector2(159,16),10,WoodlandAssets.Cream);
        }
    }
    void DrawFactions(){
        layoutDirty=true;
        foreach(var go in factionTiles)Destroy(go);factionTiles.Clear();
        var buttons=FindObjectsOfType<FactionButton>().Where(x=>x&&x.faction&&x.faction.Units!=null&&x.faction.Units.Count(u=>u)>=7).OrderBy(x=>x.index).ToArray();
        float totalWidth=1200f;float cell=totalWidth/Math.Max(1,buttons.Length);
        for(int i=0;i<buttons.Length;i++){
            var native=buttons[i];var f=native.faction;
            var button=ArenaUI.Button(factionStrip,TabsGiftDirector.DisplayName(f.Entity.Name),new Vector2(-totalWidth*.5f+cell*(i+.5f),0),new Vector2(cell-1,41),blue,()=>{if(PlacementUI.Instance)PlacementUI.Instance.SelectFaction(f.Entity.GUID,native);Arena.Director.UseFaction(f);DrawFactions();});
            var caption=button.GetComponentInChildren<Text>();caption.resizeTextMinSize=9;caption.resizeTextMaxSize=14;
            if(Arena.Director.SelectedFaction==f)button.GetComponent<Image>().color=new Color(1,.72f,.35f);
            factionTiles.Add(button.gameObject);
        }
    }

    void SelectUnit(int slot){
        var unit=Arena.Director.Hero(slot);if(!unit||!PlacementUI.Instance)return;
        Arena.Director.SetHint("hint.select_unit",Arena.Settings.giftRules[slot].unit);
        TabsArena.Mode.Brush.SetBrushUnit(unit.Entity.GUID);panel.SetActive(false);
    }

    void Connect(bool test)
    {
        var live=Arena.Live;
        if(live.TestMode!=test) { live.enabled=false;live.TestMode=test;live.enabled=true; }
        Arena.Settings.username=username.text.Trim().TrimStart('@');
        System.IO.File.WriteAllText(TabsArena.ConfigPath,Newtonsoft.Json.JsonConvert.SerializeObject(Arena.Settings,Newtonsoft.Json.Formatting.Indented));
        live.ConnectLive(username.text);
    }
    void Demo(int slot,int team)
    {
                Arena.Director.SpawnManual(slot,team);
    }
void Update()
    {
        if(!panel)return;
        bool placement=Arena.Director.CanSpawn&&TabsArena.Mode.GameStateManager.GameState!=Landfall.TABS.GameState.GameState.BattleState;
        if(Arena.Director.CanSpawn&&Time.unscaledTime>=nextDock){nextDock=Time.unscaledTime+.5f;HideNativeChrome();int count=FindObjectsOfType<FactionButton>().Length;if(count!=lastFactionCount){lastFactionCount=count;DrawFactions();}}
        if(rosterVersion!=Arena.Director.RosterVersion){rosterVersion=Arena.Director.RosterVersion;for(int i=0;i<7;i++){var u=Arena.Director.Hero(i);unitIcons[i].sprite=u?TabsUnitPortraits.Get(u):null;unitIcons[i].enabled=unitIcons[i].sprite;unitIcons[i].color=Color.white;unitLabels[i].text=Arena.Settings.giftRules[i].unit;}}
        for(int i=0;i<7;i++){var preview=TabsUnitPortraits.Get(Arena.Director.Hero(i));if(preview){unitIcons[i].sprite=preview;unitIcons[i].enabled=true;}var rule=Arena.Settings.giftRules[i];unitGifts[i].sprite=TabsGiftCatalog.Icon(rule.gift);unitGifts[i].enabled=unitGifts[i].sprite;giftCaptions[i].text=rule.triggerGift?rule.gift:"";}
        if(Input.GetKeyDown(KeyCode.F8))panel.SetActive(!panel.activeSelf);
        bool menuOpen=panel.activeSelf||(GetComponent<TabsGiftSettingsUI>()&&GetComponent<TabsGiftSettingsUI>().Visible);
        if(dock)dock.SetActive(placement&&!(TabsDisplay.Portrait&&menuOpen));
        if(menuOpen){
            if(!ownEvents){var go=new GameObject("TikTok Menu Input",typeof(EventSystem),typeof(StandaloneInputModule));DontDestroyOnLoad(go);ownEvents=go.GetComponent<EventSystem>();}
            foreach(var e in FindObjectsOfType<EventSystem>())if(e!=ownEvents&&e.enabled){suspendedEvents.Add(e);e.enabled=false;}
            ownEvents.enabled=true;EventSystem.current=ownEvents;
            foreach(var c in FindObjectsOfType<CameraMovement>())SuspendCamera(c);
            foreach(var c in FindObjectsOfType<CameraMovementZoomIn>())SuspendCamera(c);
            foreach(var c in FindObjectsOfType<Landfall.TABS.PlacementCameraMovement>())SuspendCamera(c);
            Cursor.visible=true;Cursor.lockState=CursorLockMode.None;
        }else if(ownEvents&&ownEvents.enabled){ownEvents.enabled=false;foreach(var e in suspendedEvents)if(e)e.enabled=true;suspendedEvents.Clear();foreach(var c in suspendedCameras)if(c)c.enabled=true;suspendedCameras.Clear();}
        if(!panel.activeSelf)return;
        for(int i=0;i<heroLabels.Length;i++)if(heroLabels[i]){var rule=Arena.Settings.giftRules[i];heroLabels[i].text="→ "+rule.unit;giftNames[i].text=rule.gift;giftIcons[i].sprite=TabsGiftCatalog.Icon(rule.gift);giftIcons[i].enabled=giftIcons[i].sprite;}
        Cursor.visible=true;Cursor.lockState=CursorLockMode.None;
        var l=Arena.Live;status.text=(l.TestMode?TabsLanguage.T("ui.test_prefix"):"[LIVE] ")+l.Detail;
        var d=Arena.Director;
        hint.text=d.Hint;
    }
    void HideNativeChrome(){
        var native=PlacementUI.Instance;if(!native)return;
        foreach(string field in new[]{"units","factionBar","redMoney","blueMoney","topButtons","m_WinConditionPresenterAnimator"}){
            var value=AccessTools.Field(typeof(PlacementUI),field).GetValue(native) as Component;if(!value)continue;
            var group=value.GetComponent<CanvasGroup>();if(!group)group=value.gameObject.AddComponent<CanvasGroup>();
            if(!nativeGroups.ContainsKey(group))nativeGroups[group]=group.alpha;
            group.alpha=0;group.blocksRaycasts=false;
        }
    }
    void SuspendCamera(Behaviour c){if(c&&c.enabled){suspendedCameras.Add(c);c.enabled=false;}}
    void Layout(){
        if(!canvas||!dock||!panel)return;
        var area=((RectTransform)canvas.transform).rect;bool portrait=TabsDisplay.Portrait;
        if(area.width<=0||area.height<=0)return;
        if(!layoutDirty&&area.size==lastLayoutSize&&portrait==lastLayoutPortrait)return;
        layoutDirty=false;lastLayoutSize=area.size;lastLayoutPortrait=portrait;
        var menu=(RectTransform)panel.transform;
        if(portrait){menu.anchorMin=menu.anchorMax=menu.pivot=new Vector2(.5f,.5f);menu.anchoredPosition=Vector2.zero;float scale=Mathf.Min(1.7f,Mathf.Min((area.width-36)/320,(area.height-100)/660));menu.localScale=Vector3.one*scale;}
        else{menu.anchorMin=menu.anchorMax=menu.pivot=new Vector2(0,1);menu.anchoredPosition=new Vector2(16,-16);menu.localScale=Vector3.one*Mathf.Min(1,(area.height-250)/660);}
        var tray=(RectTransform)dock.transform;tray.anchorMin=tray.anchorMax=tray.pivot=new Vector2(.5f,0);tray.anchoredPosition=new Vector2(0,8);tray.sizeDelta=new Vector2(portrait?880:1260,portrait?500:210);
        float trayScale=Mathf.Min(1,(area.width-16)/tray.sizeDelta.x);tray.localScale=Vector3.one*trayScale;
        var strip=(RectTransform)factionStrip;TabsDisplay.Top(strip,new Vector2(0,-12));strip.sizeDelta=new Vector2(portrait?860:1200,portrait?86:42);
        int columns=portrait?7:Mathf.Max(1,factionTiles.Count);float cell=strip.sizeDelta.x/columns;
        for(int i=0;i<factionTiles.Count;i++){var tab=(RectTransform)factionTiles[i].transform;TabsDisplay.Top(tab,new Vector2(-strip.sizeDelta.x*.5f+cell*(i%columns+.5f),-(i/columns)*42));tab.sizeDelta=new Vector2(cell-1,41);var caption=tab.GetComponentInChildren<Text>();caption.resizeTextMinSize=portrait?16:9;caption.resizeTextMaxSize=portrait?18:14;caption.fontStyle=portrait?FontStyle.Bold:FontStyle.Normal;}
        for(int i=0;i<7;i++){
            int row=portrait?i/4:0;int count=portrait?(row==0?4:3):7;int col=portrait?i%4:i;float gap=portrait?208:173;
            TabsDisplay.Top(unitCards[i],new Vector2((col-(count-1)*.5f)*gap,-(portrait?110:58)-row*(portrait?188:154)));unitCards[i].sizeDelta=new Vector2(portrait?190:158,portrait?156:126);
            unitIcons[i].rectTransform.anchoredPosition=new Vector2(0,portrait?30:14);
            unitLabels[i].rectTransform.anchoredPosition=new Vector2(portrait?-18:-12,portrait?-43:-45);unitLabels[i].rectTransform.sizeDelta=new Vector2(portrait?144:115,portrait?48:24);unitLabels[i].fontSize=portrait?22:17;unitLabels[i].resizeTextMinSize=portrait?18:11;unitLabels[i].resizeTextMaxSize=portrait?22:17;
            ((RectTransform)unitGifts[i].transform.parent).anchoredPosition=portrait?new Vector2(80,-61):new Vector2(59,-43);
            giftCaptions[i].rectTransform.anchoredPosition=new Vector2(0,portrait?-92:-72);giftCaptions[i].rectTransform.sizeDelta=new Vector2(portrait?190:159,portrait?22:16);giftCaptions[i].fontSize=portrait?18:10;
        }
    }
    void LateUpdate(){Layout();if(panel&&(panel.activeSelf||(GetComponent<TabsGiftSettingsUI>()&&GetComponent<TabsGiftSettingsUI>().Visible))){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}}
    void OnDestroy() { foreach(var pair in nativeGroups)if(pair.Key){pair.Key.alpha=pair.Value;pair.Key.blocksRaycasts=true;}if(canvas)Destroy(canvas.gameObject);if(ownEvents)Destroy(ownEvents.gameObject);foreach(var e in suspendedEvents)if(e)e.enabled=true;foreach(var c in suspendedCameras)if(c)c.enabled=true; }
}





