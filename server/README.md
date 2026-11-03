# PTVgame Accounts — คู่มือติดตั้งเซิร์ฟเวอร์ (ฟรี)

ระบบบัญชีของ Launcher ใช้ **Supabase แผนฟรี** (ล็อกอิน + ฐานข้อมูล + เก็บรูป + Edge Functions) และ **PayPal** (PayPal + บัตรเครดิต/เดบิต) ส่วน **QR พร้อมเพย์** สร้างจากฐานข้อมูลเองโดยไม่มีค่าบริการ

| ส่วน | ใช้อะไร | ค่าใช้จ่าย |
|---|---|---|
| สมัคร/ล็อกอิน อีเมล, Google, Facebook | Supabase Auth | ฟรี (50,000 ผู้ใช้ที่ใช้งาน/เดือน) |
| โปรไฟล์, PTV ID, VIP, ตัวแทน, แอดมิน, คำสั่งซื้อ | Supabase Postgres + RLS | ฟรี (500 MB) |
| รูปโปรไฟล์, สลิปโอนเงิน | Supabase Storage | ฟรี (1 GB) |
| PayPal / บัตร | Supabase Edge Functions + PayPal REST | ฟรีค่าระบบ — PayPal หักค่าธรรมเนียมต่อรายการ |
| QR พร้อมเพย์ + แนบสลิป | SQL (EMVCo + CRC16) + แอดมินอนุมัติ | ฟรี |

> แผนฟรีของ Supabase จะ **หยุดโปรเจกต์ชั่วคราวถ้าไม่มีการใช้งาน 1 สัปดาห์** (ตอนมีผู้เล่นใช้ทุกวันจะไม่หยุด) ถ้าถูกหยุด ให้กด Restore ในหน้า Dashboard

## 1. สร้างโปรเจกต์ Supabase
1. สมัครที่ supabase.com → **New project** (ภูมิภาคแนะนำ Singapore)
2. **SQL Editor** → วางไฟล์ `supabase/migrations/20261103000000_accounts.sql` ทั้งไฟล์ → Run
3. **Project Settings → API**: จด `Project URL` และ `anon` (หรือ publishable) key

## 2. ตั้งค่าการล็อกอิน
**Authentication → URL Configuration → Redirect URLs** เพิ่ม:
```
http://127.0.0.1:53682/auth/callback
```
(Launcher เปิดเบราว์เซอร์ แล้วรับผลกลับที่พอร์ตนี้บนเครื่องผู้เล่นเอง ถ้าเปลี่ยน `callbackPort` ใน account.json ต้องแก้ตรงนี้ด้วย)

**Authentication → Providers**
- **Email**: เปิด (แนะนำเปิด Confirm email)
- **Google**: สร้าง OAuth Client (Web application) ใน Google Cloud Console → Authorized redirect URI = `https://<project-ref>.supabase.co/auth/v1/callback` → ใส่ Client ID/Secret ใน Supabase
- **Facebook**: สร้างแอปใน developers.facebook.com → Facebook Login → Valid OAuth Redirect URI = `https://<project-ref>.supabase.co/auth/v1/callback` → ใส่ App ID/Secret ใน Supabase

รูปโปรไฟล์และชื่อจาก Google/Facebook ถูกดึงมาอัตโนมัติตอนสมัคร ผู้เล่นอัปโหลดรูปเองได้ในหน้าโปรไฟล์

## 3. PayPal และบัตร (Edge Functions)
1. developer.paypal.com → Apps & Credentials → สร้างแอป (เริ่มที่ **Sandbox** เพื่อทดสอบ)
2. ติดตั้ง Supabase CLI แล้วรันในโฟลเดอร์ `server/`:
```bash
npx supabase login
npx supabase link --project-ref <project-ref>
npx supabase secrets set PAYPAL_CLIENT_ID=xxx PAYPAL_SECRET=xxx PAYPAL_ENV=sandbox
npx supabase functions deploy paypal-create paypal-capture paypal-webhook
```
3. ใน PayPal → Webhooks → Add webhook URL `https://<project-ref>.supabase.co/functions/v1/paypal-webhook` เลือก event **Checkout order approved** และ **Payment capture completed** → นำ Webhook ID ไปตั้ง
```bash
npx supabase secrets set PAYPAL_WEBHOOK_ID=xxx
```
4. ทดสอบผ่าน Sandbox ให้ครบ แล้วค่อยเปลี่ยนเป็นแอป Live + `PAYPAL_ENV=live`

