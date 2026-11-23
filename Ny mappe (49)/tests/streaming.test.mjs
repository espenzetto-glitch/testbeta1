// node tests/streaming.test.mjs
import assert from 'node:assert/strict';
import { WorldLayout, ChunkStreamer, blendEnvironment } from '../src/SharpEngine.Streaming/web/streaming.js';

let passed = 0;
const test = async (name, fn) => { await fn(); passed++; console.log('  ok   ', name); };
const grid = (n, size = 60) => new WorldLayout(Array.from({ length: n * n }, (_, i) => ({ scene: `S${i % n}_${Math.floor(i / n)}`, x: i % n, y: Math.floor(i / n), width: size, depth: size })));
const tick = () => new Promise(r => setTimeout(r, 0));

function make(layout, opts = {}, perChunk = 10) {
  const world = new Set(), offsets = {};
  const hooks = {
    fetchScene: async name => ({ name }),
    begin: (chunk, data, off) => {
      world.add(chunk.scene); offsets[chunk.scene] = off; let made = 0;
      return { step(b) { const u = Math.min(b, perChunk - made); made += u; return { used: u, done: made >= perChunk }; }, dispose() { world.delete(chunk.scene); } };
    },
  };
  return { s: new ChunkStreamer(layout, hooks, { maxParallelFetches: 100, objectsPerFrame: 1000, ...opts }), world, offsets };
}
async function settle(s, x, z) { for (let i = 0; i < 20; i++) { s.update(x, z); await tick(); } }

await test('3x3 loads around the player at the right offsets', async () => {
  const { s, world, offsets } = make(grid(5));
  await settle(s, 120, 120);
  assert.equal(s.loadedChunks().length, 9);
  assert.ok(world.has('S1_1') && world.has('S3_3') && !world.has('S0_0'));
  assert.deepEqual(offsets['S3_3'], { x: 180, y: 0, z: 180 });
});
await test('crossing a border loads/unloads', async () => {
  const { s, world } = make(grid(5));
  await settle(s, 60, 120); assert.ok(world.has('S0_2') && !world.has('S3_2'));
  await settle(s, 120, 120); assert.ok(!world.has('S0_2') && world.has('S3_2'));
});
await test('hysteresis stops flicker', async () => {
  let loads = 0, unloads = 0;
  const { s } = make(grid(5), { onLoaded: () => loads++, onUnloaded: () => unloads++ });
  await settle(s, 89, 120); const l0 = loads, u0 = unloads;
  for (let i = 0; i < 20; i++) { s.update(90.5, 120); s.update(89.5, 120); await tick(); }
  assert.equal(loads, l0); assert.equal(unloads, u0);
  await settle(s, 93, 120); assert.ok(loads > l0);
});
await test('radius 2 = 5x5', async () => {
  const { s } = make(grid(7), { radius: 2 }); await settle(s, 180, 180); assert.equal(s.loadedChunks().length, 25);
});
await test('object budget spreads work over frames', async () => {
  const { s } = make(grid(1), { objectsPerFrame: 10 }, 25);
  let frames = 0; while (!s.settled || s.loadedChunks().length === 0) { s.update(0, 0); frames++; await tick(); assert.ok(frames < 50); }
  assert.ok(frames >= 3);
});
await test('clamp: free across loaded borders, stops at loaded edge', async () => {
  const { s } = make(grid(3)); await settle(s, 0, 0);
  assert.deepEqual(s.clampToLoaded(55, 10), { x: 55, z: 10 });
  assert.deepEqual(s.clampToLoaded(-500, 0), { x: -30, z: 0 });
});
await test('environment is continuous and local deep inside', async () => {
  const l = grid(2);
  const day = { sun: 1, sky: [0.2, 0.4, 1] }, night = { sun: 0.1, sky: [0, 0, 0.1] };
  const parts = l.chunks.map(c => ({ r: l.rectOf(c), e: c.x === 0 ? day : night }));
  assert.equal(blendEnvironment(0, 0, parts, 10).sun, 1);
  assert.equal(blendEnvironment(60, 0, parts, 10).sun, 0.1);
  assert.ok(Math.abs(blendEnvironment(30, 0, parts, 10).sun - 0.55) < 0.01);
  let prev = 1; for (let x = 15; x <= 45; x += 0.25) { const v = blendEnvironment(x, 0, parts, 10).sun; assert.ok(v <= prev + 1e-9 && prev - v < 0.05); prev = v; }
});
await test('failed fetch is reported once', async () => {
  const errs = [];
  const s = new ChunkStreamer(grid(1), { fetchScene: async () => { throw new Error('404'); }, begin() {} }, { onError: (c, e) => errs.push(e.message) });
  for (let i = 0; i < 10; i++) { s.update(0, 0); await tick(); }
  assert.deepEqual(errs, ['404']);
});
console.log(`\n${passed} passed`);
