using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Small cute gift card that pops up in the middle of the screen: round profile picture, sender name,
/// what the gift does ("ช่วยย่าง 2 ไม้" in green, a prank in red). Several gifts queue up and hurry along.
/// </summary>
[DisallowMultipleComponent]
public sealed class PTVGiftNotice : MonoBehaviour
{
    struct Item { public PTVTikTokLive.Message m; public string text; public bool help; }
    readonly Queue<Item> pending = new Queue<Item>();
    Canvas canvas; CanvasGroup group; RectTransform card;
    RawImage portrait; Image ring, gift, cardBg; Text senderLabel, actionLabel, fallback;
    Texture retained; float age, hold; int generation; bool active;
    public int ShownCount { get; private set; }
    public int PendingCount => pending.Count;
    static Sprite round, circle;
    /// <summary>Where on the table the card sits (world point). Null = middle of the screen.</summary>
    public static Vector3? WorldAnchor;

    public void Enqueue(PTVTikTokLive.Message message, string text, bool help = true)
    {
        if (message == null) return;
        if (!string.IsNullOrEmpty(text) && text.StartsWith("หมุนกงล้อ")) return;   // the lucky wheel shows its own card
        if (pending.Count >= 64) pending.Dequeue();
        pending.Enqueue(new Item { m = message, text = text, help = help });
    }

