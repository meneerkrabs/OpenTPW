// Reads the game's data files out of a CD image in the browser (docs/WEB.md, "Game data sources"): a raw BIN dump (2352-byte
// sectors) or an ISO (2048-byte sectors), loose or inside a ZIP, from a local file or an address. The image is never held in memory
// and never stored whole: sectors are converted to their 2048-byte user data as they arrive, the ISO 9660 directory tree is read
// from the first sectors, and each wanted file is handed to `write` as soon as its last sector has passed. Only data files are
// taken: a ZIP's other members (a crack, a tool) are skipped without being inflated, and nothing here reads an executable.
// No imports, so the unit tests load this file as it ships.

export const USER_SECTOR = 2048;
const RAW_SECTOR = 2352;
/** Largest part of an image the page keeps in memory while it looks for the directory tree (sectors; 32 MB). */
const HEAD_LIMIT = 16384;
/** A gap this large between wanted files is skipped with a ranged request, when the server answers ranges. */
const SKIP_BYTES = 8 * 1048576;

/** A problem with the image itself (not a network or storage failure): `code` tells what kind. */
export class ImageError extends Error {
	constructor(message, code = 'format') {
		super(message);
		this.name = 'ImageError';
		this.code = code;
	}
}

class NeedMore extends Error {
	constructor(lba) {
		super(`sector ${lba} is not here yet`);
		this.lba = lba;
	}
}

const u16 = (bytes, at) => bytes[at] | bytes[at + 1] << 8;
const u32 = (bytes, at) => (bytes[at] | bytes[at + 1] << 8 | bytes[at + 2] << 16 | bytes[at + 3] << 24) >>> 0;

function concat(first, second) {
	const joined = new Uint8Array(first.length + second.length);
	joined.set(first, 0);
	joined.set(second, first.length);
	return joined;
}

// ---- cue sheets ----

/** The tracks of a cue sheet: [{ file, number, mode }] with the mode as written (MODE1/2352, MODE2/2352, MODE1/2048, AUDIO). */
export function parseCue(text) {
	const tracks = [];
	let file = null;
	for (const line of String(text).split(/\r?\n/)) {
		const fileMatch = /^\s*FILE\s+(?:"([^"]*)"|(\S+))/i.exec(line);
		if (fileMatch) { file = fileMatch[1] ?? fileMatch[2]; continue; }
		const trackMatch = /^\s*TRACK\s+(\d+)\s+(\S+)/i.exec(line);
		if (trackMatch)
			tracks.push({ file, number: Number(trackMatch[1]), mode: trackMatch[2].toUpperCase() });
	}
	return tracks;
}

/** Refuses a cue sheet whose first track is not data (the game's files are on the first track). */
export function checkCue(text) {
	const tracks = parseCue(text);
	if (tracks.length === 0)
		throw new ImageError('That cue sheet lists no track.');
	if (tracks[0].mode === 'AUDIO')
		throw new ImageError('The first track of that cue sheet is audio; the game files are on a data track.');
	return tracks[0].mode;
}

// ---- sectors ----

/** How the image stores a sector: { stride, offset } of its 2048 user bytes. A raw dump starts with the 12-byte sync pattern. */
export function detectLayout(head) {
	if (head.length < 16)
		throw new ImageError('That file is too short to be a CD image.');
	const sync = head[0] === 0 && head[11] === 0 && head.subarray(1, 11).every(value => value === 255);
	if (!sync)
		return { stride: USER_SECTOR, offset: 0, raw: false };
	if (head[15] === 1)
		return { stride: RAW_SECTOR, offset: 16, raw: true, mode: 1 };
	if (head[15] === 2)
		return { stride: RAW_SECTOR, offset: 24, raw: true, mode: 2 };
	throw new ImageError(`Unknown CD sector mode ${head[15]}; expected a Mode 1 or Mode 2 data track.`);
}

function hasSync(bytes, at) {
	if (bytes[at] !== 0 || bytes[at + 11] !== 0)
		return false;
	for (let index = 1; index < 11; index++)
		if (bytes[at + index] !== 255)
			return false;
	return true;
}

