import test from 'node:test';
import assert from 'node:assert/strict';
import {compact} from './compact.mjs';
test('metadata only on first event or change; rewards preserved; reconnect independent',()=>{
 const profiles=new Map();const original={event:'gift',eventId:'gift:1',userId:'123',uniqueId:'alice',nickname:'Alice',giftName:'Rose',repeatCount:5,profilePictureUrl:'https://example.com/thumb.png',profilePictureUrls:['https://example.com/thumb.png','https://example.com/fallback.png','https://example.com/large.png']};
 const first=compact(original,profiles),second=compact({...original,eventId:'gift:2'},profiles);
 assert.equal(first.profilePictureUrls.length,2);assert.equal(second.profilePictureUrl,undefined);assert.equal(second.profilePictureUrls,undefined);assert.equal(second.repeatCount,5);assert.equal(second.giftName,'Rose');assert.equal(second.nickname,'Alice');assert.equal(second.repeatEnd,true);
 assert.equal(compact(original,new Map()).profilePictureUrl,original.profilePictureUrl);
 const changed=compact({...original,profilePictureUrl:'https://example.com/new.png'},profiles);assert.equal(changed.profilePictureUrl,'https://example.com/new.png');
 const like=compact({...original,event:'like',likeCount:50},profiles);assert.equal(like.likeCount,50);assert.equal(like.giftName,undefined);
 console.log('Repeated gift payload bytes:',Buffer.byteLength(JSON.stringify(original)),'->',Buffer.byteLength(JSON.stringify(second)));
});
