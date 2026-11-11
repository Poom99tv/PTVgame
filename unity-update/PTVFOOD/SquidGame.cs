using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Grilled-squid stall for TikTok Live - the streamer plays it with the mouse:
///  GRILL : click the ice box -> a squid goes onto the grill (4 in a row). When the bottom side is cooked
///          "พลิกด้าน!" pops up -> click the squid to flip. When the second side is done "เก็บ!" -> click to put it
///          on the board. Too slow and it burns ("ไหม้แล้ว!" -> click to throw away).
///  SAUCE : click the ingredient tray -> chili, lime and garlic float into the mortar -> click the mortar to stir ("คนๆ!")
///          -> when it's done "ตัก!" -> click to scoop cups of sauce onto the teal board (3 cups per batch).
///  Goal  : ปลาหมึก 10 + น้ำจิ้ม 10 -> WIN.  Gifts help (add squid / sauce / sets), send customers, or close the shop.
///  Test without a live: keys 1-6 = gift slots.
/// </summary>
public sealed class SquidGame : MonoBehaviour
{
    [Header("Round")]
    public int goal = 10;
    public int winGoal = 10;
    [Tooltip("Off = endless play (no WIN rounds)")] public bool winEnabled = false;
    [Header("Helpers (gift)")]
    public float helperWorkTime = 0.45f;
    [Header("Grill (seconds)")]
    public float sideTime = 5f;
    public float burnAfter = 10f;
    [Header("Sauce")]
    public int stirClicks = 6;
    public int cupsPerBatch = 3;

    public int Squid { get; private set; }
    public int Sauce { get; private set; }
    public int Wins { get; private set; }
    public bool IsWaiting { get; private set; } = true;
    public float RestLeft { get; private set; }
    public event System.Action<SquidAction,int,int> WheelResolved;

    // scene refs (built by SquidModels.BuildStall)
    Transform stall, vendor, armL, armR, vendorHead, pestle, paste, bits, squidItems, sauceItems, iceBox, mortar, ingredients;
    readonly Transform[] slotPos = new Transform[4];
    readonly List<Transform> flames = new List<Transform>();
    readonly List<Transform> spots = new List<Transform>();
    Vector3 vendorHome, pestleHome, pasteHome, pestleTipLocal, pestleDir0; Quaternion pestleRot; float pestleTipY;
    Light grillLight;
    MaterialPropertyBlock mpb;
    Camera cam;

    // ---- grill
    enum SState { Empty, Placing, CookA, FlipReady, Flipping, CookB, Done, Burnt, Leaving }
    sealed class Slot { public GameObject go; public SState state; public float a, b, over; public Bubble bubble; public ParticleSystem smoke; public float wiggle; }
    readonly Slot[] grill = new Slot[4];

    // ---- sauce
    enum MState { Empty, Adding, Stir, Ready }
    MState mstate = MState.Empty;
    int stirs, scoops; float stirAnim, mortarBusy;
    Bubble mortarBubble, trayBubble, iceBubble;
    readonly List<Transform> bitObjs = new List<Transform>();

    // ---- customers
    sealed class Customer
    {
        public GameObject go; public Transform tr; public PTVTikTokLive.Message who;
        public int needSquid, needSauce, spot = -1; public bool leaving; public float speed, bob;
        public RectTransform tag; public Text need; public RawImage pic; public Text initial; public Texture tex; public Image needBg;
        public bool helper; public int station = -1; public float workT; public Transform armL, armR;
    }
    readonly List<Customer> customers = new List<Customer>();

    sealed class Flyer { public GameObject go; public Vector3 a, b; public float t, dur, arc; public Quaternion ra, rb; public System.Action done; public bool keep; }
    readonly List<Flyer> flyers = new List<Flyer>();

    // ---- HUD
    Canvas hud; RectTransform hudRt, bubbleLayer; Text squidText, sauceText, winText, banner, sub, owedText; float bannerT;
    SquidGiftDirector director;
    float celebrate, vendorReach;
    static Sprite pill, circle;

