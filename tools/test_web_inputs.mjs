import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

// The page modules as shipped, without a package.json or a test dependency (as test_web_originals.mjs does).
const load = async name => import(`data:text/javascript;base64,${Buffer.from(await readFile(new URL(`../source/OpenTPW.Web/wwwroot/${name}`, import.meta.url), 'utf8')).toString('base64')}`);
const { openOriginalStore } = await load('opentpw-originals.js');
const { EDITION, STORE_DIRECTORY, planInput, importInput, importFolder, restoreInput, keptComplete } = await load('opentpw-inputs.js');

// An in-memory stand-in for the origin-private file system (the handle methods opentpw-originals.js uses).
const notFound = name => Object.assign(new Error(`${name} not found`), { name: 'NotFoundError' });
class FakeFile {
	constructor() { this.bytes = new Uint8Array(0); }
	async getFile() {
		const bytes = this.bytes;
		return { size: bytes.byteLength, text: async () => new TextDecoder().decode(bytes), arrayBuffer: async () => bytes.slice().buffer };
	}
	async createWritable() {
		const parts = [];
		return {
			write: async data => parts.push(typeof data === 'string' ? new TextEncoder().encode(data) : new Uint8Array(data.buffer ?? data, data.byteOffset ?? 0, data.byteLength)),
			close: async () => {
				const length = parts.reduce((sum, part) => sum + part.byteLength, 0);
				this.bytes = new Uint8Array(length);
				let offset = 0;
				for (const part of parts) { this.bytes.set(part, offset); offset += part.byteLength; }
			},
		};
	}
}
class FakeDirectory {
	constructor() { this.children = new Map(); }
	async getDirectoryHandle(name, { create = false } = {}) {
		if (!this.children.has(name)) { if (!create) throw notFound(name); this.children.set(name, new FakeDirectory()); }
		return this.children.get(name);
	}
	async getFileHandle(name, { create = false } = {}) {
		if (!this.children.has(name)) { if (!create) throw notFound(name); this.children.set(name, new FakeFile()); }
		return this.children.get(name);
	}
	async removeEntry(name) { if (!this.children.delete(name)) throw notFound(name); }
	async *keys() { yield* [...this.children.keys()]; }
}

const digest = async data => `len${data.byteLength}`;
const file = (path, size = 3) => [path, async () => new Uint8Array(size).fill(7).buffer, size];
const paths = files => files.map(([path]) => path);

// A small Theme Park Inc install, as a folder chosen on the page lists it: the install (with Data) or Data itself.
const install = [
	'Ver.txt', 'levels/Standard.sam',
	'levels/arabian/rides/tbounce.wad', 'levels/Arabian/Facilities/litbin.wad', 'levels/science/rides/robot.wad', 'levels/science/shops/drinks.wad', 'levels/water/rides/boat.wad', 'levels/water/features/atm.wad',
	'levels/arabian/Sharetex.wad', 'levels/science/sharetex.wad', 'levels/water/sharetex.wad', 'levels/science/ssharete.wad', 'levels/science/Sound/ride.sdt', 'global/sound/catalog.sdt',
	// not needed by a mod: music, terrain, speech, movies, the interface, the other worlds' loose files
	'levels/arabian/Music/theme.sdt', 'levels/arabian/terrain.wad', 'levels/water/2dmap.tga', 'global/Speech/speechHD.SDT', 'movies/intro.mpg', 'ui.wad', 'levels/arabian/queue.wad',
];
const asInstall = install.map(path => file(`Data/${path}`));
const asData = install.map(path => file(path));
const needed = ['Data/Ver.txt', 'Data/levels/Standard.sam', 'Data/levels/arabian/rides/tbounce.wad', 'Data/levels/Arabian/Facilities/litbin.wad', 'Data/levels/science/rides/robot.wad', 'Data/levels/science/shops/drinks.wad',
	'Data/levels/water/rides/boat.wad', 'Data/levels/water/features/atm.wad', 'Data/levels/arabian/Sharetex.wad', 'Data/levels/science/sharetex.wad', 'Data/levels/water/sharetex.wad', 'Data/levels/science/ssharete.wad',
	'Data/levels/science/Sound/ride.sdt', 'Data/global/sound/catalog.sdt'];

test('only what the Theme Park Inc rides mod reads is chosen, from the install or from Data itself', () => {
	for (const sources of [asInstall, asData, asInstall.map(([path, read, size]) => [path.replace('Data/', 'data/'), read, size])]) {
		const plan = planInput(sources);
		assert.deepEqual(paths(plan.files), needed);
		assert.equal(plan.bytes, needed.length * 3);
	}
});

test('files outside Data or with unsafe paths are never chosen', () => {
	const plan = planInput([...asInstall, file('Dutch/data/levels/arabian/rides/x.wad'), file('Data/levels/arabian/rides/../../../x'), file('Data\\levels\\science\\rides\\y.wad')]);
	assert.deepEqual(paths(plan.files).filter(path => !needed.includes(path)), ['Data/levels/science/rides/y.wad']);
});

