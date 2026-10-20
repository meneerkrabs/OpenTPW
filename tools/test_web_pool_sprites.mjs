import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

// The production file is an ES module without requiring a package.json or a test dependency.
const source = await readFile(new URL('../source/OpenTPW.Web/wwwroot/opentpw-pool-sprites.js', import.meta.url), 'utf8');
const { parseManifest, planSprites, fetchSprites, serialRequests } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const shipped = await readFile(new URL('../content/pools/esprites.manifest.json', import.meta.url), 'utf8');

const hash = 'a'.repeat(64);
const file = (path, size = 4) => ({ path, size, sha256: hash });
const files = [file('Generic/Kids/SPR_BE.ESP'), file('Generic/Kidsheads/SPR_BE.ESP'), file('Swim/SPR_BE.ESP'), file('Swim/SPR_BE.TPC')];
const paths = list => list.map(item => item.path);

test('the shipped manifest is a valid list with both groups', () => {
	const list = parseManifest(shipped);
	assert.ok(list.some(item => item.path.startsWith('Swim/')) && list.some(item => item.path.startsWith('Generic/Kids/')));
	assert.equal(new Set(paths(list)).size, list.length);
});

test('with the pools extension off nothing is fetched', () => {
	assert.deepEqual(planSprites(files, { pools: false, guestArt: false }), []);
	assert.deepEqual(planSprites(files, { pools: false, guestArt: true }), []);
});

test('the swim sets are needed whenever pools is on; the land sets only with the guest art extension, and first', () => {
	assert.deepEqual(paths(planSprites(files, { pools: true, guestArt: false })), ['Swim/SPR_BE.ESP', 'Swim/SPR_BE.TPC']);
	assert.deepEqual(paths(planSprites(files, { pools: true, guestArt: true })), ['Generic/Kids/SPR_BE.ESP', 'Generic/Kidsheads/SPR_BE.ESP', 'Swim/SPR_BE.ESP', 'Swim/SPR_BE.TPC']);
});

test('a malformed manifest is refused', () => {
	for (const bad of ['{}', '{"format":2,"files":[]}', JSON.stringify({ format: 1, files: [file('../x')] }), JSON.stringify({ format: 1, files: [file('/abs')] }),
		JSON.stringify({ format: 1, files: [file('a'), file('a')] }), JSON.stringify({ format: 1, files: [file('a', 0)] }), JSON.stringify({ format: 1, files: [{ path: 'a', size: 4, sha256: 'xyz' }] })])
		assert.throws(() => parseManifest(bad));
});

const response = (bytes, status = 200) => ({ ok: status === 200, status, headers: new Map(), arrayBuffer: async () => bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) });

test('every planned file is fetched once with bounded concurrency and stored under its path', async () => {
	let active = 0, peak = 0;
	const urls = [], stored = [];
	const many = Array.from({ length: 10 }, (_, index) => file(`Swim/F${index}.ESP`));
	const ok = await fetchSprites({
		files: many, concurrency: 3,
		fetcher: async url => { urls.push(url); active++; peak = Math.max(peak, active); await new Promise(resolve => setTimeout(resolve, 5)); active--; return response(new Uint8Array(4)); },
		store: async (path, bytes) => { stored.push([path, bytes.length]); return true; },
	});
	assert.equal(ok, true);
	assert.equal(peak, 3);
	assert.deepEqual(stored.map(([path]) => path).sort(), paths(many).sort());
	assert.ok(urls.every(url => url.startsWith('pools-sprites/Swim/') && url.endsWith('?v=' + hash.slice(0, 16))));
});

test('a 404 stops the fetch with one report and no retry', async () => {
	let calls = 0, reports = 0;
	const ok = await fetchSprites({
		files, concurrency: 1, fetcher: async () => { calls++; return response(new Uint8Array(0), 404); },
		store: async () => true, failed: () => reports++,
	});
	assert.equal(ok, false);
	assert.equal(calls, 1, 'the remaining files are not requested');
	assert.equal(reports, 1);
});

