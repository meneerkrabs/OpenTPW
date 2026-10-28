// Assertions over observations, not a renderer substitute. Unit-test fixtures are NOT GPU evidence.
export const names = ['a-procedural', 'b-procedural', 'b-suspended-first', 'b-suspended-second',
  'b-resumed', 'b-edit-procedural', 'b-edit-suspended-first', 'b-edit-suspended-second',
  'a-restored-procedural', 'a-restored-static'];

export function mismatchesOf(observations) {
  const errors = [];
  const check = (ok, message) => { if (!ok) errors.push(message); };
  const byName = new Map(observations.map(item => [item.name, item]));
  check(observations.length === names.length && new Set(observations.map(x => x.name)).size === names.length,
    'Expected each declared case exactly once');
  const pixels = sample => sample?.pixels?.length === 36
    ? Array.from({ length: 9 }, (_, i) => sample.pixels.slice(i * 4, i * 4 + 4)) : [];
  const sampleOf = (item, role) => item?.samples?.find(x => x.role === role);
  const same = (a, b, role) => {
    const x = pixels(sampleOf(a, role)), y = pixels(sampleOf(b, role));
    return x.length === 9 && y.length === 9 && x.every((pixel, i) => pixel.every((c, j) => Math.abs(c - y[i][j]) <= 2));
  };
  for (const [i, name] of names.entries()) {
    const item = byName.get(name);
    check(!!item, `Missing ${name}`);
    if (!item) continue;
    check(observations[i]?.name === name && item.ordinal === i + 1 && item.levelPass === i + 1 && item.frame === i + 1,
      `${name}: sequence/frame count must not hide warm-up or second-frame draws`);
    check(Number.isInteger(item.width) && item.width > 0 && Number.isInteger(item.height) && item.height > 0,
      `${name}: missing framebuffer dimensions`);
    check(item.corner?.length === 4 && item.corner.slice(0, 3).every(x => Number.isInteger(x) && x >= 0 && x <= 2),
      `${name}: missing/changed black background control`);
    const state = item.after;
    check(!!state && state.pathCells === 2 && state.proceduralHidden === item.suspended
      && state.staticGroundHidden === !item.suspended && state.allStaticPathsVisible === item.suspended
      && state.outsidePathsVisible === !item.suspended, `${name}: unexpected real terrain representation`);
    const red = name.startsWith('a-');
    for (const role of ['inside', 'outside', 'ground', 'previous']) {
      const sample = sampleOf(item, role), data = pixels(sample);
      check(data.length === 9 && data.every(p => p.every(x => Number.isInteger(x) && x >= 0 && x <= 255)), `${name}/${role}: invalid 3x3 RGBA readback`);
      if (data.length !== 9) continue;
      check(sample.u > 0 && sample.u < 1 && sample.v > 0 && sample.v < 1, `${name}/${role}: sample outside viewport`);
      const neutral = role === 'ground' || (role === 'previous' && i >= 5);
      if (neutral) {
        check(data.every(([r, g, b]) => r >= 20 && r <= 180 && Math.max(r, g, b) - Math.min(r, g, b) <= 4),
          `${name}/${role}: ground must remain visible and neutral, not black or stale path art`);
      } else {
        check(data.every(([r, g, b]) => red ? r >= 150 && r - g >= 100 && r - b >= 100 : b >= 150 && b - g >= 100 && b - r >= 100),
          `${name}/${role}: ${red ? 'A/red' : 'B/blue'} path missing or stale`);
      }
    }
    if (!item.suspended) check(Number.isInteger(state?.builds) && state.builds > 0, `${name}: no real procedural uploads`);
    if (name === 'b-procedural' || name === 'a-restored-procedural') {
      check(item.change?.total === 1 && item.change.done === 1 && item.change.sameDimensions === true
        && item.change.nativeIdentityChanged === true, `${name}: real GPU pack replacement not proven`);
    }
  }
  for (const name of ['b-suspended-first', 'b-edit-suspended-first']) {
    const item = byName.get(name);
    check(item?.before?.staticPaths === 0 && item.before.staticStale === true && item.before.groundStale === true,
      `${name}: lazy static path/ground precondition not exercised`);
    check(item?.after?.staticPaths > 0 && item.after.staticStale === false && item.after.groundStale === false,
      `${name}: lazy static paths/ground were not prepared`);
    const second = byName.get(name.replace('-first', '-second'));
    for (const role of ['inside', 'outside', 'ground', 'previous']) check(same(item, second, role), `${name}/${role}: first frame differs from settled second frame`);
  }
  check((byName.get('b-procedural')?.after?.builds ?? 0) > (byName.get('a-procedural')?.after?.builds ?? Infinity),
    'B pack did not rebuild the procedural atlas');
  check((byName.get('a-restored-procedural')?.after?.builds ?? 0) > (byName.get('b-edit-procedural')?.after?.builds ?? Infinity),
    'A restoration did not rebuild the procedural atlas');
  return errors;
}
