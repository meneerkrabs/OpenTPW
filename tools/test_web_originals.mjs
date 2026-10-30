import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

// The page module as shipped, without a package.json or a test dependency.
const source = await readFile(new URL('../source/OpenTPW.Web/wwwroot/opentpw-originals.js', import.meta.url), 'utf8');
const { MODES, claimKeeping, openOriginalStore, supported } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const startupSource = await readFile(new URL('../source/OpenTPW.Web/wwwroot/opentpw-startup.js', import.meta.url), 'utf8');
const { plan, importStartup } = await import(`data:text/javascript;base64,${Buffer.from(startupSource).toString('base64')}`);

// An in-memory stand-in for the origin-private file system: the handle methods the module uses.
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
		const child = this.children.get(name);
		if (!(child instanceof FakeDirectory)) throw new Error(`${name} is a file`);
		return child;
	}
	async getFileHandle(name, { create = false } = {}) {
		if (!this.children.has(name)) { if (!create) throw notFound(name); this.children.set(name, new FakeFile()); }
		const child = this.children.get(name);
		if (!(child instanceof FakeFile)) throw new Error(`${name} is a directory`);
		return child;
	}
	async removeEntry(name) { if (!this.children.delete(name)) throw notFound(name); }
	async *keys() { yield* [...this.children.keys()]; }
}

const bytes = text => new TextEncoder().encode(text);
const digest = async data => `len${data.byteLength}`;
const folder = [['Data/ui.wad', 2], ['Data/levels/jungle/terrain.wad', 7], ['Dutch/data/global/speech/speechHD.SDT', 6]];

function pauseNextClose(file, fail = false) {
	const original = file.createWritable.bind(file);
	let release;
	let closing;
	const resume = new Promise(resolve => release = resolve);
	const started = new Promise(resolve => closing = resolve);
	file.createWritable = async () => {
		file.createWritable = original;
		const writable = await original();
		return { ...writable, close: async () => {
			closing();
			await resume;
			if (fail) throw new Error('paused close failed');
			await writable.close();
		} };
	};
	return { started, release: () => release() };
}

test('a rejected mutation does not block queued or later writes to the same path', async () => {
	const root = new FakeDirectory();
	const contentDigest = async data => new TextDecoder().decode(data);
	const store = await openOriginalStore(root, { digest: contentDigest });
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	await store.flush();
	const directory = await root.getDirectoryHandle('opentpw-originals');
	const file = await (await directory.getDirectoryHandle('Data')).getFileHandle('ui.wad');
	const pause = pauseNextClose(file, true);
	const rejected = assert.rejects(store.write('Data/ui.wad', bytes('AA')), /paused close failed/);
	await pause.started;
	const queued = store.write('Data/ui.wad', bytes('BB'));
	await new Promise(resolve => setImmediate(resolve));
	pause.release();
	await Promise.all([rejected, queued]);
	await store.flush();
	const again = await openOriginalStore(root, { digest: contentDigest });
	assert.equal(new TextDecoder().decode(await again.read('Data/ui.wad')), 'BB');
	assert.equal(again.manifest.files['Data/ui.wad'].sha256, 'BB');
	await store.write('Data/ui.wad', bytes('CC'));
	await store.flush();
	assert.equal(new TextDecoder().decode(await store.read('Data/ui.wad')), 'CC');
	assert.equal(store.manifest.files['Data/ui.wad'].sha256, 'CC');
});

