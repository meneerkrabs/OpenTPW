// The live co-op acceptance's page sides (C2d-5b; docs/COOP.md, "The live acceptance"; tools/coop-live.mjs). A page side is the game in a page: a headless, muted Chrome of
// its own with a fresh profile, opened through a local server of its own in front of the real one (tools/web-perf/lib.mjs: it serves the game folder at /game/ and forwards
// the rest, the page from the server's web folder and /api/ with its sockets), so that server's log is that page's traffic. The driver starts the run once the page shows
// its front end, with the player's credentials as the call's arguments (never in the address or the page's stores), and then relays, one cycle at a time: the page's own
// status out to the shared evidence folder, the other game's status and the driver's in. It takes the page's pictures (the page cannot read its output back) and gives the
// inputs the run asks for through the browser's own events on the canvas. A page whose frames stop, that crashes, or whose run fails ends its side at once.
import { createHash } from 'node:crypto';
import { createWriteStream, promises as fs } from 'node:fs';
import path from 'node:path';
import { performance } from 'node:perf_hooks';
import { crc32, inflateSync } from 'node:zlib';
import { Cdp, launchChrome, listFiles, sleep, startServer, stopChrome } from './web-perf/lib.mjs';

/** How long a page may take to show its front end (its start copies the game's files from /game/), in seconds. */
export const PAGE_START_SECONDS = 300;
/** A page whose run's frames do not advance for this long fails, on the driver's own clock, in seconds. */
export const PAGE_STALL_SECONDS = 60;
/** The park's folder in the game folder, which a page copies when its park needs it (the host before its park starts, the guest for its join). */
export const PARK_FOLDER = '/game/data/levels/jungle/';
/** The park's level, as the game asks for its files. */
export const PARK_LEVEL = 'jungle';
/** How long a page that passed must go on drawing frames, with none failing, before its side counts as completed, in seconds. */
export const AFTER_PASS_SECONDS = 2;
/** A native module this large holds the assemblies compiled ahead of time; an interpreter build's holds the runtime only (about 3 MB). */
export const AOT_NATIVE_BYTES = 10 << 20;

const NAMED_KEYS = { Enter: ['Enter', 13], Escape: ['Escape', 27], F4: ['F4', 115], Backspace: ['Backspace', 8] };
const PUNCTUATION = { ' ': ['Space', 32], ',': ['Comma', 188], '.': ['Period', 190], '-': ['Minus', 189], '!': ['Digit1', 49, true], '?': ['Slash', 191, true] };

/** A key as the keyboard gives it to the page (DevTools' Input.dispatchKeyEvent): a named key, or one character with its text. */
export function keyEvent(key) {
	if (NAMED_KEYS[key])
		return { key, code: NAMED_KEYS[key][0], windowsVirtualKeyCode: NAMED_KEYS[key][1], modifiers: 0 };
	if ([...key].length !== 1)
		throw new Error(`The driver types named keys (${Object.keys(NAMED_KEYS).join(', ')}) or one character, not '${key}'`);
	if (/^[a-z]$/i.test(key))
		return { key, code: `Key${key.toUpperCase()}`, windowsVirtualKeyCode: key.toUpperCase().charCodeAt(0), text: key, modifiers: key === key.toUpperCase() ? 8 : 0 };
	if (/^[0-9]$/.test(key))
		return { key, code: `Digit${key}`, windowsVirtualKeyCode: key.charCodeAt(0), text: key, modifiers: 0 };
	if (PUNCTUATION[key]) {
		const [code, keyCode, shifted] = PUNCTUATION[key];
		return { key, code, windowsVirtualKeyCode: keyCode, text: key, modifiers: shifted ? 8 : 0 };
	}
	return { key, text: key, modifiers: 0 };
}

/** A point of the game's canvas (in its own units, `canvas` its size) on the page's canvas element, in the viewport's CSS pixels. */
export function pagePoint(point, canvas, element) {
	if (!Array.isArray(point) || point.length !== 2 || !Array.isArray(canvas) || !(canvas[0] > 0 && canvas[1] > 0))
		throw new Error(`The run asked for a point the driver cannot place: ${JSON.stringify({ point, canvas })}`);
	return { x: element.left + point[0] * element.width / canvas[0], y: element.top + point[1] * element.height / canvas[1] };
}

