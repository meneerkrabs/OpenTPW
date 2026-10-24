import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, symlink, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { chromeArguments, keyMetrics, listFiles, loopbackOrigin, mainThreadBusy, median, medianResults, percentile, safeResolve, startServer, summarizeFrames, UPGRADE_LIMIT } from './web-perf/lib.mjs';
import { markdown } from './web-perf/report.mjs';
import { doorEvents, profileClockOffset, profileFrames, profileOfFrame } from './web-perf/events.mjs';

// The browser performance harness (tools/web-perf/run.mjs) without a browser: its server, path handling, statistics and reports.

const reportHeader = '## test — 2026-10-18T00:00:00.000Z (Chrome/test, renderer unknown)\n';
const format = results => markdown({ label: 'test', date: '2026-10-18T00:00:00.000Z', chrome: 'Chrome/test', results });
const start = { firstFrameMs: 100, lobbyMs: 200 };
const steady = { mainThreadBusyPercent: 20, fps: 60, p95Ms: 5, largestGapMs: 17, longTasks: 0 };
const startLine = '- Start: first frame 100 ms, lobby 200 ms';
const steadyLine = name => `- ${name} steady: main thread busy 20%, 60 fps, frame p95 5 ms, largest gap 17 ms, long tasks 0`;

for (const [name, results, lines] of [
	['no scenarios', {}, []],
	['start only', { start }, [startLine]],
	['lobby only', { lobby: steady }, [steadyLine('Lobby')]],
	['start and lobby', { start, lobby: steady }, [startLine, steadyLine('Lobby')]],
	['failed start', { start: { error: 'startup failed' } }, ['- start: FAILED — startup failed']],
	['failed Jungle', { start, jungle: { error: 'park failed' } }, [startLine, '- jungle: FAILED — park failed']],
	['Jungle without cold results', { jungle: { steady } }, [steadyLine('Jungle')]],
]) {
	test(`the Markdown report handles ${name}`, () => {
		assert.equal(format(results), reportHeader + (lines.length ? '\n' + lines.join('\n') : ''));
	});
}

test('the Markdown report retains Jungle construction frames, steady windows and profiles', () => {
	const results = {
		start, lobby: steady,
		jungle: {
			cold: {
				readyMs: 300, maxFrameMs: 60, longFramesMs: [60, 50], buildFramesMs: [60, 50, 20],
				startProfile: { build: { ms: 130, count: 3 } }, profile: { build: { ms: 135, count: 4 } },
			},
			steady, zoomed: { ...steady, zoom: 300 },
		},
	};
	assert.equal(format(results), reportHeader + '\n' + [
		startLine, steadyLine('Lobby'), steadyLine('Jungle'), steadyLine('Jungle at zoom 300'),
		'- Jungle cold: ready in 300 ms, largest frame 60 ms; frames over 50 ms: 60, 50',
		'- Jungle built over 3 frames, construction ms per frame (the last enters the park): 60, 50, 20',
		'- Jungle load profile until ready (ms, count; nested sections overlap): build 130 (3)',
		'- Jungle load profile including 5 s after: build 135 (4)',
	].join('\n'));
});

test('the Markdown report omits absent or empty Jungle construction frame lists', () => {
	for (const buildFramesMs of [undefined, []]) {
		assert.equal(format({ jungle: { cold: { readyMs: 30, maxFrameMs: 10, buildFramesMs } } }),
			reportHeader + '\n- Jungle cold: ready in 30 ms, largest frame 10 ms');
	}
});

test('safeResolve keeps paths below the root', () => {
	const root = path.resolve(os.tmpdir(), 'opentpw-root');
	assert.equal(safeResolve(root, 'Data/ui.wad'), path.join(root, 'Data', 'ui.wad'));
	assert.equal(safeResolve(root, 'Data%2Flevels%2Fjungle%2Fterrain.wad'), path.join(root, 'Data', 'levels', 'jungle', 'terrain.wad'));
	assert.equal(safeResolve(root, '../secret'), path.join(root, 'secret'), 'a leading .. is clamped at the root, never above it');
	assert.equal(safeResolve(root, '%2e%2e/%2e%2e/etc/passwd'), path.join(root, 'etc', 'passwd'));
	assert.equal(safeResolve(root, 'a\0b'), null);
	assert.equal(safeResolve(root, '%E0%A4%A'), null, 'malformed escapes are refused');
});

