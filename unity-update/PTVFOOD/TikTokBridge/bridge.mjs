import net from 'node:net';
import {fileURLToPath} from 'node:url';
import {GiftResolver,createGiftDispatcher} from './gift-resolver.mjs';
import { TikTokLiveConnection, WebcastEvent, ControlEvent } from 'tiktok-live-connector';
import { createEventNormalizer } from './gift-stream.mjs';
let normalize=createEventNormalizer();
const giftResolver=new GiftResolver(fileURLToPath(new URL('.',import.meta.url)));
let pipeline;
import { send, flush } from './transport.mjs';
const clients=new Set();let connection=null;let generation=0;let retry=null;let username='';let state={event:'status',state:'idle',detail:'',streamer:''};
const testMode=process.argv.includes('--test-mode');
function broadcast(data){for(const client of clients)send(client,data);}
pipeline=createGiftDispatcher(normalize,giftResolver,broadcast,()=>generation===0);
function status(value,detail=''){state={event:'status',state:value,detail:String(detail).slice(0,300),streamer:username};broadcast(state);}
async function stop(){generation++;clearTimeout(retry);retry=null;const old=connection;connection=null;if(old){old.removeAllListeners();old.on('error',()=>{});try{await old.disconnect();}catch{}}}
async function connect(name){const request=generation+1;await stop();if(request!==generation)return;username=String(name??'').trim().replace(/^@/,'');
 if(!/^[a-zA-Z0-9_.]{1,64}$/.test(username)){status('error','กรอกชื่อบัญชี TikTok เช่น @ชื่อบัญชี');return;}
 normalize=createEventNormalizer();
 const token=generation;
 pipeline=createGiftDispatcher(normalize,giftResolver,broadcast,()=>token===generation);
 if(testMode){status('connected','โหมดทดสอบ ไม่มีการเชื่อมไลฟ์จริง');return;}
 async function attempt(){if(token!==generation)return;status('connecting');
  const live=new TikTokLiveConnection(username,{processInitialData:false,webClientOptions:{timeout:{request:15000}},wsClientOptions:{handshakeTimeout:15000}});connection=live;
  for(const [type,event] of [['follow',WebcastEvent.FOLLOW],['share',WebcastEvent.SHARE],['gift',WebcastEvent.GIFT],['chat',WebcastEvent.CHAT],['like',WebcastEvent.LIKE]])live.on(event,data=>{if(token===generation){pipeline.accept(type,data);}});
  live.on(ControlEvent.ERROR,()=>{if(token===generation&&!live.isConnecting&&!live.isConnected)status('error','TikTok ไม่ตอบสนอง กรุณาลองใหม่');});
  live.on(WebcastEvent.STREAM_END,()=>{if(token!==generation)return;stop();status('ended','ไลฟ์จบแล้ว');});
  live.on(ControlEvent.DISCONNECTED,()=>{if(token!==generation||retry)return;status('reconnecting');retry=setTimeout(()=>{retry=null;attempt();},15000);});
  try{await live.connect();if(token===generation)status('connected');else await live.disconnect();}
  catch(error){if(token!==generation)return;live.removeAllListeners();live.on('error',()=>{});try{await live.disconnect();}catch{}
   status('error',error?.name==='UserOfflineError'?'บัญชีนี้ยังไม่ได้เปิดไลฟ์':'เชื่อม TikTok ไม่สำเร็จ ตรวจชื่อบัญชีและลองใหม่');}
 }
 await attempt();
}
const server=net.createServer(client=>{clients.add(client);client.setEncoding('utf8');client.setNoDelay(true);client.on('drain',()=>flush(client));send(client,state);let buffer='';
 client.on('data',chunk=>{buffer+=chunk;let end;while((end=buffer.indexOf('\n'))>=0){if(end>65536){client.destroy();return;}const line=buffer.slice(0,end);buffer=buffer.slice(end+1);try{const message=JSON.parse(line);
  if(message.command==='connect')connect(message.username).catch(()=>status('error','เชื่อมไม่สำเร็จ'));
  else if(message.command==='disconnect')stop().then(()=>{username='';status('idle');});
  else if(testMode&&message.command==='inject'){pipeline.accept(message.event,message.data);}
 }catch{send(client,{event:'status',state:'error',detail:'Invalid message'});}}if(buffer.length>65536)client.destroy();});
 client.on('error',()=>{});client.on('close',()=>{clients.delete(client);});
});
server.on('error',error=>{console.error(error.code==='EADDRINUSE'?'Bridge already running':error.message);process.exit(1);});
server.listen(Number(process.env.PAINT_BRIDGE_PORT || 18776),'127.0.0.1',()=>console.log('Paint TikTok bridge ready'));
async function shutdown(){await stop();for(const client of clients)client.destroy();server.close(()=>process.exit());}
process.on('SIGINT',shutdown);process.on('SIGTERM',shutdown);





