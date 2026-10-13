// Building blocks of the browser performance harness (run.mjs, docs/BROWSER-PERFORMANCE.md): a local server for the
// published page and the player's own game folder, a minimal Chrome DevTools Protocol client, and frame statistics.
// No dependencies: Node's own http, fs, child_process, fetch and WebSocket.
import { createServer } from 'node:http';
import { createReadStream, promises as fs } from 'node:fs';
import { spawn } from 'node:child_process';
import path from 'node:path';

const MIME = {
	'.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8',
	'.css': 'text/css; charset=utf-8', '.json': 'application/json', '.wasm': 'application/wasm', '.txt': 'text/plain; charset=utf-8',
	'.png': 'image/png', '.ico': 'image/x-icon',
};

/** The absolute path of a URL path below root, or null when it would leave root. */
export function safeResolve(root, urlPath) {
	let decoded;
	try {
		decoded = decodeURIComponent(urlPath);
	} catch {
		return null;
	}
	if (decoded.includes('\0'))
		return null;
	const resolved = path.resolve(root, `.${path.posix.normalize(`/${decoded}`)}`);
	const base = path.resolve(root);
	return resolved === base || resolved.startsWith(base + path.sep) ? resolved : null;
}

/** Every file below root as a sorted list of '/'-separated relative paths (the page's files.txt). */
export async function listFiles(root) {
	const found = [];
	async function walk(directory, prefix) {
		for (const entry of await fs.readdir(directory, { withFileTypes: true })) {
			const relative = prefix ? `${prefix}/${entry.name}` : entry.name;
			if (entry.isDirectory())
				await walk(path.join(directory, entry.name), relative);
			else if (entry.isFile())
				found.push(relative);
		}
	}
	await walk(root, '');
	return found.sort();
}

/**
 * Serves the published wwwroot at / and the game folder at /game/ with a generated /game/files.txt, the page's local
 * test route (?data=/game/). Bound to 127.0.0.1 only: the game files never leave this machine.
 */
export async function startServer(webRoot, gameRoot) {
	const files = (await listFiles(gameRoot)).join('\n') + '\n';
	const realRoots = new Map([[webRoot, await fs.realpath(webRoot)], [gameRoot, await fs.realpath(gameRoot)]]);
	const server = createServer(async (request, response) => {
		const url = new URL(request.url, 'http://localhost');
		const send = (status, body, type = 'text/plain; charset=utf-8') => {
			response.writeHead(status, { 'Content-Type': type, 'Cache-Control': 'no-store' });
			response.end(body);
		};
		if (url.pathname === '/game/files.txt')
			return send(200, files);
		const [root, relative] = url.pathname.startsWith('/game/') ? [gameRoot, url.pathname.slice('/game/'.length)] : [webRoot, url.pathname === '/' ? 'index.html' : url.pathname.slice(1)];
		const resolved = safeResolve(root, relative);
		if (!resolved)
			return send(403, 'forbidden');
		try {
			// A symbolic link below a root must not serve its target outside it: compare the canonical paths.
			const file = await fs.realpath(resolved);
			const realRoot = realRoots.get(root);
			if (file !== realRoot && !file.startsWith(realRoot + path.sep))
				return send(403, 'forbidden');
			const stat = await fs.stat(file);
			if (!stat.isFile())
				return send(404, 'not found');
			response.writeHead(200, { 'Content-Type': MIME[path.extname(file).toLowerCase()] ?? 'application/octet-stream', 'Content-Length': stat.size, 'Cache-Control': 'no-store' });
			createReadStream(file).pipe(response);
		} catch {
			send(404, 'not found');
		}
	});
	await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
	return { server, origin: `http://127.0.0.1:${server.address().port}`, close: () => new Promise(resolve => server.close(resolve)) };
}

/** A minimal DevTools Protocol client over one browser WebSocket, with flattened page sessions. */
export class Cdp {
	#socket; #next = 1; #pending = new Map(); #listeners = [];