/**
 * Converts `count` sectors of the image's own layout (from `chunk`'s start) to contiguous user data. A raw sector without the sync
 * pattern is not data (copy protection and unreadable sectors are dumped like that, outside the files): it is zero-filled and listed
 * in `bad`, and only a wanted file that lies on one is an error.
 */
function toUser(chunk, count, layout, firstLba) {
	if (!layout.raw)
		return { data: chunk.subarray(0, count * USER_SECTOR), bad: [] };
	const data = new Uint8Array(count * USER_SECTOR);
	const bad = [];
	for (let index = 0; index < count; index++) {
		const at = index * RAW_SECTOR;
		if (!hasSync(chunk, at)) {
			bad.push(firstLba + index);
			continue;
		}
		data.set(chunk.subarray(at + layout.offset, at + layout.offset + USER_SECTOR), index * USER_SECTOR);
	}
	return { data, bad };
}

/**
 * Turns the image's bytes (async iterable of Uint8Array) into batches { lba, count, data, bad } of consecutive 2048-byte sectors. The layout
 * is detected from the first bytes unless given (a stream that starts in the middle of the image gives its layout and `startLba`).
 */
export async function* userSectors(chunks, { layout = null, startLba = 0, onLayout = () => {} } = {}) {
	let carry = new Uint8Array(0);
	let lba = startLba;
	for await (let chunk of chunks) {
		if (carry.length) {
			chunk = concat(carry, chunk);
			carry = new Uint8Array(0);
		}
		if (!layout) {
			if (chunk.length < 16) { carry = chunk.slice(); continue; }
			layout = detectLayout(chunk);
			onLayout(layout);
		}
		const count = Math.floor(chunk.length / layout.stride);
		if (count > 0) {
			yield { lba, count, ...toUser(chunk, count, layout, lba) };
			lba += count;
		}
		// copy the rest: it must not keep the whole chunk alive
		carry = chunk.slice(count * layout.stride);
	}
	if (carry.length)
		throw new ImageError('The image ends in the middle of a sector: the file is damaged or the download was cut short.', 'truncated');
}

// ---- ISO 9660 ----

function text(bytes, start, end) {
	let value = '';
	for (let index = start; index < end; index++)
		value += String.fromCharCode(bytes[index]);
	return value;
}

function ucs2(bytes, start, end) {
	let value = '';
	for (let index = start; index + 1 < end; index += 2)
		value += String.fromCharCode(bytes[index] << 8 | bytes[index + 1]);
	return value;
}

/**
 * Reads the volume's directory tree. `read(lba, count)` returns count sectors of user data or throws NeedMore. The Joliet tree
 * (long mixed-case names) is used when the disc has one, else the plain ISO 9660 names. Returns { label, joliet, files: [{ path, lba, size }] };
 * a path has no leading slash and no ";1" version suffix.
 */
