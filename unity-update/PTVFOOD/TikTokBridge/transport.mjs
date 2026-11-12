import { compact } from './compact.mjs';
export function send(client,data){
 if(client.destroyed)return;
 const message=compact(data,client.profileMetadata??=new Map());
 if(client.outboundBlocked){
  const queue=client.pendingOutbound??=[];
  const last=queue.at(-1);
  if(message.event==='gift'&&last?.event==='gift'&&message.comboId&&message.comboId===last.comboId&&message.userId===last.userId&&message.giftId===last.giftId){
   const count=last.repeatCount+message.repeatCount;
   Object.assign(last,message,{repeatCount:count,eventId:message.eventId+':batch'});
  }else queue.push(message);
  return;
 }
 client.outboundBlocked=!client.write(JSON.stringify(message)+'\n');
}
export function flush(client){
 if(client.destroyed)return;
 client.outboundBlocked=false;
 while(client.pendingOutbound?.length&&!client.outboundBlocked){
  const message=client.pendingOutbound.shift();
  client.outboundBlocked=!client.write(JSON.stringify(message)+'\n');
 }
}
