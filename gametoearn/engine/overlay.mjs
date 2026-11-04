import fs from 'node:fs';
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

// gift jar: same jar shape and physics as the PTV Game To Earn jar (jar-physics.mjs)
const PHYS = fs.readFileSync(new URL('./jar-physics.mjs', import.meta.url), 'utf8').replace(/^export /gm, '');
const JARJS = PHYS + `
const cv=document.createElement('canvas');document.getElementById('app').appendChild(cv);const g=cv.getContext('2d');
let W=0,H=0;function size(){W=cv.width=innerWidth;H=cv.height=innerHeight;}addEventListener('resize',size);size();
const sim=new JarSim(),imgs={},queue=[];
function img(f){if(!imgs[f]){const i=new Image();i.src='/gift-image/'+encodeURIComponent(f)+'?t='+encodeURIComponent(T);imgs[f]=i;}return imgs[f];}
function render(s){if(clearSeq!==s.jarClear){if(clearSeq>=0){sim.clear();queue.length=0;}clearSeq=s.jarClear;}for(const d of s.drops||[])for(let i=0;i<d.count;i++)queue.push(d.file);}
function rr(x,y,w,h,r){g.beginPath();g.moveTo(x+r,y);g.arcTo(x+w,y,x+w,y+h,r);g.arcTo(x+w,y+h,x,y+h,r);g.arcTo(x,y+h,x,y,r);g.arcTo(x,y,x+w,y,r);g.closePath();}
function drawGift(q){const i=img(q.img);g.save();g.translate(q.x,q.y);g.rotate(q.rot*Math.PI/180);const s=JAR.size;if(i.complete&&i.naturalWidth)g.drawImage(i,-s/2,-s/2,s,s);else{g.fillStyle='#ff4f9a';g.beginPath();g.arc(0,0,s*.4,0,7);g.fill();}g.restore();}
function drawJar(){const J=JAR,cx=J.cx,jt=J.jt,jb=J.jb,jx1=J.jx1,jx2=J.jx2,nx1=J.nx1,nx2=J.nx2,nh=J.neckH;
 g.beginPath();g.moveTo(nx1,jt+nh);g.quadraticCurveTo(jx1+3,jt+nh+10,jx1+3,jt+nh+40);g.lineTo(jx1+3,jb-35);g.quadraticCurveTo(jx1+3,jb,jx1+35,jb);g.lineTo(jx2-35,jb);g.quadraticCurveTo(jx2-3,jb,jx2-3,jb-35);g.lineTo(jx2-3,jt+nh+40);g.quadraticCurveTo(jx2-3,jt+nh+10,nx2,jt+nh);g.closePath();
 const gr=g.createLinearGradient(jx1,0,jx2,0);[[0,.47],[.1,.2],[.3,.06],[.5,.02],[.7,.06],[.9,.2],[1,.47]].forEach(([o,a])=>gr.addColorStop(o,'rgba(230,245,255,'+a+')'));
 g.fillStyle=gr;g.fill();g.lineWidth=2.5;g.strokeStyle='rgba(215,235,255,.76)';g.stroke();
 g.beginPath();g.moveTo(jx1+14,jb-18);g.quadraticCurveTo(cx,jb-10,jx2-14,jb-18);g.lineWidth=1.5;g.strokeStyle='rgba(235,245,255,.5)';g.stroke();
 const lg=g.createLinearGradient(0,jt,0,jt+nh);lg.addColorStop(0,'rgba(195,200,205,.55)');lg.addColorStop(.4,'rgba(245,248,250,.63)');lg.addColorStop(1,'rgba(165,170,175,.55)');
 rr(nx1-3,jt+4,nx2-nx1+6,nh-4,5);g.fillStyle=lg;g.fill();g.lineWidth=2;g.strokeStyle='rgba(145,150,155,.6)';g.stroke();
 g.lineWidth=1.5;g.strokeStyle='rgba(125,130,135,.47)';for(let i=1;i<3;i++){const y=jt+4+i*((nh-4)/3|0);g.beginPath();g.moveTo(nx1-1,y);g.lineTo(nx2+1,y);g.stroke();}
 g.lineCap='round';g.beginPath();g.moveTo(jx1+18,jt+nh+25);g.quadraticCurveTo(jx1+22,jt+nh+100,jx1+19,jb-45);g.lineWidth=6;g.strokeStyle='rgba(255,255,255,.55)';g.stroke();
 g.beginPath();g.moveTo(jx2-18,jt+nh+30);g.quadraticCurveTo(jx2-20,jt+nh+80,jx2-19,jt+nh+130);g.lineWidth=3;g.strokeStyle='rgba(255,255,255,.35)';g.stroke();}
let tick=0;function frame(){tick++;if(queue.length&&tick%5===0)sim.add(queue.shift());sim.step();
 const J=JAR,sc=Math.min(W/J.W,H/J.H)*Math.max(.4,(cfg?cfg.jar.scale:100)/100);g.setTransform(1,0,0,1,0,0);g.clearRect(0,0,W,H);
 g.setTransform(sc,0,0,sc,(W-J.W*sc)/2,H-J.H*sc);
 g.save();g.beginPath();g.moveTo(-500,-2000);g.lineTo(J.W+500,-2000);g.lineTo(J.W+500,J.jt+32);g.lineTo(J.jx2-14,J.jt+45);g.lineTo(J.jx2-14,J.jb-24);g.quadraticCurveTo(J.jx2-14,J.jb-15,J.jx2-35,J.jb-15);g.lineTo(J.jx1+35,J.jb-15);g.quadraticCurveTo(J.jx1+14,J.jb-15,J.jx1+14,J.jb-24);g.lineTo(J.jx1+14,J.jt+45);g.lineTo(-500,J.jt+32);g.closePath();g.clip();
 for(const q of sim.p)if(!q.overflow)drawGift(q);g.restore();drawJar();for(const q of sim.p)if(q.overflow)drawGift(q);
 g.setTransform(1,0,0,1,0,0);g.fillStyle='#fff';g.font='900 18px Segoe UI';g.textAlign='center';g.shadowColor='#000';g.shadowBlur=6;g.fillText('🎁 '+sim.count,W/2,22);g.shadowBlur=0;
 requestAnimationFrame(frame);}requestAnimationFrame(frame);
`;

export function OVERLAY_HTML(kind) {
  if (kind === 'likers') return BASE + BOARD('TOP LIKERS', '❤️', '❤', 'likers') + END;
  if (kind === 'gifters') return BASE + BOARD('TOP GIFTERS', '🎁', '💎', 'gifters') + END;
  if (kind === 'win') return BASE + WIN + END;
  if (kind === 'stats') return BASE + STATS + END;
  return BASE + JARJS + END;
}