export function readVolume(read) {
	let primary = null;
	let joliet = null;
	for (let lba = 16; lba < 16 + 64; lba++) {
		const descriptor = read(lba, 1);
		if (text(descriptor, 1, 6) !== 'CD001') {
			if (lba === 16)
				throw new ImageError('That is not an ISO 9660 CD image (no volume descriptor at sector 16).');
			break;
		}
		const type = descriptor[0];
		if (type === 255)
			break;
		if (type === 1 && !primary)
			primary = descriptor;
		const escape = descriptor[88] === 0x25 && descriptor[89] === 0x2F && [0x40, 0x43, 0x45].includes(descriptor[90]);
		if (type === 2 && escape && !joliet)
			joliet = descriptor;
	}
	if (!primary)
		throw new ImageError('That ISO 9660 image has no primary volume descriptor.');
	const chosen = joliet ?? primary;
	const decode = joliet ? ucs2 : text;
	const files = [];
	const seen = new Set();
	const pending = [{ record: chosen.subarray(156, 190), path: '' }];
	while (pending.length > 0) {
		const { record, path } = pending.pop();
		const start = u32(record, 2);
		const length = u32(record, 10);
		if (seen.has(start) || length > 64 * 1048576)
			continue;
		seen.add(start);
		const data = read(start, Math.max(1, Math.ceil(length / USER_SECTOR)));
		let at = 0;
		while (at < length) {
			const size = data[at];
			if (size === 0) {
				at = (Math.floor(at / USER_SECTOR) + 1) * USER_SECTOR;
				continue;
			}
			const entry = data.subarray(at, at + size);
			at += size;
			const nameLength = entry[32];
			if (nameLength === 1 && (entry[33] === 0 || entry[33] === 1))
				continue;
			const name = decode(entry, 33, 33 + nameLength).replace(/;\d+$/, '').replace(/\.$/, '');
			const full = path ? `${path}/${name}` : name;
			if (entry[25] & 2)
				pending.push({ record: entry, path: full });
			else
				files.push({ path: full, lba: u32(entry, 2), size: u32(entry, 10), multi: (entry[25] & 0x80) !== 0 });
		}
	}
	return { label: text(primary, 40, 72).trim(), joliet: !!joliet, files };
}

// ---- ZIP ----

const IMAGE_NAME = /\.(iso|bin|img)$/i;

/** The entry that holds the CD image: the first .iso, else the first .bin or .img. Other members (and every executable) are ignored. */
export function pickImage(entries) {
	const images = entries.filter(entry => !entry.name.endsWith('/') && IMAGE_NAME.test(entry.name));
	return images.find(entry => /\.iso$/i.test(entry.name)) ?? images[0] ?? null;
}

/** Entries of a zip's central directory from `ranges` = { tail(length), read(start, length) } (both Promise<Uint8Array>). */
export async function readZipDirectory(ranges) {
	const tail = await ranges.tail(66000);
	let at = tail.length - 22;
	while (at >= 0 && u32(tail, at) !== 0x06054B50)
		at--;
	if (at < 0)
		throw new ImageError('That ZIP file has no central directory (damaged or cut short).');
	const count = u16(tail, at + 10);
	const size = u32(tail, at + 12);
	const offset = u32(tail, at + 16);
	if (count === 0xFFFF || size === 0xFFFFFFFF || offset === 0xFFFFFFFF)
		throw new ImageError('ZIP64 files are not supported; use a ZIP below 4 GB or choose the extracted image.');
	const directory = at >= size ? tail.subarray(at - size, at) : await ranges.read(offset, size);
	const entries = [];
	let cursor = 0;
	for (let index = 0; index < count && cursor + 46 <= directory.length; index++) {
		if (u32(directory, cursor) !== 0x02014B50)
			throw new ImageError('That ZIP file has a damaged central directory.');
		const nameLength = u16(directory, cursor + 28);
		const extraLength = u16(directory, cursor + 30);
		const commentLength = u16(directory, cursor + 32);
		entries.push({
			name: new TextDecoder().decode(directory.subarray(cursor + 46, cursor + 46 + nameLength)),
			flags: u16(directory, cursor + 8),
			method: u16(directory, cursor + 10),
			compressed: u32(directory, cursor + 20),
			size: u32(directory, cursor + 24),
			offset: u32(directory, cursor + 42),
		});
		cursor += 46 + nameLength + extraLength + commentLength;
	}
	return entries;
}

function inflater(method, name) {
	if (method === 0)
		return null;
	if (method !== 8)
		throw new ImageError(`${name} in that ZIP uses compression method ${method}; only stored and deflate are supported.`);
	if (typeof DecompressionStream !== 'function')
		throw new ImageError('This browser cannot unpack a ZIP (no DecompressionStream). Use a current Chrome, Firefox or Safari, or choose the unpacked image.', 'unsupported');
	return new DecompressionStream('deflate-raw');
}

/** Yields a stream's chunks and cancels it when the consumer stops early. */
async function* streamChunks(stream) {
	const reader = stream.getReader();
	try {
		for (;;) {
			const { value, done } = await reader.read();
			if (done)
				return;
			yield value;
		}
	} finally {
		try { await reader.cancel(); } catch { /* already closed */ }
	}
}