test('percentiles use the nearest rank', () => {
	const values = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
	assert.equal(percentile(values, 0.5), 5);
	assert.equal(percentile(values, 0.95), 10);
	assert.equal(percentile(values, 0), 1);
	assert.equal(percentile([], 0.5), null);
});

test('frame summaries count rate, slow frames and the largest gap', () => {
	const frames = [[0, 4], [16, 5], [33, 60], [100, 3], [116, 2]];
	const summary = summarizeFrames(frames, 1000);
	assert.equal(summary.frames, 5);
	assert.equal(summary.fps, 5);
	assert.equal(summary.maxMs, 60);
	assert.equal(summary.over33Ms, 1);
	assert.equal(summary.over50Ms, 1);
	assert.deepEqual(summary.longFramesMs, [60]);
	assert.equal(summary.largestGapMs, 67);
	assert.equal(summary.totalMs, 74);
	assert.deepEqual(summarizeFrames([], 1000).maxMs, null);
});

test('main-thread occupancy is task time over wall time, null without valid metrics', () => {
	const metrics = (timestamp, task) => ({ metrics: [{ name: 'Timestamp', value: timestamp }, { name: 'TaskDuration', value: task }] });
	assert.equal(mainThreadBusy(metrics(10, 1), metrics(40, 7)), 20);
	assert.equal(mainThreadBusy(metrics(10, 1), metrics(10, 2)), null, 'an empty interval');
	assert.equal(mainThreadBusy({ metrics: [{ name: 'Timestamp', value: 10 }] }, metrics(40, 7)), null, 'a missing metric is not read as 0');
	assert.equal(mainThreadBusy(metrics(10, 5), metrics(40, 1)), null, 'task time cannot go backwards');
});

test('several runs combine into medians field by field', () => {
	assert.equal(median([3, 1, 2]), 2);
	assert.equal(median([4, 1, 3, 2]), 2.5);
	assert.equal(median([]), null);
	const combined = medianResults([
		{ jungle: { cold: { maxFrameMs: 2400 }, steady: { fps: 60 } }, buy: { openMenu: { maxFrameMs: 80, selected: 'tvsim' } }, memory: { samples: [1] } },
		{ jungle: { cold: { maxFrameMs: 2550 }, steady: { fps: 59 } }, buy: { openMenu: { maxFrameMs: 96, selected: 'tvsim' } }, memory: { samples: [2] } },
		{ jungle: { cold: { maxFrameMs: 2490 }, steady: { fps: 60 } }, buy: { openMenu: { maxFrameMs: 81, selected: 'tvsim' } } },
	]);
	assert.deepEqual(combined, {
		jungle: { cold: { maxFrameMs: 2490 }, steady: { fps: 60 } },
		buy: { openMenu: { maxFrameMs: 81, selected: 'tvsim' } },
		memory: { samples: [1] },
	});
});

test('the compared measures come from the report, missing ones left out', () => {
	const rows = keyMetrics({
		jungle: { cold: { maxFrameMs: 2500, readyMs: 2800 }, steady: { p95Ms: 4.7, memory: { wasmBytes: 230 * 1048576 } } },
		buy: { openMenu: { maxFrameMs: 80 }, 'Belly Bounce': { maxFrameMs: 70 }, 'Drinks Shop': { missing: true } },
		digest: { textureMs: 900, simulationMs: 4000 },
	});
	assert.deepEqual(rows.map(([name]) => name), ['Jungle cold: largest frame', 'Jungle cold: ready', 'Jungle: frame p95',
		'Open buy menu: largest frame', 'Belly Bounce: largest frame', 'Digest: texture decoding', 'Digest: fixed park run', 'Wasm memory']);
	assert.equal(rows.at(-1)[1], 230);
});

