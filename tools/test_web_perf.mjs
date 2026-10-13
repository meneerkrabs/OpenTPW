import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, symlink, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { listFiles, mainThreadBusy, median, medianResults, percentile, safeResolve, startServer, summarizeFrames } from './web-perf/lib.mjs';

// The browser performance harness (tools/web-perf/run.mjs) without a browser: its server, path handling and statistics.

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
