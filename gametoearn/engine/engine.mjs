// PTV Game To Earn engine — runs hidden next to the PTVgame Launcher.
// TikTok LIVE connection (tiktok-live-connector), gift recognition by picture (same image matching
// as TikTokBridge, so a gift is recognised even when TikTok sends a different name), chat TTS
// (Microsoft Edge neural voices) and OBS browser-source overlays.  The Launcher owns the presets and
// the key presses; it polls /events and pushes overlay settings.
//
// env: GTE_PORT (8767), GTE_TOKEN (required), GTE_GIFTS (folder with <gift name>.png), GTE_DATA (writable folder)
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { GiftMatcher, normName } from './gifts.mjs';
import { OVERLAY_HTML } from './overlay.mjs';

const dir = path.dirname(fileURLToPath(import.meta.url));
const PORT = Number(process.env.GTE_PORT || 8767);
const WEB_ORIGINS = new Set(['https://poom99tv.github.io', 'https://ptvgame.com', 'https://www.ptvgame.com', ...String(process.env.GTE_WEB_ORIGINS || '').split(',').filter(Boolean)]);
const TOKEN = String(process.env.GTE_TOKEN || '');
const GIFTS = process.env.GTE_GIFTS || path.join(dir, 'gifts');
const DATA = process.env.GTE_DATA || dir;
if (TOKEN.length < 16) { console.error('GTE_TOKEN missing'); process.exit(2); }
fs.mkdirSync(DATA, { recursive: true });

const matcher = new GiftMatcher(path.join(dir, 'gift-catalog.json'), path.join(DATA, 'gift-identities.json'));
let status = 'Disconnected', detail = '', username = '', roomId = '';
let host = { name: '', avatar: '', title: '', viewers: 0 };
let connection = null, generation = 0, reconnectTimer = null, stopped = true;
let seq = 0;
const events = [];
const overlay = { likers: new Map(), gifters: new Map(), totals: { likes: 0, diamonds: 0, gifts: 0 }, drops: [], dropSeq: 0, jarClear: 0,
  config: { win: { count: 0, max: 10, color: '#ffffff', size: 64, label: 'WIN' }, jar: { scale: 100 }, bg: 'transparent' } };

function push(e) {
  e.seq = ++seq; e.at = Date.now(); events.push(e);
  if (events.length > 3000) events.shift();
  return e;
}
const firstUrl = (o, depth = 0) => {
  if (!o || depth > 4) return '';
  if (typeof o === 'string') return /^https:\/\//.test(o) ? o : '';
  if (Array.isArray(o)) { for (const v of o) { const u = firstUrl(v, depth + 1); if (u) return u; } return ''; }
  if (typeof o === 'object') for (const k of ['urlList', 'url_list', 'url', 'urls', 'uri']) { const u = firstUrl(o[k], depth + 1); if (u) return u; }
  return '';
};
function who(data) {
  const u = data.user || {};
  return {
    viewer: String(u.uniqueId || u.displayId || data.uniqueId || u.userId || 'unknown'),
    name: String(u.nickname || data.nickname || u.uniqueId || data.uniqueId || ''),
    avatar: firstUrl(u.profilePicture) || firstUrl(u.avatarThumb) || firstUrl(u.avatarMedium) || '',
    msgId: String(data.msgId || data.common?.msgId || data.event?.msgId || '')
  };
}
const seen = new Set();
function once(id) { if (!id) return true; if (seen.has(id)) return false; seen.add(id); if (seen.size > 20000) seen.delete(seen.values().next().value); return true; }
const streaks = new Map();

function board(map, viewer, name, avatar, add) {
  const row = map.get(viewer) || { viewer, name, avatar, score: 0 };
  row.name = name || row.name; row.avatar = avatar || row.avatar; row.score += add; map.set(viewer, row);
  if (map.size > 5000) map.delete(map.keys().next().value);
}