test('the server lists the game folder and serves it beside the page, nothing outside', async () => {
	const base = await mkdtemp(path.join(os.tmpdir(), 'opentpw-web-perf-test-'));
	try {
		const web = path.join(base, 'web'), game = path.join(base, 'game');
		await mkdir(path.join(game, 'Data', 'levels', 'jungle'), { recursive: true });
		await mkdir(web, { recursive: true });
		await writeFile(path.join(web, 'index.html'), '<!doctype html>');
		await writeFile(path.join(web, 'main.js'), 'export {};');
		await writeFile(path.join(game, 'Data', 'ui.wad'), 'ui');
		await writeFile(path.join(game, 'Data', 'levels', 'jungle', 'terrain.wad'), 'terrain');
		await writeFile(path.join(base, 'outside.txt'), 'private');
		assert.deepEqual(await listFiles(game), ['Data/levels/jungle/terrain.wad', 'Data/ui.wad']);
		const server = await startServer(web, game);
		try {
			const get = async url => { const response = await fetch(server.origin + url); return [response.status, await response.text(), response.headers.get('content-type')]; };
			assert.deepEqual(await get('/game/files.txt'), [200, 'Data/levels/jungle/terrain.wad\nData/ui.wad\n', 'text/plain; charset=utf-8']);
			assert.deepEqual((await get('/game/Data/levels/jungle/terrain.wad')).slice(0, 2), [200, 'terrain']);
			assert.deepEqual(await get('/'), [200, '<!doctype html>', 'text/html; charset=utf-8']);
			assert.equal((await get('/main.js'))[2], 'text/javascript; charset=utf-8');
			assert.equal((await get('/game/..%2F..%2Foutside.txt'))[0], 404, 'an escaped .. stays inside the game folder');
			assert.equal((await get('/game/Data/missing.wad'))[0], 404);
			assert.ok(server.origin.startsWith('http://127.0.0.1:'), 'bound to the loopback address only');
			let linked = true;
			try {
				await symlink(path.join(base, 'outside.txt'), path.join(game, 'Data', 'link.txt'));
			} catch {
				linked = false; // creating links needs a privilege on some Windows runners
			}
			if (linked)
				assert.equal((await get('/game/Data/link.txt'))[0], 403, 'a link inside the game folder does not serve its target outside it');
		} finally {
			await server.close();
		}
	} finally {
		await rm(base, { recursive: true, force: true });
	}
});

test('Chrome starts muted and headless unless a run asks for sound or a window', () => {
	const muted = chromeArguments('/tmp/profile');
	assert.ok(muted.includes('--mute-audio'));
	assert.ok(muted.includes('--headless=new'));
	const audible = chromeArguments('/tmp/profile', { audible: true, headed: true });
	assert.ok(!audible.includes('--mute-audio'));
	assert.ok(!audible.includes('--headless=new'));
});

test('the upstream is a loopback http origin only', () => {
	assert.equal(loopbackOrigin('http://127.0.0.1:8080/ignored?x=1'), 'http://127.0.0.1:8080');
	assert.equal(loopbackOrigin('http://localhost:9'), 'http://localhost:9');
	assert.equal(loopbackOrigin('http://[::1]:5'), 'http://[::1]:5');
	for (const value of ['http://example.com', 'https://127.0.0.1:1', 'http://10.0.0.1', 'file:///etc/passwd'])
		assert.throws(() => loopbackOrigin(value), /loopback/);
});

test('a forwarded request keeps the upstream host, even with an absolute-form target', async () => {
	const seen = [];
	const { createServer } = await import('node:http');
	const upstream = createServer((request, response) => { seen.push({ host: request.headers.host, url: request.url }); response.end('ok'); });
	await new Promise(resolve => upstream.listen(0, '127.0.0.1', resolve));
	const root = await mkdtemp(path.join(os.tmpdir(), 'web-perf-upstream-'));
	const harness = await startServer(root, root, `http://127.0.0.1:${upstream.address().port}`);
	try {
		const { request } = await import('node:http');
		const port = new URL(harness.origin).port;
		// an absolute-form request target names another host; only its path and query may reach the upstream
		await new Promise((resolve, reject) => request({ host: '127.0.0.1', port, path: 'http://example.com/elsewhere?q=1' }, response => { response.resume(); response.on('end', resolve); }).on('error', reject).end());
		assert.deepEqual(seen, [{ host: `127.0.0.1:${upstream.address().port}`, url: '/elsewhere?q=1' }]);
	} finally {
		await harness.close();
		await new Promise(resolve => upstream.close(resolve));
		await rm(root, { recursive: true, force: true });
	}
});

