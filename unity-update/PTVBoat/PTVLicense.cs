using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

// Verifies the PTVgame Launcher ticket with the same public key and claims rules as Zombie Rush.
public sealed class PTVLicense : MonoBehaviour
{
    public const string GameId = "ptvboat";
    public const string GameName = "PTVBoat";
    // Public key of the play-ticket service (base64url modulus / exponent, kid "play-v1").
    const string KeyN = "yuD0PJvY8G_-qQmFmc5GbRkbASrXyhKYsyXWXit_KDqnTcXkvYZYWJizJdah_0zYgWZUApou4nfcdGqqkgMn7v5dCIzTF-fMnMZnpsf3HIzJHwNIdOzXUKuVnKhwB8woTiuHp4OlUpN_yF4Ioj6EQQnO-bQ-tt27GpYi_AIjHDGA7IRZeolR19sxJPCbpFkD_QmON7dvn316txwPPWcMjAiMH7Tm3rAqiTyizoxlOccDSE_57Sx739m0DxuKlz3RAUvwaX0ibLyPysumu7ZSB6ij0Wxt5fwH0DjMcv1BR-ZJbvW_vX_95WCKgwjJcRB9NGoAsOeTx17UM0XZmh7Cvw";
    const string KeyE = "AQAB";
    const long ClockSlackSeconds = 15 * 60;   // tolerate a PC clock that is a little off

    [Serializable] public sealed class Claims { public int v; public string kid, game, name, role, vip_until, nonce; public long ptv_id, iat, exp; public bool admin; }


    public static bool Allowed { get; private set; }
    public static Claims Player { get; private set; }
    public static string Problem { get; private set; } = "";
    static DateTime vipUntil = DateTime.MaxValue;
    static PTVLicense instance;
    Canvas cover;
    RectTransform panel;
    Font font;
    float nextCheck;
    int width, height;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Allowed = false; Player = null; Problem = "";
        vipUntil = DateTime.MaxValue; instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
#if UNITY_EDITOR
        Allowed = true;
#else
        Claims claims; string problem;
        Allowed = Check(FindTicket(), GameId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out claims, out problem);
        Player = claims; Problem = problem;
        if (Allowed && !claims.admin)
            vipUntil = DateTime.Parse(claims.vip_until, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        var go = new GameObject("PTVLicense");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PTVLicense>();
        if (!Allowed) instance.Lock(problem);
#endif
    }

