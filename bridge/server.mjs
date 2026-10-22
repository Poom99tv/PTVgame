import http from 'node:http';
import {RoomLease,authorized} from './pairing.mjs';
const roomLease=new RoomLease();
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseRobloxAccount } from './parser.mjs';
import { normalizeChat } from './chat.mjs';
import { RawEvents } from './events.mjs';
import {GiftResolver} from './gift-resolver.mjs';
const directory = path.dirname(fileURLToPath(import.meta.url));
const configPath = process.env.BRIDGE_CONFIG_PATH || path.join(directory, 'config.local.json');
const config = fs.existsSync(configPath) ? JSON.parse(fs.readFileSync(configPath, 'utf8')) : {
  tiktokUsername: 'mr4rms', port: 8766, token: crypto.randomBytes(32).toString('hex')
};
if (!fs.existsSync(configPath)) fs.writeFileSync(configPath, JSON.stringify(config, null, 2));
const events = [];
const giftResolver=new GiftResolver(directory);
const defaults={version:2,likesPerBoost:1000,roseStep:.01,flashMultiplier:1.35,flashSeconds:4,roseGiftKey:'1dc1fd9347876fbd',flashGiftKey:'02a34c5909ed5891',warpAheadGiftKey:'16c55e5a27d46ff9',warpLeaderGiftKey:'12bfcfcef65f3d95'};
const GIFT_KEYS=['roseGiftKey','flashGiftKey','warpAheadGiftKey','warpLeaderGiftKey'];
// v3 = per-skill settings saved from the game (stored as-is, the game validates); v2 = older flat format
if(config.skills?.version!==3)config.skills=config.skills?.version===2?{...defaults,...config.skills}:defaults;
fs.writeFileSync(configPath,JSON.stringify(config,null,2));
const bridgeSession=crypto.randomUUID();
const skillLog=[];
function pushEvent(event){events.push({...event,seq:++sequence,receivedAt:Date.now()});if(events.length>5000)events.shift();}
const rawEvents=new RawEvents(event=>{pushEvent(event);skillLog.push({...event,at:Date.now()});if(skillLog.length>100)skillLog.shift();});
const lastAttempt = new Map();
const seenMessages = new Set();
let sequence = 0;
let status = 'Starting';
let detail = '';
let connection;
let reconnectTimer;
let stopped = false;
let generation = 0;
let comments = [];
let commentSequence = 0;
let roomId = '';
const uiSession = crypto.randomBytes(32).toString('hex');
function enqueue(text, viewer, messageId, viewerDisplayName) {
  const account = parseRobloxAccount(text);
  if (!account) return { accepted: false, reason: 'Not a Roblox username or ID' };
  if (messageId && seenMessages.has(messageId)) return { accepted: false, reason: 'Duplicate message' };
  const key = String(viewer || 'local-test');
  const now = Date.now();
  const previous = lastAttempt.get(key);
  if (previous && now - previous.time < 15000) return { accepted: false, reason: 'Please wait 15 seconds' };
  if (messageId) { seenMessages.add(messageId); if (seenMessages.size > 10000) seenMessages.delete(seenMessages.values().next().value); }
  lastAttempt.set(key, { time: now });
  const event = { seq: ++sequence, account, viewer: key, viewerDisplayName:viewerDisplayName||key, receivedAt: now };
  events.push(event);
  if (events.length > 5000) events.shift();
  console.log(JSON.stringify({ type: 'join', seq: event.seq, account }));
  return { accepted: true, seq: event.seq, account };
}
function reply(res, code, data) {
  res.writeHead(code, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
  res.end(JSON.stringify(data));
}
const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  if (req.headers.host !== `127.0.0.1:${config.port}` && req.headers.host !== `localhost:${config.port}`) return reply(res,403,{error:'Invalid host'});
  if(req.method==='GET'&&url.pathname==='/')return reply(res,200,{application:'TikTokBridge.exe',settings:'in-game gear'});
  const uiAuth = (req.headers.cookie || '').split(';').some(v=>v.trim()===`bridge_ui=${uiSession}`);
  if (!authorized(req.headers.authorization,config.token) && !uiAuth) return reply(res, 401, { error: 'Unauthorized' });
  if(req.headers['cf-ray']){
    if(!['/game/pair','/game/unpair','/game/events','/ui/skills','/health'].includes(url.pathname))return reply(res,403,{error:'Local application endpoint'});
    if(url.pathname==='/ui/skills'&&!roomLease.check(req.headers['x-race-room'],req.headers['x-race-owner']))return reply(res,409,{error:'Pair this game room first'});
  }
  if (req.method === 'POST' && req.headers.origin && ![`http://127.0.0.1:${config.port}`,`http://localhost:${config.port}`].includes(req.headers.origin)) return reply(res,403,{error:'Invalid origin'});
  if(req.method==='POST'&&url.pathname==='/game/pair'){
    try{let text='';for await(const c of req){text+=c;if(text.length>1024)throw Object.assign(Error('Too large'),{status:400});}const b=JSON.parse(text);roomLease.claim(b.room,b.owner);return reply(res,200,{ok:true,protocol:7,session:bridgeSession,sequence,status});}catch(e){return reply(res,e.status||400,{error:e.message});}
  }
  if(req.method==='POST'&&url.pathname==='/game/unpair'){
    if(!roomLease.check(req.headers['x-race-room'],req.headers['x-race-owner']))return reply(res,409,{error:'Wrong room'});roomLease.release();return reply(res,200,{ok:true});
  }
  if (req.method === 'GET' && url.pathname === '/ui/state') return reply(res,200,{status,detail,tiktokUsername:config.tiktokUsername,sequence,roomId,comments,skills:config.skills,skillLog,game:roomLease.info(),protocol:7});
  if(req.method==='POST' && url.pathname==='/ui/skills'){
    try{let text='';for await(const chunk of req){text+=chunk;if(text.length>16384)throw Error();}const body=JSON.parse(text);
      if(body&&body.version===3&&body.skills&&typeof body.skills==='object'){
        for(const s of Object.values(body.skills)){if(!s||typeof s!=='object')return reply(res,400,{error:'Invalid settings'});if(s.gift!==undefined&&!giftResolver.byKey.has(s.gift))return reply(res,400,{error:'Unknown gift'});}
        config.skills=JSON.parse(JSON.stringify(body));fs.writeFileSync(configPath,JSON.stringify(config,null,2));return reply(res,200,{ok:true,skills:config.skills});
      }
      const bounds={likesPerBoost:[1,100000],roseStep:[.001,1],flashMultiplier:[1,20],flashSeconds:[1,60]};
      const next={...defaults,...(config.skills?.version===2?config.skills:{})};for(const[key,[min,max]]of Object.entries(bounds)){const n=Number(body[key]);if(!Number.isFinite(n)||n<min||n>max)return reply(res,400,{error:'ค่าตั้งสกิลอยู่นอกช่วงที่กำหนด'});next[key]=key==='likesPerBoost'?Math.floor(n):n;}
      for(const key of GIFT_KEYS){if(body[key]!==undefined){if(!giftResolver.byKey.has(body[key]))return reply(res,400,{error:'Unknown gift'});next[key]=body[key];}}
      if(new Set(GIFT_KEYS.map(k=>next[k])).size!==GIFT_KEYS.length)return reply(res,400,{error:'แต่ละสกิลต้องเลือกของขวัญไม่ซ้ำกัน'});
      next.version=2;config.skills=next;fs.writeFileSync(configPath,JSON.stringify(config,null,2));return reply(res,200,{ok:true,skills:config.skills});
    }catch{return reply(res,400,{error:'Invalid settings'});}
  }
  if (req.method === 'POST' && url.pathname === '/ui/disconnect') {
    stopped=true; generation++; clearTimeout(reconnectTimer); reconnectTimer=null;
    connection?.removeAllListeners(); connection?.disconnect(); connection=null;status='Disconnected';detail='';roomId='';
    return reply(res,200,{ok:true});
  }
  if (req.method === 'POST' && url.pathname === '/ui/connect') {
    try {
      let body='';for await(const chunk of req){body+=chunk;if(body.length>2048)throw Error('Request too large');}
      const username=String(JSON.parse(body).username||'').trim().replace(/^@/,'');
      if(!/^[A-Za-z0-9_.]{2,24}$/.test(username))return reply(res,400,{error:'กรุณาใส่ชื่อ TikTok เช่น @mr4rms'});
      generation++;clearTimeout(reconnectTimer);reconnectTimer=null;connection?.removeAllListeners();connection?.disconnect();connection=null;
      config.tiktokUsername=username;fs.writeFileSync(configPath,JSON.stringify(config,null,2));
      stopped=false;comments=[];roomId='';lastAttempt.clear();rawEvents.reset();status='Connecting';detail='';
      void connect();return reply(res,200,{ok:true});
    }catch{return reply(res,400,{error:'Invalid request'});}
  }
  if (req.method === 'GET' && url.pathname === '/health') return reply(res, 200, { status, detail, tiktokUsername: config.tiktokUsername, sequence });
  if (req.method === 'GET' && (url.pathname === '/events' || url.pathname === '/game/events')) {
    if(url.pathname==='/game/events'&&!roomLease.check(req.headers['x-race-room'],req.headers['x-race-owner']))return reply(res,409,{error:'Pair this game room again'});
    const after = Number(url.searchParams.get('after') || 0);
    return reply(res, 200, { status,session:bridgeSession,skills:config.skills,events: events.filter(e => e.seq > after).slice(0, 100) });
  }
  if (req.method === 'POST' && url.pathname === '/test-event') {
    try{let text='';for await(const chunk of req){text+=chunk;if(text.length>2048)throw Error();}const b=JSON.parse(text);
      const viewer=String(b.viewer||'local-test');
      if(b.kind==='gift'){if(!giftResolver.byKey.has(b.giftKey))return reply(res,400,{error:'Unknown gift'});rawEvents.gift({user:{uniqueId:viewer,nickname:b.viewerDisplayName||viewer},resolvedGiftKey:b.giftKey,repeatCount:Number(b.amount)||1,repeatEnd:true,msgId:crypto.randomUUID()});}
      else if(b.kind==='like')rawEvents.like({user:{uniqueId:viewer},likeCount:Number(b.amount)||1,msgId:crypto.randomUUID()});
      else if(b.kind==='follow')rawEvents.follow({user:{uniqueId:viewer}});
      else return reply(res,400,{error:'Unknown kind'});
      return reply(res,200,{ok:true});
    }catch{return reply(res,400,{error:'Invalid test event'});}
  }
  if (req.method === 'POST' && url.pathname === '/test') {
    let text = '';
    try {
      for await (const chunk of req) { text += chunk; if (text.length > 2048) throw new Error('Request too large'); }
      const body = JSON.parse(text);
      return reply(res, 200, enqueue(body.comment, body.viewer || `test-${crypto.randomUUID()}`, body.messageId));
    } catch { return reply(res, 400, { error: 'Invalid test request' }); }
  }
  reply(res, 404, { error: 'Not found' });
});
async function connect() {
  const attempt=++generation;
  if (stopped || process.env.BRIDGE_TEST_ONLY === '1') { status = 'TestOnly'; return; }
  try {
    const { TikTokLiveConnection, WebcastEvent, ControlEvent } = await import('tiktok-live-connector');
    status = 'Connecting'; detail = '';
    connection = new TikTokLiveConnection(config.tiktokUsername, { processInitialData: false });
    connection.on(WebcastEvent.CHAT, data => {
      if(attempt!==generation)return;
      const chat=normalizeChat(data);
      const result=enqueue(chat.comment, chat.viewer, chat.messageId, chat.viewerDisplayName);
      comments.push({id:++commentSequence,viewer:chat.viewer,comment:chat.comment,at:Date.now(),accepted:result.accepted,account:result.account});
      if(comments.length>200)comments.shift();
    });
    connection.on(WebcastEvent.GIFT,async data=>{if(attempt!==generation)return;const match=await giftResolver.resolve(data);if(attempt===generation&&match)rawEvents.gift({...data,resolvedGiftKey:match.key,giftMatchMethod:match.method});});
    connection.on(WebcastEvent.LIKE,data=>{if(attempt===generation)rawEvents.like(data);});
    connection.on(WebcastEvent.FOLLOW||'follow',data=>{if(attempt===generation)rawEvents.follow(data);});
    if (ControlEvent?.DISCONNECTED) connection.on(ControlEvent.DISCONNECTED, () => {
      if(attempt!==generation)return;
      status = 'Disconnected'; scheduleReconnect();
    });
    if (ControlEvent?.ERROR) connection.on(ControlEvent.ERROR, err => { if(attempt===generation)detail = String(err?.message || err?.info || err).slice(0, 300); });
    const state = await connection.connect();
    if(attempt!==generation)return;
    roomId=String(state.roomId);
    status = 'Connected'; detail = '';
    console.log(JSON.stringify({ type: 'tiktok-connected', username: config.tiktokUsername, roomId: String(state.roomId) }));
  } catch (err) {
    if(attempt!==generation)return;
    status = 'ConnectionError'; detail = String(err?.message || err).slice(0, 300);
    console.log(JSON.stringify({ type: 'tiktok-error', message: detail }));
    scheduleReconnect();
  }
}
function scheduleReconnect() {
  if (!reconnectTimer && !stopped) reconnectTimer = setTimeout(() => { reconnectTimer = null; connect(); }, 30000);
}
server.listen(config.port, '127.0.0.1', () => {
  console.log(JSON.stringify({ type: 'bridge-ready', port: config.port, username: config.tiktokUsername }));
  if (process.argv.includes('--demo')) enqueue('@POOM99TV', 'setup-test', 'setup-test');
  if(process.argv.includes('--manual'))status='Disconnected';else connect();
});
server.on('error', err => {
  console.error(JSON.stringify({ type: 'bridge-server-error', message: err.message }));
});
process.on('SIGINT', () => { stopped = true; clearTimeout(reconnectTimer); connection?.disconnect(); server.close(); });
