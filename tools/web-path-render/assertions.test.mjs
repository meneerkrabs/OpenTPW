import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mismatchesOf, names } from './assertions.mjs';

// Fabricated records validate ONLY the assertion logic, never path rendering.
const rgba = rgb => Array.from({ length: 9 }, () => [...rgb, 255]).flat();
function fixture() {
  return names.map((name, i) => {
    const suspended = name.includes('suspended') || name.endsWith('-static');
    const first = name.endsWith('-first'), red = name.startsWith('a-');
    const state = { pathCells: 2, staticPaths: suspended ? 1 : 0, staticStale: false, groundStale: false,
      proceduralHidden: suspended, staticGroundHidden: !suspended, allStaticPathsVisible: suspended,
      outsidePathsVisible: !suspended, builds: i + 1 };
    return { name, ordinal: i + 1, frame: i + 1, levelPass: i + 1, width: 320, height: 200, suspended,
      before: first ? { ...state, staticPaths: 0, staticStale: true, groundStale: true } : state, after: state,
      corner: [0, 0, 0, 255], change: ['b-procedural', 'a-restored-procedural'].includes(name)
        ? { total: 1, done: 1, sameDimensions: true, nativeIdentityChanged: true } : null,
      samples: ['inside', 'outside', 'ground', 'previous'].map(role => ({ role, u: .5, v: .5,
        pixels: rgba(role === 'ground' || (role === 'previous' && i >= 5) ? [96, 96, 96] : red ? [224, 32, 32] : [32, 32, 224]) })) };
  });
}

test('complete observation contract is accepted', () => assert.deepEqual(mismatchesOf(fixture()), []));
for (const [label, mutate] of [
  ['stale same-size art', x => { x[1].samples[0].pixels = rgba([224, 32, 32]); }],
  ['missing first suspended path', x => { x[2].samples[0].pixels = rgba([0, 0, 0]); }],
  ['missing newly added path on first suspended frame', x => { x[6].samples[0].pixels = rgba([0, 0, 0]); }],
  ['vacated cell hole', x => { x[6].samples[3].pixels = rgba([0, 0, 0]); }],
  ['stale removed path', x => { x[6].samples[3].pixels = rgba([32, 32, 224]); }],
  ['unrelated static-only fixture', x => { x[1].after.proceduralHidden = true; }],
  ['empty fake switch notification', x => { x[1].change.total = 0; }],
  ['extra warm-up frame', x => { x[2].frame++; }],
  ['lazy batch already prepared', x => { x[2].before.staticPaths = 1; }],
  ['missing readback', x => { delete x[0].samples[0].pixels; }],
  ['duplicate case', x => { x[1] = x[0]; }],
  ['missing case', x => { x.pop(); }],
  ['all black framebuffer', x => { for (const frame of x) for (const sample of frame.samples) sample.pixels = rgba([0, 0, 0]); }],
]) test(`rejects ${label}`, () => { const x = fixture(); mutate(x); assert.ok(mismatchesOf(x).length > 0); });
