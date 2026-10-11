import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash, webcrypto } from 'node:crypto';

// The production file is an ES module without requiring a package.json or a test dependency.
const source = await readFile(new URL('../source/OpenTPW.Web/wwwroot/opentpw-textures.js', import.meta.url), 'utf8');
const { textureBaseUrl, validateDistribution, createTextureDownloads } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

function png(width = 8, height = 4) {
	const bytes = new Uint8Array(49);
	bytes.set([137, 80, 78, 71, 13, 10, 26, 10]);
	const view = new DataView(bytes.buffer);
	view.setUint32(8, 13);
	view.setUint32(12, 0x49484452);
	view.setUint32(16, width);
	view.setUint32(20, height);
	return bytes;
}

function index(paths = ['textures/levels/jungle/terrain/textures/grass.wct.png'], bytes = png()) {
	return {
		format: 1, name: 'clarity-enhanced', version: '20261011', textureBaseUrl, packManifest: { Format: 1 },
		files: paths.map(path => ({ path, size: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex'),
			width: new DataView(bytes.buffer).getUint32(16), height: new DataView(bytes.buffer).getUint32(20) })),
	};
}

function cache() {
	const entries = new Map();
	return {
		entries,
		open: async () => ({
			match: async url => entries.get(url)?.clone(),
			put: async (url, response) => entries.set(url, response.clone()),
			delete: async url => entries.delete(url),
		}),
	};
}

const digest = bytes => webcrypto.subtle.digest('SHA-256', bytes);
const quiet = () => {};

test('packaged index accepts WCT and guest atlas PNGs plus a manifest, with a fixed origin', () => {
	const distribution = index(['textures/ui/button.wct.png', 'textures/esprites/generic/kids/andy.atlas.png', 'textures/ui/textures/b_rideit!.wct.png']);
	distribution.files.push({ path: 'pack.json', size: 10, sha256: 'a'.repeat(64) });
	assert.equal(validateDistribution(distribution).size, 3);
	for (const replacement of ['http://opentpw-textures.pages.dev/clarity-enhanced/20261011/', 'https://example.com/', textureBaseUrl + '../'])
		assert.throws(() => validateDistribution({ ...distribution, textureBaseUrl: replacement }));
});

test('unsafe or duplicate paths, invalid sizes, hashes and dimensions are rejected before fetching', () => {
	for (const path of ['textures/../x.png', 'textures//x.png', '/textures/x.png', 'textures/./x.png', 'textures/x.png?query', 'textures/x\\y.png', 'textures/x.PNG', 'https://example.com/x.png'])
		assert.throws(() => validateDistribution(index([path])), path);
	for (const changed of [{ size: 0 }, { size: 26 * 1024 * 1024 }, { size: 1.5 }, { sha256: 'z'.repeat(64) }, { width: 0 }, { width: 8193 }, { width: 8192, height: 8192 }]) {
		const distribution = index();
		Object.assign(distribution.files[0], changed);
		assert.throws(() => validateDistribution(distribution));
	}
	assert.throws(() => validateDistribution(index(['textures/x.png', 'textures/x.png'])));
});

test('requests deduplicate in-flight downloads and verify before writing, without cookies or referrers', async () => {
	const distribution = index(), bytes = png(), path = distribution.files[0].path;
	let resolveFetch, fetches = 0;
	const writes = [];
	const client = createTextureDownloads({ distribution, cacheStorage: null, digest, warning: quiet,
		writeFile: (name, contents) => writes.push([name, contents]),
		fetcher: (url, options) => {
			fetches++;
			assert.equal(url, textureBaseUrl + path);
			assert.equal(options.credentials, 'omit');
			assert.equal(options.referrerPolicy, 'no-referrer');
			return new Promise(resolve => resolveFetch = resolve);
		},
	});
	const first = client.request(path), second = client.request(path);
	assert.equal(first, second);
	await new Promise(resolve => setImmediate(resolve));
	assert.equal(fetches, 1);
	resolveFetch(new Response(bytes));
	assert.equal(await first, true);
	assert.equal(writes.length, 1);
	assert.equal(writes[0][0], path);
	assert.deepEqual(writes[0][1], bytes);
});

