import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { deflateRawSync } from 'node:zlib';

// The page module as shipped. Every fixture below is synthetic: no game data is read or committed.
const source = await readFile(new URL('../source/OpenTPW.Web/wwwroot/opentpw-cdimage.js', import.meta.url), 'utf8');
const cd = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const { ImageError, parseCue, checkCue, detectLayout, userSectors, readVolume, pickImage, zipStreamChunks, blobSource, urlSource, extractImage } = cd;

const SECTOR = 2048;
const bytes = text => new TextEncoder().encode(text);
const fill = (size, seed) => Uint8Array.from({ length: size }, (_, index) => (index * 7 + seed) & 255);

// ---- fixture builders ----

function record(name, lba, size, directory, extra = {}) {
	const length = 33 + name.length + (name.length % 2 === 0 ? 1 : 0);
	const out = new Uint8Array(length);
	const view = new DataView(out.buffer);
	out[0] = length;
	view.setUint32(2, lba, true); view.setUint32(6, lba, false);
	view.setUint32(10, size, true); view.setUint32(14, size, false);
	out[25] = (directory ? 2 : 0) | (extra.flags ?? 0);
	out[26] = extra.unit ?? 0;
	out[27] = extra.gap ?? 0;
	view.setUint16(28, 1, true); view.setUint16(30, 1, false);
	out[32] = name.length;
	out.set(name, 33);
	return out;
}
const ucs2 = text => Uint8Array.from([...text].flatMap(char => [char.charCodeAt(0) >> 8, char.charCodeAt(0) & 255]));

/** Builds an ISO 9660 image from { path: Uint8Array } with a plain tree (upper case) and, when asked, a Joliet one (given case). */
function buildIso(files, { joliet = true, label = 'TESTDISC', extras = {} } = {}) {
	const tree = { dirs: new Map(), files: [] };
	for (const [path, contents] of Object.entries(files)) {
		const parts = path.split('/');
		let node = tree;
		for (const part of parts.slice(0, -1)) {
			if (!node.dirs.has(part)) node.dirs.set(part, { dirs: new Map(), files: [] });
			node = node.dirs.get(part);
		}
		node.files.push([parts.at(-1), contents, path]);
	}
	let next = 16 + (joliet ? 3 : 2);
	const extents = new Map();
	const assignDirs = node => { node.lba = next++; for (const child of node.dirs.values()) assignDirs(child); };
	assignDirs(tree);
	const plainRoot = tree.lba;
	let jolietRoot = null;
	if (joliet) {
		const jolietTree = { dirs: tree.dirs, files: tree.files };
		jolietRoot = next;
		// the Joliet tree needs its own directory sectors
		const copy = node => { const out = { dirs: new Map(), files: node.files, lba: next++ }; for (const [name, child] of node.dirs) out.dirs.set(name, copy(child)); return out; };
		tree.joliet = copy(jolietTree);
	}
	for (const [path, contents] of Object.entries(files)) {
		extents.set(path, { lba: contents.length === 0 ? 0 : next, size: contents.length });
		next += Math.ceil(contents.length / SECTOR);
	}
	const image = new Uint8Array(next * SECTOR);
	const putDir = (node, parentLba, wide) => {
		const records = [record(Uint8Array.of(0), node.lba, SECTOR, true), record(Uint8Array.of(1), parentLba, SECTOR, true)];
		for (const [name, child] of node.dirs) records.push(record(wide ? ucs2(name) : bytes(name.toUpperCase()), child.lba, SECTOR, true));
		for (const [name, , path] of node.files) {
			const extent = extents.get(path);
			records.push(record(wide ? ucs2(`${name};1`) : bytes(`${name.toUpperCase()};1`), extent.lba, extent.size, false, extras[path] ?? {}));
		}
		let at = node.lba * SECTOR;
		for (const entry of records) { image.set(entry, at); at += entry.length; }
		for (const [name, child] of node.dirs) putDir(child, node.lba, wide);
	};
	putDir(tree, tree.lba, false);
	if (joliet) putDir(tree.joliet, tree.joliet.lba, true);
	const descriptor = (type, root, escape) => {
		const out = new Uint8Array(SECTOR);
		out[0] = type; out.set(bytes('CD001'), 1); out[6] = 1;
		out.set(bytes(label.padEnd(32)), 40);
		new DataView(out.buffer).setUint32(80, next, true);
		if (escape) out.set(escape, 88);
		out.set(record(Uint8Array.of(0), root, SECTOR, true).subarray(0, 34), 156);
		return out;
	};
	image.set(descriptor(1, plainRoot), 16 * SECTOR);
	let at = 17;
	if (joliet) image.set(descriptor(2, tree.joliet.lba, Uint8Array.of(0x25, 0x2F, 0x45)), at++ * SECTOR);
	image.set(Uint8Array.from([255, ...bytes('CD001'), 1]), at * SECTOR);
	for (const [path, contents] of Object.entries(files)) image.set(contents, extents.get(path).lba * SECTOR);
	return image;
}

