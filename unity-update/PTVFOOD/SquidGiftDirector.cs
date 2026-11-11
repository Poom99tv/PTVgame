using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What a gift slot does in the grilled-squid stall.</summary>
public enum SquidAction
{
    GrillSquid,   // ช่วยย่าง  : a helper walks in and grills N squid
    MakeSauce,    // ช่วยตำ    : a helper pounds N cups of sauce
    HelpSet,      // ช่วยทำ    : a helper does N squid + N sauce
    Customer,     // ลูกค้าสั่ง : a customer orders N sets (prank: takes your stock)
    CloseShop,    // (old, unused)
    ServeAll,     // ทำออเดอร์ทั้งหมดที่ลูกค้ารออยู่ทันที
    Wheel         // หมุนกงล้อ : random help or prank, amount from the slot's range
}

/// <summary>
/// Turns TikTok gifts / likes / follows into stall actions.
/// Six skills: one slot per action. Each configured quantity is a fixed number.
/// </summary>
[DisallowMultipleComponent]
public sealed class SquidGiftDirector : MonoBehaviour
{
    public const int SlotCount = 6;
    [Serializable] public sealed class TriggerRule { public bool gift = true, follow, likes, orangeHeart; public int likesRequired = 100; }
    [Serializable] public sealed class Slot { public string gift; public int min = 1, max = 1; public TriggerRule rule = new TriggerRule(); }
    [Serializable] public sealed class Settings
    {
        public int version = 3;
        public Slot[] slots = Defaults();
        static Slot S(string g, int min, int max, bool likes = false, bool follow = false)
            => new Slot { gift = g, min = min, max = max, rule = new TriggerRule { gift = true, likes = likes, follow = follow, likesRequired = 100 } };
        public static Slot[] Defaults() => new[]
        {
            S("Rose", 1, 1, likes: true), S("GG", 1, 1), S("Ice Cream Cone", 1, 1),
            S("TikTok", 1, 1, follow: true), S("Coconut Juice", 1, 1), S("Thumbs Up", 1, 1),
        };
    }

    public static SquidAction SlotAction(int slot)
    {
        return slot == 0 ? SquidAction.GrillSquid : slot == 1 ? SquidAction.MakeSauce : slot == 2 ? SquidAction.HelpSet
            : slot == 3 ? SquidAction.Customer : slot == 4 ? SquidAction.ServeAll : SquidAction.Wheel;
    }
    /// <summary>Level 1..4 (wheel 1..3, serve-all 1).</summary>
    public static int SlotLevel(int slot) => 1;
    public static bool IsHelper(SquidAction a) => a == SquidAction.GrillSquid || a == SquidAction.MakeSauce || a == SquidAction.HelpSet;
    public static bool HasAmount(SquidAction a) => a != SquidAction.ServeAll && a != SquidAction.CloseShop;

    public static string ActionName(SquidAction a)
    {
        switch (a)
        {
            case SquidAction.GrillSquid: return "ช่วยย่าง";
            case SquidAction.MakeSauce: return "ช่วยตำน้ำจิ้ม";
            case SquidAction.HelpSet: return "ช่วยทำ (ชุด)";
            case SquidAction.Customer: return "ลูกค้าสั่ง (ชุด)";
            case SquidAction.ServeAll: return "ทำออเดอร์ทั้งหมด";
            case SquidAction.Wheel: return "สุ่มช่วย/แกล้ง";
            default: return "ปิดร้าน";
        }
    }
    /// <summary>Name shown in the settings, e.g. "ช่วยย่าง Lv.2".</summary>
    public static string SlotName(int slot)
    {
        var a = SlotAction(slot);
        return ActionName(a);
    }

    /// <summary>Short Thai label of a slot, e.g. "ช่วยย่าง 1 ไม้", "กงล้อ สุ่ม 2-6".</summary>
    public static string SlotLabel(int slot, Slot s)
    {
        string n = s.min.ToString();
        switch (SlotAction(slot))
        {
            case SquidAction.GrillSquid: return "ช่วยย่าง " + n + " ไม้";
            case SquidAction.MakeSauce: return "ช่วยตำน้ำจิ้ม " + n;
            case SquidAction.HelpSet: return "ช่วยทำ " + n + " ชุด";
            case SquidAction.Customer: return "ลูกค้าสั่ง " + n + " ชุด";
            case SquidAction.ServeAll: return "ทำออเดอร์ทั้งหมด";
            case SquidAction.Wheel: return "สุ่มช่วย/แกล้ง " + n;
            default: return "";
        }
    }
    public static string Describe(SquidAction a, int amount)
    {
        switch (a)
        {
            case SquidAction.GrillSquid: return "ช่วยย่าง " + amount + " ไม้";
            case SquidAction.MakeSauce: return "ช่วยตำน้ำจิ้ม " + amount + " ถ้วย";
            case SquidAction.HelpSet: return "ช่วยทำ " + amount + " ชุด";
            case SquidAction.Customer: return "สั่ง " + amount + " ชุด";
            case SquidAction.ServeAll: return "ทำออเดอร์ให้ทั้งหมด!";
            case SquidAction.Wheel: return "สุ่มช่วย/แกล้ง!";
            default: return "";
        }
    }

    public sealed class StallEvent { public PTVTikTokLive.Message sender; public SquidAction action; public int amount; public int slot; public int min, max; public int repeats = 1; }