/** Whether `name` is a picture a page's run may ask for: its role's, a flat name. */
export function pictureName(role, name) {
	return typeof name === 'string' && new RegExp(`^${role}-[a-z0-9-]+\\.png$`).test(name);
}

const CHANNELS = { 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 };
const DEPTHS = { 0: [1, 2, 4, 8, 16], 2: [8, 16], 3: [1, 2, 4, 8], 4: [8, 16], 6: [8, 16] };

/**
 * A PNG's width and height once the whole image decodes: every chunk whole with its checksum, the header first (a bit depth its colour type allows, the standard compression
 * and filtering) and the end last, a palette before the data of a palette image, and the image data inflating to exactly the rows the header promises (non-interlaced),
 * each row's filter one of the five. Null for anything else.
 */
export function pngSize(bytes) {
	const signature = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
	if (!Buffer.isBuffer(bytes) || bytes.length < 8 || !bytes.subarray(0, 8).equals(signature))
		return null;
	let offset = 8, header = null, ended = false, palette = false;
	const data = [];
	while (offset + 12 <= bytes.length && !ended) {
		const length = bytes.readUInt32BE(offset);
		const type = bytes.toString('latin1', offset + 4, offset + 8);
		if (offset + 12 + length > bytes.length || crc32(bytes.subarray(offset + 4, offset + 8 + length)) !== bytes.readUInt32BE(offset + 8 + length))
			return null;
		const body = bytes.subarray(offset + 8, offset + 8 + length);
		if ((header === null) !== (type === 'IHDR'))
			return null;
		if (type === 'IHDR') {
			if (body.length !== 13)
				return null;
			header = { width: body.readUInt32BE(0), height: body.readUInt32BE(4), depth: body[8], color: body[9], compression: body[10], filter: body[11], interlace: body[12] };
		} else if (type === 'PLTE')
			palette = true;
		else if (type === 'IDAT') {
			if (header.color === 3 && !palette)
				return null;
			data.push(body);
		}
		else if (type === 'IEND')
			ended = true;
		offset += 12 + length;
	}
	if (!header || !ended || header.interlace !== 0 || header.compression !== 0 || header.filter !== 0 || !(header.width > 0 && header.height > 0)
		|| !DEPTHS[header.color]?.includes(header.depth))
		return null;
	try {
		const rowBytes = Math.ceil(header.width * CHANNELS[header.color] * header.depth / 8);
		const rows = inflateSync(Buffer.concat(data));
		if (rows.length !== header.height * (1 + rowBytes))
			return null;
		for (let row = 0; row < header.height; row++)
			if (rows[row * (1 + rowBytes)] > 4)
				return null;
		return { width: header.width, height: header.height };
	} catch {
		return null;
	}
}

const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');

/** Writes a file whole, then renames it into place, as the native games write theirs. */
async function writeAtomic(file, contents) {
	await fs.writeFile(`${file}.tmp`, contents);
	await fs.rename(`${file}.tmp`, file);
}

/**
 * The page the local server gives is the build under test: its index page and its script are the web build's own bytes, and its policy lets its sockets reach its own
 * origin (the server's policy, forwarded with the page).
 */