/** The raw CD form of an ISO: 2352-byte sectors with sync, header and a recognisable EDC/ECC filler. */
function rawify(iso, mode = 1) {
	const count = iso.length / SECTOR;
	const out = new Uint8Array(count * 2352);
	for (let index = 0; index < count; index++) {
		const at = index * 2352;
		out.fill(255, at + 1, at + 11);
		out[at + 15] = mode;
		const offset = mode === 1 ? 16 : 24;
		out.set(iso.subarray(index * SECTOR, (index + 1) * SECTOR), at + offset);
		out.fill(0xEE, at + offset + SECTOR, at + 2352);
	}
	return out;
}

async function* chunked(data, size) {
	for (let at = 0; at < data.length; at += size)
		yield data.slice(at, at + size);
}

function zipOf(entries) {
	const local = [];
	const central = [];
	let offset = 0;
	const u16 = value => Uint8Array.of(value & 255, value >> 8 & 255);
	const u32 = value => Uint8Array.of(value & 255, value >> 8 & 255, value >> 16 & 255, value >>> 24);
	const join = parts => { const out = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0)); let at = 0; for (const part of parts) { out.set(part, at); at += part.length; } return out; };
	for (const { name, data, method = 8, flags = 0, rawCompressed = null, descriptor = null } of entries) {
		const compressed = rawCompressed ?? (method === 8 ? deflateRawSync(data) : data);
		const nameBytes = bytes(name);
		// descriptor: 'signed' or 'plain' writes the sizes in the local header and a data descriptor after the data (flag bit 3)
		const sized = !(flags & 8) || descriptor !== null;
		const header = join([u32(0x04034B50), u16(20), u16(flags), u16(method), u16(0), u16(0), u32(0), u32(sized ? compressed.length : 0), u32(sized ? data.length : 0), u16(nameBytes.length), u16(0), nameBytes]);
		const trailer = descriptor === null ? new Uint8Array(0) : join([...(descriptor === 'signed' ? [u32(0x08074B50)] : []), u32(0), u32(compressed.length), u32(data.length)]);
		local.push(header, compressed, trailer);
		central.push(join([u32(0x02014B50), u16(20), u16(20), u16(flags), u16(method), u16(0), u16(0), u32(0), u32(compressed.length), u32(data.length), u16(nameBytes.length), u16(0), u16(0), u16(0), u16(0), u32(0), u32(offset), nameBytes]));
		offset += header.length + compressed.length + trailer.length;
	}
	const directory = join(central);
	return join([...local, directory, u32(0x06054B50), u16(0), u16(0), u16(entries.length), u16(entries.length), u32(directory.length), u32(offset), u16(0)]);
}

