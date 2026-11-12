using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// English / Thai. English is the default. The game's source strings are Thai; when English is selected
/// every PTVText translates them on the fly (longest phrase first, so composed strings like
/// "เลเวล " + 5 also work). Change with PTVLang.Set("en" / "th"); saved in PlayerPrefs.
/// </summary>
public static class PTVLang
{
    const string Key = "PTVFOOD.Lang";
    static string lang;
    public static event Action Changed;
    public static string Current { get { if (lang == null) lang = Normalize(PlayerPrefs.GetString(Key, "en")); return lang; } }
    public static bool Thai => Current == "th";
    public static string Normalize(string v) => v == "th" ? "th" : "en";

    public static void Set(string value)
    {
        value = Normalize(value);
        if (value == Current) return;
        lang = value; PlayerPrefs.SetString(Key, value); PlayerPrefs.Save();
        Changed?.Invoke();
    }
    public static void Toggle() => Set(Thai ? "en" : "th");

    /// <summary>Keep bridge diagnostics out of the localized player-facing status label.</summary>
    public static string Status(string state, string detail)
    {
        if (!Thai || (!string.IsNullOrEmpty(detail) && detail.Any(c => c >= '\u0e00' && c <= '\u0e7f'))) return T(detail);
        switch (state)
        {
            case "connected": return "เชื่อมต่อไลฟ์แล้ว";
            case "connecting": return "กำลังเชื่อมต่อ...";
            case "reconnecting": return "กำลังเชื่อมต่อใหม่...";
            case "ended": return "ไลฟ์จบแล้ว";
            case "error": return "เชื่อม TikTok ไม่สำเร็จ ตรวจชื่อบัญชีและลองใหม่";
            default: return "ยังไม่ได้เชื่อมต่อ";
        }
    }

    /// <summary>Translate a Thai source string to the current language in one pass.</summary>
    public static string T(string s)
    {
        if (string.IsNullOrEmpty(s) || Thai) return s;
        bool any = false;
        foreach (char c in s) if (c >= '฀' && c <= '๿') { any = true; break; }
        if (!any) return s;
        if (Exact.TryGetValue(s, out var e)) return e;
        return Matcher.Replace(s, match => Dict[match.Value]);
    }

