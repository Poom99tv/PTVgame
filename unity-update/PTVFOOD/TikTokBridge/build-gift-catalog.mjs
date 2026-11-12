import fs from 'node:fs/promises';import path from 'node:path';import crypto from 'node:crypto';import {fileURLToPath} from 'node:url';
import {fingerprint} from './image-match.mjs';
const root=fileURLToPath(new URL('.',import.meta.url));
const resources=process.argv[2];if(!resources)throw Error('Usage: node build-gift-catalog.mjs <PaintGifts directory>');
const index=JSON.parse(await fs.readFile(path.join(resources,'GiftPriceIndex.json'),'utf8'));
let previous=[];try{previous=JSON.parse(await fs.readFile(path.join(root,'gift-catalog.json'),'utf8'));}catch{}
const keys=new Map(previous.map(g=>[g.name,g.key]));const gifts=index.entries.filter(g=>g.price>0);const result=new Array(gifts.length);let next=0;
await Promise.all(Array.from({length:4},async()=>{while(next<gifts.length){const i=next++,g=gifts[i];const file=g.name+'.png';const image=await fingerprint(await fs.readFile(path.join(resources,file)));result[i]={key:keys.get(g.name)??crypto.createHash('sha256').update(g.name).digest('hex').slice(0,16),name:g.name,listedName:g.listedName,price:g.price,file,...image};}}));
await fs.writeFile(path.join(root,'gift-catalog.json'),JSON.stringify(result));console.log('Fingerprinted '+result.length+' selectable game icons with bundled matcher.');
