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
/// Same flow as PTVegg's menu, adapted to the grilled-squid stall (6 slots, each with a fixed stall action).
/// </summary>
[DisallowMultipleComponent]
public sealed class PTVStartMenu : MonoBehaviour
{
    const int N = SquidGiftDirector.SlotCount;
    /// <summary>True while the start screen or the settings are on screen.</summary>
    public static bool UIBlocking { get; private set; }
    /// <summary>After the first PLAY, restarts skip the start screen.</summary>
    public static bool SessionStarted { get; private set; }

    PTVTikTokLive live; SquidGiftDirector gifts; SquidGame game;
    Canvas canvas;
    GameObject startPanel, settingsPanel, picker, gearButton;
    GameObject screenButton; Text screenLabel;
    void RefreshScreenLabel() { if (screenLabel) screenLabel.text = PTVScreenLayout.PortraitSetting ? "จอแนวตั้ง 9:16" : "จอแนวนอน 16:9"; }
    RectTransform gearRect;
    InputField account, search;
    Button connect, play;
    Text connectLabel, statusText, playHint, liveChip, settingsNotice, pageLabel;
    Image statusDot;
    static readonly string[] HomeIcons = { "icon_grill", "icon_grill", "icon_grill", "icon_grill", "icon_grill", "icon_wheel" };
    static readonly string[] HomeLabels = { "ผู้ช่วย 4 ระดับ", "ปลาหมึกพร้อมน้ำจิ้ม", "แกล้ง 5 ระดับ", "กำหนดจำนวนต่อชุด", "ทำทุกออเดอร์", "สุ่ม เล็ก/กลาง/ใหญ่" };
    readonly Image[] chipIcons = new Image[6];
    readonly Text[] chipTexts = new Text[6];
    // settings
    readonly string[] draft = new string[N];
    readonly Image[] giftIcons = new Image[N];
    readonly Text[] giftNames = new Text[N], giftPrices = new Text[N];
    readonly InputField[] amountInputs = new InputField[N], likeInputs = new InputField[N];
    readonly Toggle[,] triggerChecks = new Toggle[N, 4];
    readonly List<GameObject> cells = new List<GameObject>();
    RectTransform grid;
    int selecting, page;
    bool built, startVisible, pausedByMe, homePaused;
    float homeTimeScale = 1f;
    float gearSpin;
    GameObject langButton;
    string lastState = "idle";

    public bool SettingsOpen => settingsPanel && settingsPanel.activeSelf;
    public bool StartScreenOpen => startPanel && startPanel.activeSelf;

    static readonly Color Panel = new Color(0.32f, 0.17f, 0.07f, 0.96f);

    void Start()
    {
        live = GetComponent<PTVTikTokLive>();
        gifts = GetComponent<SquidGiftDirector>();
        Build();
    }

