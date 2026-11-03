# Game To Earn (ในตัว Launcher)

หน้า **Game To Earn** ใน PTVgame Launcher: เชื่อม TikTok LIVE ภายนอก → กดคีย์ในเกม Roblox อัตโนมัติ (แทน TikTokBridge) — ใช้ฟรี

- เมนูย่อย: เชื่อมต่อ LIVE • Preset และคำสั่ง • หน้าจอ OBS • บันทึกเหตุการณ์
- ระบบ TikTok ทำงานเบื้องหลังด้วย engine (Node.js) จาก `assets/gametoearn-engine.zip` — Launcher แตกไฟล์ให้เองที่ `%LOCALAPPDATA%\PTVgame\GameToEarn\engine`
- ของขวัญรู้จักจาก **รูป** ก่อน (ลายนิ้วมือภาพแบบเดียวกับ TikTokBridge) ถ้าชื่อที่ TikTok ส่งมาไม่ตรงก็ยังทำงาน แล้วค่อยเทียบชื่อ
- Node.js: ใช้ตัวที่มีในเครื่อง (PATH หรือของ TikTokBridge) ถ้าไม่มี Launcher ดาวน์โหลดจาก nodejs.org และตรวจ SHA256 ให้ครั้งเดียว
- ค่าที่บันทึก: `%LOCALAPPDATA%\PTVgame\GameToEarn\presets.json`, `state.json`
- รูปของขวัญ: `assets/gifts/*.png` (ย่อจากโฟลเดอร์ gifts ของโปรเจกต์เดิม)
- OBS: คัดลอกลิงก์จากหน้า "หน้าจอ OBS" → OBS เพิ่ม Browser Source

แก้ engine แล้ว build ใหม่: `powershell -File gametoearn\build-engine.ps1` (เพิ่มเลขใน `engine/version.txt`)
