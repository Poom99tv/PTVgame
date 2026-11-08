using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Start screen (connect TikTok Live -> PLAY), gear button and the gift settings with the gift picture picker.
/// Same flow as PVZ / PTVegg; 8 slots: 4 skills that push the zombie up (left) + 4 that knock him down (right).
/// </summary>
public static class PTVGameHooks
{
    /// <summary>Set by the game: reload the round (R key / back to menu / after the victory dance).</summary>
    public static System.Action Restart;
}
[DisallowMultipleComponent]
public sealed partial class PTVStartMenu : MonoBehaviour
{
    const int N = ZGiftDirector.SlotCount;
    /// <summary>True while the start screen or the settings are on screen (blocks hotkeys / game input).</summary>
    public static bool UIBlocking { get; private set; }
    /// <summary>After the first PLAY, new rounds skip the start screen.</summary>
    public static bool SessionStarted { get; private set; }

    PTVTikTokLive live; ZGiftDirector gifts;
    Canvas canvas;
    GameObject startPanel, settingsPanel, picker, gearButton, langButton, screenButton;
    Text screenLabel;
    void RefreshScreenLabel() { if (screenLabel) screenLabel.text = PTVScreenLayout.PortraitSetting ? "จอแนวตั้ง 9:16" : "จอแนวนอน 16:9"; }
    RectTransform gearRect;
    InputField account, search;
    Button connect, play;
    Text connectLabel, statusText, playHint, liveChip, settingsNotice, pageLabel;
    Image statusDot;
    // start-screen skill rows: 2 rows x 8 chips, filled for the selected mode (in the same order as the in-game columns)
    readonly RectTransform[,] rowChips = new RectTransform[2, 9];
    readonly Image[,] rowChipSkill = new Image[2, 9], rowChipGift = new Image[2, 9];
    readonly int[,] rowChipSlot = new int[2, 9];
    readonly Text[] rowLabels = new Text[2];
    GameObject menuButton;
    // settings
    readonly string[] draft = new string[N];
    readonly Image[] giftIcons = new Image[N];
    readonly Text[] giftNames = new Text[N], giftPrices = new Text[N];
    readonly InputField[] likeInputs = new InputField[N];
    readonly Toggle[,] triggerChecks = new Toggle[N, 4];
    readonly List<GameObject> cells = new List<GameObject>();
    readonly RectTransform[] settingRows = new RectTransform[N];
    readonly Image[] settingEffectIcons = new Image[N];
    readonly Text[] settingEffectNames = new Text[N], settingEffectInfo = new Text[N];
    Text settingsLeftHeading, settingsRightHeading;
    RectTransform grid;
    int selecting, page;
    bool built, startVisible, pausedByMe;
    float gearSpin;
    string lastState = "idle";

    public bool SettingsOpen => (settingsPanel && settingsPanel.activeSelf) || (ZHud.I && ZHud.I.LayoutEditing);
    public bool StartScreenOpen => startPanel && startPanel.activeSelf;

    void Start()
    {
        live = GetComponent<PTVTikTokLive>();
        gifts = GetComponent<ZGiftDirector>();
        Build();
    }