async function onGift(data, attempt) {
  const w = who(data);
  if (!once(w.msgId && w.msgId + ':' + data.repeatCount + ':' + data.repeatEnd)) return;
  const count = Math.max(1, Math.floor(Number(data.repeatCount) || 1));
  const giftId = String(data.giftId || data.gift?.id || data.giftDetails?.id || '');
  const rawGroup = String(data.groupId || data.orderId || ''); const group = rawGroup === '0' ? '' : rawGroup;
  const type = Number(data.giftDetails?.giftType ?? data.gift?.type ?? data.giftType ?? data.extendedGiftInfo?.type);
  const streak = type === 1 || !!group;
  let amount = count;
  if (streak && group) { const k = w.viewer + ':' + giftId + ':' + group; const prev = streaks.get(k) || 0; amount = Math.max(0, count - prev); streaks.set(k, Math.max(count, prev)); if (streaks.size > 10000) streaks.delete(streaks.keys().next().value); }
  else if (streak && !data.repeatEnd) return;
  if (amount <= 0) return;
  const rawName = String(data.giftDetails?.giftName || data.giftName || data.gift?.name || data.extendedGiftInfo?.name || '').trim();
  const urls = [data.giftDetails?.giftImage, data.gift?.image, data.extendedGiftInfo?.image, data.giftImage, data.giftPictureUrl, data.giftDetails?.icon]
    .map(x => firstUrl(x)).filter(Boolean);
  const match = await matcher.resolve(giftId, urls, rawName);
  if (attempt !== generation) return;
  const gift = match ? matcher.byKey.get(match.key) : null;
  const per = Number(data.gift?.diamondCount || data.giftDetails?.diamondCount || data.diamondCount || data.extendedGiftInfo?.diamond_count || 0) || (gift?.price || 1);
  const e = push({ kind: 'gift', ...w, amount, giftKey: gift?.key || '', giftName: gift?.name || rawName || 'Gift', rawName, file: gift?.file || '', method: match?.method || 'none', diamonds: per * amount });
  board(overlay.gifters, w.viewer, w.name, w.avatar, per * amount);
  overlay.totals.diamonds += per * amount; overlay.totals.gifts += amount;
  if (e.file) { overlay.drops.push({ id: ++overlay.dropSeq, file: e.file, count: Math.min(amount, 50) }); if (overlay.drops.length > 60) overlay.drops.shift(); }
}

