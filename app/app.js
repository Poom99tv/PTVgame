// PTVgame web: sign in, account, Game To Earn (cloud presets + control of the Launcher on this computer) and admin.
"use strict";
const SB_URL = "https://ynbnzpejlkodjctqtexs.supabase.co";
const SB_ANON = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InluYm56cGVqbGtvZGpjdHF0ZXhzIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTM3MTAwNTcsImV4cCI6MjEwOTI4NjA1N30.wiisZFAkuEhtQiJA2OKxZatTRs7emmUNsEC7eW_YFM8";
const APP_URL = location.origin + location.pathname;
const sb = supabase.createClient(SB_URL, SB_ANON, { auth: { flowType: "pkce", detectSessionInUrl: true, persistSession: true, autoRefreshToken: true } });

const $ = s => document.querySelector(s);
const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const view = $("#view");
const fmtDate = t => t ? new Date(t).toLocaleString("th-TH", { dateStyle: "medium", timeStyle: "short" }) : "-";
const baht = n => "฿" + Number(n || 0).toLocaleString("th-TH", { minimumFractionDigits: 0, maximumFractionDigits: 2 });
const money = (n, c) => c === "THB" ? baht(n) : "$" + Number(n || 0).toFixed(2);
const norm = s => String(s || "").toLowerCase().normalize("NFKD").replace(/[^\p{L}\p{N}]+/gu, "");
let toastTimer;
function toast(msg, err) { const t = $("#toast"); t.textContent = msg; t.className = "toast" + (err ? " err" : ""); t.hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => t.hidden = true, err ? 6000 : 3000); }
async function rpc(fn, args) { const { data, error } = await sb.rpc(fn, args || {}); if (error) throw new Error(error.message); return data; }
async function act(btn, fn, okMsg) { if (btn) btn.disabled = true; try { const r = await fn(); if (okMsg) toast(okMsg); return r; } catch (e) { toast(e.message || String(e), true); } finally { if (btn) btn.disabled = false; } }

let session = null, acct = null, recovery = false, giftData = null;
const ROLE = { admin: "แอดมิน", agent: "ตัวแทน", user: "ผู้ใช้" };
const STATUS = { pending: "รอชำระ", review: "รอตรวจสลิป", paid: "ชำระสำเร็จ", rejected: "ถูกปฏิเสธ", cancelled: "ยกเลิก", requested: "รอโอน" };
const METHOD = { promptpay: "QR พร้อมเพย์", paypal: "PayPal", card: "บัตร", manual: "แอดมิน", wallet: "กระเป๋าเงิน" };
const isVip = a => a && (a.role === "admin" || (a.vip_until && new Date(a.vip_until) > new Date()));
function avatar(el, a) { if (!el) return; const url = a?.avatar_url; el.style.backgroundImage = url ? `url("${url.replace(/"/g, "")}")` : ""; el.textContent = url ? "" : (a?.display_name || "?").trim().charAt(0).toUpperCase(); }

