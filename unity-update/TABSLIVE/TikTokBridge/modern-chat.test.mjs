import test from 'node:test';
import assert from 'node:assert/strict';
import {normalize} from './normalize.mjs';
test('modern connector content reaches team selection',()=>{
  const result=normalize('chat',{user:{id:'1234567890123456789',displayId:'viewer'},content:' 2 '});
  assert.equal(result.comment,'2');
  assert.equal(result.userId,'1234567890123456789');
});