test('a folder without Theme Park Inc data is refused with a reason, and Theme Park World data is named', () => {
	assert.throws(() => planInput([file('Data/ui.wad')]), /No Theme Park Inc data/);
	assert.throws(() => planInput([]), /No Theme Park Inc data/);
	assert.throws(() => planInput([file('Data/levels/jungle/rides/x.wad'), file('Data/Ver.txt')]), /Theme Park World data, not Theme Park Inc/);
	assert.throws(() => planInput([...asInstall, file('Data/levels/jungle/terrain.wad')]), /mixes Theme Park World and Theme Park Inc/);
});

test('one world folder is not enough: Ver.txt, every world and the global sounds are required', () => {
	assert.throws(() => planInput([file('Data/levels/arabian/rides/x.wad')]), /not a complete Theme Park Inc install: Ver\.txt is missing/);
	const without = pattern => asInstall.filter(([path]) => !pattern.test(path));
	assert.throws(() => planInput(without(/water\/rides/)), /water ride archives is missing/);
	assert.throws(() => planInput(without(/science\/sharetex/)), /science's shared textures is missing/);
	assert.throws(() => planInput(without(/global\/sound/)), /global sounds is missing/);
});

// The game's side: files go to a staging copy and become the live one only when published; a failed or aborted import leaves neither.
function runtime(log = []) {
	const staging = new Map();
	const files = new Map();
	return {
		files, staging, log, locked: false,
		BeginInput(edition) { if (this.locked) throw new Error('in use'); log.push(`begin ${edition}`); files.clear(); staging.clear(); },
		AddInputFile(edition, path, bytes) { assert.equal(edition, EDITION); assert.ok(bytes instanceof Uint8Array); staging.set(path, bytes.length); },
		PublishInput(edition, count) { assert.equal(edition, EDITION); if (staging.size !== count) throw new Error('incomplete'); log.push('publish'); for (const [path, size] of staging) files.set(path, size); staging.clear(); },
		AbortInput() { log.push('abort'); files.clear(); staging.clear(); },
		InputChanged(message) { log.push(`changed ${message}`); },
	};
}

test('an import empties the copy, copies the planned files, keeps them and reports the end', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest, name: STORE_DIRECTORY });
	const program = runtime();
	const progress = [];
	const result = await importInput(program, planInput(asInstall), { store, progress: text => progress.push(text) });
	assert.deepEqual([...program.files.keys()], needed);
	assert.deepEqual(program.log, ['begin theme-park-inc', 'publish', 'changed ']);
	assert.deepEqual({ count: result.count, kept: result.kept }, { count: needed.length, kept: true });
	assert.match(progress.at(-1), /^14 of 14 files/);
	// the second store lives in its own directory: the Theme Park World files' manifest is untouched
	assert.deepEqual([...root.children.keys()], [STORE_DIRECTORY]);
	assert.equal(store.manifest.files['Data/levels/arabian/rides/tbounce.wad'].stored, true);
	assert.equal(Object.keys(store.manifest.files).length, needed.length);
});

test('another install replaces the first whole, without mixing', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest, name: STORE_DIRECTORY });
	const program = runtime();
	await importInput(program, planInput(asInstall), { store });
	await importInput(program, planInput(asInstall.filter(([path]) => !/drinks|litbin/.test(path))), { store });
	assert.equal(program.files.size, needed.length - 2);
	assert.equal(Object.keys(store.manifest.files).length, needed.length - 2);
	assert.equal(program.files.has("Data/levels/science/shops/drinks.wad"), false);
});

test('without a store the files are loaded for this visit only; a full disk never fails the import', async () => {
	const program = runtime();
	const result = await importInput(program, planInput(asInstall));
	assert.equal(result.kept, false);
	assert.equal(program.files.size, needed.length);
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest, name: STORE_DIRECTORY });
	store.write = async () => { throw Object.assign(new Error('full'), { name: 'QuotaExceededError' }); };
	const again = runtime();
	const kept = await importInput(again, planInput(asInstall), { store });
	assert.equal(kept.kept, false);
	assert.equal(again.files.size, needed.length, 'the game still has every file');
	assert.equal(again.log.at(-1), 'changed ');
	assert.equal(store.manifest, null, 'a store that failed to keep is cleared, never half kept');
});