    static string FindTicket()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--ptv-ticket=", StringComparison.Ordinal)) return args[i].Substring(13);
            if (args[i] == "--ptv-ticket" && i + 1 < args.Length) return args[i + 1];
        }
        return Environment.GetEnvironmentVariable("PTV_TICKET") ?? "";
    }

    /// <summary>Pure check, also used by the tests: signature, game, expiry and VIP status.</summary>
    public static bool Check(string ticket, string game, long nowUnix, out Claims claims, out string problem)
    {
        claims = null; problem = "";
        if (string.IsNullOrEmpty(ticket)) { problem = "no-ticket"; return false; }
        int dot = ticket.IndexOf('.');
        if (dot <= 0 || dot == ticket.Length - 1) { problem = "bad-ticket"; return false; }
        string head = ticket.Substring(0, dot);
        byte[] sig, body;
        try { sig = B64Url(ticket.Substring(dot + 1)); body = B64Url(head); } catch { problem = "bad-ticket"; return false; }
        try
        {
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.ImportParameters(new RSAParameters { Modulus = B64Url(KeyN), Exponent = B64Url(KeyE) });
                using (var sha = SHA256.Create())
                    if (!rsa.VerifyData(Encoding.ASCII.GetBytes(head), sha, sig)) { problem = "bad-signature"; return false; }
            }
        }
        catch { problem = "bad-signature"; return false; }
        try { claims = JsonUtility.FromJson<Claims>(Encoding.UTF8.GetString(body)); } catch { claims = null; }
        if (claims == null || claims.v != 1) { problem = "bad-ticket"; return false; }
        if (claims.game != game) { problem = "wrong-game"; return false; }
        if (nowUnix > claims.exp + ClockSlackSeconds || nowUnix + ClockSlackSeconds < claims.iat) { problem = "expired"; return false; }
        if (!claims.admin)
        {
            DateTime until;
            if (string.IsNullOrEmpty(claims.vip_until) || !DateTime.TryParse(claims.vip_until, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out until)
                || until <= DateTimeOffset.FromUnixTimeSeconds(nowUnix).UtcDateTime) { problem = "no-vip"; return false; }
        }
        return true;
    }

    static byte[] B64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; case 1: throw new FormatException(); }
        return Convert.FromBase64String(s);
    }


    static bool Thai => PlayerPrefs.GetInt("PTVBoat.Language", 0) == 1;

    void Update()
    {
        if (!Allowed || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 60f;
        if (DateTime.UtcNow >= vipUntil) Lock("vip-ended");
    }

    void LateUpdate()
    {
        if (!cover) return;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        if (width == Screen.width && height == Screen.height) return;
        width = Screen.width; height = Screen.height;
        var area = ((RectTransform)cover.transform).rect;
        panel.localScale = Vector3.one * Mathf.Min(1f, Mathf.Max(.1f, (area.width - 64f) / 1040f),
            Mathf.Max(.1f, (area.height - 64f) / 520f));
    }

    void Lock(string problem)
    {
        Allowed = false; Problem = problem;
        Time.timeScale = 0f; AudioListener.pause = true;
        if (cover) return;

        var go = new GameObject("PTV VIP Access", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        cover = go.GetComponent<Canvas>();
        cover.renderMode = RenderMode.ScreenSpaceOverlay; cover.sortingOrder = 32767;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        var background = Image("Input Blocker", cover.transform, Vector2.zero, Vector2.zero, new Color(.02f, .05f, .09f, 1f));
        background.anchorMin = Vector2.zero; background.anchorMax = Vector2.one;
        background.offsetMin = background.offsetMax = Vector2.zero;
        panel = Image("Panel", cover.transform, Vector2.zero, new Vector2(1040f, 520f), new Color(.06f, .14f, .22f));
        string title = Thai ? "กรุณาเปิดเกมผ่าน PTVgame Launcher" : "Open this game from PTVgame Launcher";
        string body = Thai ? GameName + " เล่นได้เมื่อเปิดจาก Launcher ด้วยบัญชี VIP" : "Launch " + GameName + " from PTVgame Launcher with a VIP account.";
        if (problem == "vip-ended" || problem == "no-vip")
        {
            title = Thai ? "ต้องมีสมาชิก VIP ที่ยังใช้งานได้" : "An active VIP membership is required";
            body = Thai ? "ต่ออายุ VIP ที่หน้าร้านค้าใน PTVgame Launcher แล้วกดเล่นอีกครั้ง" : "Renew VIP in the PTVgame Launcher store, then launch the game again.";
        }
        else if (problem == "expired")
            body = Thai ? "ตั๋วเปิดเกมหมดเวลาแล้ว ปิดเกมแล้วกดเล่นจาก Launcher อีกครั้ง" : "Your launch ticket has expired. Close this game and press Play in the Launcher again.";
        Label("Title", title, panel, new Vector2(0f, 150f), new Vector2(960f, 100f), 45, Color.white);
        Label("Body", body, panel, new Vector2(0f, 20f), new Vector2(920f, 150f), 30, new Color(.75f, .85f, .95f));
        Button("Website", "ptvgame.com", new Vector2(-190f, -150f), () => Application.OpenURL("https://ptvgame.com/"));
        Button("Quit", Thai ? "ออกจากเกม" : "Quit game", new Vector2(190f, -150f), Application.Quit);
        if (!EventSystem.current)
        {
            var events = new GameObject("VIP EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
        }
        width = height = 0;
        Debug.Log("[PTVLicense] locked: " + problem);
    }

    static RectTransform Image(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        go.GetComponent<UnityEngine.UI.Image>().color = color;
        return rect;
    }

    UnityEngine.UI.Text Label(string name, string text, Transform parent, Vector2 position, Vector2 size, int fontSize, Color color)
    {
        if (!font) font = Font.CreateDynamicFontFromOSFont(new[] { "Leelawadee UI", "Tahoma", "Segoe UI", "Arial" }, 64);
        if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        var label = go.GetComponent<UnityEngine.UI.Text>();
        label.font = font; label.fontSize = fontSize; label.color = color;
        label.text = text; label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false; label.supportRichText = false;
        label.resizeTextForBestFit = true; label.resizeTextMinSize = 20; label.resizeTextMaxSize = fontSize;
        return label;
    }

    void Button(string name, string text, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        var rect = Image(name, panel, position, new Vector2(330f, 96f), new Color(.09f, .31f, .54f));
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = rect.GetComponent<UnityEngine.UI.Image>();
        button.onClick.AddListener(action);
        Label("Label", text, rect, Vector2.zero, new Vector2(310f, 80f), 34, Color.white);
    }
}