test('an unrelated path can finish and flush while another path waits for close', async () => {
	const root = new FakeDirectory();
	const contentDigest = async data => new TextDecoder().decode(data);
	const store = await openOriginalStore(root, { digest: contentDigest });
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	await store.flush();
	const directory = await root.getDirectoryHandle('opentpw-originals');
	const file = await (await directory.getDirectoryHandle('Data')).getFileHandle('ui.wad');
	const pause = pauseNextClose(file);
	const pending = store.write('Data/ui.wad', bytes('AA'));
	await pause.started;
	const manifestFile = await directory.getFileHandle('manifest.json');
	const original = manifestFile.createWritable.bind(manifestFile);
	let manifestWrites = 0;
	manifestFile.createWritable = async () => { manifestWrites++; return original(); };
	let finished = false;
	const independent = store.write('Data/levels/jungle/terrain.wad', bytes('terrain')).then(() => finished = true);
	await new Promise(resolve => setImmediate(resolve));
	assert.equal(finished, true, 'Only the matching path waits for the active mutation');
	assert.equal(manifestWrites, 0, 'An initial lazy write adds no manifest checkpoint');
	await independent;
	await store.flush();
	const during = await openOriginalStore(root, { digest: contentDigest });
	assert.equal(await during.read('Data/ui.wad'), null);
	assert.equal(new TextDecoder().decode(await during.read('Data/levels/jungle/terrain.wad')), 'terrain');
	pause.release();
	await pending;
	await store.flush();
	const again = await openOriginalStore(root, { digest: contentDigest });
	assert.equal(new TextDecoder().decode(await again.read('Data/ui.wad')), 'AA');
	assert.equal(again.manifest.files['Data/ui.wad'].sha256, 'AA');
});

test('a mutation queued before clearing cannot write into the replacement folder', async () => {
	const root = new FakeDirectory();
	const contentDigest = async data => new TextDecoder().decode(data);
	const store = await openOriginalStore(root, { digest: contentDigest });
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	await store.flush();
	const directory = await root.getDirectoryHandle('opentpw-originals');
	const file = await (await directory.getDirectoryHandle('Data')).getFileHandle('ui.wad');
	const pause = pauseNextClose(file);
	const active = store.write('Data/ui.wad', bytes('AA'));
	await pause.started;
	const stale = store.write('Data/ui.wad', bytes('BB'));
	await new Promise(resolve => setImmediate(resolve));
	await store.clear();
	await store.begin('lazy', folder);
	pause.release();
	await Promise.all([active, stale]);
	const after = await openOriginalStore(root, { digest: contentDigest });
	assert.equal(after.has('Data/ui.wad'), false);
	assert.equal((await directory.getDirectoryHandle('Data', { create: true })).children.has('ui.wad'), false, 'A stale queued request must not create replacement-folder bytes');
	await store.write('Data/ui.wad', bytes('CC'));
	await store.flush();
	const kept = await openOriginalStore(root, { digest: contentDigest });
	assert.equal(new TextDecoder().decode(await kept.read('Data/ui.wad')), 'CC');
	assert.equal(kept.manifest.files['Data/ui.wad'].sha256, 'CC');
});

test('overlapping same-path closes cannot publish another writers bytes under the wrong hash', async () => {
	const root = new FakeDirectory();
	const contentDigest = async data => new TextDecoder().decode(data);
	const store = await openOriginalStore(root, { digest: contentDigest });
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	await store.flush();
	const directory = await root.getDirectoryHandle('opentpw-originals');
	const file = await (await directory.getDirectoryHandle('Data')).getFileHandle('ui.wad');
	const original = file.createWritable.bind(file);
	let opened = 0;
	let closing;
	let release;
	const firstCommitted = new Promise(resolve => closing = resolve);
	const finishClose = new Promise(resolve => release = resolve);
	file.createWritable = async () => {
		const writable = await original();
		if (++opened !== 1) return writable;
		return { ...writable, close: async () => {
			await writable.close();
			closing();
			await finishClose;
		} };
	};
	const first = store.write('Data/ui.wad', bytes('AA'));
	await firstCommitted;
	const second = store.write('Data/ui.wad', bytes('BB'));
	// Let the synthetic promise-only second write finish if it is not serialized.
	await new Promise(resolve => setImmediate(resolve));
	release();
	await Promise.all([first, second]);
	await store.flush();
	const again = await openOriginalStore(root, { digest: contentDigest });
	const contents = new TextDecoder().decode(await again.read('Data/ui.wad'));
	assert.equal(contents, again.manifest.files['Data/ui.wad'].sha256, 'The final file bytes and published content hash must belong to one writer');
});

// Reject one stream operation before committing bytes; later streams work normally.
function rejectNext(file, operation) {
	const original = file.createWritable.bind(file);
	file.createWritable = async () => {
		const writable = await original();
		return { ...writable, [operation]: async () => {
			file.createWritable = original;
			throw Object.assign(new Error(`${operation} failed`), { name: 'QuotaExceededError' });
		} };
	};
}