    void Build()
    {
        round = round ? round : MakeRounded(96, 46, true);
        circle = circle ? circle : MakeRounded(128, 64, false);
        canvas = PTVUI.MakeCanvas("PTV_GiftNotifications", 120, transform);
        var sc = canvas.GetComponent<CanvasScaler>(); sc.referenceResolution = new Vector2(1080, 1920); sc.matchWidthOrHeight = 0f; sc.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        canvas.GetComponent<GraphicRaycaster>().enabled = false;
        card = PTVUI.Rect("GiftCard", canvas.transform, Vector2.zero, new Vector2(560, 132));
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f); card.pivot = new Vector2(0.5f, 0.5f);
        group = card.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false; group.alpha = 0f;
        cardBg = card.gameObject.AddComponent<Image>(); cardBg.sprite = round; cardBg.type = Image.Type.Sliced; cardBg.color = new Color(1f, 0.99f, 0.96f); cardBg.raycastTarget = false;
        var ol = card.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(0.36f, 0.22f, 0.12f, 0.9f); ol.effectDistance = new Vector2(4, -4);
        var sh = card.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0.25f, 0.12f, 0.04f, 0.3f); sh.effectDistance = new Vector2(0, -9);

        // round portrait with a coloured ring
        ring = PTVUI.Img("Ring", card, null, new Vector2(-206, 0), new Vector2(112, 112), new Color(0.4f, 0.8f, 0.4f)); ring.sprite = circle;
        var mask = PTVUI.Img("Mask", ring.transform, null, Vector2.zero, new Vector2(98, 98), new Color(1f, 0.82f, 0.68f)); mask.sprite = circle;
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        portrait = PTVUI.Rect("SenderProfile", mask.transform, Vector2.zero, new Vector2(98, 98)).gameObject.AddComponent<RawImage>();
        portrait.raycastTarget = false;
        fallback = PTVUI.Txt("ProfileFallback", mask.transform, "?", Vector2.zero, new Vector2(98, 98), 48, Color.white);

        senderLabel = PTVUI.Txt("SenderName", card, "", new Vector2(40, 24), new Vector2(380, 50), 32, new Color(0.3f, 0.18f, 0.1f), TextAnchor.MiddleLeft, false);
        senderLabel.horizontalOverflow = HorizontalWrapMode.Overflow; ((PTVText)senderLabel).translate = false;
        actionLabel = PTVUI.Txt("Action", card, "", new Vector2(40, -24), new Vector2(380, 50), 34, new Color(0.25f, 0.62f, 0.2f), TextAnchor.MiddleLeft, false);
        actionLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        gift = PTVUI.Img("GiftIcon", card, null, new Vector2(232, 30), new Vector2(64, 64)); gift.preserveAspect = true;
    }

    void Update()
    {
        if (!canvas) Build();
        if (PTVStartMenu.UIBlocking) { group.alpha = 0f; return; }
        if (!active && pending.Count > 0) Show(pending.Dequeue());
        if (!active) return;
        age += Time.unscaledDeltaTime;
        if (pending.Count > 0) hold = Mathf.Min(hold, pending.Count > 4 ? 0.9f : 1.5f);   // hurry when gifts pile up
        float pop = age < 0.18f ? Mathf.Lerp(0.4f, 1.12f, age / 0.18f) : age < 0.3f ? Mathf.Lerp(1.12f, 1f, (age - 0.18f) / 0.12f) : 1f;
        float exit = Mathf.Clamp01((age - hold) / 0.22f);
        card.localScale = Vector3.one * pop * (1f - 0.3f * exit);
        Vector2 basePos = new Vector2(0, 140);
        var cam = Camera.main;
        if (WorldAnchor.HasValue && cam)
        {
            var vp = cam.WorldToViewportPoint(WorldAnchor.Value); var size = ((RectTransform)canvas.transform).rect.size;
            basePos = new Vector2((vp.x - 0.5f) * size.x, (vp.y - 0.5f) * size.y);
        }
        card.anchoredPosition = basePos + new Vector2(0, 18f * exit + 4f * Mathf.Sin(age * 3f));
        group.alpha = Mathf.Min(Mathf.Clamp01(age / 0.1f), 1f - exit);
        if (age >= hold + 0.22f) { active = false; generation++; StopAllCoroutines(); Release(); group.alpha = 0f; }
    }

    void Show(Item item)
    {
        var m = item.m;
        StopAllCoroutines(); Release(); generation++; active = true; age = 0f; hold = 2.4f; ShownCount++;
        PTVAudio.Sfx("coin", 0.5f, item.help ? 1.15f : 0.85f, 0.08f);
        senderLabel.text = m.DisplayName;
        actionLabel.text = item.text ?? "";
        var col = item.help ? new Color(0.25f, 0.62f, 0.2f) : new Color(0.9f, 0.3f, 0.25f);
        actionLabel.color = col; ring.color = item.help ? new Color(0.45f, 0.82f, 0.4f) : new Color(1f, 0.5f, 0.45f);
        string recognizedGift = PTVGiftCatalog.Contains(m.giftCanonicalName) ? m.giftCanonicalName : m.giftName;
        gift.sprite = string.IsNullOrEmpty(recognizedGift) ? null : PTVGiftCatalog.Icon(recognizedGift); gift.enabled = gift.sprite;
        // card width follows the text
        float w = Mathf.Max(senderLabel.preferredWidth, actionLabel.preferredWidth);
        card.sizeDelta = new Vector2(Mathf.Clamp(w + 210 + (gift.enabled ? 70 : 0), 380, 900), 132);
        float half = card.sizeDelta.x * 0.5f;
        ring.rectTransform.anchoredPosition = new Vector2(-half + 70, 0);
        senderLabel.rectTransform.anchoredPosition = new Vector2(-half + 135 + 190, 24);
        actionLabel.rectTransform.anchoredPosition = new Vector2(-half + 135 + 190, -24);
        gift.rectTransform.anchoredPosition = new Vector2(half - 44, 0);
        portrait.enabled = false; fallback.enabled = true;
        fallback.text = string.IsNullOrEmpty(m.DisplayName) ? "?" : System.Globalization.StringInfo.GetNextTextElement(m.DisplayName).ToUpperInvariant();
        StartCoroutine(LoadPortrait(m, generation));
    }

    IEnumerator LoadPortrait(PTVTikTokLive.Message m, int token)
    {
        var urls = new List<string>();
        if (!string.IsNullOrEmpty(m.profilePictureUrl)) urls.Add(m.profilePictureUrl);
        if (m.profilePictureUrls != null) foreach (var u in m.profilePictureUrls) if (!urls.Contains(u)) urls.Add(u);
        if (urls.Count == 0) yield break;
        yield return PTVProfileCache.Get().Fetch(urls, t =>
        {
            if (!this || token != generation || !t) return;
            retained = t; PTVProfileCache.Get().Retain(t); portrait.texture = t;
            float aspect = (float)t.width / t.height;
            portrait.uvRect = aspect > 1 ? new Rect((1 - 1 / aspect) / 2, 0, 1 / aspect, 1) : new Rect(0, (1 - aspect) / 2, 1, aspect);
            portrait.enabled = true; fallback.enabled = false;
        });
    }

    void Release() { if (retained && PTVProfileCache.Existing) PTVProfileCache.Existing.Release(retained); retained = null; if (portrait) portrait.texture = null; }
    void OnDestroy() { Release(); }

    public static Sprite MakeRounded(int size, int radius, bool slice)
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

