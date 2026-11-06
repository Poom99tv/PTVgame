import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import net from 'node:net';
import {once} from 'node:events';
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {RUNNER_PLACE_ID} from './runner.mjs';

test('engine HTTP: route isolation, malformed auth, gift combos and game pairing', async t => {
  const dir=path.dirname(fileURLToPath(import.meta.url));
  const probe=net.createServer();probe.listen(0,'127.0.0.1');await once(probe,'listening');
  const port=probe.address().port;await new Promise(r=>probe.close(r));
  const temp=await fs.mkdtemp(path.join(dir,'.test-'));
  const token='a'.repeat(48);let log='';
  const child=spawn(process.execPath,[path.join(dir,'engine.mjs')],{env:{...process.env,GTE_PORT:String(port),GTE_TOKEN:token,GTE_OFFLINE:'1',GTE_DATA:temp},stdio:['ignore','pipe','pipe']});
  child.stdout.on('data',b=>log+=b);child.stderr.on('data',b=>log+=b);
  t.after(async()=>{if(child.exitCode===null){child.kill();await once(child,'exit');}await fs.rm(temp,{recursive:true,force:true});});
  for(let n=0;n<200&&!log.includes('ready');n++){assert.equal(child.exitCode,null,log);await new Promise(r=>setTimeout(r,25));}
  assert.match(log,/ready/);
  const call=async(route,body,headers={})=>{
    const r=await fetch(`http://127.0.0.1:${port}${route}`,{method:body?'POST':'GET',headers:{Authorization:'Bearer '+token,'Content-Type':'application/json',...headers},body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(3000)});
    return {code:r.status,data:await r.json()};
  };
  assert.equal((await call('/health?t='+encodeURIComponent('ก'.repeat(48)),null,{Authorization:''})).code,401);
  assert.equal((await call('/health')).code,200);
  assert.equal((await call('/runner/state',null,{'cf-ray':'external'})).code,403);
  assert.equal((await call('/connect',{username:'testuser'},{'cf-ray':'external'})).code,403);
  assert.equal((await call('/runner/config',{enabled:true,placeId:'999'})).code,400);
  await call('/runner/config',{enabled:true,placeId:RUNNER_PLACE_ID});
  const relay=(await call('/runner/state')).data;
  const roomHeaders={Authorization:'Bearer '+relay.pairingToken,'X-Race-Room':'studio-test','X-Race-Owner':'123','X-Race-Place':RUNNER_PLACE_ID,'cf-ray':'test-tunnel'};
  const room={room:'studio-test',owner:123,placeId:RUNNER_PLACE_ID};
  assert.equal((await call('/game/pair',room)).code,401);
  assert.equal((await call('/game/pair',room,roomHeaders)).code,200);
  await call('/connect',{username:'testuser'});
  const gift={user:{uniqueId:'viewer',nickname:'Viewer'},giftId:1,giftName:'Rose',giftType:1,groupId:'combo',diamondCount:1};
  for(const [repeatCount,repeatEnd,msgId] of [[1,false,'a'],[3,false,'b'],[3,true,'c']])await call('/test-raw',{...gift,repeatCount,repeatEnd,msgId});
  let received=(await call('/game/events?after=0',null,roomHeaders)).data;
  assert.equal(received.events.reduce((n,e)=>n+e.amount,0),3);
  assert.ok(received.events.every(e=>e.kind==='gift'&&e.giftKey==='1dc1fd9347876fbd'&&e.receivedAt>0));
  const cursor=received.events.at(-1).seq;
  for(const kind of ['gift','like','follow','chat'])await call('/test',{kind,giftName:'Rose',amount:1,comment:'POOM99TV'});
  assert.equal((await call('/game/events?after='+cursor,null,roomHeaders)).data.events.length,0);
  assert.equal((await call('/game/unpair',{},roomHeaders)).code,200);
  assert.equal((await call('/game/events?after=0',null,roomHeaders)).code,409);
});
