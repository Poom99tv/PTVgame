# PTVgame — Windows Launcher

โปรแกรมต้นแบบ WPF/C# สำหรับ Windows พร้อมซอร์สและไฟล์ EXE ใช้ .NET Framework 4.8 ที่ติดมากับ Windows 10/11 โดยทั่วไป ไม่ต้องติดตั้ง .NET SDK

## เริ่มใช้

ดับเบิลคลิก `PTVgame-Launcher.exe` เปิดหน้าแรก คลังเกม รายการโปรด และการตั้งค่าได้ เลือกธีม Midnight / Ember / Frost / Neon ได้ รุ่น 1.5.0 มี PTVCar, PTVvs และ Paint World ที่ดาวน์โหลดจาก GitHub Releases พร้อมแมพ Roblox 4 รายการ ภาพปก Roblox มาจากหน้าเกมจริง ส่วน PTVCar/PTVvs วาดใหม่ด้วย image_gen ดู ARTWORK.md

## เชื่อมเกม Unity ของคุณ

1. Build เกม Windows ใน Unity ให้ได้ `Game.exe` พร้อมโฟลเดอร์ `_Data`, DLL และไฟล์ทั้งหมด
2. บีบอัด **เนื้อหาภายในโฟลเดอร์ Build** เป็น ZIP ให้ `Game.exe` อยู่บนสุด หรือระบุ executable เป็น path เช่น `MyBuild/Game.exe`
3. สร้าง GitHub Release และอัปโหลด ZIP เป็น asset ใน public repository (ตัวอย่างนี้ไม่รองรับ private repo/token)
4. สร้าง manifest จาก ZIP จริง:

```powershell
./publish-manifest.ps1 -Zip 'D:/Build/Game_v1.0.0.zip' -Version '1.0.0' -DownloadUrl 'https://github.com/ACCOUNT/REPO/releases/download/v1.0.0/Game_v1.0.0.zip'
```

5. อัปโหลด `manifest.json` ไปยังตำแหน่ง HTTPS ที่คงที่ เช่นไฟล์ใน branch main แล้วใช้ URL raw ของ GitHub เช่น `https://raw.githubusercontent.com/ACCOUNT/REPO/main/manifest.json`
6. แก้ `catalog.json` ข้าง EXE: เปลี่ยนชื่อและใส่ URL ใน `manifestUrl` ของเกมนั้น
7. เปิด Launcher แล้วกดติดตั้งเกม หลังติดตั้งปุ่มจะเปลี่ยนเป็นเล่นเกม

อัปเดตครั้งถัดไป: สร้าง ZIP ใหม่ เพิ่ม version เช่น 1.0.1 อัปโหลด Release ใหม่ แล้วสร้างและอัปโหลด manifest ใหม่เป็นขั้นตอนสุดท้าย Launcher จะตรวจและติดตั้งเกมที่มีอยู่โดยอัตโนมัติเมื่อเริ่มโปรแกรม หากเปิดตัวเลือกนี้ไว้ กดตรวจอัปเดตด้วยตัวเองได้เช่นกัน

เวอร์ชันใช้เลข 2–4 ส่วน เช่น 1.0.0 ไม่รองรับ suffix แบบ beta การอัปเดตเป็นการดาวน์โหลดเกมเต็ม ไม่ใช่ delta patch และมีระบบอัปเดตตัว Launcher ตั้งแต่รุ่น 1.4.0

## การติดตั้งและข้อมูลผู้เล่น

ไฟล์ติดตั้งอยู่ที่ `%LOCALAPPDATA%/PTVgame/games/<id>/` แต่ละเวอร์ชันแยกโฟลเดอร์ ก่อนเปลี่ยนไปใช้เวอร์ชันใหม่จะตรวจขนาดและ SHA-256 แตก ZIP ลง staging ตรวจไฟล์ EXE และเขียนตัวชี้เวอร์ชันแบบ atomic ถ้าดาวน์โหลดหรือแตกไฟล์ล้มเหลว เวอร์ชันเดิมยังเป็นเวอร์ชันที่ใช้งานอยู่

