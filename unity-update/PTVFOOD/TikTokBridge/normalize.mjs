import { randomUUID } from 'node:crypto';
export function normalize(event, data) {
 if (!['follow','share','gift','chat','like'].includes(event)) return null;
 const user=data.user??data;
 const firstValue=(...values)=>values.find(value=>value!==undefined&&value!==null&&String(value).trim()!==''&&String(value)!=='0');
 const uniqueId=String(firstValue(user.uniqueId,user.displayId,data.uniqueId)??'').replace(/^@/,'').trim();
 const nickname=String(user.nickname??uniqueId).trim();
 if(!uniqueId&&!nickname)return null;
  const candidates=[user.avatarThumb,user.profilePicture,user.profilePictureMedium,user.avatarMedium,user.profilePictureUrl,data.profilePictureUrl,user.profilePictureLarge,user.avatarLarge];
 const urls=value=>Array.isArray(value)?value:typeof value==='string'?[value]:[];
 const profilePictureUrls=[...new Set(candidates.flatMap(image=>typeof image==='string'?[image]:Array.isArray(image)?image:[...urls(image?.url),...urls(image?.mUrls),...urls(image?.urlList),...urls(image?.urls)]) .filter(url=>typeof url==='string'&&/^https?:\/\//.test(url)))];
 profilePictureUrls.sort((a,b)=>Number(/webp/i.test(a))-Number(/webp/i.test(b)));
 const profilePictureUrl=profilePictureUrls[0]??'';
const userId=String(firstValue(user.userId,user.idStr,user.id,uniqueId)??'');
 const id=String(firstValue(data.common?.msgId,data.msgId,data.eventId)??randomUUID());
 const giftImageUrls=[...new Set([data.gift?.image,data.giftDetails?.giftImage,data.giftDetails?.image,data.extendedGiftInfo?.image,data.giftImageUrl,data.giftPictureUrl].flatMap(image=>typeof image==='string'?[image]:[...urls(image?.urlList),...urls(image?.url_list),...urls(image?.url),...urls(image?.mUrls),...urls(image?.urls)]).filter(url=>typeof url==='string'&&/^https:\/\//.test(url)))];
 return {event,comment:event==="chat"?String(data.comment??data.text??"").trim().slice(0,128):undefined,repeatCount:event==="gift"?Math.max(1,Math.min(2147483647,Math.floor(Number(data.repeatCount)||1))):0,likeCount:event==="like"?Math.max(0,Math.min(10000,Math.floor(Number(data.count??data.likeCount??0)||0))):0,userId,profilePictureUrl,profilePictureUrls,eventId:`${event}:${id}`,uniqueId,nickname,giftId:String(firstValue(data.giftId,data.gift?.id,data.gift?.giftId,data.giftDetails?.id,data.extendedGiftInfo?.id)??''),giftDiamondCount:Number(data.gift?.diamondCount??data.giftDetails?.diamondCount??data.diamondCount??0),giftImageUrl:giftImageUrls[0]??'',giftImageUrls,giftName:String(firstValue(data.giftDetails?.name,data.giftDetails?.giftName,data.giftName,data.gift?.name,data.extendedGiftInfo?.name)??'').trim(),giftType:Number(data.gift?.type??data.gift?.giftType??data.giftDetails?.giftType??data.giftType??0),repeatEnd:data.repeatEnd===true||data.repeatEnd===1||data.repeatEnd==='1'};
}