	static async connect(url) {
		const cdp = new Cdp();
		cdp.#socket = new WebSocket(url);
		await new Promise((resolve, reject) => {
			cdp.#socket.addEventListener('open', resolve, { once: true });
			cdp.#socket.addEventListener('error', reject, { once: true });
		});
		cdp.#socket.addEventListener('message', event => cdp.#receive(JSON.parse(event.data)));
		cdp.#socket.addEventListener('close', () => {
			for (const { reject } of cdp.#pending.values())
				reject(new Error('The DevTools connection closed'));
			cdp.#pending.clear();
		});
		return cdp;
	}

	#receive(message) {
		if (message.id && this.#pending.has(message.id)) {
			const { resolve, reject } = this.#pending.get(message.id);
			this.#pending.delete(message.id);
			message.error ? reject(new Error(`${message.error.message} (${message.error.code})`)) : resolve(message.result);
		} else if (message.method)
			for (const listener of this.#listeners)
				listener(message);
	}

	/** Sends a command; it fails after timeoutMs, or when the connection closes, instead of waiting for ever. */
	send(method, params = {}, sessionId, timeoutMs = 60000) {
		const id = this.#next++;
		return new Promise((resolve, reject) => {
			const timer = setTimeout(() => {
				this.#pending.delete(id);
				reject(new Error(`${method} got no answer within ${timeoutMs / 1000} s`));
			}, timeoutMs);
			const settle = handler => value => { clearTimeout(timer); handler(value); };
			this.#pending.set(id, { resolve: settle(resolve), reject: settle(reject) });
			this.#socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
		});
	}

	on(listener) { this.#listeners.push(listener); }

	close() { this.#socket.close(); }
}

/** Starts Chrome with a fresh profile and its DevTools port; resolves with the process and the browser WebSocket URL. */
export async function launchChrome(executable, profile, { headed = false, width = 1280, height = 900 } = {}) {
	const args = [
		`--user-data-dir=${profile}`, '--remote-debugging-port=0', '--no-first-run', '--no-default-browser-check',
		'--autoplay-policy=no-user-gesture-required', `--window-size=${width},${height}`, '--disable-background-timer-throttling',
		'--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows', 'about:blank',
	];
	if (!headed)
		args.unshift('--headless=new');
	const chrome = spawn(executable, args, { stdio: ['ignore', 'ignore', 'ignore'] });
	let failure = null;
	chrome.on('error', error => failure = error);
	const portFile = path.join(profile, 'DevToolsActivePort');
	for (let attempt = 0; attempt < 300; attempt++) {
		try {
			const [port, browserPath] = (await fs.readFile(portFile, 'utf8')).trim().split('\n');
			if (port && browserPath)
				return { chrome, url: `ws://127.0.0.1:${port}${browserPath}` };
		} catch {
			// not written yet
		}
		if (failure)
			throw new Error(`Chrome could not start (${executable}): ${failure.message}`);
		if (chrome.exitCode !== null)
			throw new Error(`Chrome exited with ${chrome.exitCode} before opening its DevTools port`);
		await sleep(100);
	}
	chrome.kill();
	throw new Error('Chrome did not open its DevTools port within 30 s');
}

export const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

/** Ends Chrome and waits until it has exited (at most a few seconds), so its profile can be removed. */
export async function stopChrome(chrome) {
	if (!chrome || chrome.exitCode !== null || chrome.signalCode !== null)
		return;
	const exited = new Promise(resolve => chrome.once('exit', resolve));
	chrome.kill();
	await Promise.race([exited, sleep(5000)]);
	if (chrome.exitCode === null && chrome.signalCode === null)
		chrome.kill('SIGKILL');
}

/** The value at fraction q (0..1) of an ascending list, nearest rank. */
export function percentile(sorted, q) {
	if (sorted.length === 0)
		return null;
	return sorted[Math.min(sorted.length - 1, Math.max(0, Math.ceil(q * sorted.length) - 1))];
}

/**
 * Statistics of the frames the page recorded ([start ms, Program.Frame wall time ms]) over a window of windowMs: the
 * frame rate, the percentiles and maximum of the frame time, the frames over 50 ms and the largest gap between frame starts.
 */
export function summarizeFrames(frames, windowMs) {
	const durations = frames.map(([, duration]) => duration).sort((a, b) => a - b);
	let largestGap = 0;
	for (let index = 1; index < frames.length; index++)
		largestGap = Math.max(largestGap, frames[index][0] - frames[index - 1][0]);
	const round = value => value === null ? null : Math.round(value * 100) / 100;
	return {
		frames: frames.length,
		fps: windowMs > 0 ? round(frames.length * 1000 / windowMs) : null,
		p50Ms: round(percentile(durations, 0.5)),
		p95Ms: round(percentile(durations, 0.95)),
		p99Ms: round(percentile(durations, 0.99)),
		maxMs: round(durations.at(-1) ?? null),
		totalMs: round(durations.reduce((sum, value) => sum + value, 0)),
		over50Ms: durations.filter(value => value >= 50).length,
		largestGapMs: round(largestGap),
	};
}

/**
 * Main-thread occupancy between two Performance.getMetrics results: the renderer main thread's task time over the wall time, in
 * percent. It is how busy the thread is (what postpones drawing and input), not the operating system's CPU time. Null when a
 * metric is missing or the interval is empty.
 */
export function mainThreadBusy(before, after) {
	const value = (metrics, name) => metrics?.metrics?.find(metric => metric.name === name)?.value;
	const [t0, t1, d0, d1] = [value(before, 'Timestamp'), value(after, 'Timestamp'), value(before, 'TaskDuration'), value(after, 'TaskDuration')];
	if ([t0, t1, d0, d1].some(item => typeof item !== 'number' || !Number.isFinite(item)) || t1 <= t0 || d1 < d0)
		return null;
	return Math.round((d1 - d0) / (t1 - t0) * 1000) / 10;
}

/** The median of a list of numbers (the mean of the middle two for an even count), or null for an empty list. */
export function median(values) {
	if (values.length === 0)
		return null;
	const sorted = [...values].sort((a, b) => a - b);
	const middle = Math.floor(sorted.length / 2);
	return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

/**
 * Combines the results of several runs field by field: numbers become their median, objects are combined recursively, and
 * anything else (texts, sample lists) is taken from the first run that has it. A field some runs lack uses the runs that have it.
 */
export function medianResults(runs) {
	const present = runs.filter(run => run !== undefined && run !== null);
	if (present.length === 0)
		return null;
	if (present.every(value => typeof value === 'number'))
		return Math.round(median(present) * 100) / 100;
	if (present.every(value => typeof value === 'object' && !Array.isArray(value))) {
		const combined = {};
		for (const key of new Set(present.flatMap(value => Object.keys(value))))
			combined[key] = medianResults(present.map(value => value[key]));
		return combined;
	}
	return present[0];
}

/** The measures two builds are compared on, as [name, value, unit] from one report's results (missing ones are left out). */
export function keyMetrics(results) {
	const rows = [];
	const add = (name, value, unit) => { if (Number.isFinite(value)) rows.push([name, value, unit]); };
	add('Start: first frame', results.start?.firstFrameMs, 'ms');
	add('Lobby: main thread busy', results.lobby?.mainThreadBusyPercent, '%');
	add('Lobby: frame p95', results.lobby?.p95Ms, 'ms');
	add('Jungle cold: largest frame', results.jungle?.cold?.maxFrameMs, 'ms');
	add('Jungle cold: ready', results.jungle?.cold?.readyMs, 'ms');
	add('Jungle: main thread busy', results.jungle?.steady?.mainThreadBusyPercent, '%');
	add('Jungle: frame p95', results.jungle?.steady?.p95Ms, 'ms');
	for (const [name, entry] of Object.entries(results.buy ?? {}))
		add(`${name === 'openMenu' ? 'Open buy menu' : name}: largest frame`, entry?.maxFrameMs, 'ms');
	add('Digest: texture decoding', results.digest?.textureMs, 'ms');
	add('Digest: fixed park run', results.digest?.simulationMs, 'ms');
	add('Wasm memory', results.jungle?.steady?.memory?.wasmBytes / 1048576, 'MB');
	return rows;
}