    // ------------------------------------------------------------------ build
    void Build()
    {
        EnsureEventSystem();
        canvas = PTVUI.MakeCanvas("PTV_LiveMenu", 300, transform);
        var root = canvas.transform;

        // ---------- start screen (the stall stays visible behind a dark shade)
        startPanel = PTVUI.Rect("StartScreen", root, Vector2.zero, Vector2.zero).gameObject; PTVUI.Stretch((RectTransform)startPanel.transform);
        var block = startPanel.AddComponent<Image>(); block.color = new Color(0.05f, 0.02f, 0f, 0.55f);

        var header = PTVUI.Img("TitleHeader", startPanel.transform, "fr_title_header", new Vector2(0, 405), new Vector2(1040, 320));
        header.preserveAspect = true;
        PTVUI.Txt("Title", header.transform, "ปลาหมึกย่าง", new Vector2(100, 15), new Vector2(620, 120), 82, PTVUI.Cream);

        var card = PTVUI.Img("ConnectCard", startPanel.transform, "fr_skill_card", new Vector2(0, -130), new Vector2(1120, 720), null, 1f);
        if (!card.sprite) card.color = Panel;
        var c = card.transform;
        PTVUI.Txt("Subtitle", c, "เชื่อมต่อไลฟ์ก่อนเริ่มขาย", new Vector2(0, 235), new Vector2(960, 36), 26, PTVUI.Ink, TextAnchor.MiddleCenter, false);
        PTVUI.Txt("IdLabel", c, "TikTok LIVE", new Vector2(0, 295), new Vector2(900, 64), 48, PTVUI.Ink);
        account = PTVUI.Input("Account", c, PlayerPrefs.GetString(PTVTikTokLive.UsernameKey, ""), "@ ชื่อบัญชี TikTok", new Vector2(-150, 165), new Vector2(600, 96), 38);
        account.characterLimit = 64;
        connect = PTVUI.Btn("Connect", c, "fr_button_reset", "เชื่อมต่อ", new Vector2(320, 165), new Vector2(290, 112), 38, OnConnect, 0f);
        connectLabel = connect.GetComponentInChildren<Text>();
        statusDot = PTVUI.Img("StatusDot", c, null, new Vector2(-420, 88), new Vector2(22, 22), Color.gray);
        statusText = PTVUI.Txt("Status", c, "", new Vector2(10, 88), new Vector2(820, 40), 24, PTVUI.Cream, TextAnchor.MiddleLeft);

        // Home presents food actions, independently of the viewer's selected gifts.
        for (int k = 0; k < HomeIcons.Length; k++)
        {
            float x = -350 + (k % 3) * 350, y = -25 - (k / 3) * 120;
            var tile = PTVUI.Img("ActionTile" + k, c, "fr_status_pill", new Vector2(x, y), new Vector2(332, 106), null, 2.4f);
            chipIcons[k] = PTVUI.Img("ActionIcon", tile.transform, HomeIcons[k], new Vector2(-106, 0), new Vector2(94, 94));
            chipIcons[k].preserveAspect = true;
            chipTexts[k] = PTVUI.Txt("ActionLabel", tile.transform, HomeLabels[k], new Vector2(46, 0), new Vector2(208, 82), 30, PTVUI.Ink);
            chipTexts[k].resizeTextForBestFit = true;
            chipTexts[k].resizeTextMinSize = 24; chipTexts[k].resizeTextMaxSize = 30;
        }
        var setup = PTVUI.Btn("HomeGiftSettings", c, "fr_button_close", "ตั้งค่าของขวัญ", new Vector2(-270, -265), new Vector2(440, 104), 34, OpenSettings, 0f);
        var setupText = setup.GetComponentInChildren<Text>();
        setupText.resizeTextMinSize = 22; setupText.resizeTextMaxSize = 34; setupText.resizeTextForBestFit = true;
        play = PTVUI.Btn("Play", c, "fr_button_reset", "เปิดร้าน", new Vector2(250, -265), new Vector2(480, 104), 48, BeginPlay, 0f);
        var trial = PTVUI.Btn("TryOffline", c, null, "ทดลองเล่น", new Vector2(0, -335), new Vector2(260, 38), 26, BeginTrial);
        ((Image)trial.targetGraphic).color = Color.clear;
        var trialLabel = trial.GetComponentInChildren<Text>(); trialLabel.color = PTVUI.Ink;
        if (trialLabel is PTVText) ((PTVText)trialLabel).translate = true;
        playHint = PTVUI.Txt("PlayHint", startPanel.transform, "", new Vector2(0, -540), new Vector2(1400, 40), 24, PTVUI.Cream);

        PTVScreenLayout.Fit((RectTransform)startPanel.transform, 1180f, 1180f);

        // ---------- gear (top-left)
        var gear = PTVUI.Btn("SettingsGear", root, "Gear", null, Vector2.zero, new Vector2(104, 104), 0, OpenSettings, 0f);
        gearRect = (RectTransform)gear.transform; gearRect.anchorMin = gearRect.anchorMax = new Vector2(0f, 1f);
        gearRect.pivot = new Vector2(0.5f, 0.5f);
        gearRect.anchoredPosition = new Vector2(26 + 52, -24 - 52);
        ((Image)gear.targetGraphic).preserveAspect = true;
        gear.GetComponent<PTVHoverScale>().amount = 0.12f;
        gearButton = gear.gameObject;
        liveChip = PTVUI.Txt("LiveChip", root, "", Vector2.zero, new Vector2(520, 40), 26, PTVUI.Cream, TextAnchor.MiddleLeft);
        var lr = liveChip.rectTransform; lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0f, 1f); lr.anchoredPosition = new Vector2(140, -56);

