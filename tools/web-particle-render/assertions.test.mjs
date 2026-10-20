// Validation of the oracle's acceptance rules, not a GPU/rendered pass.
import assert from 'node:assert/strict';
import { mismatchesOf } from './assertions.mjs';
const red = [255, 0, 0], blue = [0, 0, 255], black = [0, 0, 0];
const expected = {
	red0: red, redEF: [239, 0, 0], redF0: red, redFF: red, blueFF: blue,
	additive0: black, oneOne0: black, nearThenFar: red, farThenNear: red,
	nearZeroThenFar: red, nearF0ThenFar: red, noWriteZeroThenFar: blue, noWriteNearThenFar: blue, interleaved: blue, screen0: black, screenFF: red, screenThenScreen: blue,
};
const observations = colours => Object.entries(colours).map(([name, colour]) => ({ name,
	pixels: Array.from({ length: 9 }, () => [...colour, 255]).flat(), corner: [0, 0, 0, 255] }));
assert.deepEqual(mismatchesOf(observations(expected)), []);
assert(mismatchesOf(observations(Object.fromEntries(Object.keys(expected).map(name => [name, black])))).length > 0, 'a blank frame cannot pass');
for (const [name, colour] of [['red0', black], ['redF0', [240, 0, 0]], ['nearThenFar', blue], ['noWriteNearThenFar', red], ['interleaved', [0, 255, 255]], ['screenFF', black]])
	assert(mismatchesOf(observations({ ...expected, [name]: colour })).length > 0, `${name} regression must fail`);
const badBackground = observations(expected);
badBackground[0].corner = [50, 0, 0, 255];
assert(mismatchesOf(badBackground).length > 0);
console.log('Particle oracle acceptance-rule checks passed (no GPU execution).');
