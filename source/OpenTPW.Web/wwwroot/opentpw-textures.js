// Clarity replacements download only when the renderer asks for them. The game's original files
// never leave the browser. The packaged index pins every public PNG by size and SHA-256.
export const textureBaseUrl = 'https://opentpw-textures.pages.dev/clarity-enhanced/20261011/';
const maximumFileBytes = 25 * 1024 * 1024;
const maximumPixels = 16 * 1024 * 1024;
const safePath = /^textures\/[a-z0-9_!.\/-]+\.png$/;

function trustedPrimaryUrl(file) {
	if (file.url === undefined) return textureBaseUrl + file.path;
	let url;
	try { url = new URL(file.url); } catch { throw new Error('The Clarity texture index contains an invalid download URL.'); }
	const prefix = url.pathname.match(/^\/gh\/meneerkrabs\/OpenTPW-Texture-Packs@clarity-20261011-\d{3}\//)?.[0];
	if (url.origin !== 'https://cdn.jsdelivr.net' || !prefix || url.pathname !== prefix + file.path
		|| file.url !== url.origin + url.pathname)
		throw new Error('The Clarity texture index contains an untrusted download URL.');
	return file.url;
}

export function validateDistribution(distribution) {
	if (distribution?.format !== 1 || distribution.name !== 'clarity-enhanced' || distribution.version !== '20261011'
		|| distribution.textureBaseUrl !== textureBaseUrl || distribution.packManifest?.Format !== 1
		|| !Array.isArray(distribution.files) || !distribution.files.length || distribution.files.length > 20000)
		throw new Error('The Clarity texture index is not supported.');
	const files = new Map();
	for (const file of distribution.files) {
		if ((file.path !== 'pack.json' && !safePath.test(file.path)) || file.path.split('/').some(part => !part || part === '.' || part === '..')
			|| files.has(file.path) || !Number.isSafeInteger(file.size) || file.size < (file.path === 'pack.json' ? 1 : 33) || file.size > maximumFileBytes
			|| !/^[a-f0-9]{64}$/.test(file.sha256))
			throw new Error('The Clarity texture index contains an invalid file.');
		if (file.path !== 'pack.json' && (!Number.isSafeInteger(file.width) || !Number.isSafeInteger(file.height)
			|| file.width <= 0 || file.height <= 0 || file.width > 8192 || file.height > 8192 || file.width * file.height > maximumPixels))
			throw new Error('The Clarity texture index contains invalid dimensions.');
		files.set(file.path, { ...file, url: trustedPrimaryUrl(file), fallbackUrl: textureBaseUrl + file.path,
			// CacheStorage keys are URLs, but this identity depends only on the verified content digest.
			cacheUrl: `https://opentpw.io/texture-cache/sha256/${file.sha256}` });
	}
	files.delete('pack.json');
	return files;
}

// Check the IHDR before a PNG decoder can allocate its pixels. SHA-256 then confirms the complete
// file is the one in the packaged index, even when a browser cache entry was damaged.
function checkPng(bytes) {
	const signature = [137, 80, 78, 71, 13, 10, 26, 10];
	if (bytes.length < 33 || !signature.every((byte, index) => bytes[index] === byte))
		throw new Error('The replacement is not a PNG.');
	const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
	if (view.getUint32(8) !== 13 || view.getUint32(12) !== 0x49484452)
		throw new Error('The replacement has no PNG dimensions.');
	const width = view.getUint32(16), height = view.getUint32(20);
	if (!width || !height || width > 8192 || height > 8192 || width * height > maximumPixels)
		throw new Error('The replacement is too large for the browser.');
	return { width, height };
}

async function readBounded(response, size) {
	if (!response.ok)
		throw new Error(`Texture download returned ${response.status}.`);
	const declared = response.headers.get('content-length');
	if (declared !== null && Number(declared) > size)
		throw new Error('The texture download is larger than expected.');
	if (!response.body?.getReader) {
		const bytes = new Uint8Array(await response.arrayBuffer());
		if (bytes.length !== size)
			throw new Error('The texture download has the wrong size.');
		return bytes;
	}
	const reader = response.body.getReader(), chunks = [];
	let length = 0;
	try {
		for (;;) {
			const { value, done } = await reader.read();
			if (done) break;
			length += value.byteLength;
			if (length > size)
				throw new Error('The texture download is larger than expected.');
			chunks.push(value);
		}
	} catch (error) {
		await reader.cancel().catch(() => {});
		throw error;
	} finally {
		reader.releaseLock();
	}
	if (length !== size)
		throw new Error('The texture download has the wrong size.');
	const bytes = new Uint8Array(length);
	let offset = 0;
	for (const chunk of chunks) {
		bytes.set(chunk, offset);
		offset += chunk.byteLength;
	}
	return bytes;
}

/** A bounded asynchronous download queue. A failed replacement leaves the original playable. */
export function createTextureDownloads({ distribution, writeFile, enabled = () => true,
	fetcher = globalThis.fetch, cacheStorage = globalThis.caches, digest = bytes => crypto.subtle.digest('SHA-256', bytes),
	changed = () => {}, warning = error => console.warn('Clarity texture unavailable', error), concurrency = 3 }) {
	if (!Number.isInteger(concurrency) || concurrency < 1 || concurrency > 4)
		throw new Error('The texture download concurrency must be between one and four.');
	const files = validateDistribution(distribution), jobs = new Map(), failures = new Set(), queue = [];
	const cache = cacheStorage?.open('opentpw-clarity-20261011').catch(() => null) ?? Promise.resolve(null);
	let active = 0, loaded = 0;

	async function verify(response, file) {
		const bytes = await readBounded(response, file.size);
		const dimensions = checkPng(bytes);
		if (dimensions.width !== file.width || dimensions.height !== file.height)
			throw new Error('The replacement dimensions differ from the packaged index.');
		const hash = [...new Uint8Array(await digest(bytes))].map(byte => byte.toString(16).padStart(2, '0')).join('');
		if (hash !== file.sha256)
			throw new Error('The texture download failed its integrity check.');
		return bytes;
	}

	async function download(file) {
		const store = await cache;
		let bytes;
		try {
			const cached = await store?.match(file.cacheUrl);
			if (cached)
				bytes = await verify(cached, file);
		} catch {
			// A damaged or inaccessible cache must not prevent a fresh verified download.
			await store?.delete(file.cacheUrl).catch(() => {});
		}
		if (!bytes) {
			let failed;
			for (const url of new Set([file.url, file.fallbackUrl])) {
				// An Original choice also stops a provider retry or a request still waiting on cache access.
				if (!enabled()) return false;
				try {
					const response = await fetcher(url, { credentials: 'omit', referrerPolicy: 'no-referrer', mode: 'cors', signal: AbortSignal.timeout(30000) });
					bytes = await verify(response, file);
					break;
				} catch (error) { failed = error; }
			}
			if (!bytes) throw failed;
			try {
				await store?.put(file.cacheUrl, new Response(bytes, { headers: { 'content-type': 'image/png' } }));
			} catch { /* Private mode or a full cache leaves the downloaded texture usable. */ }
		}
		if (!enabled()) return false;
		return await writeFile(file.path, bytes) !== false;
	}

	function pump() {
		while (active < concurrency && queue.length) {
			const job = queue.shift();
			if (!enabled()) {
				jobs.delete(job.file.path);
				job.resolve(false);
				continue;
			}
			active++;
			download(job.file).then(success => {
				if (success) loaded++;
				job.resolve(success);
			}).catch(error => {
				failures.add(job.file.path);
				warning(error);
				job.resolve(false);
			}).finally(() => {
				active--;
				jobs.delete(job.file.path);
				changed({ active, queued: queue.length, loaded, failed: failures.size });
				pump();
			});
		}
		changed({ active, queued: queue.length, loaded, failed: failures.size });
	}

	return {
		request(path) {
			if (!enabled() || failures.has(path) || !files.has(path)) return Promise.resolve(false);
			if (jobs.has(path)) return jobs.get(path).promise;
			let resolve;
			const promise = new Promise(done => resolve = done);
			const job = { file: files.get(path), promise, resolve };
			jobs.set(path, job);
			queue.push(job);
			pump();
			return promise;
		},
	};
}
