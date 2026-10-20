// Unit tests of the cross-runtime digest comparison (tools/web-determinism/compare-digest.mjs).
import assert from 'node:assert/strict';
import test from 'node:test';
import { compareTheme, themesOf } from './web-determinism/compare-digest.mjs';

const theme = (overrides = {}) => ({
	theme: 'jungle', fixedRun: 'A', fixedRunWithStaff: 'B', weather: 'C',
	m4: { digest: 'D', count: 2, hashes: [['jungle/x minute 1', '01'], ['jungle/x minute 2', '02']] }, ...overrides,
});

test('equal digests have no differences', () => {
	assert.deepEqual(compareTheme(theme(), theme()), []);
});

test('a differing part is named with both values', () => {
	assert.deepEqual(compareTheme(theme(), theme({ weather: 'X' })), [{ part: 'weather', left: 'C', right: 'X' }]);
});

test('a differing M4 list names its first differing state', () => {
	const other = theme({ m4: { digest: 'E', count: 2, hashes: [['jungle/x minute 1', '01'], ['jungle/x minute 2', '03']] } });
	const [difference] = compareTheme(theme(), other);
	assert.equal(difference.part, 'm4');
	assert.deepEqual(difference.firstDifference, { left: ['jungle/x minute 2', '02'], right: ['jungle/x minute 2', '03'] });
});

test('both report shapes are read', () => {
	assert.ok(themesOf({ themes: [theme()] }).has('jungle'));
	assert.ok(themesOf({ results: { determinism: { themes: [theme()] } } }).has('jungle'));
	assert.throws(() => themesOf({}), /no themes/);
});