test('a failure is reported to the game with its reason and nothing is half-announced', async () => {
	const program = runtime();
	program.AddInputFile = () => { throw new Error('over the limit'); };
	await assert.rejects(importInput(program, planInput(asInstall)), /over the limit/);
	assert.deepEqual(program.log, ['begin theme-park-inc', 'abort', 'changed over the limit']);
	assert.equal(program.files.size + program.staging.size, 0, 'no partial copy stays behind');
	const locked = runtime();
	locked.locked = true;
	await assert.rejects(importInput(locked, planInput(asInstall)), /in use/);
	assert.equal(locked.log.at(-1), 'changed in use');
	// a read that fails half way: the staged files are removed and the live copy never saw them
	const reading = runtime();
	const flaky = planInput(asInstall);
	flaky.files[5][1] = async () => { throw new Error('read failed'); };
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest, name: STORE_DIRECTORY });
	await assert.rejects(importInput(reading, flaky, { store }), /read failed/);
	assert.equal(reading.files.size + reading.staging.size, 0);
	assert.equal(reading.log.filter(line => line.startsWith('changed')).length, 1);
	assert.equal(reading.log.includes('publish'), false);
	assert.equal(store.manifest, null);
});

test('a folder that is refused still tells the game exactly once', async () => {
	const program = runtime();
	await assert.rejects(importFolder(program, [file('Data/ui.wad')]), /No Theme Park Inc data/);
	assert.deepEqual(program.log, ['changed No Theme Park Inc data in that folder: choose the folder that holds Data (or Data itself).']);
	const ok = runtime();
	await importFolder(ok, asInstall);
	assert.equal(ok.log.filter(line => line.startsWith('changed')).length, 1);
});

test('kept files are restored before the game starts, and only a complete set counts', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest, name: STORE_DIRECTORY });
	await importInput(runtime(), planInput(asInstall), { store });
	// a later visit: the store is opened again from the same directory
	const later = await openOriginalStore(root, { digest, name: STORE_DIRECTORY });
	const program = runtime();
	assert.equal(await restoreInput(program, later), needed.length);
	assert.deepEqual([...program.files.keys()], needed);
	assert.deepEqual(program.log, ['begin theme-park-inc', 'publish', 'changed ']);

	// a file that is no longer kept: nothing is half-restored and the store is cleared so the folder is asked for again
	const missing = await openOriginalStore(root, { digest, name: STORE_DIRECTORY });
	missing.manifest.files['Data/Ver.txt'].stored = false;
	const partial = runtime();
	assert.equal(await restoreInput(partial, missing), 0);
	assert.equal(partial.files.size, 0);
	assert.equal(missing.manifest, null, 'an incomplete store is cleared, not left looking kept');
	assert.equal(keptComplete(missing), false);

	// a store that holds nothing restores nothing and touches nothing
	const empty = await openOriginalStore(new FakeDirectory(), { digest, name: STORE_DIRECTORY });
	const untouched = runtime();
	assert.equal(await restoreInput(untouched, empty), 0);
	assert.deepEqual(untouched.log, []);
});

test('restored files must still match their stored hashes before anything is copied', async () => {
	const root = new FakeDirectory();
	const contentDigest = async data => `sha${[...new Uint8Array(data)].join('')}`;
	const store = await openOriginalStore(root, { digest: contentDigest, name: STORE_DIRECTORY });
	await importInput(runtime(), planInput(asInstall), { store });
	const later = await openOriginalStore(root, { digest: contentDigest, name: STORE_DIRECTORY });
	// same size, other bytes: only the hash can tell
	const directory = await root.getDirectoryHandle(STORE_DIRECTORY);
	const levels = await (await directory.getDirectoryHandle('Data')).getDirectoryHandle('levels');
	const rides = await (await (await levels.getDirectoryHandle('arabian')).getDirectoryHandle('rides')).getFileHandle('tbounce.wad');
	rides.bytes = new Uint8Array(rides.bytes.length).fill(9);
	const program = runtime();
	assert.equal(await restoreInput(program, later), 0);
	assert.equal(program.files.size + program.staging.size, 0);
	assert.equal(program.log.includes('begin theme-park-inc'), false, 'nothing was copied before the check');
	assert.equal(later.manifest, null);
});

test('a store with an invalid manifest is cleared of stray files', async () => {
	const root = new FakeDirectory();
	const directory = await root.getDirectoryHandle(STORE_DIRECTORY, { create: true });
	for (const [name, text] of [['manifest.json', '{ nope'], ['stray.bin', 'x']]) {
		const writable = await (await directory.getFileHandle(name, { create: true })).createWritable();
		await writable.write(text);
		await writable.close();
	}
	const store = await openOriginalStore(root, { digest, name: STORE_DIRECTORY });
	assert.equal(await restoreInput(runtime(), store), 0);
	assert.deepEqual([...directory.children.keys()], []);
});

test('a store that cannot read a kept file is cleared and the game starts without the input', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest, name: STORE_DIRECTORY });
	await importInput(runtime(), planInput(asInstall), { store });
	const later = await openOriginalStore(root, { digest, name: STORE_DIRECTORY });
	later.read = async () => null;
	const program = runtime();
	assert.equal(await restoreInput(program, later), 0);
	assert.equal(program.files.size, 0);
	assert.equal(later.manifest, null);
});