for (const operation of ['write', 'close']) {
	test(`failed file ${operation} stays unkept and a later retry succeeds`, async () => {
		const root = new FakeDirectory();
		const store = await openOriginalStore(root, { digest });
		await store.begin('lazy', folder);
		const directory = await root.getDirectoryHandle('opentpw-originals');
		const data = await directory.getDirectoryHandle('Data', { create: true });
		rejectNext(await data.getFileHandle('ui.wad', { create: true }), operation);
		await assert.rejects(store.write('Data/ui.wad', bytes('ui')), { name: 'QuotaExceededError' });
		assert.equal(store.has('Data/ui.wad'), false);
		await store.flush();
		assert.equal((await openOriginalStore(root, { digest })).has('Data/ui.wad'), false);
		await store.write('Data/ui.wad', bytes('ui'));
		await store.flush();
		const again = await openOriginalStore(root, { digest });
		assert.equal(new TextDecoder().decode(await again.read('Data/ui.wad')), 'ui');
	});

	test(`failed manifest ${operation} remains dirty and a later flush retries`, async () => {
		const root = new FakeDirectory();
		const store = await openOriginalStore(root, { digest });
		await store.begin('lazy', folder);
		await store.write('Data/ui.wad', bytes('ui'));
		const directory = await root.getDirectoryHandle('opentpw-originals');
		rejectNext(await directory.getFileHandle('manifest.json'), operation);
		await assert.rejects(store.flush(), { name: 'QuotaExceededError' });
		assert.equal((await openOriginalStore(root, { digest })).has('Data/ui.wad'), false);
		await store.flush();
		const again = await openOriginalStore(root, { digest });
		assert.equal(new TextDecoder().decode(await again.read('Data/ui.wad')), 'ui');
	});
}

test('failed cached file reads become persistently missing and can be kept again', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest });
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	await store.flush();
	const directory = await root.getDirectoryHandle('opentpw-originals');
	const data = await directory.getDirectoryHandle('Data');
	const file = await data.getFileHandle('ui.wad');
	const original = file.getFile.bind(file);
	file.getFile = async () => { throw new Error('file read failed'); };
	assert.equal(await store.read('Data/ui.wad'), null);
	assert.equal(store.has('Data/ui.wad'), false);
	await store.flush();
	assert.equal((await openOriginalStore(root, { digest })).has('Data/ui.wad'), false);
	file.getFile = original;
	await store.write('Data/ui.wad', bytes('ui'));
	assert.equal(new TextDecoder().decode(await store.read('Data/ui.wad')), 'ui');
});

test('a failed content digest leaves the previously kept bytes and manifest consistent', async () => {
	const root = new FakeDirectory();
	let fail = false;
	const contentDigest = async data => {
		if (fail) throw new Error('digest failed');
		return new TextDecoder().decode(data);
	};
	const store = await openOriginalStore(root, { digest: contentDigest });
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	await store.flush();
	fail = true;
	await assert.rejects(store.write('Data/ui.wad', bytes('UI')), /digest failed/);
	assert.equal(new TextDecoder().decode(await store.read('Data/ui.wad')), 'ui');
	const again = await openOriginalStore(root, { digest: contentDigest });
	assert.equal(new TextDecoder().decode(await again.read('Data/ui.wad')), 'ui');
	fail = false;
	await store.write('Data/ui.wad', bytes('UI'));
	await store.flush();
	assert.equal(await store.matches('Data/ui.wad', bytes('UI')), true);
});