test('verified browser cache survives a visit and a damaged cache is fetched again', async () => {
	const distribution = index(), bytes = png(), path = distribution.files[0].path, storage = cache();
	let fetches = 0, writes = 0;
	const makeClient = () => createTextureDownloads({ distribution, cacheStorage: storage, digest, warning: quiet,
		writeFile: () => writes++, fetcher: async () => { fetches++; return new Response(bytes); },
	});
	assert.equal(await makeClient().request(path), true);
	assert.equal(await makeClient().request(path), true);
	assert.equal(fetches, 1);
	assert.equal(writes, 2);
	const damaged = bytes.slice(); damaged[30]++;
	storage.entries.set(validateDistribution(distribution).get(path).cacheUrl, new Response(damaged));
	assert.equal(await makeClient().request(path), true);
	assert.equal(fetches, 2);
});

function withPrimary(distribution, shard = '001') {
	for (const file of distribution.files)
		file.url = `https://cdn.jsdelivr.net/gh/meneerkrabs/OpenTPW-Texture-Packs@clarity-20261011-${shard}/${file.path}`;
	return distribution;
}

test('only the pinned jsDelivr repository tag and exact indexed path can be a primary URL', () => {
	const distribution = withPrimary(index()), file = distribution.files[0], good = file.url;
	assert.equal(validateDistribution(distribution).get(file.path).url, good);
	for (const url of [
		good.replace('https:', 'http:'), good.replace('cdn.jsdelivr.net', 'evil.example'),
		good.replace('meneerkrabs/', 'someone/'), good.replace('OpenTPW-Texture-Packs', 'Other-Pack'),
		good.replace('@clarity-20261011-001/', '@main/'), good.replace('20261011', '20261012'),
		good.replace('-001/', '-1/'), good.replace('grass.wct.png', 'other.wct.png'),
		good + '?x=1', good + '#fragment', good.replace('https://', 'https://user@'), null,
	])
		assert.throws(() => validateDistribution({ ...distribution, files: [{ ...file, url }] }), String(url));
});

test('a verified primary CDN download avoids the Cloudflare fallback', async () => {
	const distribution = withPrimary(index()), path = distribution.files[0].path, urls = [], storage = cache();
	let writes = 0;
	const client = createTextureDownloads({ distribution, digest, cacheStorage: storage, warning: quiet,
		writeFile: () => writes++, fetcher: async url => { urls.push(url); return new Response(png()); },
	});
	assert.equal(await client.request(path), true);
	assert.deepEqual(urls, [distribution.files[0].url]);
	assert.equal(writes, 1);
	assert.equal(storage.entries.size, 1);
	assert.ok([...storage.entries.keys()][0].endsWith(distribution.files[0].sha256));
});

test('a 403, network failure, timeout or integrity failure on jsDelivr uses verified Cloudflare bytes', async () => {
	for (const failure of ['403', 'network', 'timeout', 'integrity']) {
		const distribution = withPrimary(index()), path = distribution.files[0].path, urls = [];
		let writes = 0, warnings = 0;
		const client = createTextureDownloads({ distribution, digest, cacheStorage: null, warning: () => warnings++,
			writeFile: () => writes++, fetcher: async url => {
				urls.push(url);
				if (url === textureBaseUrl + path) return new Response(png());
				if (failure === '403') return new Response('', { status: 403 });
				if (failure === 'network') throw new TypeError('Failed to fetch');
				if (failure === 'timeout') throw new DOMException('Timed out', 'TimeoutError');
				const damaged = png(); damaged[30]++;
				return new Response(damaged);
			},
		});
		assert.equal(await client.request(path), true, failure);
		assert.deepEqual(urls, [distribution.files[0].url, textureBaseUrl + path], failure);
		assert.equal(writes, 1, failure);
		assert.equal(warnings, 0, failure);
	}
});