// A CPU profile of three frames (root > frame main.js > work), 1 ms samples from profile time 0 (microseconds), with idle samples between:
// frame A at 0-3 ms, frame B (slow) at 10-30 ms with a garbage collection sample in it, frame C at 40-42 ms.
const syntheticProfile = () => {
	const nodes = [
		{ id: 1, callFrame: { functionName: '(root)', url: '' }, children: [2, 4, 5] },
		{ id: 2, callFrame: { functionName: 'frame', url: 'http://127.0.0.1:1/main.js' }, children: [3] },
		{ id: 3, callFrame: { functionName: 'draw', url: '' } },
		{ id: 4, callFrame: { functionName: '(idle)', url: '' } },
		{ id: 5, callFrame: { functionName: '(garbage collector)', url: '' } },
	];
	const samples = [];
	for (let ms = 0; ms < 50; ms++)
		samples.push(ms < 3 || (ms >= 10 && ms < 30) || (ms >= 40 && ms < 42) ? (ms === 20 ? 5 : 3) : 4);
	return { nodes, startTime: 0, endTime: 50000, samples, timeDeltas: samples.map((_, index) => index === 0 ? 0 : 1000) };
};

test('the frames a CPU profile saw are the runs under the page\'s frame callback, a short pause inside one included', () => {
	// the profile started at page time 1000
	assert.deepEqual(profileFrames(syntheticProfile(), 1000), [[1000, 1003], [1010, 1030], [1040, 1042]]);
});

test('the profile\'s clock is lined up with the page\'s by the frames both saw', () => {
	const runs = profileFrames(syntheticProfile(), 1000);
	// the page recorded the same three frames 7 ms earlier than the profile's clock shows them
	const frames = [[993, 3], [1003, 20], [1033, 2]];
	assert.equal(profileClockOffset(runs, 1000, frames), 7);
	assert.equal(profileClockOffset([], 1000, frames), null);
	const view = profileOfFrame(syntheticProfile(), 1000, 7, 1003, 20);
	assert.equal(view.profileOffsetMs, 7);
	// the moved window holds the frame's own samples only, not the idle time before it
	assert.deepEqual(view.functions.map(entry => entry.name), ['draw', '(garbage collector)']);
});

test('the park door is clicked through the entry price window: open, close, open again, each with its state time', async () => {
	const calls = [];
	let open = false, guests = 0;
	const page = {
		async call(method, ...args) {
			calls.push([method, ...args]);
			if (method === 'PerfClick') {
				open = !open;
				if (open)
					guests++;
			}
			return true;
		},
		async eval(expression) {
			if (expression.includes('PerfParkOpen'))
				return open;
			if (expression.includes('PerfGuests'))
				return guests;
			throw new Error(`unexpected ${expression}`);
		},
	};
	const records = {};
	await doorEvents(page, async (name, act) => {
		await act();
		return (records[name] = { name });
	}, 1);
	assert.deepEqual(calls, [['PerfOpenWindow', 'entryPrice'], ['PerfClick', 'door'], ['PerfClick', 'door'], ['PerfClick', 'door'], ['PerfKey', 'Back']]);
	assert.deepEqual(Object.keys(records), ['park door: first click (opens)', 'park door: second click (closes)', 'park door: third click (opens again)']);
	for (const record of Object.values(records))
		assert.ok(record.stateMs >= 0);
	assert.ok(records['park door: first click (opens)'].firstGuestMs >= 0);
	assert.equal(records['park door: second click (closes)'].firstGuestMs, null);
});

test('the Markdown report lists the door scenario\'s presses', () => {
	const press = { name: 'park door: first click (opens)', maxMs: 12, p95Ms: 2, over20Ms: 0, longTasks: 0, slow: [] };
	assert.ok(format({ door: { events: { [press.name]: press } } }).includes('| park door: first click (opens) | 12 | 2 | 0 | 0 |  |'));
});

/** An upgrade request to the local server: resolves with the upgraded socket and the answer's status, or with a plain answer's status and body. */
async function upgradeThrough(origin, pathname) {
	const { request } = await import('node:http');
	const url = new URL(origin);
	return new Promise((resolve, reject) => {
		const outgoing = request({ host: url.hostname, port: url.port, path: pathname, headers: { connection: 'Upgrade', upgrade: 'websocket' } });
		outgoing.on('upgrade', (response, socket) => resolve({ status: response.statusCode, socket, headers: response.headers }));
		outgoing.on('response', response => {
			const body = [];
			response.on('data', chunk => body.push(chunk));
			response.on('end', () => resolve({ status: response.statusCode, body: Buffer.concat(body).toString() }));
		});
		outgoing.on('error', reject);
		outgoing.end();
	});
}

/** The next chunk a socket receives. */
const nextChunk = socket => new Promise(resolve => socket.once('data', chunk => resolve(chunk.toString())));