เก็บ build เก่าไว้ให้กู้คืนได้ด้วยมือ โดยสำรอง installed.json แล้วแก้ folder/version/executable ให้ตรงกับ build เดิม ยังไม่มีปุ่ม rollback และไม่มีการลบ build เก่าอัตโนมัติ การอัปเดตจึงใช้พื้นที่เพิ่มตามเวอร์ชัน

**เกมควรเซฟข้อมูลลง Unity Application.persistentDataPath** อย่าเซฟลงโฟลเดอร์ติดตั้ง เพราะ build ใหม่ไม่ได้ย้ายไฟล์ที่ผู้เล่นสร้างใน build เก่ามาให้

ZIP ต้องมาจากเจ้าของเกมที่เชื่อถือได้ SHA-256 ตรวจไฟล์ให้ตรงกับ manifest แต่ยังไม่มี digital signature ของ manifest หากแหล่ง manifest ถูกแก้ไข ผู้โจมตีสามารถเปลี่ยนทั้ง URL และ hash ได้ การแจกจริงควรเพิ่ม signed manifest และเซ็น EXE ด้วย Authenticode

Roblox: ใส่ `placeId` ที่ต้องการใน catalog และติดตั้ง Roblox ไว้ก่อน Launcher ใช้ roblox:// เปิดแอป การอัปเดต Roblox จัดการโดย Roblox เอง

## พัฒนาและทดสอบ

เปิดโฟลเดอร์นี้ใน VS Code แก้ LauncherShell.cs สำหรับหน้าตา และ Launcher.cs สำหรับระบบอัปเดต แล้วรัน `./build.ps1` ใน PowerShell เพื่อสร้าง EXE ใหม่ ใช้ `Start-Process ./PTVgame-Launcher.exe -ArgumentList '--self-test' -Wait` รันการทดสอบ และอ่าน test-results.txt

การทดสอบครอบคลุม path traversal, ZIP extraction, version comparison และ atomic state writes ส่วนการดาวน์โหลดจาก GitHub และเปิดเกมจริงต้องทดสอบกับ Release/Build ของคุณ ยังไม่มีไฟล์เกมหรือ repository จริงแนบมาในงานนี้


ภาพทั้งหมดอยู่ใน assets/ ต้องเก็บโฟลเดอร์นี้ข้าง EXE หาก clone ซอร์สจาก GitHub ให้ดาวน์โหลด assets/ จาก PTVgame-Windows-v1.2.zip ใน Release ก่อน build



## Launcher self-update (1.4.0+)

The launcher checks `release-manifests/launcher.json` at startup, independently of the game auto-update setting. A newer numeric version downloads automatically, verifies size and SHA-256, then runs a temporary helper. The helper waits for the launcher process to exit, backs up replaced files, applies only the launcher EXE, catalog, README and assets, and restarts the launcher. Replacement errors roll back files already changed. Games, favorites and settings remain in LocalAppData. A manual launcher-update button is available in Settings. If checking fails, the launcher remains usable and reports the error; game version enforcement still applies.

To publish the next launcher: increment LauncherUpdate.Version, build and ZIP only PTVgame-Launcher.exe, catalog.json, README.md and assets at ZIP root. Upload the release asset first; generate release-manifests/launcher.json with publish-manifest.ps1 and publish the JSON last. Versions 1.3 and earlier need this one manual upgrade to gain self-update. GitHub raw caches may delay discovery by a few minutes. Portable folders must be writable; no admin elevation is performed.

## Paint World (1.5.0)
Third Unity game: Paint World (project paint gun), ID paintworld, stable manifest release-manifests/paintworld.json, executable PaintWorld/PaintWorld.exe. Game version 1.0.0 uses the silent mandatory update gate. The launcher cover was drawn from the actual game view. Launcher 1.4.0 users receive this catalog and artwork through launcher self-update.

