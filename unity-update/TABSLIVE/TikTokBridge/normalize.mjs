import { avatarUrls } from './profiles.mjs';
import { randomUUID } from 'node:crypto';
export function normalize(event, data) {
 if (!['follow','share','gift','chat','like'].includes(event)) return null;
 const user=data.user??data;
 const firstValue=(...values)=>values.find(value=>value!==undefined&&value!==null&&String(value).trim()!==''&&String(value)!=='0');
 const uniqueId=String(firstValue(user.uniqueId,user.displayId,data.uniqueId)??'').replace(/^@/,'').trim();
 const nickname=String(user.nickname??uniqueId).trim();
 if(!uniqueId&&!nickname)return null;
 const urls=value=>Array.isArray(value)?value:typeof value==='string'?[value]:[];
 const profilePictureUrls=avatarUrls({...data,...user});
 const profilePictureUrl=profilePictureUrls[0]??'';
const userId=String(firstValue(user.userId,user.idStr,user.id,uniqueId)??'');
 const id=String(firstValue(data.common?.msgId,data.msgId,data.eventId)??randomUUID());
 const likeCount=event==='like'?[data.count,data.likeCount].map(Number).find(value=>Number.isFinite(value)&&value>0)??0:0;
 return {event,comment:event==="chat"?String(data.comment??data.content??data.text??"").trim().slice(0,128):undefined,repeatCount:event==="gift"?Math.max(1,Math.min(2147483647,Math.floor(Number(data.repeatCount)||1))):0,likeCount:Math.max(0,Math.min(2147483647,Math.floor(likeCount))),userId,profilePictureUrl,profilePictureUrls,eventId:`${event}:${id}`,uniqueId,nickname,giftId:String(firstValue(data.giftId,data.gift?.id,data.gift?.giftId,data.giftDetails?.id)??''),giftDiamondCount:Number(data.gift?.diamondCount??data.giftDetails?.diamondCount??data.diamondCount??0),giftImageUrl:[data.gift?.image,data.giftDetails?.giftImage,data.giftDetails?.image,data.giftPictureUrl].flatMap(image=>typeof image==='string'?[image]:[...urls(image?.urlList),...urls(image?.url),...urls(image?.mUrls)]).find(url=>typeof url==='string'&&/^(https:\/\/|http:\/\/127\.0\.0\.1[:\/])/.test(url))??'',giftName:String(data.giftDetails?.name??data.giftDetails?.giftName??data.giftName??data.gift?.name??'').trim(),giftType:Number(data.gift?.type??data.gift?.giftType??data.giftDetails?.giftType??data.giftType??0),repeatEnd:data.repeatEnd===true||data.repeatEnd===1||data.repeatEnd==='1'};
}








