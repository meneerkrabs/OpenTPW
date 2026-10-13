#!/usr/bin/env node
// Browser performance harness (docs/BROWSER-PERFORMANCE.md, "Measuring"). Serves a published web build and the player's own
// game folder on 127.0.0.1, opens it in a fresh headless Chrome profile through the DevTools Protocol and runs fixed scenarios:
//
//   start   loading until the first frame and the lobby
//   lobby   a steady window in the lobby
//   jungle  the cold start of Full Simulation Jungle (the construction frame), then a steady window in the park
//   buy     the first buy-menu open and four first selections (the 12 October table)
//   memory  memory samples over a longer park session
//
// The game files stay on this machine: they are read from --game and served to the local page only; nothing is uploaded
// and no game data belongs in the report. Example:
//
//   node tools/web-perf/run.mjs --web out/web/wwwroot --game "/path/to/Theme Park World" --label interpreter
import { promises as fs } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { Cdp, launchChrome, mainThreadBusy, medianResults, sleep, startServer, stopChrome, summarizeFrames } from './lib.mjs';

const DEFAULT_CHROME = {
	darwin: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
	linux: '/usr/bin/google-chrome',
	win32: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
}[process.platform];

// The first selections measured on 12 October (BROWSER-PERFORMANCE.md), by their English names. The buy window opens on the rides
// with the first one (Aztec Mayhem in Jungle) selected, so the menu open covers it.
const SELECTIONS = [['Rides', 'Belly Bounce'], ['Shops', 'Balloon Shop'], ['Shops', 'Burger Shop'], ['Shops', 'Drinks Shop']];

function parseArguments(argv) {
	const options = {
		web: null, game: null, label: 'unlabelled', out: null, chrome: process.env.CHROME ?? DEFAULT_CHROME, headed: false,
		scenarios: ['start', 'lobby', 'jungle', 'buy'], window: 30, memoryMinutes: 10, music: false, width: 1280, height: 900, runs: 1,
	};
	for (let index = 2; index < argv.length; index++) {
		const [flag, inline] = argv[index].split(/=(.*)/s);
		const value = () => inline ?? argv[++index];
		switch (flag) {
			case '--web': options.web = value(); break;
			case '--game': options.game = value(); break;
			case '--label': options.label = value(); break;
			case '--out': options.out = value(); break;
			case '--chrome': options.chrome = value(); break;
			case '--headed': options.headed = true; break;
			case '--scenarios': options.scenarios = value().split(',').map(item => item.trim()).filter(Boolean); break;
			case '--window': options.window = Number(value()); break;
			case '--memory-minutes': options.memoryMinutes = Number(value()); break;
			case '--music': options.music = true; break;
			case '--size': [options.width, options.height] = value().split('x').map(Number); break;
			case '--runs': options.runs = Math.max(1, Number(value()) || 1); break;
			case '--help': case '-h': console.log(usage()); process.exit(0);
			default: throw new Error(`Unknown option ${flag}\n${usage()}`);
		}
	}
	if (!options.web || !options.game)
		throw new Error(`--web and --game are required\n${usage()}`);
	// Every scenario needs the loaded page, so the start always runs (and is measured) first.
	options.scenarios = ['start', ...options.scenarios.filter(name => name !== 'start')];
	return options;
}

function usage() {
	return `Usage: node tools/web-perf/run.mjs --web <published wwwroot> --game <Theme Park World folder> [options]
  --label <name>          build label in the report (interpreter, aot, ...)
  --out <file.json>       report path (default reports/web-perf/<date>-<label>.json)
  --scenarios <list>      comma list of start,lobby,jungle,buy,memory (default start,lobby,jungle,buy)
  --window <seconds>      steady window length (default 30)
  --memory-minutes <n>    memory scenario length (default 10)
  --music                 keep music and speech on (default: muted, effects on, as on 11 October)
  --size <w>x<h>          window size (default 1280x900)
  --runs <n>              repeat the whole session n times, each in a fresh profile, and report the medians (default 1)
  --headed                show the browser window
  --chrome <path>         Chrome executable (default: the platform's Chrome, or $CHROME)`;
}

/** One page session: evaluation, waiting and the measurement windows. */
class Page {
	constructor(cdp, sessionId) { this.cdp = cdp; this.sessionId = sessionId; this.console = []; }

	send(method, params, timeoutMs) { return this.cdp.send(method, params, this.sessionId, timeoutMs); }

