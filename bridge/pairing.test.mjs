import test from 'node:test';import assert from 'node:assert/strict';
import {RoomLease,authorized} from './pairing.mjs';
test('isolate rooms, refresh lease, expiry and release',()=>{
 let now=100000;const lease=new RoomLease(()=>now);
 lease.claim('room-one',123);assert(lease.check('room-one',123));
 assert(!lease.check('room-two',123));assert(!lease.check('room-one',456));
 assert.throws(()=>lease.claim('room-two',456),e=>e.status===409);
 now+=31000;assert(!lease.check('room-one',123));lease.claim('room-two',456);
 assert(lease.check('room-two',456));lease.release();assert(!lease.info().paired);
});
test('reject invalid room, owner and authorization',()=>{
 const lease=new RoomLease();for(const [room,owner]of [['',123],['room',0],['room','123'],['x'.repeat(101),123]])assert.throws(()=>lease.claim(room,owner));
 assert(authorized('Bearer abc','abc'));assert(!authorized('Bearer ab','abc'));assert(!authorized(undefined,'abc'));
});