    // ------------------------------------------------------------------ build
    void Build()
    {
        EnsureEventSystem();
        canvas = PTVUI.MakeCanvas("PVZUP_LiveMenu", 300, transform);
        var root = canvas.transform;

        // ---------- start screen (the garden stays visible behind it)
        startPanel = PTVUI.Rect("StartScreen", root, Vector2.zero, Vector2.zero).gameObject; PTVUI.Stretch((RectTransform)startPanel.transform);
        var block = startPanel.AddComponent<Image>(); block.color = new Color(0.02f, 0.06f, 0.02f, 0.45f);

        var header = PTVUI.Img("TitleHeader", startPanel.transform, null, new Vector2(0, 390), new Vector2(980, 250), Color.white);
        var logo = Resources.Load<Texture2D>("PTVUP/ptvup-logo");
        if (logo) { header.sprite = Sprite.Create(logo, new Rect(0,0,logo.width,logo.height), Vector2.one*.5f, 100); header.preserveAspect = true; }
        else PTVUI.Txt("Title", header.transform, "PTVUP", Vector2.zero, new Vector2(940, 100), 92, ZComicUI.Ink);
        header.raycastTarget = false;
        var sub = PTVUI.Txt("Sub", header.transform, "ptvgame.com", new Vector2(0, -108), new Vector2(940, 40), 28, Color.white);

        var card = PTVUI.Img("ConnectCard", startPanel.transform, "fr_skill_card", new Vector2(0, -40), new Vector2(1240, 565), null, 1f);
        if (!card.sprite) card.color = new Color(0.08f, 0.2f, 0.12f, 0.95f);
        var c = card.transform;
        ZComicUI.InkText(PTVUI.Txt("IdLabel", c, "TikTok ID ของไลฟ์", new Vector2(-150, 192), new Vector2(600, 50), 34, PTVUI.Muted, TextAnchor.MiddleLeft));
        account = PTVUI.Input("Account", c, PlayerPrefs.GetString(PTVTikTokLive.UsernameKey, ""), "@ ชื่อบัญชี TikTok", new Vector2(-150, 123), new Vector2(650, 80), 34);
        account.characterLimit = 64;
        var inputPaper=account.GetComponent<UnityEngine.UI.Image>();inputPaper.sprite=ZComicUI.Get("cc_menu_input-frame");inputPaper.type=UnityEngine.UI.Image.Type.Simple;inputPaper.preserveAspect=false;inputPaper.color=Color.white;
        foreach(var effect in account.GetComponents<UnityEngine.UI.Shadow>())effect.enabled=false;
        ZComicUI.InkText(account.textComponent);ZComicUI.InkText(account.placeholder as UnityEngine.UI.Text,true);
        PTVUI.Stretch(account.textComponent.rectTransform);account.textComponent.rectTransform.offsetMin=new Vector2(86,8);account.textComponent.rectTransform.offsetMax=new Vector2(-20,-8);account.textComponent.alignment=TextAnchor.MiddleLeft;
        var placeholder=account.placeholder as UnityEngine.UI.Text;if(placeholder){PTVUI.Stretch(placeholder.rectTransform);placeholder.rectTransform.offsetMin=new Vector2(86,8);placeholder.rectTransform.offsetMax=new Vector2(-20,-8);placeholder.alignment=TextAnchor.MiddleLeft;placeholder.fontStyle=FontStyle.BoldAndItalic;}
        connect = PTVUI.Btn("Connect", c, "fr_button_reset", "เชื่อมต่อ", new Vector2(350, 123), new Vector2(310, 112), 38, OnConnect, 0f);
        connectLabel = connect.GetComponentInChildren<Text>();
        statusDot = PTVUI.Img("StatusDot", c, null, new Vector2(-430, 63), new Vector2(20, 20), new Color(.78f,.77f,.65f));
        statusText = PTVUI.Txt("Status", c, "", new Vector2(10, 63), new Vector2(820, 40), 25, PTVUI.Cream, TextAnchor.MiddleLeft);

        // Drawn reference assets: pale bands, leafy category tags and individual rounded slots.
        ZComicUI.InkText(statusText,true);
        for(int row=0;row<2;row++)
        {
            bool helper=row==0;float y=helper?-30:-165;
            PTVUI.Img(helper?"MainHelpersBand":"MainPranksBand",c,helper?"cc_menu_helper-band":"cc_menu_prank-band",new Vector2(0,y),new Vector2(1160,125));
            var tag=PTVUI.Img(helper?"MainHelpersTag":"MainPranksTag",c,helper?"cc_menu_helper-tag":"cc_menu_prank-tag",new Vector2(-477,y),new Vector2(215,100));
            var label=PTVUI.Txt(helper?"HelpRowLabel":"PrankRowLabel",tag.transform,helper?"ขึ้น ▲":"ลง ▼",new Vector2(4,3),new Vector2(170,64),38,Color.black);ZComicUI.InkText(label);
            rowLabels[row]=label;
        }
        for (int row = 0; row < 2; row++)
            for (int k = 0; k < 9; k++)
            {
                var chip=PTVUI.Img("GiftChip"+row+"_"+k,c,"cc_menu_menu-slot",new Vector2(-310+k*118,row==0?-30:-165),new Vector2(136,100));
                var ic=PTVUI.Img("Effect",chip.transform,null,new Vector2(-15,8),new Vector2(100,100));ic.preserveAspect=true;
                var gi=PTVUI.Img("Gift",chip.transform,null,new Vector2(39,-4),new Vector2(52,52));gi.preserveAspect=true;
                rowChips[row,k]=chip.rectTransform;rowChipSkill[row,k]=ic;rowChipGift[row,k]=gi;
            }
        RefreshStartSkills();
        play=PTVUI.Btn("Play",c,"cc_menu_play-complete","เริ่มเล่น",new Vector2(0,-330),new Vector2(620,185),70,BeginPlay,0f);
        var playText=play.GetComponentInChildren<UnityEngine.UI.Text>();playText.rectTransform.anchoredPosition=new Vector2(47,1);playText.rectTransform.sizeDelta=new Vector2(300,100);playText.resizeTextMaxSize=70;
        playHint = PTVUI.Txt("PlayHint", startPanel.transform, "", new Vector2(0, -480), new Vector2(1500, 50), 26, PTVUI.Cream);

        header.transform.SetAsLastSibling();
        PTVScreenLayout.Fit((RectTransform)startPanel.transform, 1300f, 1120f);

        // ---------- gear (top-left)
        var gear = PTVUI.Btn("SettingsGear", root, "Gear", null, Vector2.zero, new Vector2(104, 104), 0, OpenSettings, 0f);
        gearRect = (RectTransform)gear.transform; gearRect.anchorMin = gearRect.anchorMax = new Vector2(0f, 1f);
        gearRect.pivot = new Vector2(0.5f, 0.5f);
        gearRect.anchoredPosition = new Vector2(26 + 52, -24 - 52);
        ((Image)gear.targetGraphic).preserveAspect = true;
        gear.GetComponent<PTVHoverScale>().amount = 0.12f;
        gearButton = gear.gameObject;
        PTVDraggable.Attach(gearButton, "Gear");
        // back to the start screen (choose the mode again) - shown while a round is running
        var back = PTVUI.Btn("BackToMenu", root, "cc_sticker", null, Vector2.zero, new Vector2(88, 88), 0, BackToMenu, 0f);
        var br = (RectTransform)back.transform; br.anchorMin = br.anchorMax = new Vector2(0f, 1f); br.pivot = new Vector2(0.5f, 0.5f); br.anchoredPosition = new Vector2(196, -76);
        var backHome=PTVUI.Img("HomeIcon",br,null,Vector2.zero,new Vector2(68,68));backHome.sprite=ZIcons.Home;backHome.preserveAspect=true;
        PTVDraggable.Attach(back.gameObject,"HomeMenu");
        menuButton = back.gameObject; menuButton.SetActive(false);
        liveChip = PTVUI.Txt("LiveChip", root, "", Vector2.zero, new Vector2(520, 40), 26, PTVUI.Cream, TextAnchor.MiddleLeft);
        liveChip.raycastTarget = true; PTVDraggable.Attach(liveChip.gameObject, "LiveChip");
        var lr = liveChip.rectTransform; lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0f, 1f); lr.anchoredPosition = new Vector2(140, -56);

