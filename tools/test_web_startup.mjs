import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../source/OpenTPW.Web/wwwroot/opentpw-startup.js', import.meta.url), 'utf8');
const startup = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const file = path => [path, async () => new Uint8Array([1]).buffer, 1];
const paths = files => files.map(([path]) => path);

for (const theme of ['arabian', 'science', 'water']) {
	test(`the selected ${theme} theme is imported before edition detection`, () => {
		const files = [file('Data/Ver.txt'), file('Data/levels/Standard.sam'), file(`Data/levels/${theme}/terrain.wad`),
			file('Data/levels/jungle/terrain.wad'), file('Data/movies/start.mpg')];
		const plan = startup.plan(files, theme);
		assert.deepEqual(paths(plan.start), paths(files.slice(0, 3)));
		assert.deepEqual(paths(plan.levels), ['Data/levels/jungle/terrain.wad']);
	});
}

test('a missing selected theme refuses before an import can begin', () => {
	assert.throws(() => startup.plan([file('Data/Ver.txt')], 'water'), /water/i);
});

test('an arbitrary theme is not an experimental entry route', () => {
	assert.throws(() => startup.plan([file('Data/Ver.txt')], '../water'), /theme/i);
});

test('the TPW lobby retains deferred themes and excludes movies', () => {
	const files = [file('Data/global/a.sam'), file('Data/levels/jungle/Easymode.TPWI'), file('Data/levels/jungle/terrain.wad'), file('Data/movies/a.mpg')];
	const plan = startup.plan(files);
	assert.deepEqual(paths(plan.start), paths(files.slice(0, 2)));
	assert.deepEqual(paths(plan.levels), ['Data/levels/jungle/terrain.wad']);
});

function runtime() {
	return {
		files: new Map([['Data/levels/jungle/stale.wad', 99]]), loaded: new Set(['jungle']), active: false,
		config: new Map([['language.json', 'French']]), save: new Map([['layout.json', 'checkpoint']]),
		BeginImport() {
			if (this.active) throw new Error('Reload before changing installation');
			this.files.clear(); this.loaded.clear();
		},
		LevelLoaded(theme) { this.loaded.add(theme); },
	};
}

test('a failed partial theme import is not loaded; a retry starts fresh', async () => {
	const program = runtime();
	const files = startup.plan([file('Data/Ver.txt'), file('Data/levels/water/terrain.wad')], 'water');
	await assert.rejects(startup.importStartup(program, files, async sources => {
		program.files.set(sources[0][0], 1);
		throw new Error('read failed');
	}), /read failed/);
	assert.equal(program.loaded.size, 0);
	assert.equal(program.files.has('Data/levels/jungle/stale.wad'), false);
	await startup.importStartup(program, files, async sources => {
		assert.equal(program.files.size, 0);
		for (const [path] of sources) program.files.set(path, 1);
		assert.equal(program.loaded.size, 0);
	});
	assert.deepEqual([...program.loaded], ['water']);
	assert.deepEqual([...program.config], [['language.json', 'French']]);
	assert.deepEqual([...program.save], [['layout.json', 'checkpoint']]);
});

test('cross-edition imports discard stale theme readiness and retain explicit dispatch', async () => {
	const program = runtime();
	for (const theme of ['water', '', 'science']) {
		const files = startup.plan([file('Data/Ver.txt'), file('Data/levels/water/a.wad'), file('Data/levels/science/b.wad'), file('Data/levels/jungle/c.wad')], theme);
		await startup.importStartup(program, files, async sources => {
			assert.equal(program.files.size, 0);
			assert.equal(program.loaded.size, 0);
			for (const [path] of sources) program.files.set(path, 1);
		});
		assert.deepEqual([...program.loaded], theme ? [theme] : []);
		assert.equal(program.files.has('Data/levels/water/a.wad'), theme === 'water');
		assert.equal(program.files.has('Data/levels/science/b.wad'), theme === 'science');
	}
});

test('once active, reset refuses before import or persistent/cache mutation', async () => {
	const program = runtime();
	program.active = true;
	let copied = false;
	await assert.rejects(startup.importStartup(program, { theme: '', start: [] }, async () => { copied = true; }), /Reload/);
	assert.equal(copied, false);
	assert.equal(program.files.get('Data/levels/jungle/stale.wad'), 99);
	assert.deepEqual([...program.loaded], ['jungle']);
});

test('manifest planning needs every selected theme entry, not just the lobby', () => {
	const manifestPaths = ['Data/Ver.txt', 'Data/levels/Standard.sam', 'Data/levels/arabian/a.wad', 'Data/levels/arabian/b.wad', 'Data/levels/science/a.wad'];
	const kept = new Set(manifestPaths.slice(0, 3));
	const ready = theme => startup.plan(manifestPaths.map(file), theme).start.every(([path]) => kept.has(path));
	assert.equal(ready(''), true);
	assert.equal(ready('arabian'), false);
	kept.add('Data/levels/arabian/b.wad');
	assert.equal(ready('arabian'), true);
	assert.equal(ready('science'), false);
});

test('only explicit themes dispatch the experimental entry point; TPW retains its lobby', () => {
	const calls = [];
	const program = {
		Start: origin => calls.push(['lobby', origin]),
		StartExperimental: (origin, theme) => calls.push(['experimental', origin, theme]),
	};
	for (const theme of ['', 'arabian', 'science', 'water'])
		startup.startSelected(program, 'https://test.invalid', theme);
	assert.deepEqual(calls, [['lobby', 'https://test.invalid'], ...['arabian', 'science', 'water'].map(theme => ['experimental', 'https://test.invalid', theme])]);
});