test('a pending digest leaves the old cache readable without opening a writable file', async () => {
	const root = new FakeDirectory();
	let delay = false;
	let started;
	let reject;
	const hashing = new Promise(resolve => started = resolve);
	const contentDigest = async data => {
		if (delay) {
			started();
			return await new Promise((resolve, fail) => reject = fail);
		}
		return new TextDecoder().decode(data);
	};
	const store = await openOriginalStore(root, { digest: contentDigest });
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	await store.flush();
	const directory = await root.getDirectoryHandle('opentpw-originals');
	const file = await (await directory.getDirectoryHandle('Data')).getFileHandle('ui.wad');
	const original = file.createWritable.bind(file);
	let opened = 0;
	file.createWritable = async () => { opened++; return original(); };
	delay = true;
	const pending = store.write('Data/ui.wad', bytes('UI'));
	await hashing;
	assert.equal(new TextDecoder().decode(await store.read('Data/ui.wad')), 'ui');
	const during = await openOriginalStore(root, { digest: contentDigest });
	assert.equal(new TextDecoder().decode(await during.read('Data/ui.wad')), 'ui');
	assert.equal(during.manifest.files['Data/ui.wad'].sha256, 'ui');
	assert.equal(opened, 0);
	const failed = assert.rejects(pending, /delayed digest failed/);
	reject(new Error('delayed digest failed'));
	await failed;
	assert.equal(store.has('Data/ui.wad'), true);
	assert.equal(store.manifest.files['Data/ui.wad'].sha256, 'ui');
	assert.equal(opened, 0);
	delay = false;
	await store.write('Data/ui.wad', bytes('UI'));
	await store.flush();
	const retried = await openOriginalStore(root, { digest: contentDigest });
	assert.equal(new TextDecoder().decode(await retried.read('Data/ui.wad')), 'UI');
	assert.equal(retried.manifest.files['Data/ui.wad'].sha256, 'UI');
});

test('a digest finishing after the folder was cleared cannot overwrite the new installation', async () => {
	const root = new FakeDirectory();
	let release;
	let hashing;
	const started = new Promise(resolve => hashing = resolve);
	const store = await openOriginalStore(root, { digest: async data => {
		if (new TextDecoder().decode(data) === 'ui') {
			hashing();
			await new Promise(resolve => release = resolve);
		}
		return new TextDecoder().decode(data);
	} });
	await store.begin('lazy', folder);
	const previousWrite = store.write('Data/ui.wad', bytes('ui'));
	await started;
	await store.clear();
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('UI'));
	await store.flush();
	release();
	await previousWrite;
	assert.equal(new TextDecoder().decode(await store.read('Data/ui.wad')), 'UI');
	assert.equal(await store.matches('Data/ui.wad', bytes('UI')), true);
});

test('a replaced cached file is not advertised by the old persisted hash when publishing fails', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest: async data => new TextDecoder().decode(data) });
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	await store.flush();
	await store.write('Data/ui.wad', bytes('UI'));
	const directory = await root.getDirectoryHandle('opentpw-originals');
	rejectNext(await directory.getFileHandle('manifest.json'), 'write');
	await assert.rejects(store.flush());
	const interrupted = await openOriginalStore(root, { digest });
	assert.equal(interrupted.has('Data/ui.wad'), false);
	assert.equal(await interrupted.read('Data/ui.wad'), null);
	await store.flush();
	const retried = await openOriginalStore(root, { digest });
	assert.equal(new TextDecoder().decode(await retried.read('Data/ui.wad')), 'UI');
	assert.equal(retried.manifest.files['Data/ui.wad'].sha256, 'UI');
});

for (const operation of ['write', 'close']) {
	test(`a cached replacement whose ${operation} fails remains missing after reopen`, async () => {
		const root = new FakeDirectory();
		const store = await openOriginalStore(root, { digest });
		await store.begin('lazy', folder);
		await store.write('Data/ui.wad', bytes('ui'));
		await store.flush();
		const directory = await root.getDirectoryHandle('opentpw-originals');
		const data = await directory.getDirectoryHandle('Data');
		rejectNext(await data.getFileHandle('ui.wad'), operation);
		await assert.rejects(store.write('Data/ui.wad', bytes('UI')));
		assert.equal(store.has('Data/ui.wad'), false);
		assert.equal((await openOriginalStore(root, { digest })).has('Data/ui.wad'), false);
		await store.write('Data/ui.wad', bytes('UI'));
		await store.flush();
		assert.equal(new TextDecoder().decode(await store.read('Data/ui.wad')), 'UI');
	});
}

