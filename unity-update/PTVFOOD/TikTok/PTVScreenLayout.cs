using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Landscape (16:9) / TikTok portrait (9:16) screen mode.
///  - Switches the game window size (in a build; the editor follows the Game view size).
///  - Whenever the window is taller than wide, every ScaleWithScreenSize canvas is re-scaled by width so the
///    1920-wide HUD becomes 1080 units wide (corner HUD stays readable), and the big full-screen panels
///    registered with <see cref="Fit"/> are shrunk to fit the width while still covering the whole screen.
/// </summary>
[DisallowMultipleComponent]
public sealed class PTVScreenLayout : MonoBehaviour
{
    const string PrefsKey = "PTVFOOD.Portrait";
    public const float PortraitUnits = 1080f;           // canvas width (for 1920-wide designs) in portrait = 1:1 on a 1080x1920 TikTok frame

    public static PTVScreenLayout I { get; private set; }
    public static bool PortraitSetting { get => PlayerPrefs.GetInt(PrefsKey, 1) == 1; private set { PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0); PlayerPrefs.Save(); } }
    /// <summary>True when the screen is currently taller than wide.</summary>
    public static bool IsPortrait => Screen.height > Screen.width;
    public static event System.Action Changed;

    sealed class Orig { public Vector2 res; public float match; public CanvasScaler.ScreenMatchMode mode; }
    readonly Dictionary<CanvasScaler, Orig> scalers = new Dictionary<CanvasScaler, Orig>();
    sealed class FitItem { public RectTransform rt; public float designW, designH; }
    static readonly List<FitItem> fits = new List<FitItem>();

    int lastW, lastH; bool lastPortrait; float nextScan;

    void Awake() { I = this; }
    void Start() { ApplyWindow(PortraitSetting); }

    /// <summary>Shrink a full-screen (stretched) panel so a design of designW x designH fits the screen in portrait.</summary>
    public static void Fit(RectTransform stretchedPanel, float designW, float designH = 1080f)
    {
        if (!stretchedPanel) return;
        fits.Add(new FitItem { rt = stretchedPanel, designW = designW, designH = designH });
        if (I) I.ApplyFits();
    }

    public static void Toggle() { SetPortrait(!PortraitSetting); }
    public static void SetPortrait(bool portrait)
    {
        PortraitSetting = portrait;
        if (I) I.ApplyWindow(portrait);
    }

    void ApplyWindow(bool portrait)
    {
#if !UNITY_EDITOR
        int sh = Display.main.systemHeight > 0 ? Display.main.systemHeight : Screen.currentResolution.height;
        int h = Mathf.RoundToInt(sh * (portrait ? 0.9f : 0.8f));
        int w = portrait ? Mathf.RoundToInt(h * 9f / 16f) : Mathf.RoundToInt(h * 16f / 9f);
        Screen.SetResolution(w, h, FullScreenMode.Windowed);
#endif
        nextScan = 0f; lastW = -1;
    }

    void LateUpdate()
    {
        bool sizeChanged = Screen.width != lastW || Screen.height != lastH;
        if (!sizeChanged && Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.5f;            // new canvases appear over time (cutscene UI, popups...)
        lastW = Screen.width; lastH = Screen.height;
        bool portrait = IsPortrait;

        foreach (var sc in FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (sc.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;
            if (!scalers.TryGetValue(sc, out var o)) scalers[sc] = o = new Orig { res = sc.referenceResolution, match = sc.matchWidthOrHeight, mode = sc.screenMatchMode };
            if (portrait)
            {
                float designW = Mathf.Max(o.res.x, o.res.y);
                sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                sc.referenceResolution = new Vector2(designW * PortraitUnits / 1920f, o.res.y);
                sc.matchWidthOrHeight = 0f;
            }
            else { sc.referenceResolution = o.res; sc.matchWidthOrHeight = o.match; sc.screenMatchMode = o.mode; }
        }
        Canvas.ForceUpdateCanvases();
        ApplyFits();
        if (sizeChanged || portrait != lastPortrait) { lastPortrait = portrait; Changed?.Invoke(); }
    }

    void ApplyFits()
    {
        bool portrait = IsPortrait;
        for (int i = fits.Count - 1; i >= 0; i--)
        {
            var f = fits[i];
            if (!f.rt) { fits.RemoveAt(i); continue; }
            var parent = f.rt.parent as RectTransform;
            if (!parent) continue;
            Vector2 ps = parent.rect.size;
            float s = 1f;
            if (ps.x > 1f && ps.y > 1f) s = Mathf.Min(1f, ps.x / f.designW, ps.y / f.designH);
            f.rt.localScale = Vector3.one * s;
            // keep it stretched over the whole parent after scaling
            f.rt.anchorMin = Vector2.zero; f.rt.anchorMax = Vector2.one; f.rt.pivot = new Vector2(0.5f, 0.5f);
            f.rt.anchoredPosition = Vector2.zero;
            f.rt.sizeDelta = ps * (1f / s - 1f);
        }
    }
}