const GAME = {
	'Data/ui.wad': fill(5000, 1),
	'Data/levels/jungle/terrain.wad': fill(2048 * 3, 2),
	'Data/movies/intro.mpg': fill(9000, 3),
	'Dutch/data/language/Dutch/ERRORMSG.str': fill(300, 4),
	'Data/empty.sam': new Uint8Array(0),
	'README.TXT': fill(10, 5),
};
const chooseData = files => files.filter(file => /^(Data|Dutch)\//i.test(file.path) && !/movies/i.test(file.path)).map(file => file.path);

function collector({ have = () => false } = {}) {
	const state = { written: new Map(), order: [], begun: null, progress: [] };
	return {
		state,
		options: {
			choose: chooseData,
			begin: async taken => { assert.equal(state.written.size, 0, 'begin comes before the first write'); state.begun = taken; },
			have,
			write: async (path, contents) => { state.written.set(path, contents.slice()); state.order.push(path); },
			progress: info => state.progress.push(info),
		},
	};
}

const expected = path => GAME[path];
const wantedPaths = Object.keys(GAME).filter(path => chooseData(Object.keys(GAME).map(name => ({ path: name }))).includes(path));

// ---- cue, layout, sectors ----

test('a cue sheet gives its tracks and a leading audio track is refused', () => {
	const cue = 'REM x\nFILE "TPWORLD.bin" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n';
	assert.deepEqual(parseCue(cue), [{ file: 'TPWORLD.bin', number: 1, mode: 'MODE1/2352' }]);
	assert.equal(checkCue(cue), 'MODE1/2352');
	assert.throws(() => checkCue('FILE a.bin BINARY\nTRACK 01 AUDIO\n'), ImageError);
	assert.throws(() => checkCue('nothing'), ImageError);
});

test('the sector layout comes from the sync pattern and mode byte', () => {
	const raw = new Uint8Array(32);
	raw.fill(255, 1, 11); raw[15] = 1;
	assert.deepEqual({ ...detectLayout(raw) }, { stride: 2352, offset: 16, raw: true, mode: 1 });
	raw[15] = 2;
	assert.equal(detectLayout(raw).offset, 24);
	raw[15] = 0;
	assert.throws(() => detectLayout(raw), ImageError);
	assert.deepEqual({ ...detectLayout(new Uint8Array(32)) }, { stride: 2048, offset: 0, raw: false });
	assert.throws(() => detectLayout(new Uint8Array(4)), ImageError);
});

for (const mode of [1, 2]) {
	test(`raw Mode ${mode} sectors become 2048-byte user data whatever the chunk size`, async () => {
		const iso = buildIso(GAME);
		const raw = rawify(iso, mode);
		for (const size of [1000, 2352, 65536, raw.length]) {
			const parts = [];
			for await (const batch of userSectors(chunked(raw, size)))
				parts.push(batch.data);
			const joined = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0));
			let at = 0;
			for (const part of parts) { joined.set(part, at); at += part.length; }
			assert.deepEqual(joined, iso, `chunk size ${size}`);
		}
	});
}

test('a plain ISO passes through, a cut-off image is reported, a sector without sync is marked bad', async () => {
	const iso = buildIso(GAME);
	const parts = [];
	for await (const batch of userSectors(chunked(iso, 3000)))
		parts.push(batch);
	assert.equal(parts[0].lba, 0);
	assert.equal(parts.reduce((sum, batch) => sum + batch.count, 0), iso.length / SECTOR);
	const raw = rawify(iso);
	const broken = raw.slice();
	broken[2352 * 5 + 3] = 9;
	const bad = [];
	for await (const batch of userSectors(chunked(broken, 5000)))
		bad.push(...batch.bad);
	assert.deepEqual(bad, [5]);
	await assert.rejects(async () => { for await (const unused of userSectors(chunked(raw.subarray(0, raw.length - 100), 5000))); }, error => error.code === 'truncated');
});

test('sectors without sync outside the files (copy protection) are ignored, one under a wanted file is an error', async () => {
	const files = { 'Data/a.wad': fill(5000, 1), 'Data/b.wad': fill(5000, 2) };
	const iso = buildIso(files);
	const volume = readVolume(readerOf(iso));
	const a = volume.files.find(file => file.path === 'Data/a.wad');
	const b = volume.files.find(file => file.path === 'Data/b.wad');
	const raw = rawify(iso);
	const lastSector = Math.max(a.lba, b.lba) + 3;
	raw[2352 * lastSector + 5] = 9; // after both files: harmless
	const { state, options } = collector();
	await extractImage({ ...options, choose: list => list.map(file => file.path), source: { random: false, open: () => ({ chunks: chunked(raw, 6000) }) } });
	assert.equal(state.written.size, 2);
	const hit = rawify(iso);
	hit[2352 * (a.lba + 1) + 4] = 9;
	await assert.rejects(extractImage({ ...collector().options, choose: list => list.map(file => file.path), source: { random: false, open: () => ({ chunks: chunked(hit, 6000) }) } }), error => error.code === 'damaged' && /a\.wad/.test(error.message));
	await assert.rejects(extractImage({ ...collector().options, choose: list => list.map(file => file.path), source: { random: true, total: hit.length, read: async (start, length) => hit.slice(start, start + length) } }), error => error.code === 'damaged');
});

// ---- ISO 9660 ----

function readerOf(iso) {
	return (lba, count) => iso.subarray(lba * SECTOR, (lba + count) * SECTOR);
}

test('the Joliet tree wins: mixed-case names without version suffixes', () => {
	const volume = readVolume(readerOf(buildIso(GAME)));
	assert.equal(volume.joliet, true);
	assert.equal(volume.label, 'TESTDISC');
	assert.deepEqual(volume.files.map(file => file.path).sort(), Object.keys(GAME).sort());
	for (const file of volume.files)
		assert.equal(file.size, GAME[file.path].length);
});