export async function checkBuild(origin, web) {
	const build = { files: {} };
	// every file of the page that no integrity check covers (its page, scripts and styles), and the runtime's loader, whose embedded list holds every framework file's
	// hash, which the runtime checks as it loads each
	const entries = await fs.readdir(web, { withFileTypes: true });
	const files = [...entries.filter(entry => entry.isFile() && /\.(html|js|mjs|css)$/.test(entry.name)).map(entry => entry.name), '_framework/dotnet.js'];
	for (const file of files) {
		const response = await fetch(`${origin}/${file}`, { signal: AbortSignal.timeout(30000) });
		const served = Buffer.from(await response.arrayBuffer());
		const built = await fs.readFile(path.join(web, file));
		if (response.status !== 200 || !served.equals(built))
			throw new Error(`The page's ${file} through the local server (${response.status}) is not the web build's (${web})`);
		build.files[file] = sha256(built);
		if (file === 'index.html')
			build.policy = response.headers.get('content-security-policy');
	}
	if (!/connect-src[^;]*'self'/.test(build.policy ?? ''))
		throw new Error(`The page's policy does not let its sockets reach its own origin: ${build.policy ?? 'none'}`);
	// the native module the loader names, and the runtime its size shows
	const loader = await fs.readFile(path.join(web, '_framework', 'dotnet.js'), 'utf8');
	const native = /"wasmNative":\s*\[\s*\{\s*"name":\s*"([^"]+)"/.exec(loader)?.[1] ?? null;
	build.nativeWasm = native;
	build.nativeWasmBytes = native ? (await fs.stat(path.join(web, '_framework', native))).size : null;
	build.measuredRuntime = build.nativeWasmBytes === null ? 'unknown' : build.nativeWasmBytes >= AOT_NATIVE_BYTES ? 'aot' : 'interpreter';
	return build;
}

export class PageSide {
	/**
	 * @param role `host` or `guest`.
	 * @param options the driver's: web, chrome, gamePath, scenario, minutes, headed.
	 * @param out the run's folder; the page's own folder (its Chrome profile) is `<out>/<role>`, its console `<out>/<role>.log`.
	 * @param evidence the shared evidence folder.
	 * @param player its throwaway player; server the real server (its origin); redact replaces the secrets in what is logged.
	 */
	constructor({ role, options, out, evidence, player, server, redact }) {
		Object.assign(this, { role, options, out, evidence, player, server, redact });
		this.kind = 'page';
		this.other = role === 'host' ? 'guest' : 'host';
		this.folder = path.join(out, role);
		this.relayed = new Map();
		this.state = null;
		this.crashed = null;
		this.outcome = null;
		this.started = false;
		this.relaying = false;
		this.frames = -1;
		this.framesAt = performance.now();
		this.network = [];
		this.logged = Promise.resolve();
	}

	/** Starts the page and, once it shows its front end, the run (the guest with the host's code); a side stopped meanwhile ends what it had started. */
	async start(code) {
		try {
			await this.#start(code);
		} catch (error) {
			if (this.stopped)
				await this.stop();
			throw error;
		}
	}

