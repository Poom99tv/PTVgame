import crypto from 'node:crypto';
export class RoomLease {
 constructor(now=()=>Date.now()){this.now=now;this.room=null;}
 claim(room,owner){
  if(typeof room!=='string'||!room||room.length>100||!Number.isSafeInteger(owner)||owner<1)throw Object.assign(Error('Invalid room'),{status:400});
  if(this.room&&this.now()-this.room.at<30000&&(this.room.id!==room||this.room.owner!==owner))throw Object.assign(Error('This bridge is already paired with another active room'),{status:409});
  this.room={id:room,owner,at:this.now()};return {ok:true};
 }
 check(room,owner){if(!this.room||this.room.id!==room||this.room.owner!==Number(owner)||this.now()-this.room.at>30000)return false;this.room.at=this.now();return true;}
 release(){this.room=null;}
 info(){return this.room?{paired:this.now()-this.room.at<30000,owner:this.room.owner,lastSeen:this.room.at}:{paired:false};}
}
export function authorized(value,token){const a=Buffer.from(String(value||'')),b=Buffer.from('Bearer '+token);return a.length===b.length&&crypto.timingSafeEqual(a,b);}