function toStream(chunks) {
	const iterator = chunks[Symbol.asyncIterator]();
	return new ReadableStream({
		async pull(controller) {
			const { value, done } = await iterator.next();
			if (done)
				controller.close();
			else
				controller.enqueue(value);
		},
		async cancel() { await iterator.return?.(); },
	});
}

/** The (inflated) bytes of an entry's data, from the compressed chunks. */
function entryChunks(compressed, entry) {
	const inflate = inflater(entry.method, entry.name);
	return inflate ? streamChunks(toStream(compressed).pipeThrough(inflate)) : compressed;
}

/** Reads exact byte counts from an async iterable of chunks. */
class ByteReader {
	constructor(chunks) {
		this.iterator = chunks[Symbol.asyncIterator]();
		this.rest = new Uint8Array(0);
	}
	async more() {
		const { value, done } = await this.iterator.next();
		if (done)
			return false;
		this.rest = this.rest.length ? concat(this.rest, value) : value;
		return true;
	}
	/** The next `count` bytes, or null when the stream ends before the first of them. */
	async read(count) {
		while (this.rest.length < count)
			if (!await this.more())
				if (this.rest.length === 0)
					return null;
				else
					throw new ImageError('The ZIP file ends in the middle of an entry (damaged or cut short).', 'truncated');
		const bytes = this.rest.subarray(0, count);
		this.rest = this.rest.subarray(count);
		return bytes;
	}
	async skip(count) {
		while (count > 0) {
			if (this.rest.length === 0 && !await this.more())
				throw new ImageError('The ZIP file ends in the middle of an entry (damaged or cut short).', 'truncated');
			const used = Math.min(count, this.rest.length);
			this.rest = this.rest.subarray(used);
			count -= used;
		}
	}
	async *take(count) {
		while (count > 0) {
			if (this.rest.length === 0 && !await this.more())
				throw new ImageError('The ZIP file ends in the middle of an entry (damaged or cut short).', 'truncated');
			const used = Math.min(count, this.rest.length);
			yield this.rest.subarray(0, used);
			this.rest = this.rest.subarray(used);
			count -= used;
		}
	}
	async close() { await this.iterator.return?.(); }
}

/**
 * The image's bytes out of a ZIP that arrives as a stream (no random access): local headers are read in order, every member that is
 * not the image is skipped by its compressed size without being inflated, and the image is inflated as it passes.
 */
export async function* zipStreamChunks(chunks) {
	const reader = new ByteReader(chunks);
	try {
		for (;;) {
			const signature = await reader.read(4);
			if (!signature || u32(signature, 0) !== 0x04034B50)
				throw new ImageError('There is no image (.iso or .bin) in that ZIP file.');
			const header = await reader.read(26);
			const flags = u16(header, 2);
			const entry = { flags, method: u16(header, 4), compressed: u32(header, 14), size: u32(header, 18) };
			entry.name = new TextDecoder().decode(await reader.read(u16(header, 22)));
			await reader.skip(u16(header, 24));
			const image = !entry.name.endsWith('/') && IMAGE_NAME.test(entry.name);
			if (entry.compressed === 0xFFFFFFFF)
				throw new ImageError('ZIP64 files are not supported; use a ZIP below 4 GB or choose the extracted image.');
			if ((flags & 8) && (entry.compressed === 0 || image) && !entry.name.endsWith('/'))
				throw new ImageError('This ZIP was written as a stream (sizes after the data), which the browser cannot skip through. Download the file and choose it from your disk instead.', 'unsupported');
			if (!image) {
				await reader.skip(entry.compressed);
				continue;
			}
			yield* entryChunks(reader.take(entry.compressed), entry);
			return;
		}
	} finally {
		await reader.close();
	}
}

// ---- sources ----

