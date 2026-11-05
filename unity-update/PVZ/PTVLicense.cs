using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PTVgame VIP check. The PTVgame Launcher starts the game with --ptv-ticket=&lt;ticket&gt;: a short-lived ticket
/// signed by the PTVgame server (RSA-SHA256). The game only holds the public key, so a ticket cannot be faked,
/// and an old one stops working after ten minutes. Without a valid ticket the game stays covered by a notice
/// asking the player to open it from the Launcher with VIP. Runs by itself; no scene setup is needed.
/// </summary>
public sealed class PTVLicense : MonoBehaviour
{
    public const string GameId = "zombierush";
    // Public key of the play-ticket service (base64url modulus / exponent, kid "play-v1").
    const string KeyN = "__PTV_KEY_N__";
    const string KeyE = "__PTV_KEY_E__";
    const long ClockSlackSeconds = 15 * 60;   // tolerate a PC clock that is a little off

    [Serializable] public sealed class Claims { public int v; public string kid, game, name, role, vip_until, nonce; public long ptv_id, iat, exp; public bool admin; }

    public static bool Allowed { get; private set; }
    public static Claims Player { get; private set; }
    public static string Problem { get; private set; } = "";

    static DateTime vipUntil = DateTime.MaxValue;
    static PTVLicense instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
#if UNITY_EDITOR
        Allowed = true; return;            // play mode in the editor is never locked
#else
        string ticket = FindTicket();
        Claims c; string why;
        Allowed = Check(ticket, GameId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out c, out why);
        Player = c; Problem = why;
        if (Allowed && c != null && !c.admin && !string.IsNullOrEmpty(c.vip_until))
        {
            DateTime t; if (DateTime.TryParse(c.vip_until, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out t)) vipUntil = t;
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

    // ------------------------------------------------------------------ lock screen
    Canvas cover;
    float nextCheck;

    void Update()
    {
        if (cover) { Time.timeScale = 0f; AudioListener.pause = true; return; }
        if (!Allowed || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 60f;
        if (DateTime.UtcNow > vipUntil) { Allowed = false; Lock("vip-ended"); }   // VIP ran out during a long session
    }

    void Lock(string why)
    {
        if (cover) return;
        cover = PTVUI.MakeCanvas("PTVLicense", 32000, transform);
        var bg = PTVUI.Img("Shade", cover.transform, null, Vector2.zero, Vector2.zero, new Color(0.02f, 0.05f, 0.09f, 0.94f), 0f, true);
        PTVUI.Stretch(bg.rectTransform);
        var panel = PTVUI.Img("Panel", cover.transform, null, Vector2.zero, new Vector2(1040, 520), new Color(0.06f, 0.14f, 0.22f, 1f), 0f, true);
        string title = why == "vip-ended" ? "VIP ของคุณหมดอายุแล้ว" : why == "no-vip" ? "เกมนี้สำหรับสมาชิก VIP" : "กรุณาเปิดเกมผ่าน PTVgame Launcher";
        string body = why == "vip-ended" || why == "no-vip"
            ? "ต่ออายุ VIP ได้ที่หน้าร้านค้าใน PTVgame Launcher แล้วกดเล่นจาก Launcher อีกครั้ง"
            : why == "expired" ? "สิทธิ์เปิดเกมหมดเวลาแล้ว ปิดเกมแล้วกดเล่นจาก PTVgame Launcher อีกครั้ง"
            : "Zombie Rush เล่นได้เมื่อเปิดจาก PTVgame Launcher ด้วยบัญชี VIP";
        PTVUI.Txt("Title", panel.transform, title, new Vector2(0, 150), new Vector2(960, 90), 52, Color.white);
        PTVUI.Txt("Body", panel.transform, body, new Vector2(0, 30), new Vector2(920, 140), 30, new Color(0.75f, 0.85f, 0.95f), TextAnchor.MiddleCenter, false, FontStyle.Normal);
        PTVUI.Btn("Website", panel.transform, null, "ptvgame.com", new Vector2(-190, -150), new Vector2(330, 96), 34, () => Application.OpenURL("https://ptvgame.com/"));
        PTVUI.Btn("Quit", panel.transform, null, "ออกจากเกม", new Vector2(190, -150), new Vector2(330, 96), 34, Application.Quit);
        Time.timeScale = 0f; AudioListener.pause = true;
        Debug.Log("[PTVLicense] locked: " + why);
    }
}
