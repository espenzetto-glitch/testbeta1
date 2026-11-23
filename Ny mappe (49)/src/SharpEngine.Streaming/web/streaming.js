// SharpEngine WebGL chunk streamer (plain ES module, no dependencies).
// Mirrors SharpEngine.Streaming: cell -> offset, radius load/unload with border hysteresis,
// async fetch + parse of scene files, objects created a few per frame, loaded-area clamp,
// distance-blended environment.
//
// Wire-up inside player.js (see the bottom of this file):
//   const s = new ChunkStreamer(layout, hooks, { radius: 1 });
//   each frame: s.update(cameraX, cameraZ);  camera = s.clampToLoaded(camera);  env = s.environmentAt(x, z, envOf, 30);

export class WorldLayout {
  // chunks: [{ scene, x, y, width, depth }], pivot: 'center' | 'corner'
  constructor(chunks, pivot = 'center', cellSize = null) {
    this.chunks = chunks;
    this.pivot = pivot;
    this.cellSize = cellSize ?? Math.max(1, ...chunks.map(c => Math.max(c.width, c.depth)));
    this.byCell = new Map();
    for (const c of chunks) {
      const k = `${c.x},${c.y}`;
      if (this.byCell.has(k)) throw new Error(`Cell (${k}) used twice`);
      this.byCell.set(k, c);
    }
  }
  offsetOf(c) { return { x: c.x * this.cellSize, y: 0, z: c.y * this.cellSize }; }
  cellAt(x, z) {
    const h = this.pivot === 'center' ? this.cellSize / 2 : 0;
    return { x: Math.floor((x + h) / this.cellSize), y: Math.floor((z + h) / this.cellSize) };
  }
  cellRect(cx, cy) {
    const cs = this.cellSize, ox = cx * cs, oz = cy * cs;
    return this.pivot === 'center'
      ? { minX: ox - cs / 2, minZ: oz - cs / 2, maxX: ox + cs / 2, maxZ: oz + cs / 2 }
      : { minX: ox, minZ: oz, maxX: ox + cs, maxZ: oz + cs };
  }
  rectOf(c) {
    const o = this.offsetOf(c);
    return this.pivot === 'center'
      ? { minX: o.x - c.width / 2, minZ: o.z - c.depth / 2, maxX: o.x + c.width / 2, maxZ: o.z + c.depth / 2 }
      : { minX: o.x, minZ: o.z, maxX: o.x + c.width, maxZ: o.z + c.depth };
  }
}

const inRect = (r, x, z) => x >= r.minX && x <= r.maxX && z >= r.minZ && z <= r.maxZ;
const rectDist = (r, x, z) => Math.hypot(x - Math.min(Math.max(x, r.minX), r.maxX), z - Math.min(Math.max(z, r.minZ), r.maxZ));
const cheb = (a, b) => Math.max(Math.abs(a.x - b.x), Math.abs(a.y - b.y));

// hooks = {
//   fetchScene(sceneName) -> Promise<sceneData>      (network + JSON.parse; runs async, never blocks a frame)
//   begin(chunk, sceneData, offset) -> build         (cheap: create the root only)
//   build.step(budget) -> { used, done }             (create up to `budget` objects, report how many)
//   build.dispose()                                  (remove root + everything under it)
// }
export class ChunkStreamer {
  constructor(layout, hooks, opts = {}) {
    this.layout = layout;
    this.hooks = hooks;
    this.radius = opts.radius ?? 1;
    this.unloadRadius = Math.max(opts.unloadRadius ?? this.radius, this.radius);
    this.hysteresis = opts.borderHysteresis ?? 2;
    this.objectsPerFrame = opts.objectsPerFrame ?? 64;
    this.maxParallel = opts.maxParallelFetches ?? 2;
    this.onLoaded = opts.onLoaded ?? (() => {});
    this.onUnloaded = opts.onUnloaded ?? (() => {});
    this.onError = opts.onError ?? ((c, e) => console.error('chunk', c.scene, e));
    this.slots = new Map();  // "x,y" -> { chunk, state, build }
    this.center = null;
    this.inFlight = 0;
    this.ready = [];         // fetched, waiting to be begun on the main loop
  }

  loadedChunks() { return [...this.slots.values()].filter(s => s.state === 'loaded').map(s => s.chunk); }
  get settled() { return [...this.slots.values()].every(s => s.state === 'loaded' || s.state === 'failed'); }

  update(px, pz) {
    const L = this.layout;
    const raw = L.cellAt(px, pz);
    if (!this.center) this.center = raw;
    else if (raw.x !== this.center.x || raw.y !== this.center.y) {
      const keep = L.cellRect(this.center.x, this.center.y);
      const h = this.hysteresis;
      if (!inRect({ minX: keep.minX - h, minZ: keep.minZ - h, maxX: keep.maxX + h, maxZ: keep.maxZ + h }, px, pz)) this.center = raw;
    }
    const c = this.center;

    for (const [k, s] of [...this.slots]) if (cheb({ x: s.chunk.x, y: s.chunk.y }, c) > this.unloadRadius) this._unload(k);

    const wanted = L.chunks
      .filter(ch => cheb({ x: ch.x, y: ch.y }, c) <= this.radius && !this.slots.has(`${ch.x},${ch.y}`))
      .sort((a, b) => cheb({ x: a.x, y: a.y }, c) - cheb({ x: b.x, y: b.y }, c));
    for (const ch of wanted) { if (this.inFlight >= this.maxParallel) break; this._fetch(ch); }

    for (const slot of this.ready.splice(0)) {
      if (this.slots.get(`${slot.chunk.x},${slot.chunk.y}`) !== slot) continue; // unloaded meanwhile
      try { slot.build = this.hooks.begin(slot.chunk, slot.data, L.offsetOf(slot.chunk)); slot.state = 'building'; }
      catch (e) { this._fail(slot, e); }
    }

    let budget = this.objectsPerFrame;
    const building = [...this.slots.values()].filter(s => s.state === 'building')
      .sort((a, b) => cheb({ x: a.chunk.x, y: a.chunk.y }, c) - cheb({ x: b.chunk.x, y: b.chunk.y }, c));
    for (const s of building) {
      if (budget <= 0) break;
      try {
        const r = s.build.step(budget);
        budget -= r.used;
        if (r.done) { s.state = 'loaded'; this.onLoaded(s.chunk); }
      } catch (e) { this._fail(s, e); }
    }
  }