test('without Joliet the plain upper-case names are used', () => {
	const volume = readVolume(readerOf(buildIso(GAME, { joliet: false })));
	assert.equal(volume.joliet, false);
	assert.ok(volume.files.some(file => file.path === 'DATA/UI.WAD'));
});

test('a file that is not an ISO is refused', () => {
	assert.throws(() => readVolume(() => new Uint8Array(SECTOR)), ImageError);
});

// ---- extraction from a stream ----

for (const [name, make] of [['a raw BIN', iso => rawify(iso)], ['a plain ISO', iso => iso]]) {
	test(`extractImage takes only the chosen files out of ${name} streamed in odd chunks`, async () => {
		const iso = buildIso(GAME);
		const image = make(iso);
		const { state, options } = collector();
		const result = await extractImage({ ...options, source: { random: false, total: image.length, open: () => ({ chunks: chunked(image, 1234), total: image.length }) } });
		assert.deepEqual([...state.written.keys()].sort(), wantedPaths.sort());
		for (const [path, contents] of state.written)
			assert.deepEqual(contents, expected(path), path);
		assert.deepEqual(state.begun.map(file => file.path).sort(), wantedPaths.sort());
		assert.equal(result.files, wantedPaths.length);
		assert.ok(state.progress.some(info => info.bytes > 0));
	});
}

test('files already kept are neither buffered nor written, and the stream stops after the last wanted file', async () => {
	const iso = buildIso({ ...GAME, 'Data/zz-last.wad': fill(4096, 9), 'Data/trailing-junk.bin': fill(200000, 8) });
	const image = rawify(iso);
	let served = 0;
	async function* counting() { for await (const chunk of chunked(image, 4096)) { served += chunk.length; yield chunk; } }
	const chosen = files => files.filter(file => /^Data\/(ui\.wad|zz-last\.wad)$/.test(file.path)).map(file => file.path);
	const { state, options } = collector({ have: path => path === 'Data/ui.wad' });
	await extractImage({ ...options, choose: chosen, source: { random: false, total: image.length, open: () => ({ chunks: counting(), total: image.length }) } });
	assert.deepEqual([...state.written.keys()], ['Data/zz-last.wad']);
	assert.ok(served < image.length, 'the rest of the image is not read');
});

test('a truncated stream says how many files are missing', async () => {
	const image = rawify(buildIso(GAME));
	const { options } = collector();
	await assert.rejects(extractImage({ ...options, source: { random: false, open: () => ({ chunks: chunked(image.subarray(0, 2352 * 20), 5000) }) } }), error => error instanceof ImageError && error.code === 'truncated');
});

test('an image without the wanted files is refused by the chooser before anything is written', async () => {
	const image = buildIso({ 'Other/file.txt': fill(10, 1) });
	const { state, options } = collector();
	await assert.rejects(extractImage({ ...options, choose: () => { throw new ImageError('no Data folder'); }, source: { random: false, open: () => ({ chunks: chunked(image, 9999) }) } }), /no Data folder/);
	assert.equal(state.written.size, 0);
});

test('cancelling stops the extraction', async () => {
	const image = rawify(buildIso(GAME));
	const controller = new AbortController();
	const { options } = collector();
	const run = extractImage({ ...options, signal: controller.signal, progress: () => controller.abort(), source: { random: false, open: () => ({ chunks: chunked(image, 3000) }) } });
	await assert.rejects(run, error => error.name === 'AbortError');
});

test('a directory tree beyond the head limit is refused with advice', async () => {
	const iso = buildIso(GAME);
	// point the root at a sector far away
	const view = new DataView(iso.buffer);
	view.setUint32(16 * SECTOR + 156 + 2, 50000, true);
	view.setUint32(18 * SECTOR + 156 + 2, 50000, true);
	view.setUint32(17 * SECTOR + 156 + 2, 50000, true);
	const { options } = collector();
	await assert.rejects(extractImage({ ...options, source: { random: false, open: () => ({ chunks: chunked(iso, 65536) }) } }), /first 32 MB/);
});

// ---- ZIP ----

