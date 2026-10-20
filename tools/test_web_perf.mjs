import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, symlink, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { chromeArguments, keyMetrics, listFiles, loopbackOrigin, mainThreadBusy, median, medianResults, percentile, safeResolve, startServer, summarizeFrames } from './web-perf/lib.mjs';
import { markdown } from './web-perf/report.mjs';

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
