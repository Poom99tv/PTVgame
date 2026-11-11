using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Persistent root for the TikTok Live system (survives scene reloads / restarts with R).
/// Holds: live connection, gift director, gift notices and the start / settings menu.
/// </summary>
public sealed class PTVLive : MonoBehaviour
{
    public static PTVLive I { get; private set; }
    public PTVTikTokLive live;
    public SquidGiftDirector director;
    public PTVGiftNotice notice;
    public PTVStartMenu menu;

    public static PTVLive Ensure()
    {
        if (I) return I;
        var go = new GameObject("PTV_TikTokLive");
        DontDestroyOnLoad(go);
        I = go.AddComponent<PTVLive>();
        I.live = go.AddComponent<PTVTikTokLive>();
        I.director = go.AddComponent<SquidGiftDirector>();
        I.notice = go.AddComponent<PTVGiftNotice>();
        go.AddComponent<PTVScreenLayout>();
        I.menu = go.AddComponent<PTVStartMenu>();
        go.AddComponent<SquidSkillHud>();
        var _ = PTVAudio.I;   // build the sound effects up front
        return I;
    }
    void OnDestroy() { if (I == this) I = null; }
}

/// <summary>
/// TikTok Live connection (ported from the Paint Gun project).
/// Starts the Node bridge (TikTokBridge/bridge.mjs, bundled runtime/node.exe) and talks to it over
/// TCP 127.0.0.1:18776 with newline-delimited JSON.
/// </summary>
[DisallowMultipleComponent]
public sealed class PTVTikTokLive : MonoBehaviour
{
    [Serializable] public sealed class Message
    {
        public string @event, eventId, userId, uniqueId, nickname, profilePictureUrl, giftName, giftId, state, detail, streamer;
        public string[] profilePictureUrls;
        public int repeatCount, giftType, likeCount;
        public bool repeatEnd, giftDelta, streakEnded; public int giftTotalCount; public string comboId;
        public string DisplayName => string.IsNullOrEmpty(nickname) ? uniqueId : nickname;
    }
    [Serializable] sealed class Command { public string command, username; }

    public const string UsernameKey = "PTVFOOD.TikTok.Username";
    public string State { get; private set; } = "idle";
    public string Detail { get; private set; } = "กรอก @ชื่อบัญชี TikTok แล้วกดเชื่อมต่อ";
    public string Streamer { get; private set; } = "";
    public bool Connected => State == "connected";
    public bool Busy => State == "connecting" || State == "reconnecting";
    public bool BridgeConnected => transportConnected;
    public int ReceivedGifts { get; private set; }
    public int DuplicateEvents { get; private set; }
    public int Port = 18776;

    readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
    readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
    readonly HashSet<string> seen = new HashSet<string>();
    readonly Queue<string> seenOrder = new Queue<string>();
    readonly Dictionary<string, Message> profiles = new Dictionary<string, Message>();
    CancellationTokenSource cancel;
    TcpClient socket;
    System.Diagnostics.Process ownedBridge;
    volatile bool transportConnected;
    bool wantsLive;
    SquidGiftDirector director;

    void OnEnable() { director = GetComponent<SquidGiftDirector>(); }

