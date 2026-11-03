// Gift recognition: TikTok gift id (cached) -> picture fingerprint -> name.
// Uses the same fingerprints / matching rules as TikTokBridge (image-match.mjs + gift-catalog.json).
import fs from 'node:fs';

export const normName = s => String(s || '').toLowerCase().normalize('NFKD').replace(/[‘’'`´]/g, '').replace(/[^a-z0-9]+/g, '');
const CDN = /(^|\.)(tiktokcdn[^.]*\.com|byteoversea\.com|ibytedtos\.com|tiktokcdn-eu\.com|ttwstatic\.com)$/;

export class GiftMatcher {
  constructor(catalogFile, idsFile) {
    this.catalog = JSON.parse(fs.readFileSync(catalogFile, 'utf8'));
    this.byKey = new Map(this.catalog.map(g => [g.key, g]));
    this.names = new Map();
    for (const g of this.catalog) { const n = normName(g.name); if (!this.names.has(n)) this.names.set(n, g); }
    this.idsFile = idsFile;
    try { this.ids = JSON.parse(fs.readFileSync(idsFile, 'utf8')); } catch { this.ids = {}; }
    this.urls = new Map();
    this.img = null;
  }
  byName(name) { return this.names.get(normName(name)) || null; }
  async imageMatch(url) {
    if (!this.img) this.img = await import('./image-match.mjs');
    const response = await fetch(url, { signal: AbortSignal.timeout(5000), redirect: 'error' });
    if (!response.ok) throw Error('image unavailable');
    const chunks = []; let length = 0;
    for await (const chunk of response.body) { length += chunk.length; if (length > 8 * 1024 * 1024) throw Error('image too large'); chunks.push(chunk); }
    return this.img.matchFingerprint(await this.img.fingerprint(Buffer.concat(chunks)), this.catalog);
  }
  async resolve(giftId, urls, rawName) {
    const id = String(giftId || '');
    if (id && this.byKey.has(this.ids[id])) return { key: this.ids[id], method: 'gift-id' };
    let result = null;
    for (const url of (urls || []).slice(0, 3)) {
      try {
        const u = new URL(url); if (u.protocol !== 'https:' || !CDN.test(u.hostname)) continue;
        if (!this.urls.has(url)) { this.urls.set(url, this.imageMatch(url).catch(() => null)); if (this.urls.size > 1000) this.urls.delete(this.urls.keys().next().value); }
        result = await this.urls.get(url); if (result) break;
      } catch {}
    }
    if (!result) { const g = this.byName(rawName); if (g) result = { key: g.key, method: 'name' }; }
    if (id && result) { this.ids[id] = result.key; try { fs.writeFileSync(this.idsFile, JSON.stringify(this.ids)); } catch {} }
    return result;
  }
}
