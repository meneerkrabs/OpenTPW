#!/usr/bin/env node
// End-to-end check of the page's game data sources (docs/WEB.md, "Game data sources"), in headless, muted Chrome with a fresh profile:
// the page loads a CD image from a local test server (a ZIP, ISO or BIN the player already has on disk) through "Load from this address", or
// through the file chooser (--via file), keeps the files in the origin-private store, starts the game from them and enters the Jungle park. A
// second visit in the same profile then shows Play without any request to the image server. The image never leaves this machine and nothing
// is committed: pass your own file.
//
//   node tools/web-archive-smoke.mjs --web out/web/wwwroot --image ~/opentpw-data/archive-tpworld/TPWORLD.zip [--overlay source/OpenTPW.Web/wwwroot]
//
// --probe-archive needs no image: it clicks "Load from archive.org" in the real page, waits until about 15 MB of the real stream have been read
// (CORS, the redirect and the stream work in this browser), then cancels. It reads a few megabytes of archive.org, nothing more.
// --overlay serves the files of a source wwwroot over the published one (a JavaScript-only change needs no new publish). --norange makes the
// image server answer 200 to ranged requests; --cors off removes its CORS headers (the page must then explain the failure).
import { createServer } from 'node:http';
import { createReadStream, promises as fs, statSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { Cdp, launchChrome, safeResolve, sleep, stopChrome } from './web-perf/lib.mjs';

const MIME = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.json': 'application/json', '.wasm': 'application/wasm', '.dat': 'application/octet-stream', '.txt': 'text/plain' };

function parse(argv) {
	const options = { web: null, image: null, overlay: null, via: 'url', ranges: true, cors: true, chrome: process.env.CHROME ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', timeoutMs: 600000 };
	for (let index = 2; index < argv.length; index++) {
		const [flag, inline] = argv[index].split(/=(.*)/s);
		const value = () => inline ?? argv[++index];
		if (flag === '--web') options.web = value();
		else if (flag === '--image') options.image = value();
		else if (flag === '--overlay') options.overlay = value();
		else if (flag === '--via') options.via = value();
		else if (flag === '--norange') options.ranges = false;
		else if (flag === '--cors') options.cors = value() !== 'off';
		else if (flag === '--chrome') options.chrome = value();
		else if (flag === '--probe-archive') options.probeArchive = true;
		else throw new Error(`Unknown option ${flag}`);
	}
	if (!options.web || (!options.image && !options.probeArchive))
		throw new Error('Usage: node tools/web-archive-smoke.mjs --web <published wwwroot> --image <zip, iso or bin> [--overlay <source wwwroot>] [--via url|file] [--norange] [--cors off]');
	return options;
}

async function listen(server) {
	await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
	return server.address().port;
}

/** The published page (and, first, the overlay's files), uncompressed. */
function pageServer(web, overlay) {
	return createServer(async (request, response) => {
		const url = new URL(request.url, 'http://x');
		const relative = url.pathname === '/' ? '/index.html' : url.pathname;
		for (const root of [overlay, web].filter(Boolean)) {
			const file = safeResolve(root, relative);
			if (!file)
				break;
			try {
				const info = await fs.stat(file);
				if (!info.isFile())
					continue;
				response.writeHead(200, { 'content-type': MIME[path.extname(file)] ?? 'application/octet-stream', 'content-length': info.size, 'cache-control': 'no-store' });
				createReadStream(file).pipe(response);
				return;
			} catch {
				// try the next root
			}
		}
		response.writeHead(404).end();
	});
}

/** The image on another origin, like archive.org: CORS headers, optional ranges, a log of every request. */
function imageServer(file, { ranges, cors }, log) {
	const size = statSync(file).size;
	return createServer((request, response) => {
		const headers = cors ? { 'access-control-allow-origin': '*', 'access-control-allow-headers': 'Range', 'access-control-expose-headers': 'Content-Range, Content-Length' } : {};
		if (request.method === 'OPTIONS') {
			response.writeHead(cors ? 204 : 405, headers).end();
			return;
		}
		const range = ranges ? /^bytes=(\d*)-(\d*)$/.exec(request.headers.range ?? '') : null;
		let start = 0, end = size - 1, status = 200;
		if (range) {
			status = 206;
			if (range[1] === '') { start = Math.max(0, size - Number(range[2])); }
			else { start = Number(range[1]); if (range[2] !== '') end = Math.min(end, Number(range[2])); }
		}
		log.push({ path: request.url, range: request.headers.range ?? null, status });
		response.writeHead(status, { ...headers, 'content-type': 'application/octet-stream', 'accept-ranges': ranges ? 'bytes' : 'none', 'content-length': end - start + 1, ...(status === 206 ? { 'content-range': `bytes ${start}-${end}/${size}` } : {}) });
		createReadStream(file, { start, end }).pipe(response);
	});
}

async function main() {
	const options = parse(process.argv);
	const requests = [];
	const page = pageServer(path.resolve(options.web), options.overlay ? path.resolve(options.overlay) : null);
	const images = options.image ? imageServer(options.image, options, requests) : null;
	const pagePort = await listen(page);
	const imagePort = images ? await listen(images) : 0;
	const imageUrl = images ? `http://127.0.0.1:${imagePort}/${path.basename(options.image)}` : '';
	const profile = await fs.mkdtemp(path.join(os.tmpdir(), 'opentpw-archive-smoke-'));
	const { chrome, url } = await launchChrome(options.chrome, profile, {});
	const cdp = await Cdp.connect(url);
	const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
	const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
	const send = (method, params) => cdp.send(method, params, sessionId);
	const evaluate = async expression => {
		const { result, exceptionDetails } = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
		if (exceptionDetails)
			throw new Error(exceptionDetails.exception?.description ?? exceptionDetails.text);
		return result.value;
	};
	const logs = [];
	cdp.on(message => {
		if (message.method === 'Runtime.consoleAPICalled' && ['error', 'warning'].includes(message.params.type))
			logs.push(message.params.args.map(arg => arg.value ?? arg.description ?? '').join(' ').slice(0, 300));
	});
	await send('Runtime.enable');
	await send('Page.enable');
	const result = { via: options.via, ranges: options.ranges, cors: options.cors };
	const fail = async message => { throw Object.assign(new Error(message), { logs, fatal: true }); };
	try {
		const base = `http://127.0.0.1:${pagePort}/index.html?perf=1`;
		if (options.probeArchive) {
			await send('Page.navigate', { url: base });
			await waitFor(() => evaluate("!!document.getElementById('archiveLoad') && !document.getElementById('archiveLoad').disabled"), 120000, 'the page');
			await evaluate("document.getElementById('archiveLoad').click()");
			let text = '';
			await waitFor(async () => {
				text = await evaluate("document.getElementById('imageText').textContent + ' | ' + document.getElementById('status').textContent");
				result.lastText = text;
				if (/Could not|failed|cannot/i.test(text))
					await fail(`the archive.org probe failed: ${text}`);
				const match = /^(\d+) MB of/.exec(text);
				return match && Number(match[1]) >= 15;
			}, 120000, 'the first megabytes from archive.org');
			result.progressText = text;
			await evaluate("document.getElementById('imageCancel').click()");
			await waitFor(() => evaluate("/Cancelled/.test(document.getElementById('status').textContent)"), 30000, 'the cancel');
			result.afterCancel = await evaluate("document.getElementById('status').textContent");
			result.ok = true;
		} else {
			await send('Page.navigate', { url: options.via === 'url' ? `${base}&source=${encodeURIComponent(imageUrl)}` : base });
			await waitFor(() => evaluate("!!document.getElementById('archiveLoad') && !document.getElementById('archiveLoad').disabled"), 120000, 'the page (its buttons are enabled once its script is ready)');
			await sleep(500);
			result.defaultButton = await evaluate("document.getElementById('archiveLoad').textContent");
			result.detailsOpen = await evaluate("document.getElementById('otherSources').open");
			result.requestsBeforeClick = requests.length;
			if (requests.length !== 0)
				await fail('the page asked the image server before any click');
			const started = Date.now();
			if (options.via === 'url') {
				await evaluate("document.getElementById('urlLoad').click()");
			} else {
				// The file chooser: hand the file to the input through DevTools (no dialog).
				const { root } = await send('DOM.getDocument');
				const { nodeId } = await send('DOM.querySelector', { nodeId: root.nodeId, selector: '#imageFile' });
				await send('DOM.setFileInputFiles', { files: [path.resolve(options.image)], nodeId });
			}
			let last = '';
			let shown = 0;
			await waitFor(async () => {
				const state = await evaluate("JSON.stringify({ text: document.getElementById('imageText')?.textContent ?? '', status: document.getElementById('status')?.textContent ?? '', perf: globalThis.opentpwPerf ? JSON.parse(opentpwPerf.program.PerfState()) : null })").then(JSON.parse);
				const line = `${state.text} | ${state.status}`;
				if (line !== last && Date.now() - shown > 10000) { last = line; shown = Date.now(); console.log(`  ${Math.round((Date.now() - started) / 1000)} s: ${line.slice(0, 160)}`); }
				if (/Could not|failed|cannot|no room|not a Theme Park|cancel/i.test(state.status) && !/Starting/.test(state.status))
					await fail(`the load failed: ${state.status}`);
				return state.perf?.started && state.perf.menu;
			}, options.timeoutMs, 'the game to start from the loaded files');
			result.loadSeconds = Math.round((Date.now() - started) / 1000);
			result.imageRequests = requests.map(request => `${request.status} ${request.range ?? 'no range'}`);
			const kept = await evaluate("(async () => { const root = await navigator.storage.getDirectory(); const dir = await root.getDirectoryHandle('opentpw-originals'); const manifest = JSON.parse(await (await (await dir.getFileHandle('manifest.json')).getFile()).text()); const files = Object.values(manifest.files); return { total: files.length, stored: files.filter(file => file.stored).length, mode: manifest.mode, bytes: files.filter(file => file.stored).reduce((sum, file) => sum + file.size, 0) }; })()");
			result.kept = kept;
			if (kept.stored !== kept.total || kept.total === 0)
				await fail(`the store is not complete: ${JSON.stringify(kept)}`);
			await enterJungle(evaluate, fail);
			result.park = await evaluate('opentpwPerf.program.PerfState()').then(JSON.parse);
			const shot = await send('Page.captureScreenshot', { format: 'png' });
			await fs.writeFile(path.join(os.tmpdir(), 'opentpw-archive-smoke.png'), Buffer.from(shot.data, 'base64'));
			// Second visit: Play from the kept files, no request to the image server.
			const before = requests.length;
			await send('Page.navigate', { url: base });
			await waitFor(() => evaluate("!!document.getElementById('play') && !document.getElementById('kept').hidden"), 120000, 'Play on the second visit');
			result.playOnSecondVisit = true;
			await evaluate("document.getElementById('play').click()");
			await waitFor(async () => (await evaluate("globalThis.opentpwPerf ? opentpwPerf.program.PerfState() : '{}'").then(JSON.parse)).menu, 180000, 'the game on the second visit');
			result.imageRequestsSecondVisit = requests.length - before;
			if (requests.length !== before)
				await fail('the second visit asked the image server again');
		result.ok = true;
		}
	} catch (error) {
		result.ok = false;
		result.error = error.message;
		result.logs = error.logs ?? logs.slice(-15);
	} finally {
		cdp.close();
		await stopChrome(chrome);
		page.close(); images?.close();
		await fs.rm(profile, { recursive: true, force: true }).catch(() => {});
	}
	console.log(JSON.stringify(result, null, 2));
	process.exit(result.ok ? 0 : 1);
}

async function waitFor(test, timeoutMs, what) {
	const begin = Date.now();
	// A test that throws a fatal error ends the wait; any other failure (the page is still loading) is retried.
	while (!await test().catch(error => { if (error.fatal) throw error; return false; })) {
		if (Date.now() - begin > timeoutMs)
			throw new Error(`Timed out waiting for ${what}`);
		await sleep(250);
	}
}

/** Lobby: enter the park, Full Simulation, Jungle: the park's HUD shows when the loaded files are complete. */
async function enterJungle(evaluate, fail) {
	const steps = { lobby: ['enterPark'], gameMode: ['fullSimulation', 'ok'], newProgress: ['choice0'], themeStart: ['choice1'] };
	let screen = null, done = 0;
	const begin = Date.now();
	for (;;) {
		const state = JSON.parse(await evaluate('opentpwPerf.program.PerfState()'));
		if (state.hud && state.level)
			return state;
		if (state.menu !== screen) { screen = state.menu; done = 0; }
		const order = steps[state.menu];
		if (order && done < order.length && await evaluate(`opentpwPerf.program.PerfClick(${JSON.stringify(order[done])})`).catch(() => false))
			done++;
		const status = await evaluate("document.getElementById('status')?.textContent ?? ''");
		if (/stopped|could not start/i.test(status))
			await fail(`the game stopped: ${status}`);
		if (Date.now() - begin > 240000)
			await fail(`no park after 240 s; state ${JSON.stringify(state)}`);
		await sleep(200);
	}
}

main().catch(error => { console.error(error); process.exit(1); });