test('a streamed ZIP skips other members without inflating them and inflates the image', async () => {
	const image = rawify(buildIso(GAME));
	const archive = zipOf([
		{ name: 'EXTRA/', data: new Uint8Array(0), method: 0 },
		{ name: 'EXTRA/other.dat', data: fill(500, 1), rawCompressed: Uint8Array.from([0xFF, 0xFF, 0xFF, 0xFF, 0xFF]) },
		{ name: 'DISC.bin', data: image },
		{ name: 'DISC.cue', data: bytes('x') },
	]);
	const inflated = [];
	for await (const chunk of zipStreamChunks(chunked(archive, 777)))
		inflated.push(chunk);
	assert.deepEqual(Buffer.concat(inflated), Buffer.from(image));
});

test('a stored image in a ZIP is passed through', async () => {
	const iso = buildIso(GAME);
	const archive = zipOf([{ name: 'a.iso', data: iso, method: 0 }]);
	const parts = [];
	for await (const chunk of zipStreamChunks(chunked(archive, 4000)))
		parts.push(chunk);
	assert.deepEqual(Buffer.concat(parts), Buffer.from(iso));
});

test('a ZIP without an image, with streamed sizes, or with an unknown method is refused clearly', async () => {
	const none = zipOf([{ name: 'readme.txt', data: bytes('hi') }]);
	await assert.rejects(async () => { for await (const unused of zipStreamChunks(chunked(none, 100))); }, /no image/);
	const streamed = zipOf([{ name: 'a.bin', data: fill(100, 1), flags: 8 }]);
	await assert.rejects(async () => { for await (const unused of zipStreamChunks(chunked(streamed, 100))); }, error => error.code === 'unsupported');
	const odd = zipOf([{ name: 'a.bin', data: fill(100, 1), method: 12, rawCompressed: fill(10, 1) }]);
	await assert.rejects(async () => { for await (const unused of zipStreamChunks(chunked(odd, 100))); }, /method 12/);
});

test('pickImage prefers an ISO and never picks an executable', () => {
	assert.equal(pickImage([{ name: 'a.exe' }, { name: 'b.bin' }, { name: 'c.iso' }]).name, 'c.iso');
	assert.equal(pickImage([{ name: 'a.exe' }, { name: 'b.bin' }]).name, 'b.bin');
	assert.equal(pickImage([{ name: 'a.exe' }, { name: 'x/' }]), null);
});

// ---- local files ----

test('a local ZIP is read through its central directory', async () => {
	const iso = buildIso(GAME);
	const archive = zipOf([{ name: 'EXTRA/other.dat', data: fill(50, 1), rawCompressed: Uint8Array.from([0xFF, 0xFF]) }, { name: 'DISC.bin', data: rawify(iso) }]);
	const sourceOfZip = await blobSource(new Blob([archive]));
	assert.equal(sourceOfZip.name, 'DISC.bin');
	const { state, options } = collector();
	await extractImage({ ...options, source: sourceOfZip });
	for (const [path, contents] of state.written)
		assert.deepEqual(contents, expected(path), path);
	assert.equal(state.written.size, wantedPaths.length);
});

for (const [name, make] of [['BIN', iso => rawify(iso)], ['ISO', iso => iso]]) {
	test(`a local ${name} is read at random and only the wanted sectors are touched`, async () => {
		const image = make(buildIso(GAME));
		const blob = new Blob([image]);
		let read = 0;
		const original = await blobSource(blob);
		const counting = { ...original, read: async (start, length) => { const out = await original.read(start, length); read += out.length; return out; } };
		const { state, options } = collector();
		await extractImage({ ...options, source: counting });
		assert.equal(state.written.size, wantedPaths.length);
		for (const [path, contents] of state.written)
			assert.deepEqual(contents, expected(path), path);
		assert.ok(read < image.length + 100000);
	});
}

test('a local file that is not a CD image is refused', async () => {
	const { options } = collector();
	await assert.rejects(extractImage({ ...options, source: await blobSource(new Blob([fill(100000, 1)])) }), ImageError);
});

// ---- addresses ----

/** A fetch that serves `body` like a static server; `ranges` false answers 200 to everything. */
function fakeServer(body, { ranges = true, status = 200, cors = true } = {}) {
	const requests = [];
	// like a real response body: many small chunks, not one big one
	const streamOf = bytesOut => new ReadableStream({
		start(controller) { for (let at = 0; at < bytesOut.length; at += 32768) controller.enqueue(bytesOut.slice(at, at + 32768)); controller.close(); },
	});
	const fetchFn = async (url, { headers = {}, signal } = {}) => {
		requests.push({ url, range: headers.Range ?? null });
		if (!cors && headers.Range)
			throw new TypeError('Failed to fetch');
		if (status !== 200)
			return new Response('no', { status, statusText: 'Not Found' });
		const range = /^bytes=(\d*)-(\d*)$/.exec(headers.Range ?? '');
		if (ranges && range) {
			let [from, to] = [range[1] === '' ? null : Number(range[1]), range[2] === '' ? null : Number(range[2])];
			if (from === null) { from = Math.max(0, body.length - to); to = body.length - 1; }
			else to = Math.min(to ?? body.length - 1, body.length - 1);
			return new Response(streamOf(body.subarray(from, to + 1)), { status: 206 });
		}
		return new Response(streamOf(body), { status: 200, headers: { 'content-length': String(body.length) } });
	};
	return { fetchFn, requests };
}

