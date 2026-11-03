// OBS browser-source overlays: /overlay/likers, /overlay/gifters, /overlay/win, /overlay/jar, /overlay/stats
const BASE = `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<style>
html,body{margin:0;height:100%;overflow:hidden;font-family:"Segoe UI","Noto Sans Thai",sans-serif;color:#fff}
.board{padding:14px 16px;width:340px}
.title{font-size:22px;font-weight:900;text-shadow:0 2px 6px #000a;margin-bottom:10px}
.row{display:flex;align-items:center;gap:10px;margin:8px 0;padding:8px 10px;border-radius:14px;background:#0008;backdrop-filter:blur(2px)}
.rank{width:26px;height:26px;border-radius:13px;display:flex;align-items:center;justify-content:center;font-weight:900;background:#ffffff22}
.r1 .rank{background:#FFC94A;color:#2a1c00}.r2 .rank{background:#C9D6E3;color:#102030}.r3 .rank{background:#E39B5B;color:#2a1400}
.av{width:42px;height:42px;border-radius:21px;background:#214766 center/cover;flex:none;border:2px solid #fff3}
.nm{flex:1;font-weight:800;font-size:16px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;text-shadow:0 1px 3px #000}
.sc{font-weight:900;font-size:17px}
.win{display:flex;align-items:center;justify-content:center;height:100%;font-weight:900;letter-spacing:2px;text-shadow:0 0 12px #000,0 3px 0 #0008;-webkit-text-stroke:2px #0009}
.stats{display:flex;gap:12px;padding:12px}.stat{background:#000a;border-radius:14px;padding:10px 16px;font-size:22px;font-weight:900}
canvas{display:block}
</style></head><body><div id="app"></div><script>
const q=new URLSearchParams(location.search);const T=q.get('t')||'';let after=0,cfg=null,clearSeq=-1;
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
async function poll(){try{const r=await fetch('/overlay/state?t='+encodeURIComponent(T)+'&after='+after,{cache:'no-store'});const s=await r.json();cfg=s.config;
document.body.style.background=q.get('bg')||cfg.bg||'transparent';render(s);for(const d of s.drops||[])after=Math.max(after,d.id);}catch(e){}setTimeout(poll,500);}
`;
const END = `poll();</script></body></html>`;

const BOARD = (title, icon, unit, field) => `
function render(s){const list=s.${field}||[];document.getElementById('app').innerHTML='<div class="board"><div class="title">${icon} ${title}</div>'+
list.map((r,i)=>'<div class="row r'+(i+1)+'"><div class="rank">'+(i+1)+'</div><div class="av" style="background-image:url(\\''+esc(r.avatar)+'\\')"></div><div class="nm">'+esc(r.name||r.viewer)+'</div><div class="sc">${unit} '+r.score.toLocaleString()+'</div></div>').join('')+'</div>';}
`;

const WIN = `
function render(s){const w=s.config.win;document.getElementById('app').innerHTML='<div class="win" style="color:'+esc(w.color)+';font-size:'+(Number(w.size)||64)+'px">'+esc(w.label||'WIN')+' '+w.count+(w.max>0?' / '+w.max:'')+'</div>';}
`;

const STATS = `
function render(s){const t=s.totals;document.getElementById('app').innerHTML='<div class="stats"><div class="stat">❤️ '+t.likes.toLocaleString()+'</div><div class="stat">🎁 '+t.gifts.toLocaleString()+'</div><div class="stat">💎 '+t.diamonds.toLocaleString()+'</div><div class="stat">🏆 '+s.config.win.count+'</div></div>';}
`;

