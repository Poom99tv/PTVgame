import {normalizeChat} from './chat.mjs';
// Bridge v6: forwards raw TikTok actions to Roblox. The game decides (per skill, from the
// in-game settings) which gift / follow / Heart Me / like count fires which skill.
//   {kind:'gift', viewer, viewerDisplayName, giftKey, giftName, amount}
//   {kind:'like', viewer, viewerDisplayName, amount}   (likes summed per viewer, sent once a second)
//   {kind:'follow', viewer, viewerDisplayName}          (once per viewer per connection)
export class RawEvents {
  constructor(emit,{likeFlushMs=1000}={}){
    this.emit=emit;this.streaks=new Map();this.seen=new Set();this.likes=new Map();this.followed=new Set();
    this.timer=setInterval(()=>this.flushLikes(),likeFlushMs);this.timer.unref?.();
  }
  reset(){this.flushLikes();this.streaks.clear();this.seen.clear();this.likes.clear();this.followed.clear();}
  stop(){clearInterval(this.timer);}
  once(id){if(!id)return true;if(this.seen.has(id))return false;this.seen.add(id);if(this.seen.size>20000)this.seen.delete(this.seen.values().next().value);return true;}
  gift(data){
    const {viewer,messageId,viewerDisplayName}=normalizeChat(data);
    if(viewer==='unknown'||!this.once(messageId&&messageId+':'+data.repeatCount+':'+data.repeatEnd))return;
    const giftKey=data.resolvedGiftKey;if(!giftKey)return;
    const giftName=String(data.gift?.name||data.giftName||data.extendedGiftInfo?.name||'').trim();
    const id=String(data.giftId||data.gift?.id||'');
    const count=Math.max(1,Math.floor(Number(data.repeatCount)||1));
    const rawGroup=String(data.groupId||data.orderId||'');
    const group=rawGroup==='0'?'':rawGroup;
    const streak=Number(data.gift?.type??data.giftType)===1||!!group;
    let amount=count;
    if(streak&&group){const key=viewer+':'+id+':'+group;const previous=this.streaks.get(key)||0;amount=Math.max(0,count-previous);this.streaks.set(key,Math.max(count,previous));if(this.streaks.size>10000)this.streaks.delete(this.streaks.keys().next().value);}
    else if(streak&&!data.repeatEnd)return;
    if(amount>0)this.emit({kind:'gift',viewer,viewerDisplayName,giftKey,giftName,amount});
  }
  like(data){
    const {viewer,messageId,viewerDisplayName}=normalizeChat(data);
    if(viewer==='unknown'||!this.once(messageId))return;
    const count=Math.max(0,Math.floor(Number(data.likeCount??data.count)||0));
    if(!Number.isFinite(count)||count===0)return;
    const entry=this.likes.get(viewer)||{amount:0,viewerDisplayName};
    entry.amount+=count;if(viewerDisplayName)entry.viewerDisplayName=viewerDisplayName;
    this.likes.set(viewer,entry);
  }
  flushLikes(){
    for(const [viewer,entry] of this.likes)this.emit({kind:'like',viewer,viewerDisplayName:entry.viewerDisplayName,amount:entry.amount});
    this.likes.clear();
  }
  follow(data){
    const {viewer,viewerDisplayName}=normalizeChat(data);
    if(viewer==='unknown'||this.followed.has(viewer))return;
    this.followed.add(viewer);if(this.followed.size>50000)this.followed.delete(this.followed.values().next().value);
    this.emit({kind:'follow',viewer,viewerDisplayName});
  }
}