test('a streamed raw image from an address (no range support) is extracted in one request', async () => {
	const image = rawify(buildIso(GAME));
	const { fetchFn, requests } = fakeServer(image, { ranges: false });
	const { state, options } = collector();
	await extractImage({ ...options, source: urlSource('https://example.test/disc.bin', { fetch: fetchFn }) });
	assert.equal(requests.length, 1);
	assert.equal(state.written.size, wantedPaths.length);
});

test('a ZIP address that answers ranges is read by its directory: the other members are never requested', async () => {
	const iso = buildIso(GAME);
	const exe = fill(4000, 3);
	const archive = zipOf([{ name: 'EXTRA/other.dat', data: exe, method: 0 }, { name: 'DISC.bin', data: rawify(iso) }]);
	const { fetchFn, requests } = fakeServer(archive);
	const { state, options } = collector();
	const address = urlSource('https://example.test/disc.zip', { fetch: fetchFn });
	await extractImage({ ...options, source: address });
	assert.equal(address.zipRanged, true);
	assert.equal(state.written.size, wantedPaths.length);
	assert.ok(requests.every(request => request.range), 'every request is ranged');
	const exeStart = archive.indexOf(exe.subarray(0, 64));
	for (const request of requests) {
		const match = /^bytes=(\d+)-(\d+)$/.exec(request.range);
		if (match) assert.ok(Number(match[1]) > exeStart + exe.length - 1 || Number(match[2]) < exeStart, `range ${request.range} stays out of the executable`);
	}
});

test('a ZIP address whose server refuses ranges falls back to one streamed request', async () => {
	const iso = buildIso(GAME);
	const archive = zipOf([{ name: 'EXTRA/other.dat', data: fill(300, 3), rawCompressed: Uint8Array.from([0xFF]) }, { name: 'DISC.bin', data: rawify(iso) }]);
	for (const server of [fakeServer(archive, { ranges: false }), fakeServer(archive, { cors: false })]) {
		const { state, options } = collector();
		const address = urlSource('https://example.test/disc.zip', { fetch: server.fetchFn });
		await extractImage({ ...options, source: address });
		assert.equal(address.zipRanged, false);
		assert.equal(state.written.size, wantedPaths.length);
		assert.equal(server.requests.at(-1).range, null);
	}
});

test('a long unwanted stretch is skipped with a range request, or read through when ranges are not answered', async () => {
	const files = { 'Data/first.wad': fill(5000, 1), 'Data/movies/big.mpg': fill(9 * 1048576, 2), 'Data/last.wad': fill(7000, 3) };
	const image = rawify(buildIso(files));
	const choose = list => list.filter(file => !/movies/.test(file.path)).map(file => file.path);
	for (const ranges of [true, false]) {
		const server = fakeServer(image, { ranges });
		const { state, options } = collector();
		const address = urlSource('https://example.test/disc.bin', { fetch: server.fetchFn });
		await extractImage({ ...options, choose, source: address });
		assert.deepEqual(state.written.get('Data/last.wad'), files['Data/last.wad']);
		assert.deepEqual(state.written.get('Data/first.wad'), files['Data/first.wad']);
		assert.equal(server.requests.some(request => request.range), true);
		assert.equal(address.rangeable === false, !ranges);
		// a server that ignores ranges is read once, from the start: the probe is cancelled, the stream is not restarted
		if (!ranges)
			assert.equal(server.requests.filter(request => !request.range).length, 1);
	}
});

test('an address declared range-less (archive.org member streams) is never asked for a range', async () => {
	const files = { 'Data/first.wad': fill(5000, 1), 'Data/movies/big.mpg': fill(9 * 1048576, 2), 'Data/last.wad': fill(7000, 3) };
	const image = rawify(buildIso(files));
	const server = fakeServer(image, { ranges: false });
	const { state, options } = collector();
	await extractImage({ ...options, choose: list => list.filter(file => !/movies/.test(file.path)).map(file => file.path), source: urlSource('https://example.test/disc.bin', { fetch: server.fetchFn, ranges: false }) });
	assert.deepEqual(state.written.get('Data/last.wad'), files['Data/last.wad']);
	assert.deepEqual(server.requests.map(request => request.range), [null]);
});