// simple circle physics in a glass jar
const JAR = `
const cv=document.createElement('canvas');document.getElementById('app').appendChild(cv);const g=cv.getContext('2d');
let W=0,H=0;function size(){W=cv.width=innerWidth;H=cv.height=innerHeight;}addEventListener('resize',size);size();
const imgs={},balls=[],queue=[];function img(f){if(!imgs[f]){const i=new Image();i.src='/gift-image/'+encodeURIComponent(f)+'?t='+encodeURIComponent(T);imgs[f]=i;}return imgs[f];}
function jar(){const sc=(cfg?cfg.jar.scale:100)/100;const w=Math.min(W*0.8,260*sc),h=Math.min(H*0.85,330*sc);return{x:(W-w)/2,y:H-h-8,w,h,r:Math.max(14,22*sc)};}
function render(s){if(clearSeq!==s.jarClear){if(clearSeq>=0)balls.length=0;clearSeq=s.jarClear;}for(const d of s.drops||[])for(let i=0;i<d.count;i++)queue.push(d.file);}
let tick=0;function step(){const J=jar();tick++;if(queue.length&&tick%4===0&&balls.length<400){const f=queue.shift();balls.push({x:J.x+J.r+Math.random()*(J.w-2*J.r),y:J.y-40,vx:(Math.random()-.5)*2,vy:0,r:J.r,f});}
if(balls.length>=400&&queue.length)balls.shift();
for(const b of balls){b.vy+=0.45;b.x+=b.vx;b.y+=b.vy;b.vx*=0.985;
 if(b.y+b.r>J.y+J.h){b.y=J.y+J.h-b.r;b.vy*=-0.2;b.vx*=0.85;}
 if(b.y>J.y){if(b.x-b.r<J.x){b.x=J.x+b.r;b.vx*=-0.3;}if(b.x+b.r>J.x+J.w){b.x=J.x+J.w-b.r;b.vx*=-0.3;}}}
for(let k=0;k<3;k++)for(let i=0;i<balls.length;i++)for(let j=i+1;j<balls.length;j++){const a=balls[i],c=balls[j];const dx=c.x-a.x,dy=c.y-a.y,d=Math.hypot(dx,dy),m=a.r+c.r;if(d>0&&d<m){const o=(m-d)/2,nx=dx/d,ny=dy/d;a.x-=nx*o;a.y-=ny*o;c.x+=nx*o;c.y+=ny*o;const rv=(c.vx-a.vx)*nx+(c.vy-a.vy)*ny;if(rv<0){const imp=rv*0.6;a.vx+=imp*nx;a.vy+=imp*ny;c.vx-=imp*nx;c.vy-=imp*ny;}}}
g.clearRect(0,0,W,H);
g.save();g.lineWidth=6;g.strokeStyle='rgba(220,240,255,.75)';g.fillStyle='rgba(200,230,255,.10)';g.beginPath();g.moveTo(J.x-6,J.y-10);g.lineTo(J.x,J.y+J.h-24);g.quadraticCurveTo(J.x,J.y+J.h,J.x+24,J.y+J.h);g.lineTo(J.x+J.w-24,J.y+J.h);g.quadraticCurveTo(J.x+J.w,J.y+J.h,J.x+J.w,J.y+J.h-24);g.lineTo(J.x+J.w+6,J.y-10);g.fill();g.stroke();g.restore();
for(const b of balls){const i=img(b.f);if(i.complete&&i.naturalWidth)g.drawImage(i,b.x-b.r,b.y-b.r,b.r*2,b.r*2);else{g.fillStyle='#ff4f9a';g.beginPath();g.arc(b.x,b.y,b.r*.8,0,7);g.fill();}}
g.fillStyle='#fff';g.font='900 18px Segoe UI';g.textAlign='center';g.shadowColor='#000';g.shadowBlur=6;g.fillText('🎁 '+balls.length,W/2,J.y-16);g.shadowBlur=0;
requestAnimationFrame(step);}requestAnimationFrame(step);
`;

export function OVERLAY_HTML(kind) {
  if (kind === 'likers') return BASE + BOARD('TOP LIKERS', '❤️', '❤', 'likers') + END;
  if (kind === 'gifters') return BASE + BOARD('TOP GIFTERS', '🎁', '💎', 'gifters') + END;
  if (kind === 'win') return BASE + WIN + END;
  if (kind === 'stats') return BASE + STATS + END;
  return BASE + JAR + END;
}
