import fs from 'node:fs';
import path from 'node:path';
import {fingerprint,matchFingerprint} from './image-match.mjs';
export class GiftResolver {
 constructor(directory){this.catalog=JSON.parse(fs.readFileSync(path.join(directory,'gift-catalog.json')));this.byKey=new Map(this.catalog.map(g=>[g.key,g]));this.file=path.join(directory,'gift-identities.json');try{this.ids=JSON.parse(fs.readFileSync(this.file))}catch{this.ids={}}this.urls=new Map();}
 async resolve(data){
  const id=String(data.giftId||data.gift?.id||'');
  if(id&&this.byKey.has(this.ids[id]))return {key:this.ids[id],method:'gift-id'};
  const urls=data.gift?.image?.urlList||data.gift?.image?.url_list||data.extendedGiftInfo?.image?.urlList||[];
  let result=null;
  for(const url of urls.slice(0,2)){try{
   const u=new URL(url);if(u.protocol!=='https:'||!/(^|\.)(tiktokcdn[^.]*\.com|byteoversea\.com|ibytedtos\.com|tiktokcdn-eu\.com)$/.test(u.hostname))continue;
   if(!this.urls.has(url)){this.urls.set(url,(async()=>{const response=await fetch(url,{signal:AbortSignal.timeout(5000),redirect:'error'});if(!response.ok)throw Error('image unavailable');const chunks=[];let length=0;for await(const chunk of response.body){length+=chunk.length;if(length>8*1024*1024)throw Error('image too large');chunks.push(chunk)}return matchFingerprint(await fingerprint(Buffer.concat(chunks)),this.catalog)})().catch(()=>null));if(this.urls.size>1000)this.urls.delete(this.urls.keys().next().value)}
   result=await this.urls.get(url);if(result)break;
  }catch{}}
  if(!result){const name=String(data.gift?.name||data.giftName||data.extendedGiftInfo?.name||'').trim().toLowerCase();const gift=this.catalog.find(g=>g.name.toLowerCase()===name);if(gift)result={key:gift.key,method:'name'};}
  if(id&&result){this.ids[id]=result.key;fs.writeFileSync(this.file,JSON.stringify(this.ids));}
  return result;
 }
}
