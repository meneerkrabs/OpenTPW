#!/usr/bin/env node
// The co-op join smoke's join phase in the browser (docs/COOP.md, "The browser's join"). Serves a published web build and the player's own game folder on
// 127.0.0.1 (tools/web-perf/lib.mjs), opens the page in a fresh headless, muted Chrome profile with ?coopSmoke=1, copies a join the native smoke recorded on
// the desktop into the page, runs the join phase (join, the adversarial one, or measure) and writes each run's evidence for tools/coop_join_timings.py.
// One run a page and a profile: the builds the smoke checks must be the runtime's first. The game files and the recorded join stay on this machine.
//
// The recorded join comes from the native smoke's host phase:
//   OPENTPW_COOP_JOIN_PHASE=host OPENTPW_SMOKE_LOG_DIR=<logs> scripts/smoke-coop-join.sh   (its evidence: <logs>/opentpw-coop-join-evidence)
//
// Usage:
//   node tools/web-coop-join.mjs --web out/web/wwwroot --game "/path/to/Theme Park World" --evidence <host evidence directory>
//        [--mode join|measure] [--runs N] [--out <directory>] [--label <name>] [--chrome <executable>] [--headed] [--timeout-minutes M]
import { promises as fs } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { Cdp, launchChrome, sleep, startServer, stopChrome } from './web-perf/lib.mjs';

const DEFAULT_CHROME = {
	darwin: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
	linux: '/usr/bin/google-chrome',
	win32: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
}[process.platform];

// The files of a recorded join the join phase reads (the captures besides, by the names the host evidence gives)
const FIXED_FILES = ['host-evidence.json', 'frames.bin', 'host-final-state.bin', 'host-final-save.json'];
// The same rule as CoopJoinSmokeTest.IsEvidenceFileName: a flat name, no Windows device name, no trailing dot
const DEVICE = /^(con|prn|aux|nul|com[0-9]|lpt[0-9])$/i;
const isEvidenceFileName = name => typeof name === 'string' && /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/.test(name) && !name.endsWith('.') && !DEVICE.test(name.split('.')[0]);

function parseArguments(argv) {
	const options = { web: null, game: null, evidence: null, mode: 'join', runs: 1, out: null, label: 'unlabelled', chrome: process.env.CHROME ?? DEFAULT_CHROME, headed: false, timeoutMinutes: 25, width: 1280, height: 900 };
	for (let index = 2; index < argv.length; index++) {
		const [flag, inline] = argv[index].split(/=(.*)/s);
		const value = () => inline ?? argv[++index];
		switch (flag) {
			case '--web': options.web = value(); break;
			case '--game': options.game = value(); break;
			case '--evidence': options.evidence = value(); break;
			case '--mode': options.mode = value(); break;
			case '--runs': options.runs = Math.max(1, Math.trunc(Number(value())) || 1); break;
			case '--out': options.out = value(); break;
			case '--label': options.label = value(); break;
			case '--chrome': options.chrome = value(); break;
			case '--headed': options.headed = true; break;
			case '--timeout-minutes': options.timeoutMinutes = Math.max(1, Number(value()) || 25); break;
			case '--size': [options.width, options.height] = value().split('x').map(Number); break;
			default: throw new Error(`Unknown option ${flag}`);
		}
	}
	if (!options.web || !options.game || !options.evidence)
		throw new Error('Pass --web <published wwwroot>, --game <Theme Park World folder> and --evidence <the host phase\'s evidence directory>.');
	if (!['join', 'measure'].includes(options.mode))
		throw new Error(`--mode is 'join' or 'measure', not '${options.mode}'.`);
	if (!/^[A-Za-z0-9._-]+$/.test(options.label))
		throw new Error('--label is letters, digits, dots, dashes and underscores.');
	return options;
}

/** The recorded join's files, read and checked: the host phase completed, and every file it names is a flat name present in the directory. */
async function readEvidence(directory) {
	const host = JSON.parse(await fs.readFile(path.join(directory, 'host-evidence.json'), 'utf8'));
	if (!host.Completed || host.Failure || !Array.isArray(host.Cuts) || host.Cuts.length !== 2)
		throw new Error(`${directory}: the host phase did not complete with two captures`);
	const names = [...FIXED_FILES, ...host.Cuts.map(cut => cut.File)];
	const files = [];
	for (const name of names) {
		if (!isEvidenceFileName(name))
			throw new Error(`${directory}: '${name}' is not a file name of a recorded join`);
		files.push({ name, bytes: await fs.readFile(path.join(directory, name)) });
	}
	return { host, files };
}

