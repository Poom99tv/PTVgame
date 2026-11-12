import test from 'node:test';import assert from 'node:assert/strict';
import fs from 'node:fs';import os from 'node:os';import path from 'node:path';import {fileURLToPath} from 'node:url';
import sharp from 'sharp';
import {GiftResolver,createGiftDispatcher} from './gift-resolver.mjs';
import {normalize} from './normalize.mjs';import {compact} from './compact.mjs';import {createEventNormalizer} from './gift-stream.mjs';
const directory=fileURLToPath(new URL('.',import.meta.url));
const hippo=fs.readFileSync(new URL('./test-fixtures/hippo.png',import.meta.url));
const necklace=fs.readFileSync(new URL('./test-fixtures/necklace.png',import.meta.url));
const url='https://p19-webcast.tiktokcdn.com/hippo.png';
const msg=(extra={})=>({event:'gift',userId:'viewer-42',eventId:'gift-test',giftId:'123456',giftName:'ชื่อไม่ตรง',giftImageUrl:url,repeatCount:1,...extra});
const resolver=(fetchImage=async()=>hippo,options={})=>new GiftResolver(directory,{fetchImage,idsFile:false,...options});
test('the two reported live names map to their existing selected gift icons',async()=>{
 const r=resolver();assert.equal(r.byName('little hippo').name,'Baby Hippo');assert.equal(r.byName('BFF Necklace').name,'Friendship Necklace');
 assert.equal((await r.resolve(msg({giftImageUrl:'',giftName:'little hippo'}))).name,'Baby Hippo');
 assert.equal((await r.resolve(msg({giftImageUrl:'',giftName:'BFF Necklace'}))).name,'Friendship Necklace');
});
test('both real icon PNGs resolve by picture even when the gift name is completely different',async()=>{
 for(const[bytes,name]of[[hippo,'Baby Hippo'],[necklace,'Friendship Necklace']]){
  const result=await resolver(async()=>bytes).resolve(msg());assert.equal(result.name,name);assert.equal(result.method,'image-exact');
 }
});
test('a picture overrides a wrong name that happens to be another known gift',async()=>{
 const result=await resolver().resolve(msg({giftName:'Rose'}));assert.equal(result.name,'Baby Hippo');assert.equal(result.method,'image-exact');
});
test('JPEG and WebP versions of both icons still resolve by visual fingerprint',async()=>{
 for(const[bytes,name]of[[hippo,'Baby Hippo'],[necklace,'Friendship Necklace']])for(const format of ['jpeg','webp']){
  const variant=await sharp(bytes).resize(128,128).flatten({background:'#ffffff'})[format]({quality:85}).toBuffer();
  const result=await resolver(async()=>variant).resolve(msg());assert.equal(result?.name,name,format+' '+name);assert.equal(result.method,'image-perceptual');
 }
});
test('a verified numeric gift id continues working when later packets omit the picture and change names',async()=>{
 let calls=0;const r=resolver(async()=>{calls++;return hippo;});await r.resolve(msg());
 const result=await r.resolve(msg({giftImageUrl:'',giftName:'another language'}));assert.equal(result.name,'Baby Hippo');assert.equal(result.method,'gift-id');assert.equal(calls,1);
});
test('name-only fallback never poisons the verified gift id cache',async()=>{
 const r=resolver();assert.equal((await r.resolve(msg({giftImageUrl:'',giftName:'Rose'}))).name,'Rose');assert.equal(r.ids['123456'],undefined);
 assert.equal((await r.resolve(msg())).name,'Baby Hippo');
});
test('verified image identity survives restart; a read-only cache does not prevent actions',async()=>{
 const dir=fs.mkdtempSync(path.join(os.tmpdir(),'ptv-gift-image-'));
 try{
  const file=path.join(dir,'ids.json');await resolver(undefined,{idsFile:file}).resolve(msg());
  const restored=resolver(async()=>{throw Error('should use id');},{idsFile:file});assert.equal((await restored.resolve(msg({giftImageUrl:''}))).name,'Baby Hippo');
  const readOnly=resolver(undefined,{idsFile:path.join(dir,'missing','ids.json')});assert.equal((await readOnly.resolve(msg())).name,'Baby Hippo');
 }finally{const allowed=path.resolve(os.tmpdir())+path.sep;if(!path.resolve(dir).startsWith(allowed)||!path.basename(dir).startsWith('ptv-gift-image-'))throw Error('Unexpected test cleanup path');fs.rmSync(dir,{recursive:true,force:true});}
});
test('unknown pictures are not guessed from price or unrelated names',async()=>{
 const blank=await sharp({create:{width:32,height:32,channels:4,background:'#777777'}}).png().toBuffer();
 assert.equal(await resolver(async()=>blank).resolve(msg({giftDiamondCount:15})),null);
});
test('image URLs stay restricted to HTTPS TikTok CDN hosts',async()=>{
 let calls=0;const r=resolver(async()=>{calls++;return hippo;});
 for(const giftImageUrl of ['http://p19-webcast.tiktokcdn.com/icon.png','https://127.0.0.1/icon.png','https://tiktokcdn.com.attacker.example/a.png','https://u:p@p19-webcast.tiktokcdn.com/icon.png'])assert.equal(await r.resolve(msg({giftImageUrl})),null);
 assert.equal(calls,0);
});
test('failed image fetches can retry rather than remaining permanently unmatched',async()=>{
 let calls=0;const r=resolver(async()=>{if(++calls===1)throw Error('temporary');return hippo;});
 assert.equal(await r.resolve(msg()),null);r.urls.get(url).retryAt=0;assert.equal((await r.resolve(msg())).name,'Baby Hippo');assert.equal(calls,2);
});
test('legacy and current image URL forms preserve all gift metadata',()=>{
 const n=normalize('gift',{uniqueId:'viewer',giftName:'',extendedGiftInfo:{id:'99',name:'little hippo',image:{url_list:[url]}},giftPictureUrl:'https://p16.tiktokcdn.com/fallback.png'});
 assert.equal(n.giftId,'99');assert.equal(n.giftName,'little hippo');assert.deepEqual(n.giftImageUrls,[url,'https://p16.tiktokcdn.com/fallback.png']);
});
test('compact transport keeps canonical identity and raw name on every gift even after image metadata is omitted',async()=>{
 const r=resolver();const cache=new Map();const first=compact(await r.enrich(msg()),cache);
 const next=compact(await r.enrich(msg({eventId:'second',repeatCount:7})),cache);
 assert.equal(first.giftCanonicalName,'Baby Hippo');assert.equal(next.giftCanonicalName,'Baby Hippo');assert.equal(next.giftName,'ชื่อไม่ตรง');assert.equal(next.giftImageUrl,undefined);assert.equal(next.repeatCount,7);
});
test('image lookup latency cannot reorder later gifts, likes, or follows',async()=>{
 let release;const pending=new Promise(resolve=>{release=resolve;});const delivered=[];
 const dispatcher=createGiftDispatcher((event,data)=>({event,...data}),{async enrich(m){if(m.event==='gift')await pending;return m;}},m=>delivered.push(m.event));
 const first=dispatcher.accept('gift',{});const next=dispatcher.accept('follow',{});await Promise.resolve();assert.deepEqual(delivered,[]);release();await Promise.all([first,next]);assert.deepEqual(delivered,['gift','follow']);
});
test('disconnecting a session cancels pending deliveries to the game',async()=>{
 let active=true,release;const wait=new Promise(resolve=>{release=resolve;});let count=0;
 const dispatcher=createGiftDispatcher((event,data)=>({event,...data}),{async enrich(m){await wait;return m;}},()=>count++,()=>active);
 const pending=dispatcher.accept('gift',{});active=false;release();await pending;assert.equal(count,0);
});
test('image recognition preserves exact combo deltas and suppresses duplicate final receipts',async()=>{
 const delivered=[];const dispatcher=createGiftDispatcher(createEventNormalizer(),resolver(),m=>delivered.push(m));
 const raw=(count,end,id)=>({user:{userId:'42',uniqueId:'viewer'},giftId:'123456',giftName:'unknown',giftType:1,gift:{type:1,image:{urlList:[url]}},repeatCount:count,repeatEnd:end,groupId:'combo',msgId:id});
 dispatcher.accept('gift',raw(1,false,'1'));dispatcher.accept('gift',raw(3,false,'2'));dispatcher.accept('gift',raw(5,false,'3'));dispatcher.accept('gift',raw(5,true,'4'));await dispatcher.flush();
 assert.equal(delivered.reduce((n,m)=>n+m.repeatCount,0),5);assert.ok(delivered.every(m=>m.giftCanonicalName==='Baby Hippo'));
});
