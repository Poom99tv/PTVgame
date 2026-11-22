export function compact(message, profiles) {
 if (!message.userId || !message.eventId) return message;
 const result = {event:message.event,eventId:message.eventId,userId:message.userId,uniqueId:message.uniqueId,nickname:message.nickname};
 if(message.event==='gift') Object.assign(result,{giftId:message.giftId,giftDiamondCount:message.giftDiamondCount,giftName:message.giftName,repeatCount:message.repeatCount,repeatEnd:true,giftDelta:message.giftDelta===true,giftTotalCount:message.giftTotalCount,comboId:message.comboId,streakEnded:message.streakEnded});
 if(message.event==='gift'&&message.giftImageUrl){const key='gift:'+(message.giftId||message.giftName);if(profiles.get(key)!==message.giftImageUrl){profiles.set(key,message.giftImageUrl);result.giftImageUrl=message.giftImageUrl;}}
 if(message.event==='chat') result.comment=message.comment??'';
 if(message.event==='like') result.likeCount=message.likeCount;
 const urls=[...new Set([message.profilePictureUrl,...(message.profilePictureUrls??[])].filter(Boolean))].slice(0,2);
 const signature=urls.join('|');
 if(signature && profiles.get(message.userId)!==signature){
  profiles.delete(message.userId);profiles.set(message.userId,signature);
  if(profiles.size>4096)profiles.delete(profiles.keys().next().value);
  result.profilePictureUrl=urls[0];if(urls.length>1)result.profilePictureUrls=urls;
 }
 return result;
}