  clear() { for (const k of [...this.slots.keys()]) this._unload(k); this.center = null; }

  loadedBounds() {
    let b = null;
    for (const ch of this.loadedChunks()) {
      const r = this.layout.rectOf(ch);
      b = b ? { minX: Math.min(b.minX, r.minX), minZ: Math.min(b.minZ, r.minZ), maxX: Math.max(b.maxX, r.maxX), maxZ: Math.max(b.maxZ, r.maxZ) } : r;
    }
    return b;
  }

  // Unchanged inside any loaded chunk; otherwise the closest point of the closest loaded chunk.
  clampToLoaded(x, z) {
    let best = null, bd = Infinity;
    for (const ch of this.loadedChunks()) {
      const r = this.layout.rectOf(ch);
      if (inRect(r, x, z)) return { x, z };
      const d = rectDist(r, x, z);
      if (d < bd) { bd = d; best = r; }
    }
    return best ? { x: Math.min(Math.max(x, best.minX), best.maxX), z: Math.min(Math.max(z, best.minZ), best.maxZ) } : { x, z };
  }

  // envOf(chunk) -> { sunIntensity, ambient, skyTop:[r,g,b], ... } (any numbers / numeric arrays); returns the blend.
  environmentAt(x, z, envOf, blendDistance, fallback = null) {
    const parts = this.loadedChunks().map(ch => ({ r: this.layout.rectOf(ch), e: envOf(ch) }));
    return blendEnvironment(x, z, parts, blendDistance, fallback);
  }

  _fetch(chunk) {
    const slot = { chunk, state: 'fetching', build: null, data: null };
    this.slots.set(`${chunk.x},${chunk.y}`, slot);
    this.inFlight++;
    this.hooks.fetchScene(chunk.scene).then(
      d => { this.inFlight--; slot.data = d; this.ready.push(slot); },
      e => { this.inFlight--; this._fail(slot, e); });
  }
  _unload(key) {
    const s = this.slots.get(key);
    if (!s) return;
    this.slots.delete(key);
    try { s.build?.dispose(); } catch { /* ignore */ }
    if (s.state === 'loaded') this.onUnloaded(s.chunk);
  }
  _fail(slot, e) {
    slot.state = 'failed';
    try { slot.build?.dispose(); } catch { /* ignore */ }
    slot.build = null;
    this.onError(slot.chunk, e);
  }
}

const smooth = t => { t = Math.min(Math.max(t, 0), 1); return t * t * (3 - 2 * t); };
const mix = (a, b, t) => Array.isArray(a) ? a.map((v, i) => v + (b[i] - v) * t) : a + (b - a) * t;

export function blendEnvironment(x, z, parts, blend, fallback = null) {
  if (parts.length === 0) return fallback;
  const nearest = () => parts.reduce((m, p) => (rectDist(p.r, x, z) < rectDist(m.r, x, z) ? p : m)).e;
  if (blend <= 0) return nearest();
  let acc = null, total = 0;
  for (const p of parts) {
    const w = smooth(1 - rectDist(p.r, x, z) / blend);
    if (w <= 0) continue;
    if (!acc) { acc = p.e; total = w; continue; }
    total += w;
    const t = w / total;
    const next = {};
    for (const k of Object.keys(acc)) next[k] = mix(acc[k], p.e[k], t);
    acc = next;
  }
  return acc ?? nearest();
}

// ---------------------------------------------------------------------------------------------
// Example wiring for the exported player.js (adapt names to your generated code):
//
//   const layout = new WorldLayout(DATA.worldMap.map(e => ({ scene: e.scene, x: e.x, y: e.y, width: e.width, depth: e.depth })));
//   const streamer = new ChunkStreamer(layout, {
//     fetchScene: async name => (await fetch(`scenes/${name}.json`)).json(),   // or read from data.js
//     begin: (chunk, data, off) => {
//       const root = new THREE_or_your_node(); root.position.set(off.x, off.y, off.z); scene.add(root);
//       let i = 0;
//       return {
//         step(budget) { let used = 0; while (used < budget && i < data.objects.length) { root.add(makeObject(data.objects[i++])); used++; }
//                        return { used, done: i >= data.objects.length }; },
//         dispose() { scene.remove(root); /* free geometries/textures */ },
//       };
//     },
//   }, { radius: 1 });
//   // per frame: streamer.update(cam.x, cam.z); ({x: cam.x, z: cam.z} = streamer.clampToLoaded(cam.x, cam.z));
