# PTV – Game To Earn (เครื่องมือในตัว Launcher)

เชื่อม TikTok LIVE ภายนอก → กดคีย์ในหน้าต่าง Roblox อัตโนมัติ (ใช้แทน TikTokBridge)

- ไม่มีระบบ License / Google Sheets แล้ว — สิทธิ์ใช้งานตามบัญชี PTVgame (VIP) ใน Launcher
- ขนาดหน้าต่าง 1160×760 หัวข้อและธีมเดียวกับ Launcher (Midnight / Ember / Neon / Frost) ภาษา ไทย/อังกฤษ ตาม Launcher
- ค่าที่บันทึก (Preset, ชื่อ TikTok, เสียง, โหล) อยู่ที่ %LOCALAPPDATA%\PTVgame\GameToEarn

## Build (Windows, ต้องมี Python 3.10+)

    powershell -ExecutionPolicy Bypass -File gametoearn\build.ps1 -Version 1.0.0

ได้ไฟล์ GameToEarn\PTVGameToEarn.exe ข้าง PTVgame-Launcher.exe (Launcher ใช้ตัวนี้ก่อน)
และ GameToEarn-v1.0.0.zip + release-manifests\gametoearn.json สำหรับปล่อยให้ผู้เล่นคนอื่นอัปเดตผ่าน GitHub Releases

ถ้ามีโฟลเดอร์รูปของขวัญ gifts\ และ app.ico ให้วางไว้ในโฟลเดอร์ gametoearn ก่อน build

รันจากซอร์สเพื่อทดสอบ: python gametoearn\ptv_game_to_earn.py [--theme Neon] [--lang th]