for (const operation of ['write', 'close']) {
	test(`a failed pre-replacement manifest ${operation} retries invalidation before changing bytes`, async () => {
		const root = new FakeDirectory();
		const contentDigest = async data => new TextDecoder().decode(data);
		const store = await openOriginalStore(root, { digest: contentDigest });
		await store.begin('lazy', folder);
		await store.write('Data/ui.wad', bytes('ui'));
		await store.flush();
		const directory = await root.getDirectoryHandle('opentpw-originals');
		const file = await (await directory.getDirectoryHandle('Data')).getFileHandle('ui.wad');
		const original = file.createWritable.bind(file);
		let opened = 0;
		file.createWritable = async () => { opened++; return original(); };
		rejectNext(await directory.getFileHandle('manifest.json'), operation);
		await assert.rejects(store.write('Data/ui.wad', bytes('UI')));
		assert.equal(opened, 0, 'A failed invalidation cannot open the replacement file');
		const previous = await openOriginalStore(root, { digest: contentDigest });
		assert.equal(new TextDecoder().decode(await previous.read('Data/ui.wad')), 'ui');
		assert.equal(previous.manifest.files['Data/ui.wad'].sha256, 'ui');
		await store.write('Data/ui.wad', bytes('UI'));
		assert.equal((await openOriginalStore(root, { digest })).has('Data/ui.wad'), false);
		await store.flush();
		const retried = await openOriginalStore(root, { digest: contentDigest });
		assert.equal(new TextDecoder().decode(await retried.read('Data/ui.wad')), 'UI');
		assert.equal(retried.manifest.files['Data/ui.wad'].sha256, 'UI');
	});
}

test('the modes and the browser check', () => {
	assert.deepEqual(MODES, ['lazy', 'all', 'off']);
	class Handle { createWritable() {} }
	assert.equal(supported({ getDirectory() {} }, Handle), true);
	assert.equal(supported({ getDirectory() {} }, class {}), false, 'no writable streams: nothing is kept');
	assert.equal(supported(undefined, Handle), false);
});

test('kept files survive a new visit, with their sizes and hashes in the manifest', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest });
	assert.equal(store.manifest, null);
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	await store.flush();
	const again = await openOriginalStore(root, { digest });
	assert.equal(again.manifest.mode, 'lazy');
	assert.ok(again.has('Data/ui.wad'));
	assert.ok(!again.has('Data/levels/jungle/terrain.wad'), 'listed but not kept yet');
	assert.equal(new TextDecoder().decode(await again.read('Data/ui.wad')), 'ui');
	assert.equal(await again.read('Data/levels/jungle/terrain.wad'), null);
	assert.equal(again.manifest.files['Data/ui.wad'].sha256, 'len2');
	assert.deepEqual(again.bytes, { stored: 2, total: 15 });
});

test('the same folder chosen again keeps what is stored; another folder starts over', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest });
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	await store.begin('all', folder);
	assert.ok(store.has('Data/ui.wad'), 'same paths and sizes: the same installation');
	assert.equal(store.manifest.mode, 'all');
	await store.begin('all', folder.map(([path]) => [path, null]));
	assert.ok(store.has('Data/ui.wad'), 'unknown sizes (a served folder) still match by path');
	await store.begin('lazy', [['Data/ui.wad', 3], ...folder.slice(1)]);
	assert.ok(!store.has('Data/ui.wad'), 'a different size is another installation');
	assert.deepEqual([...(await root.getDirectoryHandle('opentpw-originals')).children.keys()], ['manifest.json'], 'its files are gone');
});

test('a write that finishes while the manifest is being written is not forgotten', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest });
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	const flushing = store.flush();
	await store.write('Data/levels/jungle/terrain.wad', bytes('terrain'));
	await flushing;
	await store.flush();
	const again = await openOriginalStore(root, { digest });
	assert.ok(again.has('Data/ui.wad') && again.has('Data/levels/jungle/terrain.wad'));
});

test('the content check tells a kept file from another one with the same size', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root);
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	assert.equal(await store.matches('Data/ui.wad', bytes('ui')), true);
	assert.equal(await store.matches('Data/ui.wad', bytes('UI')), false, 'same size, different bytes (real SHA-256)');
	assert.equal(await store.matches('Data/levels/jungle/terrain.wad', bytes('terrain')), false, 'not kept');
});