        BuildSettings(root);
        BuildPicker(settingsPanel.transform);
        settingsPanel.SetActive(false);
        PTVScreenLayout.Fit((RectTransform)settingsPanel.transform, 1800f, 1420f);

        var lang = PTVUI.Btn("Language", root, "fr_button_close", "ภาษา: ไทย", Vector2.zero, new Vector2(300, 92), 30, () => PTVLang.Toggle(), 0f);
        var lr2 = (RectTransform)lang.transform; lr2.anchorMin = lr2.anchorMax = new Vector2(1f, 1f); lr2.pivot = new Vector2(0.5f, 0.5f);
        lr2.anchoredPosition = new Vector2(-24 - 150, -24 - 46);
        langButton = lang.gameObject;
        var scr = PTVUI.Btn("ScreenMode", root, "fr_button_close", "จอแนวตั้ง 9:16", Vector2.zero, new Vector2(300, 92), 26, () => { PTVScreenLayout.Toggle(); RefreshScreenLabel(); }, 0f);
        var sr = (RectTransform)scr.transform; sr.anchorMin = sr.anchorMax = new Vector2(1f, 1f); sr.pivot = new Vector2(0.5f, 0.5f);
        sr.anchoredPosition = new Vector2(-24 - 150, -24 - 46 - 104);
        screenButton = scr.gameObject; screenLabel = scr.GetComponentInChildren<Text>();
        RefreshScreenLabel();
        PTVLang.Changed += RefreshLanguage;
        gifts.SettingsChanged += RefreshChips;
        built = true;
        RefreshChips();
    }

    void BuildSettings(Transform root)
    {
        settingsPanel = PTVUI.Rect("GiftSettings", root, Vector2.zero, Vector2.zero).gameObject; PTVUI.Stretch((RectTransform)settingsPanel.transform);
        var dim = settingsPanel.AddComponent<Image>(); dim.color = new Color(0.03f, 0.02f, 0f, 0.72f);
        var card = PTVUI.Img("SettingsCard", settingsPanel.transform, "fr_skill_card", new Vector2(0, -40), new Vector2(1720, 1120), null, 1f);
        if (!card.sprite) card.color = Panel;
        var c = card.transform;
        var header = PTVUI.Img("Header", settingsPanel.transform, "fr_title_header", new Vector2(0, 540), new Vector2(700, 272));
        header.preserveAspect = true;
        PTVUI.Txt("Heading", header.transform, "ตั้งค่าของขวัญ", new Vector2(65, 12), new Vector2(420, 90), 44, PTVUI.Cream);
        var close = PTVUI.Btn("CloseX", c, "ct_close_x", null, new Vector2(800, 510), new Vector2(92, 92), 0, CloseSettings, 0f);
        ((Image)close.targetGraphic).preserveAspect = true;

        PTVUI.Txt("ColGift", c, "รูปของขวัญ (คลิกเพื่อเปลี่ยน)", new Vector2(-560, 420), new Vector2(420, 44), 26, PTVUI.Muted);
        PTVUI.Txt("ColAction", c, "ผลในร้าน (จำนวนต่อครั้ง)", new Vector2(-170, 420), new Vector2(420, 44), 26, PTVUI.Muted);
        PTVUI.Txt("ColTrigger", c, "เงื่อนไข", new Vector2(450, 420), new Vector2(600, 44), 26, PTVUI.Muted);

        string[] trig = { "ส่งของขวัญ", "กดติดตาม", "เคาะใจ", "หัวใจสีส้ม" };
        float[] boxX = { 80, 255, 425, 700 };
        for (int i = 0; i < N; i++)
        {
            int slot = i; int rowIndex = i < 5 ? i : i < 10 ? i - 5 : i - 10; float y = 330 - rowIndex * 128;
            var row = PTVUI.Img("Row" + i, c, "fr_status_pill", new Vector2(0, y), new Vector2(1640, 118), null, 1.6f);
            if (!row.sprite) row.color = new Color(0.15f, 0.12f, 0.05f, 0.9f);
            var rt = row.transform;
            PTVUI.Txt("Num", rt, (i + 1).ToString(), new Vector2(-765, 0), new Vector2(60, 60), 40, PTVUI.Gold);

            var choice = PTVUI.Btn("GiftChoice" + i, rt, "fr_xp_card", null, new Vector2(-580, 0), new Vector2(300, 104), 0, () => OpenGiftPicker(slot), 2.6f);
            choice.GetComponent<PTVHoverScale>().amount = 0.04f;
            giftIcons[i] = PTVUI.Img("Icon", choice.transform, null, new Vector2(-95, 0), new Vector2(80, 80)); giftIcons[i].preserveAspect = true;
            giftNames[i] = PTVUI.Txt("Name", choice.transform, "", new Vector2(45, 14), new Vector2(200, 40), 24, PTVUI.Cream);
            giftNames[i].resizeTextForBestFit = true; giftNames[i].resizeTextMinSize = 16; giftNames[i].resizeTextMaxSize = 24;
            giftPrices[i] = PTVUI.Txt("Price", choice.transform, "", new Vector2(45, -22), new Vector2(200, 34), 20, PTVUI.Muted);

            rows[i] = row.gameObject;
            var act = SquidGiftDirector.SlotAction(i);
            string name = SquidGiftDirector.SlotName(i);
            var col = SquidGiftDirector.IsHelper(act) ? new Color(0.6f, 1f, 0.55f) : act == SquidAction.Customer ? new Color(1f, 0.6f, 0.5f)
                    : act == SquidAction.Wheel ? new Color(1f, 0.85f, 0.35f) : new Color(0.75f, 0.8f, 1f);
            var actionLabel = PTVUI.Txt("Action", rt, name, new Vector2(-305, 0), new Vector2(230, 50), 24, col);
            actionLabel.resizeTextForBestFit = true; actionLabel.resizeTextMinSize = 16; actionLabel.resizeTextMaxSize = 24;
            amountInputs[i] = PTVUI.Input("Amount" + i, rt, "", "1", new Vector2(-65, 0), new Vector2(180, 76), 28);
            amountInputs[i].contentType = InputField.ContentType.IntegerNumber; amountInputs[i].characterLimit = 3;
            if (!SquidGiftDirector.HasAmount(act))
            {   // serves every waiting order at that moment - no number to set
                amountInputs[i].gameObject.SetActive(false);
                actionLabel.rectTransform.anchoredPosition = new Vector2(-185, 0); actionLabel.rectTransform.sizeDelta = new Vector2(420, 50);
            }

            for (int s = 0; s < 4; s++)
            {
                triggerChecks[i, s] = PTVUI.Check("Trigger" + i + "_" + s, rt, new Vector2(boxX[s], 0), 42);
                var lab = PTVUI.Txt("TrigLabel" + s, rt, trig[s], new Vector2(boxX[s] + 85, 0), new Vector2(110, 44), 22, s == 3 ? new Color(1f, 0.7f, 0.35f) : PTVUI.Cream, TextAnchor.MiddleLeft);
                lab.horizontalOverflow = HorizontalWrapMode.Wrap; lab.resizeTextForBestFit = true; lab.resizeTextMinSize = 16; lab.resizeTextMaxSize = 22;
            }
            likeInputs[i] = PTVUI.Input("Likes" + i, rt, "100", "100", new Vector2(575, 0), new Vector2(100, 68), 26);
            likeInputs[i].contentType = InputField.ContentType.IntegerNumber; likeInputs[i].characterLimit = 6;
            PTVUI.Txt("LikesUnit", rt, "ครั้ง", new Vector2(650, 0), new Vector2(62, 44), 18, PTVUI.Cream);
        }
        settingsNotice = PTVUI.Txt("Notice", c, "", new Vector2(0, -448), new Vector2(1200, 40), 24, new Color(1f, 0.6f, 0.45f));
        PTVUI.Btn("SetPrev", c, "fr_button_close", "‹", new Vector2(-350, -395), new Vector2(110, 70), 46, () => SetPage(setPage - 1), 0f);
        PTVUI.Btn("SetNext", c, "fr_button_close", "›", new Vector2(350, -395), new Vector2(110, 70), 46, () => SetPage(setPage + 1), 0f);
        setPageLabel = PTVUI.Txt("SetPage", c, "", new Vector2(0, -395), new Vector2(500, 60), 28, PTVUI.Cream);
        SetPage(0);
        PTVUI.Btn("HudSettings", c, "fr_button_close", "แถบสกิล", new Vector2(-210, -500), new Vector2(330, 100), 32,
            () => { if (SquidSkillHud.I) SquidSkillHud.I.OpenSettings(); }, 0f);
        PTVUI.Btn("Defaults", c, "fr_button_close", "ค่าเริ่มต้น", new Vector2(-560, -500), new Vector2(280, 100), 32, LoadDefaults, 0f);
        PTVUI.Btn("Cancel", c, "fr_button_close", "ยกเลิก", new Vector2(150, -500), new Vector2(280, 100), 34, CloseSettings, 0f);
        PTVUI.Btn("Save", c, "fr_button_reset", "บันทึก", new Vector2(510, -500), new Vector2(360, 116), 40, SaveAndClose, 0f);
    }

    void BuildPicker(Transform root)
    {
        picker = PTVUI.Rect("GiftPicker", root, Vector2.zero, Vector2.zero).gameObject; PTVUI.Stretch((RectTransform)picker.transform);
        var dim = picker.AddComponent<Image>(); dim.color = new Color(0.02f, 0.01f, 0f, 0.8f);
        var card = PTVUI.Img("PickerCard", picker.transform, "fr_skill_card", new Vector2(0, -30), new Vector2(1560, 930), null, 1f);
        if (!card.sprite) card.color = Panel;
        var c = card.transform;
        var header = PTVUI.Img("Header", picker.transform, "fr_title_header", new Vector2(0, 440), new Vector2(640, 249));
        header.preserveAspect = true;
        PTVUI.Txt("Title", header.transform, "เลือกของขวัญ", new Vector2(65, 12), new Vector2(380, 90), 42, PTVUI.Cream);
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
        if (!game) { game = FindFirstObjectByType<SquidGame>(); EnsureEventSystem(); }

        bool wantStart = game && game.IsWaiting && !SessionStarted;
        if (game && game.IsWaiting && SessionStarted) game.BeginRound();
        if (wantStart != startVisible) { startVisible = wantStart; startPanel.SetActive(wantStart); }

        UIBlocking = StartScreenOpen || SettingsOpen;

        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) HandleEscape();

        gearButton.SetActive(!SettingsOpen);
        langButton.SetActive(StartScreenOpen || SettingsOpen);
        if (screenButton) screenButton.SetActive(StartScreenOpen && !SettingsOpen);
        liveChip.rectTransform.anchoredPosition = PTVScreenLayout.IsPortrait ? new Vector2(30, -134) : new Vector2(140, -56);
        if (langButton.activeSelf) langButton.transform.SetAsLastSibling();
        liveChip.gameObject.SetActive(!SettingsOpen && !StartScreenOpen);
        gearSpin += Time.unscaledDeltaTime * (IsHover(gearRect) ? 120f : 0f);
        gearRect.localRotation = Quaternion.Euler(0, 0, -gearSpin);

        if (live.State != lastState)
        {
            if (live.State == "connected") PTVAudio.Sfx("connect", 0.6f);
            else if (live.State == "error" || live.State == "ended") PTVAudio.Sfx("error", 0.5f);
            lastState = live.State;
        }
        Color dot = live.Connected ? new Color(0.35f, 0.95f, 0.35f) : live.Busy ? new Color(1f, 0.8f, 0.2f) : live.State == "error" || live.State == "ended" ? new Color(1f, 0.35f, 0.3f) : Color.gray;
        if (StartScreenOpen)
        {
            connectLabel.text = live.Connected ? "เชื่อมแล้ว ✓" : live.Busy ? "กำลังต่อ..." : live.State == "error" ? "ลองใหม่" : "เชื่อมต่อ";
            statusDot.color = dot;
            statusText.text = PTVLang.Status(live.State, live.Detail);
            bool empty = string.IsNullOrWhiteSpace(account.text);
            bool sameUser = live.Connected && string.Equals(live.Streamer, account.text.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase);
            play.interactable = empty || sameUser;
            playHint.text = empty ? "เว้นชื่อบัญชีว่างไว้เพื่อเล่นออฟไลน์  ·  ทดสอบ: กด 1-6"
                          : sameUser ? "พร้อมแล้ว! กด เปิดร้าน!"
                          : "เชื่อมต่อให้สำเร็จก่อนเปิดร้าน หรือกดทดลองเล่นเพื่อเล่นออฟไลน์";
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) && !SettingsOpen)
            { if (play.interactable) BeginPlay(); else if (!live.Busy) OnConnect(); }
        }
        if (liveChip.gameObject.activeSelf)
        {
            string hex = ColorUtility.ToHtmlStringRGB(dot);
            liveChip.text = live.Connected ? "<color=#" + hex + ">●</color> ไลฟ์ @" + live.Streamer
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
        for (int i = 0; i < HomeIcons.Length; i++)
        {
            if (chipIcons[i]) { chipIcons[i].sprite = PTVUI.Spr(HomeIcons[i]); chipIcons[i].enabled = chipIcons[i].sprite != null; }
            if (chipTexts[i]) chipTexts[i].text = HomeLabels[i];
        }
    }

    void RefreshLanguage()
    {
        if (!built) return;
        RefreshChips();
        RefreshScreenLabel();
        if (statusText) statusText.text = PTVLang.Status(live.State, live.Detail);
        SetPage(setPage);
        if (SettingsOpen) RefreshSelections();
        if (picker && picker.activeSelf) RenderPage();
    }

    void BeginTrial()
    {
        live.DisconnectLive();
        account.SetTextWithoutNotify("");
        play.interactable = true;
        BeginPlay();
    }

    // ------------------------------------------------------------------ actions
    void OnConnect()
    {
        if (live.Connected && string.Equals(live.Streamer, account.text.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase)) return;
        live.ConnectLive(account.text);
    }

    public void BeginPlay()
    {
        if (!StartScreenOpen || !play.interactable) return;
        PTVAudio.Sfx("start", 0.7f);
        SessionStarted = true;
        startPanel.SetActive(false); startVisible = false;
        UIBlocking = false;
        if (homePaused) { Time.timeScale = homeTimeScale; homePaused = false; }
        if (game) game.BeginRound();
    }

    public void HandleEscape()
    {
        if (!built) return;
        if (SquidSkillHud.I && SquidSkillHud.I.SettingsOpen) { SquidSkillHud.I.CloseSettings(); return; }
        if (SettingsOpen) { if (picker.activeSelf) picker.SetActive(false); else CloseSettings(); return; }
        if (!StartScreenOpen) ReturnToStart();
    }

    public void ReturnToStart()
    {
        if (!built || StartScreenOpen) return;
        if (SettingsOpen) CloseSettings();
        if (SquidSkillHud.I) SquidSkillHud.I.CloseSettings();
        SessionStarted = false;
        if (game) game.SuspendRound();
        homeTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        homePaused = true; Time.timeScale = 0f;
        startVisible = true; startPanel.SetActive(true); UIBlocking = true;
        if (live.Connected) account.SetTextWithoutNotify(live.Streamer);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        PTVAudio.Sfx("open", 0.5f);
    }

    void FillRow(int i, SquidGiftDirector.Slot s)
    {
        draft[i] = s.gift;
        amountInputs[i].SetTextWithoutNotify(s.min.ToString());
        likeInputs[i].SetTextWithoutNotify(s.rule.likesRequired.ToString());
        triggerChecks[i, 0].SetIsOnWithoutNotify(s.rule.gift);
        triggerChecks[i, 1].SetIsOnWithoutNotify(s.rule.follow);
        triggerChecks[i, 2].SetIsOnWithoutNotify(s.rule.likes);
        triggerChecks[i, 3].SetIsOnWithoutNotify(s.rule.orangeHeart);
    }

    const int PerPage = 6;
    readonly GameObject[] rows = new GameObject[N];
    int setPage; Text setPageLabel;
    int SetPages => 3;
    void SetPage(int p)
    {
        setPage = (p % SetPages + SetPages) % SetPages;
        for (int i = 0; i < N; i++) if (rows[i]) rows[i].SetActive((i < 5 ? 0 : i < 10 ? 1 : 2) == setPage);
        if (setPageLabel) setPageLabel.text = (setPage == 0 ? "ผู้ช่วย" : setPage == 1 ? "แกล้ง" : "สุ่มช่วย/แกล้ง") + "  " + (setPage + 1) + "/" + SetPages;
    }

    public void OpenSettings()
    {
        if (!built || SettingsOpen) return;
        var s = gifts.settings.slots;
        for (int i = 0; i < N; i++) FillRow(i, s[i]);
        RefreshSelections();
        settingsNotice.text = "";
        picker.SetActive(false);
        settingsPanel.SetActive(true);
        PTVAudio.Sfx("open", 0.5f);
        settingsPanel.transform.SetAsLastSibling();
        UIBlocking = true;
        if (!StartScreenOpen && Time.timeScale > 0f) { Time.timeScale = 0f; pausedByMe = true; }
    }

    public void CloseSettings()
    {
        picker.SetActive(false);
        if (settingsPanel.activeSelf) PTVAudio.Sfx("close", 0.45f);
        settingsPanel.SetActive(false);
        if (pausedByMe) { Time.timeScale = 1f; pausedByMe = false; }
        if (!StartScreenOpen) UIBlocking = false;
        RefreshChips();
    }

    void LoadDefaults()
    {
        var d = SquidGiftDirector.Settings.Defaults();
        for (int i = 0; i < N; i++) FillRow(i, d[i]);
        RefreshSelections();
        settingsNotice.text = "คืนค่าเริ่มต้นแล้ว (กด บันทึก เพื่อใช้)";
    }

    public void SaveAndClose()
    {
        var slots = new SquidGiftDirector.Slot[N];
        for (int i = 0; i < N; i++)
        {
            if (!int.TryParse(amountInputs[i].text, out int amount) || amount < 1 || amount > 999)
            { settingsNotice.text = "ช่อง " + (i + 1) + ": จำนวนต้องเป็นเลข 1 ถึง 999"; return; }
            if (!int.TryParse(likeInputs[i].text, out int likes) || likes < 1)
            { settingsNotice.text = "ช่อง " + (i + 1) + ": จำนวนเคาะใจต้องเป็นเลขจำนวนเต็มบวก"; return; }
            slots[i] = new SquidGiftDirector.Slot
            {
                gift = draft[i], min = amount, max = amount,
                rule = new SquidGiftDirector.TriggerRule
                {
                    gift = triggerChecks[i, 0].isOn, follow = triggerChecks[i, 1].isOn, likes = triggerChecks[i, 2].isOn,
                    orangeHeart = triggerChecks[i, 3].isOn, likesRequired = likes
                }
            };
        }
        gifts.settings.slots = slots;
        gifts.SaveSettings();
        CloseSettings();
    }

    // ------------------------------------------------------------------ gift picker
    string[] Filtered()
    {
        string q = search ? (search.text ?? "").Trim() : "";
        return PTVGiftCatalog.Names
            .Where(n => q.Length == 0 || n.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || PTVGiftCatalog.ListedName(n).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || PTVGiftCatalog.DisplayName(n).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
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
            int usedBy = Enumerable.Range(0, N).FirstOrDefault(s => s != selecting && string.Equals(draft[s], name, StringComparison.OrdinalIgnoreCase));
            bool used = Enumerable.Range(0, N).Any(s => s != selecting && string.Equals(draft[s], name, StringComparison.OrdinalIgnoreCase));
            var tile = PTVUI.Btn("Gift_" + name, grid, "fr_xp_card", null, new Vector2((i % 4 - 1.5f) * 345, 190 - (i / 4) * 190), new Vector2(330, 178), 0, () => ChooseGift(name), 2.2f);
            tile.GetComponent<PTVHoverScale>().amount = 0.05f;
            tile.interactable = !used;
            if (current) { var o = tile.gameObject.AddComponent<Outline>(); o.effectColor = PTVUI.Gold; o.effectDistance = new Vector2(5, -5); }
            cells.Add(tile.gameObject);
            var im = PTVUI.Img("Icon", tile.transform, null, new Vector2(0, 26), new Vector2(100, 100)); im.sprite = PTVGiftCatalog.Icon(name); im.preserveAspect = true;
            if (used) im.color = new Color(1, 1, 1, 0.35f);
            var nm = PTVUI.Txt("Name", tile.transform, PTVGiftCatalog.DisplayName(name), new Vector2(0, -44), new Vector2(310, 36), 23, PTVUI.Cream);
            nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 16; nm.resizeTextMaxSize = 23;
            PTVUI.Txt("Price", tile.transform, used ? "ใช้แล้วในช่อง " + (usedBy + 1) : current ? "✓ เลือกอยู่" : PTVGiftCatalog.PriceLabel(name),
                      new Vector2(0, -70), new Vector2(310, 30), 19, current ? PTVUI.Gold : PTVUI.Muted);
        }
    }

    public void ChooseGift(string name)
    {
        if (!PTVGiftCatalog.Contains(name)) return;
        if (Enumerable.Range(0, N).Any(s => s != selecting && string.Equals(draft[s], name, StringComparison.OrdinalIgnoreCase))) return;
        draft[selecting] = name;
        RefreshSelections();
        picker.SetActive(false);
    }

    void RefreshSelections()
    {
        for (int i = 0; i < N; i++)
        {
            giftIcons[i].sprite = PTVGiftCatalog.Icon(draft[i]); giftIcons[i].enabled = giftIcons[i].sprite;
            giftNames[i].text = PTVGiftCatalog.DisplayName(draft[i]);
            giftPrices[i].text = PTVGiftCatalog.PriceLabel(draft[i]);
        }
    }

    void OnDestroy() { UIBlocking = false; PTVLang.Changed -= RefreshLanguage; if (gifts) gifts.SettingsChanged -= RefreshChips; }
}




