import crypto from 'node:crypto';

export const RUNNER_PLACE_ID = '71880688656931';
export const RUNNER_PROTOCOL = 8;
const secureEqual = (a, b) => {
  const x = Buffer.from(String(a || '')), y = Buffer.from(String(b || ''));
  return x.length === y.length && crypto.timingSafeEqual(x, y);
};
const parseAccount = text => {
  if (typeof text !== 'string' || text.length > 100) return null;
  const account = text.trim().replace(/^(?:rbx|roblox|join|!join)\s+/i, '').replace(/^@/, '');
  return /^[1-9]\d{0,14}$/.test(account) || /^[A-Za-z0-9_]{3,20}$/.test(account) ? account : null;
};

// The relay never reads skill settings or translates gifts into keyboard input.
// The one paired PTVRunner server owns all skill mappings and counters.
export class RunnerRelay {
  constructor(now = () => Date.now()) {
    this.now = now; this.token = crypto.randomBytes(32).toString('hex');
    this.enabled = false; this.room = null; this.events = []; this.sequence = 0;
    this.session = crypto.randomUUID(); this.joins = new Map();
  }
  configure(enabled, placeId) {
    if (enabled && String(placeId) !== RUNNER_PLACE_ID) throw Object.assign(Error('PTVRunner is the only supported signal map'), { status: 400 });
    if (this.enabled !== !!enabled) {
      this.enabled = !!enabled; this.room = null; this.events = []; this.sequence = 0;
      this.joins.clear(); this.session = crypto.randomUUID();
    }
    return this.state();
  }
  state() {
    const paired = !!this.room && this.now() - this.room.at < 30000;
    return { enabled: this.enabled, placeId: RUNNER_PLACE_ID, protocol: RUNNER_PROTOCOL,
      paired, owner: paired ? this.room.owner : null, sequence: this.sequence, session: this.session };
  }
  accept(event) {
    if (!this.enabled || event.demo || event.method === 'test' || event.test) return;
    const viewer = String(event.viewer || '').replace(/^@/, '');
    if (!viewer || viewer === 'unknown') return;
    const e = { kind: event.kind, viewer, viewerDisplayName: String(event.name || viewer), receivedAt: event.at || this.now() };
    if (event.kind === 'gift') {
      if (!/^[a-f0-9]{16}$/.test(event.giftKey || '')) return;
      Object.assign(e, { giftKey: event.giftKey, giftName: String(event.giftName || ''), amount: event.amount });
    } else if (event.kind === 'like') e.amount = event.amount;
    else if (event.kind === 'chat') {
      const account = parseAccount(event.comment);
      if (!account || this.now() - (this.joins.get(viewer) ?? -Infinity) < 15000) return;
      this.joins.set(viewer, this.now());
      if (this.joins.size > 10000) this.joins.delete(this.joins.keys().next().value);
      e.kind = 'join'; e.account = account;
    } else if (event.kind !== 'follow') return;
    if (e.kind === 'gift' || e.kind === 'like') {
      if (!Number.isSafeInteger(e.amount) || e.amount < 1) return;
    }
    this.events.push({ ...e, seq: ++this.sequence });
    if (this.events.length > 5000) this.events.shift();
  }
  check(req) {
    const r = this.room;
    if (!r || r.id !== req.headers['x-race-room'] || r.owner !== Number(req.headers['x-race-owner']) ||
        req.headers['x-race-place'] !== RUNNER_PLACE_ID || this.now() - r.at >= 30000) return false;
    r.at = this.now(); return true;
  }
  async handle(req, url, res, reply, body, liveStatus) {
    if (!secureEqual(req.headers.authorization, 'Bearer ' + this.token)) return reply(res, 401, { error: 'unauthorized' });
    if (!this.enabled) return reply(res, 409, { error: 'Select PTVRunner in Game To Earn first' });
    if (req.method === 'POST' && url.pathname === '/game/pair') {
      const b = await body(req, 2048);
      if (String(b.placeId) !== RUNNER_PLACE_ID || req.headers['x-race-place'] !== RUNNER_PLACE_ID)
        return reply(res, 403, { error: 'This relay is only for PTVRunner' });
      if (typeof b.room !== 'string' || !b.room || b.room.length > 100 || !Number.isSafeInteger(b.owner) || b.owner < 1 ||
          b.room !== req.headers['x-race-room'] || b.owner !== Number(req.headers['x-race-owner']))
        return reply(res, 400, { error: 'Invalid room' });
      if (this.room && this.now() - this.room.at < 30000 && (this.room.id !== b.room || this.room.owner !== b.owner))
        return reply(res, 409, { error: 'Already paired with another active room' });
      this.room = { id: b.room, owner: b.owner, at: this.now() };
      return reply(res, 200, { ok: true, ...this.state(), status: liveStatus, settingsAuthority: 'game' });
    }
    if (!this.check(req)) return reply(res, 409, { error: 'Pair this game room first' });
    if (req.method === 'POST' && url.pathname === '/game/unpair') {
      this.room = null; return reply(res, 200, { ok: true });
    }
    if (req.method === 'GET' && url.pathname === '/game/events') {
      const after = Number(url.searchParams.get('after') || 0);
      if (!Number.isSafeInteger(after) || after < 0) return reply(res, 400, { error: 'Invalid cursor' });
      return reply(res, 200, { status: liveStatus, session: this.session, settingsAuthority: 'game',
        placeId: RUNNER_PLACE_ID, events: this.events.filter(e => e.seq > after).slice(0, 100) });
    }
    return reply(res, 404, { error: 'not found' });
  }
}