	async #start(code) {
		await fs.mkdir(this.folder, { recursive: true });
		const log = this.logStream = createWriteStream(path.join(this.out, `${this.role}.log`), { flags: 'a' });
		this.logged = new Promise((resolve, reject) => {
			log.once('finish', resolve);
			log.once('error', reject);
		});
		this.logged.catch(() => {});
		this.local = await startServer(path.resolve(this.options.web), path.resolve(this.options.gamePath), this.server.origin);
		this.#unlessStopped();
		this.build = await checkBuild(this.local.origin, path.resolve(this.options.web));
		this.build.runtime = this.options.runtime;
		if (this.options.runtime !== 'unknown' && this.options.runtime !== this.build.measuredRuntime)
			throw new Error(`The web build is an ${this.build.measuredRuntime} build (its native module is ${this.build.nativeWasmBytes} bytes), not the ${this.options.runtime} one given`);
		// the park's files in the game folder, which a page copies whole for its park
		this.parkFiles = (await listFiles(path.resolve(this.options.gamePath))).filter(file => `/game/${file}`.toLowerCase().startsWith(PARK_FOLDER)).length;
		this.profile = path.join(this.folder, 'profile');
		await fs.mkdir(this.profile);
		this.#unlessStopped();
		const { chrome, url } = await launchChrome(this.options.chrome, this.profile, { headed: this.options.headed });
		this.pictureSizes = {};
		this.chrome = chrome;
		chrome.once('exit', (exitCode, signal) => this.crashed ??= `Chrome exited (${exitCode ?? signal})`);
		// the run stopped while Chrome started: it ends with it
		if (this.stopped) {
			await stopChrome(chrome);
			this.#unlessStopped();
		}
		this.cdp = await Cdp.connect(url, 30000);
		this.chromeVersion = (await this.cdp.send('Browser.getVersion')).product;
		const { targetId } = await this.cdp.send('Target.createTarget', { url: 'about:blank' });
		const { sessionId } = await this.cdp.send('Target.attachToTarget', { targetId, flatten: true });
		this.sessionId = sessionId;
		this.cdp.on(message => this.#event(message, targetId));
		for (const domain of ['Runtime', 'Page', 'Network', 'Inspector'])
			await this.cdp.send(`${domain}.enable`, {}, sessionId);
		await this.cdp.send('Page.navigate', { url: `${this.local.origin}/index.html?coopLive=1&data=/game/&keep=off` }, sessionId);
		await this.#until(() => this.eval('Boolean(globalThis.opentpwLive)'), 'its live mode');
		await this.#until(async () => JSON.parse(await this.call('CoopLiveState')).frontEnd, 'its front end');
		// the pointer rests over the middle of the canvas, as a player's does: a page that never saw it stands at the corner, whose edge band scrolls the park's view
		const element = await this.#canvas();
		await this.cdp.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: element.left + element.width / 2, y: element.top + element.height / 2 }, this.sessionId, 30000);
		await this.call('CoopLiveStart', this.role === 'host' ? 'host' : 'join', this.player.name, this.player.password, this.options.scenario,
			String(this.options.minutes), code ?? '');
		this.started = true;
		this.framesAt = performance.now();
	}

	async #until(condition, what) {
		const begin = performance.now();
		for (;;) {
			this.#alive();
			if (await condition().catch(() => false))
				return;
			if (performance.now() - begin > PAGE_START_SECONDS * 1000)
				throw new Error(`The ${this.role} page did not show ${what} within ${PAGE_START_SECONDS} s`);
			await sleep(250);
		}
	}

	#alive() {
		this.#unlessStopped();
		if (this.crashed)
			throw new Error(`The ${this.role} page crashed: ${this.crashed}`);
	}

	#unlessStopped() {
		if (this.stopped)
			throw new Error(`The ${this.role} page was stopped`);
	}

	#event(message, targetId) {
		if (message.method === 'Target.targetCrashed' && message.params?.targetId === targetId)
			this.crashed ??= `its renderer ended (${message.params.status}, ${message.params.errorCode})`;
		if (message.method === 'Target.detachedFromTarget' && message.params?.sessionId === this.sessionId)
			this.crashed ??= 'it was detached';
		if (message.sessionId !== this.sessionId)
			return;
		const params = message.params ?? {};
		if (message.method === 'Inspector.targetCrashed')
			this.crashed ??= 'its renderer crashed';
		else if (message.method === 'Runtime.consoleAPICalled')
			this.#log(`${params.type}: ${(params.args ?? []).map(arg => arg.value ?? arg.description ?? '').join(' ')}`);
		else if (message.method === 'Runtime.exceptionThrown')
			this.#log(`exception: ${params.exceptionDetails?.exception?.description ?? params.exceptionDetails?.text}`);
		else if (message.method === 'Network.responseReceived') {
			// what the page fetched of the park's folder and the API, bounded: the game's start copies many more files
			const { pathname } = new URL(params.response?.url ?? 'about:blank');
			const folder = decodeURIComponent(pathname).toLowerCase();
			if ((folder.startsWith('/game/data/levels/') || folder.startsWith('/api/') || folder.startsWith('/_framework/dotnet.native')) && this.network.length < 20000)
				this.network.push({ path: decodeURIComponent(pathname), status: params.response.status, at: Date.now() });
		}
	}

	#log(line) {
		if (this.logStream && !this.logStream.writableEnded)
			this.logStream.write(this.redact(line) + '\n');
	}

	async eval(expression) {
		const result = await this.cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }, this.sessionId, 30000);
		if (result.exceptionDetails)
			throw new Error(`${this.role}: ${expression.slice(0, 80)}: ${result.exceptionDetails.exception?.description ?? result.exceptionDetails.text}`);
		return result.result.value;
	}

	/** One of the page's exports with its arguments: the name and the arguments travel as the protocol's arguments of one fixed function, never inside code the page compiles. */
	async call(name, ...args) {
		const { result: global } = await this.cdp.send('Runtime.evaluate', { expression: 'globalThis', objectGroup: 'opentpw-live' }, this.sessionId);
		try {
			const result = await this.cdp.send('Runtime.callFunctionOn', {
				functionDeclaration: '(name, args) => opentpwLive.program[name](...args)',
				objectId: global.objectId,
				arguments: [{ value: name }, { value: args }],
				returnByValue: true, awaitPromise: true,
			}, this.sessionId, 30000);
			if (result.exceptionDetails)
				throw new Error(`${this.role}: ${name}: ${this.redact(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text)}`);
			return result.result.value ?? null;
		} finally {
			await this.cdp.send('Runtime.releaseObjectGroup', { objectGroup: 'opentpw-live' }, this.sessionId).catch(() => {});
		}
	}

	/**
	 * One relay cycle, never two at once: the run's state (a run whose frames stopped fails), the page's own status out, the other game's and the driver's in, then the picture
	 * and the input the run asked for; once the run has ended, its evidence out. `driver` is the driver's status, with its `publish`. Returns the page's status, or null.
	 */
	async relay(driver) {
		if (!this.started || this.relaying || (this.outcome && this.outcome !== 'completed'))
			return null;
		this.relaying = true;
		try {
			this.#alive();
			const state = this.state = JSON.parse(await this.call('CoopLiveState'));
			// the page's own frames, during its run and after it to the run's end: a page whose frames stop fails, as does one whose frame failed
			if (state.frames > this.frames)
				[this.frames, this.framesAt] = [state.frames, performance.now()];
			else if (performance.now() - this.framesAt > PAGE_STALL_SECONDS * 1000)
				throw new Error(`The ${this.role} page's frames stopped at ${state.frames} for ${PAGE_STALL_SECONDS} s (at ${state.run?.step ?? 'its start'})`);
			if (state.frameError) {
				if (this.outcome !== 'completed')
					await this.#collect();
				this.outcome = `failed: its frame failed: ${state.frameError}`;
				return null;
			}
			// a completed page is only watched from here on
			if (this.outcome === 'completed')
				return null;
			const own = `${this.role}-status.json`;
			const text = await this.call('CoopLiveRead', own);
			if (text !== null && text !== this.relayed.get(own)) {
				await writeAtomic(path.join(this.evidence, own), text);
				this.relayed.set(own, text);
			}
			for (const name of [`${this.other}-status.json`, 'driver-status.json']) {
				const given = await fs.readFile(path.join(this.evidence, name), 'utf8').catch(() => null);
				if (given !== null && given !== this.relayed.get(name)) {
					await this.call('CoopLiveWrite', name, given);
					this.relayed.set(name, given);
				}
			}
			const status = text === null ? null : JSON.parse(text);
			if (status?.picture && !driver.pictures.includes(status.picture))
				await this.#picture(status.picture, driver);
			if (Number.isInteger(status?.input?.id) && status.input.id > (driver.inputs[this.role] ?? 0)) {
				await this.#input(status.input);
				driver.inputs[this.role] = status.input.id;
				await driver.publish();
			}
			if (state.state === 'failed') {
				await this.#collect();
				this.outcome = `failed: ${state.message}`;
			} else if (state.state === 'passed') {
				// a pass counts once the page has gone on drawing frames for a while, with none failing
				this.passed ??= { at: performance.now(), frames: state.frames };
				if (performance.now() - this.passed.at >= AFTER_PASS_SECONDS * 1000 && state.frames > this.passed.frames)
					this.outcome = await this.#collect() ? 'completed' : 'failed: its evidence is missing';
			}
			return status;
		} finally {
			this.relaying = false;
		}
	}

	/** Copies the run's evidence out of the page; false when it has none. */
	async #collect() {
		const evidence = await this.call('CoopLiveRead', `${this.role}-evidence.json`);
		if (evidence !== null)
			await writeAtomic(path.join(this.evidence, `${this.role}-evidence.json`), evidence);
		return evidence !== null;
	}

	/** Reads the page's state once more at the run's end (its frames went on, and none failed); a page that cannot answer, or crashed, fails. */
	async refresh() {
		if (!this.started)
			return;
		try {
			this.#alive();
			this.state = JSON.parse(await this.call('CoopLiveState'));
		} catch (error) {
			this.state = { ...this.state, frameError: this.state?.frameError ?? `the page could not answer at the run's end: ${error.message}` };
		}
	}

	/** The canvas's picture while the run's view stays, kept as the run named it; the driver's status says it is taken. */
	async #picture(name, driver) {
		if (!pictureName(this.role, name))
			throw new Error(`The ${this.role} page asked for a picture named '${name}'`);
		const element = await this.#canvas();
		const { data } = await this.cdp.send('Page.captureScreenshot', { format: 'png', clip: { x: element.left, y: element.top, width: element.width, height: element.height, scale: 1 } },
			this.sessionId, 30000);
		await fs.writeFile(path.join(this.evidence, name), Buffer.from(data, 'base64'));
		// the screenshot's size is the canvas element's in device pixels
		this.pictureSizes[name] = [Math.round(element.width * element.ratio), Math.round(element.height * element.ratio)];
		driver.pictures.push(name);
		await driver.publish();
	}

	/** The canvas element's content box in the viewport's CSS pixels. */
	#canvas() {
		return this.eval(`(() => { const canvas = document.getElementById('canvas'); const box = canvas.getBoundingClientRect();
			return { left: box.left + canvas.clientLeft, top: box.top + canvas.clientTop, width: canvas.clientWidth, height: canvas.clientHeight,
				ratio: window.devicePixelRatio || 1 }; })()`);
	}

	/** Gives the run's input through the browser's own events on the canvas: keys pressed and released, a line typed, the pointer moved or pressed and released. */
	async #input(input) {
		const send = (method, params) => this.cdp.send(method, params, this.sessionId, 30000);
		await this.eval(`document.getElementById('canvas').focus()`);
		for (const key of [...(input.keys ?? []), ...[...(input.text ?? '')]]) {
			const event = keyEvent(key);
			await send('Input.dispatchKeyEvent', { type: 'keyDown', ...event });
			await send('Input.dispatchKeyEvent', { type: 'keyUp', ...event, text: undefined });
		}
		const element = (input.move || input.click) ? await this.#canvas() : null;
		if (input.move) {
			const { x, y } = pagePoint(input.move, input.canvas, element);
			await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
		}
		if (input.click) {
			const { x, y } = pagePoint(input.click, input.canvas, element);
			await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
			await send('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', buttons: 1, clickCount: 1 });
			await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', buttons: 0, clickCount: 1 });
		}
	}

	/** How the side ended, in words, or null while it runs. */
	ending() {
		// a crash counts over a completed run: the page must live to the run's end
		if (this.crashed && !this.stopped)
			return `crashed: ${this.crashed}`;
		return this.outcome;
	}

	/** The side's facts for the run's record: the build, Chrome, and the page's traffic through its local server. */
	facts() {
		const log = this.local?.log ?? [];
		return {
			kind: 'page', chrome: this.chromeVersion ?? null, build: this.build ?? null, origin: this.local?.origin ?? null,
			upgrades: log.filter(entry => entry.upgrade), requests: log.filter(entry => !entry.upgrade && entry.path.startsWith('/api/')), network: this.network,
			startedAt: this.state?.startedAt ?? null, levels: this.state?.levels ?? [], frames: this.state?.frames ?? null, frameError: this.state?.frameError ?? null,
			framesAtPass: this.passed?.frames ?? null,
			parkFiles: this.parkFiles ?? null, pictureSizes: this.pictureSizes ?? {},
		};
	}

	/** Ends the page: its Chrome, its local server, its log; true once Chrome has exited. */
	async stop() {
		this.stopped = true;
		try {
			this.cdp?.close();
		} catch {
			// already closed
		}
		await stopChrome(this.chrome);
		await this.local?.close();
		if (this.logStream && !this.logStream.writableEnded)
			this.logStream.end();
		return !this.chrome || this.chrome.exitCode !== null || this.chrome.signalCode !== null;
	}
}