test('being offline is one report', async () => {
	let reports = 0;
	assert.equal(await fetchSprites({ files, concurrency: 3, fetcher: async () => { throw new TypeError('Failed to fetch'); }, store: async () => true, failed: () => reports++ }), false);
	assert.equal(reports, 1);
});

test('a wrong size, such as an HTML page served for a missing file, is never handed to the game', async () => {
	const stored = [];
	let reports = 0;
	assert.equal(await fetchSprites({ files: [file('Swim/A.ESP', 4)], fetcher: async () => response(new TextEncoder().encode('<!doctype html>')), store: async (...args) => stored.push(args), failed: () => reports++ }), false);
	assert.deepEqual(stored, []);
	assert.equal(reports, 1);
});

test('a file the game refuses ends the fetch', async () => {
	let reports = 0;
	assert.equal(await fetchSprites({ files, concurrency: 1, fetcher: async () => response(new Uint8Array(4)), store: async () => false, failed: () => reports++ }), false);
	assert.equal(reports, 1);
});

test('the concurrency stays between one and four', async () => {
	for (const concurrency of [0, 5, 1.5])
		await assert.rejects(fetchSprites({ files, concurrency, fetcher: async () => response(new Uint8Array(4)), store: async () => true }));
});

function streamed(chunks, headers = new Map()) {
	let cancelled = false, index = 0;
	return {
		cancelled: () => cancelled,
		response: { ok: true, status: 200, headers, body: { getReader: () => ({
			read: async () => index < chunks.length ? { value: chunks[index++], done: false } : { value: undefined, done: true },
			cancel: async () => { cancelled = true; }, releaseLock() {},
		}) }, arrayBuffer: async () => { throw new Error('the whole body must not be read at once'); } },
	};
}

test('a streamed body is read in chunks and stored when it has exactly the pinned size', async () => {
	const stored = [];
	const { response } = streamed([new Uint8Array([1, 2]), new Uint8Array([3, 4])]);
	assert.equal(await fetchSprites({ files: [file('Swim/A.ESP', 4)], fetcher: async () => response, store: async (path, bytes) => { stored.push([...bytes]); return true; } }), true);
	assert.deepEqual(stored, [[1, 2, 3, 4]]);
});

test('a body past the pinned size is cancelled at once, never held whole', async () => {
	const body = streamed([new Uint8Array(3), new Uint8Array(3), new Uint8Array(1000)]);
	let reports = 0;
	assert.equal(await fetchSprites({ files: [file('Swim/A.ESP', 4)], fetcher: async () => body.response, store: async () => true, failed: () => reports++ }), false);
	assert.equal(body.cancelled(), true);
	assert.equal(reports, 1);
});

test('a refused file after the game has stopped is not reported', async () => {
	let reports = 0, running = true;
	const ok = await fetchSprites({ files, concurrency: 1, active: () => running, fetcher: async () => response(new Uint8Array(4)), store: async () => { running = false; return false; }, failed: () => reports++ });
	assert.equal(ok, false);
	assert.equal(reports, 0);
});

test('a fetch requested again while one runs is followed by exactly one more, and none after the game stopped', async () => {
	const tick = () => new Promise(resolve => setTimeout(resolve, 0));
	const gates = [];
	let runs = 0;
	const request = serialRequests(() => { runs++; return new Promise(resolve => gates.push(resolve)); });
	request();
	request();
	request();
	assert.equal(runs, 0, 'the run starts on the next turn, not inside the request');
	await tick();
	assert.equal(runs, 1);
	gates.shift()();
	await tick();
	assert.equal(runs, 2, 'one more run for the requests that came meanwhile');
	gates.shift()();
	await tick();
	assert.equal(runs, 2, 'and no third');
	let running = true;
	const stopped = serialRequests(() => { runs++; return new Promise(resolve => gates.push(resolve)); }, () => running);
	stopped();
	stopped();
	await tick();
	assert.equal(runs, 3);
	running = false;
	gates.shift()();
	await tick();
	assert.equal(runs, 3, 'a game that stopped asks for nothing more');
});
