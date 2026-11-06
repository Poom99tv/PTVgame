import test from 'node:test';
import assert from 'node:assert/strict';
import { RunnerRelay, RUNNER_PLACE_ID } from './runner.mjs';

const gift = { kind: 'gift', viewer: 'viewer', name: 'Viewer Name', giftKey: '1dc1fd9347876fbd', giftName: 'Rose', amount: 300, at: 12345 };
test('signal mode is restricted to PTVRunner and never forwards before enabled', () => {
  const r = new RunnerRelay(); r.accept(gift); assert.equal(r.events.length, 0);
  assert.throws(() => r.configure(true, '999'), /only supported/);
  r.configure(true, RUNNER_PLACE_ID); r.accept(gift);
  assert.equal(r.events[0].amount, 300); assert.equal(r.events[0].giftKey, gift.giftKey);
  assert.equal(r.events[0].viewerDisplayName, 'Viewer Name'); assert.equal(r.events[0].receivedAt, 12345);
  assert.equal(r.events[0].skill, undefined); assert.equal(r.events[0].key, undefined);
});
test('demo and test buttons never send actions to a paired game', () => {
  const r = new RunnerRelay(); r.configure(true, RUNNER_PLACE_ID);
  for (const event of [{ ...gift, demo: true }, { ...gift, method: 'test' }, { kind: 'like', viewer: 'x', amount: 1000, test: true }, { kind: 'follow', viewer: 'x', test: true }]) r.accept(event);
  assert.equal(r.events.length, 0);
});
test('raw likes, follows and Roblox chat registration preserve viewer identity', () => {
  let now = 100000; const r = new RunnerRelay(() => now); r.configure(true, RUNNER_PLACE_ID);
  r.accept({ kind: 'like', viewer: 'x', name: 'X', amount: 1234 });
  r.accept({ kind: 'follow', viewer: 'x', name: 'X' });
  r.accept({ kind: 'chat', viewer: 'x', name: 'X', comment: '!join POOM99TV' });
  r.accept({ kind: 'chat', viewer: 'x', comment: '!join AnotherUser' });
  assert.deepEqual(r.events.map(e => e.kind), ['like', 'follow', 'join']);
  assert.equal(r.events[2].account, 'POOM99TV'); assert.equal(r.events[2].viewer, 'x');
  now += 15001; r.accept({ kind: 'chat', viewer: 'x', comment: '12345' });
  assert.equal(r.events[3].account, '12345');
});
test('changing back to presets releases the room and discards the old relay session', () => {
  const r = new RunnerRelay(); r.configure(true, RUNNER_PLACE_ID);r.accept(gift);
  r.room = { id: 'old', owner: 1, at: Date.now() };const session = r.session;
  r.configure(false, ''); assert.equal(r.state().paired, false);assert.equal(r.events.length, 0);
  assert.notEqual(r.session, session);r.accept(gift);assert.equal(r.events.length, 0);
});

async function request(r, path, body, overrides = {}) {
  const req = { method: body ? 'POST' : 'GET', headers: { authorization: 'Bearer ' + r.token, 'x-race-room': 'room-1', 'x-race-owner': '123', 'x-race-place': RUNNER_PLACE_ID, ...overrides } };
  let result; const reply = (res, code, data) => result = { code, data };
  await r.handle(req, new URL(path, 'http://localhost'), {}, reply, async () => body, 'Connected');return result;
}
test('one paired room, exact place, separate credentials and cursor delivery', async () => {
  let now = 10000;const r = new RunnerRelay(() => now);r.configure(true, RUNNER_PLACE_ID);
  const body = { room: 'room-1', owner: 123, placeId: RUNNER_PLACE_ID };
  assert.equal((await request(r, '/game/pair', body, {authorization: 'Bearer wrong'})).code, 401);
  assert.equal((await request(r, '/game/pair', {...body, placeId: '999'})).code, 403);
  const paired = await request(r, '/game/pair', body);assert.equal(paired.code, 200);assert.equal(paired.data.protocol, 8);
  assert.equal(paired.data.settingsAuthority, 'game');
  assert.equal((await request(r, '/game/pair', { ...body, room: 'room-2' }, {'x-race-room': 'room-2'})).code, 409);
  r.accept(gift);
  const events = await request(r, '/game/events?after=0');assert.equal(events.data.events.length, 1);
  assert.equal((await request(r, '/game/events?after=1')).data.events.length, 0);
  assert.equal((await request(r, '/game/events?after=0', null, {'x-race-place': '999'})).code, 409);
  assert.equal((await request(r, '/game/events?after=0', null, {'x-race-room': 'room-2'})).code, 409);
  now += 30001;assert.equal((await request(r, '/game/events?after=0')).code, 409);
});
