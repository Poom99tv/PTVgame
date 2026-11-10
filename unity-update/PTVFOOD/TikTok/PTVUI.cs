using UnityEngine;
using UnityEngine.UI;

/// <summary>Small runtime uGUI helpers shared by the TikTok start / settings screens (mint seafood theme).</summary>
public static class PTVUI
{
    static Font font;
    public static Font Font
    {
        get
        {
            if (!font) font = Resources.Load<Font>("Fonts/Mali-Bold");
            if (!font) font = Font.CreateDynamicFontFromOSFont(new[] { "Leelawadee UI", "Leelawadee", "Tahoma", "Segoe UI", "Segoe UI Symbol", "Segoe UI Emoji", "Arial" }, 64);
            if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }
    public static readonly Color Cream = new Color(1f, 0.95f, 0.82f);
    public static readonly Color Gold = new Color(1f, 0.83f, 0.3f);
    public static readonly Color Brown = new Color(0.25f, 0.13f, 0.04f);
    public static readonly Color Muted = new Color(0.86f, 0.8f, 0.66f);
    public static readonly Color Ink = new Color(0.063f, 0.333f, 0.306f);
    static bool MintTheme => Resources.Load<Sprite>("MintUI/fr_skill_card") != null;

    public static Sprite Spr(string name)
    {
        var s = Resources.Load<Sprite>("MintUI/" + name);
        if (!s) s = Resources.Load<Sprite>("SkillUI3/" + name);
        if (!s) s = Resources.Load<Sprite>("PaintUI/" + name);
        if (!s) s = Resources.Load<Sprite>("SkillUI2/" + name);
        if (!s) s = Resources.Load<Sprite>("SkillUI/" + name);
        return s;
    }

    public static Canvas MakeCanvas(string name, int order, Transform parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        if (parent) go.transform.SetParent(parent, false);
        var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
        var sc = go.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        return c;
    }

    public static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos; r.sizeDelta = size;
        return r;
    }

    public static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    /// <summary>Image. sliceScale > 0 -> 9-sliced with border shown at 1/sliceScale size.</summary>
    public static Image Img(string name, Transform parent, string sprite, Vector2 pos, Vector2 size, Color? color = null, float sliceScale = 0f, bool raycast = false)
    {
        var r = Rect(name, parent, pos, size);
        var im = r.gameObject.AddComponent<Image>();
        im.sprite = string.IsNullOrEmpty(sprite) ? null : Spr(sprite);
        im.color = color ?? Color.white;
        im.raycastTarget = raycast;
        // Native opaque fill plus a separate generated frame. Both live under
        // the same RectTransform, so pagination, resizing and visibility stay aligned.
        if (MintTheme && im.sprite && (sprite == "fr_skill_card" || sprite == "fr_status_pill"))
        {
            var frameSprite = im.sprite;
            float radius = Mathf.Max(4f, Mathf.Min(48f, Mathf.Min(size.x, size.y) * 0.2f));
            im.sprite = PTVGiftNotice.MakeRounded(64, 12, true);
            im.type = UnityEngine.UI.Image.Type.Sliced;
            im.pixelsPerUnitMultiplier = 12f / radius;
            im.color = sprite == "fr_skill_card" ? new Color(1f, 0.953f, 0.863f) : new Color(0.80f, 0.937f, 0.847f);
            var frameRect = Rect("MintFrame", r, Vector2.zero, size);
            Stretch(frameRect);
            var frame = frameRect.gameObject.AddComponent<UnityEngine.UI.Image>();
            frame.sprite = frameSprite;
            frame.type = UnityEngine.UI.Image.Type.Sliced;
            frame.fillCenter = false;
            frame.pixelsPerUnitMultiplier = Mathf.Max(1f, frameSprite.border.y / radius);
            frame.raycastTarget = false;
            return im;
        }
        if (im.sprite && im.sprite.border != Vector4.zero)
        {
            im.type = Image.Type.Sliced;
            im.pixelsPerUnitMultiplier = MintTheme
                ? Mathf.Max(1f, im.sprite.border.y / Mathf.Max(4f, Mathf.Min(48f, Mathf.Min(size.x, size.y) * 0.2f)))
                : Mathf.Max(1f, sliceScale);
        }
        return im;
    }