/** One page over a flattened DevTools session. */
class Page {
	constructor(cdp, sessionId) { this.cdp = cdp; this.sessionId = sessionId; this.errors = []; this.inputFailures = []; this.crashed = null; }

	send(method, params, timeoutMs) { return this.cdp.send(method, params, this.sessionId, timeoutMs); }

	async eval(expression, timeoutMs = 30000) {
		const result = await this.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }, timeoutMs);
		if (result.exceptionDetails)
			throw new Error(`${expression.slice(0, 120)}: ${result.exceptionDetails.exception?.description ?? result.exceptionDetails.text}`);
		return result.result.value;
	}

	/** The smoke's status, or null before the harness object exists. */
	status() { return this.eval("globalThis.opentpwPerf ? JSON.parse(opentpwPerf.program.CoopJoinSmokeStatus()) : null"); }

	/** Why the page's own loop stopped ('The game stopped: …' or a start that failed), or null. */
	async stopped() {
		const text = await this.eval("document.getElementById('status')?.textContent ?? ''").catch(() => '');
		return /stopped|could not start/i.test(text) ? text : null;
	}

	/** Throws when the page crashed, lost a level's files, or stopped its loop. */
	async check() {
		if (this.crashed)
			throw new Error(`The page crashed: ${this.crashed}`);
		if (this.inputFailures.length)
			throw new Error(`The page could not copy the game's files: ${this.inputFailures.join('; ')}`);
		const stopped = await this.stopped();
		if (stopped)
			throw new Error(`The page stopped: ${stopped}`);
	}

	async waitFor(what, test, timeoutMs) {
		const begin = Date.now();
		for (;;) {
			await this.check();
			const status = await this.status().catch(() => null);
			if (status && test(status))
				return status;
			if (Date.now() - begin > timeoutMs)
				throw new Error(`Timed out after ${timeoutMs / 1000} s waiting for ${what}; last status ${JSON.stringify(status)}`);
			await sleep(250);
		}
	}
}