test('content-addressed cache reuses fallback bytes across provider changes and changed shard tags', async () => {
	const distribution = index(), path = distribution.files[0].path, storage = cache();
	let fetches = 0, writes = 0;
	const makeClient = candidate => createTextureDownloads({ distribution: candidate, digest, cacheStorage: storage,
		warning: quiet, writeFile: () => writes++, fetcher: async () => { fetches++; return new Response(png()); },
	});
	assert.equal(await makeClient(distribution).request(path), true);
	assert.equal(await makeClient(withPrimary(index(), '001')).request(path), true);
	assert.equal(await makeClient(withPrimary(index(), '002')).request(path), true);
	assert.equal(fetches, 1);
	assert.equal(writes, 3);
	assert.equal(storage.entries.size, 1);
});

test('both failed providers leave the original playable without repeating requests', async () => {
	const distribution = withPrimary(index()), path = distribution.files[0].path, urls = [], storage = cache();
	let writes = 0, warnings = 0;
	const client = createTextureDownloads({ distribution, digest, cacheStorage: storage, warning: () => warnings++,
		writeFile: () => writes++, fetcher: async url => { urls.push(url); return new Response('', { status: 403 }); },
	});
	assert.equal(await client.request(path), false);
	assert.equal(await client.request(path), false);
	assert.deepEqual(urls, [distribution.files[0].url, textureBaseUrl + path]);
	assert.equal(writes, 0);
	assert.equal(warnings, 1);
	assert.equal(storage.entries.size, 0);
});

test('preparing the packaged index while Original is selected never downloads a PNG', async () => {
	let fetches = 0;
	const distribution = withPrimary(index());
	const client = createTextureDownloads({ distribution, digest, cacheStorage: null, warning: quiet,
		enabled: () => false, fetcher: async () => { fetches++; return new Response(png()); },
		writeFile: () => assert.fail('Original texture received a replacement'),
	});
	await new Promise(resolve => setImmediate(resolve));
	assert.equal(fetches, 0, 'constructing the client must not preload the pack');
	assert.equal(await client.request(distribution.files[0].path), false);
	assert.equal(fetches, 0);
});

test('switching to Original discards queued downloads while at most three active requests finish', async () => {
	const paths = Array.from({ length: 12 }, (_, number) => `textures/levels/jungle/${number}.wct.png`);
	const distribution = withPrimary(index(paths));
	let enabled = true, fetches = 0, writes = 0;
	const complete = [];
	const client = createTextureDownloads({ distribution, digest, cacheStorage: null, warning: quiet,
		enabled: () => enabled, writeFile: () => writes++,
		fetcher: () => { fetches++; return new Promise(resolve => complete.push(resolve)); },
	});
	const requests = paths.map(path => client.request(path));
	await new Promise(resolve => setImmediate(resolve));
	assert.equal(fetches, 3);
	enabled = false;
	for (const resolve of complete) resolve(new Response(png()));
	assert.deepEqual(await Promise.all(requests), paths.map(() => false));
	assert.equal(fetches, 3, 'queued PNGs must not begin downloading after Original is selected');
	assert.equal(writes, 0);
});

test('Original selection prevents a pending CDN fallback and permits a later explicit Enhanced request', async () => {
	const distribution = withPrimary(index()), path = distribution.files[0].path;
	let enabled = true, complete;
	const urls = [];
	const client = createTextureDownloads({ distribution, digest, cacheStorage: null, warning: quiet,
		enabled: () => enabled, writeFile: () => true,
		fetcher: url => {
			urls.push(url);
			return urls.length === 1 ? new Promise(resolve => complete = resolve) : Promise.resolve(new Response(png()));
		},
	});
	const active = client.request(path);
	await new Promise(resolve => setImmediate(resolve));
	enabled = false;
	complete(new Response('', { status: 403 }));
	assert.equal(await active, false);
	await new Promise(resolve => setImmediate(resolve));
	assert.deepEqual(urls, [distribution.files[0].url], 'a new fallback fetch needs Enhanced to remain selected');
	enabled = true;
	assert.equal(await client.request(path), true);
	assert.equal(urls.length, 2);
});

