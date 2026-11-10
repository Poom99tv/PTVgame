using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Lets the player drag a UI block around the screen. The position is saved (as a fraction of the screen, so it
/// survives switching between landscape and portrait) under <see cref="prefsKey"/>. Double-click = default spot.
/// </summary>
public sealed class PTVDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public string prefsKey;
    public Vector2 defaultAnchor = new Vector2(1f, 1f);     // normalized screen point of the block's pivot
    public Vector2 defaultOffset;                           // canvas units from that point
    public Image highlight;                                 // shown while hovering / dragging

    RectTransform rt, canvasRt; bool dragging, hover; Vector2 grab;

    void Awake() { rt = (RectTransform)transform; }

    RectTransform CanvasRt()
    {
        if (!canvasRt) { var c = GetComponentInParent<Canvas>(); if (c) canvasRt = (RectTransform)c.rootCanvas.transform; }
        return canvasRt;
    }

    public void Load()
    {
        if (!rt) rt = (RectTransform)transform;
        if (!string.IsNullOrEmpty(prefsKey) && PlayerPrefs.HasKey(prefsKey + ".x"))
        {
            var p = new Vector2(PlayerPrefs.GetFloat(prefsKey + ".x"), PlayerPrefs.GetFloat(prefsKey + ".y"));
            rt.anchorMin = rt.anchorMax = p; rt.anchoredPosition = Vector2.zero;
        }
        else { rt.anchorMin = rt.anchorMax = defaultAnchor; rt.anchoredPosition = defaultOffset; }
        Clamp();
    }

    void Save()
    {
        var c = CanvasRt(); if (!c || string.IsNullOrEmpty(prefsKey)) return;
        Vector2 size = c.rect.size;
        Vector2 local = (Vector2)c.InverseTransformPoint(rt.position) + size * 0.5f;    // canvas bottom-left origin
        Vector2 n = new Vector2(local.x / size.x, local.y / size.y);
        rt.anchorMin = rt.anchorMax = n; rt.anchoredPosition = Vector2.zero;
        PlayerPrefs.SetFloat(prefsKey + ".x", n.x); PlayerPrefs.SetFloat(prefsKey + ".y", n.y); PlayerPrefs.Save();
    }

    public void ResetPosition()
    {
        if (!string.IsNullOrEmpty(prefsKey)) { PlayerPrefs.DeleteKey(prefsKey + ".x"); PlayerPrefs.DeleteKey(prefsKey + ".y"); PlayerPrefs.Save(); }
        Load();
    }

    /// <summary>Keep the whole block on screen.</summary>
    public void Clamp()
    {
        var c = CanvasRt(); if (!c || !rt) return;
        Vector3[] wc = new Vector3[4]; rt.GetWorldCorners(wc);
        Vector3[] cc = new Vector3[4]; c.GetWorldCorners(cc);
        Vector3 d = Vector3.zero;
        if (wc[0].x < cc[0].x) d.x = cc[0].x - wc[0].x; else if (wc[2].x > cc[2].x) d.x = cc[2].x - wc[2].x;
        if (wc[0].y < cc[0].y) d.y = cc[0].y - wc[0].y; else if (wc[2].y > cc[2].y) d.y = cc[2].y - wc[2].y;
        if (d != Vector3.zero) rt.position += d;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        dragging = true;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rt.parent, e.position, e.pressEventCamera, out var p);
        grab = (Vector2)rt.localPosition - p;
        PTVAudio.Sfx("click", 0.35f, 1.2f);
    }
    public void OnDrag(PointerEventData e)
    {
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rt.parent, e.position, e.pressEventCamera, out var p))
            rt.localPosition = p + grab;
        Clamp();
    }
    public void OnEndDrag(PointerEventData e) { dragging = false; Clamp(); Save(); PTVAudio.Sfx("toggle", 0.35f, 0.9f); }
    public void OnPointerClick(PointerEventData e) { if (e.clickCount >= 2) ResetPosition(); }
    public void OnPointerEnter(PointerEventData e) { hover = true; }
    public void OnPointerExit(PointerEventData e) { hover = false; }

    void Update()
    {
        if (!highlight) return;
        float target = dragging ? 0.28f : hover ? 0.14f : 0f;
        var c = highlight.color; c.a = Mathf.MoveTowards(c.a, target, Time.unscaledDeltaTime * 1.5f); highlight.color = c;
    }
}