async function connect() {
  const attempt = ++generation;
  clearTimeout(reconnectTimer); reconnectTimer = null;
  try { connection?.removeAllListeners(); connection?.disconnect(); } catch {}
  connection = null;
  if (stopped) return;
  if (process.env.GTE_OFFLINE === '1') { status = 'Connected'; detail = 'offline test'; push({ kind: 'status', status }); return; }
  status = 'Connecting'; detail = ''; push({ kind: 'status', status });
  try {
    const { TikTokLiveConnection, WebcastEvent, ControlEvent } = await import('tiktok-live-connector');
    const c = new TikTokLiveConnection(username, { processInitialData: false });
    connection = c;
    c.on(WebcastEvent.GIFT, d => { if (attempt === generation) onGift(d, attempt).catch(() => {}); });
    c.on(WebcastEvent.LIKE, d => {
      if (attempt !== generation) return; const w = who(d); if (!once(w.msgId)) return;
      const n = Math.max(0, Math.floor(Number(d.likeCount ?? d.count) || 0)); if (!n) return;
      board(overlay.likers, w.viewer, w.name, w.avatar, n); overlay.totals.likes += n;
      push({ kind: 'like', ...w, amount: n });
    });
    c.on(WebcastEvent.FOLLOW || 'follow', d => { if (attempt === generation) push({ kind: 'follow', ...who(d) }); });
    c.on(WebcastEvent.CHAT, d => {
      if (attempt !== generation) return; const w = who(d); if (!once(w.msgId)) return;
      const comment = String(d.comment ?? d.content ?? '').trim(); if (comment) push({ kind: 'chat', ...w, comment: comment.slice(0, 300) });
    });
    if (ControlEvent?.DISCONNECTED) c.on(ControlEvent.DISCONNECTED, () => { if (attempt !== generation) return; status = 'Disconnected'; push({ kind: 'status', status }); schedule(); });
    if (ControlEvent?.ERROR) c.on(ControlEvent.ERROR, err => { if (attempt === generation) detail = String(err?.message || err?.info || err).slice(0, 300); });
    if (WebcastEvent.STREAM_END) c.on(WebcastEvent.STREAM_END, () => { if (attempt !== generation) return; status = 'Ended'; push({ kind: 'status', status, detail: 'LIVE ended' }); schedule(); });
    if (WebcastEvent.ROOM_USER) c.on(WebcastEvent.ROOM_USER, d => { if (attempt !== generation) return; const v = Number(d.viewerCount ?? d.total ?? d.totalUser ?? 0); if (Number.isFinite(v) && v >= 0) host.viewers = Math.floor(v); });
    const state = await c.connect();
    if (attempt !== generation) return;
    try {
      const info = state?.roomInfo?.data || state?.roomInfo || c.roomInfo?.data || {}; const owner = info.owner || {};
      host = { name: String(owner.nickname || owner.display_id || username).slice(0, 60), avatar: firstUrl(owner.avatar_thumb) || firstUrl(owner.avatar_medium) || firstUrl(owner.avatarThumb) || '',
        title: String(info.title || '').slice(0, 120), viewers: Number(info.user_count || info.stats?.total_user || 0) || 0 };
    } catch { host = { name: username, avatar: '', title: '', viewers: 0 }; }
    roomId = String(state?.roomId || ''); status = 'Connected'; detail = ''; push({ kind: 'status', status, roomId });
  } catch (err) {
    if (attempt !== generation) return;
    status = 'Error'; detail = String(err?.message || err).slice(0, 300); push({ kind: 'status', status, detail }); schedule();
  }
}
function schedule() { if (!stopped && !reconnectTimer) reconnectTimer = setTimeout(() => { reconnectTimer = null; connect(); }, 20000); }

let ttsLib = null;
async function tts(text, voice) {
  if (!ttsLib) ttsLib = await import('msedge-tts');
  const t = new ttsLib.MsEdgeTTS();
  await t.setMetadata(voice, ttsLib.OUTPUT_FORMAT.AUDIO_24KHZ_48KBITRATE_MONO_MP3);
  const { audioStream } = t.toStream(text);
  const chunks = []; let size = 0;
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('tts timeout')), 15000);
    audioStream.on('data', c => { size += c.length; if (size < 4 * 1024 * 1024) chunks.push(c); });
    audioStream.on('end', () => { clearTimeout(timer); resolve(); });
    audioStream.on('close', () => { clearTimeout(timer); resolve(); });
    audioStream.on('error', e => { clearTimeout(timer); reject(e); });
  });
  try { t.close?.(); } catch {}
  return Buffer.concat(chunks);
}