    public static Text Txt(string name, Transform parent, string text, Vector2 pos, Vector2 size, int fontSize, Color color,
                           TextAnchor anchor = TextAnchor.MiddleCenter, bool outline = true, FontStyle style = FontStyle.Bold)
    {
        var r = Rect(name, parent, pos, size);
        var parentImage = parent ? parent.GetComponent<UnityEngine.UI.Image>() : null;
        bool wood = parentImage && parentImage.sprite && parentImage.sprite.name == "fr_title_header";
        bool primary = parentImage && parentImage.sprite && parentImage.sprite.name == "fr_button_reset";
        if (MintTheme)
        {
            if (wood || primary) color = Cream;
            else if (color.r > 0.9f && color.g < 0.7f && color.b < 0.6f) color = new Color(0.67f, 0.22f, 0.12f, color.a);
            else color = new Color(Ink.r, Ink.g, Ink.b, color.a);
            outline = wood;
        }
        var t = r.gameObject.AddComponent<PTVText>();
        r.gameObject.AddComponent<PTVThaiShift>();
        t.font = Font; t.fontSize = fontSize; t.fontStyle = style; t.color = color; t.alignment = anchor; t.text = text;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false; t.supportRichText = true;
        if (outline)
        {
            var o = r.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.16f, 0.07f, 0.01f, 0.95f); o.effectDistance = new Vector2(2, -2);
            var s = r.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, 0.4f); s.effectDistance = new Vector2(0, -3);
        }
        return t;
    }

    public static Button Btn(string name, Transform parent, string sprite, string label, Vector2 pos, Vector2 size, int fontSize,
                             UnityEngine.Events.UnityAction action, float sliceScale = 0f)
    {
        var im = Img(name, parent, sprite, pos, size, null, sliceScale, true);
        if (!im.sprite) im.color = new Color(0.55f, 0.27f, 0.08f);
        var b = im.gameObject.AddComponent<Button>();
        b.targetGraphic = im;
        var cb = b.colors; cb.highlightedColor = new Color(1.08f, 1.04f, 0.92f); cb.pressedColor = new Color(0.78f, 0.7f, 0.6f);
        cb.disabledColor = MintTheme ? new Color(0.94f, 0.82f, 0.78f, 0.85f) : new Color(0.45f, 0.45f, 0.45f, 0.85f); cb.fadeDuration = 0.08f; b.colors = cb;
        b.onClick.AddListener(() => PTVAudio.Sfx("click", 0.55f, Random.Range(0.95f, 1.05f)));
        if (action != null) b.onClick.AddListener(action);
        if (!string.IsNullOrEmpty(label)) Txt("Label", im.transform, label, new Vector2(0, 2), size - new Vector2(30, 10), fontSize, Cream);
        b.gameObject.AddComponent<PTVHoverScale>();
        return b;
    }

    public static InputField Input(string name, Transform parent, string value, string placeholder, Vector2 pos, Vector2 size, int fontSize)
    {
        var bg = Img(name, parent, MintTheme ? "mint_input" : "fr_status_pill", pos, size, null, 2.2f, true);
        if (!bg.sprite) bg.color = new Color(0.2f, 0.15f, 0.08f);
        var input = bg.gameObject.AddComponent<InputField>();
        input.targetGraphic = bg;
        var text = Txt("Text", bg.transform, "", Vector2.zero, size - new Vector2(36, 8), fontSize, Cream, TextAnchor.MiddleCenter, false, FontStyle.Bold);
        text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Overflow;
        ((PTVText)text).translate = false;   // what the player types stays as typed
        var ph = Txt("Placeholder", bg.transform, placeholder, Vector2.zero, size - new Vector2(36, 8), fontSize, new Color(0.85f, 0.8f, 0.65f, 0.55f), TextAnchor.MiddleCenter, false, FontStyle.Italic);
        input.textComponent = text; input.placeholder = ph;
        input.lineType = InputField.LineType.SingleLine;
        input.caretColor = MintTheme ? Ink : Gold; input.customCaretColor = true; input.caretWidth = 3;
        input.selectionColor = MintTheme ? new Color(0.3f, 0.8f, 0.65f, 0.3f) : new Color(1f, 0.8f, 0.3f, 0.35f);
        input.text = value;
        return input;
    }

    public static Toggle Check(string name, Transform parent, Vector2 pos, float size)
    {
        var box = Img(name, parent, MintTheme ? "mint_checkbox" : null, pos, new Vector2(size, size),
            MintTheme ? Color.white : new Color(0.16f, 0.09f, 0.03f, 0.95f), 0f, true);
        if (!MintTheme) { var rim = box.gameObject.AddComponent<Outline>(); rim.effectColor = new Color(0.95f, 0.7f, 0.25f); rim.effectDistance = new Vector2(3, -3); }
        var t = box.gameObject.AddComponent<Toggle>();
        t.targetGraphic = box;
        var mark = Img("Check", box.transform, "AcornIcon", Vector2.zero, new Vector2(size * 0.67f, size * 0.67f), null, 0f, false);
        mark.preserveAspect = true;
        if (!mark.sprite) mark.color = Gold;
        t.graphic = mark;
        t.onValueChanged.AddListener(on => PTVAudio.Sfx("toggle", 0.45f, on ? 1.1f : 0.85f));
        return t;
    }
}

/// <summary>Little bounce when the mouse is over a button.</summary>
public sealed class PTVHoverScale : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    public float amount = 0.06f;
    bool over; float k;
    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { over = true; var s = GetComponent<Selectable>(); if (!s || s.interactable) PTVAudio.Sfx("hover", 0.3f, 1f, 0.04f); }
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { over = false; }
    void OnDisable() { over = false; k = 0f; transform.localScale = Vector3.one; }
    void Update()
    {
        var sel = GetComponent<Selectable>();
        bool on = over && (!sel || sel.interactable);
        k = Mathf.MoveTowards(k, on ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        transform.localScale = Vector3.one * (1f + amount * k);
    }
}