test('a kept file whose size changed counts as not kept', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest });
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	store.manifest.files['Data/ui.wad'].size = 99;
	assert.equal(await store.read('Data/ui.wad'), null);
	assert.ok(!store.has('Data/ui.wad'));
});

test('clear removes everything, and bad paths and unlisted files are refused', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest });
	await assert.rejects(store.begin('off', folder), /Cannot keep/);
	await store.begin('lazy', folder);
	await assert.rejects(store.write('Data/other.wad', bytes('x')), /not in the kept folder/);
	store.manifest.files['../escape'] = { size: 1, stored: false };
	await assert.rejects(store.write('../escape', bytes('x')), /Not a file path/);
	delete store.manifest.files['../escape'];
	await store.write('Data/ui.wad', bytes('ui'));
	await store.flush();
	await store.clear();
	assert.equal(store.manifest, null);
	assert.equal((await openOriginalStore(root, { digest })).manifest, null);
});

// Edition choices use the existing originals manifest and readers; no edition-specific cache bypass.
test('a reopened TPI cache requires the whole selected theme and preserves other themes', async () => {
	const root = new FakeDirectory();
	let store = await openOriginalStore(root, { digest });
	const files = [['Data/Ver.txt', 1], ['Data/levels/Standard.sam', 1], ['Data/levels/water/a.wad', 1], ['Data/levels/water/b.wad', 1], ['Data/levels/science/a.wad', 1]];
	await store.begin('lazy', files);
	for (const [path] of files.slice(0, 3)) await store.write(path, bytes('a'));
	await store.flush();
	store = await openOriginalStore(root, { digest });
	const selected = () => plan(Object.entries(store.manifest.files).map(([path, entry]) => [path, () => store.read(path), entry.size]), 'water');
	assert.equal(selected().start.every(([path]) => store.has(path)), false);
	await store.write('Data/levels/water/b.wad', bytes('b'));
	await store.flush();
	store = await openOriginalStore(root, { digest });
	assert.equal(selected().start.every(([path]) => store.has(path)), true);
	assert.equal(store.has('Data/levels/science/a.wad'), false);
	const loaded = [];
	await importStartup({ BeginImport() {}, LevelLoaded: theme => loaded.push(theme) }, selected(), async sources => {
		for (const [, read] of sources) assert.equal((await read()).byteLength, 1);
	});
	assert.deepEqual(loaded, ['water']);
});

test('a physically incomplete cached TPI theme fails without marking it loaded and can recover', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest });
	const files = [['Data/Ver.txt', 1], ['Data/levels/water/a.wad', 1]];
	await store.begin('lazy', files);
	for (const [path] of files) await store.write(path, bytes('a'));
	await store.flush();
	const dir = await (await (await (await root.getDirectoryHandle('opentpw-originals')).getDirectoryHandle('Data')).getDirectoryHandle('levels')).getDirectoryHandle('water');
	(await dir.getFileHandle('a.wad')).bytes = new Uint8Array(0);
	let resets = 0;
	const loaded = [];
	const program = { BeginImport() { resets++; }, LevelLoaded: theme => loaded.push(theme) };
	const planned = () => plan(files.map(([path, size]) => [path, async () => {
		const kept = await store.read(path);
		if (!kept) throw new Error(`${path}: kept copy is missing`);
		return kept;
	}, size]), 'water');
	const copy = async sources => { for (const [, read] of sources) await read(); };
	await assert.rejects(importStartup(program, planned(), copy), /missing/);
	assert.deepEqual(loaded, []);
	await store.write('Data/levels/water/a.wad', bytes('r'));
	await importStartup(program, planned(), copy);
	assert.equal(resets, 2);
	assert.deepEqual(loaded, ['water']);
});

test('changing cached editions keeps the existing installation replacement policy', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest });
	await store.begin('lazy', [['Data/levels/jungle/a.wad', 1]]);
	await store.write('Data/levels/jungle/a.wad', bytes('a'));
	await store.flush();
	const tpi = [['Data/Ver.txt', 1], ['Data/levels/water/a.wad', 1]];
	await store.begin('lazy', tpi);
	assert.equal(store.has('Data/levels/jungle/a.wad'), false);
	for (const [path] of tpi) await store.write(path, bytes('t'));
	await store.flush();
	const restored = await openOriginalStore(root, { digest });
	assert.deepEqual(Object.keys(restored.manifest.files).sort(), tpi.map(([path]) => path).sort());
	assert.deepEqual(plan(tpi.map(([path]) => [path]), 'water').levels, []);
});