test('a WebSocket upgrade below /api/ is forwarded to the upstream and joined both ways; any other is refused', async () => {
	const { createServer } = await import('node:http');
	const seen = [];
	const upstream = createServer((request, response) => response.end('plain'));
	upstream.on('upgrade', (request, socket) => {
		seen.push({ url: request.url, host: request.headers.host, upgrade: request.headers.upgrade });
		if (request.url.startsWith('/api/refused')) {
			socket.end('HTTP/1.1 403 Forbidden\r\nContent-Length: 2\r\n\r\nno');
			return;
		}
		socket.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nX-Upstream: yes\r\n\r\n');
		socket.on('data', chunk => socket.write(Buffer.concat([Buffer.from('echo:'), chunk])));
		// a server closes its side once the client closed its own
		socket.on('end', () => socket.end());
	});
	await new Promise(resolve => upstream.listen(0, '127.0.0.1', resolve));
	const root = await mkdtemp(path.join(os.tmpdir(), 'web-perf-upgrade-'));
	const harness = await startServer(root, root, `http://127.0.0.1:${upstream.address().port}`);
	try {
		// the chat socket: the upstream's 101 with its headers, then bytes each way
		const chat = await upgradeThrough(harness.origin, '/api/v1/chat?room=lobby');
		assert.equal(chat.status, 101);
		assert.equal(chat.headers['x-upstream'], 'yes');
		assert.deepEqual(seen, [{ url: '/api/v1/chat?room=lobby', host: `127.0.0.1:${upstream.address().port}`, upgrade: 'websocket' }]);
		const echoed = nextChunk(chat.socket);
		chat.socket.write('hello');
		assert.equal(await echoed, 'echo:hello');
		const entry = harness.log.find(item => item.upgrade);
		assert.deepEqual([entry.path, entry.status, entry.bytesIn, entry.bytesOut], ['/api/v1/chat', 101, 5, 10]);
		// an upgrade elsewhere is refused before the upstream hears it
		assert.equal((await upgradeThrough(harness.origin, '/index.html')).status, 404);
		// an upstream that will not upgrade: its answer comes back as it is
		const refused = await upgradeThrough(harness.origin, '/api/refused');
		assert.deepEqual([refused.status, refused.body], [403, 'no']);
		assert.equal(seen.length, 2);
		chat.socket.destroy();
	} finally {
		await harness.close();
		await new Promise(resolve => upstream.close(resolve));
		await rm(root, { recursive: true, force: true });
	}
});

test('the forwarded sockets are bounded, and closing the local server ends the open ones', async () => {
	const { createServer } = await import('node:http');
	const upstream = createServer((request, response) => response.end('plain'));
	upstream.on('upgrade', (request, socket) => {
		socket.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n\r\n');
		socket.on('end', () => socket.end());
	});
	await new Promise(resolve => upstream.listen(0, '127.0.0.1', resolve));
	const root = await mkdtemp(path.join(os.tmpdir(), 'web-perf-upgrade-'));
	const harness = await startServer(root, root, `http://127.0.0.1:${upstream.address().port}`);
	try {
		const open = [];
		for (let index = 0; index < UPGRADE_LIMIT; index++)
			open.push(await upgradeThrough(harness.origin, `/api/v1/coop?n=${index}`));
		assert.ok(open.every(item => item.status === 101));
		assert.equal((await upgradeThrough(harness.origin, '/api/v1/coop?n=over')).status, 503, 'one past the limit is refused');
		// one closed makes room for another
		const ended = new Promise(resolve => open[0].socket.once('close', resolve));
		open[0].socket.destroy();
		await ended;
		await new Promise(resolve => setTimeout(resolve, 50));
		const another = await upgradeThrough(harness.origin, '/api/v1/coop?n=again');
		assert.equal(another.status, 101);
		// close ends what is still open instead of waiting for it
		const closed = Promise.all([...open.slice(1), another].map(item => new Promise(resolve => item.socket.once('close', resolve))));
		await harness.close();
		await closed;
	} finally {
		await new Promise(resolve => upstream.close(resolve));
		await rm(root, { recursive: true, force: true });
	}
});