test('an HTTP error and a network failure are told apart', async () => {
	const { options } = collector();
	await assert.rejects(extractImage({ ...options, source: urlSource('https://example.test/missing.bin', { fetch: fakeServer(new Uint8Array(1), { status: 404 }).fetchFn }) }), error => error instanceof ImageError && error.code === 'http' && /404/.test(error.message));
	const down = async () => { throw new TypeError('Failed to fetch'); };
	await assert.rejects(extractImage({ ...options, source: urlSource('https://example.test/disc.bin', { fetch: down }) }), error => error instanceof ImageError && error.code === 'network');
});

test('a ZIP is never skipped into: a long unwanted stretch inside a zipped image is read through (regression)', async () => {
	const files = { 'Data/first.wad': fill(5000, 1), 'Data/movies/big.mpg': fill(9 * 1048576, 2), 'Data/last.wad': fill(7000, 3) };
	const archive = zipOf([{ name: 'DISC.bin', data: rawify(buildIso(files)) }]);
	const choose = list => list.filter(file => !/movies/.test(file.path)).map(file => file.path);
	const sources = [await blobSource(new Blob([archive])), urlSource('https://example.test/disc.zip', { fetch: fakeServer(archive, { ranges: false }).fetchFn }), urlSource('https://example.test/disc.zip', { fetch: fakeServer(archive).fetchFn })];
	for (const candidate of sources) {
		const { state, options } = collector();
		await extractImage({ ...options, choose, source: candidate });
		assert.deepEqual(state.written.get('Data/last.wad'), files['Data/last.wad']);
		assert.deepEqual(state.written.get('Data/first.wad'), files['Data/first.wad']);
	}
});

// ---- review follow-ups ----

/** The image with every file record of size `size` (little then big endian) changed to `to`: file records only (flag bit 1 clear). */
function resized(iso, size, to) {
	const patched = iso.slice();
	const view = new DataView(patched.buffer);
	for (let at = 10; at + 8 <= patched.length; at++)
		if (view.getUint32(at, true) === size && view.getUint32(at + 4, false) === size && (patched[at + 15] & 2) === 0 && patched[at - 10] > 33) {
			view.setUint32(at, to, true);
			view.setUint32(at + 4, to, false);
		}
	return patched;
}
const everything = list => list.map(file => file.path);

test('a streamed ZIP skips a member with a data descriptor (with or without its signature) and reaches the image', async () => {
	const iso = buildIso(GAME);
	for (const descriptor of ['signed', 'plain']) {
		const archive = zipOf([
			{ name: 'readme.txt', data: bytes('read me please'), method: 0, flags: 8, descriptor },
			{ name: 'disc.iso', data: iso, method: 0 },
		]);
		const parts = [];
		for await (const chunk of zipStreamChunks(chunked(archive, 333)))
			parts.push(chunk);
		assert.deepEqual(Buffer.concat(parts), Buffer.from(iso), descriptor);
	}
});

test('a failure of the connection in the middle of the stream is a network error, not a bare TypeError', async () => {
	const image = rawify(buildIso(GAME));
	const fetchFn = async () => new Response(new ReadableStream({
		start(controller) { controller.enqueue(image.slice(0, 30000)); },
		pull() { throw new TypeError('network error'); },
	}), { status: 200 });
	const { options } = collector();
	await assert.rejects(extractImage({ ...options, source: urlSource('https://example.test/disc.bin', { fetch: fetchFn }) }), error => error.code === 'network');
});

test('a wanted file over 64 MB, or beyond the end of a known-size image, is refused', async () => {
	const iso = buildIso({ 'Data/a.wad': fill(5000, 1) });
	const { options } = collector();
	await assert.rejects(extractImage({ ...options, choose: everything, source: { random: false, open: () => ({ chunks: chunked(resized(iso, 5000, 65 * 1048576), 65536) }) } }), /64 MB/);
	const file = readVolume(readerOf(iso)).files[0];
	const cut = iso.slice(0, file.lba * SECTOR);
	await assert.rejects(extractImage({ ...options, choose: everything, source: { random: false, total: cut.length, open: () => ({ chunks: chunked(cut, 65536), total: cut.length }) } }), error => error.code === 'truncated');
	await assert.rejects(extractImage({ ...options, choose: everything, source: { random: true, total: cut.length, read: async (start, length) => cut.slice(start, start + length) } }), error => error.code === 'truncated');
});

