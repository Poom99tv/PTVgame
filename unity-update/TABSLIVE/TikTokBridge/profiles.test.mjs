import test from 'node:test';
import assert from 'node:assert/strict';
import {avatarUrls,readOwner} from './profiles.mjs';
import {normalize} from './normalize.mjs';

test('owner room-info uses snake-case fields and URL lists',()=>{
 assert.deepEqual(readOwner({data:{owner:{nickname:'เจ้าของไลฟ์',avatar_thumb:{url_list:['https://cdn/owner.jpg']}}}}),
  {ownerNickname:'เจ้าของไลฟ์',ownerAvatars:['https://cdn/owner.jpg']});
});
test('protobuf and direct URL formats retain fallbacks, prefer supported images',()=>{
 assert.deepEqual(avatarUrls({avatarThumb:{mUrls:['https://cdn/a.webp','https://cdn/a.jpg']},avatarMedium:{url:['https://cdn/a.jpg','https://cdn/b.png']},profilePictureUrl:'https://cdn/c.jpg'}),
  ['https://cdn/a.jpg','https://cdn/b.png','https://cdn/c.jpg','https://cdn/a.webp']);
});
test('blank nickname falls back to owner handle, never viewer identity',()=>{
 assert.deepEqual(readOwner({owner:{nickname:' ',displayId:'owner.handle',avatarLarge:{urlList:['https://cdn/owner.png']}},user:{nickname:'viewer'}}),
  {ownerNickname:'owner.handle',ownerAvatars:['https://cdn/owner.png']});
 assert.deepEqual(readOwner({user:{nickname:'viewer'}}),{ownerNickname:'',ownerAvatars:[]});
});
test('owner and donor photos remain separate in the same event',()=>{
 const payload={owner:{nickname:'Host',avatarThumb:{urlList:['https://cdn/host.png']}},user:{uniqueId:'donor',nickname:'ผู้ส่งหิน',avatar_thumb:{url_list:['https://cdn/donor.png']}},giftName:'Rose',giftId:'5655',repeatCount:1,repeatEnd:true};
 assert.equal(readOwner(payload).ownerAvatars[0],'https://cdn/host.png');
 const donor=normalize('gift',payload);assert.equal(donor.nickname,'ผู้ส่งหิน');assert.equal(donor.profilePictureUrl,'https://cdn/donor.png');
});
test('invalid and duplicate image candidates are filtered',()=>{
 assert.deepEqual(avatarUrls({avatarThumb:{url_list:['file:///bad','https://cdn/x.png','https://cdn/x.png',42]},profilePictureUrl:'not a URL'}),['https://cdn/x.png']);
});