    static readonly Dictionary<string, string> Dict = new Dictionary<string, string>
    {
                {"สิทธิ์เล่นเกมหมดอายุแล้ว", "Game access has expired"},
        {"เปิดเกมผ่าน PTVgame Launcher", "Open this game from PTVgame Launcher"},
        {"ซื้อหรือต่ออายุสิทธิ์เกมปลาหมึกย่างใน Launcher แล้วเปิดเกมอีกครั้ง", "Buy or renew Grilled Squid in the launcher, then start the game again."},
        {"ตั๋วเข้าเล่นหมดเวลาแล้ว ปิดเกมแล้วกดเล่นจาก Launcher อีกครั้ง", "Your play ticket has expired. Close the game and press Play in the launcher again."},
        {"เข้าสู่บัญชีที่มีสิทธิ์เกมปลาหมึกย่าง แล้วกดเล่นจาก PTVgame Launcher", "Sign in to an account with Grilled Squid access, then press Play in PTVgame Launcher."},
        {"ดาวน์โหลด Launcher", "Get Launcher"},
        {"ออกจากเกม", "Quit"},
        // ---- game flow
        {"กด R เพื่อเล่นใหม่", "Press R to play again"},
        {"ชนะ!", "YOU WIN!"},
        {"เก็บครบแล้ว!", "All collected!"},
        // ---- skills (SquirrelProgress)
        {"เพิ่ม XP พื้นฐาน", "Base XP Boost"},
        {"เพิ่ม XP\nพื้นฐาน", "Base XP\nBoost"},
        {"รัศมีดูดข้างหน้า ", "Front Suction "},
        {"ความเร็ว ", "Speed "},
        {"ดูดได้ 2 ลูก", "Grab 2 acorns"},
        {"ดูดได้ 3 ลูก", "Grab 3 acorns"},
        {"พลังดูด  +1 แถวหน้า", "Suction  +1 row ahead"},
        {"เร็วขึ้น ", "Speed "},
        {"ลมพัด  เร็วขึ้น +50%", "Wind  Speed +50%"},
        {"สาย XP สูงสุด: วอลนัททอง", "Top XP path: Golden Walnut"},
        {"สุ่มเกิดลูกวอลนัททองคำ ได้ XP มหาศาล", "Random golden walnuts give huge XP"},
        {"สุ่มเกิดลูกวอลนัททองคำ\nได้ XP มหาศาล", "Random golden walnuts\ngive huge XP"},
        {"Master ดูด", "Suck Master"},
        {"Master วิ่ง", "Run Master"},
        {"หลุมดำดูดรอบๆ\nทุกคอมโบ +150 ลูก\nถ้าหยุดจะเริ่มใหม่", "Black hole pull\nevery 150 combo\nresets if you stop"},
        {"สายฟ้าผ่า\nทุกคอมโบ +50 ลูก\nถ้าหยุดจะเริ่มใหม่", "Lightning strikes\nevery 50 combo\nresets if you stop"},
        // ---- skill tree UI
        {"   •   ได้แต้มสกิล +1   (กด K)", "   •   +1 skill point   (press K)"},
        {"START:\nกระรอกน้อย", "START:\nLittle Squirrel"},
        {"คืนแต้มสกิลทั้งหมดแล้ว", "All skill points refunded"},
        {"ดูพาสซีฟสกิล", "Passive Skills"},
        {"พาสซีฟสกิล", "Passive Skills"},
        {"ต้องปลดสกิลแถวก่อนหน้าก่อน", "Unlock the previous row first"},
        {"ปลดล็อกแล้ว", "Already unlocked"},
        {"ปลดล็อก: ", "Unlocked: "},
        {"ปิด (K)", "Close (K)"},
        {"รีเซ็ตสกิล", "Reset Skills"},
        {"ล็อก • ", "Locked • "},
        {"วอลนัททอง", "Golden Walnut"},
        {"สาย XP สูงสุด", "Top XP path"},
        {"อัปสกิล (", "Upgrade ("},
        {"อัปเลย! • ", "Upgrade! • "},
        {"เลือกอีกสายแล้ว", "Other path chosen"},
        {"เลเวล ", "Level "},
        {"แต้มสกิลไม่พอ (ต้องใช้ ", "Not enough skill points (need "},
        {"แต้มสกิล ", "Skill pts "},
        {"แถวนี้เลือกได้แค่ 1 สาย (ดูด หรือ เร็ว)", "Only 1 path per row (Suction or Speed)"},
        {" แต้ม)", " pts)"},
        {" แต้ม", " pts"},
        {"ใช้ ", "Cost "},
        // ---- skill effects
        {"หลุมดำ! ดูด ", "Black hole! Grabbed "},
        {"สายฟ้า! +", "Lightning! +"},
        {"วอลนัททอง! +", "Golden walnut! +"},
        {"วอลนัททองปรากฏ! ตามแสงสีทองไปเลย", "A golden walnut appeared! Follow the golden light"},
        {" ลูก", " acorns"},
        // ---- TikTok start / settings / picker
        {"กระรอกเก็บลูกโอ๊ก  ·  เชื่อมต่อ TikTok Live ก่อนเริ่มเล่น", "Squirrel acorn hunt  ·  Connect your TikTok LIVE before you play"},
        {"TikTok ID ของไลฟ์", "TikTok LIVE ID"},
        {"@ ชื่อบัญชี TikTok", "@ TikTok username"},
        {"เชื่อมแล้ว ✓", "Connected ✓"},
        {"กำลังต่อ...", "Connecting..."},
        {"ลองใหม่", "Retry"},
        {"เชื่อมต่อ", "Connect"},
        {"เริ่มเล่น", "PLAY"},
        {"ไม่ใส่ ID = เล่นแบบไม่เชื่อมไลฟ์  ·  ⚙ มุมซ้ายบน = ตั้งค่าของขวัญ", "No ID = play without LIVE  ·  ⚙ top-left = gift settings"},
        {"พร้อมแล้ว! กด เริ่มเล่น", "Ready! Press PLAY"},
        {"กด เชื่อมต่อ ให้สำเร็จก่อน จึงจะเริ่มเล่นได้  (หรือลบ ID ออกเพื่อเล่นแบบไม่เชื่อมไลฟ์)", "Connect successfully to start  (or clear the ID to play without LIVE)"},
        {"ไม่ได้เชื่อมไลฟ์", "Not connected to LIVE"},
        {"ตั้งค่าของขวัญ", "Gift Settings"},
        {"รูปของขวัญ (คลิกเพื่อเปลี่ยน)", "Gift (click to change)"},
        {"ได้ลูกโอ๊ก", "Acorns"},
        {"เงื่อนไขที่ให้ลูกโอ๊ก", "What gives the acorns"},
        {"ส่งของขวัญ", "Gift"},
        {"กดติดตาม", "Follow"},
        {"เคาะใจ", "Likes"},
        {"ครั้ง", "times"},
        {"ค่าเริ่มต้น", "Defaults"},
        {"ยกเลิก", "Cancel"},
        {"บันทึก", "Save"},
        {"คืนค่าเริ่มต้นแล้ว (กด บันทึก เพื่อใช้)", "Defaults restored (press Save to apply)"},
        {": จำนวนลูกโอ๊กต้องเป็น 1 - 100,000", ": acorns must be 1 - 100,000"},
        {": จำนวนเคาะใจต้องเป็นเลขจำนวนเต็มบวก", ": likes must be a positive whole number"},
        {"ช่อง ", "Slot "},
        {"ปิดอยู่", "Off"},
        {"เลือกของขวัญ", "Choose a Gift"},
        {"ค้นหาชื่อของขวัญ...", "Search gifts..."},
        {"กลับ", "Back"},
        {" ของขวัญ", " gifts"},
        {"ใช้แล้วในช่อง ", "Used in slot "},
        {"✓ เลือกอยู่", "✓ Selected"},
        // ---- cutscene
        {"ฝนลูกโอ๊กมหาศาล!!", "MEGA ACORN RAIN!!"},
        {"ฝูงนกส่งลูกโอ๊ก!", "BIRD DELIVERY!"},
        {"ขนาดไอคอน", "Icon size"},
        {"จอแนวตั้ง 9:16", "Portrait 9:16"},
        {"จอแนวนอน 16:9", "Landscape 16:9"},
        {"แสดงไอคอนรางวัล", "Show reward icons"},
        {"ฝนอุกกาบาตลูกโอ๊ก!!", "METEOR SHOWER!!"},
        {"พายุลูกโอ๊ก!", "ACORN STORM!"},
        {"ฝนลูกโอ๊ก!", "ACORN RAIN!"},
        {"ผู้ชม", "Viewer"},
        {"ส่ง ", "sent "},
        // ---- TikTok connection (game + bridge messages)
        {"กรอก @ชื่อบัญชี TikTok แล้วกดเชื่อมต่อ", "Enter your TikTok @username, then press Connect"},
        {"ชื่อบัญชีไม่ถูกต้อง  ใช้ได้เฉพาะ a-z 0-9 _ .", "Invalid username (only a-z 0-9 _ .)"},
        {"กำลังเชื่อมต่อใหม่...", "Reconnecting..."},
        {"กำลังเชื่อมต่อ...", "Connecting..."},
        {"กำลังเชื่อมต่อ @", "Connecting @"},
        {"ตัวเชื่อมหลุด กำลังต่อใหม่...", "Bridge disconnected, reconnecting..."},
        {"ยกเลิกการเชื่อมต่อแล้ว", "Disconnected"},
        {"ยังไม่ได้เชื่อมต่อ", "Not connected"},
        {"เชื่อมต่อไลฟ์แล้ว", "Connected to LIVE"},
        {"เชื่อมต่อไลฟ์ @", "Connected to LIVE @"},
        {" แล้ว", ""},
        {"เปิดตัวเชื่อม TikTok ไม่ได้: ", "Cannot start the TikTok bridge: "},
        {"ไม่พบโฟลเดอร์ TikTokBridge", "TikTokBridge folder not found"},
        {"ไลฟ์จบแล้ว", "The LIVE has ended"},
        {"กรอกชื่อบัญชี TikTok เช่น @ชื่อบัญชี", "Enter a TikTok username, e.g. @username"},
        {"โหมดทดสอบ ไม่มีการเชื่อมไลฟ์จริง", "Test mode - not a real LIVE"},
        {"TikTok ไม่ตอบสนอง กรุณาลองใหม่", "TikTok is not responding, please retry"},
        {"บัญชีนี้ยังไม่ได้เปิดไลฟ์", "This account is not LIVE right now"},
        {"เชื่อม TikTok ไม่สำเร็จ ตรวจชื่อบัญชีและลองใหม่", "Could not connect to TikTok. Check the username and retry"},
        {"เชื่อมไม่สำเร็จ", "Connection failed"},
        // ---- language button
        {"ภาษา: ไทย", "Language: English"},
    };
    static readonly Dictionary<string, string> Exact = Dict;
    static readonly string[] Keys;
    static readonly Regex Matcher;
    static PTVLang()
    {
        Dict["ปลาหมึกย่าง"] = "Grilled Squid";
        Dict["เปิดร้าน!"] = "Open Shop!";
        Dict["เปิดร้าน"] = "Open Shop";
        Dict["ทดลองเล่น"] = "Try Offline";
        Dict["พร้อมแล้ว! กด เปิดร้าน!"] = "Ready! Press Open Shop!";
        Dict["เชื่อมต่อ TikTok LIVE ก่อนเริ่มขาย"] = "Connect TikTok LIVE before opening";
        Dict["เชื่อมต่อไลฟ์ก่อนเริ่มขาย"] = "Connect your LIVE before opening";
        Dict["ชื่อบัญชี TikTok"] = "TikTok username";
        Dict["ผลในร้าน (ต่ำสุด - สูงสุด)"] = "Action (minimum - maximum)";
        Dict["เงื่อนไข"] = "Triggers";
        Dict["หัวใจสีส้ม"] = "Heart Me";
        Dict[" เหรียญ"] = " coins";
        Dict["หน้า "] = "Page ";
        Dict[" ระดับ "] = " Level ";
        Dict["4 ระดับ"] = "4 levels";
        Dict["ช่วยย่าง"] = "Grill Squid";
        Dict["ช่วยตำน้ำจิ้ม"] = "Make Sauce";
        Dict["ช่วยทำ (ชุด)"] = "Prepare Sets";
        Dict["ลูกค้าสั่ง (ชุด)"] = "Customer Orders";
        Dict["ทำออเดอร์ทั้งหมด"] = "Complete All Orders";
        Dict["ทำทุกออเดอร์"] = "All Orders";
        Dict["หมุนกงล้อ"] = "Prize Wheel";
        Dict["หมุนวงล้อ"] = "Prize Wheel";
        Dict["เพิ่มรสชาติ"] = "Add Flavor";
        Dict["ช่วยเสิร์ฟ"] = "Serve Food";
        Dict["ปิดร้าน"] = "Close Shop";
        Dict["ช่วยตำ"] = "Make Sauce";
        Dict["ช่วยทำ"] = "Prepare";
        Dict["ลูกค้าสั่ง"] = "Customer Orders";
        Dict["สุ่ม "] = "Random ";
        Dict[" ไม้"] = " skewers";
        Dict[" ถ้วย"] = " cups";
        Dict[" ชุด"] = " sets";
        Dict["ทำออเดอร์ให้ทั้งหมด!"] = "Completed every order!";
        Dict["สั่ง "] = "Order ";
        Dict["ร้านปิดอยู่จ้า"] = "The shop is resting";
        Dict["เตาเต็มแล้ว!"] = "The grill is full!";
        Dict["ตักก่อนนะ!"] = "Scoop the sauce first!";
        Dict["คนก่อนนะ!"] = "Stir the sauce first!";
        Dict["กดถาดเครื่องก่อน!"] = "Select the ingredients first!";
        Dict["คนๆ!"] = "Stir!";
        Dict["คน!"] = "Stir!";
        Dict["ตัก!"] = "Scoop!";
        Dict["ถาดเต็ม! ขายก่อนนะ"] = "The tray is full! Serve an order first";
        Dict["ใส่เครื่อง!"] = "Add ingredients!";
        Dict["หยิบปลาหมึก!"] = "Pick a squid!";
        Dict["พลิกด้าน!"] = "Flip it!";
        Dict["เก็บ!"] = "Collect!";
        Dict["ไหม้แล้ว!"] = "Burnt!";
        Dict["แชมป์ปลาหมึกย่าง!"] = "Grilled Squid Champion!";
        Dict["ขายครบชุดแล้ว  WIN "] = "Set complete! Win ";
        Dict["ขายครบชุดแล้ว  ชนะ "] = "Set complete! Win ";
        Dict["ขายครบ "] = "Completed ";
        Dict[" รอบ!"] = " rounds!";
        Dict["ทำออเดอร์ครบ!"] = "All orders complete!";
        Dict["ออร์เดอร์ทั้งหมด "] = "Total orders ";
        Dict[" คน"] = " customers";
        Dict["กำลังเสิร์ฟ!"] = "Serving!";
        Dict["ไม่มีออเดอร์ค้าง"] = "No waiting orders";
        Dict["ปิดร้านไปพัก!"] = "Taking a break!";
        Dict["ปิดร้านไปพัก"] = "Shop on a break";
        Dict[" สั่งปิดร้าน "] = " closed the shop for ";
        Dict[" วิ"] = " sec";
        Dict["ยังไม่มีปลาหมึก!"] = "No grilled squid yet!";
        Dict["ยังไม่มีน้ำจิ้ม!"] = "No sauce yet!";
        Dict["ปลาหมึก ✓ คลิกลูกค้า"] = "Squid ready! Select a customer";
        Dict["น้ำจิ้ม ✓ คลิกลูกค้า"] = "Sauce ready! Select a customer";
        Dict["คลิกถาดก่อนนะ"] = "Select the tray first";
        Dict["ไม่ได้สั่งปลาหมึก"] = "No squid in this order";
        Dict["ไม่ได้สั่งน้ำจิ้ม"] = "No sauce in this order";
        Dict["ของหมดแล้ว!"] = "Out of stock!";
        Dict["ถาดเต็ม! รอขายก่อน"] = "The tray is full! Serve an order first";
        Dict["แกล้ง! ลูกค้าสั่ง "] = "Prank! Customer orders ";
        Dict["แกล้ง!"] = "Prank!";
        Dict["ลูกค้ารอ  ปลาหมึก "] = "Waiting orders: squid ";
        Dict["  ·  น้ำจิ้ม "] = "  ·  sauce ";
        Dict["เปิดอีก "] = "Reopens in ";
        Dict["บ๊ายบาย!"] = "Bye-bye!";
        Dict["ขอบคุณจ้า!"] = "Thank you!";
        Dict["ปลาหมึก"] = "Squid";
        Dict["น้ำจิ้ม"] = "Sauce";
        Dict["หมึก "] = "Squid ";
        Dict["จิ้ม "] = "Sauce ";
        Dict["ไม่ใส่ ID = เล่นแบบไม่เชื่อมไลฟ์  ·  ⚙ มุมซ้ายบน = ตั้งค่าของขวัญ  ·  ทดสอบ: กด 1-6"] = "No ID: play offline  ·  Settings: top-left gear  ·  Test: keys 1–6";
        Dict["ไม่ใส่ ID = เล่นแบบไม่เชื่อมไลฟ์"] = "Leave the ID empty to play offline";
        Dict["ทดสอบ: กด 1-6"] = "Test: keys 1–6";
        Dict[": ตัวเลขต้องเป็น 1 - 999 และต่ำสุด ≤ สูงสุด"] = ": enter values from 1 to 999; minimum must not exceed maximum";
        Dict["เชื่อมต่อไลฟ์แล้ว"] = "Connected to LIVE";
        Dict["ไลฟ์ "] = "LIVE ";
        Dict["ระดับ "] = "Level ";
        Dict["เว้นชื่อบัญชีว่างไว้เพื่อเล่นออฟไลน์  ·  ทดสอบ: กด 1-6"] = "Leave the username empty to play offline  ·  Test: keys 1–6";
        Dict["เชื่อมต่อให้สำเร็จก่อนเปิดร้าน หรือกดทดลองเล่นเพื่อเล่นออฟไลน์"] = "Connect before opening the shop, or choose Try Offline";
        Dict["แถบสกิล"] = "Skill HUD";
        Dict["ผู้ช่วย"] = "Helpers";
        Dict["สกิลสุ่ม"] = "Random";
        Dict["สุ่มสกิล"] = "Random";
        Dict["ลูกค้า"] = "Orders";
        Dict["สั่งชุด"] = "Order Set";
        Dict["ทั้งหมด"] = "All";
        Dict["ทุกออเดอร์"] = "Serve All";
        Dict["ลากแถบสกิลในหน้าเล่นเพื่อย้าย • แตะสองครั้งเพื่อคืนตำแหน่ง"] = "Drag skill groups during gameplay. Double-tap to reset their position.";
        Dict["คืนค่าแถบสกิล"] = "Reset Skill HUD";
        Dict["กรอบสกิล: เปิด"] = "Skill frames: On";
        Dict["กรอบสกิล: ปิด"] = "Skill frames: Off";
        Dict["สุ่มช่วย/แกล้ง"] = "Random Help/Prank";
        Dict["ผลในร้าน (จำนวนต่อครั้ง)"] = "Action (amount per trigger)";
        Dict[": จำนวนต้องเป็นเลข 1 ถึง 999"] = ": enter one number from 1 to 999";
        Dict["ช่วยทำชุด"] = "Prepare Sets";
        Dict["เพิ่มออเดอร์"] = "Add Orders";
        Dict["แกล้งเพิ่มออเดอร์"] = "Add Order Sets";
        Dict["ผู้ช่วย 4 ระดับ"] = "4 Helper Levels";
        Dict["ปลาหมึกพร้อมน้ำจิ้ม"] = "Squid + Sauce Sets";
        Dict["แกล้ง 5 ระดับ"] = "5 Prank Levels";
        Dict["กำหนดจำนวนต่อชุด"] = "Set the Quantity";
        Dict["สุ่ม เล็ก/กลาง/ใหญ่"] = "Random: S / M / L";
        Dict["แกล้ง"] = "Pranks";
        Dict["เล็ก"] = "Small";
        Dict["กลาง"] = "Medium";
        Dict["ใหญ่"] = "Large";
        Keys = Dict.Keys.OrderByDescending(k => k.Length).ToArray();
        Matcher = new Regex(string.Join("|", Keys.Select(Regex.Escape)), RegexOptions.CultureInvariant);
    }
}