test('failed integrity checks fall back once and never write or cache unverified bytes', async () => {
	const distribution = index(), bytes = png(), path = distribution.files[0].path, storage = cache();
	bytes[30]++;
	let writes = 0, fetches = 0, warnings = 0;
	const client = createTextureDownloads({ distribution, cacheStorage: storage, digest,
		warning: () => warnings++, writeFile: () => writes++, fetcher: async () => { fetches++; return new Response(bytes); },
	});
	assert.equal(await client.request(path), false);
	assert.equal(await client.request(path), false);
	assert.equal(fetches, 1);
	assert.equal(warnings, 1);
	assert.equal(writes, 0);
	assert.equal(storage.entries.size, 0);
});

test('Original choice and unknown paths issue no requests; a choice changed during a fetch prevents writing', async () => {
	const distribution = index(), path = distribution.files[0].path;
	let enabled = false, fetches = 0, writes = 0, complete;
	const client = createTextureDownloads({ distribution, cacheStorage: null, digest, warning: quiet, enabled: () => enabled,
		writeFile: () => writes++, fetcher: () => { fetches++; return new Promise(resolve => complete = resolve); },
	});
	assert.equal(await client.request(path), false);
	enabled = true;
	assert.equal(await client.request('textures/unknown.png'), false);
	assert.equal(fetches, 0);
	const pending = client.request(path);
	await new Promise(resolve => setImmediate(resolve));
	enabled = false;
	complete(new Response(png()));
	assert.equal(await pending, false);
	assert.equal(writes, 0);
});

test('download concurrency stays bounded including an asynchronous GPU staging back pressure', async () => {
	const paths = Array.from({ length: 12 }, (_, number) => `textures/levels/jungle/${number}.wct.png`);
	const distribution = index(paths);
	let active = 0, peak = 0, release;
	const gate = new Promise(resolve => release = resolve);
	const client = createTextureDownloads({ distribution, cacheStorage: null, digest, warning: quiet,
		fetcher: async () => { active++; peak = Math.max(peak, active); return new Response(png()); },
		writeFile: async () => { await gate; active--; return true; },
	});
	const requests = paths.map(path => client.request(path));
	await new Promise(resolve => setImmediate(resolve));
	assert.equal(peak, 3);
	release();
	assert.deepEqual(await Promise.all(requests), paths.map(() => true));
	assert.equal(peak, 3);
});

test('cache quota errors leave a verified texture usable', async () => {
	const distribution = index(), path = distribution.files[0].path;
	let writes = 0;
	const client = createTextureDownloads({ distribution, digest, warning: quiet, writeFile: () => writes++,
		cacheStorage: { open: async () => ({ match: async () => null, put: async () => { throw new Error('QuotaExceeded'); } }) },
		fetcher: async () => new Response(png()),
	});
	assert.equal(await client.request(path), true);
	assert.equal(writes, 1);
});

test('an oversized streamed response is cancelled before decoding or writing', async () => {
	const distribution = index(), path = distribution.files[0].path;
	let cancelled = false, writes = 0;
	const client = createTextureDownloads({ distribution, cacheStorage: null, digest, warning: quiet, writeFile: () => writes++,
		fetcher: async () => new Response(new ReadableStream({
			start(controller) { controller.enqueue(new Uint8Array(50)); },
			cancel() { cancelled = true; },
		})),
	});
	assert.equal(await client.request(path), false);
	assert.equal(cancelled, true);
	assert.equal(writes, 0);
});

test('truncated PNGs, missing IHDR and dimensions different from the index never reach the decoder', async () => {
	const noHeader = png(); noHeader[12] = 0;
	for (const bytes of [png().slice(0, 20), noHeader, png(9, 4)]) {
		const distribution = index();
		distribution.files[0].sha256 = createHash('sha256').update(bytes).digest('hex');
		let writes = 0;
		const client = createTextureDownloads({ distribution, cacheStorage: null, digest, warning: quiet,
			writeFile: () => writes++, fetcher: async () => new Response(bytes),
		});
		assert.equal(await client.request(distribution.files[0].path), false);
		assert.equal(writes, 0);
	}
});
