// The pools extension's own sprite sets (docs/POOLS.md, "Browser delivery") are static files of the site, not part of the startup download.
// The page fetches them in the background after the game has started, while the pools extension is on, with a small bounded concurrency.
// The game keeps a file only when its path, size and SHA-256 are in the manifest embedded in the build (Program.AddPoolSpriteFile), so this
// module only plans and moves bytes; a failure (offline, 404, a page of HTML instead of a file) stops the fetch with one console line and the
// game keeps its fallbacks (the kid sprite for swimmers, the game's own land guests). Nothing is stored in the saves or the options.
export const spriteFolder = 'pools-sprites/';
const safePath = /^[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)*$/;
const maximumFileBytes = 4 * 1024 * 1024;

/** Reads the pinned manifest text, or throws when it is not a supported, well-formed list. */
export function parseManifest(text) {
	const manifest = JSON.parse(text);
	if (manifest?.format !== 1 || !Array.isArray(manifest.files) || manifest.files.length > 1000)
		throw new Error('The pools sprite manifest is not supported.');
	const seen = new Set();
	for (const file of manifest.files) {
		if (typeof file.path !== 'string' || !safePath.test(file.path) || file.path.split('/').some(part => part === '.' || part === '..')
			|| seen.has(file.path) || !Number.isSafeInteger(file.size) || file.size <= 0 || file.size > maximumFileBytes
			|| !/^[a-f0-9]{64}$/.test(file.sha256))
			throw new Error('The pools sprite manifest contains an invalid file.');
		seen.add(file.path);
	}
	return manifest.files;
}

/**
 * The files to fetch, in order: the swim sets (first path segment "Swim") whenever the pools extension is on, the land guest sets ("Generic")
 * only when the pool guest art extension is on too. The land sets come first because a park swaps them only at its setup, while the swimmers
 * pick up their sets the moment they are complete.
 */
export function planSprites(files, { pools, guestArt }) {
	if (!pools) return [];
	const group = name => files.filter(file => file.path.startsWith(name + '/'));
	return [...(guestArt ? group('Generic') : []), ...group('Swim')];
}

/** Reads a body of exactly `size` bytes; the stream is cancelled the moment it passes `size`, so a wrong or endless body is never held whole. */
async function readBounded(response, size, name) {
	if (!response.body?.getReader) {
		// No streaming (an old browser, a test double): the length is checked after the read.
		const bytes = new Uint8Array(await response.arrayBuffer());
		if (bytes.length !== size) throw new Error(`${name}: unexpected size`);
		return bytes;
	}
	const reader = response.body.getReader(), bytes = new Uint8Array(size);
	let length = 0;
	try {
		for (;;) {
			const { value, done } = await reader.read();
			if (done) break;
			if (length + value.byteLength > size) throw new Error(`${name}: larger than expected`);
			bytes.set(value, length);
			length += value.byteLength;
		}
	} catch (error) {
		await reader.cancel().catch(() => {});
		throw error;
	} finally {
		reader.releaseLock();
	}
	if (length !== size) throw new Error(`${name}: unexpected size`);
	return bytes;
}

/**
 * Fetches the planned files with bounded concurrency. Resolves true when every file was stored, false after the first failure (the rest are
 * not fetched and nothing is retried); `failed` is called once with the reason. `active` says whether the game still runs: once it has
 * stopped, a refused file is not a failure to report.
 */
export async function fetchSprites({ files, store, active = () => true, fetcher = globalThis.fetch, base = spriteFolder, concurrency = 3, failed = error => console.warn('Pools sprites unavailable; swimmers and land guests keep their fallbacks:', error?.message ?? error) }) {
	if (!Number.isInteger(concurrency) || concurrency < 1 || concurrency > 4)
		throw new Error('The sprite download concurrency must be between one and four.');
	const queue = [...files];
	let stopped = false;
	async function worker() {
		while (!stopped && queue.length) {
			const file = queue.shift();
			try {
				// The default HTTP cache applies; the content hash in the address makes a changed file a new URL.
				const url = base + file.path.split('/').map(encodeURIComponent).join('/') + '?v=' + file.sha256.slice(0, 16);
				const response = await fetcher(url, { credentials: 'omit', referrerPolicy: 'no-referrer', signal: AbortSignal.timeout(60000) });
				if (!response.ok)
					throw new Error(`${file.path}: HTTP ${response.status}`);
				const declared = response.headers?.get?.('content-length');
				if (declared != null && Number(declared) > file.size && !response.headers.get('content-encoding'))
					throw new Error(`${file.path}: unexpected size`);
				const bytes = await readBounded(response, file.size, file.path);
				if (!active()) { stopped = true; return; }
				if (await store(file.path, bytes) !== true) {
					if (!active()) { stopped = true; return; }
					throw new Error(`${file.path}: rejected by the integrity check`);
				}
			} catch (error) {
				if (!stopped) {
					stopped = true;
					if (active()) failed(error);
				}
			}
		}
	}
	await Promise.all(Array.from({ length: Math.min(concurrency, files.length) }, worker));
	return !stopped;
}

/**
 * Runs `run` (an async function) one call at a time: a request while a run is going on is followed by exactly one more run once it ends, however many requests came
 * meanwhile, and only while `active()` says the game still runs. The page fetches the sprite sets this way at the start and again when the game switches the pools
 * extension or its guest art on later (a park load or the Extensions and mods screen, without a restart).
 */
export function serialRequests(run, active = () => true) {
	let running = null, again = false;
	const request = () => {
		if (running) {
			again = true;
			return running;
		}
		running = Promise.resolve().then(run).finally(() => {
			running = null;
			if (again && active()) {
				again = false;
				request();
			}
		});
		return running;
	};
	return request;
}