    // ------------------------------------------------------------------ setup
    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        PTVLive.Ensure();
        director = PTVLive.I.director;
        stall = GameObject.Find("Stall")?.transform;
        if (!stall) stall = SquidModels.BuildStall(null);
        cam = Camera.main;
        Find();
        BuildHud();
        StartCoroutine(WheelRunner());
        PTVGiftNotice.WorldAnchor = new Vector3(0f, SquidModels.TopY + 0.05f, 0.22f);   // empty table space under the grill
        AddSignText();
        PTVLang.Changed += RefreshSignText;
        RefreshTrays();
    }

    Transform F(string path) => stall.Find(path);

    void AddSignText()
    {
        var font = PTVUI.Font;
        var sign = SquidModels.Deep(stall, "Sign");
        Vector3 p = sign ? sign.position : new Vector3(0, 4.62f, -1.05f);
        for (int k = 0; k < 2; k++)
        {
            var go = new GameObject(k == 0 ? "SignTextShadow" : "SignText"); go.transform.SetParent(stall, true);
            go.transform.position = p + new Vector3(0.36f + (k == 0 ? 0.02f : 0f), k == 0 ? -0.03f : 0f, -0.14f - k * 0.01f);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font; tm.text = PTVLang.T("ปลาหมึกย่าง"); tm.fontSize = 120; tm.characterSize = 0.034f;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.color = k == 0 ? new Color(0.45f, 0.2f, 0.07f) : new Color(1f, 0.93f, 0.8f);
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = font.material;
            // fit the words inside the sign face, right of the squid icon (about 1.7 x 0.45 units)
            float w = mr.bounds.size.x, h = mr.bounds.size.y;
            if (w > 0.001f && h > 0.001f) tm.characterSize *= Mathf.Min(1.7f / w, 0.46f / h);
        }
        foreach (var b in stall.GetComponentsInChildren<Transform>())
        {
            if (b.name != "Glass") continue;
            var lg = new GameObject("BulbLight"); lg.transform.SetParent(b, false);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.85f, 0.55f); l.range = 2.5f; l.intensity = 0.8f;
        }
        foreach (var r in stall.GetComponentsInChildren<Transform>()) if (r.name == "RawSquid") SquidModels.CookSquid(r.gameObject, 0f, 0f, 0f, mpb);
    }

    void RefreshSignText()
    {
        if (!stall) return;
        foreach (var tm in stall.GetComponentsInChildren<UnityEngine.TextMesh>(true))
        {
            if (tm.name != "SignText" && tm.name != "SignTextShadow") continue;
            tm.characterSize = 0.034f;
            tm.text = PTVLang.T("ปลาหมึกย่าง");
            var renderer = tm.GetComponent<UnityEngine.MeshRenderer>();
            float w = renderer.bounds.size.x, h = renderer.bounds.size.y;
            if (w > 0.001f && h > 0.001f) tm.characterSize *= Mathf.Min(1.7f / w, 0.46f / h);
        }
    }

    void OnDestroy() { PTVLang.Changed -= RefreshSignText; }

    void Find()
    {
        vendor = F("Vendor"); vendorHome = vendor.localPosition;
        armL = SquidModels.Deep(vendor, "ArmL"); armR = SquidModels.Deep(vendor, "ArmR"); vendorHead = SquidModels.Deep(vendor, "Head");
        mortar = F("Mortar"); pestle = SquidModels.Deep(mortar, "Pestle"); paste = SquidModels.Deep(mortar, "Paste"); bits = F("Mortar/Bits");
        pasteHome = paste.position;
        pestleHome = pestle.position; pestleRot = pestle.rotation;   // world space: the mortar model is axis-converted
        {   // find the pestle's tip (lowest point) and its stick direction, for the stirring pose
            Vector3 lo = pestle.position, hi = pestle.position; bool first = true;
            foreach (var mf in pestle.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh) foreach (var v in mf.sharedMesh.vertices)
                {
                    var w = mf.transform.TransformPoint(v);
                    if (first) { lo = hi = w; first = false; }
                    if (w.y < lo.y) lo = w; if (w.y > hi.y) hi = w;
                }
            pestleTipLocal = pestle.InverseTransformPoint(lo); pestleTipY = lo.y;
            pestleDir0 = (hi - lo).sqrMagnitude > 1e-4f ? (hi - lo).normalized : Vector3.up;
        }
        SetPasteColor(1f);
        iceBox = F("IceBox"); ingredients = F("Ingredients");
        squidItems = F("SquidTray/Items"); sauceItems = F("SauceTray/Items");
        for (int i = 0; i < 4; i++) { slotPos[i] = F("Grill/GrillSlots/Slot" + i); grill[i] = new Slot(); grill[i].smoke = MakeSmoke(slotPos[i]); }
        foreach (Transform f in F("Grill/Flames")) flames.Add(f);
        for (int i = 0; i < 8; i++) spots.Add(F("CustomerSpots/Spot" + i));
        AddCollider(iceBox, new Vector3(0, 0.4f, 0), new Vector3(0.9f, 0.8f, 0.9f));
        AddCollider(mortar, new Vector3(0, 0.45f, 0), new Vector3(1.1f, 0.9f, 1.1f));
        AddCollider(ingredients, new Vector3(0, 0.2f, 0), new Vector3(1.1f, 0.4f, 1.0f));
        AddCollider(squidItems.parent, new Vector3(0, 0.25f, 0), new Vector3(2.3f, 0.5f, 1.3f));
        AddCollider(sauceItems.parent, new Vector3(0, 0.25f, 0), new Vector3(2.3f, 0.5f, 1.3f));
        var lg = new GameObject("GrillLight"); lg.transform.SetParent(F("Grill"), false); lg.transform.localPosition = new Vector3(0, 1.0f, -0.3f);
        grillLight = lg.AddComponent<Light>(); grillLight.type = LightType.Point; grillLight.color = new Color(1f, 0.55f, 0.2f); grillLight.range = 3f; grillLight.intensity = 1.6f;
    }

    static void AddCollider(Transform t, Vector3 center, Vector3 size)
    {
        var c = t.GetComponent<BoxCollider>(); if (!c) c = t.gameObject.AddComponent<BoxCollider>();
        c.center = center; c.size = size;
    }

    ParticleSystem MakeSmoke(Transform at)
    {
        var go = new GameObject("Smoke"); go.transform.SetParent(at, false); go.transform.localPosition = new Vector3(0, 0.1f, 0.1f);
        go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f); main.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
        main.startColor = new Color(1f, 1f, 1f, 0.75f); main.maxParticles = 80; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.25f, 0.6f, 0.05f);
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.6f; noise.scrollSpeed = 0.4f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.95f, 0.93f, 0.9f), 1) },
                                          new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.6f, 0.2f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 2.2f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = SquidModels.Mat("Smoke", Color.white, 0f, null, true);
        if (!r.sharedMaterial.GetTexture("_BaseMap")) r.sharedMaterial.SetTexture("_BaseMap", SoftDot());
        ps.Play();
        return ps;
    }

    static Texture2D softDot;
    static Texture2D SoftDot()
    {
        if (softDot) return softDot;
        var t = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
            float a = Mathf.Clamp01(1 - d); t.SetPixel(x, y, new Color(1, 1, 1, a * a * (3 - 2 * a)));
        }
        t.Apply(); return softDot = t;
    }

    public void BeginRound() { IsWaiting = false; }
    bool CanPlay => !IsWaiting && RestLeft <= 0f && celebrate <= 0f && !PTVStartMenu.UIBlocking;

    // ------------------------------------------------------------------ loop
    void Update()
    {
        FitCamera();
        HandleTestKeys();
        while (director.events.Count > 0) Apply(director.events.Dequeue());
        float dt = Time.deltaTime;
        if (RestLeft > 0f) { RestLeft -= dt; if (RestLeft <= 0f) { RestLeft = 0f; PTVAudio.Sfx("start", 0.5f); } }
        if (celebrate > 0f) { celebrate -= dt; if (celebrate <= 0f) { RefreshTrays(); CheckWin(); } }

        HandleClick();
        UpdateGrill(dt);
        UpdateSauce(dt);
        AnimateFire(dt);
        AnimateVendor(dt);
        UpdateCustomers(dt);
        UpdateHelpers(dt);
        UpdateSelection(dt);
        UpdateFlyers(dt);
        UpdateHud(dt);
    }

    void FitCamera()
    {
        if (!cam) cam = Camera.main; if (!cam) return;
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        // keep the whole counter width on screen whatever the shape of the window
        float v = 2f * Mathf.Atan(Mathf.Tan(25.5f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;
        cam.fieldOfView = Mathf.Clamp(v, 34f, 84f);
        UpdateZoom();
    }

    // ---- camera zoom: mouse wheel / + - keys (saved). 1 = the scene camera, smaller = closer.
    const string ZoomKey = "PTVFOOD.Zoom2";
    public float defaultZoom = 1f, minZoom = 0.6f, maxZoom = 1.5f;   // multiplier on top of the auto-fit (1 = whole table fits)
    bool zoomInit; Vector3 camBase, zoomPivot; float zoom, zoomTarget, zoomSaveAt = -1f;

    /// <summary>Camera distance factor (from the pivot) that just fits the whole table top on screen, with room at the bottom for customers' heads.</summary>
    float FitTable()
    {
        float lo = 0.3f, hi = 2.5f;
        for (int it = 0; it < 14; it++)
        {
            float mid = (lo + hi) * 0.5f;
            cam.transform.position = zoomPivot + (camBase - zoomPivot) * mid;
            if (TableFits()) hi = mid; else lo = mid;
        }
        return hi;
    }

    bool TableFits()
    {
        float Y = SquidModels.TopY;
        var pts = new[] { new Vector3(-3.35f, Y, -1.75f), new Vector3(3.35f, Y, -1.75f), new Vector3(-3.35f, Y, 2.5f), new Vector3(3.35f, Y, 2.5f), new Vector3(0, Y + 1.9f, 2.95f) };
        for (int i = 0; i < pts.Length; i++)
        {
            var v = cam.WorldToViewportPoint(pts[i]);
            if (v.z <= 0f || v.x < 0.015f || v.x > 0.985f) return false;
            if (i < 2 && v.y < 0.2f) return false;            // keep space for the customers' heads
            if (i == 4 && v.y > 0.86f) return false;          // vendor's head below the score bar
        }
        return true;
    }

    void UpdateZoom()
    {
        if (!zoomInit)
        {
            zoomInit = true; camBase = cam.transform.position;
            var f = cam.transform.forward; float k = (SquidModels.TopY - camBase.y) / Mathf.Min(-0.01f, f.y);
            zoomPivot = camBase + f * k;
            zoom = zoomTarget = Mathf.Clamp(PlayerPrefs.GetFloat(ZoomKey, defaultZoom), minZoom, maxZoom);
        }
        if (!PTVStartMenu.UIBlocking)
        {
            float wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
            var kb = Keyboard.current;
            if (kb != null && (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed)) wheel += 400f * Time.unscaledDeltaTime;
            if (kb != null && (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed)) wheel -= 400f * Time.unscaledDeltaTime;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                zoomTarget = Mathf.Clamp(zoomTarget * (1f - Mathf.Clamp(wheel / 120f, -3f, 3f) * 0.07f), minZoom, maxZoom);
                zoomSaveAt = Time.unscaledTime + 0.8f;
            }
        }
        if (zoomSaveAt > 0f && Time.unscaledTime > zoomSaveAt) { zoomSaveAt = -1f; PlayerPrefs.SetFloat(ZoomKey, zoomTarget); PlayerPrefs.Save(); }
        zoom = Mathf.Lerp(zoom, zoomTarget, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        cam.transform.position = zoomPivot + (camBase - zoomPivot) * (FitTable() * zoom);
    }

    void HandleTestKeys()
    {
        var kb = Keyboard.current;
        if (kb == null || PTVStartMenu.UIBlocking || IsWaiting) return;
        Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6 };
        Key[] pad = { Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6 };
        for (int i = 0; i < keys.Length; i++)
            if (kb[keys[i]].wasPressedThisFrame || kb[pad[i]].wasPressedThisFrame) director.SimulateSlot(i);
    }

    // ------------------------------------------------------------------ clicks
    void HandleClick()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || !cam) return;
        if (EventSystem.current && EventSystem.current.IsPointerOverGameObject()) return;
        if (!CanPlay) { if (RestLeft > 0f) { PTVAudio.Sfx("deny", 0.4f, 1f, 0.2f); Pop("ร้านปิดอยู่จ้า", vendor.position + Vector3.up * 2.4f, new Color(0.6f, 0.62f, 0.75f)); } return; }
        var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(ray, out var hit, 100f)) return;
        var t = hit.collider.transform;
        if (t == iceBox) { ClickIceBox(); return; }
        if (t == ingredients) { ClickIngredients(); return; }
        if (t == mortar) { ClickMortar(); return; }
        if (t == squidItems.parent) { SelectTray(1); return; }
        if (t == sauceItems.parent) { SelectTray(2); return; }
        foreach (var c in customers) if (!c.leaving && c.go && t == c.tr) { ServeCustomer(c); return; }
        for (int i = 0; i < 4; i++) if (grill[i].go && (t == grill[i].go.transform || t.IsChildOf(grill[i].go.transform))) { ClickSquid(i); return; }
    }

    void ClickIceBox()
    {
        int i = System.Array.FindIndex(grill, s => s.state == SState.Empty);
        StartCoroutine(Squash(iceBox));
        if (i < 0) { PTVAudio.Sfx("deny", 0.4f); Pop("เตาเต็มแล้ว!", iceBox.position + Vector3.up * 1.0f, new Color(0.6f, 0.62f, 0.75f)); return; }
        var s = grill[i];
        s.go = SquidModels.Squid(null, "Grilling");
        s.go.transform.position = iceBox.position + Vector3.up * 0.7f;
        s.a = s.b = s.over = 0f; s.state = SState.Placing;
        SquidModels.CookSquid(s.go, 0, 0, 0, mpb);
        var col = s.go.AddComponent<BoxCollider>(); col.center = new Vector3(0, 0, 0.05f); col.size = new Vector3(0.7f, 0.45f, 1.7f);
        PTVAudio.Sfx("swoosh", 0.45f, 1.1f);
        vendorReach = 0.5f;
        Fly(s.go, iceBox.position + Vector3.up * 0.7f, slotPos[i].position, 0.45f, Quaternion.Euler(0, -40, 0), slotPos[i].rotation, () =>
        {
            s.go.transform.SetParent(slotPos[i], true);
            s.go.transform.localPosition = Vector3.zero; s.go.transform.localRotation = Quaternion.identity; s.go.transform.localScale = Vector3.one * 0.8f;
            s.state = SState.CookA;
            SquidSfx.Play("sizzle", 1f, Random.Range(0.95f, 1.08f));   // ฉ่า!
        });
    }

    void ClickSquid(int i)
    {
        var s = grill[i];
        switch (s.state)
        {
            case SState.FlipReady:
                s.state = SState.Flipping; Hide(ref s.bubble); vendorReach = 0.5f;
                PTVAudio.Sfx("jump", 0.45f, 1.2f);
                StartCoroutine(FlipSquid(s));
                break;
            case SState.Done:
                s.state = SState.Leaving; Hide(ref s.bubble); vendorReach = 0.5f;
                var go = s.go; s.go = null;
                go.transform.SetParent(null, true);
                int index = Squid;
                SquidSfx.Play("serve", 0.45f, 0.9f);
                Fly(go, go.transform.position, TraySpot(true, index), 0.5f, go.transform.rotation, Quaternion.Euler(0, 90, 0), () =>
                {
                    Destroy(go);
                    s.state = SState.Empty;
                    Produce(true, 1, TraySpot(true, index), false);
                });
                break;
            case SState.Burnt:
                Hide(ref s.bubble);
                var bgo = s.go; s.go = null; s.state = SState.Empty;
                bgo.transform.SetParent(null, true);
                PTVAudio.Sfx("deny", 0.5f, 0.8f);
                Fly(bgo, bgo.transform.position, bgo.transform.position + new Vector3(-3f, 2.5f, -2f), 0.6f, bgo.transform.rotation, Random.rotation, () => Destroy(bgo));
                break;
            default:
                s.wiggle = 0.3f;   // still cooking
                break;
        }
    }

    IEnumerator FlipSquid(Slot s)
    {
        var t = s.go.transform; float k = 0f;
        while (k < 1f && t)
        {
            k += Time.deltaTime * 2.6f;
            float e = Mathf.SmoothStep(0, 1, k);
            t.localRotation = Quaternion.Euler(0, 0, 180f * e);
            t.localPosition = new Vector3(0, Mathf.Sin(k * Mathf.PI) * 0.55f, 0);
            yield return null;
        }
        if (!t) yield break;
        t.localRotation = Quaternion.Euler(0, 0, 180); t.localPosition = Vector3.zero;
        s.state = SState.CookB; s.over = 0f;
        SquidSfx.Play("sizzle", 0.75f, Random.Range(1.05f, 1.15f));
    }

    void ClickIngredients()
    {
        StartCoroutine(Squash(ingredients));
        if (mstate != MState.Empty) { Pop(mstate == MState.Ready ? "ตักก่อนนะ!" : "คนก่อนนะ!", ingredients.position + Vector3.up * 0.8f, new Color(0.6f, 0.62f, 0.75f)); PTVAudio.Sfx("deny", 0.35f); return; }
        mstate = MState.Adding; mortarBusy = 0.9f; stirs = 0;
        Hide(ref trayBubble);
        foreach (var b in bitObjs) if (b) Destroy(b.gameObject);
        bitObjs.Clear();
        paste.gameObject.SetActive(true);
        paste.position = pasteHome;
        SetPasteColor(0f);
        var pieces = new List<GameObject>
        {
            SquidModels.Chili(null), SquidModels.Chili(null), SquidModels.Lime(null), SquidModels.Garlic(null), SquidModels.Garlic(null)
        };
        for (int i = 0; i < pieces.Count; i++)
        {
            var go = pieces[i];
            var from = ingredients.position + new Vector3(-0.25f + i * 0.12f, 0.3f, 0.1f);
            float ang = i * 72f * Mathf.Deg2Rad;
            var to = bits.position + new Vector3(Mathf.Cos(ang) * 0.17f, 0.02f, Mathf.Sin(ang) * 0.17f);
            go.transform.position = from;
            int idx = i;
            Fly(go, from, to, 0.55f + i * 0.08f, Random.rotation, Quaternion.Euler(0, idx * 72f, 0), () =>
            {
                go.transform.SetParent(bits, true);
                bitObjs.Add(go.transform);
                SquidSfx.Play("plop", 0.6f, 1f + idx * 0.07f);
            }, true, 1.1f);
        }
        PTVAudio.Sfx("swoosh", 0.45f, 0.9f);
    }

    void ClickMortar()
    {
        StartCoroutine(Squash(mortar, 0.06f));
        switch (mstate)
        {
            case MState.Empty: Pop("กดถาดเครื่องก่อน!", mortar.position + Vector3.up * 1.1f, new Color(0.6f, 0.62f, 0.75f)); PTVAudio.Sfx("deny", 0.35f); break;
            case MState.Adding: break;
            case MState.Stir:
                stirs++; stirAnim = 1f; vendorReach = 0.4f;
                SquidSfx.Play("pound", 0.8f, 0.9f + stirs * 0.04f); SquidSfx.Play("stir", 0.5f, Random.Range(0.9f, 1.1f));
                SetPasteColor((float)stirs / stirClicks);
                Pop("คน!", mortar.position + new Vector3(Random.Range(-0.3f, 0.3f), 1.0f, 0), new Color(1f, 0.78f, 0.25f), 0.6f);
                if (stirs >= stirClicks) { mstate = MState.Ready; scoops = cupsPerBatch; PTVAudio.Sfx("sparkle", 0.6f); Hide(ref mortarBubble); }
                break;
            case MState.Ready:
                if (scoops <= 0) return;
                if (Sauce + sauceInFlight >= SauceCapacity) { PTVAudio.Sfx("deny", 0.45f); Pop("ถาดเต็ม! ขายก่อนนะ", sauceItems.position + Vector3.up * 0.7f, new Color(0.6f, 0.62f, 0.75f)); return; }
                scoops--; vendorReach = 0.4f; sauceInFlight++;
                int index = Sauce + sauceInFlight - 1;
                var cup = SquidModels.SauceCup(null, "Scoop");
                var from = mortar.position + Vector3.up * 0.7f;
                cup.transform.position = from;
                SquidSfx.Play("scoop", 0.8f, Random.Range(0.95f, 1.05f));
                Fly(cup, from, TraySpot(false, Mathf.Min(index, 9)), 0.5f, Quaternion.identity, Quaternion.identity, () =>
                {
                    Destroy(cup); sauceInFlight = Mathf.Max(0, sauceInFlight - 1);
                    Produce(false, 1, TraySpot(false, Mathf.Min(index, 9)), false);
                });
                paste.position = pasteHome + Vector3.down * ((cupsPerBatch - scoops) * 0.05f);
                foreach (var b in bitObjs) if (b) b.localScale = Vector3.one * Mathf.Max(0.2f, 0.5f - 0.1f * (cupsPerBatch - scoops));
                if (scoops <= 0)
                {
                    mstate = MState.Empty; Hide(ref mortarBubble);
                    foreach (var b in bitObjs) if (b) Destroy(b.gameObject);
                    bitObjs.Clear();
                    SetPasteColor(1f);
                    paste.position = pasteHome;
                }
                break;
        }
    }

    void SetPasteColor(float k)
    {
        var c = Color.Lerp(new Color(0.62f, 0.78f, 0.3f), new Color(0.47f, 0.72f, 0.16f), Mathf.Clamp01(k));
        mpb.SetColor("_BaseColor", c); paste.GetComponent<Renderer>().SetPropertyBlock(mpb);
    }

    IEnumerator Squash(Transform t, float amount = 0.1f)
    {
        float k = 0f; var s0 = Vector3.one;
        while (k < 1f && t) { k += Time.deltaTime * 5f; float w = Mathf.Sin(k * Mathf.PI) * amount; t.localScale = new Vector3(s0.x * (1 + w), s0.y * (1 - w), s0.z * (1 + w)); yield return null; }
        if (t) t.localScale = s0;
    }

    // ------------------------------------------------------------------ grill / sauce update
    void UpdateGrill(float dt)
    {
        int frying = 0; foreach (var g in grill) if (g.go && g.state != SState.Empty && g.state != SState.Placing && g.state != SState.Leaving) frying++;
        SquidSfx.SetFry(IsWaiting ? 0f : frying * 0.28f * (RestLeft > 0f ? 0.4f : 1f));
        bool cooking = RestLeft <= 0f;
        for (int i = 0; i < 4; i++)
        {
            var s = grill[i];
            var em = s.smoke.emission;
            bool onGrill = s.go && (s.state == SState.CookA || s.state == SState.FlipReady || s.state == SState.CookB || s.state == SState.Done || s.state == SState.Burnt || s.state == SState.Flipping);
            em.rateOverTime = onGrill ? (s.state == SState.Burnt ? 14f : 5f) : 0f;
            var main = s.smoke.main; main.startColor = s.state == SState.Burnt ? new Color(0.35f, 0.33f, 0.32f, 0.8f) : new Color(1f, 1f, 1f, 0.75f);
            if (!s.go) continue;
            if (s.wiggle > 0f) { s.wiggle -= dt; s.go.transform.localPosition = new Vector3(Mathf.Sin(s.wiggle * 60f) * 0.03f * s.wiggle * 3f, 0, 0); }
            if (!cooking) continue;
            switch (s.state)
            {
                case SState.CookA:
                    s.a += dt / sideTime;
                    if (s.a >= 1f) { s.a = 1f; s.state = SState.FlipReady; s.over = 0f; Show(ref s.bubble, "พลิกด้าน!", new Color(1f, 0.55f, 0.25f)); PTVAudio.Sfx("tick", 0.5f, 1.3f); }
                    break;
                case SState.FlipReady:
                    s.over += dt; if (s.over > burnAfter) Burn(s);
                    break;
                case SState.CookB:
                    s.b += dt / sideTime;
                    if (s.b >= 1f) { s.b = 1f; s.state = SState.Done; s.over = 0f; Show(ref s.bubble, "เก็บ!", new Color(0.4f, 0.8f, 0.35f)); PTVAudio.Sfx("tick", 0.5f, 1.6f); }
                    break;
                case SState.Done:
                    s.over += dt; if (s.over > burnAfter) Burn(s);
                    break;
            }
            float burnt = s.state == SState.Burnt ? 1f : (s.state == SState.FlipReady || s.state == SState.Done) ? Mathf.Clamp01((s.over - burnAfter * 0.55f) / (burnAfter * 0.45f)) * 0.5f : 0f;
            bool flipped = !(s.state == SState.CookA || s.state == SState.FlipReady || (s.state == SState.Burnt && s.b <= 0f));
            SquidModels.CookSquid(s.go, s.a, s.b, burnt, mpb, flipped ? s.a : s.b);
            if (s.state == SState.CookA || s.state == SState.CookB) s.go.transform.localPosition = new Vector3(0, 0.012f * Mathf.Abs(Mathf.Sin(Time.time * 14f + i)), 0);
            if (s.bubble != null) s.bubble.world = s.go.transform.position + Vector3.up * (0.45f + (i % 2) * 0.42f);
        }
    }

    void Burn(Slot s) { s.state = SState.Burnt; Show(ref s.bubble, "ไหม้แล้ว!", new Color(0.45f, 0.42f, 0.42f)); PTVAudio.Sfx("error", 0.4f); }

    void UpdateSauce(float dt)
    {
        if (mstate == MState.Adding) { mortarBusy -= dt; if (mortarBusy <= 0f) { mstate = MState.Stir; } }
        // bubbles that tell the player what to click next
        bool play = !IsWaiting;
        if (play && mstate == MState.Empty) Show(ref trayBubble, "ใส่เครื่อง!", new Color(1f, 0.5f, 0.6f)); else Hide(ref trayBubble);
        if (trayBubble != null) trayBubble.world = ingredients.position + Vector3.up * 0.75f;
        if (play && mstate == MState.Stir) Show(ref mortarBubble, "คนๆ! " + stirs + "/" + stirClicks, new Color(1f, 0.75f, 0.2f));
        else if (play && mstate == MState.Ready) Show(ref mortarBubble, "ตัก! x" + scoops, new Color(0.3f, 0.75f, 0.75f));
        else Hide(ref mortarBubble);
        if (mortarBubble != null) { mortarBubble.world = mortar.position + Vector3.up * 1.25f; mortarBubble.text.text = mstate == MState.Stir ? "คนๆ! " + stirs + "/" + stirClicks : "ตัก! x" + scoops; }
        bool anyEmpty = System.Array.Exists(grill, s => s.state == SState.Empty);
        if (play && anyEmpty && CountOnGrill() == 0) Show(ref iceBubble, "หยิบปลาหมึก!", new Color(0.35f, 0.6f, 0.95f)); else Hide(ref iceBubble);
        if (iceBubble != null) iceBubble.world = iceBox.position + Vector3.up * 1.05f;

        // stir animation: pestle circles, the bits swirl and get smaller
        if (stirAnim > 0f) stirAnim = Mathf.Max(0f, stirAnim - dt * 2.2f);
        float ang = (1f - stirAnim) * 360f;
        if (stirAnim > 0f)
        {
            // the tip runs round the inside of the bowl wall, the handle leaning out over the rim
            var u = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad), 0f, Mathf.Sin(ang * Mathf.Deg2Rad));
            var dir = (Vector3.up * Mathf.Cos(16f * Mathf.Deg2Rad) + u * Mathf.Sin(16f * Mathf.Deg2Rad)).normalized;
            var rot = Quaternion.FromToRotation(pestleDir0, dir) * pestleRot;
            var tip = new Vector3(mortar.position.x, pestleTipY - 0.03f, mortar.position.z) + u * 0.27f;
            pestle.rotation = rot;
            var pos = pestle.position + tip - pestle.TransformPoint(pestleTipLocal);
            float w = Mathf.Clamp01(Mathf.Sin(stirAnim * Mathf.PI) * 3f);   // ease in/out from the resting pose
            pestle.position = Vector3.Lerp(pestleHome, pos, w);
            pestle.rotation = Quaternion.Slerp(pestleRot, rot, w);
            bits.localRotation = Quaternion.Euler(0, bits.localEulerAngles.y + dt * 600f * stirAnim, 0);
        }
        else { pestle.position = Vector3.Lerp(pestle.position, pestleHome, dt * 8f); pestle.rotation = Quaternion.Slerp(pestle.rotation, pestleRot, dt * 8f); }
        if (mstate == MState.Stir || mstate == MState.Ready)
        {
            float k = mstate == MState.Ready ? 1f : (float)stirs / stirClicks;
            foreach (var b in bitObjs) if (b && mstate == MState.Stir) b.localScale = Vector3.one * Mathf.Lerp(1f, 0.5f, k);
        }
    }

    int CountOnGrill() { int n = 0; foreach (var s in grill) if (s.state != SState.Empty) n++; return n; }

    void AnimateFire(float dt)
    {
        float heat = RestLeft > 0f ? 0.35f : 1f;
        float t = Time.time;
        for (int i = 0; i < flames.Count; i++)
        {
            float n = Mathf.PerlinNoise(t * 3f, i * 1.7f);
            flames[i].localScale = new Vector3(0.8f + 0.3f * n, (0.6f + 0.9f * n) * heat, 0.8f + 0.3f * n);
        }
        grillLight.intensity = 1.6f * heat * (0.85f + 0.15f * Mathf.PerlinNoise(t * 5f, 3f));
    }

    void AnimateVendor(float dt)
    {
        Vector3 target = RestLeft > 0f ? vendorHome + new Vector3(1.6f, -0.25f, 0.3f) : vendorHome;
        var p = Vector3.MoveTowards(vendor.localPosition, target, dt * 3f);
        float t = Time.time;
        vendorReach = Mathf.Max(0f, vendorReach - dt);
        if (celebrate > 0f)
        {
            armL.localRotation = Quaternion.Euler(0, -150 + 20 * Mathf.Sin(t * 12), 0);
            armR.localRotation = Quaternion.Euler(0, 150 - 20 * Mathf.Sin(t * 12), 0);
            p.y = vendorHome.y + Mathf.Abs(Mathf.Sin(t * 8f)) * 0.25f;
            vendorHead.localRotation = Quaternion.Euler(0, 8 * Mathf.Sin(t * 6), 0);
        }
        else if (RestLeft > 0f)
        {
            armL.localRotation = Quaternion.Euler(10, 0, 0); armR.localRotation = Quaternion.Euler(10, 0, 0);
            vendorHead.localRotation = Quaternion.Euler(18 + 4 * Mathf.Sin(t * 1.5f), 14, 0);
        }
        else
        {
            float reach = Mathf.SmoothStep(0, 1, Mathf.Clamp01(vendorReach * 3f));
            armL.localRotation = Quaternion.Euler(15 + 70 * reach + 5 * Mathf.Sin(t * 2f), -10, 0);
            armR.localRotation = Quaternion.Euler(15 + 70 * reach + 5 * Mathf.Sin(t * 2f + 1), 10, 0);
            vendorHead.localRotation = Quaternion.Euler(6, 4 * Mathf.Sin(t * 1.3f), 10 * Mathf.Sin(t * 0.7f));
            if (Vector3.Distance(p, target) < 0.01f) p.y = vendorHome.y + Mathf.Abs(Mathf.Sin(t * 2.2f)) * 0.03f;
        }
        vendor.localPosition = p;
    }

    // ------------------------------------------------------------------ stock
    /// <summary>New squid / sauce: waiting customers get it first, the rest goes on the board.</summary>
    void Produce(bool squid, int count, Vector3 from, bool fly = true)
    {
        int toTray = 0;
        for (int k = 0; k < count; k++)
        {
            if (!squid && Sauce >= SauceCapacity) continue;   // board full: extra sauce is not kept
            if (squid) Squid++; else Sauce++;
            if (fly && k < 10) FlyItem(squid, from, TraySpot(squid, (squid ? Squid : Sauce) - 1), 0.55f);
            toTray++;
        }
        if (toTray > 0) Pop("+" + toTray, (squid ? squidItems : sauceItems).position + Vector3.up * 0.6f, squid ? new Color(1f, 0.6f, 0.3f) : new Color(0.45f, 0.78f, 0.3f), 0.8f);
        PTVAudio.I.Pickup(Random.Range(0, 5));
        RefreshTrays();
        CheckWin();
    }

    void CheckWin()
    {
        if (celebrate > 0f || !winEnabled || Squid < goal || Sauce < goal) return;
        Squid -= goal; Sauce -= goal; Wins++;
        celebrate = 3f;
        PTVAudio.Sfx("win", 0.8f);
        bool champ = Wins >= winGoal;
        ShowBanner(champ ? "แชมป์ปลาหมึกย่าง!" : "ชนะ!", champ ? "ขายครบ " + winGoal + " รอบ!" : "ขายครบชุดแล้ว  ชนะ " + Wins + "/" + winGoal, 3f);
        if (champ) Wins = 0;
        for (int i = 0; i < Mathf.Min(goal, 10); i++)
        {
            FlyItem(true, TraySpot(true, i), cam.transform.position + new Vector3(Random.Range(-3f, 3f), 3f, 2f), 0.9f);
            FlyItem(false, TraySpot(false, i), cam.transform.position + new Vector3(Random.Range(-3f, 3f), 3f, 2f), 0.9f);
        }
        ClearTray(squidItems); ClearTray(sauceItems);
    }

    const int MaxSquidShown = 16, SauceCapacity = 10;
    int sauceInFlight;

    /// <summary>Layers of 4 then 3 squid; the 3-layers sit in the gaps of the layer below.</summary>
    static Vector3 SquidStack(int index)
    {
        int layer = 0;
        for (; layer < 30; layer++) { int n = layer % 2 == 0 ? 4 : 3; if (index < n) break; index -= n; }
        float x = layer % 2 == 0 ? -0.84f + index * 0.56f : -0.56f + index * 0.56f;
        return new Vector3(x, layer * 0.12f, 0.05f);
    }

    Vector3 TraySpot(bool squid, int index)
    {
        index = Mathf.Clamp(index, 0, squid ? MaxSquidShown - 1 : SauceCapacity - 1);
        var tray = squid ? squidItems : sauceItems;
        Vector3 local = squid ? SquidStack(index)
                              : new Vector3(-0.84f + (index % 5) * 0.42f, 0, index < 5 ? 0.27f : -0.27f);
        return tray.TransformPoint(local);
    }

    void ClearTray(Transform t) { for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject); }

    void RefreshTrays()
    {
        if (celebrate > 0f) return;
        Sync(squidItems, Mathf.Min(Squid, MaxSquidShown), true);
        Sync(sauceItems, Mathf.Min(Sauce, SauceCapacity), false);
    }

    void Sync(Transform tray, int want, bool squid)
    {
        while (tray.childCount > want) { var c = tray.GetChild(tray.childCount - 1); c.SetParent(null); Destroy(c.gameObject); }
        for (int i = tray.childCount; i < want; i++)
        {
            var go = squid ? SquidModels.Squid(tray, "Done") : SquidModels.SauceCup(tray, "Done");
            go.transform.position = TraySpot(squid, i);
            if (squid)
            {
                go.transform.localRotation = Quaternion.Euler(0, (i % 2 == 0 ? 3 : -3), 0);
                go.transform.localScale = Vector3.one * 0.8f;
                SquidModels.CookSquid(go, 1f, 1f, 0f, mpb);
            }
            else go.transform.localScale = Vector3.one * 1.2f;
        }
    }

    // ------------------------------------------------------------------ gifts
    void Apply(SquidGiftDirector.StallEvent e)
    {
        if (IsWaiting) return;
        Vector3 sky = cam ? cam.transform.position + cam.transform.forward * 2.5f + Vector3.up * 1.5f : new Vector3(0, 6, -3);
        switch (e.action)
        {
            case SquidAction.GrillSquid: AddHelper(e.sender, e.amount, 0); break;
            case SquidAction.MakeSauce: AddHelper(e.sender, 0, e.amount); break;
            case SquidAction.HelpSet: AddHelper(e.sender, e.amount, e.amount); break;
            case SquidAction.Customer: AddCustomer(e.sender, e.amount); break;
            case SquidAction.Wheel: wheelQueue.Enqueue(e); break;
            case SquidAction.ServeAll:
            {
                int served = 0;
                foreach (var c in customers) if (!c.leaving && (c.needSquid > 0 || c.needSauce > 0)) { c.needSquid = 0; c.needSauce = 0; served++; }
                PTVAudio.Sfx("sparkle", 0.7f);
                Pop(served > 0 ? "ทำออเดอร์ครบ!" : "ไม่มีออเดอร์ค้าง", vendor.position + Vector3.up * 2.6f, new Color(0.4f, 0.8f, 0.4f), 1.4f);
                break;
            }
            case SquidAction.CloseShop:
                RestLeft += e.amount;
                PTVAudio.Sfx("deny", 0.6f);
                ShowBanner("ปิดร้านไปพัก!", e.sender.DisplayName + " สั่งปิดร้าน " + e.amount + " วิ", 2.5f);
                break;
        }
    }

    void AddCustomer(PTVTikTokLive.Message who, int sets)
    {
        var shirts = new[] { new Color(0.95f, 0.55f, 0.5f), new Color(0.5f, 0.65f, 0.95f), new Color(0.98f, 0.82f, 0.4f), new Color(0.7f, 0.55f, 0.9f), new Color(0.45f, 0.8f, 0.65f), new Color(1f, 0.68f, 0.8f) };
        var hairs = new[] { new Color(0.16f, 0.12f, 0.1f), new Color(0.45f, 0.27f, 0.14f), new Color(0.8f, 0.6f, 0.35f), new Color(0.3f, 0.2f, 0.15f), new Color(0.95f, 0.75f, 0.4f) };
        var go = SquidModels.Person(stall, "Customer", shirts[Random.Range(0, shirts.Length)], hairs[Random.Range(0, hairs.Length)], new Color(1f, Random.Range(0.75f, 0.86f), Random.Range(0.62f, 0.74f)), false, Random.Range(0.92f, 1.0f));
        float side = Random.value < 0.5f ? -1 : 1;
        go.transform.localPosition = new Vector3(5.5f * side, 0, -1.6f);
        go.transform.localRotation = Quaternion.Euler(0, 180, 0);
        var c = new Customer { go = go, tr = go.transform, who = who, needSquid = sets, needSauce = sets, speed = Random.Range(3.2f, 4.2f), bob = Random.value * 6f };
        customers.Add(c);
        MakeTag(c);
        SquidSfx.Play("bell", 0.45f);

        var col = go.AddComponent<BoxCollider>(); col.center = new Vector3(0, 1.9f, 0); col.size = new Vector3(1.2f, 1.5f, 1.2f);
    }

    // ------------------------------------------------------------------ serving: click a board, then click a customer
    int selectedTray;   // 0 none, 1 squid, 2 sauce
    Bubble selBubble; float selPulse;

    void SelectTray(int which)
    {
        var tr = which == 1 ? squidItems.parent : sauceItems.parent;
        StartCoroutine(Squash(tr, 0.05f));
        if (selectedTray == which) { selectedTray = 0; Hide(ref selBubble); PTVAudio.Sfx("toggle", 0.4f, 0.8f); return; }
        if ((which == 1 ? Squid : Sauce) <= 0) { PTVAudio.Sfx("deny", 0.4f); Pop(which == 1 ? "ยังไม่มีปลาหมึก!" : "ยังไม่มีน้ำจิ้ม!", tr.position + Vector3.up * 0.7f, new Color(0.6f, 0.62f, 0.75f)); return; }
        selectedTray = which;
        Hide(ref selBubble);
        Show(ref selBubble, which == 1 ? "ปลาหมึก ✓ คลิกลูกค้า" : "น้ำจิ้ม ✓ คลิกลูกค้า", which == 1 ? new Color(1f, 0.6f, 0.3f) : new Color(0.45f, 0.78f, 0.3f));
        PTVAudio.Sfx("toggle", 0.5f, 1.2f);
    }

    void ServeCustomer(Customer c)
    {
        var head = c.tr.position + Vector3.up * 1.9f;
        if (selectedTray == 0) { PTVAudio.Sfx("deny", 0.35f); Pop("คลิกถาดก่อนนะ", head, new Color(0.6f, 0.62f, 0.75f)); return; }
        bool squid = selectedTray == 1;
        int need = squid ? c.needSquid : c.needSauce, have = squid ? Squid : Sauce;
        if (need <= 0) { PTVAudio.Sfx("deny", 0.35f); Pop(squid ? "ไม่ได้สั่งปลาหมึก" : "ไม่ได้สั่งน้ำจิ้ม", head, new Color(0.6f, 0.62f, 0.75f)); return; }
        int n = Mathf.Min(need, have);
        if (n <= 0) { PTVAudio.Sfx("deny", 0.35f); Pop("ของหมดแล้ว!", head, new Color(0.6f, 0.62f, 0.75f)); selectedTray = 0; Hide(ref selBubble); return; }
        for (int i = 0; i < Mathf.Min(n, 8); i++) FlyItem(squid, TraySpot(squid, have - 1 - i), head, 0.5f + i * 0.04f);
        if (squid) { Squid -= n; c.needSquid -= n; } else { Sauce -= n; c.needSauce -= n; }
        vendorReach = 0.5f;
        SquidSfx.Play("serve", 0.6f); SquidSfx.Play("cash", 0.45f);
        Pop("+" + n + (squid ? " ปลาหมึก" : " น้ำจิ้ม"), head + Vector3.up * 0.4f, squid ? new Color(1f, 0.6f, 0.3f) : new Color(0.45f, 0.78f, 0.3f), 0.9f);
        selectedTray = 0; Hide(ref selBubble);
        RefreshTrays();
    }

    void UpdateSelection(float dt)
    {
        selPulse += dt;
        var s = squidItems.parent; var u = sauceItems.parent;
        float p = 1f + 0.03f * Mathf.Sin(selPulse * 10f);
        if (selectedTray == 1 && Squid <= 0 || selectedTray == 2 && Sauce <= 0) { selectedTray = 0; Hide(ref selBubble); }
        if (selectedTray == 1) s.localScale = new Vector3(p, 1f, p); else if (s.localScale != Vector3.one && selectedTray != 1) s.localScale = Vector3.MoveTowards(s.localScale, Vector3.one, dt);
        if (selectedTray == 2) u.localScale = new Vector3(p, 1f, p); else if (u.localScale != Vector3.one && selectedTray != 2) u.localScale = Vector3.MoveTowards(u.localScale, Vector3.one, dt);
        if (selBubble != null) selBubble.world = (selectedTray == 1 ? s : u).position + Vector3.up * 0.75f;
    }

    void UpdateCustomers(float dt)
    {
        var used = new HashSet<int>();
        foreach (var c in customers) if (c.spot >= 0) used.Add(c.spot);
        foreach (var c in customers)
        {
            if (c.leaving || c.spot >= 0) continue;
            for (int i = 0; i < spots.Count; i++) if (!used.Contains(i)) { c.spot = i; used.Add(i); break; }
        }
        foreach (var c in customers)
        {
            if (c.leaving || c.spot < 4) continue;
            for (int i = 0; i < 4; i++) if (!used.Contains(i)) { used.Remove(c.spot); c.spot = i; used.Add(i); break; }
        }
        for (int k = customers.Count - 1; k >= 0; k--)
        {
            var c = customers[k];
            if (!c.leaving && c.needSquid <= 0 && c.needSauce <= 0) { c.leaving = true; c.spot = -1; SquidSfx.Play("cash", 0.6f, 1.1f); }
            Vector3 target = c.leaving ? new Vector3(c.tr.localPosition.x < 0 ? -6f : 6f, 0, -2.0f)
                           : c.spot >= 0 ? spots[c.spot].localPosition : new Vector3(c.tr.localPosition.x, 0, -3.2f);
            var p = c.tr.localPosition; p.y = 0;
            var np = Vector3.MoveTowards(p, target, c.speed * dt);
            bool moving = (np - p).sqrMagnitude > 1e-6f;
            c.bob += dt * (moving ? 12f : 2.5f);
            np.y = moving ? Mathf.Abs(Mathf.Sin(c.bob)) * 0.12f : 0.02f * Mathf.Sin(c.bob);
            c.tr.localPosition = np;
            var head = SquidModels.Deep(c.tr, "Head");
            if (head) head.localRotation = Quaternion.Euler(0, (moving ? 6f : 3f) * Mathf.Sin(c.bob * 0.5f), 0);
            if (c.leaving && Mathf.Abs(np.x) > 5.8f)
            {
                if (c.tag) Destroy(c.tag.gameObject);
                ReleaseTex(c);
                Destroy(c.go); customers.RemoveAt(k);
                continue;
            }
            UpdateTag(c);
        }
    }

    // ------------------------------------------------------------------ helpers (gift senders walk in and help)
    readonly List<Customer> helpers = new List<Customer>();
    readonly Customer[] stationBusy = new Customer[2];
    static Vector3 StationPos(int s) => s == 0 ? new Vector3(-1.75f, 0, 2.8f) : new Vector3(1.95f, 0, 2.8f);

    void AddHelper(PTVTikTokLive.Message who, int squid, int sauce)
    {
        if (squid <= 0 && sauce <= 0) return;
        var shirts = new[] { new Color(0.45f, 0.7f, 1f), new Color(1f, 0.7f, 0.35f), new Color(0.75f, 0.55f, 0.95f), new Color(1f, 0.6f, 0.75f), new Color(0.5f, 0.85f, 0.6f) };
        var hairs = new[] { new Color(0.16f, 0.12f, 0.1f), new Color(0.45f, 0.27f, 0.14f), new Color(0.85f, 0.62f, 0.32f), new Color(0.3f, 0.2f, 0.15f) };
        var go = SquidModels.Person(stall, "Helper", shirts[Random.Range(0, shirts.Length)], hairs[Random.Range(0, hairs.Length)], new Color(1f, Random.Range(0.76f, 0.86f), Random.Range(0.62f, 0.74f)), false, 0.95f);
        float side = squid > 0 ? -1f : 1f;
        go.transform.localPosition = new Vector3(6.5f * side, 0, 2.8f);
        var h = new Customer { go = go, tr = go.transform, who = who, needSquid = squid, needSauce = sauce, speed = Random.Range(3.4f, 4.2f), bob = Random.value * 6f, helper = true };
        h.armL = SquidModels.Deep(go.transform, "ArmL"); h.armR = SquidModels.Deep(go.transform, "ArmR");
        helpers.Add(h);
        MakeTag(h);
        SquidSfx.Play("bell", 0.4f, 1.2f);
    }

    bool SauceRoom() => Sauce + sauceInFlight < SauceCapacity;

    void UpdateHelpers(float dt)
    {
        float t = Time.time;
        for (int k = helpers.Count - 1; k >= 0; k--)
        {
            var h = helpers[k];
            if (!h.leaving && h.needSquid <= 0 && h.needSauce <= 0)
            {
                h.leaving = true;
                if (h.station >= 0 && stationBusy[h.station] == h) stationBusy[h.station] = null;
                h.station = -1; PTVAudio.Sfx("coin", 0.4f, 1.4f);
            }
            if (!h.leaving && h.station < 0)
            {
                int want = h.needSquid > 0 ? 0 : 1;
                if (stationBusy[want] == null) { stationBusy[want] = h; h.station = want; h.workT = 0.5f; }
            }
            int waitIdx = 0; for (int j = 0; j < k; j++) if (!helpers[j].leaving && helpers[j].station < 0) waitIdx++;
            Vector3 target = h.leaving ? new Vector3(h.tr.localPosition.x < 0 ? -6.5f : 6.5f, 0, 2.8f)
                           : h.station >= 0 ? StationPos(h.station)
                           : new Vector3((h.needSquid > 0 ? -1f : 1f) * (3.7f + 0.8f * waitIdx), 0, 2.8f);
            var p = h.tr.localPosition; p.y = 0;
            var np = Vector3.MoveTowards(p, target, h.speed * dt);
            bool moving = (np - p).sqrMagnitude > 1e-6f;
            h.bob += dt * (moving ? 12f : 3f);
            bool working = !moving && !h.leaving && h.station >= 0 && RestLeft <= 0f;
            np.y = moving ? Mathf.Abs(Mathf.Sin(h.bob)) * 0.12f : working ? Mathf.Abs(Mathf.Sin(t * 9f + k)) * 0.05f : 0f;
            h.tr.localPosition = np;
            if (h.armL && h.armR)
            {
                if (working) { h.armL.localRotation = Quaternion.Euler(60 + 30 * Mathf.Sin(t * 12f), -10, 0); h.armR.localRotation = Quaternion.Euler(60 - 30 * Mathf.Sin(t * 12f), 10, 0); }
                else { float sw = moving ? 30f * Mathf.Sin(h.bob) : 5f * Mathf.Sin(h.bob); h.armL.localRotation = Quaternion.Euler(sw, 0, 0); h.armR.localRotation = Quaternion.Euler(-sw, 0, 0); }
            }
            if (working)
            {
                h.workT -= dt;
                if (h.workT <= 0f)
                {
                    h.workT = helperWorkTime;
                    if (h.station == 0 && h.needSquid > 0)
                    {
                        h.needSquid--;
                        Produce(true, 1, F("Grill").position + Vector3.up * 0.9f);
                        SquidSfx.Play("sizzle", 0.45f, Random.Range(1.05f, 1.15f), 0.15f);
                    }
                    else if (h.station == 1 && h.needSauce > 0)
                    {
                        if (SauceRoom()) { h.needSauce--; Produce(false, 1, mortar.position + Vector3.up * 0.9f); SquidSfx.Play("pound", 0.55f, 1f, 0.1f); stirAnim = 1f; }
                        else if (Random.value < 0.1f) Pop("ถาดเต็ม! รอขายก่อน", sauceItems.position + Vector3.up * 0.7f, new Color(0.6f, 0.62f, 0.75f));
                    }
                    bool doneHere = h.station == 0 ? h.needSquid <= 0 : h.needSauce <= 0;
                    if (doneHere) { stationBusy[h.station] = null; h.station = -1; }
                }
            }
            if (h.leaving && Mathf.Abs(np.x) > 6.2f)
            {
                if (h.tag) Destroy(h.tag.gameObject);
                ReleaseTex(h);
                Destroy(h.go); helpers.RemoveAt(k);
                continue;
            }
            UpdateTag(h);
        }
    }

    // ------------------------------------------------------------------ lucky wheel (random help or prank)
    readonly Queue<SquidGiftDirector.StallEvent> wheelQueue = new Queue<SquidGiftDirector.StallEvent>();
    RectTransform wheelRoot, wheelDisc; Text wheelTitle, wheelResult; RawImage wheelPic; Text wheelInitial;
    static readonly SquidAction[] WheelSeg = { SquidAction.HelpSet, SquidAction.Customer, SquidAction.HelpSet, SquidAction.Customer,
                                               SquidAction.HelpSet, SquidAction.Customer, SquidAction.HelpSet, SquidAction.HelpSet };
    static Color SegColor(int i)
    {
        switch (WheelSeg[i])
        {
            case SquidAction.GrillSquid: return new Color(1f, 0.66f, 0.3f);
            case SquidAction.MakeSauce: return new Color(0.55f, 0.82f, 0.32f);
            case SquidAction.HelpSet: return new Color(0.42f, 0.78f, 0.95f);
            default: return i % 2 == 1 ? new Color(0.95f, 0.38f, 0.36f) : new Color(1f, 0.5f, 0.5f);
        }
    }
    static string SegLabel(SquidAction a) => a == SquidAction.GrillSquid ? "ช่วยย่าง" : a == SquidAction.MakeSauce ? "ช่วยตำ" : a == SquidAction.HelpSet ? "ช่วยทำ" : "แกล้ง!";

    void BuildWheel()
    {
        const int S = 512; int n = WheelSeg.Length;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f, r = Mathf.Sqrt(dx * dx + dy * dy) / (S / 2f);
            Color c;
            if (r > 1f) c = Color.clear;
            else if (r > 0.92f) c = new Color(0.55f, 0.3f, 0.15f);
            else
            {
                float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg; if (ang < 0) ang += 360f;
                int seg = Mathf.Min(n - 1, (int)(ang / (360f / n)));
                float edge = Mathf.Abs(Mathf.DeltaAngle(ang, Mathf.Round(ang / (360f / n)) * (360f / n)));
                c = edge * Mathf.Deg2Rad * r * (S / 2f) < 3f ? Color.white : SegColor(seg);
                c = Color.Lerp(c, Color.white, Mathf.Clamp01((r - 0.86f) * 4f) * 0.25f);
            }
            float a = Mathf.Clamp01((1f - r) * (S / 2f) + 0.5f);
            c.a = Mathf.Min(c.a, a); px[y * S + x] = c;
        }
        tex.SetPixels(px); tex.Apply();
        wheelRoot = PTVUI.Rect("LuckyWheel", hudRt, new Vector2(0, -40), new Vector2(620, 760));
        wheelRoot.gameObject.SetActive(false);
        PTVUI.Img("WheelCard", wheelRoot, "fr_skill_card", new Vector2(0, 30), new Vector2(680, 820), null, 1f);
        var shade = PTVUI.Img("Shade", wheelRoot, null, new Vector2(0, -10), new Vector2(640, 640), new Color(0.2f, 0.1f, 0.03f, 0.25f)); shade.sprite = circle;
        wheelDisc = PTVUI.Rect("Disc", wheelRoot, new Vector2(0, -20), new Vector2(560, 560));
        var disc = wheelDisc.gameObject.AddComponent<RawImage>(); disc.texture = tex; disc.raycastTarget = false;
        for (int i = 0; i < n; i++)
        {
            float a = (i + 0.5f) * 360f / n;
            var lab = PTVUI.Txt("Seg" + i, wheelDisc, SegLabel(WheelSeg[i]), new Vector2(Mathf.Sin(a * Mathf.Deg2Rad), Mathf.Cos(a * Mathf.Deg2Rad)) * 175f, new Vector2(200, 60), 34, Color.white);
            lab.rectTransform.localRotation = Quaternion.Euler(0, 0, -a);
        }
        var hub = PTVUI.Img("Hub", wheelRoot, null, new Vector2(0, -20), new Vector2(150, 150), new Color(1f, 0.96f, 0.88f)); hub.sprite = circle;
        var hubO = hub.gameObject.AddComponent<Outline>(); hubO.effectColor = new Color(0.55f, 0.3f, 0.15f); hubO.effectDistance = new Vector2(5, -5);
        var maskImg = PTVUI.Img("PicMask", hub.transform, null, Vector2.zero, new Vector2(128, 128), new Color(1f, 0.8f, 0.6f)); maskImg.sprite = circle;
        maskImg.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        wheelPic = PTVUI.Rect("Pic", maskImg.transform, Vector2.zero, new Vector2(128, 128)).gameObject.AddComponent<RawImage>(); wheelPic.enabled = false;
        wheelInitial = PTVUI.Txt("Initial", maskImg.transform, "?", Vector2.zero, new Vector2(128, 128), 60, Color.white);
        var pointer = PTVUI.Img("Pointer", wheelRoot, null, new Vector2(0, 262), new Vector2(64, 64), new Color(0.95f, 0.25f, 0.25f));
        pointer.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        var po = pointer.gameObject.AddComponent<Outline>(); po.effectColor = Color.white; po.effectDistance = new Vector2(5, -5);
        var tip = PTVUI.Img("PointerTip", wheelRoot, null, new Vector2(0, 285), new Vector2(70, 40), new Color(0.95f, 0.25f, 0.25f)); tip.sprite = pill;
        wheelTitle = PTVUI.Txt("Title", wheelRoot, "", new Vector2(0, 345), new Vector2(1000, 70), 46, Color.white);
        wheelTitle.rectTransform.sizeDelta = new Vector2(620, 80);
        wheelTitle.resizeTextForBestFit = true; wheelTitle.resizeTextMinSize = 22; wheelTitle.resizeTextMaxSize = 42;
        wheelTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
        wheelResult = PTVUI.Txt("Result", wheelRoot, "", new Vector2(0, -350), new Vector2(1000, 90), 56, Color.white);
        wheelResult.rectTransform.sizeDelta = new Vector2(620, 110);
        wheelResult.resizeTextForBestFit = true; wheelResult.resizeTextMinSize = 26; wheelResult.resizeTextMaxSize = 44;
        wheelResult.horizontalOverflow = HorizontalWrapMode.Wrap;
        foreach (var tx in new[] { wheelTitle, wheelResult }) { var o = tx.GetComponent<Outline>(); if (o) { o.effectColor = new Color(0.45f, 0.2f, 0.06f); o.effectDistance = new Vector2(5, -5); } }
    }

    IEnumerator WheelRunner()
    {
        while (this)
        {
            if (wheelQueue.Count == 0 || IsWaiting) { yield return null; continue; }
            var e = wheelQueue.Peek();
            if (--e.repeats <= 0) wheelQueue.Dequeue();
            if (!wheelRoot) BuildWheel();
            int n = WheelSeg.Length;
            int seg = Random.Range(0, n);
            int amount = Random.Range(Mathf.Max(1, e.min), Mathf.Max(e.min, e.max) + 1);
            string nm = e.sender != null ? e.sender.DisplayName : "?";
            wheelTitle.text = nm + "  สุ่มช่วย/แกล้ง " + SquidGiftDirector.RandomSize(e.slot) + "!";
            wheelResult.text = "";
            wheelPic.enabled = false; wheelInitial.enabled = true;
            wheelInitial.text = string.IsNullOrEmpty(nm) ? "?" : System.Globalization.StringInfo.GetNextTextElement(nm).ToUpperInvariant();
            if (e.sender != null)
            {
                var urls = new List<string>();
                if (!string.IsNullOrEmpty(e.sender.profilePictureUrl)) urls.Add(e.sender.profilePictureUrl);
                if (e.sender.profilePictureUrls != null) urls.AddRange(e.sender.profilePictureUrls);
                if (urls.Count > 0) StartCoroutine(PTVProfileCache.Get().Fetch(urls, tx =>
                {
                    if (!tx || !wheelPic) return;
                    wheelPic.texture = tx; wheelPic.enabled = true; wheelInitial.enabled = false;
                    float asp = (float)tx.width / tx.height;
                    wheelPic.uvRect = asp > 1 ? new Rect((1 - 1 / asp) / 2, 0, 1 / asp, 1) : new Rect(0, (1 - asp) / 2, 1, asp);
                }));
            }
            wheelRoot.gameObject.SetActive(true);
            wheelRoot.SetAsLastSibling();
            PTVAudio.Sfx("open", 0.6f, 1.2f);
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.25f) { wheelRoot.localScale = Vector3.one * Mathf.Lerp(0.25f, 0.78f, Mathf.SmoothStep(0, 1, k)); yield return null; }
            wheelRoot.localScale = Vector3.one * 0.78f;
            float target = 360f * 5f + (seg + 0.5f) * 360f / n + Random.Range(-14f, 14f);
            float dur = 2.8f, tt = 0f; int lastTick = 0;
            while (tt < dur)
            {
                tt += Time.deltaTime;
                float u = Mathf.Clamp01(tt / dur), ang = target * (1f - Mathf.Pow(1f - u, 3f));
                wheelDisc.localRotation = Quaternion.Euler(0, 0, ang);
                int tick = (int)(ang / (360f / n));
                if (tick != lastTick) { lastTick = tick; PTVAudio.Sfx("tick", 0.35f, 1.4f, 0.03f); }
                yield return null;
            }
            var act = WheelSeg[seg];
            bool help = act != SquidAction.Customer;
            wheelResult.text = help ? SquidGiftDirector.Describe(act, amount) + "!" : "แกล้ง! ลูกค้าสั่ง " + amount + " ชุด";
            wheelResult.color = help ? new Color(0.65f, 1f, 0.5f) : new Color(1f, 0.55f, 0.5f);
            PTVAudio.Sfx(help ? "sparkle" : "deny", 0.7f);
            for (float k = 0; k < 1.3f; k += Time.deltaTime) { wheelResult.rectTransform.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(k * 14f) * Mathf.Exp(-k * 3f)); yield return null; }
            if (help) AddHelper(e.sender, act == SquidAction.MakeSauce ? 0 : amount, act == SquidAction.GrillSquid ? 0 : amount);
            else AddCustomer(e.sender, amount);
            WheelResolved?.Invoke(act, amount, e.slot);
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.2f) { wheelRoot.localScale = Vector3.one * Mathf.Lerp(0.78f, 0.15f, k); yield return null; }
            wheelRoot.gameObject.SetActive(false);
            if (wheelPic) wheelPic.texture = null;
        }
    }

    // ------------------------------------------------------------------ flying things
    void FlyItem(bool squid, Vector3 from, Vector3 to, float dur)
    {
        if (flyers.Count > 60) return;
        var go = squid ? SquidModels.Squid(null, "Flyer") : SquidModels.SauceCup(null, "Flyer");
        if (squid) { SquidModels.CookSquid(go, 1f, 1f, 0f, mpb); go.transform.localScale = Vector3.one * 0.8f; }
        else go.transform.localScale = Vector3.one * 1.2f;
        go.transform.position = from;
        flyers.Add(new Flyer { go = go, a = from, b = to, dur = dur * Random.Range(0.85f, 1.15f), arc = Random.Range(0.6f, 1.2f), ra = Random.rotation, rb = Quaternion.identity, t = -Random.Range(0f, 0.25f) });
    }

    void Fly(GameObject go, Vector3 from, Vector3 to, float dur, Quaternion ra, Quaternion rb, System.Action done, bool keep = true, float arc = 0.8f)
        => flyers.Add(new Flyer { go = go, a = from, b = to, dur = dur, arc = arc, ra = ra, rb = rb, done = done, keep = keep });

    void UpdateFlyers(float dt)
    {
        for (int i = flyers.Count - 1; i >= 0; i--)
        {
            var f = flyers[i];
            if (!f.go) { flyers.RemoveAt(i); continue; }
            f.t += dt / f.dur;
            float t = Mathf.Clamp01(f.t);
            float e = 1f - (1f - t) * (1f - t);
            f.go.transform.position = Vector3.Lerp(f.a, f.b, e) + Vector3.up * Mathf.Sin(t * Mathf.PI) * f.arc;
            f.go.transform.rotation = Quaternion.Slerp(f.ra, f.rb, e);
            f.go.SetActive(f.t >= 0f);
            if (f.t >= 1f)
            {
                flyers.RemoveAt(i);
                if (f.done != null) f.done(); else Destroy(f.go);
            }
        }
    }

    // ------------------------------------------------------------------ HUD + cute bubbles
    sealed class Bubble { public RectTransform rt; public Text text; public Image bg; public Vector3 world; public float age, life = -1f; public bool dying; }
    readonly List<Bubble> bubbles = new List<Bubble>();

    Bubble MakeBubble(string text, Color color, float fontSize = 32f)
    {
        var rt = PTVUI.Rect("Bubble", bubbleLayer, Vector2.zero, new Vector2(260, 86));
        rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0f);
        var bg = rt.gameObject.AddComponent<Image>(); bg.sprite = pill; bg.type = Image.Type.Sliced; bg.color = color; bg.raycastTarget = false;
        var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = new Color(1f, 1f, 1f, 0.95f); o.effectDistance = new Vector2(4, -4);
        var sh = rt.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0.3f, 0.15f, 0.05f, 0.35f); sh.effectDistance = new Vector2(0, -6);
        var tail = PTVUI.Img("Tail", rt, null, new Vector2(0, -2), new Vector2(26, 26), color); tail.rectTransform.anchorMin = tail.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        tail.rectTransform.localRotation = Quaternion.Euler(0, 0, 45); tail.transform.SetAsFirstSibling();
        var t = PTVUI.Txt("Text", rt, text, new Vector2(0, 4), new Vector2(400, 80), (int)fontSize, Color.white);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        var b = new Bubble { rt = rt, text = t, bg = bg };
        bubbles.Add(b);
        Resize(b);
        return b;
    }

    static void Resize(Bubble b) { b.rt.sizeDelta = new Vector2(Mathf.Max(100, b.text.preferredWidth + 44), 66); }

    void Show(ref Bubble b, string text, Color color)
    {
        if (b != null && !b.dying) { if (b.text.text != text) { b.text.text = text; Resize(b); } return; }
        b = MakeBubble(text, color);
    }

    void Hide(ref Bubble b) { if (b != null) { b.dying = true; b.age = 0f; } b = null; }

    /// <summary>A short cute word that pops up and floats away.</summary>
    void Pop(string text, Vector3 world, Color color, float life = 1.1f)
    {
        var b = MakeBubble(text, color, 30f); b.world = world; b.life = life;
    }

    void UpdateBubbles(float dt)
    {
        for (int i = bubbles.Count - 1; i >= 0; i--)
        {
            var b = bubbles[i];
            b.age += dt;
            float scale;
            if (b.dying)
            {
                scale = Mathf.Lerp(1f, 0f, b.age / 0.18f);
                if (b.age >= 0.18f) { Destroy(b.rt.gameObject); bubbles.RemoveAt(i); continue; }
            }
            else if (b.life > 0f)
            {
                b.world += Vector3.up * dt * 0.8f;
                scale = b.age < 0.15f ? Mathf.Lerp(0.3f, 1.15f, b.age / 0.15f) : 1f;
                var cg = b.bg.color; cg.a = Mathf.Clamp01((b.life - b.age) / 0.3f); b.bg.color = cg;
                var tc = b.text.color; tc.a = cg.a; b.text.color = tc;
                if (b.age >= b.life) { Destroy(b.rt.gameObject); bubbles.RemoveAt(i); continue; }
            }
            else
            {
                // pop in with a little overshoot, then breathe
                scale = b.age < 0.25f ? Mathf.Lerp(0.2f, 1.18f, b.age / 0.25f) : 1f + 0.06f * Mathf.Sin((b.age - 0.25f) * 6f);
            }
            b.rt.localScale = Vector3.one * scale;
            Place(b.rt, b.world + Vector3.up * (b.life > 0f ? 0f : 0.05f * Mathf.Sin(b.age * 4f)));
        }
    }

    void Place(RectTransform rt, Vector3 world)
    {
        if (!cam) return;
        var sp = cam.WorldToScreenPoint(world);
        rt.gameObject.SetActive(sp.z > 0);
        float k = hudRt.rect.width / Mathf.Max(1, Screen.width);
        float half = rt.sizeDelta.x * 0.5f * rt.localScale.x + 12f, w = hudRt.rect.width;
        rt.anchoredPosition = new Vector2(Mathf.Clamp(sp.x * k, half, Mathf.Max(half, w - half)), sp.y * k);
    }

    void BuildHud()
    {
        pill = pill ? pill : MakeRounded(64, 30);
        circle = circle ? circle : MakeRounded(128, 64, false);
        hud = PTVUI.MakeCanvas("PTV_SquidHUD", 100, transform);
        var sc = hud.GetComponent<CanvasScaler>(); sc.referenceResolution = new Vector2(1080, 1920); sc.matchWidthOrHeight = 0f; sc.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        hud.GetComponent<GraphicRaycaster>().enabled = false;
        hudRt = (RectTransform)hud.transform;
        var root = hud.transform;
        bubbleLayer = PTVUI.Rect("Bubbles", root, Vector2.zero, Vector2.zero); PTVUI.Stretch(bubbleLayer);

        // score bar (top)
        var bar = PTVUI.Rect("Score", root, Vector2.zero, new Vector2(860, 110));
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1f); bar.anchoredPosition = new Vector2(50, -78);
        squidText = Chip(bar, new Vector2(-150, 0), new Color(1f, 0.6f, 0.3f));
        sauceText = Chip(bar, new Vector2(150, 0), new Color(0.48f, 0.78f, 0.3f));
        owedText = PTVUI.Txt("Owed", root, "", Vector2.zero, new Vector2(1000, 60), 34, new Color(1f, 0.92f, 0.85f));
        var orr = owedText.rectTransform; orr.anchorMin = orr.anchorMax = new Vector2(0.5f, 0f); orr.anchoredPosition = new Vector2(0, 60);

        banner = PTVUI.Txt("Banner", root, "", new Vector2(0, 120), new Vector2(1060, 170), 120, new Color(1f, 0.85f, 0.3f));
        sub = PTVUI.Txt("BannerSub", root, "", new Vector2(0, 10), new Vector2(1000, 80), 46, Color.white);
        foreach (var t in new[] { banner, sub }) { var o = t.GetComponent<Outline>(); if (o) { o.effectColor = new Color(0.55f, 0.25f, 0.08f); o.effectDistance = new Vector2(5, -5); } }
    }

    Text Chip(RectTransform parent, Vector2 pos, Color color)
    {
        var bg = PTVUI.Rect("Chip", parent, pos, new Vector2(270, 96)).gameObject.AddComponent<Image>();
        bg.sprite = pill; bg.type = Image.Type.Sliced; bg.color = color; bg.raycastTarget = false;
        var o = bg.gameObject.AddComponent<Outline>(); o.effectColor = Color.white; o.effectDistance = new Vector2(5, -5);
        var s = bg.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0.3f, 0.15f, 0.05f, 0.35f); s.effectDistance = new Vector2(0, -7);
        var t = PTVUI.Txt("Text", bg.transform, "", new Vector2(0, 3), new Vector2(260, 90), 40, Color.white);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        return t;
    }

    void ShowBanner(string big, string small, float seconds) { banner.text = big; sub.text = small; bannerT = seconds; }

    void UpdateHud(float dt)
    {
        hud.enabled = !IsWaiting;
        squidText.text = "ปลาหมึก " + Squid;
        sauceText.text = "น้ำจิ้ม " + Sauce;
        int os = 0, ou = 0; foreach (var c in customers) if (!c.leaving) { os += c.needSquid; ou += c.needSauce; }
        owedText.text = os + ou > 0 ? "ลูกค้ารอ  ปลาหมึก " + os + "  ·  น้ำจิ้ม " + ou : "";

        if (RestLeft > 0f && bannerT <= 0f) { banner.text = "ปิดร้านไปพัก"; sub.text = "เปิดอีก " + Mathf.CeilToInt(RestLeft) + " วิ"; }
        else if (bannerT > 0f) bannerT -= dt;
        else if (RestLeft <= 0f) { banner.text = ""; sub.text = ""; }
        float pop = bannerT > 0f ? 1f + 0.08f * Mathf.Sin(Time.time * 10f) : 1f;
        banner.rectTransform.localScale = Vector3.one * pop;
        UpdateBubbles(dt);
    }

    // ------------------------------------------------------------------ customer name tags (profile picture + need)
    void MakeTag(Customer c)
    {
        var tag = PTVUI.Rect("Tag", bubbleLayer, Vector2.zero, new Vector2(220, 200));
        tag.anchorMin = tag.anchorMax = Vector2.zero; tag.pivot = new Vector2(0.5f, 0f);
        tag.SetAsFirstSibling();
        var ring = PTVUI.Img("Ring", tag, null, new Vector2(0, 0), new Vector2(104, 104), Color.white); ring.sprite = circle;
        var maskImg = PTVUI.Img("Mask", ring.transform, null, Vector2.zero, new Vector2(92, 92), new Color(1f, 0.75f, 0.55f));
        maskImg.sprite = circle; maskImg.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        c.pic = PTVUI.Rect("Pic", maskImg.transform, Vector2.zero, new Vector2(92, 92)).gameObject.AddComponent<RawImage>(); c.pic.enabled = false;
        string nm = c.who != null ? c.who.DisplayName : "?";
        c.initial = PTVUI.Txt("Initial", maskImg.transform, string.IsNullOrEmpty(nm) ? "?" : System.Globalization.StringInfo.GetNextTextElement(nm).ToUpperInvariant(), Vector2.zero, new Vector2(92, 92), 46, Color.white);
        var name = PTVUI.Txt("Name", tag, nm, new Vector2(0, -62), new Vector2(260, 40), 26, Color.white);
        name.horizontalOverflow = HorizontalWrapMode.Overflow;
        ((PTVText)name).translate = false;
        c.needBg = PTVUI.Img("NeedBg", tag, null, new Vector2(0, 80), new Vector2(170, 54), new Color(1f, 0.45f, 0.45f)); c.needBg.sprite = pill; c.needBg.type = Image.Type.Sliced;
        var o = c.needBg.gameObject.AddComponent<Outline>(); o.effectColor = Color.white; o.effectDistance = new Vector2(3, -3);
        c.need = PTVUI.Txt("Need", c.needBg.transform, "", new Vector2(0, 2), new Vector2(170, 52), 28, Color.white); c.need.horizontalOverflow = HorizontalWrapMode.Overflow;
        c.tag = tag;
        if (c.who != null) StartCoroutine(LoadPic(c));
    }

    IEnumerator LoadPic(Customer c)
    {
        var urls = new List<string>();
        if (!string.IsNullOrEmpty(c.who.profilePictureUrl)) urls.Add(c.who.profilePictureUrl);
        if (c.who.profilePictureUrls != null) foreach (var u in c.who.profilePictureUrls) if (!urls.Contains(u)) urls.Add(u);
        if (urls.Count == 0) yield break;
        yield return PTVProfileCache.Get().Fetch(urls, t =>
        {
            if (!t || c.go == null || !c.pic) return;
            c.tex = t; PTVProfileCache.Get().Retain(t);
            c.pic.texture = t; c.pic.enabled = true; c.initial.enabled = false;
            float aspect = (float)t.width / t.height;
            c.pic.uvRect = aspect > 1 ? new Rect((1 - 1 / aspect) / 2, 0, 1 / aspect, 1) : new Rect(0, (1 - aspect) / 2, 1, aspect);
        });
    }

    void ReleaseTex(Customer c) { if (c.tex && PTVProfileCache.Existing) PTVProfileCache.Existing.Release(c.tex); c.tex = null; }

    void UpdateTag(Customer c)
    {
        if (!c.tag) return;
        Place(c.tag, c.tr.position + Vector3.up * (c.helper ? 2.2f : 1.95f));
        int left = Mathf.Max(c.needSquid, c.needSauce);
        if (c.helper)
        {
            c.need.text = c.leaving ? "บ๊ายบาย!" : c.needSquid > 0 ? "ช่วยย่าง " + c.needSquid : "ช่วยตำ " + c.needSauce;
            c.needBg.color = c.leaving ? new Color(0.45f, 0.8f, 0.4f) : new Color(0.35f, 0.62f, 0.95f);
            return;
        }
        c.need.text = c.leaving ? "ขอบคุณจ้า!" : (c.needSquid > 0 ? "หมึก " + c.needSquid : "") + (c.needSquid > 0 && c.needSauce > 0 ? " · " : "") + (c.needSauce > 0 ? "จิ้ม " + c.needSauce : "");
        c.needBg.rectTransform.sizeDelta = new Vector2(Mathf.Max(150, c.need.preferredWidth + 36), 54);
        c.needBg.color = c.leaving ? new Color(0.45f, 0.8f, 0.4f) : new Color(1f, 0.45f, 0.45f);
    }

    /// <summary>White rounded-rectangle sprite (9-sliced) - or a circle when slice = false.</summary>
    static Sprite MakeRounded(int size, int radius, bool slice = true)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = radius;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float cx = Mathf.Clamp(x + 0.5f, r, size - r), cy = Mathf.Clamp(y + 0.5f, r, size - r);
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
            t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d + 0.5f)));
        }
        t.Apply();
        var border = slice ? new Vector4(radius, radius, radius, radius) : Vector4.zero;
        return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
    }
}


