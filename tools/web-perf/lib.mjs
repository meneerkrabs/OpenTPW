// Building blocks of the browser performance harness (run.mjs, docs/BROWSER-PERFORMANCE.md): a local server for the
// published page and the player's own game folder, a minimal Chrome DevTools Protocol client, and frame statistics.
// No dependencies: Node's own http, fs, child_process, fetch and WebSocket.
import { createServer, request as httpRequest } from 'node:http';
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
 *
 * With `upstream` (a base URL such as http://127.0.0.1:8080) every request but /game/ is forwarded unchanged to that server, the
 * OpenTPW.Server that serves the page in production (docs/SERVER.md), and answered with its own headers and body: the browser then
 * sees the production transport (Brotli siblings, ETag, Cache-Control). The forwarded requests are logged in `log`
 * (path, status, Content-Encoding, Cache-Control, ETag, whether the request was conditional, body bytes as sent), and `marks`
 * records the log length where each scenario started. A WebSocket upgrade below /api/ (the chat and co-op sockets) is forwarded to
 * the upstream too, at most UPGRADE_LIMIT at a time, each with UPGRADE_ANSWER_MS for the upstream's answer, and its two ends then
 * joined as they are; it is logged with its status and the bytes each way. Any other upgrade is refused.
 */
export async function startServer(webRoot, gameRoot, upstream = null) {
	const log = [], marks = [];
	// The upstream is the local OpenTPW.Server only: an http origin on a loopback address, fixed when the harness starts.
	const upstreamUrl = upstream == null ? null : new URL(loopbackOrigin(upstream));
	const forward = (request, response, url) => {
		// Host and port are the upstream's, fixed at start; only the request's path and query are passed on, so an absolute-form
		// request target (http://other/...) cannot pick another host.
		const target = { hostname: upstreamUrl.hostname.replace(/^\[|\]$/g, ''), port: upstreamUrl.port || 80, path: url.pathname + url.search, host: upstreamUrl.host };
		const entry = { path: url.pathname, method: request.method, conditional: Boolean(request.headers['if-none-match'] || request.headers['if-modified-since']), acceptEncoding: request.headers['accept-encoding'] ?? '', bytes: 0 };
		log.push(entry);
		const outgoing = httpRequest({ hostname: target.hostname, port: target.port, path: target.path, method: request.method, headers: { ...request.headers, host: target.host } }, incoming => {
			entry.status = incoming.statusCode;
			entry.encoding = incoming.headers['content-encoding'] ?? null;
			entry.cacheControl = incoming.headers['cache-control'] ?? null;
			entry.etag = incoming.headers.etag ?? null;
			entry.type = incoming.headers['content-type'] ?? null;
			entry.length = incoming.headers['content-length'] ? Number(incoming.headers['content-length']) : null;
			response.writeHead(incoming.statusCode, incoming.headers);
			incoming.on('data', chunk => entry.bytes += chunk.length);
			incoming.pipe(response);
		});
		outgoing.on('error', () => { entry.status ??= 502; response.writeHead(502).end(); });
		request.pipe(outgoing);
	};
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
		if (upstream && !url.pathname.startsWith('/game/'))
			return forward(request, response, url);
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
	const sockets = new Set();
	server.on('upgrade', (request, socket, head) => forwardUpgrade(request, socket, head, upstreamUrl, log, sockets));
	await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
	const close = () => {
		// the forwarded sockets are the server's no longer once upgraded: they are ended here, or close would wait for them
		for (const socket of sockets)
			socket.destroy();
		return new Promise(resolve => server.close(resolve));
	};
	return { server, log, marks, origin: `http://127.0.0.1:${server.address().port}`, close };
}

/** The most WebSocket upgrades the local server forwards at a time (a page's chat and co-op sockets, a reconnecting one beside them). */
export const UPGRADE_LIMIT = 16;
/** How long the upstream may take to answer an upgrade, in milliseconds. */
export const UPGRADE_ANSWER_MS = 10000;
/** How long a forwarded socket may stay half closed after one side ended, in milliseconds. */
export const UPGRADE_END_MS = 5000;

/** A plain HTTP answer on a socket that asked for an upgrade, which then closes. */
function refuseUpgrade(socket, status, reason) {
	socket.end(`HTTP/1.1 ${status} ${reason}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n`);
}

/**
 * Forwards a WebSocket upgrade below /api/ to the upstream (startServer): the request's path and query and its headers, with the upstream's host, as for a forwarded
 * request. The upstream's 101 goes back with its headers, and the two sockets are joined; any other answer goes back as it is and the connection closes. Either end
 * closing or failing closes both.
 */
function forwardUpgrade(request, socket, head, upstreamUrl, log, sockets) {
	const url = new URL(request.url, 'http://localhost');
	if (!upstreamUrl || !url.pathname.startsWith('/api/'))
		return refuseUpgrade(socket, 404, 'Not Found');
	if (sockets.size >= UPGRADE_LIMIT)
		return refuseUpgrade(socket, 503, 'Service Unavailable');
	const entry = { path: url.pathname, method: request.method, upgrade: true, status: null, bytesIn: 0, bytesOut: 0 };
	log.push(entry);
	sockets.add(socket);
	let upstreamSocket = null;
	const close = () => {
		sockets.delete(socket);
		socket.destroy();
		upstreamSocket?.destroy();
		outgoing.destroy();
	};
	socket.on('error', close);
	socket.on('close', close);
	// a connection of its own (no agent's pool): it is the socket's for as long as the socket lives, and closes with it
	const outgoing = httpRequest({ hostname: upstreamUrl.hostname.replace(/^\[|\]$/g, ''), port: upstreamUrl.port || 80, path: url.pathname + url.search,
		method: request.method, headers: { ...request.headers, host: upstreamUrl.host }, agent: false });
	const timer = setTimeout(() => {
		entry.status = 504;
		refuseUpgrade(socket, 504, 'Gateway Timeout');
		close();
	}, UPGRADE_ANSWER_MS);
	outgoing.on('upgrade', (response, upstream, upstreamHead) => {
		clearTimeout(timer);
		upstreamSocket = upstream;
		entry.status = response.statusCode;
		const lines = [`HTTP/1.1 ${response.statusCode} ${response.statusMessage}`];
		for (let index = 0; index < response.rawHeaders.length; index += 2)
			lines.push(`${response.rawHeaders[index]}: ${response.rawHeaders[index + 1]}`);
		socket.write(lines.join('\r\n') + '\r\n\r\n');
		if (upstreamHead?.length)
			socket.write(upstreamHead);
		if (head?.length)
			upstream.write(head);
		upstream.on('data', chunk => entry.bytesOut += chunk.length);
		socket.on('data', chunk => entry.bytesIn += chunk.length);
		upstream.on('error', close);
		upstream.on('close', close);
		// an end on one side ends the other once its data is out (the pipes); a peer that never ends its own side is closed after UPGRADE_END_MS
		for (const side of [socket, upstream])
			side.once('end', () => setTimeout(close, UPGRADE_END_MS).unref());
		socket.pipe(upstream);
		upstream.pipe(socket);
	});
	outgoing.on('response', response => {
		// the upstream would not upgrade: its answer goes back as it is
		clearTimeout(timer);
		entry.status = response.statusCode;
		const lines = [`HTTP/1.1 ${response.statusCode} ${response.statusMessage}`, 'Connection: close'];
		for (let index = 0; index < response.rawHeaders.length; index += 2)
			if (!/^(connection|transfer-encoding)$/i.test(response.rawHeaders[index]))
				lines.push(`${response.rawHeaders[index]}: ${response.rawHeaders[index + 1]}`);
		const body = [];
		response.on('data', chunk => body.push(chunk));
		response.on('end', () => {
			const bytes = Buffer.concat(body);
			if (!response.headers['content-length'])
				lines.push(`Content-Length: ${bytes.length}`);
			socket.end(Buffer.concat([Buffer.from(lines.join('\r\n') + '\r\n\r\n'), bytes]));
			sockets.delete(socket);
			outgoing.destroy();
		});
	});
	outgoing.on('error', () => {
		clearTimeout(timer);
		entry.status ??= 502;
		refuseUpgrade(socket, 502, 'Bad Gateway');
		close();
	});
	outgoing.end();
}

/** The origin of `value` if it is an http URL on a loopback address (127.0.0.0/8, ::1 or localhost); throws otherwise. */
export function loopbackOrigin(value) {
	const url = new URL(value);
	const host = url.hostname.replace(/^\[|\]$/g, '');
	if (url.protocol !== 'http:' || !(host === 'localhost' || host === '::1' || /^127(\.\d{1,3}){3}$/.test(host)))
		throw new Error(`--upstream must be an http URL on a loopback address, not ${value}`);
	return url.origin;
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

/** Chrome's command line for a fresh profile: headless and muted unless asked otherwise. */
export function chromeArguments(profile, { headed = false, audible = false, width = 1280, height = 900 } = {}) {
	const args = [
		`--user-data-dir=${profile}`, '--remote-debugging-port=0', '--no-first-run', '--no-default-browser-check',
		'--autoplay-policy=no-user-gesture-required', `--window-size=${width},${height}`, '--disable-background-timer-throttling',
		'--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows', 'about:blank',
	];
	// Muting only silences the speakers; Web Audio still decodes, so --music timings are unchanged.
	if (!audible)
		args.push('--mute-audio');
	if (!headed)
		args.unshift('--headless=new');
	return args;
}

/** Starts Chrome with a fresh profile and its DevTools port; resolves with the process and the browser WebSocket URL. */
export async function launchChrome(executable, profile, options = {}) {
	const args = chromeArguments(profile, options);
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
 * frame rate, the percentiles and maximum of the frame time, the frames over 50 ms (their count, and their times in order) and the
 * largest gap between frame starts.
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
		over33Ms: durations.filter(value => value > 33).length,
		over50Ms: durations.filter(value => value >= 50).length,
		longFramesMs: frames.map(([, duration]) => duration).filter(value => value >= 50).map(round),
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
	add('Jungle cold: frames over 33 ms', results.jungle?.cold?.over33Ms, '');
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