// ───────────────────────── routing
async function route() {
  if (recovery) return viewReset();
  if (!session) { $("#nav").hidden = true; $("#me").hidden = true; return viewLogin("signin"); }
  if (!acct) { try { acct = await rpc("my_account"); } catch (e) { view.innerHTML = `<div class="card pad"><h2>โหลดบัญชีไม่สำเร็จ</h2><p class="muted">${esc(e.message)}</p><button class="btn" onclick="location.reload()">ลองใหม่</button></div>`; return; } }
  $("#nav").hidden = false; $("#me").hidden = false; avatar($("#meAv"), acct);
  if (acct.admin) acct.role = "admin";
  $("#navAdmin").hidden = acct.role !== "admin";
  const tab = (location.hash.match(/^#\/(\w+)/) || [])[1] || "gte";
  document.querySelectorAll("#nav a").forEach(a => a.classList.toggle("on", a.dataset.tab === tab));
  stopPolling();
  if (tab === "account") return viewAccount();
  if (tab === "admin" && acct.role === "admin") return viewAdmin();
  return viewGte();
}
$("#logout").onclick = async () => { await sb.auth.signOut(); acct = null; session = null; location.hash = ""; route(); };

// ───────────────────────── sign in / sign up / reset
async function viewLogin(mode) {
  let providers = {};
  try { const r = await fetch(SB_URL + "/auth/v1/settings", { headers: { apikey: SB_ANON } }); providers = (await r.json()).external || {}; } catch {}
  const social = ["google", "facebook"].filter(p => providers[p]);
  const titles = { signin: "เข้าสู่ระบบ", signup: "สมัครสมาชิก", forgot: "ลืมรหัสผ่าน" };
  view.innerHTML = `<div class="auth"><h1>${titles[mode]}</h1><p class="muted">บัญชีเดียวกับ PTVgame Launcher</p>
   <form class="card" id="authForm">
    <label for="em">อีเมล</label><input id="em" type="email" autocomplete="email" required class="grow" style="width:100%">
    ${mode !== "forgot" ? `<label for="pw">รหัสผ่าน</label><input id="pw" type="password" minlength="6" autocomplete="${mode === "signup" ? "new-password" : "current-password"}" required style="width:100%">` : ""}
    <button class="btn blue wide" style="margin-top:18px" id="go">${mode === "signin" ? "เข้าสู่ระบบ" : mode === "signup" ? "สมัครสมาชิก" : "ส่งลิงก์ตั้งรหัสใหม่"}</button>
    ${social.length && mode !== "forgot" ? `<div class="or">หรือ</div>` + social.map(p => `<button type="button" class="btn ghost wide" style="margin-top:8px" data-p="${p}">เข้าสู่ระบบด้วย ${p === "google" ? "Google" : "Facebook"}</button>`).join("") : ""}
    <p class="small muted" style="margin:16px 0 0">${mode === "signin" ? `ยังไม่มีบัญชี? <a href="#" data-m="signup">สมัครสมาชิก</a> • <a href="#" data-m="forgot">ลืมรหัสผ่าน</a>` : `<a href="#" data-m="signin">กลับไปเข้าสู่ระบบ</a>`}</p>
   </form></div>`;
  view.querySelectorAll("[data-m]").forEach(a => a.onclick = e => { e.preventDefault(); viewLogin(a.dataset.m); });
  view.querySelectorAll("[data-p]").forEach(b => b.onclick = () => sb.auth.signInWithOAuth({ provider: b.dataset.p, options: { redirectTo: APP_URL } }));
  $("#authForm").onsubmit = async e => {
    e.preventDefault(); const email = $("#em").value.trim(), pw = $("#pw")?.value;
    await act($("#go"), async () => {
      if (mode === "signin") { const { error } = await sb.auth.signInWithPassword({ email, password: pw }); if (error) throw new Error(error.message === "Invalid login credentials" ? "อีเมลหรือรหัสผ่านไม่ถูกต้อง" : error.message); }
      else if (mode === "signup") { const { data, error } = await sb.auth.signUp({ email, password: pw, options: { emailRedirectTo: APP_URL } }); if (error) throw new Error(error.message); if (!data.session) view.innerHTML = `<div class="auth card"><h2>เช็กอีเมลของคุณ</h2><p class="muted">ส่งลิงก์ยืนยันไปที่ ${esc(email)} แล้ว กดลิงก์ในอีเมลแล้วหน้านี้จะเข้าสู่ระบบให้เอง</p></div>`; }
      else { const { error } = await sb.auth.resetPasswordForEmail(email, { redirectTo: APP_URL }); if (error) throw new Error(error.message); toast("ส่งลิงก์ตั้งรหัสผ่านใหม่ไปที่อีเมลแล้ว"); }
    });
  };
}
function viewReset() {
  view.innerHTML = `<div class="auth"><h1>ตั้งรหัสผ่านใหม่</h1><form class="card" id="rf"><label for="np">รหัสผ่านใหม่ (อย่างน้อย 6 ตัว)</label><input id="np" type="password" minlength="6" required style="width:100%" autocomplete="new-password"><button class="btn blue wide" style="margin-top:16px" id="sv">บันทึกรหัสผ่าน</button></form></div>`;
  $("#rf").onsubmit = async e => { e.preventDefault(); await act($("#sv"), async () => { const { error } = await sb.auth.updateUser({ password: $("#np").value }); if (error) throw new Error(error.message); recovery = false; toast("ตั้งรหัสผ่านใหม่แล้ว"); route(); }); };
}

// ───────────────────────── account
async function viewAccount() {
  const a = acct;
  view.innerHTML = `<h1>บัญชีของฉัน</h1>
   <div class="card prof"><span class="av" id="bigAv"></span><div class="grow">
     <div class="row"><input id="dn" value="${esc(a.display_name)}" maxlength="40" class="w220"><button class="btn" id="saveName">บันทึกชื่อ</button></div>
     <div class="row"><span class="muted">ID ของคุณ</span><b>${String(a.ptv_id).padStart(8, "0")}</b><button class="btn ghost" id="cp">คัดลอก</button></div>
     <div class="row"><span class="pill blue">${ROLE[a.role] || a.role}</span>${isVip(a) ? `<span class="pill gold">${a.role === "admin" ? "เล่นได้ทุกเกม" : "VIP ถึง " + fmtDate(a.vip_until)}</span>` : `<span class="pill">ฟรี</span>`}${a.agent_name ? `<span class="pill">ตัวแทน: ${esc(a.agent_name)}</span>` : ""}</div>
   </div></div>
   <div class="card"><h2>ซื้อ VIP</h2><p class="muted">ซื้อหรือแลกโค้ด VIP ได้ในหน้าร้านค้าของ Launcher (QR พร้อมเพย์ / PayPal / บัตร) แล้วสถานะจะขึ้นที่นี่ทันที</p></div>
   <div class="card"><h2>ประวัติคำสั่งซื้อ</h2><div class="scroll" id="orders"><p class="muted">กำลังโหลด…</p></div></div>`;
  avatar($("#bigAv"), a);
  $("#cp").onclick = () => { navigator.clipboard.writeText(String(a.ptv_id).padStart(8, "0")); toast("คัดลอก ID แล้ว"); };
  $("#saveName").onclick = e => act(e.target, async () => { await rpc("update_my_profile", { p_display_name: $("#dn").value.trim(), p_avatar_url: a.avatar_url || null }); acct = await rpc("my_account"); }, "บันทึกชื่อแล้ว");
  try {
    const list = await rpc("my_orders");
    $("#orders").innerHTML = !list?.length ? `<p class="muted">ยังไม่มีคำสั่งซื้อ</p>` : `<table><tr><th>#</th><th>รายการ</th><th>ยอด</th><th>ช่องทาง</th><th>สถานะ</th><th>วันที่</th></tr>${list.map(o => `<tr><td>${o.order_no}</td><td>${esc(o.plan_code === "yearly" ? "VIP รายปี" : "VIP รายเดือน")}${o.kind === "codes" ? " • โค้ด x" + o.quantity : ""}</td><td>${money(o.amount, o.currency)}</td><td>${METHOD[o.method] || o.method}</td><td>${STATUS[o.status] || o.status}</td><td>${fmtDate(o.created_at)}</td></tr>`).join("")}</table>`;
  } catch (e) { $("#orders").innerHTML = `<p class="muted">${esc(e.message)}</p>`; }
}

// ───────────────────────── Game To Earn
let gte = null, gteSub = "live", poll = null, logSeq = 0, logs = [];
function stopPolling() { if (poll) { clearInterval(poll); poll = null; } }
async function loadGifts() { if (!giftData) giftData = await (await fetch("gifts.json")).json(); return giftData; }
const giftByName = name => (giftData?.gifts || []).find(g => norm(g.name) === norm(name));
const giftImg = name => { const g = giftByName(name); return g ? `../gifts/${encodeURIComponent(g.file)}` : ""; };
async function loadGte() {
  await loadGifts();
  const r = await rpc("gte_get");
  gte = r ? { presets: r.presets, state: r.state || {}, updated: r.updated_at } : { presets: JSON.parse(JSON.stringify(giftData.defaultPresets)), state: {}, updated: null };
  if (!Array.isArray(gte.presets) || !gte.presets.length) gte.presets = JSON.parse(JSON.stringify(giftData.defaultPresets));
}
async function saveGte(btn) { return act(btn, async () => { const r = await rpc("gte_save", { p_presets: gte.presets, p_state: gte.state, p_source: "web" }); gte.updated = r.updated_at; }, "บันทึกแล้ว • Launcher จะใช้ค่าใหม่ภายใน 20 วินาที"); }
// local engine (the Launcher on this computer)
const eng = () => ({ port: Number(gte?.state?.port) || 8767, token: gte?.state?.token || "" });
async function engine(path, body) {
  const { port, token } = eng(); if (!token) throw new Error("no-token");
  const c = new AbortController(); const t = setTimeout(() => c.abort(), 2500);
  try {
    const r = await fetch(`http://127.0.0.1:${port}/${path}`, { method: body ? "POST" : "GET", headers: { Authorization: "Bearer " + token, ...(body ? { "Content-Type": "application/json" } : {}) }, body: body ? JSON.stringify(body) : undefined, signal: c.signal });
    const d = await r.json(); if (!r.ok) throw new Error(d.error || "HTTP " + r.status); return d;
  } finally { clearTimeout(t); }
}
const overlayUrl = (kind, bg) => { const { port, token } = eng(); return `http://127.0.0.1:${port}/overlay/${kind}?t=${token}${bg ? "&bg=" + encodeURIComponent(bg) : ""}`; };

async function viewGte() {
  view.innerHTML = `<h1>Game To Earn</h1><p class="muted">TikTok LIVE → ปุ่มกดใน Roblox • ตั้งค่าจากเว็บได้ ส่วนการกดคีย์ในเกมทำโดย Launcher บนเครื่องที่เปิดเกม</p><p class="muted pad">กำลังโหลด…</p>`;
  try { await loadGte(); } catch (e) { view.innerHTML += `<div class="card">${esc(e.message)}</div>`; return; }
  const subs = [["live", "((•))  เชื่อมต่อ LIVE"], ["preset", "☰  Preset และคำสั่ง"], ["obs", "▭  หน้าจอ OBS"], ["log", "≡  บันทึกเหตุการณ์"]];
  view.innerHTML = `<h1>Game To Earn</h1><p class="muted">ตั้งค่าที่นี่แล้ว Launcher ทุกเครื่องที่ล็อกอินบัญชีนี้จะใช้ค่าเดียวกัน • การกดคีย์ในเกมทำโดย Launcher บนเครื่องที่เปิดเกม</p>
   <div class="cols" style="margin-top:20px"><nav class="side" id="gsub">${subs.map(s => `<button data-s="${s[0]}" class="${s[0] === gteSub ? "on" : ""}">${s[1]}</button>`).join("")}<div class="status" id="engStatus">ตรวจหา Launcher…</div></nav><div id="gbody"></div></div>`;
  view.querySelectorAll("#gsub button").forEach(b => b.onclick = () => { gteSub = b.dataset.s; viewGteSub(); view.querySelectorAll("#gsub button").forEach(x => x.classList.toggle("on", x === b)); });
  viewGteSub();
}
async function engineStatus() {
  const el = $("#engStatus"); if (!el) return null;
  try { const s = await engine("state"); el.innerHTML = `<b style="color:var(--green)">● Launcher เครื่องนี้พร้อม</b><br>${s.status === "Connected" ? "ไลฟ์อยู่ @" + esc(s.username) : "ยังไม่เชื่อมต่อ LIVE"}`; return s; }
  catch (e) { el.innerHTML = `<b style="color:var(--mist)">○ ไม่พบ Launcher บนเครื่องนี้</b><br><span class="small">${eng().token ? "เปิด Launcher แล้วเข้าหน้า Game To Earn" : "เปิด Launcher ล็อกอินบัญชีนี้ แล้วเข้าหน้า Game To Earn 1 ครั้ง"}</span>`; return null; }
}
function viewGteSub() {
  stopPolling();
  const body = $("#gbody");
  if (gteSub === "live") return gteLive(body);
  if (gteSub === "preset") return gtePreset(body);
  if (gteSub === "obs") return gteObs(body);
  return gteLog(body);
}
async function gteLive(body) {
  const st = gte.state;
  body.innerHTML = `<div class="grid2"><div class="card"><h2>เชื่อมต่อ TikTok LIVE</h2>
     <div class="row"><input id="tu" placeholder="@ชื่อผู้ใช้" value="${st.username ? "@" + esc(st.username) : ""}" class="grow"><button class="btn blue" id="conn">เชื่อมต่อ</button><button class="btn red" id="disc">ตัดการเชื่อมต่อ</button></div>
     <p class="small muted">สั่ง Launcher บนเครื่องนี้ให้เชื่อมต่อ (ต้องเปิด Launcher ไว้) • ถ้าเบราว์เซอร์ถามสิทธิ์ "อุปกรณ์ในเครือข่าย" ให้กดอนุญาต</p></div>
    <div class="card"><h2>สถานะ</h2><div id="liveState" class="muted">กำลังตรวจ…</div></div></div>
   <div class="card"><h2>ทดสอบโดยไม่ต้องไลฟ์</h2><p class="small muted">คลิกหน้าต่าง Roblox ภายใน 3 วินาทีหลังกด เพื่อดูว่าคีย์ทำงาน</p>
     <div class="row"><button class="btn" data-t="gift">🎁 ของขวัญ</button><button class="btn" data-t="like">❤ Like</button><button class="btn" data-t="follow">👤 ติดตาม</button><button class="btn" data-t="chat">💬 แชท</button></div></div>
   <div class="card"><h2>Preset ที่ใช้</h2><div class="row"><select id="pp">${gte.presets.map((p, i) => `<option value="${i}" ${i === (st.preset || 0) ? "selected" : ""}>${i + 1}. ${esc(p.name)}</option>`).join("")}</select><button class="btn" id="ppSave">บันทึก</button></div></div>`;
  const refresh = async () => { const s = await engineStatus(); const el = $("#liveState"); if (!el) return;
    el.innerHTML = s ? `<div class="row"><span class="pill ${s.status === "Connected" ? "green" : s.status === "Error" ? "red" : ""}">${{ Connected: "● เชื่อมต่อแล้ว", Connecting: "กำลังเชื่อมต่อ…", Error: "เชื่อมต่อไม่สำเร็จ", Ended: "LIVE จบแล้ว" }[s.status] || "ยังไม่เชื่อมต่อ"}</span>${s.username ? "@" + esc(s.username) : ""}</div>${s.host?.viewers ? `<p>👀 ${s.host.viewers.toLocaleString()} คนดู</p>` : ""}<p class="small">❤ ${s.totals.likes.toLocaleString()} • 🎁 ${s.totals.gifts.toLocaleString()} • 💎 ${s.totals.diamonds.toLocaleString()}</p>${s.detail ? `<p class="small muted">${esc(s.detail)}</p>` : ""}` : `<p>ไม่พบ Launcher บนเครื่องนี้ ตั้งค่า Preset ได้ แต่ต้องเปิด Launcher เพื่อเชื่อมต่อ LIVE</p>`; };
  refresh(); poll = setInterval(refresh, 2000);
  $("#conn").onclick = e => act(e.target, async () => { const u = $("#tu").value.trim().replace(/^@/, ""); if (!/^[A-Za-z0-9_.]{2,24}$/.test(u)) throw new Error("กรุณาใส่ชื่อ TikTok เช่น @poomtv99"); st.username = u; await engine("connect", { username: u }).catch(err => { throw new Error(err.message === "no-token" || err.name === "TypeError" || err.name === "AbortError" ? "ไม่พบ Launcher บนเครื่องนี้" : err.message); }); rpc("gte_save", { p_presets: gte.presets, p_state: st, p_source: "web" }).catch(() => {}); refresh(); }, "กำลังเชื่อมต่อ…");
  $("#disc").onclick = e => act(e.target, async () => { await engine("disconnect", {}); refresh(); }, "ตัดการเชื่อมต่อแล้ว");
  body.querySelectorAll("[data-t]").forEach(b => b.onclick = () => act(b, async () => { await new Promise(r => setTimeout(r, 3000)); const p = gte.presets[st.preset || 0] || {}; const first = Object.keys(p.gift_map || {})[0]; await engine("test", { kind: b.dataset.t, giftName: first || "Rose", amount: b.dataset.t === "like" ? (p.like_target || 100) : 1, comment: "ทดสอบเสียงอ่านแชท" }); }, "ส่งทดสอบแล้ว"));
  $("#ppSave").onclick = e => { st.preset = Number($("#pp").value); saveGte(e.target); };
}

let pIdx = 0;
function gtePreset(body) {
  const ps = gte.presets; pIdx = Math.min(Math.max(0, gte.state.preset || pIdx || 0), ps.length - 1);
  const p = ps[pIdx]; p.gift_map = p.gift_map || {};
  const rows = Object.entries(p.gift_map).map(([name, v]) => ({ name: giftByName(name)?.name || name, key: typeof v === "string" ? v : v?.key || "", win: !!(v && v.win_enabled), delta: (v && v.win_delta) ?? 1 }));
  const flush = () => { const m = {}; for (const r of rows) if (r.name.trim()) m[r.name.trim().toLowerCase()] = { key: r.key.trim(), win_enabled: r.win, win_delta: Number(r.delta) || 0 }; p.gift_map = m; };
  const voices = [["off", "ปิดเสียงอ่าน"], ["th-TH-PremwadeeNeural", "ไทย หญิง (Premwadee)"], ["th-TH-NiwatNeural", "ไทย ชาย (Niwat)"], ["en-US-JennyNeural", "English (Jenny)"]];
  body.innerHTML = `<div class="cols" style="grid-template-columns:220px 1fr"><div class="card"><h2>รายการ Preset</h2><button class="btn blue wide" id="pAdd">+ สร้าง Preset</button>
     <div style="margin-top:10px">${ps.map((x, i) => `<button class="btn ${i === pIdx ? "blue" : "ghost"} wide" style="justify-content:flex-start;margin-top:6px" data-i="${i}">${i + 1}. ${esc(x.name)}${i === pIdx ? " ✓" : ""}</button>`).join("")}</div>
     <div class="row" style="margin-top:10px"><button class="btn" id="pDup">⧉ สำเนา</button><button class="btn red" id="pDel">ลบ</button></div></div>
   <div class="card"><h2>แก้ไข Preset: ${pIdx + 1}. ${esc(p.name)}</h2>
     <div class="row"><span class="muted">ชื่อ</span><input id="pName" value="${esc(p.name)}" class="w220" maxlength="40"></div>
     <h3 style="margin-top:14px">คำสั่ง (เหตุการณ์ → ปุ่มกด)</h3>
     <div class="cmd"><span>👤</span><span>ติดตาม</span><span class="arrow">→</span><input id="fSeq" value="${esc(p.follow_seq ?? "r")}" class="w90"><span class="extra"><label class="row" style="margin:0"><input type="checkbox" id="fOn" ${p.follow_enabled !== false ? "checked" : ""}> เปิดใช้</label><label class="row" style="margin:0"><input type="checkbox" id="fOnce" ${p.follow_once !== false ? "checked" : ""}> ครั้งเดียว/คน</label></span><span></span></div>
     <div class="cmd"><span style="color:#FF6B9A">❤</span><span class="row" style="margin:0">Like <input id="lT" type="number" min="1" value="${p.like_target ?? 100}" class="w90"></span><span class="arrow">→</span><input id="lSeq" value="${esc(p.like_seq ?? "v")}" class="w90"><span class="extra"><label class="row" style="margin:0"><input type="checkbox" id="lOn" ${p.like_enabled !== false ? "checked" : ""}> เปิดใช้</label></span><span></span></div>
     <div id="gRows"></div>
     <button class="btn green" id="gAdd" style="margin-top:8px">+ เพิ่มของขวัญ</button>
     <p class="small muted">คีย์: yy = กด y สองครั้ง • a b = กด a แล้ว b • ปุ่มพิเศษ: up down left right space enter</p>
     <div class="grid2" style="margin-top:12px"><div><label for="dly">ระยะหน่วง (มิลลิวินาที)</label><input id="dly" type="number" min="10" max="2000" value="${p.key_delay_ms ?? 130}" class="w140"></div>
       <div><label for="vc">เสียงอ่านข้อความ</label><select id="vc">${voices.map(v => `<option value="${v[0]}" ${(gte.state.voice || "off") === v[0] ? "selected" : ""}>${v[1]}</option>`).join("")}</select></div></div>
     <button class="btn blue wide" id="pSave" style="margin-top:16px">💾 บันทึก</button>
     <p class="small muted" style="margin-bottom:0">${gte.updated ? "บันทึกล่าสุด " + fmtDate(gte.updated) : "ยังไม่เคยบันทึกบนเว็บ"}</p></div></div>`;
  const drawRows = () => {
    $("#gRows").innerHTML = rows.map((r, i) => `<div class="cmd">${giftImg(r.name) ? `<img src="${giftImg(r.name)}" alt="">` : "<span>🎁</span>"}<button class="btn ghost" style="justify-content:flex-start" data-pick="${i}">${esc(r.name || "เลือกของขวัญ…")}${giftByName(r.name)?.price ? ` <span class="small muted">💎${giftByName(r.name).price}</span>` : ""}</button><span class="arrow">→</span><input data-k="${i}" value="${esc(r.key)}" class="w90"><span class="row extra" style="margin:0"><label class="row" style="margin:0"><input type="checkbox" data-w="${i}" ${r.win ? "checked" : ""}> Win</label><input data-d="${i}" type="number" value="${r.delta}" class="w60"></span><button class="btn ghost" data-x="${i}" aria-label="ลบ">🗑</button></div>`).join("");
    $("#gRows").querySelectorAll("[data-pick]").forEach(b => b.onclick = async () => { const g = await pickGift(rows[b.dataset.pick].name); if (g) { rows[b.dataset.pick].name = g.name; drawRows(); } });
    $("#gRows").querySelectorAll("[data-k]").forEach(i => i.oninput = () => rows[i.dataset.k].key = i.value);
    $("#gRows").querySelectorAll("[data-w]").forEach(i => i.onchange = () => rows[i.dataset.w].win = i.checked);
    $("#gRows").querySelectorAll("[data-d]").forEach(i => i.oninput = () => rows[i.dataset.d].delta = Number(i.value) || 0);
    $("#gRows").querySelectorAll("[data-x]").forEach(b => b.onclick = () => { rows.splice(b.dataset.x, 1); drawRows(); });
  };
  drawRows();
  const collect = () => { flush(); p.name = $("#pName").value.trim() || p.name; p.follow_seq = $("#fSeq").value.trim(); p.follow_enabled = $("#fOn").checked; p.follow_once = $("#fOnce").checked;
    p.like_target = Math.max(1, Number($("#lT").value) || 100); p.like_seq = $("#lSeq").value.trim(); p.like_enabled = $("#lOn").checked; p.key_delay_ms = Math.min(2000, Math.max(10, Number($("#dly").value) || 130)); gte.state.voice = $("#vc").value; };
  $("#gAdd").onclick = async () => { const g = await pickGift(""); if (g) { rows.push({ name: g.name, key: "", win: false, delta: 1 }); drawRows(); } };
  body.querySelectorAll("[data-i]").forEach(b => b.onclick = () => { collect(); gte.state.preset = pIdx = Number(b.dataset.i); gtePreset(body); });
  $("#pAdd").onclick = () => { collect(); ps.push({ name: "Preset " + (ps.length + 1), gift_map: {}, follow_enabled: true, follow_seq: "r", follow_once: true, like_enabled: true, like_seq: "v", like_target: 100, key_delay_ms: 130 }); gte.state.preset = pIdx = ps.length - 1; gtePreset(body); };
  $("#pDup").onclick = () => { collect(); const c = JSON.parse(JSON.stringify(p)); c.name += " (copy)"; ps.push(c); gte.state.preset = pIdx = ps.length - 1; gtePreset(body); };
  $("#pDel").onclick = () => { if (ps.length <= 1) return toast("ต้องมีอย่างน้อย 1 Preset", true); if (!confirm("ลบ Preset นี้?")) return; ps.splice(pIdx, 1); gte.state.preset = pIdx = Math.max(0, pIdx - 1); gtePreset(body); };
  $("#pSave").onclick = e => { collect(); gte.state.preset = pIdx; saveGte(e.target); };
}
function pickGift(current) {
  return new Promise(resolve => {
    const d = $("#dlg"), b = $("#dlgBody"); let done = false;
    b.innerHTML = `<div class="row"><h2 class="grow" style="margin:0">เลือกของขวัญ</h2><button class="btn ghost" value="cancel">ปิด</button></div><input id="gq" placeholder="ค้นหาชื่อของขวัญ" style="width:100%;margin-top:10px"><div class="gifts" id="gl"></div>`;
    const fill = () => { const q = norm($("#gq").value); $("#gl").innerHTML = giftData.gifts.filter(g => !q || norm(g.name).includes(q)).slice(0, 300).map(g => `<button type="button" data-n="${esc(g.name)}" style="${norm(g.name) === norm(current) ? "border-color:var(--blue)" : ""}"><img src="../gifts/${encodeURIComponent(g.file)}" alt="" loading="lazy">${esc(g.name)}${g.price ? `<br><span class="muted">💎 ${g.price}</span>` : ""}</button>`).join("");
      $("#gl").querySelectorAll("button").forEach(x => x.onclick = () => { done = true; d.close(); resolve(giftByName(x.dataset.n)); }); };
    $("#gq").oninput = fill; fill(); d.onclose = () => { if (!done) resolve(null); }; d.showModal(); $("#gq").focus();
  });
}
function gteObs(body) {
  const bg = gte.state.obs_bg || "";
  const W = [["likers", "❤ ยอดกด Like"], ["gifters", "🎁 ยอดส่งของขวัญ"], ["win", "🏆 นับ Win"], ["jar", "🫙 แก้วโหล"], ["stats", "📊 สรุปยอด"]];
  body.innerHTML = `<div class="card"><h2>ลิงก์หน้าจอสำหรับ OBS / TikTok LIVE Studio</h2><p class="muted small">ลิงก์ทำงานเมื่อเปิด Launcher บนเครื่องที่ไลฟ์ • OBS: Sources → Browser วางลิงก์ กว้าง 400 สูง 600 • TikTok LIVE Studio: แหล่ง Link หรือใช้ปุ่ม 🪟 หน้าต่าง ใน Launcher แล้ว Window Capture</p>
    <div class="row"><span class="muted small">พื้นหลังลิงก์</span><select id="obg"><option value="">โปร่งใส</option><option value="#00ff00" ${bg ? "selected" : ""}>เขียว (Chroma)</option></select></div>
    ${eng().token ? "" : `<p class="status">เปิด Launcher ล็อกอินบัญชีนี้ แล้วเข้าหน้า Game To Earn 1 ครั้ง ลิงก์จะขึ้นที่นี่</p>`}
    <div class="ovl" style="margin-top:12px">${W.map(w => `<div class="card"><h3>${w[1]}</h3><div class="row"><button class="btn blue" data-c="${w[0]}" ${eng().token ? "" : "disabled"}>🔗 คัดลอกลิงก์</button><button class="btn" data-v="${w[0]}" ${eng().token ? "" : "disabled"}>ดูตัวอย่าง</button></div></div>`).join("")}</div></div>
   <div class="card"><h2 id="pvT">ตัวอย่างผลลัพธ์</h2><iframe class="frame" id="pv" title="ตัวอย่าง overlay"></iframe><p class="small muted">ตัวอย่างแสดงเมื่อ Launcher บนเครื่องนี้เปิดอยู่</p></div>`;
  $("#obg").onchange = () => { gte.state.obs_bg = $("#obg").value; saveGte(null); gteObs(body); };
  body.querySelectorAll("[data-c]").forEach(b => b.onclick = () => { navigator.clipboard.writeText(overlayUrl(b.dataset.c, bg)); toast("คัดลอกลิงก์แล้ว"); });
  body.querySelectorAll("[data-v]").forEach(b => b.onclick = () => { $("#pv").src = overlayUrl(b.dataset.v, bg || "#173040"); $("#pvT").textContent = "ตัวอย่างผลลัพธ์: " + b.closest(".card").querySelector("h3").textContent; });
  engineStatus();
}
function gteLog(body) {
  body.innerHTML = `<div class="card"><div class="row"><h2 class="grow" style="margin:0">บันทึกเหตุการณ์ (Launcher เครื่องนี้)</h2><button class="btn ghost" id="lc">🗑 ล้าง</button></div><div class="log" id="lg"></div></div>`;
  const lg = $("#lg"), line = l => `<div><span class="t">[${new Date(l.at).toLocaleTimeString("th-TH")}]</span> ${esc(l.text)}</div>`;
  const draw = () => { lg.innerHTML = logs.length ? logs.map(line).join("") : `<span class="muted">ยังไม่มีเหตุการณ์</span>`; lg.scrollTop = lg.scrollHeight; };
  $("#lc").onclick = () => { logs = []; draw(); };
  const text = e => ({ gift: `🎁 ${e.name || e.viewer} ส่ง ${e.giftName} x${e.amount}`, like: `❤ ${e.name || e.viewer} กดถูกใจ ${e.amount}`, follow: `👤 ${e.name || e.viewer} ติดตาม`, chat: `💬 ${e.name || e.viewer}: ${e.comment}`, status: `● สถานะ: ${e.status}${e.detail ? " • " + e.detail : ""}` }[e.kind] || e.kind);
  const tick = async () => { try { const d = await engine("events?after=" + logSeq); for (const e of d.events) { logSeq = Math.max(logSeq, e.seq); logs.push({ at: e.at, text: text(e) }); } if (logs.length > 500) logs = logs.slice(-500); draw(); } catch { if (!logs.length) lg.innerHTML = `<span class="muted">ไม่พบ Launcher บนเครื่องนี้ • เปิด Launcher แล้วเข้าหน้า Game To Earn</span>`; } };
  draw(); tick(); poll = setInterval(tick, 1000); engineStatus();
}

// ───────────────────────── admin
let adminTab = "overview";
async function viewAdmin() {
  const tabs = [["overview", "ภาพรวม"], ["users", "ผู้ใช้"], ["orders", "คำสั่งซื้อ"], ["withdrawals", "ถอนเงินตัวแทน"], ["prices", "ราคาและตั้งค่า"], ["live", "กำลังไลฟ์"], ["audit", "Log"]];
  view.innerHTML = `<h1>แอดมิน</h1><div class="tabs">${tabs.map(t => `<button data-a="${t[0]}" class="${t[0] === adminTab ? "on" : ""}">${t[1]}</button>`).join("")}</div><div id="abody"><p class="muted">กำลังโหลด…</p></div>`;
  view.querySelectorAll("[data-a]").forEach(b => b.onclick = () => { adminTab = b.dataset.a; viewAdmin(); });
  const body = $("#abody");
  try { await ({ overview: aOverview, users: aUsers, orders: aOrders, withdrawals: aWithdrawals, prices: aPrices, live: aLive, audit: aAudit }[adminTab])(body); }
  catch (e) { body.innerHTML = `<div class="card">${esc(e.message)}</div>`; }
}
async function aOverview(body) {
  const s = await rpc("admin_stats");
  const box = (n, l) => `<div class="card stat"><b>${n}</b><span>${l}</span></div>`;
  body.innerHTML = `<div class="grid3">${box(s.users, "ผู้ใช้ทั้งหมด")}${box(s.vip, "VIP ที่ใช้งาน")}${box(s.agents, "ตัวแทน")}${box(s.review, "รอตรวจสลิป")}${box(s.pending, "รอชำระ")}${box(s.withdrawals, "คำขอถอนเงิน")}${box(baht(s.revenue_thb_30d), "รายได้ THB 30 วัน")}${box("$" + Number(s.revenue_usd_30d).toFixed(2), "รายได้ USD 30 วัน")}${box(s.new_users_7d, "สมัครใหม่ 7 วัน")}</div>`;
}
async function aUsers(body, q) {
  body.innerHTML = `<div class="card"><div class="row"><input id="uq" placeholder="ค้นหา ID / ชื่อ / อีเมล / รหัสตัวแทน" value="${esc(q || "")}" class="grow"><button class="btn blue" id="us">ค้นหา</button></div><div class="scroll" id="ul"><p class="muted">กำลังโหลด…</p></div></div>`;
  $("#us").onclick = () => aUsers(body, $("#uq").value.trim()); $("#uq").onkeydown = e => { if (e.key === "Enter") $("#us").click(); };
  const list = await rpc("admin_list_users", { p_search: q || "", p_limit: 100, p_offset: 0 });
  $("#ul").innerHTML = `<table><tr><th>ID</th><th>ชื่อ / อีเมล</th><th>บทบาท</th><th>VIP</th><th>ตัวแทนของ</th><th>กระเป๋า</th><th>จัดการ</th></tr>${list.map((u, i) => `<tr><td>${String(u.ptv_id).padStart(8, "0")}</td><td><b>${esc(u.display_name)}</b>${u.banned ? ' <span class="pill red">ระงับ</span>' : ""}<br><span class="small muted">${esc(u.email || "")}</span></td>
     <td><select data-role="${i}">${["user", "agent", "admin"].map(r => `<option value="${r}" ${u.role === r ? "selected" : ""}>${ROLE[r]}</option>`).join("")}</select>${u.agent_code ? `<br><span class="small muted">โค้ด ${esc(u.agent_code)} • ลูกค้า ${u.customers}</span>` : ""}</td>
     <td>${u.vip_until && new Date(u.vip_until) > new Date() ? `<span class="pill gold">ถึง ${fmtDate(u.vip_until)}</span>` : '<span class="muted">-</span>'}</td>
     <td>${u.agent_name ? esc(u.agent_name) + `<br><span class="small muted">${String(u.agent_ptv_id).padStart(8, "0")}</span>` : '<span class="muted">-</span>'}</td><td>${u.role === "agent" || Number(u.wallet_thb) ? baht(u.wallet_thb) : '<span class="muted">-</span>'}</td>
     <td class="row" style="margin:0"><button class="btn gold" data-vip="${i}">+ VIP</button><button class="btn" data-wal="${i}">กระเป๋า</button><button class="btn" data-ref="${i}">ผูกตัวแทน</button><button class="btn ${u.banned ? "green" : "red"}" data-ban="${i}">${u.banned ? "ปลดระงับ" : "ระงับ"}</button></td></tr>`).join("")}</table>${list.length ? "" : '<p class="muted">ไม่พบผู้ใช้</p>'}`;
  const again = () => aUsers(body, q);
  $("#ul").querySelectorAll("[data-role]").forEach(s => s.onchange = () => act(s, () => rpc("admin_set_role", { p_user: list[s.dataset.role].id, p_role: s.value }), "เปลี่ยนบทบาทแล้ว").then(again));
  $("#ul").querySelectorAll("[data-vip]").forEach(b => b.onclick = () => { const d = prompt("เพิ่ม VIP กี่วัน? (ใส่ติดลบเพื่อลด)", "30"); if (!d) return; act(b, () => rpc("admin_grant_vip", { p_user: list[b.dataset.vip].id, p_days: parseInt(d, 10) }), "ปรับ VIP แล้ว").then(again); });
  $("#ul").querySelectorAll("[data-wal]").forEach(b => b.onclick = () => { const a = prompt("เพิ่ม/ลดเงินในกระเป๋า (บาท) เช่น 100 หรือ -50", ""); if (!a) return; const note = prompt("หมายเหตุ", "") || ""; act(b, () => rpc("admin_adjust_wallet", { p_user: list[b.dataset.wal].id, p_amount: Number(a), p_note: note }), "ปรับกระเป๋าแล้ว").then(again); });
  $("#ul").querySelectorAll("[data-ref]").forEach(b => b.onclick = () => { const a = prompt("ID ตัวแทน 8 หลัก (เว้นว่างเพื่อยกเลิกการผูก)", ""); if (a === null) return; act(b, () => rpc("admin_set_referrer", { p_user: list[b.dataset.ref].id, p_agent_ptv_id: a.trim() ? Number(a) : null }), "บันทึกแล้ว").then(again); });
  $("#ul").querySelectorAll("[data-ban]").forEach(b => b.onclick = () => { const u = list[b.dataset.ban]; if (!confirm((u.banned ? "ปลดระงับ " : "ระงับ ") + u.display_name + "?")) return; act(b, () => rpc("admin_set_banned", { p_user: u.id, p_banned: !u.banned }), "บันทึกแล้ว").then(again); });
}
async function aOrders(body, status) {
  status = status ?? "review";
  body.innerHTML = `<div class="card"><div class="row"><select id="os">${[["review", "รอตรวจสลิป"], ["pending", "รอชำระ"], ["paid", "ชำระสำเร็จ"], ["rejected", "ถูกปฏิเสธ"], ["", "ทั้งหมด"]].map(s => `<option value="${s[0]}" ${s[0] === status ? "selected" : ""}>${s[1]}</option>`).join("")}</select></div><div class="scroll" id="ol"><p class="muted">กำลังโหลด…</p></div></div>`;
  $("#os").onchange = () => aOrders(body, $("#os").value);
  const list = await rpc("admin_list_orders", { p_status: status || null, p_limit: 100, p_offset: 0 });
  $("#ol").innerHTML = list.length ? `<table><tr><th>#</th><th>ผู้ซื้อ → ผู้รับ</th><th>รายการ</th><th>ยอด</th><th>ช่องทาง</th><th>สถานะ</th><th>วันที่</th><th></th></tr>${list.map((o, i) => `<tr><td>${o.order_no}</td><td>${esc(o.buyer_name)} <span class="small muted">${String(o.buyer_ptv_id).padStart(8, "0")}</span>${o.target_ptv_id !== o.buyer_ptv_id ? `<br>→ ${esc(o.target_name)} <span class="small muted">${String(o.target_ptv_id).padStart(8, "0")}</span>` : ""}</td>
     <td>${o.plan_code === "yearly" ? "VIP รายปี" : "VIP รายเดือน"}${o.kind === "codes" ? " • โค้ด x" + o.quantity : ""}${o.discount_code ? `<br><span class="small muted">โค้ดลด ${esc(o.discount_code)} ${o.discount_pct}%</span>` : ""}${o.commission_thb ? `<br><span class="small muted">ค่าคอม ${esc(o.commission_agent_name)} ${baht(o.commission_thb)}</span>` : ""}</td>
     <td>${money(o.amount, o.currency)}</td><td>${METHOD[o.method] || o.method}</td><td>${STATUS[o.status] || o.status}${o.note ? `<br><span class="small muted">${esc(o.note)}</span>` : ""}</td><td>${fmtDate(o.created_at)}</td>
     <td class="row" style="margin:0">${o.slip_path ? `<button class="btn" data-slip="${i}">ดูสลิป</button>` : ""}${["review", "pending"].includes(o.status) ? `<button class="btn green" data-ok="${i}">อนุมัติ</button><button class="btn red" data-no="${i}">ปฏิเสธ</button>` : ""}</td></tr>`).join("")}</table>` : '<p class="muted">ไม่มีคำสั่งซื้อ</p>';
  const again = () => aOrders(body, status);
  $("#ol").querySelectorAll("[data-slip]").forEach(b => b.onclick = () => act(b, async () => { const { data, error } = await sb.storage.from("slips").download(list[b.dataset.slip].slip_path); if (error) throw new Error(error.message); const url = URL.createObjectURL(data); $("#dlgBody").innerHTML = `<div class="row"><h2 class="grow" style="margin:0">สลิป #${list[b.dataset.slip].order_no}</h2><button class="btn ghost" value="close">ปิด</button></div><img src="${url}" alt="สลิปการโอน" style="max-width:100%;margin-top:12px;border-radius:10px">`; $("#dlg").showModal(); }));
  $("#ol").querySelectorAll("[data-ok]").forEach(b => b.onclick = () => { const o = list[b.dataset.ok]; if (!confirm(`อนุมัติคำสั่งซื้อ #${o.order_no} ${money(o.amount, o.currency)}?`)) return; act(b, () => rpc("admin_approve_order", { p_order: o.id }), "อนุมัติแล้ว • VIP เปิดให้ผู้รับแล้ว").then(again); });
  $("#ol").querySelectorAll("[data-no]").forEach(b => b.onclick = () => { const r = prompt("เหตุผลที่ปฏิเสธ", "สลิปไม่ถูกต้อง"); if (r === null) return; act(b, () => rpc("admin_reject_order", { p_order: list[b.dataset.no].id, p_reason: r }), "ปฏิเสธแล้ว").then(again); });
}
async function aWithdrawals(body, status) {
  status = status ?? "requested";
  body.innerHTML = `<div class="card"><div class="row"><select id="ws">${[["requested", "รอโอน"], ["paid", "โอนแล้ว"], ["rejected", "ปฏิเสธ"], ["", "ทั้งหมด"]].map(s => `<option value="${s[0]}" ${s[0] === status ? "selected" : ""}>${s[1]}</option>`).join("")}</select></div><div class="scroll" id="wl"></div></div>`;
  $("#ws").onchange = () => aWithdrawals(body, $("#ws").value);
  const list = await rpc("admin_list_withdrawals", { p_status: status });
  $("#wl").innerHTML = list.length ? `<table><tr><th>ตัวแทน</th><th>ยอด</th><th>บัญชีรับเงิน</th><th>สถานะ</th><th>วันที่</th><th></th></tr>${list.map((w, i) => `<tr><td>${esc(w.agent_name)} <span class="small muted">${String(w.agent_ptv_id).padStart(8, "0")}</span><br><span class="small muted">คงเหลือ ${baht(w.wallet_thb)}</span></td><td><b>${baht(w.amount_thb)}</b></td><td>${esc(w.method === "bank" ? "ธนาคาร" : "พร้อมเพย์")}<br>${esc(w.account)}</td><td>${STATUS[w.status] || w.status}${w.admin_note ? `<br><span class="small muted">${esc(w.admin_note)}</span>` : ""}</td><td>${fmtDate(w.created_at)}</td>
     <td class="row" style="margin:0">${w.status === "requested" ? `<button class="btn green" data-paid="${i}">โอนแล้ว</button><button class="btn red" data-rej="${i}">ปฏิเสธ (คืนเงิน)</button>` : ""}</td></tr>`).join("")}</table>` : '<p class="muted">ไม่มีรายการ</p>';
  const again = () => aWithdrawals(body, status);
  $("#wl").querySelectorAll("[data-paid]").forEach(b => b.onclick = () => { const n = prompt("หมายเหตุ (เช่น เลขอ้างอิงการโอน)", "โอนแล้ว"); if (n === null) return; act(b, () => rpc("admin_process_withdrawal", { p_id: list[b.dataset.paid].id, p_paid: true, p_note: n }), "ยืนยันการโอนแล้ว").then(again); });
  $("#wl").querySelectorAll("[data-rej]").forEach(b => b.onclick = () => { const n = prompt("เหตุผล", "ข้อมูลบัญชีไม่ถูกต้อง"); if (n === null) return; act(b, () => rpc("admin_process_withdrawal", { p_id: list[b.dataset.rej].id, p_paid: false, p_note: n }), "ปฏิเสธและคืนเงินเข้ากระเป๋าแล้ว").then(again); });
}
async function aPrices(body) {
  const { data: plans, error } = await sb.from("plans").select("*").order("sort"); if (error) throw new Error(error.message);
  const st = await rpc("admin_get_settings");
  const S = [["promptpay_id", "เบอร์ / เลขพร้อมเพย์"], ["promptpay_name", "ชื่อบัญชีพร้อมเพย์"], ["usd_thb_rate", "อัตรา USD → THB"], ["discount_pct", "ส่วนลดโค้ดตัวแทน (%)"], ["commission_months", "ผูกลูกค้ากับตัวแทน (เดือน)"], ["min_withdrawal_thb", "ถอนขั้นต่ำ (บาท)"], ["max_pending_orders", "คำสั่งซื้อค้างได้สูงสุด"], ["download_url", "ลิงก์ดาวน์โหลด Launcher"]];
  body.innerHTML = `<div class="card"><h2>ราคาแพ็กเกจ</h2><div class="scroll"><table><tr><th>แพ็กเกจ</th><th>USD</th><th>USD ขีดฆ่า</th><th>USD ตัวแทน</th><th>THB</th><th>THB ตัวแทน</th><th>เปิดขาย</th><th></th></tr>${plans.map((p, i) => `<tr><td><b>${esc(p.name)}</b><br><span class="small muted">${p.days} วัน</span></td>${["price_usd", "list_price_usd", "agent_price_usd", "price_thb", "agent_price_thb"].map(k => `<td><input type="number" step="0.01" min="0" data-p="${i}" data-k="${k}" value="${p[k] ?? ""}" class="w90"></td>`).join("")}<td><input type="checkbox" data-p="${i}" data-k="active" ${p.active ? "checked" : ""}></td><td><button class="btn blue" data-save="${i}">บันทึก</button></td></tr>`).join("")}</table></div></div>
   <div class="card"><h2>ตั้งค่าระบบ</h2><div class="grid2">${S.map(s => `<div><label for="s_${s[0]}">${s[1]}</label><div class="row" style="margin:0"><input id="s_${s[0]}" value="${esc(st?.[s[0]] ?? "")}" class="grow"><button class="btn" data-set="${s[0]}">บันทึก</button></div></div>`).join("")}</div></div>`;
  body.querySelectorAll("[data-save]").forEach(b => b.onclick = () => { const i = b.dataset.save; const v = k => { const el = body.querySelector(`[data-p="${i}"][data-k="${k}"]`); return k === "active" ? el.checked : (el.value === "" ? null : Number(el.value)); };
    act(b, () => rpc("admin_update_plan", { p_code: plans[i].code, p_price_usd: v("price_usd"), p_list_price_usd: v("list_price_usd"), p_agent_price_usd: v("agent_price_usd"), p_price_thb: v("price_thb"), p_agent_price_thb: v("agent_price_thb"), p_active: v("active") }), "บันทึกราคาแล้ว"); });
  body.querySelectorAll("[data-set]").forEach(b => b.onclick = () => act(b, () => rpc("admin_set_setting", { p_key: b.dataset.set, p_value: $("#s_" + b.dataset.set).value.trim() }), "บันทึกแล้ว"));
}
async function aLive(body) {
  const list = await rpc("live_now");
  body.innerHTML = `<div class="card"><h2>กำลังไลฟ์ตอนนี้ (${list.length})</h2>${list.length ? `<table><tr><th>TikTok</th><th>เกม</th><th>คนดู</th><th>เริ่ม</th></tr>${list.map(s => `<tr><td><a href="https://www.tiktok.com/@${encodeURIComponent(s.tiktok_username)}/live" target="_blank" rel="noopener">${esc(s.display_name || s.tiktok_username)} @${esc(s.tiktok_username)}</a>${s.vip ? ' <span class="pill gold">VIP</span>' : ""}</td><td>${esc(s.game)}</td><td>${Number(s.viewers).toLocaleString()}</td><td>${fmtDate(s.started_at)}</td></tr>`).join("")}</table>` : '<p class="muted">ยังไม่มีใครไลฟ์</p>'}</div>`;
}
async function aAudit(body) {
  const list = await rpc("admin_audit", { p_limit: 200 });
  body.innerHTML = `<div class="card scroll"><table><tr><th>เวลา</th><th>ผู้ทำ</th><th>การกระทำ</th><th>ผู้ใช้</th><th>รายละเอียด</th></tr>${list.map(a => `<tr><td>${fmtDate(a.at)}</td><td>${esc(a.actor || "-")}</td><td>${esc(a.action)}</td><td>${a.target_ptv_id ? String(a.target_ptv_id).padStart(8, "0") : "-"}</td><td class="small muted">${esc(JSON.stringify(a.detail || {}))}</td></tr>`).join("")}</table></div>`;
}

// ───────────────────────── start
sb.auth.onAuthStateChange((event, s) => {
  if (event === "PASSWORD_RECOVERY") recovery = true;
  const changed = (s?.user?.id || null) !== (session?.user?.id || null);
  session = s; if (changed) { acct = null; gte = null; setTimeout(route, 0); }
});
window.addEventListener("hashchange", route);
(async () => { session = (await sb.auth.getSession()).data.session; route(); })();