	async eval(expression, timeoutMs = 30000) {
		const result = await this.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }, timeoutMs);
		if (result.exceptionDetails)
			throw new Error(`${expression}: ${result.exceptionDetails.exception?.description ?? result.exceptionDetails.text}`);
		return result.result.value;
	}

	state() { return this.eval('globalThis.opentpwPerf?.started ? opentpwPerf.state() : null'); }

	async waitFor(what, test, timeoutMs) {
		const begin = Date.now();
		for (;;) {
			const state = await this.state().catch(() => null);
			if (state && test(state))
				return state;
			const stopped = await this.eval("document.getElementById('status')?.textContent ?? ''").catch(() => '');
			if (/stopped|could not start/i.test(stopped))
				throw new Error(`The game stopped while waiting for ${what}: ${stopped}`);
			if (Date.now() - begin > timeoutMs)
				throw new Error(`Timed out after ${timeoutMs / 1000} s waiting for ${what}; last state ${JSON.stringify(state)}`);
			await sleep(100);
		}
	}

	async call(method, ...args) {
		const ok = await this.eval(`opentpwPerf.program.${method}(${args.map(arg => JSON.stringify(arg)).join(', ')})`);
		if (ok === false)
			throw new Error(`${method}(${args.join(', ')}) found nothing to act on; state ${JSON.stringify(await this.state())}`);
		return ok;
	}

	async metrics() { return this.send('Performance.getMetrics'); }

	async memory() {
		const counters = await this.eval('opentpwPerf.counters()');
		const metrics = await this.metrics();
		const heap = metrics.metrics.find(metric => metric.name === 'JSHeapUsedSize')?.value ?? null;
		return { ...counters, jsHeapBytes: heap };
	}

	/** The page's clock and the frames recorded since the last mark (which it drains): the boundaries of every interval. */
	mark() { return this.eval('({ now: performance.now(), frames: opentpwPerf.take() })'); }

	/** Waits until the page has drawn at least one more frame (two animation frames: the game's own callback runs in between). */
	nextFrame() { return this.eval('new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))'); }

	/** Frames, main-thread occupancy and long tasks over about windowMs, starting now; rates use the page's own elapsed time. */
	async steady(windowMs) {
		const start = await this.mark();
		const before = await this.metrics();
		await sleep(windowMs);
		const after = await this.metrics();
		const end = await this.mark();
		const elapsed = end.now - start.now;
		const tasks = (await this.eval('globalThis.__opentpwLongTasks.splice(0)'))
			.filter(([startTime]) => startTime >= start.now && startTime < end.now);
		return {
			windowS: Math.round(elapsed) / 1000,
			mainThreadBusyPercent: mainThreadBusy(before, after),
			...summarizeFrames(end.frames, elapsed),
			longTasks: tasks.length,
			longestTaskMs: tasks.length ? Math.round(Math.max(...tasks.map(([, duration]) => duration))) : 0,
			memory: await this.memory(),
		};
	}

	/**
	 * Runs act(), waits until done(state) and a frame has been drawn after it, then settles; returns the frames from the action on
	 * (the first-use cost). readyMs is page time from the action to that drawn frame.
	 */
	async action(what, act, done, { timeoutMs = 10000, settleMs = 1500 } = {}) {
		const start = await this.mark();
		await act();
		await this.waitFor(what, done, timeoutMs);
		await this.nextFrame();
		const readyMs = Math.round(await this.eval('performance.now()') - start.now);
		await sleep(settleMs);
		const end = await this.mark();
		const summary = summarizeFrames(end.frames, end.now - start.now);
		return { readyMs, maxFrameMs: summary.maxMs, over50Ms: summary.over50Ms, frames: summary.frames, p95Ms: summary.p95Ms };
	}
}