/// <summary>Downloads + caches TikTok profile pictures (max 256 px, 4 at a time). Ported from Paint Gun.</summary>
public sealed class PTVProfileCache : MonoBehaviour
{
    static PTVProfileCache instance;
    public static PTVProfileCache Existing => instance;
    readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
    readonly Dictionary<Texture, int> uses = new Dictionary<Texture, int>();
    readonly HashSet<string> downloading = new HashSet<string>();
    public static PTVProfileCache Get()
    {
        if (!instance) { var go = new GameObject("PTV_ProfileCache"); DontDestroyOnLoad(go); instance = go.AddComponent<PTVProfileCache>(); }
        return instance;
    }
    public void Retain(Texture t) { uses.TryGetValue(t, out int c); uses[t] = c + 1; }
    public void Release(Texture t) { if (uses.TryGetValue(t, out int c)) { if (c <= 1) uses.Remove(t); else uses[t] = c - 1; } Trim(); }
    void Trim()
    {
        if (cache.Count <= 128) return;
        string victim = null;
        foreach (var p in cache) if (!uses.ContainsKey(p.Value)) { victim = p.Key; break; }
        if (victim != null) { var t = cache[victim]; cache.Remove(victim); Destroy(t); }
    }
    public IEnumerator Fetch(List<string> urls, System.Action<Texture> ready)
    {
        foreach (string url in urls)
        {
            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri) || !(uri.Scheme == "https" || uri.IsLoopback)) continue;
            while (downloading.Contains(url)) yield return null;
            if (cache.TryGetValue(url, out var saved) && saved) { ready(saved); yield break; }
            while (downloading.Count >= 4 || downloading.Contains(url)) yield return null;
            if (cache.TryGetValue(url, out saved) && saved) { ready(saved); yield break; }
            downloading.Add(url);
            try
            {
                using (var request = UnityWebRequestTexture.GetTexture(url, true))
                {
                    request.timeout = 5; request.SetRequestHeader("User-Agent", "Mozilla/5.0");
                    var op = request.SendWebRequest();
                    while (!op.isDone) { if (request.downloadedBytes > 4194304) { request.Abort(); break; } yield return null; }
                    if (request.result != UnityWebRequest.Result.Success || request.downloadedBytes > 4194304) continue;
                    var texture = DownloadHandlerTexture.GetContent(request); if (!texture) continue;
                    if (texture.width > 2048 || texture.height > 2048) { Destroy(texture); continue; }
                    if (texture.width > 256 || texture.height > 256)
                    {
                        float f = 256f / Mathf.Max(texture.width, texture.height);
                        int w = Mathf.Max(1, Mathf.RoundToInt(texture.width * f)), h = Mathf.Max(1, Mathf.RoundToInt(texture.height * f));
                        var rt = RenderTexture.GetTemporary(w, h, 0); var prev = RenderTexture.active; Graphics.Blit(texture, rt); RenderTexture.active = rt;
                        var small = new Texture2D(w, h, TextureFormat.RGB24, false); small.ReadPixels(new Rect(0, 0, w, h), 0, 0); small.Apply(false, true);
                        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); Destroy(texture); texture = small;
                    }
                    texture.wrapMode = TextureWrapMode.Clamp;
                    cache[url] = texture; ready(texture); Trim(); yield break;
                }
            }
            finally { downloading.Remove(url); }
        }
        ready(null);
    }
    void OnDestroy() { foreach (var t in cache.Values) if (t) Destroy(t); if (instance == this) instance = null; }
}