    public void ConnectLive(string username)
    {
        username = (username ?? "").Trim().TrimStart('@');
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, "^[a-zA-Z0-9_.]{1,64}$"))
        { State = "error"; Detail = "ชื่อบัญชีไม่ถูกต้อง  ใช้ได้เฉพาะ a-z 0-9 _ ."; return; }
        Streamer = username; wantsLive = true; State = "connecting"; Detail = "กำลังเชื่อมต่อ @" + username + " ...";
        PlayerPrefs.SetString(UsernameKey, username); PlayerPrefs.Save();
        if (cancel == null) { cancel = new CancellationTokenSource(); _ = ReceiveLoop(cancel.Token); }
        if (!transportConnected) StartBridge(); else SendConnect();
    }

    void StartBridge()
    {
        try
        {
            if (ownedBridge != null && !ownedBridge.HasExited) return;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../TikTokBridge"));
            if (!File.Exists(Path.Combine(folder, "bridge.mjs"))) throw new FileNotFoundException("ไม่พบโฟลเดอร์ TikTokBridge");
            string bundled = Path.Combine(folder, "runtime/node.exe");
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = File.Exists(bundled) ? bundled : "node", Arguments = "bridge.mjs", WorkingDirectory = folder,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            info.EnvironmentVariables["PAINT_BRIDGE_PORT"] = Port.ToString();
            ownedBridge = System.Diagnostics.Process.Start(info);
        }
        catch (Exception e) { State = "error"; Detail = "เปิดตัวเชื่อม TikTok ไม่ได้: " + e.Message; }
    }

    void SendConnect() { outgoing.Enqueue(JsonUtility.ToJson(new Command { command = "connect", username = Streamer })); }

    public void DisconnectLive()
    {
        wantsLive = false; outgoing.Enqueue(JsonUtility.ToJson(new Command { command = "disconnect" }));
        State = "idle"; Detail = "ยกเลิกการเชื่อมต่อแล้ว";
    }

    async Task ReceiveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var connection = new TcpClient(); socket = connection;
            try
            {
                using (token.Register(() => connection.Close()))
                {
                    connection.NoDelay = true; await connection.ConnectAsync("127.0.0.1", Port).ConfigureAwait(false); transportConnected = true;
                    incoming.Enqueue("{\"event\":\"transport\",\"state\":\"connected\"}");
                    using (var stream = connection.GetStream())
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true))
                    {
                        var sending = SendLoop(stream, token);
                        try
                        {
                            while (!token.IsCancellationRequested)
                            {
                                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                                if (line == null) break;
                                if (line.Length <= 65536) incoming.Enqueue(line);
                            }
                        }
                        finally { connection.Close(); try { await sending.ConfigureAwait(false); } catch { } }
                    }
                }
            }
            catch (Exception) { }
            finally
            {
                connection.Dispose(); transportConnected = false;
                if (!token.IsCancellationRequested) incoming.Enqueue("{\"event\":\"transport\",\"state\":\"disconnected\"}");
            }
            try { await Task.Delay(1500, token).ConfigureAwait(false); } catch { break; }
        }
    }

    async Task SendLoop(NetworkStream stream, CancellationToken token)
    {
        while (!token.IsCancellationRequested && stream.CanWrite)
        {
            while (outgoing.TryDequeue(out var line))
            {
                var bytes = Encoding.UTF8.GetBytes(line + "\n");
                await stream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
            }
            await Task.Delay(40, token).ConfigureAwait(false);
        }
    }

    void Update() { for (int i = 0; i < 32 && incoming.TryDequeue(out var line); i++) AcceptJson(line); }

    static string ThaiStatus(string state, string detail)
    {
        if (!string.IsNullOrEmpty(detail) && detail != state) return detail;
        switch (state)
        {
            case "connected": return "เชื่อมต่อไลฟ์แล้ว";
            case "connecting": return "กำลังเชื่อมต่อ...";
            case "reconnecting": return "กำลังเชื่อมต่อใหม่...";
            case "ended": return "ไลฟ์จบแล้ว";
            case "idle": return "ยังไม่ได้เชื่อมต่อ";
            default: return state;
        }
    }

    public bool AcceptJson(string json)
    {
        Message m; try { m = JsonUtility.FromJson<Message>(json); } catch { return false; }
        if (m == null) return false;
        if (m.@event == "transport")
        {
            if (m.state == "connected" && wantsLive) SendConnect();
            if (m.state == "disconnected" && wantsLive) { State = "reconnecting"; Detail = "ตัวเชื่อมหลุด กำลังต่อใหม่..."; }
            return false;
        }
        if (m.@event == "status")
        {
            State = m.state ?? "idle";
            Detail = ThaiStatus(State, m.detail);
            if (State == "connected" && !string.IsNullOrEmpty(m.streamer) && string.IsNullOrEmpty(m.detail)) Detail = "เชื่อมต่อไลฟ์ @" + m.streamer + " แล้ว";
            return false;
        }
        string key = string.IsNullOrEmpty(m.userId) ? m.uniqueId : m.userId;
        if (string.IsNullOrEmpty(key)) return false;
        if (!string.IsNullOrEmpty(m.profilePictureUrl)) { if (profiles.Count > 4096) profiles.Clear(); profiles[key] = m; }
        else if (profiles.TryGetValue(key, out var profile)) { m.profilePictureUrl = profile.profilePictureUrl; m.profilePictureUrls = profile.profilePictureUrls; }
        if (m.@event != "gift" && m.@event != "like" && m.@event != "follow") return false;
        if (m.@event == "gift" && m.giftType == 1 && !m.repeatEnd && !m.giftDelta) return false;   // streak in progress
        if (!string.IsNullOrEmpty(m.eventId))
        {
            if (!seen.Add(m.eventId)) { DuplicateEvents++; return false; }
            seenOrder.Enqueue(m.eventId); if (seenOrder.Count > 8192) seen.Remove(seenOrder.Dequeue());
        }
        m.repeatCount = Math.Max(1, m.repeatCount);
        if (!director) director = GetComponent<SquidGiftDirector>();
        int acorns = director ? director.Enqueue(m) : 0;
        if (acorns > 0) GetComponent<PTVGiftNotice>()?.Enqueue(m, director.LastText, director.LastIsHelp);
        if (acorns <= 0) return false;
        if (m.@event == "gift") ReceivedGifts += m.repeatCount;
        return true;
    }

    void OnDisable()
    {
        cancel?.Cancel(); socket?.Close(); cancel?.Dispose(); cancel = null; transportConnected = false;
        if (ownedBridge != null) { try { if (!ownedBridge.HasExited) ownedBridge.Kill(); } catch { } ownedBridge.Dispose(); ownedBridge = null; }
    }
}
