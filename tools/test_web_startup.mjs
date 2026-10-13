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

// Execute the actual page module and its registered folder handler. DOM and .NET exports
// are synthetic; this protects their wiring, not browser APIs, graphics or native data.
const pageSource = (await readFile(new URL('../source/OpenTPW.Web/wwwroot/main.js', import.meta.url), 'utf8')).replace(/^import .*;\n/gm, '');
async function page(theme, active = false) {
	const calls = [], files = new Map([['Data/old.wad', 9]]), elements = new Map();
	const element = id => {
		if (!elements.has(id)) elements.set(id, { value: id === 'edition' ? theme : '', textContent: '', handlers: new Map(),
			addEventListener(name, handler) { this.handlers.set(name, handler); }, focus() {} });
		return elements.get(id);
	};
	const program = {
		BeginImport() { calls.push(['reset']); if (active) throw new Error('Reload'); files.clear(); },
		AddFile(path, bytes) { calls.push(['file', path]); files.set(path, bytes[0]); },
		LevelLoaded(value) { calls.push(['ready', value]); },
		StartExperimental(origin, value) { calls.push(['experimental', value]); }, Start() { calls.push(['lobby']); },
		ReadFile() { throw new Error('no saved language'); }, ClarityDistribution() { return '{"packManifest":{}}'; },
		InstallClarity() {}, Frame() { return false; },
	};
	const store = { manifest: null, bytes: { stored: 0 }, begin() { calls.push(['cache-begin']); }, async flush() {} };
	const environment = {
		dotnet: { async create() { return { setModuleImports() {}, async getAssemblyExports() { return { OpenTPW: { Program: program } }; },
			getConfig() { return { mainAssemblyName: 'synthetic' }; }, async runMain() {} }; } },
		webgl: {}, audio: {}, storage: { async restore() {}, async sync() {} }, createTextureDownloads() { return {}; },
		originals: { supported() { return true; }, MODES: ['lazy', 'all', 'off'], async openOriginalStore() { return store; } },
		...startup, document: { getElementById: element, addEventListener() {} },
		location: { search: '', hostname: 'test.invalid', origin: 'https://test.invalid' },
		navigator: { locks: { async request(name, options, callback) { callback({}); } }, storage: { async getDirectory() { return {}; } } },
		localStorage: { getItem() { return 'off'; }, setItem() {} }, window: { addEventListener() {} },
		requestAnimationFrame(callback) { queueMicrotask(() => callback(0)); }, setInterval() {},
		console: { warn() {}, error() {} },
	};
	const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
	await new AsyncFunction(...Object.keys(environment), pageSource)(...Object.values(environment));
	return { calls, files, element, choose(sources) { element('folder').handlers.get('change')({ target: { files: sources } }); } };
}
const folderFile = (path, value = 1, fail = false) => ({ webkitRelativePath: `synthetic/${path}`, size: 1,
	async arrayBuffer() { if (fail) throw new Error('synthetic read failure'); return new Uint8Array([value]).buffer; } });
async function settled(predicate) {
	for (let count = 0; count < 50; count++) { if (predicate()) return; await new Promise(resolve => setImmediate(resolve)); }
	assert.fail('the actual page handler did not settle');
}

test('the actual folder handler loads the selected theme before its experimental export', async () => {
	const context = await page('water');
	context.choose([folderFile('Data/Ver.txt'), folderFile('Data/levels/water/a.wad'), folderFile('Data/levels/science/b.wad')]);
	await settled(() => context.calls.some(([kind]) => kind === 'experimental'));
	assert.deepEqual(context.calls, [['reset'], ['file', 'Data/Ver.txt'], ['file', 'Data/levels/water/a.wad'], ['ready', 'water'], ['experimental', 'water']]);
	assert.equal(context.files.has('Data/old.wad'), false);
	assert.equal(context.files.has('Data/levels/science/b.wad'), false);
});

test('the actual folder handler refuses partial imports then retries without stale files or readiness', async () => {
	const context = await page('water');
	context.choose([folderFile('Data/old-attempt.sam'), folderFile('Data/levels/water/a.wad', 1, true)]);
	await settled(() => context.element('status').textContent.includes('Could not start'));
	assert.equal(context.calls.some(([kind]) => kind === 'ready' || kind === 'experimental'), false);
	context.choose([folderFile('Data/Ver.txt'), folderFile('Data/levels/water/b.wad', 2)]);
	await settled(() => context.calls.some(([kind]) => kind === 'experimental'));
	assert.equal(context.files.has('Data/old-attempt.sam'), false);
	assert.equal(context.calls.filter(([kind]) => kind === 'ready').length, 1);
	assert.equal(context.files.get('Data/levels/water/b.wad'), 2);
});

test('the actual folder handler refuses an active reset before any cache or file work', async () => {
	const context = await page('water', true);
	context.element('keep').value = 'lazy'; context.element('keep').handlers.get('change')({ target: { value: 'lazy' } });
	context.choose([folderFile('Data/Ver.txt'), folderFile('Data/levels/water/a.wad')]);
	await settled(() => context.element('status').textContent.includes('Reload'));
	assert.deepEqual(context.calls, [['reset']]);
	assert.equal(context.files.get('Data/old.wad'), 9);
});

test('the actual TPW folder handler keeps its lobby and deferred park files', async () => {
	const context = await page('');
	context.choose([folderFile('Data/global/a.sam'), folderFile('Data/levels/jungle/Easymode.TPWI'), folderFile('Data/levels/jungle/terrain.wad')]);
	await settled(() => context.calls.some(([kind]) => kind === 'lobby'));
	assert.deepEqual(context.calls, [['reset'], ['file', 'Data/global/a.sam'], ['file', 'Data/levels/jungle/Easymode.TPWI'], ['lobby']]);
	assert.equal(context.files.has('Data/levels/jungle/terrain.wad'), false);
});
