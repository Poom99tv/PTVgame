import sharp from 'sharp';import crypto from 'node:crypto';
export async function fingerprint(bytes){
 const normalized=await sharp(bytes).rotate().trim({threshold:10}).png().toBuffer();
 const grid=await sharp(normalized).flatten({background:'#ffffff'}).resize(17,16,{fit:'fill'}).greyscale().raw().toBuffer();
 let bits='';for(let y=0;y<16;y++)for(let x=0;x<16;x++)bits+=grid[y*17+x]>grid[y*17+x+1]?'1':'0';
 let hash='';for(let i=0;i<bits.length;i+=4)hash+=parseInt(bits.slice(i,i+4),2).toString(16);
 const mean=[...(await sharp(normalized).flatten({background:'#ffffff'}).resize(1,1).removeAlpha().raw().toBuffer())].slice(0,3);
 return {sha:crypto.createHash('sha256').update(normalized).digest('hex'),hash,mean};
}
export function distance(a,b){let n=0;for(let i=0;i<a.length;i++){let v=parseInt(a[i],16)^parseInt(b[i],16);while(v){n+=v&1;v>>=1;}}return n;}
export function matchFingerprint(input,catalog){
 const exact=catalog.find(g=>g.sha===input.sha);if(exact)return {key:exact.key,confidence:1,method:'image-exact'};
 const scores=catalog.map(g=>({g,d:distance(input.hash,g.hash),color:Math.hypot(...input.mean.map((v,i)=>v-g.mean[i]))})).sort((a,b)=>a.d-b.d);
 const a=scores[0],b=scores[1];
 if(a&&a.d<=42&&a.color<100&&(!b||b.d-a.d>=7))return {key:a.g.key,confidence:1-a.d/256,method:'image-perceptual'};
 return null;
}