test('a filtered actual directory witness survives cache reopening without cached or imported media', async () => {
	const root = new FakeDirectory(); let store = await openOriginalStore(root, { digest });
	const sources = [['Data/Ver.txt', null, 1], ['Data/levels/water/a.wad', null, 1], ['Data/levels/space/preview.mpg', null, 1000]];
	const planned = plan(sources);
	await store.begin('lazy', planned.manifestSources.map(([path, , size]) => [path, size]));
	await store.write('Data/Ver.txt', bytes('v')); await store.flush();
	store = await openOriginalStore(root, { digest });
	assert.equal(store.has('Data/levels/space/preview.mpg'), false);
	assert.equal(store.manifest.version, 1);
	assert.equal(store.manifest.files['Data/levels/space/preview.mpg'].stored, false);
	assert.equal(store.manifest.files['Data/levels/space/preview.mpg'].sha256, undefined);
	assert.equal(await store.read('Data/levels/space/preview.mpg'), null);
	const reopened = plan(Object.entries(store.manifest.files).map(([path, entry]) => [path, () => store.read(path), entry.size]));
	assert.deepEqual(reopened.themeDirectories, ['Data/levels/water', 'Data/levels/space']);
	assert.deepEqual(reopened.start.map(([path]) => path), ['Data/Ver.txt']);
	assert.equal(reopened.all.some(([path]) => path.endsWith('.mpg')), false);
	let witnesses; const loaded = [];
	await importStartup({ BeginImport(dirs) { witnesses = dirs; }, LevelLoaded(theme) { loaded.push(theme); } }, reopened, async sources => {
		for (const [, read] of sources) assert.equal((await read()).byteLength, 1);
	});
	assert.deepEqual(witnesses, reopened.themeDirectories); assert.deepEqual(loaded, []);
});

// A stand-in for navigator.locks: one holder per name, `ifAvailable` answers null when it is taken, a lock lasts until its callback's promise settles.
class FakeLocks {
	constructor() { this.held = new Set(); }
	async request(name, options, callback) {
		if (this.held.has(name))
			return callback(null);
		this.held.add(name);
		try { return await callback({ name }); } finally { this.held.delete(name); }
	}
}

test('a stopped page gives the keeping lock back so another tab can claim it', async () => {
	const locks = new FakeLocks();
	const first = await claimKeeping(locks);
	assert.equal(first.acquired, true);
	const second = await claimKeeping(locks);
	assert.equal(second.acquired, false);
	assert.equal(second.unavailable, false);
	second.release();
	assert.equal((await claimKeeping(locks)).acquired, false, 'a tab that never held the lock cannot free it');
	first.release();
	await new Promise(resolve => setImmediate(resolve));
	const third = await claimKeeping(locks);
	assert.equal(third.acquired, true);
	third.release();
});

test('keeping is reported unavailable without Web Locks, and a failing request does not hang', async () => {
	assert.deepEqual({ ...await claimKeeping(null), release: undefined }, { acquired: false, release: undefined, unavailable: true });
	const broken = await claimKeeping({ request: async () => { throw new Error('denied'); } });
	assert.equal(broken.acquired, false);
	assert.equal(broken.unavailable, false);
	const throwing = await claimKeeping({ request() { throw new Error('denied'); } });
	assert.equal(throwing.acquired, false);
});

test('a note on the manifest (where the files came from) is written by the flush and survives a reopen', async () => {
	const root = new FakeDirectory();
	const first = await openOriginalStore(root, { digest: async () => 'x' });
	await first.begin('all', [['Data/a.wad', 3]]);
	first.annotate('source', 'image');
	await first.flush();
	assert.equal((await openOriginalStore(root, { digest: async () => 'x' })).manifest.source, 'image');
	first.annotate('source', null);
	await first.flush();
	assert.equal((await openOriginalStore(root, { digest: async () => 'x' })).manifest.source, undefined);
});
