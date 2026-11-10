using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// PTVgame VIP check. The PTVgame Launcher starts the game with --ptv-ticket=&lt;ticket&gt;: a short-lived ticket
/// signed by the PTVgame server (RSA-SHA256). The game only holds the public key, so a ticket cannot be faked,
/// and an old one stops working after ten minutes. Without a valid ticket the game stays covered by a notice
/// asking the player to open it from the Launcher with VIP. Runs by itself; no scene setup is needed.
/// </summary>
public sealed class PTVLicense : MonoBehaviour
{
    public const string GameId = "ptvfood";
    /// <summary>Standalone builds require a signed PTVFOOD ticket from the VIP Launcher.</summary>
    public const bool Enforce = true;
    // Public key of the play-ticket service (base64url modulus / exponent, kid "play-v1").
    const string KeyN = "yuD0PJvY8G_-qQmFmc5GbRkbASrXyhKYsyXWXit_KDqnTcXkvYZYWJizJdah_0zYgWZUApou4nfcdGqqkgMn7v5dCIzTF-fMnMZnpsf3HIzJHwNIdOzXUKuVnKhwB8woTiuHp4OlUpN_yF4Ioj6EQQnO-bQ-tt27GpYi_AIjHDGA7IRZeolR19sxJPCbpFkD_QmON7dvn316txwPPWcMjAiMH7Tm3rAqiTyizoxlOccDSE_57Sx739m0DxuKlz3RAUvwaX0ibLyPysumu7ZSB6ij0Wxt5fwH0DjMcv1BR-ZJbvW_vX_95WCKgwjJcRB9NGoAsOeTx17UM0XZmh7Cvw";
    const string KeyE = "AQAB";
    const long ClockSlackSeconds = 60;   // tolerate a PC clock that is a little off

    [Serializable] public sealed class Claims { public int v; public string kid, game, name, role, vip_until, nonce; public long ptv_id, iat, exp; public bool admin; }

    public static bool Allowed { get; private set; }
    public static Claims Player { get; private set; }
    public static string Problem { get; private set; } = "";

    static DateTime accessUntil = DateTime.MaxValue;
    static PTVLicense instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && PTV_RELEASE_QA)
        Allowed = true; return;            // play mode in the editor is never locked
#else
        // A verified session survives scene reloads; VIP expiry is still checked every minute.
        if (instance) return;
        if (!Enforce) { Allowed = true; return; }
        string ticket = FindTicket();
        Claims c; string why;
        Allowed = Check(ticket, GameId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out c, out why);
        Player = c; Problem = why;
        if (Allowed && c != null && !c.admin && !string.IsNullOrEmpty(c.vip_until))
        {
            DateTime t; if (DateTime.TryParse(c.vip_until, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out t)) accessUntil = t;
        }
        if (!instance) { var go = new GameObject("PTVLicense"); DontDestroyOnLoad(go); instance = go.AddComponent<PTVLicense>(); }
        if (!Allowed) instance.Lock(why);
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
        return CheckWithKey(ticket, game, nowUnix, KeyN, KeyE, out claims, out problem);
    }

    // Separate key parameter permits deterministic cryptographic tests without production private keys.
    internal static bool CheckWithKey(string ticket, string game, long nowUnix, string keyN, string keyE, out Claims claims, out string problem)
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
                rsa.ImportParameters(new RSAParameters { Modulus = B64Url(keyN), Exponent = B64Url(keyE) });
                using (var sha = SHA256.Create())
                    if (!rsa.VerifyData(Encoding.ASCII.GetBytes(head), sha, sig)) { problem = "bad-signature"; return false; }
            }
        }
        catch { problem = "bad-signature"; return false; }
        try { claims = JsonUtility.FromJson<Claims>(Encoding.UTF8.GetString(body)); } catch { claims = null; }
        if (claims == null || claims.v != 1 || claims.kid != "play-v1" || claims.ptv_id <= 0 || string.IsNullOrEmpty(claims.nonce)) { problem = "bad-ticket"; return false; }
        if (claims.game != game) { problem = "wrong-game"; return false; }
        if (claims.iat <= 0 || claims.exp <= claims.iat || claims.exp - claims.iat > 600 || nowUnix > claims.exp + ClockSlackSeconds || nowUnix + ClockSlackSeconds < claims.iat) { problem = "expired"; return false; }
        if (!claims.admin)
        {
            DateTime until;
            if (string.IsNullOrEmpty(claims.vip_until) || !DateTime.TryParse(claims.vip_until, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out until)
                || until <= DateTimeOffset.FromUnixTimeSeconds(nowUnix).UtcDateTime) { problem = "no-access"; return false; }
        }
        return true;
    }

    static byte[] B64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; case 1: throw new FormatException(); }
        return Convert.FromBase64String(s);
    }

    // ------------------------------------------------------------------ lock screen
    Canvas cover;
    float nextCheck;

    void Update()
    {
        if (cover) { Time.timeScale = 0f; AudioListener.pause = true; return; }
        if (!Allowed || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 60f;
        if (DateTime.UtcNow > accessUntil) { Allowed = false; Lock("access-ended"); }   // VIP ran out during a long session
    }

    void Lock(string why)
    {
        if (cover) return;
        cover = PTVUI.MakeCanvas("PTVLicense", 32000, transform);
        var shade = PTVUI.Img("Shade", cover.transform, null, Vector2.zero, Vector2.zero, new Color(0.02f,0.07f,0.06f,0.94f), 0f, true);
        PTVUI.Stretch(shade.rectTransform);
        var panel = PTVUI.Img("Panel", cover.transform, "fr_skill_card", Vector2.zero, new Vector2(1100,620), null, 1f, true);
        string title = why == "access-ended" || why == "no-access"
            ? "สิทธิ์เล่นเกมหมดอายุแล้ว" : "เปิดเกมผ่าน PTVgame Launcher";
        string body = why == "access-ended" || why == "no-access"
            ? "ซื้อหรือต่ออายุสิทธิ์เกมปลาหมึกย่างใน Launcher แล้วเปิดเกมอีกครั้ง"
            : why == "expired" ? "ตั๋วเข้าเล่นหมดเวลาแล้ว ปิดเกมแล้วกดเล่นจาก Launcher อีกครั้ง"
            : "เข้าสู่บัญชีที่มีสิทธิ์เกมปลาหมึกย่าง แล้วกดเล่นจาก PTVgame Launcher";
        PTVUI.Txt("Title",panel.transform,title,new Vector2(0,180),new Vector2(1000,110),44,PTVUI.Ink);
        PTVUI.Txt("Body",panel.transform,body,new Vector2(0,30),new Vector2(940,160),30,PTVUI.Ink,TextAnchor.MiddleCenter,false);
        PTVUI.Btn("Launcher",panel.transform,"fr_button_close","ดาวน์โหลด Launcher",new Vector2(-210,-180),new Vector2(380,100),28,
            ()=>Application.OpenURL("https://github.com/Poom99tv/PTVgame/releases/latest"));
        PTVUI.Btn("Quit",panel.transform,"fr_button_reset","ออกจากเกม",new Vector2(230,-180),new Vector2(320,100),30,Application.Quit);
        PTVScreenLayout.Fit((RectTransform)cover.transform,1180,720);
        Time.timeScale=0f;AudioListener.pause=true;
        Debug.Log("[PTVLicense] locked: "+why);
    }
}