/** One run in a fresh profile: the page in its co-op smoke mode, the recorded join copied in, the join phase to its end, its evidence written to runDirectory. */
async function runOnce(options, evidence, webRoot, gameRoot, runDirectory) {
	const run = { mode: options.mode, label: options.label, started: new Date().toISOString(), size: `${options.width}x${options.height}`, muted: true, frameCap: 0 };
	let server, profile, chrome, cdp;
	try {
		server = await startServer(webRoot, gameRoot);
		profile = await fs.mkdtemp(path.join(os.tmpdir(), 'opentpw-web-coop-join-'));
		const launched = await launchChrome(options.chrome, profile, { headed: options.headed, audible: false, width: options.width, height: options.height });
		chrome = launched.chrome;
		cdp = await Cdp.connect(launched.url);
		run.chrome = (await cdp.send('Browser.getVersion')).product;
		const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
		const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
		const page = new Page(cdp, sessionId);
		cdp.on(message => {
			if (message.method === 'Target.targetCrashed' && message.params?.targetId === targetId)
				page.crashed = `the renderer ended (${message.params.status}, ${message.params.errorCode})`;
			if (message.method === 'Target.detachedFromTarget' && message.params?.sessionId === sessionId)
				page.crashed ??= 'the page was detached';
			if (message.sessionId !== sessionId)
				return;
			if (message.method === 'Runtime.exceptionThrown')
				page.errors.push(message.params.exceptionDetails?.exception?.description ?? message.params.exceptionDetails?.text);
			if (message.method === 'Runtime.consoleAPICalled' && message.params.type === 'error') {
				const text = message.params.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
				page.errors.push(text);
				// main.js reports a level folder it could not copy, and then tells the game the level is in all the same
				if (/^Copying .* failed/.test(text))
					page.inputFailures.push(text);
			}
		});
		await cdp.send('Target.setDiscoverTargets', { discover: true });
		await page.send('Runtime.enable');
		await page.send('Page.enable');
		// music and speech off, no frame cap, as the native smoke runs (its sound device suppressed; here muted Chrome, whose Web Audio still runs)
		const seed = { '/save/opentpw-options.json': JSON.stringify({ MusicOn: false, SpeechOn: false }), '/config/display.json': JSON.stringify({ FrameCap: 0 }) };
		const query = new URLSearchParams({ coopSmoke: '1', data: '/game/', keep: 'off', perfSeed: Buffer.from(JSON.stringify(seed)).toString('base64') });
		await page.send('Page.navigate', { url: `${server.origin}/index.html?${query}` });
		run.renderer = await page.eval(`(() => { const gl = document.createElement('canvas').getContext('webgl2'); const info = gl?.getExtension('WEBGL_debug_renderer_info');
			return info ? gl.getParameter(info.UNMASKED_RENDERER_WEBGL) : null; })()`).catch(() => null);
		const ready = await page.waitFor('the front end', status => status.mode && status.started && status.frontEnd, 10 * 60000);
		if (!ready.mode)
			throw new Error('The page did not choose its co-op smoke mode');
		// the pools extension's sprite sets arrive in the background: the run starts once they have (or the fetch gave up), so no fetch overlaps its frames
		const settled = await page.waitFor('the pool sprites', status => status.poolSprites === 'settled', 120000).then(() => true).catch(() => false);
		run.poolSprites = settled ? 'settled' : 'pending';
		for (const file of evidence.files)
			await page.eval(`opentpwPerf.program.CoopSmokeAddFile(${JSON.stringify(file.name)}, Uint8Array.from(atob(${JSON.stringify(file.bytes.toString('base64'))}), c => c.charCodeAt(0)))`, 120000);
		await page.eval('opentpwPerf.take(), opentpwPerf.takeSkipped(), true');
		const begin = Date.now();
		await page.eval(`opentpwPerf.program.CoopJoinSmokeStart(${JSON.stringify(options.mode)})`);
		// to the end: a page whose loop stops drawing frames while the smoke runs has stopped, whatever its status says
		let drawn = await page.eval('opentpwPerf.program.PerfFramesDrawn()'), drawnAt = Date.now(), status;
		for (;;) {
			await page.check();
			status = await page.status();
			if (status.state !== 'running')
				break;
			const now = await page.eval('opentpwPerf.program.PerfFramesDrawn()');
			if (now !== drawn)
				[drawn, drawnAt] = [now, Date.now()];
			else if (Date.now() - drawnAt > 120000)
				throw new Error(`The page drew no frame for 120 s at ${status.step} (frame ${status.frames})`);
			if (Date.now() - begin > options.timeoutMinutes * 60000)
				throw new Error(`The join phase did not end within ${options.timeoutMinutes} minutes; it stands at ${status.step} (frame ${status.frames})`);
			await sleep(1000);
		}
		run.seconds = (Date.now() - begin) / 1000;
		run.state = status.state;
		run.message = status.message;
		run.frames = status.frames;
		// the page's whole frame calls ([start, milliseconds]), the smoke's frames carry the same calls by frame number
		const pageFrames = await page.eval('opentpwPerf.take()', 120000);
		const joinEvidence = await page.eval("new TextDecoder().decode(opentpwPerf.program.ReadFile('/coop-smoke/join-evidence.json'))", 120000);
		await fs.mkdir(runDirectory, { recursive: true });
		await fs.writeFile(path.join(runDirectory, 'join-evidence.json'), joinEvidence);
		await fs.writeFile(path.join(runDirectory, 'host-evidence.json'), JSON.stringify(evidence.host, null, 2) + '\n');
		await fs.writeFile(path.join(runDirectory, 'page-frames.json'), JSON.stringify(pageFrames) + '\n');
		run.pageErrors = page.errors.slice(0, 20);
		return run;
	} catch (error) {
		run.state = 'failed';
		run.message = error.message;
		return run;
	} finally {
		cdp?.close();
		await stopChrome(chrome);
		await server?.close();
		if (profile)
			await fs.rm(profile, { recursive: true, force: true }).catch(() => {});
		await fs.mkdir(runDirectory, { recursive: true }).catch(() => {});
		await fs.writeFile(path.join(runDirectory, 'run.json'), JSON.stringify(run, null, 2) + '\n').catch(() => {});
	}
}

async function main() {
	const options = parseArguments(process.argv);
	const webRoot = path.resolve(options.web);
	const gameRoot = path.resolve(options.game);
	await fs.access(path.join(webRoot, 'index.html')).catch(() => { throw new Error(`${webRoot} has no index.html: pass the published wwwroot`); });
	const evidence = await readEvidence(path.resolve(options.evidence));
	const out = path.resolve(options.out ?? path.join('reports', 'web-coop-join', `${new Date().toISOString().slice(0, 10)}-${options.label}-${options.mode}`));
	let failed = 0;
	for (let index = 1; index <= options.runs; index++) {
		process.stderr.write(`run ${index} of ${options.runs}…\n`);
		const run = await runOnce(options, evidence, webRoot, gameRoot, path.join(out, `run-${index}`));
		console.log(`browser co-op join smoke [${options.mode}, ${options.label}] run ${index}: ${run.state} (${(run.message ?? '').slice(0, 300)})`);
		if (run.state !== 'passed')
			failed++;
	}
	console.log(`Evidence: ${out}`);
	process.exitCode = failed ? 1 : 0;
}

main().catch(error => {
	console.error(error.message);
	process.exit(2);
});