/**
 * A source is { random, total, open() -> { chunks, total? } }: the image's bytes from the start. A random one also has read(start, length)
 * (a loose file whose bytes can be read anywhere). A streamed one may have openAt(byte) -> { chunks } | null, a cheap jump ahead by a ranged
 * request (null: not possible, and `rangeable` becomes false); extraction uses it to skip long stretches it does not need.
 */

/** A source for a local file: a loose image is read at random; a ZIP is inflated from its central directory. */
export async function blobSource(blob) {
	const head = new Uint8Array(await blob.slice(0, 4).arrayBuffer());
	if (!(head[0] === 0x50 && head[1] === 0x4B && head[2] === 3 && head[3] === 4))
		return {
			random: true, total: blob.size, name: blob.name ?? 'image',
			async read(start, length) { return new Uint8Array(await blob.slice(start, start + length).arrayBuffer()); },
			open() { return { chunks: streamChunks(blob.stream()), total: blob.size }; },
		};
	const ranges = {
		tail: async length => new Uint8Array(await blob.slice(Math.max(0, blob.size - length)).arrayBuffer()),
		read: async (start, length) => new Uint8Array(await blob.slice(start, start + length).arrayBuffer()),
	};
	const entries = await readZipDirectory(ranges);
	const entry = pickImage(entries);
	if (!entry)
		throw new ImageError('There is no image (.iso or .bin) in that ZIP file.');
	if (entry.compressed === 0xFFFFFFFF || entry.size === 0xFFFFFFFF || entry.offset === 0xFFFFFFFF)
		throw new ImageError('ZIP64 files are not supported; use a ZIP below 4 GB or choose the extracted image.');
	const local = await ranges.read(entry.offset, 30);
	const start = entry.offset + 30 + u16(local, 26) + u16(local, 28);
	return {
		random: false, rangeable: false, total: entry.size, name: entry.name,
		open: () => ({ chunks: entryChunks(streamChunks(blob.slice(start, start + entry.compressed).stream()), entry), total: entry.size }),
	};
}

function httpError(response) {
	return new ImageError(`The server answered HTTP ${response.status}${response.statusText ? ` ${response.statusText}` : ''} for that address.`, 'http');
}

/**
 * A source for an address (fetch). A ZIP whose server answers ranges (and allows them cross-origin) is read from its central
 * directory, so only the image's bytes are requested; any other answer falls back to one streamed request, in which a ZIP is
 * recognised by its first bytes. `total` is the image's size when the server tells it (else `expectedBytes`, for the progress bar).
 */
