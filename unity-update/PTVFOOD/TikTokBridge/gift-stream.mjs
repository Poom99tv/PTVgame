import { normalize } from './normalize.mjs';
// Retain one instance across reconnects of the same LIVE; reset for a new LIVE.
export function createEventNormalizer({now=Date.now,maxEntries=16384}={}) {
 const combos=new Map(),seen=new Map(),active=new Map();let serial=0;
 const put=(map,key,value)=>{map.delete(key);map.set(key,value);while(map.size>maxEntries)map.delete(map.keys().next().value);};
 return (event,data)=>{
  const message=normalize(event,data);if(!message||event!=='gift')return message;
  const time=now(),count=message.repeatCount;
  const identity=JSON.stringify([message.userId,message.giftId||message.giftName,String(data.toUser?.id??data.toUser?.userId??'')]);
  const group=String(data.groupId??'').replace(/^0$/,'');
  const key=group?identity+':'+group:identity;
  const fingerprint=message.eventId+':'+identity+':'+group+':'+count+':'+message.repeatEnd;
  if(seen.has(fingerprint))return null;put(seen,fingerprint,time);
  let quantity=count,comboId=message.eventId;
  if(message.giftType===1){
   let current=combos.get(key);
   // Some TikTok frames rotate groupId while advancing the same streak.
   const ongoing=active.get(identity);
   if(!current&&group&&ongoing&&!ongoing.closed&&count>1&&time-ongoing.at<30000)current=ongoing;
   if(!current&&group&&ongoing&&message.repeatEnd&&time-ongoing.at<30000&&count>=ongoing.total)current=ongoing;
   const expired=current&&time-current.at>30*60*1000;
   const restarted=current&&!group&&!message.repeatEnd&&(current.closed&&count===1||count===1&&current.total>1&&time-current.at>3000);
   if(!current||expired||restarted)current={total:0,closed:false,id:key+':'+(++serial)};
   quantity=Math.max(0,count-current.total);
   current.total=Math.max(count,current.total);current.closed||=message.repeatEnd;current.at=time;
   comboId=current.id;put(combos,key,current);put(active,identity,current);if(!quantity)return null;
  }
  return {...message,repeatCount:quantity,giftTotalCount:count,giftDelta:true,comboId,
   streakEnded:message.repeatEnd,repeatEnd:true,eventId:message.eventId+':'+comboId+':'+count};
 };
}