const scenarios = {
	async start(page, options, context) {
		const begin = context.navigationStart;
		await page.waitFor('the first frame', state => state.started, 600000);
		const firstFrameMs = Date.now() - begin;
		const lobby = await page.waitFor('the lobby', state => state.menu === 'lobby', 120000);
		const lobbyMs = Date.now() - begin;
		await sleep(3000);
		return { firstFrameMs, lobbyMs, island: lobby.island, memory: await page.memory() };
	},

	async lobby(page, options) {
		return page.steady(options.window * 1000);
	},

	async jungle(page, options) {
		const state = await page.state();
		if (state.island !== 'jungle')
			throw new Error(`The lobby shows ${state.island}, not the jungle island`);
		await page.call('PerfClick', 'enterPark');
		await page.waitFor('the game mode menu', current => current.menu === 'gameMode', 10000);
		const cold = await page.action('the Jungle park', () => page.call('PerfClick', 'fullSimulation'),
			current => current.hud && current.level === 'jungle', { timeoutMs: 300000, settleMs: 5000 });
		const steady = await page.steady(options.window * 1000);
		return { cold, steady };
	},

	async buy(page) {
		const state = await page.state();
		if (!state.hud)
			throw new Error('The buy scenario needs a park: run it after jungle');
		const result = {};
		result.openMenu = await page.action('the buy window', () => page.call('PerfClick', 'buy'), current => current.buyOpen);
		result.openMenu.selected = (await page.state()).selected;
		for (const [category, name] of SELECTIONS) {
			const items = JSON.parse(await page.eval(`opentpwPerf.program.PerfBuyItems(${JSON.stringify(category)})`));
			const item = items.find(entry => entry.name?.toLowerCase() === name.toLowerCase());
			if (!item) {
				result[name] = { missing: true };
				continue;
			}
			result[name] = await page.action(name, () => page.call('PerfShowBuyItem', category, item.archive),
				current => current.selected?.toLowerCase() === item.archive.toLowerCase());
		}
		await page.call('PerfKey', 'Back');
		return result;
	},

	async memory(page, options) {
		const state = await page.state();
		if (!state.hud)
			throw new Error('The memory scenario needs a park: run it after jungle');
		const samples = [];
		const begin = Date.now();
		for (let minute = 0; minute <= options.memoryMinutes * 6; minute++) {
			// Drain the page's frame log first, so the samples do not measure its own growth.
			await page.eval('opentpwPerf.take().length');
			const memory = await page.memory();
			samples.push({ atS: Math.round((Date.now() - begin) / 1000), turn: (await page.state()).turn, ...memory });
			if (minute < options.memoryMinutes * 6)
				await sleep(10000);
		}
		const first = samples[0], last = samples.at(-1);
		return {
			minutes: options.memoryMinutes,
			wasmGrowthBytes: last.wasmBytes - first.wasmBytes,
			managedGrowthBytes: last.managedBytes - first.managedBytes,
			peakWasmBytes: Math.max(...samples.map(sample => sample.wasmBytes)),
			samples,
		};
	},
};

function gitCommit(directory) {
	try {
		return execFileSync('git', ['-C', directory, 'rev-parse', '--short', 'HEAD'], { encoding: 'utf8' }).trim();
	} catch {
		return null;
	}
}

function markdown(report) {
	const lines = [`## ${report.label} — ${report.date} (${report.chrome}, ${report.renderer ?? 'renderer unknown'})`, ''];
	const results = report.results;
	if (results.start && !results.start.error)
		lines.push(`- Start: first frame ${results.start.firstFrameMs} ms, lobby ${results.start.lobbyMs} ms`);
	for (const [name, steady] of [['Lobby', results.lobby], ['Jungle', results.jungle?.steady]])
		if (steady && !steady.error)
			lines.push(`- ${name} steady: main thread busy ${steady.mainThreadBusyPercent}%, ${steady.fps} fps, frame p95 ${steady.p95Ms} ms, largest gap ${steady.largestGapMs} ms, long tasks ${steady.longTasks}`);
	if (results.jungle?.cold)
		lines.push(`- Jungle cold: ready in ${results.jungle.cold.readyMs} ms, largest frame ${results.jungle.cold.maxFrameMs} ms`);
	if (results.buy && !results.buy.error) {
		lines.push('', '| First action | Largest frame (ms) | Frames ≥ 50 ms |', '| --- | ---: | ---: |');
		for (const [name, entry] of Object.entries(results.buy))
			lines.push(entry.missing ? `| ${name} | not listed | |` : `| ${name === 'openMenu' ? `Open buy menu / ${entry.selected ?? ''}` : name} | ${entry.maxFrameMs} | ${entry.over50Ms} |`);
	}
	if (results.memory && !results.memory.error)
		lines.push('', `- Memory over ${results.memory.minutes} min: wasm +${(results.memory.wasmGrowthBytes / 1048576).toFixed(1)} MB (peak ${(results.memory.peakWasmBytes / 1048576).toFixed(0)} MB), managed +${(results.memory.managedGrowthBytes / 1048576).toFixed(1)} MB`);
	for (const [name, entry] of Object.entries(results))
		if (entry?.error)
			lines.push(`- ${name}: FAILED — ${entry.error}`);
	return lines.join('\n');
}

