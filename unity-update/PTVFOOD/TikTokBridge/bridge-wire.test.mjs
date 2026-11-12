import test from 'node:test';import assert from 'node:assert/strict';import net from 'node:net';import {spawn} from 'node:child_process';import {once} from 'node:events';import {fileURLToPath} from 'node:url';
test('real TCP bridge forwards both reported aliases as canonical gift identities without a LIVE connection',{timeout:15000},async()=>{
 const reservation=net.createServer();reservation.listen(0,'127.0.0.1');await once(reservation,'listening');const port=reservation.address().port;await new Promise(r=>reservation.close(r));
 const child=spawn(process.execPath,['bridge.mjs','--test-mode'],{cwd:fileURLToPath(new URL('.',import.meta.url)),env:{...process.env,PAINT_BRIDGE_PORT:String(port)},windowsHide:true,stdio:['ignore','pipe','pipe']});
 const exited=once(child,'exit');let client;let errors='';child.stderr.on('data',b=>errors+=b);
 try{
  await new Promise((resolve,reject)=>{let out='';const timer=setTimeout(()=>reject(Error('Bridge startup timeout: '+errors)),8000);child.once('error',e=>{clearTimeout(timer);reject(e);});child.stdout.on('data',b=>{out+=b;if(out.includes('bridge ready')){clearTimeout(timer);resolve();}});});
  client=net.createConnection({host:'127.0.0.1',port});await once(client,'connect');client.setEncoding('utf8');let buffer='';const messages=[];const waiters=[];
  client.on('data',chunk=>{buffer+=chunk;let end;while((end=buffer.indexOf('\n'))>=0){messages.push(JSON.parse(buffer.slice(0,end)));buffer=buffer.slice(end+1);for(const fn of [...waiters])fn();}});
  const waitFor=predicate=>new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error('Missing bridge message: '+errors)),5000);const check=()=>{const m=messages.find(predicate);if(m){clearTimeout(timer);waiters.splice(waiters.indexOf(check),1);resolve(m);}};waiters.push(check);check();});
  client.write(JSON.stringify({command:'connect',username:'offline_gift_test'})+'\n');await waitFor(m=>m.state==='connected');
  for(const[giftName,giftId,id]of[['BFF Necklace','10001','wire-necklace'],['little hippo','10002','wire-hippo']])client.write(JSON.stringify({command:'inject',event:'gift',data:{user:{userId:'42',uniqueId:'fixture'},giftName,giftId,msgId:id,repeatCount:1,giftType:0,repeatEnd:true}})+'\n');
  const necklace=await waitFor(m=>m.giftName==='BFF Necklace');const hippo=await waitFor(m=>m.giftName==='little hippo');
  assert.equal(necklace.giftCanonicalName,'Friendship Necklace');assert.equal(hippo.giftCanonicalName,'Baby Hippo');assert.equal(necklace.repeatCount,1);assert.equal(hippo.repeatCount,1);assert.equal(necklace.giftMatchMethod,'name');
  assert.deepEqual(messages.filter(m=>m.event==='gift').map(m=>m.giftCanonicalName),['Friendship Necklace','Baby Hippo']);
 }finally{client?.destroy();child.kill();await exited;}
});
