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
