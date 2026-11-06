# Game To Earn (ในตัว Launcher)

หน้า **Game To Earn** ใน PTVgame Launcher: เชื่อม TikTok LIVE — ใช้ฟรี

## PTVRunner: ส่งสัญญาณให้เกม (ไม่ใช้ Preset)

- รองรับเฉพาะแมพ `71880688656931` สำหรับโหมดส่งสัญญาณนี้
- เลือก PTVRunner แล้วเมนู Preset และตัวเลือก Preset จะซ่อน ของขวัญ / Like / Follow จะไม่กดคีย์และไม่ทำ Win action ใน Launcher
- กดสร้าง/คัดลอกรหัสจับคู่ แล้วในเกมกดรูปเฟือง → Game To Earn → จับคู่ (`GTE1|…`)
- จับคู่เฉพาะเจ้าของห้อง/ผู้ควบคุมเดิมของเกม และจำกัดการส่งเข้า 1 ห้องที่ใช้งานอยู่ต่อ Launcher
- เกมตั้งค่าของขวัญ สกิล และจำนวนสะสมทั้งหมด ไม่มีการส่งการตั้งค่าสกิลออกไปให้โปรแกรม
- ส่งชื่อผู้ชมพร้อมอีเวนต์ของขวัญ Like Follow และชื่อ Roblox จากแชทเพื่อให้ระบบสมัครนักวิ่งเดิมทำงานต่อ
- รหัสเกมเป็นคนละ credential กับ API/Overlay ในเครื่อง ช่องทาง HTTPS เปิดให้เฉพาะ `/game/*` ไม่เปิดการควบคุม LIVE, TTS หรือ API ในเครื่องออกสู่ tunnel
- แมพ Roblox อื่นยังใช้ Preset แบบกดคีย์ตามเดิม

## สร้างและทดสอบ

```powershell
node --test gametoearn/engine/runner.test.mjs gametoearn/engine/runner-http.test.mjs
./gametoearn/build-engine.ps1
./build.ps1
./gametoearn/check-launcher.ps1
```

สคริปต์ build จะติดตั้ง dependencies ตาม lockfile, แพ็ก `runner.mjs` เข้า engine และดาวน์โหลด cloudflared รุ่นที่ตรึง SHA-256 ไว้ ไฟล์ `assets/cloudflared.exe` ต้องอยู่ในแพ็กเกจ Launcher ด้วย

สคริปต์ Roblox ที่แก้ใน Studio มีสำเนาใน `roblox/PTVRunner/` ไม่รวม token หรือ config ของเครื่อง ต้อง Publish แมพก่อนใช้กับผู้เล่นจริง

## แมพอื่นและ Overlay

- เมนูย่อย: เชื่อมต่อ LIVE • Preset และคำสั่ง • หน้าจอ OBS • บันทึกเหตุการณ์
- ระบบ TikTok ทำงานเบื้องหลังด้วย engine (Node.js) จาก `assets/gametoearn-engine.zip` — Launcher แตกไฟล์ให้เองที่ `%LOCALAPPDATA%\PTVgame\GameToEarn\engine`
- ของขวัญรู้จักจาก **รูป** ก่อน (ลายนิ้วมือภาพแบบเดียวกับ TikTokBridge) ถ้าชื่อที่ TikTok ส่งมาไม่ตรงก็ยังทำงาน แล้วค่อยเทียบชื่อ
- Node.js: ใช้ตัวที่มีในเครื่อง (PATH หรือของ TikTokBridge) ถ้าไม่มี Launcher ดาวน์โหลดจาก nodejs.org และตรวจ SHA256 ให้ครั้งเดียว
- ค่าที่บันทึก: `%LOCALAPPDATA%\PTVgame\GameToEarn\presets.json`, `state.json`
- รูปของขวัญ: `assets/gifts/*.png` (ย่อจากโฟลเดอร์ gifts ของโปรเจกต์เดิม)
- OBS: คัดลอกลิงก์จากหน้า "หน้าจอ OBS" → OBS เพิ่ม Browser Source

แก้ engine แล้ว build ใหม่: `powershell -File gametoearn\build-engine.ps1` (เพิ่มเลขใน `engine/version.txt`)
