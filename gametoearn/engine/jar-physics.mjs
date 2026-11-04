// Gift jar physics shared by the OBS browser overlay (this file) and the Launcher (C# port in
// LauncherGteOverlay.cs). Jar shape and sizes follow the PTV Game To Earn PyQt jar.
// Position-based solver: gifts stack without sinking into each other, never pass through the glass,
// and when the jar is full the gifts above the rim spill over and slide down around the jar.
// Logical space 500 x 580; the jar stands on the ground at the bottom centre.
export const JAR = (() => {
  const W = 500, H = 580, JAR_W = 220, JAR_H = 280, cx = 250, jt = 290, jb = jt + JAR_H;
  const jx1 = cx - JAR_W / 2, jx2 = cx + JAR_W / 2, neckW = JAR_W - 48, nx1 = cx - neckW / 2, nx2 = cx + neckW / 2, neckH = 32;
  return { W, H, cx, jt, jb, jx1, jx2, nx1, nx2, neckH, size: 32, r: 15,
    inX1: jx1 + 16, inX2: jx2 - 16, floorIn: jb - 16, ground: jb, G: 0.42, MAX: 170, ITER: 10 };
})();

export class JarSim {
  constructor(rand = Math.random) { this.p = []; this.rand = rand; }
  get count() { return this.p.filter(q => !q.dying).length; }
  add(img) {
    const J = JAR, R = this.rand;
    const x = J.nx1 + 22 + R() * (J.nx2 - J.nx1 - 44);
    const y0 = -J.size - R() * 40;
    this.p.push({ x, y: y0, px: x, py: y0 - 3, rot: (R() - 0.5) * 70, vrot: (R() - 0.5) * 3,
      finalRot: (R() - 0.5) * 60, r: J.r, img, overflow: false, sleep: false, still: 0, rim: 0, alpha: 1, dying: false });
    for (const q of this.p) if (q.sleep && q.y < J.jt + 90) { q.sleep = false; q.still = 0; }
    // too many: the oldest spilled gift fades out (never pops away)
    if (this.p.filter(q => !q.dying).length > J.MAX) { const q = this.p.find(q => q.overflow && !q.dying) || this.p.find(q => !q.dying); if (q) q.dying = true; }
  }
  clear() { this.p.length = 0; }
  // inner glass at height y: neck -> rounded shoulder -> body
  static walls(y) {
    const J = JAR, top = J.jt + J.neckH;
    if (y < top) return [J.nx1 + 4, J.nx2 - 4];
    const t = Math.min(1, (y - top) / 36), e = 1 - (1 - t) * (1 - t);
    return [J.nx1 + 4 + (J.inX1 - J.nx1 - 4) * e, J.nx2 - 4 + (J.inX2 - J.nx2 + 4) * e];
  }
  static inside(q) {
    const J = JAR;
    if (q.y + q.r > J.floorIn) q.y = J.floorIn - q.r;
    if (q.y + q.r * 0.5 > J.jt) { const [a, b] = JarSim.walls(q.y); if (q.x - q.r < a) q.x = a + q.r; else if (q.x + q.r > b) q.x = b - q.r; }
    // rounded bottom corners
    for (const cxr of [J.inX1 + 22, J.inX2 - 22]) {
      const cy = J.floorIn - 22, dx = q.x - cxr, dy = q.y - cy;
      if (dy > 0 && (cxr < J.cx ? dx < 0 : dx > 0)) { const d = Math.hypot(dx, dy), lim = 22 - q.r; if (d > lim && lim > 0) { q.x = cxr + dx / d * lim; q.y = cy + dy / d * lim; } }
    }
  }
  // solid jar seen from outside: lid + body (rounded rectangle approximated by boxes)
  static outside(q) {
    const J = JAR; let onTop = false;
    for (const [x1, y1, x2, y2] of [[J.nx1 - 4, J.jt, J.nx2 + 4, J.jt + J.neckH], [J.jx1, J.jt + J.neckH, J.jx2, J.jb]]) {
      const cx = Math.max(x1, Math.min(q.x, x2)), cy = Math.max(y1, Math.min(q.y, y2));
      const dx = q.x - cx, dy = q.y - cy, d = Math.hypot(dx, dy);
      if (d >= q.r) continue;
      if (d === 0) { const l = q.x - x1, r = x2 - q.x, t = q.y - y1, m = q.py < y1 ? t : Math.min(l, r, t); if (m === t) { q.y = y1 - q.r; onTop = true; } else if (m === l) q.x = x1 - q.r; else q.x = x2 + q.r; continue; }
      q.x += dx / d * (q.r - d); q.y += dy / d * (q.r - d); if (dy / d < -0.6) onTop = true;
    }
    if (q.y + q.r > J.ground) q.y = J.ground - q.r;
    return onTop;
  }
  step() {
    const J = JAR;
    let removed = false;
    for (const q of this.p) if (q.dying) { q.alpha -= 1 / 40; if (q.alpha <= 0) removed = true; }
    if (removed) { this.p = this.p.filter(q => q.alpha > 0); for (const q of this.p) { q.sleep = false; q.still = 0; } }
    const P = this.p, n = P.length;
    for (const q of P) {
      if (q.sleep) { q.px = q.x; q.py = q.y; continue; }
      let vx = (q.x - q.px) * (q.overflow ? 0.985 : 0.94), vy = (q.y - q.py) * 0.995 + J.G;
      const sp = Math.hypot(vx, vy); if (sp > 14) { vx *= 14 / sp; vy *= 14 / sp; }
      q.px = q.x; q.py = q.y; q.x += vx; q.y += vy; q.rot += q.vrot; q.vrot *= 0.92;
    }
    // spatial hash for neighbour pairs
    const cell = 32, grid = new Map(), key = (i, j) => i * 1000 + j;
    for (let i = 0; i < n; i++) { const q = P[i], k = key(Math.floor(q.x / cell), Math.floor(q.y / cell)); let a = grid.get(k); if (!a) grid.set(k, a = []); a.push(i); }
    const pairs = [];
    for (let i = 0; i < n; i++) {
      const q = P[i], gx = Math.floor(q.x / cell), gy = Math.floor(q.y / cell);
      for (let ox = -1; ox <= 1; ox++) for (let oy = -1; oy <= 1; oy++) { const a = grid.get(key(gx + ox, gy + oy)); if (a) for (const j of a) if (j > i) pairs.push(i, j); }
    }
    for (let it = 0; it < J.ITER; it++) {
      for (let k = 0; k < pairs.length; k += 2) {
        const a = P[pairs[k]], b = P[pairs[k + 1]];
        const dx = a.x - b.x, dy = a.y - b.y, d = Math.hypot(dx, dy) || 0.01, min = a.r + b.r;
        if (d >= min || (a.sleep && b.sleep && min - d < 3)) continue;
        const ov = min - d, nx = dx / d, ny = dy / d;
        // the lower gift carries the weight; sleeping gifts are (almost) fixed
        let wa = a.sleep ? 0.05 : (a.y < b.y ? 0.65 : 0.35), wb = b.sleep ? 0.05 : 1 - (a.sleep ? 0.05 : wa);
        if (a.sleep) wb = 0.95; const s = wa + wb; wa /= s; wb /= s;
        a.x += nx * ov * wa; a.y += ny * ov * wa; b.x -= nx * ov * wb; b.y -= ny * ov * wb;
        if (ov > 3) { if (a.sleep) { a.sleep = false; a.still = 0; } if (b.sleep) { b.sleep = false; b.still = 0; } }
      }
      for (const q of P) {
        if (q.overflow) { if (JarSim.outside(q) && it === J.ITER - 1) q.x += q.x < J.cx ? -0.6 : 0.6; }
        else JarSim.inside(q);
        if (q.x - q.r < 0) q.x = q.r; else if (q.x + q.r > J.W) q.x = J.W - q.r;
      }
    }
    for (const q of P) {
      if (q.sleep) continue;
      // pushes from the solver must not shoot gifts around: limit the velocity they create
      if (q.y - q.py < -2.5) q.py = q.y + 2.5;
      const hx = q.x - q.px; if (Math.abs(hx) > 5) q.px = q.x - Math.sign(hx) * 5;
      // full jar: a gift resting above the rim spills over the side
      if (!q.overflow) {
        // above the rim but outside the opening = it is lying on the glass edge -> spills over (no snapping inside)
        if (q.y < J.jt + q.r && (q.x < J.nx1 + 4 || q.x > J.nx2 - 4)) { q.overflow = true; q.rim = 0; }
        else {
          const slow = Math.abs(q.y - q.py) < 0.8;
          q.rim = q.y < J.jt - 4 && slow ? q.rim + 1 : 0;
          if (q.rim > 6) { q.overflow = true; q.px = q.x - (q.x < J.cx ? -1.2 : 1.2); }
        }
      } else if (q.x > J.nx1 + 4 + q.r && q.x < J.nx2 - 4 - q.r && q.y > J.jt - q.r + 6 && q.y < J.jt + J.neckH) {
        q.overflow = false; q.rim = 0;   // there is room again in the opening: fall back into the jar
      }
      const moving = Math.hypot(q.x - q.px, q.y - q.py);
      const restless = q.overflow ? q.y < J.jt + 6 && q.x > J.nx1 - 24 && q.x < J.nx2 + 24 : q.y < J.jt;
      if (!restless && moving < 0.12) { if (++q.still > 8) { q.sleep = true; } } else q.still = 0;
      if (q.sleep || moving < 0.3) { const df = q.finalRot - q.rot; q.rot += Math.abs(df) > 0.5 ? df * 0.15 : df; q.vrot = 0; }
    }
  }
}
