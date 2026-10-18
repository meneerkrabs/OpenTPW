import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../source/OpenTPW.Web/wwwroot/opentpw-text.js', import.meta.url), 'utf8');
const { normalizeCompatibility } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

test('browser identity normalization preserves NFKC without changing casing', () => {
	for (const [input, expected] of [['Bob', 'Bob'], ['Ｂｏｂ', 'Bob'], ['ﬃab', 'ffiab'], ['Straße', 'Straße'], ['Ju\u0308rgen', 'Jürgen']])
		assert.equal(normalizeCompatibility(input), expected);
});

test('the page registers the text module before starting the .NET runtime', async () => {
	const main = await readFile(new URL('../source/OpenTPW.Web/wwwroot/main.js', import.meta.url), 'utf8');
	assert.match(main, /import \* as text from '\.\/opentpw-text\.js';/);
	const registration = main.indexOf("setModuleImports('opentpw-text', text)");
	assert.ok(registration >= 0 && registration < main.indexOf('await runMain()'));
});