function top(map) { return [...map.values()].sort((a, b) => b.score - a.score).slice(0, 5); }
function overlayState(after) {
  return { status, likers: top(overlay.likers), gifters: top(overlay.gifters), totals: overlay.totals, config: overlay.config,
    jarClear: overlay.jarClear, drops: overlay.drops.filter(d => d.id > after) };
}
const reply = (res, code, data, type = 'application/json') => {
  res.writeHead(code, { 'Content-Type': type, 'Cache-Control': 'no-store' });
  res.end(type === 'application/json' ? JSON.stringify(data) : data);
};
async function body(req, limit = 65536) { let t = ''; for await (const c of req) { t += c; if (t.length > limit) throw Object.assign(Error('too large'), { status: 413 }); } return t ? JSON.parse(t) : {}; }
const authorized = (req, url) => {
  const h = String(req.headers.authorization || ''); const q = url.searchParams.get('t') || '';
  const given = h.startsWith('Bearer ') ? h.slice(7) : q;
  return given.length === TOKEN.length && crypto.timingSafeEqual(Buffer.from(given), Buffer.from(TOKEN));
};

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1');
  if (![`127.0.0.1:${PORT}`, `localhost:${PORT}`].includes(req.headers.host)) return reply(res, 403, { error: 'host' });
  // the PTVgame website may control this engine from the same computer (token still required)
  const origin = String(req.headers.origin || '');
  if (WEB_ORIGINS.has(origin)) { res.setHeader('Access-Control-Allow-Origin', origin); res.setHeader('Vary', 'Origin'); }
  if (req.method === 'OPTIONS') {
    if (!WEB_ORIGINS.has(origin)) return reply(res, 403, { error: 'origin' });
    res.writeHead(204, { 'Access-Control-Allow-Methods': 'GET, POST', 'Access-Control-Allow-Headers': 'authorization, content-type',
      'Access-Control-Allow-Private-Network': 'true', 'Access-Control-Max-Age': '600' }); return res.end();
  }
  if (origin && !WEB_ORIGINS.has(origin) && origin !== `http://127.0.0.1:${PORT}` && req.method !== 'GET') return reply(res, 403, { error: 'origin' });
  if (!authorized(req, url)) return reply(res, 401, { error: 'unauthorized' });
  try {
    const p = url.pathname;
    if (req.method === 'GET' && p === '/health') return reply(res, 200, { ok: true, version: 1, status, gifts: matcher.catalog.length });
    if (req.method === 'GET' && p === '/state') return reply(res, 200, { status, detail, username, roomId, seq, totals: overlay.totals, host });
    if (req.method === 'GET' && p === '/events') { const after = Number(url.searchParams.get('after') || 0); return reply(res, 200, { status, detail, seq, events: events.filter(e => e.seq > after).slice(0, 300) }); }
    if (req.method === 'GET' && p === '/gifts') return reply(res, 200, matcher.catalog.map(g => ({ key: g.key, name: g.name, file: g.file, price: g.price || 0 })));
    if (req.method === 'POST' && p === '/connect') {
      const b = await body(req); const u = String(b.username || '').trim().replace(/^@/, '');
      if (!/^[A-Za-z0-9_.]{2,24}$/.test(u)) return reply(res, 400, { error: 'invalid username' });
      username = u; stopped = false; streaks.clear(); host = { name: u, avatar: '', title: '', viewers: 0 }; connect(); return reply(res, 200, { ok: true });
    }
    if (req.method === 'POST' && p === '/disconnect') { stopped = true; generation++; clearTimeout(reconnectTimer); reconnectTimer = null; try { connection?.removeAllListeners(); connection?.disconnect(); } catch {} connection = null; status = 'Disconnected'; detail = ''; push({ kind: 'status', status }); return reply(res, 200, { ok: true }); }
    if (req.method === 'POST' && p === '/test') {
      const b = await body(req); const viewer = String(b.viewer || 'tester').slice(0, 40); const nick = String(b.name || 'Test').slice(0, 40); const demo = !!b.demo; const n = Math.max(1, Math.min(100000, Number(b.amount) || 1));
      if (b.kind === 'gift') {
        const g = matcher.byKey.get(b.giftKey) || matcher.byName(b.giftName || 'Rose');
        const per = g?.price || 1;
        push({ kind: 'gift', viewer, name: nick, avatar: '', amount: n, giftKey: g?.key || '', giftName: g?.name || String(b.giftName || 'Gift'), rawName: String(b.giftName || ''), file: g?.file || '', method: 'test', diamonds: per * n, demo });
        board(overlay.gifters, viewer, nick, '', per * n); overlay.totals.diamonds += per * n; overlay.totals.gifts += n;
        if (g?.file) overlay.drops.push({ id: ++overlay.dropSeq, file: g.file, count: Math.min(n, 50) });
      } else if (b.kind === 'like') { board(overlay.likers, viewer, nick, '', n); overlay.totals.likes += n; push({ kind: 'like', viewer, name: nick, avatar: '', amount: n, demo }); }
      else if (b.kind === 'follow') push({ kind: 'follow', viewer, name: nick, avatar: '', demo });
      else if (b.kind === 'chat') push({ kind: 'chat', viewer, name: nick, avatar: '', comment: String(b.comment || 'สวัสดี').slice(0, 300), demo });
      else return reply(res, 400, { error: 'kind' });
      return reply(res, 200, { ok: true });
    }
    if (req.method === 'POST' && p === '/test-raw' && process.env.GTE_OFFLINE === '1') { const b = await body(req); await onGift(b, generation); return reply(res, 200, { ok: true }); }
    if (req.method === 'GET' && p === '/tts') {
      const text = String(url.searchParams.get('text') || '').slice(0, 200); const voice = String(url.searchParams.get('voice') || 'th-TH-PremwadeeNeural');
      if (!text.trim() || !/^[a-z]{2}-[A-Z]{2}-[A-Za-z]+Neural$/.test(voice)) return reply(res, 400, { error: 'tts' });
      const audio = await tts(text, voice); return reply(res, 200, audio, 'audio/mpeg');
    }
    if (req.method === 'POST' && p === '/overlay/config') { const b = await body(req); if (b.win) Object.assign(overlay.config.win, b.win); if (b.jar) Object.assign(overlay.config.jar, b.jar); if (b.bg) overlay.config.bg = String(b.bg); return reply(res, 200, { ok: true }); }
    if (req.method === 'POST' && p === '/overlay/reset') {
      const b = await body(req); const w = String(b.what || 'all');
      if (w === 'likers' || w === 'all') { overlay.likers.clear(); overlay.totals.likes = 0; }
      if (w === 'gifters' || w === 'all') { overlay.gifters.clear(); overlay.totals.diamonds = 0; overlay.totals.gifts = 0; }
      if (w === 'jar' || w === 'all') { overlay.jarClear++; overlay.drops = []; }
      return reply(res, 200, { ok: true });
    }
    if (req.method === 'POST' && p === '/overlay/drop') { const b = await body(req); const g = matcher.byKey.get(b.giftKey) || matcher.byName(b.giftName || 'Rose'); if (g) overlay.drops.push({ id: ++overlay.dropSeq, file: g.file, count: Math.max(1, Math.min(50, Number(b.count) || 1)) }); return reply(res, 200, { ok: true }); }
    if (req.method === 'GET' && p === '/overlay/state') return reply(res, 200, overlayState(Number(url.searchParams.get('after') || 0)));
    const o = /^\/overlay\/(likers|gifters|win|jar|stats)$/.exec(p);
    if (req.method === 'GET' && o) return reply(res, 200, OVERLAY_HTML(o[1]), 'text/html; charset=utf-8');
    const img = /^\/gift-image\/([^/]+)$/.exec(p);
    if (req.method === 'GET' && img) {
      const name = decodeURIComponent(img[1]); if (name.includes('..') || name.includes('/') || name.includes('\\') || !name.toLowerCase().endsWith('.png')) return reply(res, 400, { error: 'name' });
      const file = path.join(GIFTS, name); if (!fs.existsSync(file)) return reply(res, 404, { error: 'missing' });
      res.writeHead(200, { 'Content-Type': 'image/png', 'Cache-Control': 'max-age=86400' }); return fs.createReadStream(file).pipe(res);
    }
    reply(res, 404, { error: 'not found' });
  } catch (e) { reply(res, e.status || 500, { error: String(e.message || e).slice(0, 200) }); }
});
server.listen(PORT, '127.0.0.1', () => console.log(JSON.stringify({ type: 'ready', port: PORT, gifts: matcher.catalog.length })));
server.on('error', e => { console.error(JSON.stringify({ type: 'server-error', message: e.message })); process.exit(3); });
// exit with the Launcher
const parent = Number(process.env.GTE_PARENT || 0);
if (parent) setInterval(() => { try { process.kill(parent, 0); } catch { process.exit(0); } }, 2000).unref();
export { normName };
