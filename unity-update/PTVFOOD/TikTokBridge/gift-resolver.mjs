import fs from 'node:fs';
import path from 'node:path';
import {fingerprint,matchFingerprint} from './image-match.mjs';

export const nameKey=value=>String(value??'').normalize('NFKD').toLowerCase().replace(/[^a-z0-9]/g,'');
const aliases=new Map([['littlehippo','babyhippo'],['bffnecklace','friendshipnecklace'],['friendship','friendshipnecklace'],['donut','doughnut'],['handheart','handhearts']]);
const CDN=/(^|\.)(tiktokcdn[^.]*\.com|byteoversea\.com|ibytedtos\.com|ttwstatic\.com)$/i;
const MAX_BYTES=8*1024*1024;
export class GiftResolver {
  constructor(directory,{fetchImage,idsFile}={}){
    this.catalog=JSON.parse(fs.readFileSync(path.join(directory,'gift-catalog.json'),'utf8'));
    this.byKey=new Map(this.catalog.map(g=>[g.key,g]));this.names=new Map();
    for(const gift of this.catalog)for(const name of [gift.name,gift.listedName])if(name)this.names.set(nameKey(name),gift);
    this.file=idsFile===false?null:(idsFile??path.join(directory,'gift-image-identities.v1.json'));
    try{this.ids=JSON.parse(fs.readFileSync(this.file,'utf8'));}catch{this.ids={};}
    this.urls=new Map();this.fetchImage=fetchImage??this.download.bind(this);this.active=0;this.waiting=[];
  }
  byName(name){let key=nameKey(name);key=aliases.get(key)??key;return this.names.get(key)??null;}
  async limited(task){
    if(this.active>=4)await new Promise(resolve=>this.waiting.push(resolve));else this.active++;
    try{return await task();}finally{const next=this.waiting.shift();if(next)next();else this.active--;}
  }
  async download(url){
    const response=await fetch(url,{signal:AbortSignal.timeout(3000),redirect:'error'});
    if(!response.ok)throw Error('Gift image unavailable');
    const chunks=[];let length=0;
    for await(const chunk of response.body){length+=chunk.length;if(length>MAX_BYTES)throw Error('Gift image too large');chunks.push(chunk);}
    return Buffer.concat(chunks);
  }
  learn(id,result){
    if(!/^\d{1,24}$/.test(id)||!result.method.startsWith('image-'))return;
    this.ids[id]={key:result.key,verified:true};
    const keys=Object.keys(this.ids);if(keys.length>4096)delete this.ids[keys[0]];
    if(this.file){const content=JSON.stringify(this.ids);try{const temp=this.file+'.tmp';fs.writeFileSync(temp,content);fs.renameSync(temp,this.file);}catch{try{fs.writeFileSync(this.file,content);}catch{}}}
  }
  async resolve(message){
    const rawId=String(message.giftId??'');const id=/^\d{1,24}$/.test(rawId)?rawId:'';
    const cached=id&&this.ids[id];
    if(cached?.verified&&this.byKey.has(cached.key))return {...this.byKey.get(cached.key),method:'gift-id'};
    const urls=[...new Set([message.giftImageUrl,...(message.giftImageUrls??[])].filter(Boolean))].slice(0,2);
    for(const url of urls){
      try{
        const u=new URL(url);if(u.protocol!=='https:'||!CDN.test(u.hostname)||u.username||u.password||u.port)continue;
        let entry=this.urls.get(url);
        if(!entry||entry.retryAt<Date.now()){
          entry={retryAt:Infinity,promise:this.limited(async()=>{
            const bytes=await this.fetchImage(url);if(!bytes?.length||bytes.length>MAX_BYTES)throw Error('Invalid gift image size');
            return matchFingerprint(await fingerprint(bytes),this.catalog);
          }).catch(()=>null)};
          this.urls.set(url,entry);if(this.urls.size>512)this.urls.delete(this.urls.keys().next().value);
        }
        const match=await entry.promise;
        if(match&&this.byKey.has(match.key)){this.learn(id,match);return {...this.byKey.get(match.key),method:match.method,confidence:match.confidence};}
        if(entry.retryAt===Infinity)entry.retryAt=Date.now()+5000;
      }catch{}
    }
    const named=this.byName(message.giftName);return named?{...named,method:'name'}:null;
  }
  async enrich(message){
    if(message.event!=='gift')return message;
    const result=await this.resolve(message);
    return result?{...message,giftCanonicalName:result.name,giftMatchMethod:result.method}:message;
  }
}

// Start image work concurrently, but deliver every event in original arrival order.
export function createGiftDispatcher(normalize,resolver,deliver,isCurrent=()=>true){
  let tail=Promise.resolve();
  return {
    accept(event,data){
      const message=normalize(event,data);if(!message)return Promise.resolve();
      const ready=Promise.resolve().then(()=>resolver.enrich(message)).catch(()=>message);
      tail=tail.then(()=>ready).then(result=>{if(isCurrent())deliver(result);}).catch(error=>console.error('Gift dispatch failed:',error.message));
      return tail;
    },
    flush(){return tail;}
  };
}