test('overlapping wanted files do not make the extraction hold unbounded memory', async () => {
	const iso = resized(buildIso({ 'Data/a.wad': fill(2048, 1), 'Data/b.wad': fill(2048, 2), 'Data/c.wad': fill(2048, 3) }), 2048, 60 * 1048576);
	const { options } = collector();
	await assert.rejects(extractImage({ ...options, choose: everything, source: { random: false, open: () => ({ chunks: chunked(iso, 1 << 20) }) } }), error => error instanceof ImageError && /overlapping/.test(error.message));
});

test('a damaged directory record is skipped; interleaved, associated and multi-extent wanted files are refused', async () => {
	const files = { 'Data/a.wad': fill(3000, 1), 'Data/b.wad': fill(3000, 2) };
	for (const [extra, word] of [[{ unit: 1 }, /interleaved/], [{ gap: 1 }, /interleaved/], [{ flags: 4 }, /associated/], [{ flags: 0x80 }, /several parts/]]) {
		const iso = buildIso(files, { extras: { 'Data/b.wad': extra } });
		await assert.rejects(extractImage({ ...collector().options, choose: everything, source: { random: false, open: () => ({ chunks: chunked(iso, 65536) }) } }), word);
		const { state, options } = collector();
		await extractImage({ ...options, choose: list => list.filter(file => file.path === 'Data/a.wad').map(file => file.path), source: { random: false, open: () => ({ chunks: chunked(iso, 65536) }) } });
		assert.deepEqual([...state.written.keys()], ['Data/a.wad']);
	}
	// a record whose name runs past its own length is skipped, never read beyond itself
	const iso = buildIso(files, { joliet: false });
	const root = new DataView(iso.buffer).getUint32(16 * SECTOR + 156 + 2, true);
	let at = root * SECTOR;
	at += iso[at]; at += iso[at]; // . and ..
	iso[at + 32] = 200; // the first entry's name length now exceeds its record
	assert.deepEqual(readVolume(readerOf(iso)).files, []);
});

test('an image that lists more than 100,000 files is refused', () => {
	const iso = buildIso({ 'x.txt': fill(10, 1) }, { joliet: false });
	const sectors = [];
	let current = [];
	let used = 0;
	for (let index = 0; index < 100001; index++) {
		const entry = record(bytes(`F${index}.TXT;1`), 0, 0, false);
		if (used + entry.length > SECTOR) { sectors.push(current); current = []; used = 0; }
		current.push(entry);
		used += entry.length;
	}
	sectors.push(current);
	const big = new Uint8Array(sectors.length * SECTOR);
	sectors.forEach((entries, index) => { let offset = index * SECTOR; for (const entry of entries) { big.set(entry, offset); offset += entry.length; } });
	const image = new Uint8Array(iso.length + big.length);
	image.set(iso);
	image.set(big, iso.length);
	const view = new DataView(image.buffer);
	view.setUint32(16 * SECTOR + 156 + 2, iso.length / SECTOR, true);
	view.setUint32(16 * SECTOR + 156 + 10, big.length, true);
	assert.throws(() => readVolume(readerOf(image)), /more than 100000 files/);
});

test('a Mode 2 Form 2 sector is not file data', async () => {
	const iso = buildIso({ 'Data/a.wad': fill(5000, 1) });
	const raw = rawify(iso, 2);
	const file = readVolume(readerOf(iso)).files[0];
	raw[2352 * (file.lba + 1) + 18] = 0x20;
	const bad = [];
	for await (const batch of userSectors(chunked(raw, 5000)))
		bad.push(...batch.bad);
	assert.deepEqual(bad, [file.lba + 1]);
	await assert.rejects(extractImage({ ...collector().options, choose: everything, source: { random: false, open: () => ({ chunks: chunked(raw, 5000) }) } }), error => error.code === 'damaged');
});

test('a local BIN reports its progress against the wanted bytes and reaches 100%', async () => {
	const { options, state } = collector();
	await extractImage({ ...options, source: await blobSource(new Blob([rawify(buildIso(GAME))])) });
	const last = state.progress.at(-1);
	assert.equal(last.bytes, last.total);
	assert.equal(last.total, wantedPaths.reduce((sum, path) => sum + GAME[path].length, 0));
});