        BuildSettings(root);
        BuildPicker(settingsPanel.transform);
        settingsPanel.SetActive(false);
        PTVScreenLayout.Fit((RectTransform)settingsPanel.transform, 1920f, 1080f);

        // ---------- language + screen shape (top-right)
        var lang = PTVUI.Btn("Language", root, "fr_button_close", "ภาษา: ไทย", Vector2.zero, new Vector2(300, 92), 30, () => PTVLang.Toggle(), 0f);
        var lr2 = (RectTransform)lang.transform; lr2.anchorMin = lr2.anchorMax = new Vector2(1f, 1f); lr2.pivot = new Vector2(0.5f, 0.5f);
        lr2.anchoredPosition = new Vector2(-24 - 150, -24 - 46);
        langButton=lang.gameObject;var languageText=lang.GetComponentInChildren<UnityEngine.UI.Text>();ZComicUI.InkText(languageText);languageText.rectTransform.anchoredPosition=new Vector2(4,4);languageText.rectTransform.sizeDelta=new Vector2(202,64);languageText.resizeTextMinSize=18;languageText.resizeTextMaxSize=28;
        var scr = PTVUI.Btn("ScreenMode", root, "fr_button_close", "จอแนวนอน 16:9", Vector2.zero, new Vector2(300, 92), 26, () => { PTVScreenLayout.Toggle(); RefreshScreenLabel(); }, 0f);
        var sr = (RectTransform)scr.transform; sr.anchorMin = sr.anchorMax = new Vector2(1f, 1f); sr.pivot = new Vector2(0.5f, 0.5f);
        sr.anchoredPosition = new Vector2(-24 - 150, -24 - 46 - 104);
        screenButton=scr.gameObject;screenLabel=scr.GetComponentInChildren<Text>();ZComicUI.InkText(screenLabel);screenLabel.rectTransform.anchoredPosition=new Vector2(4,4);screenLabel.rectTransform.sizeDelta=new Vector2(202,64);screenLabel.resizeTextMinSize=18;screenLabel.resizeTextMaxSize=26;
        RefreshScreenLabel();
        PTVLang.Changed += RefreshChips;
        built = true;
        RefreshChips();
    }

    void BuildSettings(Transform root)
    {
        settingsPanel = PTVUI.Rect("GiftSettings", root, Vector2.zero, Vector2.zero).gameObject; PTVUI.Stretch((RectTransform)settingsPanel.transform);
        var dim = settingsPanel.AddComponent<Image>(); dim.color = new Color(0.02f, 0.03f, 0f, 0.78f);
        var card = PTVUI.Img("SettingsCard", settingsPanel.transform, "fr_skill_card", new Vector2(0, -20), new Vector2(1890, 1000), null, 1f);
        if (!card.sprite) card.color = new Color(0.08f, 0.2f, 0.12f, 0.97f);
        var c = card.transform;
        BuildComicSettingsPages(c);var content=comicGiftPage.transform;
        var header = PTVUI.Img("Header", settingsPanel.transform, "fr_title_header", new Vector2(0, 470), new Vector2(500, 118));
        header.preserveAspect = true;
        PTVUI.Txt("Heading", header.transform, "ตั้งค่า", new Vector2(0, 2), new Vector2(440, 86), 60, PTVUI.Cream);
        var close = PTVUI.Btn("CloseX", c, "ct_close_x", null, new Vector2(875, 445), new Vector2(86, 86), 0, CloseSettings, 0f);
        ((Image)close.targetGraphic).preserveAspect = false;

        PTVUI.Img("HelpersHeadingBack",content,"cc_strip",new Vector2(-450,315),new Vector2(870,62),ZComicUI.Lime);
        PTVUI.Img("PranksHeadingBack",content,"cc_strip",new Vector2(450,315),new Vector2(870,62),ZComicUI.Coral);
        settingsLeftHeading = PTVUI.Txt("ColHelpers", content, "ขึ้น ▲  (+ เมตร)", new Vector2(-450, 315), new Vector2(600, 48), 32, new Color(0.6f, 1f, 0.5f));
        settingsRightHeading = PTVUI.Txt("ColPranks", content, "ลง ▼  (− เมตร)", new Vector2(450, 315), new Vector2(700, 48), 32, new Color(1f, 0.6f, 0.5f));

        string[] trig = { "ส่งของขวัญ", "ติดตาม", "เคาะใจ", "หัวใจส้ม" };
        float[] boxX = { 20, 110, 200, 290 };
        for (int i = 0; i < N; i++)
        {
            int slot = i;
            bool helper = ZEffects.IsHelper((ZEffect)i);   // left column: + skills, right column: − skills
            int row=ZEffects.DisplayRow(i);
            float x = helper ? -450 : 450, y = 238 - row * 112;
            var rowImg = PTVUI.Img("Row" + i, content, "fr_status_pill", new Vector2(x, y), new Vector2(880, 76), null, 1.6f);
            if (!rowImg.sprite) rowImg.color = new Color(0.15f, 0.12f, 0.05f, 0.9f);
            var rt = rowImg.transform; settingRows[i] = rowImg.rectTransform;

            var eff = PTVUI.Img("EffectIcon", rt, null, new Vector2(-392, 0), new Vector2(72, 72)); eff.preserveAspect = true;
            settingEffectIcons[i] = eff; eff.sprite = ZIcons.Effect((ZEffect)i); eff.enabled = eff.sprite;
            var nm = PTVUI.Txt("EffectName", rt, ZEffects.Names[i], new Vector2(-268, 0), new Vector2(170, 88), 22, helper ? new Color(0.75f, 1f, 0.6f) : new Color(1f, 0.75f, 0.6f));
            settingEffectNames[i] = nm; nm.horizontalOverflow = HorizontalWrapMode.Wrap;
            settingEffectInfo[i] = PTVUI.Txt("EffectInfo",rt,"",new Vector2(-268,-10),new Vector2(170,32),14,ZComicUI.Ink);

            var choice = PTVUI.Btn("GiftChoice" + i, rt, "fr_xp_card", null, new Vector2(-95, 0), new Vector2(170, 96), 0, () => OpenGiftPicker(slot), 2.6f);
            choice.GetComponent<PTVHoverScale>().amount = 0.04f;
            giftIcons[i] = PTVUI.Img("Icon", choice.transform, null, new Vector2(-53, 4), new Vector2(56, 56)); giftIcons[i].preserveAspect = true;
            giftNames[i] = PTVUI.Txt("Name", choice.transform, "", new Vector2(28, 10), new Vector2(96, 46), 16, PTVUI.Cream);
            giftNames[i].horizontalOverflow = HorizontalWrapMode.Wrap;giftNames[i].resizeTextForBestFit=true;giftNames[i].resizeTextMinSize=11;giftNames[i].resizeTextMaxSize=16;
            giftPrices[i] = PTVUI.Txt("Price", choice.transform, "", new Vector2(28, -26), new Vector2(100, 22), 13, PTVUI.Muted);

            for (int s = 0; s < 4; s++)
            {
                triggerChecks[i, s] = PTVUI.Check("Trigger" + i + "_" + s, rt, new Vector2(boxX[s], 14), 38);
                var lab = PTVUI.Txt("TrigLabel" + s, rt, trig[s], new Vector2(boxX[s], -28), new Vector2(86, 30), 15, s == 3 ? new Color(1f, 0.7f, 0.35f) : PTVUI.Cream);
                lab.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            PTVUI.Txt("NLabel",rt,"N",new Vector2(323,14),new Vector2(22,30),16,Color.white);
            likeInputs[i] = PTVUI.Input("Likes" + i, rt, "100", "100", new Vector2(375, 14), new Vector2(78, 52), 22);
            likeInputs[i].contentType = InputField.ContentType.IntegerNumber; likeInputs[i].characterLimit = 6;
        }
        PTVUI.Txt("LikesHelp", content, "เคาะใจ = ทุก ๆ N ครั้งต่อคน (ใส่ N ในช่องข้างกล่องเคาะใจ)", new Vector2(-450, -372), new Vector2(860, 40), 22, PTVUI.Muted);
        settingsNotice = PTVUI.Txt("Notice", c, "", new Vector2(450, -362), new Vector2(760, 34), 24, new Color(1f, 0.6f, 0.45f));
        PTVUI.Btn("Defaults", c, "fr_button_close", "ค่าเริ่มต้น", new Vector2(-510, -425), new Vector2(280, 100), 32, LoadDefaults, 0f);
        PTVUI.Btn("Cancel", c, "fr_button_close", "ยกเลิก", new Vector2(-40, -425), new Vector2(280, 100), 34, CloseSettings, 0f);
        PTVUI.Btn("Save", c, "fr_button_reset", "บันทึก", new Vector2(450, -425), new Vector2(390, 100), 40, SaveAndClose, 0f);
    }

    void BuildPicker(Transform root)
    {
        picker = PTVUI.Rect("GiftPicker", root, Vector2.zero, Vector2.zero).gameObject; PTVUI.Stretch((RectTransform)picker.transform);
        var dim = picker.AddComponent<Image>(); dim.color = new Color(0.02f, 0.01f, 0f, 0.8f);
        var card = PTVUI.Img("PickerCard", picker.transform, "fr_skill_card", new Vector2(0, -30), new Vector2(1560, 930), null, 1f);
        if (!card.sprite) card.color = new Color(0.08f, 0.2f, 0.12f, 0.97f);
        var c = card.transform;
        var header = PTVUI.Img("Header", picker.transform, "fr_title_header", new Vector2(0, 440), new Vector2(640, 249));
        header.preserveAspect = true;
        PTVUI.Txt("Title", header.transform, "เลือกของขวัญ", new Vector2(0, 2), new Vector2(520, 90), 50, PTVUI.Cream);
        search = PTVUI.Input("Search", c, "", "ค้นหาชื่อของขวัญ...", new Vector2(-150, 335), new Vector2(760, 84), 32);
        search.onValueChanged.AddListener(_ => { page = 0; RenderPage(); });
        PTVUI.Btn("Back", c, "fr_button_close", "กลับ", new Vector2(420, 335), new Vector2(240, 92), 34, () => picker.SetActive(false), 0f);
        grid = PTVUI.Rect("Grid", c, new Vector2(0, -20), new Vector2(1400, 560));
        PTVUI.Btn("Prev", c, "fr_button_close", "‹", new Vector2(-260, -385), new Vector2(150, 84), 54, () => ChangePage(-1), 0f);
        PTVUI.Btn("Next", c, "fr_button_close", "›", new Vector2(260, -385), new Vector2(150, 84), 54, () => ChangePage(1), 0f);
        pageLabel = PTVUI.Txt("Page", c, "", new Vector2(0, -385), new Vector2(320, 80), 26, PTVUI.Cream);
        picker.SetActive(false);
    }

    static void EnsureEventSystem()
    {
        var es = FindFirstObjectByType<EventSystem>();
        if (!es)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(go);
            return;
        }
        if (!es.GetComponent<BaseInputModule>()) es.gameObject.AddComponent<InputSystemUIInputModule>();
    }

    // ------------------------------------------------------------------ loop
    void Update()
    {
        if (!built) return;
        EnsureEventSystem();

        bool wantStart = !SessionStarted;
        if (wantStart != startVisible) { startVisible = wantStart; startPanel.SetActive(wantStart); }

        UIBlocking = StartScreenOpen || SettingsOpen;

        var kb = Keyboard.current;
        if (SettingsOpen && kb != null && kb.escapeKey.wasPressedThisFrame) { if (picker.activeSelf) picker.SetActive(false); else CloseSettings(); }

        gearButton.SetActive(!SettingsOpen);
        if (menuButton) menuButton.SetActive(SessionStarted && !StartScreenOpen && !SettingsOpen);
        langButton.SetActive(StartScreenOpen&&!SettingsOpen);
        if(screenButton)screenButton.SetActive(StartScreenOpen&&!SettingsOpen);
        liveChip.rectTransform.anchoredPosition = PTVScreenLayout.IsPortrait ? new Vector2(30, -134) : new Vector2(140, -56);
        if (langButton.activeSelf) langButton.transform.SetAsLastSibling();
        liveChip.gameObject.SetActive(false);
        gearSpin += Time.unscaledDeltaTime * (IsHover(gearRect) ? 120f : 0f);
        gearRect.localRotation = Quaternion.Euler(0, 0, -gearSpin);

        if (live.State != lastState)
        {
            if (live.State == "connected") PTVAudio.Sfx("connect", 0.6f);
            else if (live.State == "error" || live.State == "ended") PTVAudio.Sfx("error", 0.5f);
            if(SessionStarted&&(live.State=="connected"||live.State=="error"||live.State=="ended")){var notice=UnityEngine.Object.FindFirstObjectByType<PTVGiftNotice>();if(notice)notice.Announce(live.Connected?"เชื่อมต่อไลฟ์แล้ว":live.Detail,Color.white,3);}
            lastState = live.State;
        }
        Color dot = live.Connected ? new Color(0.35f, 0.95f, 0.35f) : live.Busy ? new Color(1f, 0.8f, 0.2f) : live.State == "error" || live.State == "ended" ? new Color(1f, 0.35f, 0.3f) : Color.gray;
        if (StartScreenOpen)
        {
            connectLabel.text = live.Connected ? "เชื่อมแล้ว ✓" : live.Busy ? "กำลังต่อ..." : live.State == "error" ? "ลองใหม่" : "เชื่อมต่อ";
            statusDot.color = dot;
            statusText.text = live.Detail;
            bool empty = string.IsNullOrWhiteSpace(account.text);
            bool sameUser = live.Connected && string.Equals(live.Streamer, account.text.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase);
            play.interactable = empty || sameUser;
            playHint.text = empty ? "ไม่ใส่ ID = เล่นแบบไม่เชื่อมไลฟ์"
                          : sameUser ? "พร้อมแล้ว! กด เริ่มเล่น"
                          : "กด เชื่อมต่อ ให้สำเร็จก่อน จึงจะเริ่มเล่นได้  (หรือลบ ID ออกเพื่อเล่นแบบไม่เชื่อมไลฟ์)";
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) && !SettingsOpen)
            { if (play.interactable) BeginPlay(); else if (!live.Busy) OnConnect(); }
        }
        if (liveChip.gameObject.activeSelf)
        {
            string hex = ColorUtility.ToHtmlStringRGB(dot);
            liveChip.text = live.Connected ? "<color=#" + hex + ">●</color> LIVE  @" + live.Streamer
                          : live.Busy ? "<color=#" + hex + ">●</color> กำลังเชื่อมต่อ..."
                          : string.IsNullOrEmpty(live.Streamer) ? "<color=#" + hex + ">●</color> ไม่ได้เชื่อมไลฟ์"
                          : "<color=#" + hex + ">●</color> " + live.Detail;
        }
    }

    static bool IsHover(RectTransform r)
    {
        var m = Mouse.current; if (m == null || !r) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(r, m.position.ReadValue(), null);
    }

    void RefreshChips()
    {
        var s = gifts.settings.slots;
        for (int row = 0; row < 2; row++)
            for (int k = 0; k < 9; k++)
            {
                var g = rowChipGift[row, k]; if (!g) continue;
                int slot = rowChipSlot[row, k]; g.sprite = slot >= 0 ? PTVGiftCatalog.Icon(s[slot].gift) : null; g.enabled = g.sprite;
            }
        ZHud.I?.RefreshGifts();
    }

    // ------------------------------------------------------------------ actions
    void OnConnect()
    {
        if (live.Connected && string.Equals(live.Streamer, account.text.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase)) return;
        live.ConnectLive(account.text);
    }


    /// <summary>Fills the two skill rows of the start screen for the selected mode, in the same order as the in-game columns.</summary>
    void RefreshStartSkills()
    {
        var rows = new[] { ZEffects.HelperOrder, ZEffects.PrankOrder };
        string[] names = { "ขึ้น ▲", "ลง ▼" };
        for (int row = 0; row < 2; row++)
        {
            if (rowLabels[row]) rowLabels[row].text = names[row];
            var order = rows[row]; bool roomy = order.Length <= 6;
            for (int k = 0; k < 9; k++)
            {
                var chip = rowChips[row, k]; if (!chip) continue;
                bool on = k < order.Length; chip.gameObject.SetActive(on); rowChipSlot[row, k] = on ? (int)order[k] : -1;
                if (!on) continue;
                chip.anchoredPosition = new Vector2(order.Length>8?-320+k*105:roomy ? -300 + k * 156 : -310 + k * 118, row == 0 ? -30 : -165);
                chip.localScale = Vector3.one * (order.Length>8?.76f:roomy ? 1f : 0.84f);
                var e = order[k]; var icon = ZIcons.Effect(e);
                rowChipSkill[row, k].sprite = icon; rowChipSkill[row, k].enabled = icon;
            }
        }
        if (gifts) RefreshChips();
    }

    /// <summary>Leave the round and go back to the start screen (pick the other mode, change the TikTok ID...).</summary>
    public void BackToMenu()
    {
        if (!built) return;
        if (SettingsOpen) CloseSettings();
        SessionStarted = false;
        if (gifts) gifts.ClearPending();
        PTVGameHooks.Restart?.Invoke();   // fresh round on the rooftop, behind the start screen
        RefreshStartSkills();
        PTVAudio.Sfx("close", 0.5f);
    }

    public void BeginPlay()
    {
        if (!StartScreenOpen || !play.interactable) return;
        SessionStarted = true;
        startPanel.SetActive(false); startVisible = false;
        UIBlocking = false;
        PTVAudio.Sfx("start", 0.6f);
    }

    public void OpenSettings()
    {
        if (!built || SettingsOpen) return;
        RefreshModeSettings();
        CaptureComicSound();
        var s = gifts.settings.slots;
        for (int i = 0; i < N; i++)
        {
            draft[i] = s[i].gift;
            likeInputs[i].SetTextWithoutNotify(s[i].rule.likesRequired.ToString());
            triggerChecks[i, 0].SetIsOnWithoutNotify(s[i].rule.gift);
            triggerChecks[i, 1].SetIsOnWithoutNotify(s[i].rule.follow);
            triggerChecks[i, 2].SetIsOnWithoutNotify(s[i].rule.likes);
            triggerChecks[i, 3].SetIsOnWithoutNotify(s[i].rule.orangeHeart);
        }
        RefreshSelections();
        settingsNotice.text = "";
        picker.SetActive(false);
        settingsPanel.SetActive(true);
        PTVAudio.Sfx("open", 0.5f);
        settingsPanel.transform.SetAsLastSibling();
        UIBlocking = true;
        if (!StartScreenOpen && Time.timeScale > 0f) { Time.timeScale = 0f; pausedByMe = true; }
    }

    void RefreshModeSettings()
    {
        for (int i = 0; i < N; i++)
        {
            var effect = (ZEffect)i;
            var sprite = ZIcons.Effect(effect);
            settingEffectIcons[i].sprite = sprite; settingEffectIcons[i].enabled = sprite;
            var name=settingEffectNames[i];name.text=ZEffects.Names[i]+"  "+ZEffects.MetreLabel(effect);
            name.rectTransform.anchoredPosition=new Vector2(-268,18);name.rectTransform.sizeDelta=new Vector2(170,30);
            name.horizontalOverflow=HorizontalWrapMode.Wrap;name.verticalOverflow=VerticalWrapMode.Truncate;
            name.resizeTextForBestFit=true;name.resizeTextMinSize=11;name.resizeTextMaxSize=22;
            var info=settingEffectInfo[i];info.text=ZEffects.Info[i];info.rectTransform.anchoredPosition=new Vector2(-268,-16);
            info.horizontalOverflow=HorizontalWrapMode.Wrap;info.verticalOverflow=VerticalWrapMode.Truncate;
            info.resizeTextForBestFit=true;info.resizeTextMinSize=10;info.resizeTextMaxSize=15;
        }
        RefreshModeControls();
    }

    public void EditSkillLayout()
    {
        // positions are now dragged directly on the game screen; settings only hold the skill sizes
        SetComicSettingsTab(2);
    }

    public void CloseSettings()
    {
        FinishComicSound();
        picker.SetActive(false);
        if (settingsPanel.activeSelf) PTVAudio.Sfx("close", 0.45f);
        settingsPanel.SetActive(false);
        if (pausedByMe) { Time.timeScale = 1f; pausedByMe = false; }
        if (!StartScreenOpen) UIBlocking = false;
        RefreshChips();
    }

    void LoadDefaults()
    {
        var d = ZGiftDirector.Settings.Defaults();
        for (int i = 0; i < N; i++)
        {
            draft[i] = d[i].gift;
            likeInputs[i].SetTextWithoutNotify(d[i].rule.likesRequired.ToString());
            triggerChecks[i, 0].SetIsOnWithoutNotify(d[i].rule.gift);
            triggerChecks[i, 1].SetIsOnWithoutNotify(d[i].rule.follow);
            triggerChecks[i, 2].SetIsOnWithoutNotify(d[i].rule.likes);
            triggerChecks[i, 3].SetIsOnWithoutNotify(d[i].rule.orangeHeart);
        }
        RefreshSelections();
        settingsNotice.text = "คืนค่าเริ่มต้นแล้ว (กด บันทึก เพื่อใช้)";
    }

    public void SaveAndClose()
    {
        var slots = new ZGiftDirector.Slot[N];
        for (int i = 0; i < N; i++)
        {
            if (!int.TryParse(likeInputs[i].text, out int likes) || likes < 1)
            { settingsNotice.text = ZEffects.Names[i] + ": จำนวนเคาะใจต้องเป็นเลขจำนวนเต็มบวก"; return; }
            slots[i] = new ZGiftDirector.Slot
            {
                gift = draft[i],
                rule = new ZGiftDirector.TriggerRule
                {
                    gift = triggerChecks[i, 0].isOn, follow = triggerChecks[i, 1].isOn, likes = triggerChecks[i, 2].isOn,
                    orangeHeart = triggerChecks[i, 3].isOn, likesRequired = likes
                }
            };
        }
        bool stalkInvalid;bool stalkChanged=CommitStalk(out stalkInvalid);
        if(stalkInvalid){SetComicSettingsTab(3);settingsNotice.text="ความสูงต้นถั่วต้องอยู่ระหว่าง 100 ถึง 5000 เมตร";return;}
        gifts.settings.slots = slots;
        gifts.SaveSettings();
        CommitComicSound();
        CloseSettings();
        if(stalkChanged)PTVGameHooks.Restart?.Invoke();   // rebuild the stalk at its new height
    }

    // ------------------------------------------------------------------ gift picker
    string[] Filtered()
    {
        string q = search ? (search.text ?? "").Trim() : "";
        return PTVGiftCatalog.Names
            .Where(n => q.Length == 0 || n.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || PTVGiftCatalog.ListedName(n).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(n => string.Equals(n, draft[selecting], StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(n => PTVGiftCatalog.Price(n)).ThenBy(n => n).ToArray();
    }

    public void OpenGiftPicker(int slot)
    {
        selecting = Mathf.Clamp(slot, 0, N - 1); page = 0;
        search.SetTextWithoutNotify("");
        picker.SetActive(true); picker.transform.SetAsLastSibling();
        RenderPage();
    }

    public void ChangePage(int dir) { page = Mathf.Clamp(page + dir, 0, Mathf.Max(0, (Filtered().Length - 1) / 12)); RenderPage(); }

    int UsedBy(string name) { for (int s = 0; s < N; s++) if (s != selecting && string.Equals(draft[s], name, StringComparison.OrdinalIgnoreCase)) return s; return -1; }

    void RenderPage()
    {
        foreach (var cell in cells) { cell.SetActive(false); Destroy(cell); }
        cells.Clear();
        var names = Filtered();
        int pages = Mathf.Max(1, (names.Length + 11) / 12);
        page = Mathf.Clamp(page, 0, pages - 1);
        pageLabel.text = (page + 1) + " / " + pages + "\n<size=20>" + names.Length + " ของขวัญ</size>";
        for (int i = 0; i < 12 && page * 12 + i < names.Length; i++)
        {
            string name = names[page * 12 + i];
            bool current = string.Equals(name, draft[selecting], StringComparison.OrdinalIgnoreCase);
            int usedBy = UsedBy(name); bool used = usedBy >= 0;
            var tile = PTVUI.Btn("Gift_" + name, grid, "fr_xp_card", null, new Vector2((i % 4 - 1.5f) * 345, 190 - (i / 4) * 190), new Vector2(330, 178), 0, () => ChooseGift(name), 2.2f);
            tile.GetComponent<PTVHoverScale>().amount = 0.05f;
            tile.interactable = !used;
            if (current) { var o = tile.gameObject.AddComponent<Outline>(); o.effectColor = PTVUI.Gold; o.effectDistance = new Vector2(5, -5); }
            cells.Add(tile.gameObject);
            var im = PTVUI.Img("Icon", tile.transform, null, new Vector2(0, 26), new Vector2(100, 100)); im.sprite = PTVGiftCatalog.Icon(name); im.preserveAspect = true;
            if (used) im.color = new Color(1, 1, 1, 0.35f);
            var nm = PTVUI.Txt("Name", tile.transform, PTVGiftCatalog.ListedName(name), new Vector2(0, -44), new Vector2(310, 36), 23, PTVUI.Cream);
            nm.horizontalOverflow = HorizontalWrapMode.Overflow;
            PTVUI.Txt("Price", tile.transform, used ? "ใช้แล้ว: " + ZEffects.Names[usedBy] : current ? "✓ เลือกอยู่" : PTVGiftCatalog.Price(name).ToString("N0") + " เหรียญ",
                      new Vector2(0, -70), new Vector2(310, 30), 19, current ? PTVUI.Gold : PTVUI.Muted);
        }
    }

    public void ChooseGift(string name)
    {
        if (!PTVGiftCatalog.Contains(name) || UsedBy(name) >= 0) return;
        draft[selecting] = name;
        RefreshSelections();
        picker.SetActive(false);
    }

    void RefreshSelections()
    {
        for (int i = 0; i < N; i++)
        {
            giftIcons[i].sprite = PTVGiftCatalog.Icon(draft[i]); giftIcons[i].enabled = giftIcons[i].sprite;
            giftNames[i].text = PTVGiftCatalog.ListedName(draft[i]);
            giftPrices[i].text = PTVGiftCatalog.Price(draft[i]).ToString("N0") + " เหรียญ";
        }
    }

    void OnDestroy() { UIBlocking = false; PTVLang.Changed -= RefreshChips; }
}
