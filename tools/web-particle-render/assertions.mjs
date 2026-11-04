// Pure checks of recorded framebuffer observations; no renderer substitute.
export function mismatchesOf(observations) {
	const mismatches = [];
	const byName = Object.fromEntries(observations.map(item => [item.name, item]));
	const rgb = item => Array.from({ length: 9 }, (_, i) => item.pixels.slice(i * 4, i * 4 + 3));
	const check = (condition, message) => { if (!condition) mismatches.push(message); };
	const same = (a, b) => rgb(byName[a]).every((pixel, i) => pixel.every((value, c) => Math.abs(value - rgb(byName[b])[i][c]) <= 2));
	const black = name => rgb(byName[name]).every(pixel => pixel.every(value => value <= 2));
	for (const item of observations) check(item.corner.slice(0, 3).every(value => value <= 2), `${item.name}: background corner is not black`);
	check(rgb(byName.redFF).every(([r, g, b]) => r >= 200 && g <= 2 && b <= 2), 'Opaque red control is not visible');
	check(rgb(byName.blueFF).every(([r, g, b]) => b >= 200 && r <= 2 && g <= 2), 'Opaque blue control is not visible');
	for (const name of ['red0', 'redF0', 'nearZeroThenFar', 'nearF0ThenFar', 'nearThenFar', 'farThenNear']) check(same(name, 'redFF'), `${name}: expected opaque red`);
	check(rgb(byName.redEF).every(([r, g, b], i) => r > 0 && r < rgb(byName.redFF)[i][0] - 2 && g <= 2 && b <= 2), 'alphaEF must remain fractional');
	for (const name of ['noWriteZeroThenFar', 'noWriteNearThenFar', 'interleaved', 'screenThenScreen']) check(same(name, 'blueFF'), `${name}: expected blue`);
	// an alpha-0 particle fails the blended states' alpha test (0x10): additive ones are invisible
	for (const name of ['additive0', 'oneOne0']) check(black(name), `${name}: existing transparent control changed`);
	// 0x57C6FA: an untextured particle of alpha 0 or >= 0xF0 is the opaque state 0x1256, screen-space ones too
	for (const name of ['screen0', 'screenFF']) check(same(name, 'redFF'), `${name}: expected opaque red`);
	return mismatches;
}