ปุ่ม **บัตรเครดิต/เดบิต** เปิดหน้าใส่บัตรของ PayPal โดยไม่ต้องมีบัญชี PayPal (Guest Checkout) — ใช้ได้เฉพาะประเทศ/บัญชีที่ PayPal เปิด Guest Checkout ให้ ถ้าไม่เปิด ผู้ซื้อจะเห็นหน้าล็อกอิน PayPal แทน

## 4. ตั้งค่า Launcher
คัดลอก `account.example.json` เป็น `account.json` ไว้ข้าง `PTVgame-Launcher.exe` แล้วใส่ค่า:
```json
{ "supabaseUrl": "https://<project-ref>.supabase.co", "anonKey": "<anon หรือ publishable key>", "defaultAccess": "vip", "callbackPort": 53682, "offlineHours": 72 }
```
- ไม่มี `account.json` = Launcher ทำงานแบบเดิม (ไม่ต้องล็อกอิน เล่นได้ทุกเกม)
- `anonKey` เป็นคีย์สาธารณะ ใส่ในโปรแกรมได้ **ห้ามใส่ service_role key เด็ดขาด**
- `defaultAccess: "vip"` = ทุกเกมต้องเป็น VIP ถ้าจะให้เกมไหนเล่นฟรี ใส่ `"access": "free"` ในเกมนั้นใน `catalog.json`
- `offlineHours` = VIP ที่ตรวจล่าสุดแล้วเล่นต่อได้กี่ชั่วโมงตอนไม่มีเน็ต

## 5. ตั้งแอดมินคนแรก
สมัครใน Launcher ก่อน 1 ครั้ง ดู ID 8 หลักที่หน้าโปรไฟล์ แล้วรันใน SQL Editor:
```sql
update public.profiles set role = 'admin' where ptv_id = 12345678;
```
เปิด Launcher ใหม่ จะมีเมนู **แอดมิน**: ภาพรวม, ผู้ใช้ (ตั้งตัวแทน/แอดมิน, เพิ่ม/ลบ VIP, ระงับบัญชี), คำสั่งซื้อ (ดูสลิป, อนุมัติ/ปฏิเสธ), ราคาและพร้อมเพย์, บันทึกระบบ

ไปที่ **แอดมิน → ราคาและการชำระเงิน** ใส่เลขพร้อมเพย์และชื่อบัญชี แล้วตรวจราคา

## ราคาเริ่มต้น (แก้ได้ในหน้าแอดมิน)
| แผน | ราคาปกติ | ขีดฆ่า | ตัวแทน | QR (บาท) | ตัวแทน QR |
|---|---|---|---|---|---|
| รายเดือน 30 วัน | $20.00 | $25.00 | $15.00 | ฿690 | ฿520 |
| รายปี 365 วัน | $220.00 | $300.00 | $165.00 | ฿7,590 | ฿5,690 |

ราคาตัวแทนและราคาบาทเป็นค่าตั้งต้นที่เดาไว้ ปรับให้ตรงกับที่ต้องการก่อนเปิดขาย

## ความปลอดภัย
- ราคา, สิทธิ์ VIP, ระดับบัญชี คำนวณและบันทึกฝั่งเซิร์ฟเวอร์เท่านั้น (SQL `SECURITY DEFINER` + RLS) Launcher แก้เองไม่ได้
- PayPal: เซิร์ฟเวอร์ตรวจยอด, สกุลเงิน และเลขคำสั่งซื้อกับ PayPal ก่อนเปิด VIP; webhook ตรวจลายเซ็นกับ PayPal ทุกครั้ง
- Session เก็บแบบเข้ารหัส Windows DPAPI (อ่านได้เฉพาะผู้ใช้ Windows คนนั้น)
- **ข้อจำกัด:** การล็อกเกมอยู่ที่ Launcher ไฟล์เกมยังดาวน์โหลดจาก GitHub Releases สาธารณะ และ EXE เกมเปิดตรงได้ ถ้าต้องการกันจริงจัง ขั้นต่อไปคือให้เกมแต่ละเกมตรวจสิทธิ์กับเซิร์ฟเวอร์ตอนเปิด หรือย้ายไฟล์เกม VIP ไปเก็บแบบส่วนตัว

## ทดสอบ
- `supabase/functions/_shared/paypal_test.ts` — `deno test --allow-env` (7 กรณี: จ่ายครบ, จ่ายซ้ำ, ยอดไม่ตรง, สกุลเงินไม่ตรง, คนละคำสั่งซื้อ, ยังไม่อนุมัติ, รอตรวจ)
- `PTVgame-Launcher.exe --self-test` รวมการทดสอบ QR, OAuth callback, JSON และกฎสิทธิ์ VIP