    public const string PrefsKey = "PTVFOOD.Gifts3";
    public const string LegacyPrefsKey = "PTVFOOD.Gifts2";
    public Settings settings = new Settings();
    public string LastText { get; private set; } = "";
    /// <summary>True when the last event only helps (green), false when it pranks (red) - for the gift card colour.</summary>
    public bool LastIsHelp { get; private set; } = true;
    public readonly Queue<StallEvent> events = new Queue<StallEvent>();
    public event Action SettingsChanged;
    public event Action<int> SlotTriggered;

    readonly Dictionary<string, int[]> likeProgress = new Dictionary<string, int[]>();
    readonly HashSet<string> followed = new HashSet<string>();

    void Awake()
    {
        var json = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(json)) json = PlayerPrefs.GetString(LegacyPrefsKey, "");
        if (!string.IsNullOrEmpty(json)) { try { JsonUtility.FromJsonOverwrite(json, settings); } catch { } }
        Validate();
    }

    public void Validate()
    {
        var def = Settings.Defaults();
        // Keep the first configured gift of each old action. The original 20-slot
        // preferences remain untouched under LegacyPrefsKey for recovery.
        if (settings.slots != null && settings.slots.Length == 20)
        {
            int[] previous = { 0, 4, 8, 12, 16, 17 };
            var old = settings.slots;
            settings.slots = new Slot[SlotCount];
            for (int i = 0; i < SlotCount; i++)
                if (old[previous[i]] != null) settings.slots[i] = JsonUtility.FromJson<Slot>(JsonUtility.ToJson(old[previous[i]]));
        }
        if (settings.slots == null || settings.slots.Length != SlotCount) settings.slots = def;
        settings.version = 3;
        for (int i = 0; i < SlotCount; i++)
        {
            var s = settings.slots[i] ?? (settings.slots[i] = def[i]);
            if (s.rule == null) s.rule = def[i].rule;
            if (string.IsNullOrEmpty(s.gift)) s.gift = def[i].gift;
            s.min = Mathf.Clamp(s.min, 1, 999); s.max = s.min;
            s.rule.likesRequired = Mathf.Max(1, s.rule.likesRequired);
        }
    }

    public void SaveSettings()
    {
        Validate();
        likeProgress.Clear();
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings)); PlayerPrefs.Save();
        SettingsChanged?.Invoke();
    }

    static string Key(string t) => PTVGiftCatalog.MatchKey(t);

    /// <summary>Queue the stall actions of a live event. Returns how many actions it triggered (0 = nothing).</summary>
    public int Enqueue(PTVTikTokLive.Message m)
    {
        Validate();
        string viewer = string.IsNullOrEmpty(m.userId) ? m.uniqueId : m.userId;
        if (string.IsNullOrEmpty(viewer)) viewer = "?";
        string kind = string.IsNullOrEmpty(m.@event) ? "gift" : m.@event;
        if (kind == "follow" && !followed.Add(viewer)) return 0;
        int triggered = 0; var texts = new List<string>(); bool help = true;
        for (int slot = 0; slot < SlotCount; slot++)
        {
            var s = settings.slots[slot]; var rule = s.rule; int count = 0;
            if (kind == "gift" && ((rule.gift && Key(m.giftName) == Key(s.gift)) || (rule.orangeHeart && Key(m.giftName) == Key("Heart Me"))))
                count = Math.Max(1, m.repeatCount);
            else if (kind == "follow" && rule.follow) count = 1;
            else if (kind == "like" && rule.likes && m.likeCount > 0)
            {
                if (!likeProgress.TryGetValue(viewer, out var progress))
                { if (likeProgress.Count >= 4096) likeProgress.Clear(); progress = new int[SlotCount]; likeProgress[viewer] = progress; }
                long sum = (long)progress[slot] + m.likeCount; int threshold = Math.Max(1, rule.likesRequired);
                count = (int)(sum / threshold); progress[slot] = (int)(sum % threshold);
            }
            if (count <= 0) continue;
            var action = SlotAction(slot);
            if (action == SquidAction.Wheel)
            {
                int spins = count;
                events.Enqueue(new StallEvent { sender = m, action = action, slot = slot, min = s.min, max = s.max, repeats = spins });
                texts.Add("สุ่มช่วย/แกล้ง" + (spins > 1 ? " x" + spins : ""));
            }
            else if (action == SquidAction.ServeAll)
            {
                events.Enqueue(new StallEvent { sender = m, action = action, slot = slot });
                texts.Add(Describe(action, 0));
            }
            else
            {
                count = Math.Min(count, 999);
                int amount = 0;
                amount = count * s.min;
                events.Enqueue(new StallEvent { sender = m, action = action, amount = amount, slot = slot, min = s.min, max = s.max });
                texts.Add(Describe(action, amount));
                if (action == SquidAction.Customer) help = false;
            }
            triggered++;
            SlotTriggered?.Invoke(slot);
        }
        if (triggered > 0) { LastText = string.Join("  ", texts); LastIsHelp = help; }
        return triggered;
    }

    /// <summary>For testing without a live: pretend a viewer triggered a slot.</summary>
    public void SimulateSlot(int slot, string user = null)
    {
        slot = Mathf.Clamp(slot, 0, SlotCount - 1);
        user = user ?? ("ผู้ชม" + UnityEngine.Random.Range(1, 99));
        var m = new PTVTikTokLive.Message { @event = "gift", giftName = settings.slots[slot].gift, repeatCount = 1, uniqueId = user, nickname = user, userId = user + slot + Time.frameCount };
        if (Enqueue(m) > 0) GetComponent<PTVGiftNotice>()?.Enqueue(m, LastText, LastIsHelp);
    }
}