/** One session in a fresh Chrome profile: the scenarios in order, their results and the page's uncaught errors. */
async function runSession(options, webRoot, gameRoot) {
	const report = { date: new Date().toISOString(), results: {} };
	let server, profile, chrome, cdp;
	try {
		server = await startServer(webRoot, gameRoot);
		profile = await fs.mkdtemp(path.join(os.tmpdir(), 'opentpw-web-perf-'));
		const launched = await launchChrome(options.chrome, profile, options);
		chrome = launched.chrome;
		cdp = await Cdp.connect(launched.url);
		report.chrome = (await cdp.send('Browser.getVersion')).product;
		const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
		const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
		const page = new Page(cdp, sessionId);
		cdp.on(message => {
			if (message.sessionId === sessionId && message.method === 'Runtime.exceptionThrown')
				page.console.push(message.params.exceptionDetails?.exception?.description ?? message.params.exceptionDetails?.text);
		});
		await page.send('Runtime.enable');
		await page.send('Performance.enable', { timeDomain: 'timeTicks' });
		await page.send('Page.enable');
		await page.send('Page.addScriptToEvaluateOnNewDocument', { source: `globalThis.__opentpwLongTasks = [];
			new PerformanceObserver(list => { for (const entry of list.getEntries()) globalThis.__opentpwLongTasks.push([entry.startTime, entry.duration]); })
				.observe({ type: 'longtask', buffered: true });` });
		const seed = options.music ? {} : { '/save/opentpw-options.json': JSON.stringify({ MusicOn: false, SpeechOn: false }) };
		const query = new URLSearchParams({ perf: '1', data: '/game/', perfSeed: Buffer.from(JSON.stringify(seed)).toString('base64') });
		const context = { navigationStart: Date.now() };
		await page.send('Page.navigate', { url: `${server.origin}/index.html?${query}` });
		report.renderer = await page.eval(`(() => { const gl = document.createElement('canvas').getContext('webgl2'); const info = gl?.getExtension('WEBGL_debug_renderer_info');
			return info ? gl.getParameter(info.UNMASKED_RENDERER_WEBGL) : null; })()`).catch(() => null);
		for (const name of options.scenarios) {
			if (!scenarios[name]) {
				report.results[name] = { error: 'unknown scenario' };
				continue;
			}
			process.stderr.write(`${name}…\n`);
			try {
				report.results[name] = await scenarios[name](page, options, context);
			} catch (error) {
				report.results[name] = { error: error.message };
				if (name === 'start' || name === 'jungle')
					break;
			}
		}
		if (page.console.length)
			report.pageErrors = page.console.slice(0, 20);
	} finally {
		cdp?.close();
		await stopChrome(chrome);
		await server?.close();
		if (profile)
			await fs.rm(profile, { recursive: true, force: true }).catch(() => {});
	}
	return report;
}

async function main() {
	const options = parseArguments(process.argv);
	const webRoot = path.resolve(options.web);
	const gameRoot = path.resolve(options.game);
	await fs.access(path.join(webRoot, 'index.html')).catch(() => { throw new Error(`${webRoot} has no index.html: pass the published wwwroot`); });
	const sessions = [];
	for (let run = 1; run <= options.runs; run++) {
		if (options.runs > 1)
			process.stderr.write(`run ${run} of ${options.runs}\n`);
		sessions.push(await runSession(options, webRoot, gameRoot));
	}
	// With several runs the results are the medians (memory samples from the first run); every run is kept in "runs".
	const report = {
		label: options.runs > 1 ? `${options.label} (median of ${options.runs} runs)` : options.label,
		date: sessions[0].date, commit: gitCommit(path.dirname(new URL(import.meta.url).pathname)),
		machine: `${os.cpus()[0]?.model ?? 'unknown'}, ${os.platform()} ${os.release()}`, size: `${options.width}x${options.height}`,
		muted: !options.music, scenarios: options.scenarios, chrome: sessions[0].chrome, renderer: sessions[0].renderer,
		results: options.runs > 1 ? medianResults(sessions.map(session => session.results)) : sessions[0].results,
		...(options.runs > 1 ? { runs: sessions } : { pageErrors: sessions[0].pageErrors }),
	};
	for (const session of sessions)
		for (const [name, entry] of Object.entries(session.results))
			if (entry?.error)
				report.results[name] = { ...report.results[name], error: entry.error };
	const out = options.out ?? path.join('reports', 'web-perf', `${report.date.slice(0, 10)}-${options.label}.json`);
	await fs.mkdir(path.dirname(out), { recursive: true });
	await fs.writeFile(out, JSON.stringify(report, null, '\t') + '\n');
	console.log(markdown(report));
	console.log(`\nReport: ${out}`);
	if (Object.values(report.results).some(entry => entry?.error))
		process.exitCode = 1;
}

main().catch(error => {
	console.error(error.message);
	process.exit(2);
});
