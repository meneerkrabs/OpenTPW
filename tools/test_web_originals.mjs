import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

// The page module as shipped, without a package.json or a test dependency (as test_web_textures.mjs does).
const source = await readFile(new URL('../source/OpenTPW.Web/wwwroot/opentpw-originals.js', import.meta.url), 'utf8');
const { MODES, openOriginalStore, supported } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

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

test('a failed pre-replacement manifest flush retries invalidation before changing bytes', async () => {
	const root = new FakeDirectory();
	const store = await openOriginalStore(root, { digest });
	await store.begin('lazy', folder);
	await store.write('Data/ui.wad', bytes('ui'));
	await store.flush();
	const directory = await root.getDirectoryHandle('opentpw-originals');
	rejectNext(await directory.getFileHandle('manifest.json'), 'write');
	await assert.rejects(store.write('Data/ui.wad', bytes('UI')));
	const previous = await openOriginalStore(root, { digest });
	assert.equal(new TextDecoder().decode(await previous.read('Data/ui.wad')), 'ui');
	await store.write('Data/ui.wad', bytes('UI'));
	assert.equal((await openOriginalStore(root, { digest })).has('Data/ui.wad'), false);
	await store.flush();
	assert.equal(new TextDecoder().decode(await (await openOriginalStore(root, { digest })).read('Data/ui.wad')), 'UI');
});

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