test('an upstream answer that refuses an upgrade is bounded in time and size, and one cut off is a bad gateway', async () => {
	const { createServer } = await import('node:http');
	const upstream = createServer((request, response) => response.end('plain'));
	upstream.on('upgrade', (request, socket) => {
		socket.on('end', () => socket.end());
		if (request.url.startsWith('/api/stalled'))
			socket.write('HTTP/1.1 403 Forbidden\r\nContent-Length: 100\r\n\r\npartial');
		else if (request.url.startsWith('/api/long'))
			socket.end(`HTTP/1.1 403 Forbidden\r\nContent-Length: 4096\r\n\r\n${'x'.repeat(4096)}`);
		else if (request.url.startsWith('/api/cut'))
			socket.write('HTTP/1.1 403 Forbidden\r\nContent-Length: 100\r\n\r\npartial', () => socket.destroy());
	});
	await new Promise(resolve => upstream.listen(0, '127.0.0.1', resolve));
	const root = await mkdtemp(path.join(os.tmpdir(), 'web-perf-upgrade-'));
	const harness = await startServer(root, root, `http://127.0.0.1:${upstream.address().port}`, { answerMs: 300, refusalBytes: 1024, sockets: 1 });
	try {
		const started = Date.now();
		assert.equal((await upgradeThrough(harness.origin, '/api/stalled')).status, 504, 'an answer that never ends times out');
		assert.ok(Date.now() - started < 5000);
		// each answer freed its place (one at a time here)
		assert.equal((await upgradeThrough(harness.origin, '/api/long')).status, 502, 'a refusal past the size limit');
		assert.equal((await upgradeThrough(harness.origin, '/api/cut')).status, 502, 'a refusal cut off before its end');
		assert.deepEqual(harness.log.filter(item => item.upgrade).map(item => item.status), [504, 502, 502]);
	} finally {
		await harness.close();
		await new Promise(resolve => upstream.close(resolve));
		await rm(root, { recursive: true, force: true });
	}
});

test('a refused client that keeps its side open keeps its place until the local server closes it', async () => {
	const { createServer } = await import('node:http');
	const net = await import('node:net');
	const upstream = createServer((request, response) => response.end('plain'));
	upstream.on('upgrade', (request, socket) => socket.end('HTTP/1.1 403 Forbidden\r\nContent-Length: 2\r\n\r\nno'));
	await new Promise(resolve => upstream.listen(0, '127.0.0.1', resolve));
	const root = await mkdtemp(path.join(os.tmpdir(), 'web-perf-upgrade-'));
	const harness = await startServer(root, root, `http://127.0.0.1:${upstream.address().port}`, { sockets: 1, endMs: 300 });
	try {
		const port = Number(new URL(harness.origin).port);
		// a raw client: it reads the refusal and never ends its own side (a half-open socket does not answer the server's end with its own)
		const lingering = net.connect({ port, host: '127.0.0.1', allowHalfOpen: true });
		lingering.write('GET /api/v1/chat HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: Upgrade\r\nUpgrade: websocket\r\n\r\n');
		const refused = await new Promise(resolve => lingering.once('data', chunk => resolve(chunk.toString())));
		assert.match(refused, /^HTTP\/1\.1 403 Forbidden/);
		// its place is still taken after the answer's end (the half-open client never closes its own side)
		assert.equal((await upgradeThrough(harness.origin, '/api/v1/chat')).status, 503);
		// until the local server closes it, after the half-close wait (300 ms here)
		await new Promise(resolve => setTimeout(resolve, 900));
		assert.equal((await upgradeThrough(harness.origin, '/api/v1/chat')).status, 403);
		lingering.destroy();
	} finally {
		await harness.close();
		await new Promise(resolve => upstream.close(resolve));
		await rm(root, { recursive: true, force: true });
	}
});

test('a refusal before any forwarding keeps a lingering client among the sockets the local server ends', async () => {
	const net = await import('node:net');
	const root = await mkdtemp(path.join(os.tmpdir(), 'web-perf-upgrade-'));
	// no upstream answers here: the refusals are the local server's own
	const harness = await startServer(root, root, 'http://127.0.0.1:9', { endMs: 60000 });
	try {
		const port = Number(new URL(harness.origin).port);
		const lingering = net.connect({ port, host: '127.0.0.1', allowHalfOpen: true });
		lingering.write('GET /not-api HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: Upgrade\r\nUpgrade: websocket\r\n\r\n');
		assert.match(await new Promise(resolve => lingering.once('data', chunk => resolve(chunk.toString()))), /^HTTP\/1\.1 404 Not Found/);
		// the client keeps its side open, and the wait for it is long: the server's close ends it anyway
		const ended = new Promise(resolve => lingering.once('end', resolve));
		await harness.close();
		await ended;
		lingering.destroy();
	} finally {
		await rm(root, { recursive: true, force: true });
	}
});