export function urlSource(url, { fetch: fetchFn = globalThis.fetch.bind(globalThis), signal, expectedBytes = null, ranges = true } = {}) {
	const source = {
		random: false, total: expectedBytes, rangeable: ranges ? undefined : false, name: url, zipRanged: false,
		async open() {
			if (this.rangeable !== false && /\.zip(?:[?#]|$)/i.test(url)) {
				const ranged = await this.openZipRanged();
				if (ranged)
					return ranged;
			}
			const response = await fetchFn(url, { signal, cache: 'no-store' });
			if (!response.ok)
				throw httpError(response);
			const length = Number(response.headers.get('content-length'));
			const chunks = streamChunks(response.body);
			const first = await chunks.next();
			if (first.done)
				throw new ImageError('That address returned an empty file.', 'format');
			const whole = (async function* () { yield first.value; yield* chunks; })();
			const zip = first.value.length >= 4 && first.value[0] === 0x50 && first.value[1] === 0x4B && first.value[2] === 3 && first.value[3] === 4;
			// bytes of the image, not of the ZIP: a ZIP is never skipped into with a range
			if (zip)
				this.rangeable = false;
			if (length > 0 && !zip)
				this.total = this.total ?? length;
			return { chunks: zip ? zipStreamChunks(whole) : whole, total: length > 0 && !zip ? length : null };
		},
		/**
		 * The image from byte `start` on, by a ranged request, while the caller keeps reading the stream it has; null when the server
		 * does not answer ranges for this page (it is then not asked again, and the caller reads on).
		 */
		async openAt(start) {
			if (this.rangeable === false)
				return null;
			try {
				const response = await fetchFn(url, { signal, cache: 'no-store', headers: { Range: `bytes=${start}-` } });
				if (response.status === 206)
					return { chunks: streamChunks(response.body), total: null };
				await response.body?.cancel().catch(() => {});
			} catch (error) {
				if (error?.name === 'AbortError')
					throw error;
			}
			this.rangeable = false;
			return null;
		},
		/** The ranged route for a ZIP; null when the server cannot do ranges for the browser (then one plain request is used). */
		async openZipRanged() {
			try {
				const tailResponse = await fetchFn(url, { signal, cache: 'no-store', headers: { Range: 'bytes=-66000' } });
				if (tailResponse.status !== 206) {
					// a server that ignores ranges is streaming the whole file: stop it
					await tailResponse.body?.cancel().catch(() => {});
					return null;
				}
				const tailBytes = new Uint8Array(await tailResponse.arrayBuffer());
				const readRange = async (from, length) => {
					const response = await fetchFn(url, { signal, cache: 'no-store', headers: { Range: `bytes=${from}-${from + length - 1}` } });
					if (response.status !== 206)
						throw new ImageError('The server stopped answering range requests.', 'http');
					return new Uint8Array(await response.arrayBuffer());
				};
				const entries = await readZipDirectory({ tail: async () => tailBytes, read: readRange });
				const entry = pickImage(entries);
				if (!entry)
					throw new ImageError('There is no image (.iso or .bin) in that ZIP file.');
				if (entry.compressed === 0xFFFFFFFF || entry.offset === 0xFFFFFFFF)
					throw new ImageError('ZIP64 files are not supported; use a ZIP below 4 GB or choose the extracted image.');
				const local = await readRange(entry.offset, 30);
				const from = entry.offset + 30 + u16(local, 26) + u16(local, 28);
				const response = await fetchFn(url, { signal, cache: 'no-store', headers: { Range: `bytes=${from}-${from + entry.compressed - 1}` } });
				if (response.status !== 206) {
					await response.body?.cancel().catch(() => {});
					return null;
				}
				this.zipRanged = true;
				this.rangeable = false;
				this.total = entry.size;
				return { chunks: entryChunks(streamChunks(response.body), entry), total: entry.size };
			} catch (error) {
				if (error?.name === 'AbortError' || error instanceof ImageError)
					throw error;
				return null;
			}
		},
	};
	return source;
}

// ---- extraction ----

/** Delivers wanted files as the sectors that hold them pass; files the caller already has are never buffered. */
function makeDispatcher(files, write, progress) {
	const list = [...files].sort((a, b) => a.lba - b.lba);
	let next = 0;
	let active = [];
	let done = 0;
	const remaining = () => list.length - done;
	return {
		list,
		get remaining() { return remaining(); },
		/** The first sector still needed after sector `lba`, or null when nothing more is. */
		nextNeeded(lba) {
			if (active.length > 0)
				return lba;
			return next < list.length ? Math.max(list[next].lba, lba) : null;
		},
		async feed(batch) {
			const end = batch.lba + batch.count;
			while (next < list.length && list[next].lba < end) {
				const file = list[next++];
				file.end = file.lba + Math.ceil(file.size / USER_SECTOR);
				file.buffer = new Uint8Array(file.size);
				if (file.lba < batch.lba)
					throw new ImageError(`${file.path} starts before the sectors the page has (internal error).`, 'internal');
				active.push(file);
			}
			const finished = [];
			for (const file of active) {
				const from = Math.max(file.lba, batch.lba);
				const to = Math.min(file.end, end);
				if (to > from && batch.bad.some(sector => sector >= from && sector < to))
					throw new ImageError(`${file.path} lies on sectors of the image that cannot be read (damaged or protected): the image is not a good dump.`, 'damaged');
				if (to > from) {
					const source = batch.data.subarray((from - batch.lba) * USER_SECTOR, (to - batch.lba) * USER_SECTOR);
					const target = (from - file.lba) * USER_SECTOR;
					file.buffer.set(source.subarray(0, Math.max(0, Math.min(source.length, file.size - target))), target);
				}
				if (file.end <= end)
					finished.push(file);
			}
			for (const file of finished) {
				active = active.filter(entry => entry !== file);
				const bytes = file.buffer;
				file.buffer = null;
				await write(file.path, bytes);
				done++;
				progress(file);
			}
		},
		get unfinishedActive() { return active.length; },
	};
}

/**
 * Extracts the wanted files of the image behind `source`.
 *  choose(files)  -> paths to take (throws for an image that is not the game's);
 *  begin(taken)   -> called once with the chosen files [{ path, size }] before the first write (the store's list);
 *  have(path, size) -> true for a file already kept (it is skipped);
 *  write(path, bytes) -> keeps one file (bytes: Uint8Array);
 *  progress({ bytes, total, done, count, path }) -> after every batch and file.
 * Returns { files, bytes, joliet, label }.
 */
export async function extractImage({ source, choose, begin, have = () => false, write, progress = () => {}, signal = null }) {
	const aborted = () => {
		if (signal?.aborted)
			throw Object.assign(new Error('Cancelled'), { name: 'AbortError' });
	};
	let layout = null;
	let total = source.total ?? null;
	const report = (extra = {}) => progress({ total, done: 0, count: 0, bytes: 0, ...extra });
	const wantedOf = (volume) => {
		const paths = new Set(choose(volume.files.map(file => ({ path: file.path, size: file.size }))));
		const wanted = volume.files.filter(file => paths.has(file.path));
		if (wanted.some(file => file.multi))
			throw new ImageError(`${wanted.find(file => file.multi).path} is stored in several parts, which is not supported.`);
		return wanted;
	};
	const summary = (volume, wanted) => ({ files: wanted.length, bytes: wanted.reduce((sum, file) => sum + file.size, 0), joliet: volume.joliet, label: volume.label });

	if (source.random) {
		layout = detectLayout(await source.read(0, 64));
		const sector = async (lba, count, file = null) => {
			const raw = await source.read(lba * layout.stride, count * layout.stride);
			if (raw.length < count * layout.stride)
				throw new ImageError('The image ends before its files do (damaged or cut short).', 'truncated');
			const { data, bad } = toUser(raw, count, layout, lba);
			if (file && bad.length > 0)
				throw new ImageError(`${file.path} lies on sectors of the image that cannot be read (damaged or protected): the image is not a good dump.`, 'damaged');
			return data;
		};
		// The directory tree is read in one go from the first sectors (a random source can afford the 32 MB).
		const headSectors = Math.min(HEAD_LIMIT, Math.max(1, Math.floor(source.total / layout.stride)));
		const head = await sector(0, headSectors);
		const volume = readVolume((lba, count) => {
			if (lba + count > headSectors)
				throw new ImageError('The directory tree of that image lies beyond its first 32 MB, which the page does not support.', 'format');
			return head.subarray(lba * USER_SECTOR, (lba + count) * USER_SECTOR);
		});
		const wanted = wantedOf(volume);
		await begin(wanted.map(file => ({ path: file.path, size: file.size })));
		const todo = wanted.filter(file => !have(file.path, file.size)).sort((a, b) => a.lba - b.lba);
		let bytes = 0;
		let done = 0;
		const count = todo.length;
		for (const file of todo) {
			aborted();
			const parts = [];
			let at = 0;
			while (at < file.size) {
				aborted();
				const sectors = Math.min(Math.ceil((file.size - at) / USER_SECTOR), 2048);
				const part = await sector(file.lba + at / USER_SECTOR, sectors, file);
				parts.push(part.subarray(0, Math.min(part.length, file.size - at)));
				at += sectors * USER_SECTOR;
				bytes += sectors * layout.stride;
				report({ bytes, done, count, path: file.path });
			}
			const contents = new Uint8Array(file.size);
			let offset = 0;
			for (const part of parts) { contents.set(part, offset); offset += part.length; }
			await write(file.path, contents);
			done++;
			report({ bytes, done, count, path: file.path });
		}
		return summary(volume, wanted);
	}

	// A streamed source: the head of the image is kept until the directory tree is read, then wanted files are cut out as they pass.
	let opened = await source.open();
	total = opened.total ?? total;
	let iterator = userSectors(opened.chunks, { onLayout: found => layout = found })[Symbol.asyncIterator]();
	const head = [];
	let available = 0;
	let needed = 17;
	let volume = null;
	let wanted = null;
	let dispatcher = null;
	let skipped = 0;
	const readHead = (lba, count) => {
		if (lba + count > available)
			throw new NeedMore(lba + count);
		const out = new Uint8Array(count * USER_SECTOR);
		for (let index = 0; index < count; index++) {
			const at = lba + index;
			let low = 0;
			let high = head.length - 1;
			while (low < high) {
				const middle = (low + high + 1) >> 1;
				if (head[middle].lba <= at) low = middle; else high = middle - 1;
			}
			const batch = head[low];
			out.set(batch.data.subarray((at - batch.lba) * USER_SECTOR, (at - batch.lba + 1) * USER_SECTOR), index * USER_SECTOR);
		}
		return out;
	};
	let finished = false;
	try {
		for (;;) {
			aborted();
			const step = await iterator.next();
			if (step.done)
				break;
			const batch = step.value;
			const position = (batch.lba + batch.count) * layout.stride;
			if (!dispatcher) {
				head.push(batch);
				available = batch.lba + batch.count;
				report({ bytes: position, phase: 'directory' });
				if (available < needed)
					continue;
				try {
					volume = readVolume(readHead);
				} catch (error) {
					if (error instanceof NeedMore) {
						needed = error.lba;
						if (needed > HEAD_LIMIT)
							throw new ImageError('The directory tree of that image lies beyond its first 32 MB; extract the image to a file and choose that, or use the unpacked game folder.', 'format');
						continue;
					}
					throw error;
				}
				wanted = wantedOf(volume);
				await begin(wanted.map(file => ({ path: file.path, size: file.size })));
				const empty = wanted.filter(file => file.size === 0 && !have(file.path, 0));
				for (const file of empty)
					await write(file.path, new Uint8Array(0));
				const todo = wanted.filter(file => file.size > 0 && !have(file.path, file.size));
				dispatcher = makeDispatcher(todo, write, file => report({ bytes: position, done: wanted.length - dispatcher.remaining, count: wanted.length, path: file.path }));
				const replay = head.splice(0);
				for (const old of replay) {
					await dispatcher.feed(old);
					aborted();
				}
			} else {
				await dispatcher.feed(batch);
			}
			if (dispatcher.remaining === 0) {
				finished = true;
				break;
			}
			report({ bytes: position + skipped, done: wanted.length - dispatcher.remaining, count: wanted.length });
			// A long stretch the player does not need (movies, other discs' files) is skipped when the server answers ranges.
			if (source.rangeable !== false && dispatcher.unfinishedActive === 0) {
				const lba = dispatcher.nextNeeded(batch.lba + batch.count);
				if (lba != null && (lba - (batch.lba + batch.count)) * layout.stride >= SKIP_BYTES) {
					const jump = await source.openAt?.(lba * layout.stride);
					if (jump) {
						await iterator.return?.();
						skipped += (lba - (batch.lba + batch.count)) * layout.stride;
						iterator = userSectors(jump.chunks, { layout, startLba: lba })[Symbol.asyncIterator]();
					}
				}
			}
		}
		if (!finished && (!dispatcher || dispatcher.remaining > 0)) {
			if (!dispatcher)
				throw new ImageError('The image ended before its file list was found (not an ISO 9660 CD image, or cut short).', 'truncated');
			throw new ImageError(`The image ended before ${dispatcher.remaining} of its files (cut short).`, 'truncated');
		}
	} finally {
		await iterator.return?.();
	}
	return summary(volume, wanted);
}