/**
 * The page sides' own checks beside the games' evidence (docs/COOP.md, "The live acceptance"): each page's co-op and chat sockets went through its local server with bytes
 * both ways (and, after a restart, came back through it), and it fetched the park's files from the game folder. `restartedAt` is the restart's time (ms), or null.
 */
export function checkPageSides(sides, restartedAt) {
	const checks = [];
	for (const [role, facts] of Object.entries(sides)) {
		if (facts?.kind !== 'page')
			continue;
		const carried = (route, after = -Infinity) => (facts.upgrades ?? []).filter(entry => entry.path === route && entry.status === 101 && entry.bytesIn > 0
			&& entry.bytesOut > 0 && entry.at > after).length;
		const login = (facts.requests ?? []).filter(entry => entry.path === '/api/v1/sessions' && entry.method === 'POST' && entry.status === 200 && entry.bytes > 0);
		checks.push({ what: `the ${role} page logged in through its local server`, passed: login.length > 0, detail: { logins: login } });
		const native = facts.build?.nativeWasm ? (facts.network ?? []).filter(item => item.path === `/_framework/${facts.build.nativeWasm}` && item.status === 200) : [];
		checks.push({ what: `the ${role} page ran the web build given, its ${facts.build?.measuredRuntime ?? 'unknown'} runtime loaded, and no frame of it failed`,
			passed: Boolean(facts.build?.files?.['index.html']) && native.length > 0 && facts.frameError === null && facts.frames > facts.framesAtPass,
			detail: { build: facts.build, nativeLoaded: native.length, frames: facts.frames, framesAtPass: facts.framesAtPass, frameError: facts.frameError } });
		checks.push({ what: `the ${role} page's co-op and chat sockets went through its local server, with bytes both ways`,
			passed: carried('/api/v1/coop') > 0 && carried('/api/v1/chat') > 0, detail: { upgrades: facts.upgrades } });
		if (restartedAt !== null)
			checks.push({ what: `the ${role} page's sockets came back through its local server after the restart`,
				passed: carried('/api/v1/coop', restartedAt) > 0 && carried('/api/v1/chat', restartedAt) > 0, detail: { restartedAt, upgrades: facts.upgrades } });
		// the park's files asked for during the run (none were there at its start), all of them fetched, and the copy whole
		const copies = (facts.levels ?? []).filter(copy => copy.level?.toLowerCase() === PARK_LEVEL);
		const park = new Set((facts.network ?? []).filter(item => item.path.toLowerCase().startsWith(PARK_FOLDER) && item.status === 200).map(item => item.path.toLowerCase()));
		checks.push({ what: `the ${role} page fetched the park's files from the game folder during the run, whole`,
			passed: copies.length > 0 && copies.every(copy => copy.requestedAt >= facts.startedAt && Number.isFinite(copy.loadedAt) && copy.failure === null)
				&& facts.parkFiles > 0 && park.size === facts.parkFiles,
			detail: { copies, startedAt: facts.startedAt, fetched: park.size, files: facts.parkFiles } });
	}
	return checks;
}

/**
 * Every sighting's picture is in the evidence folder and decodes as a PNG of the size expected: a native game's as it captured it (its sighting says), a page's as the
 * driver took it (`pageSizes`, by name). `read` reads a file of the folder, null when absent.
 */
export async function checkPictures(evidences, read, pageSizes = {}) {
	const pictures = [];
	for (const [role, evidence] of Object.entries(evidences))
		for (const sighting of evidence?.sightings ?? []) {
			const bytes = await read(sighting.picture);
			const size = bytes ? pngSize(bytes) : null;
			const expected = sighting.pictureSize ?? pageSizes[sighting.picture] ?? null;
			pictures.push({ role, picture: sighting.picture, size, expected });
		}
	return { what: 'every sighting\'s picture was kept, a whole PNG of the view at its size', passed: pictures.length > 0
		&& pictures.every(item => Array.isArray(item.expected) && item.size?.width === item.expected[0] && item.size?.height === item.expected[1]), detail: pictures };
}
