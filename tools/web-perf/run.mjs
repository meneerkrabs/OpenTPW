#!/usr/bin/env node
// Browser performance harness (docs/BROWSER-PERFORMANCE.md, "Measuring"). Serves a published web build and the player's own
// game folder on 127.0.0.1, opens it in a fresh headless Chrome profile through the DevTools Protocol and runs fixed scenarios:
//
//   start   loading until the first frame and the lobby
//   lobby   a steady window in the lobby
//   jungle  the cold start of Full Simulation Jungle (the construction frame), then a steady window in the park
//   buy     the first buy-menu open and four first selections (the 12 October table)
//   winterCost    the frame cost of a crowd in rain, then settled snow, then umbrellas, then warm items added one after the other (after jungle)
//   umbrellaCost  the frame cost of a crowd in rain with and without an open umbrella over every guest, at the Home and the farthest zoom (after jungle)
//   memory  memory samples over a longer park session
//   switch  memory across park changes: Jungle, the lobby, the next island's park, the lobby, Jungle again
//   revisit a second visit in the same profile without the served folder: Play starts from the files kept in the browser
//           (needs --keep), then Jungle opens from them
//   reload  a second load of the page in the same profile (the HTTP cache is warm): which files are asked again and which answer 304 (needs --upstream)
//   transfer  the requests the page made to the served site, by the scenario they happened in, with status, Content-Encoding, Cache-Control and
//           the bytes sent (needs --upstream; run it last)
//   digest  deterministic work that must give the same result in every build (texture decoding, a fixed park run), timed
//
// The game files stay on this machine: they are read from --game and served to the local page only; nothing is uploaded
// and no game data belongs in the report. Example:
//
//   node tools/web-perf/run.mjs --web out/web/wwwroot --game "/path/to/Theme Park World" --label interpreter
import { promises as fs, readFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { Cdp, launchChrome, mainThreadBusy, medianResults, sleep, startServer, stopChrome, summarizeFrames } from './lib.mjs';
import { markdown } from './report.mjs';
import { doorScenario, eventsScenario, lobbyEnterClick, profileClockOffset, recordEvent, profileFrames, profileOfFrame, useSymbols } from './events.mjs';

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
		web: null, game: null, upstream: null, trackOrigins: false, gc: null, label: 'unlabelled', out: null, chrome: process.env.CHROME ?? DEFAULT_CHROME, headed: false, audible: false,
		scenarios: ['start', 'lobby', 'jungle', 'buy', 'digest'], window: 30, memoryMinutes: 10, music: false, width: 1280, height: 900, runs: 1, frameCap: 0, keep: 'off', zoom: null, shot: null, follow: null, crowd: 400, profile: false, soakSeconds: 90, doorWaitSeconds: 20, coldMarkMs: 40, advice: [], cover: 1, evening: null, eveningLamps: 0, visits: 3,
	};
	for (let index = 2; index < argv.length; index++) {
		const [flag, inline] = argv[index].split(/=(.*)/s);
		const value = () => inline ?? argv[++index];
		switch (flag) {
			case '--web': options.web = value(); break;
			case '--game': options.game = value(); break;
			case '--upstream': options.upstream = value(); break;
			case '--track-origins': options.trackOrigins = true; break;
			case '--gc': options.gc = value(); break;
			case '--label': options.label = value(); break;
			case '--out': options.out = value(); break;
			case '--chrome': options.chrome = value(); break;
			case '--headed': options.headed = true; break;
			case '--audible': options.audible = true; break;
			case '--scenarios': options.scenarios = value().split(',').map(item => item.trim()).filter(Boolean); break;
			case '--window': options.window = Number(value()); break;
			case '--memory-minutes': options.memoryMinutes = Number(value()); break;
			case '--music': options.music = true; break;
			case '--size': [options.width, options.height] = value().split('x').map(Number); break;
			case '--runs': options.runs = Math.max(1, Number(value()) || 1); break;
			case '--frame-cap': options.frameCap = Number(value()); break;
			case '--keep': options.keep = value(); break;
			case '--zoom': options.zoom = Number(value()); break;
			case '--crowd': options.crowd = Number(value()); break;
			case '--profile': options.profile = true; break;
			case '--profile-out': options.profileOut = value(); break;
			case '--symbols': useSymbols(readFileSync(value(), 'utf8')); break;
			case '--cold-mark-ms': options.coldMarkMs = Math.max(1, Number(value()) || 40); options.coldMarks = true; break;
			case '--door-wait': options.doorWaitSeconds = Math.max(1, Number(value()) || 20); break;
			case '--soak': options.soakSeconds = Math.max(1, Number(value()) || 90); break;
			case '--advice': options.advice = value().split(',').map(Number).filter(Number.isFinite); break;
			case '--shot': options.shot = value(); break;
			case '--follow': options.follow = Math.trunc(Number(value())) || 0; break;
			case '--cover': options.cover = Math.min(1, Math.max(0, Number(value()))); break;
			case '--evening': options.evening = value(); break;
			case '--evening-lamps': options.eveningLamps = Math.max(0, Number(value()) || 0); break;
			case '--audio-later': options.audioLater = true; break;
			case '--runtime-roots': options.runtimeRoots = true; break;
			case '--visits': options.visits = Math.max(3, Math.trunc(Number(value())) || 3); break;
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
  --upstream <url>        forward every request but /game/ to this server (the OpenTPW.Server that serves the page in production) and log its transport: scenarios transfer and reload
  --gc <params>           an experiment: the runtime's garbage collector parameters (MONO_GC_PARAMS, for example soft-heap-limit=200m), to see how they change the peak wasm memory
  --track-origins         jungle: record where the textures made from bytes come from (their stack), so the switch scenario names the largest ones (a few ms per texture)
  --label <name>          build label in the report (interpreter, aot, ...)
  --out <file.json>       report path (default reports/web-perf/<date>-<label>.json)
  --scenarios <list>      comma list of start,lobby,jungle,buy,umbrellas,snow,winter,winterCost,umbrellaCost,events,door,lobbyEnter,memory,switch,revisit,digest,determinism,transfer,reload (default start,lobby,jungle,buy,digest)
  --window <seconds>      steady window length (default 30)
  --memory-minutes <n>    memory scenario length (default 10)
  --music                 keep music and speech on (default: muted, effects on, as on 11 October)
  --size <w>x<h>          window size (default 1280x900)
  --profile               jungle (cold start) and events: also record a CPU profile (Profiler domain) and name the functions each slow frame spent its time in
  --profile-out <file>    with --profile: also write the cold start's raw CPU profile (.cpuprofile JSON with the slow frames' page times)
  --symbols <file>        with --profile: name the AOT functions from the publish's Emscripten symbol map (-p:WasmEmitSymbolMap=true)
  --cold-mark-ms <ms>     jungle: record the cold start's named work (also without events or --profile), in its frames of at least this long; default 40
  --door-wait <seconds>   door: how long each opening waits for the first new guest (default 20)
  --soak <seconds>        events: length of the closing soak at the fastest speed (default 90)
  --advice <ids>          events: advisor advice message ids to post and time, comma separated
  --crowd <n>             umbrellaCost, winterCost: extra guests staged in the Jungle park (default 400)
  --shot <file>           umbrellas, snow, winter, winterCost: where the screenshot goes; buy: one screenshot per row, named <file>-<row>.png
  --follow <n>            winter, winterCost: the screenshot follows the visible guest after skipping |n| of them by id, counted from the newest when negative (winter default 0, the lowest; winterCost default -250, a guest of a fresh crowd standing in the open and not under the entrance roofs)
  --cover <0..1>          snow: the settled snow cover to hold (default 1)
  --zoom <value>          jungle: after the steady window, hold the park camera at this zoom value (40..300 with the wide zoom option) and measure a second window
  --evening <mode>        jungle: after the steady window, hold the evening look (night, dusk, day) or turn the cycle and its lamps off, and measure a second window
  --evening-lamps <n>     with --evening: add n extra lamp lights around the camera target (the browser twin of the desktop's --debug-evening-lamps) for the second window
  --visits <n>            switch: park visits (default 3); each one past the third is another lobby and Jungle, to show whether memory grows per visit
  --runtime-roots         switch: in the lobby, also the bytes behind the framework's own statics and shared array pools, and the reached objects by type (slow)
  --audio-later           switch: read what the sound mixer reaches again 20 s after each point (a decode in flight then has ended)
  --keep <mode>           keep the game files in the browser on the first visit: lazy, all or off (default off)
  --frame-cap <fps>       the display settings' frame cap (0 off, 30, 60; default 0)
  --runs <n>              repeat the whole session n times, each in a fresh profile, and report the medians (default 1)
  --headed                show the browser window
  --audible               let Chrome play sound (default: --mute-audio; --music still measures decoding)
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

	/**
	 * Starts Full Simulation on the game mode screen by what the game shows rather than by counting presses: Full Simulation, the
	 * tick, and the new-progress consent (a profile without a progress file asks for it once, its first choice starts the
	 * progress) are each clicked once per visit of their screen, until the park of `level` has its HUD. Returns the time of the park.
	 * A progress failure dialog ends it with an error; the Continue screen of an island with an automatic checkpoint is answered with New park.
	 */
	async enterFullSimulation(level, timeoutMs = 300000) {
		// Leaving a park writes its automatic checkpoint, so entering the island again offers it: [Continue automatic, New park, Back], and the harness only
		// ever starts new parks (it never saves by hand, so no Continue saved line shifts the choices).
		const steps = { gameMode: ['fullSimulation', 'ok'], newProgress: ['choice0'], themeStart: ['choice1'] };
		const begin = Date.now();
		let screen = null, done = 0;
		for (;;) {
			const state = await this.state().catch(() => null);
			if (state?.hud && state.level === level)
				return Date.now() - begin;
			const shown = state?.hudScreen === 'progressFailed' ? 'progressFailed' : state?.menu;
			if (shown === 'progressFailed')
				throw new Error(`Starting ${level} ended in the progress failure dialog; state ${JSON.stringify(state)}`);
			if (shown !== screen) {
				screen = shown;
				done = 0;
			}
			const order = steps[shown];
			if (order && done < order.length) {
				// A screen can show its element only after its first frame; a missing one is retried on the next poll.
				if (await this.eval(`opentpwPerf.program.PerfClick(${JSON.stringify(order[done])})`).catch(() => false)) {
					done++;
					await this.nextFrame();
				}
			}
			const stopped = await this.eval("document.getElementById('status')?.textContent ?? ''").catch(() => '');
			if (/stopped|could not start/i.test(stopped))
				throw new Error(`The game stopped while starting ${level}: ${stopped}`);
			if (Date.now() - begin > timeoutMs)
				throw new Error(`Timed out after ${timeoutMs / 1000} s starting ${level}; last state ${JSON.stringify(state)}`);
			await sleep(100);
		}
	}

	async metrics() { return this.send('Performance.getMetrics'); }

	async memory() {
		const counters = await this.eval('opentpwPerf.counters()');
		const metrics = await this.metrics();
		const heap = metrics.metrics.find(metric => metric.name === 'JSHeapUsedSize')?.value ?? null;
		// Resident memory of the largest renderer process (physical memory: a wasm memory that grew counts only the pages the page touched), where ps exists
		let rssBytes = null;
		try {
			const processes = (await this.cdp.send('SystemInfo.getProcessInfo')).processInfo.filter(info => info.type === 'renderer');
			const sizes = processes.map(info => Number(execFileSync('ps', ['-o', 'rss=', '-p', String(info.id)], { encoding: 'utf8' }).trim()) * 1024).filter(Number.isFinite);
			rssBytes = sizes.length ? Math.max(...sizes) : null;
		} catch {
			// no SystemInfo domain or no ps: the other counters stand
		}
		return { ...counters, jsHeapBytes: heap, rssBytes };
	}

	/** The page's clock and the frames recorded since the last mark (which it drains): the boundaries of every interval. */
	mark() { return this.eval('({ now: performance.now(), frames: opentpwPerf.take(), skipped: opentpwPerf.takeSkipped() })'); }

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
			skippedFrames: end.skipped,
			longTasks: tasks.length,
			longestTaskMs: tasks.length ? Math.round(Math.max(...tasks.map(([, duration]) => duration))) : 0,
			memory: await this.memory(),
		};
	}

	/**
	 * Runs act(), waits until done(state) and a frame has been drawn after it, then settles; returns the frames from the action on
	 * (the first-use cost). readyMs is page time from the action to that drawn frame.
	 */
	async action(what, act, done, { timeoutMs = 10000, settleMs = 1500, onReady, slowMs = 40 } = {}) {
		const start = await this.mark();
		await act();
		await this.waitFor(what, done, timeoutMs);
		await this.nextFrame();
		const readyMs = Math.round(await this.eval('performance.now()') - start.now);
		const atReady = await onReady?.();
		await sleep(settleMs);
		const end = await this.mark();
		const summary = summarizeFrames(end.frames, end.now - start.now);
		// With the game's named work on (the events scenario turns it on) the slowest frames come with it.
		const slowFrames = end.frames.filter(frame => frame[2] && frame[1] >= slowMs).map(([begin, duration, marks]) => ({ atMs: Math.round(begin - start.now), beginMs: begin, ms: Math.round(duration * 10) / 10, marks }));
		const result = { readyMs, maxFrameMs: summary.maxMs, over33Ms: summary.over33Ms, over50Ms: summary.over50Ms, longFramesMs: summary.longFramesMs, frames: summary.frames, p95Ms: summary.p95Ms, ...(slowFrames.length ? { slowFrames } : {}), ...(atReady ? { atReady } : {}) };
		// every frame ([begin, ms]), to line a CPU profile up with the page's clock; not enumerable, so it stays out of the report
		Object.defineProperty(result, 'pageFrames', { value: end.frames.map(([begin, duration]) => [begin, duration]) });
		return result;
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
		await page.waitFor('the game mode menu', current => current.menu === 'gameMode' || current.menu === 'themeStart', 10000);
		// The load profile covers the click, the construction and the first seconds after (textures that load lazily).
		await page.eval('opentpwPerf.program.PerfProfile(true)');
		// The events scenario (and --profile) also wants the named work of the cold start's slow frames.
		const coldMarks = options.scenarios.includes('events') || options.profile || options.coldMarks;
		if (coldMarks)
			await page.eval(`opentpwPerf.marks = true, opentpwPerf.markMs = ${options.coldMarkMs}, opentpwPerf.program.PerfFrameMarks(true), 0`);
		// --profile: a CPU profile of the cold start names the functions of each slow frame
		let coldProfile = null;
		if (options.profile) {
			await page.send('Profiler.enable');
			await page.send('Profiler.setSamplingInterval', { interval: 250 });
			await page.send('Profiler.start');
			coldProfile = await page.eval('performance.now()');
		}
		if (options.trackOrigins)
			await page.eval('opentpwPerf.program.PerfTrackTextureOrigins(true)');
		const cold = await page.action('the Jungle park', () => page.enterFullSimulation('jungle'),
			current => current.hud && current.level === 'jungle', { timeoutMs: 300000, settleMs: 5000, slowMs: options.coldMarkMs,
				onReady: async () => ({
					profile: JSON.parse(await page.eval('opentpwPerf.program.PerfLoadProfile()')),
					// Builds from before parks were built over frames have no such hook.
					build: JSON.parse(await page.eval("opentpwPerf.program.PerfParkBuild?.() ?? '[]'")),
				}) });
		// The profile when the park was ready (the start itself), and after the settle time (with what loaded lazily).
		cold.startProfile = cold.atReady.profile;
		// The construction time of each frame the park was built over; the last includes entering it.
		cold.buildFramesMs = cold.atReady.build;
		delete cold.atReady;
		if (coldProfile != null) {
			const captured = (await page.send('Profiler.stop', undefined, 120000)).profile;
			if (options.profileOut)
				await fs.writeFile(options.profileOut, JSON.stringify({ pageNowAtStart: coldProfile, slowFrames: (cold.slowFrames ?? []).map(({ beginMs, ms }) => [beginMs, ms]), frames: cold.pageFrames, profile: captured }));
			// each slow frame's window is lined up with the frame the profile itself saw (profileOffsetMs: how far the two clocks were apart)
			const offset = profileClockOffset(profileFrames(captured, coldProfile), coldProfile, cold.pageFrames);
			for (const frame of cold.slowFrames ?? [])
				Object.assign(frame, profileOfFrame(captured, coldProfile, offset, frame.beginMs, frame.ms, { functions: 40, stacks: 15, owners: 20 }));
		}
		cold.profile = JSON.parse(await page.eval('opentpwPerf.program.PerfLoadProfile()'));
		await page.eval('opentpwPerf.program.PerfProfile(false)');
		if (coldMarks)
			await page.eval('opentpwPerf.marks = false, opentpwPerf.markMs = 8, opentpwPerf.program.PerfFrameMarks(false), 0');
		const steady = await page.steady(options.window * 1000);
		// the GPU deletions spread over frames (at most 1 ms of them per frame) drain once the park has settled: a few frames at most
		let pendingDeletions = null;
		for (const begin = Date.now(); Date.now() - begin < 5000; await sleep(100))
			if ((pendingDeletions = await page.eval('opentpwPerf.counters().pendingDeletions ?? 0')) === 0)
				break;
		if (pendingDeletions !== 0)
			throw new Error(`${pendingDeletions} scheduled GPU deletions still wait 5 s after the Jungle park's steady window`);
		const result = { cold, steady };
		// [evening] the same park with the evening look held at night, dusk or day, or the cycle and its lamps turned off (night, dusk, day, off), measured in a second window
		if (options.evening != null) {
			const accepted = await page.eval(`opentpwPerf.program.PerfEvening(${JSON.stringify(options.evening)})`);
			if (!accepted)
				throw new Error(`--evening expects night, dusk, day, off or on, not ${options.evening}`);
			let lamps = null;
			if (options.eveningLamps > 0) {
				lamps = await page.eval(`opentpwPerf.program.PerfEveningLamps(${options.eveningLamps})`);
				if (lamps !== options.eveningLamps)
					throw new Error(`--evening-lamps: the park did not take ${options.eveningLamps} lamps (got ${lamps})`);
			}
			await sleep(2000);
			result.evening = { mode: options.evening, ...(lamps != null ? { extraLamps: lamps } : {}), ...await page.steady(options.window * 1000) };
		}
		// the zoom window measures the park as it was: undo the evening hold and the extra lamps first
		if (options.evening != null && options.zoom != null) {
			if (options.eveningLamps > 0)
				await page.eval('opentpwPerf.program.PerfEveningLamps(0)');
			await page.eval('opentpwPerf.program.PerfEvening("on")');
		}
		if (options.zoom == null)
			return result;
		// [wide zoom] the same park with the camera held at another zoom value (tp.exe's own range is 70..130; the option widens it to 40..300)
		const reached = await page.eval(`opentpwPerf.program.PerfZoom(${options.zoom})`);
		await sleep(2000);
		const zoomed = { zoom: options.zoom, reached, ...await page.steady(options.window * 1000) };
		return { ...result, zoomed };
	},

	/**
	 * [EXT:umbrellas-tuning] Frame cost of umbrellas: in the Jungle park with a crowd (--crowd extra guests, default 400) in rain, a steady window with no umbrella and one with an open umbrella over every guest, the umbrellas the
	 * last frame drew, and the same two windows at the wide zoom's far end (zoom 300). The park is staged by Program.PerfUmbrellaCrowd (UmbrellaPerf) and is not a real run afterwards.
	 */
	async umbrellaCost(page, options) {
		const state = await page.state();
		if (!state.hud || state.level !== 'jungle')
			throw new Error('The umbrellaCost scenario needs the Jungle park: run it after jungle');
		const crowd = options.crowd;
		// the crowd is staged once (the first call); later calls only switch the umbrellas, so both windows have the same guests
		let extra = crowd;
		const stage = async umbrellas => {
			const staged = JSON.parse(await page.eval(`opentpwPerf.program.PerfUmbrellaCrowd(${extra}, 30, ${umbrellas})`));
			extra = 0;
			return staged;
		};
		const result = { crowd };
		for (const zoom of [null, 300]) {
			const key = zoom == null ? 'home' : `zoom${zoom}`;
			if (zoom != null)
				result[`${key}Reached`] = await page.eval(`opentpwPerf.program.PerfZoom(${zoom})`);
			result[`${key}Plain`] = { staged: await stage(false), ...await page.steady(options.window * 1000) };
			result[`${key}Umbrellas`] = { staged: await stage(true) };
			await sleep(2000);
			Object.assign(result[`${key}Umbrellas`], await page.steady(options.window * 1000));
			result[`${key}Umbrellas`].drawn = await page.eval('opentpwPerf.program.PerfUmbrellaCrowdDrawn()');
		}
		return result;
	},

	/**
	 * [EXT:winter-tuning] After jungle: the frame cost of a staged crowd (--crowd, default 400 extra guests) at the Home zoom, one window per step, each added to the one before. Every window runs in rain: the staging applies weather quality 30
	 * (as umbrellaCost) and each step applies it again just before its window (the park's own weather rolls can otherwise end the rain, which also closes the umbrellas), and every window records whether it still rained at its end (`raining`, 1 or 0).
	 * So the first window is a rain-only crowd, not a clear one: `rain` (crowd bare, no snow cover), `rainSnow` (the settled snow held at --cover, default 1), `rainSnowUmbrellas` (an open umbrella over every guest, held open every frame),
	 * `rainSnowDressed` (the umbrellas still open and every guest dressed in warm items, hat or earmuffs by id and mittens on three in four, as the winter scenario) and `rainSnowDressedFar` (the same at the farthest zoom).
	 * The cost of the items alone is `rainSnowDressed` minus `rainSnowUmbrellas`; the umbrellas' own cost is `rainSnowUmbrellas` minus `rainSnow` (docs/UMBRELLAS.md "Tuning" has it too). One 15 s window is noisier than any snow cost; use --runs.
	 * After the windows, a screenshot of a freshly staged, dressed crowd in snow without umbrellas, the camera on one of its guests standing in the open away from the entrance roofs (--follow, default -250), at --zoom (default 70), is
	 * saved (--shot, default opentpw-winter-cost.png in the temp dir). Not a real run afterwards: the guests' fields are written directly.
	 */
	async winterCost(page, options) {
		const state = await page.state();
		if (!state.hud || state.level !== 'jungle')
			throw new Error('The winterCost scenario needs the Jungle park: run it after jungle');
		const result = { crowd: options.crowd, cover: options.cover };
		const measure = async (key, umbrellas) => {
			// rain again at the start of the window, umbrellas as the step wants them
			await page.eval(`opentpwPerf.program.PerfUmbrellaCrowd(0, 30, ${umbrellas})`);
			await sleep(4000);
			result[key] = await page.steady(options.window * 1000);
			result[key].raining = (await page.eval('opentpwPerf.program.PerfRaining()')) ? 1 : 0;
			result[key].drawnUmbrellas = await page.eval('opentpwPerf.program.PerfDrawnUmbrellas()');
			result[key].drawnItems = await page.eval('opentpwPerf.program.PerfDrawnWinterItems()');
		};
		result.staged = JSON.parse(await page.eval(`opentpwPerf.program.PerfUmbrellaCrowd(${options.crowd}, 30, false)`));
		if (!(await page.eval('opentpwPerf.program.PerfSnow(0)')))
			throw new Error('The park has no snow cover (the opentpw.winter-snow extension is off, or the park has no weather yet)');
		await measure('rain', false);
		await page.eval(`opentpwPerf.program.PerfSnow(${options.cover})`);
		await measure('rainSnow', false);
		if (!(await page.eval('opentpwPerf.program.PerfUmbrellas(0, 0)')))
			throw new Error('The park has no umbrella renderer (the opentpw.umbrellas extension is off)');
		await measure('rainSnowUmbrellas', true);
		if (!(await page.eval('opentpwPerf.program.PerfWinter(0, 0, 0)')))
			throw new Error('The park has no warm item renderer (the opentpw.winter extension is off)');
		await measure('rainSnowDressed', true);
		result.farReached = await page.eval('opentpwPerf.program.PerfZoom(300)');
		await measure('rainSnowDressedFar', true);
		// the screenshot comes last: a second crowd is staged without umbrellas (the staged guests of the windows have been walking out of the park for about two minutes), the guests are dressed and the snow is held, and the camera
		// follows the --follow-th guest from the newest down (default -250, the 150th of the new crowd: the crowd starts in a blob at the gate under the turnstile roofs, and after about 24 s the followed guest stands in the open
		// on the road). A positive --follow counts from the lowest id instead.
		await page.eval('opentpwPerf.program.PerfUmbrellasOff()');
		await page.eval(`opentpwPerf.program.PerfUmbrellaCrowd(${options.crowd}, 30, false)`);
		if (!(await page.eval(`opentpwPerf.program.PerfWinter(${options.zoom ?? 70}, 35, ${options.follow ?? -250})`)))
			throw new Error('The park has no warm item renderer (the opentpw.winter extension is off)');
		await sleep(24000);
		const shot = await page.send('Page.captureScreenshot', { format: 'png' });
		const file = options.shot ?? path.join(os.tmpdir(), 'opentpw-winter-cost.png');
		await fs.writeFile(file, Buffer.from(shot.data, 'base64'));
		result.screenshot = file;
		return result;
	},

	/** [EXT:debug-umbrellas] After jungle: every guest gets an open umbrella, the camera follows the first one, and a screenshot is saved (--shot, default opentpw-umbrellas.png in the temp dir). */
	async umbrellas(page, options) {
		if (!(await page.state()).hud)
			throw new Error('The umbrellas scenario needs a park: run it after jungle');
		const zoom = options.zoom ?? 80;
		await page.call('PerfUmbrellas', zoom, 35);
		// A new park has no guests until the gate has been open for a while: wait until an umbrella is drawn (at most three minutes).
		let drawn = 0;
		for (let waited = 0; waited < 180000 && drawn < 1; waited += 1000) {
			await sleep(1000);
			drawn = await page.eval('opentpwPerf.program.PerfDrawnUmbrellas()');
		}
		await sleep(2000);
		drawn = await page.eval('opentpwPerf.program.PerfDrawnUmbrellas()');
		const shot = await page.send('Page.captureScreenshot', { format: 'png' });
		const file = options.shot ?? path.join(os.tmpdir(), 'opentpw-umbrellas.png');
		await fs.writeFile(file, Buffer.from(shot.data, 'base64'));
		return { zoom, drawnUmbrellas: drawn, screenshot: file };
	},

	/** [EXT:debug-winter] After jungle: every guest gets warm items, the camera follows the first one, and a screenshot is saved (--shot, default opentpw-winter.png in the temp dir). */
	async winter(page, options) {
		if (!(await page.state()).hud)
			throw new Error('The winter scenario needs a park: run it after jungle');
		const zoom = options.zoom ?? 80;
		await page.call('PerfWinter', zoom, 35, options.follow ?? 0);
		// A new park has no guests until the gate has been open for a while: wait until an item is drawn (at most three minutes).
		let drawn = 0;
		for (let waited = 0; waited < 180000 && drawn < 1; waited += 1000) {
			await sleep(1000);
			drawn = await page.eval('opentpwPerf.program.PerfDrawnWinterItems()');
		}
		// let the followed guest walk clear of the entrance before the shot
		await sleep(10000);
		drawn = await page.eval('opentpwPerf.program.PerfDrawnWinterItems()');
		const shot = await page.send('Page.captureScreenshot', { format: 'png' });
		const file = options.shot ?? path.join(os.tmpdir(), 'opentpw-winter.png');
		await fs.writeFile(file, Buffer.from(shot.data, 'base64'));
		return { zoom, drawnWinterItems: drawn, screenshot: file };
	},

	/** [EXT:debug-snow-cover] After jungle: holds the park's settled snow cover (--cover, default 1) and saves a screenshot (--shot, default opentpw-snow.png in the temp dir): the browser build's check that test.shader's snow compiles and draws. */
	async snow(page, options) {
		if (!(await page.state()).hud)
			throw new Error('The snow scenario needs a park: run it after jungle');
		const held = await page.eval(`opentpwPerf.program.PerfSnow(${options.cover})`);
		if (!held)
			throw new Error('The park has no snow cover (the opentpw.winter-snow extension is off, or the park has no weather yet)');
		if (options.zoom != null)
			await page.eval(`opentpwPerf.program.PerfZoom(${options.zoom})`);
		await sleep(4000);
		const shot = await page.send('Page.captureScreenshot', { format: 'png' });
		const file = options.shot ?? path.join(os.tmpdir(), 'opentpw-snow.png');
		await fs.writeFile(file, Buffer.from(shot.data, 'base64'));
		return { cover: options.cover, screenshot: file };
	},

	/** The frames each event of a park session causes, with the game's named work in the slow ones (events.mjs; run after jungle). */
	async events(page, options) {
		return eventsScenario(page, options);
	},

	/**
	 * The lobby's enter-park button in a session that starts with saved parks (run after jungle): the park is left to the lobby, which
	 * writes its automatic checkpoint, the page is loaded again in the same profile, and the first and a second click on the button are
	 * measured, each up to the screen it opens and back.
	 */
	async lobbyEnter(page, options, context) {
		const state = await page.state();
		if (!state.hud || state.level !== 'jungle')
			throw new Error('The lobbyEnter scenario needs the Jungle park: run it after jungle');
		await page.call('PerfOpenPauseMenu');
		await page.waitFor('the pause menu', current => current.hudScreen === 'pause', 10000);
		for (let frame = 0; frame < 3; frame++)
			await page.nextFrame();
		await page.call('PerfClick', 'ExitToLobby');
		await page.waitFor('the lobby', current => current.menu === 'lobby' && !current.hud, 60000);
		// the checkpoint reaches the browser's storage after the lobby shows: a reload too soon finds no saved park to offer
		await sleep(10000);
		await page.send('Page.navigate', { url: context.url });
		await page.waitFor('the page loaded again', current => current.started, 600000);
		await page.waitFor('the lobby', current => current.menu === 'lobby', 120000);
		await sleep(3000);
		const profile = !!options.profile;
		if (profile) {
			await page.send('Profiler.enable');
			await page.send('Profiler.setSamplingInterval', { interval: 250 });
		}
		await page.eval('opentpwPerf.marks = true, opentpwPerf.program.PerfFrameMarks(true), 0');
		const events = {};
		const run = async (name, act, extra) => {
			process.stderr.write(`  ${name}\n`);
			const record = await recordEvent(page, name, act, extra).catch(error => ({ name, missing: true, error: error.message }));
			events[name] = record;
			return record;
		};
		try {
			await lobbyEnterClick(page, run, 'lobby enter park: first click of the session', { profile });
			await lobbyEnterClick(page, run, 'lobby enter park: second click', { profile });
			await lobbyEnterClick(page, run, 'lobby enter park: third click', { profile });
			return { events };
		} finally {
			await page.eval('opentpwPerf.marks = false, opentpwPerf.program.PerfFrameMarks(false), 0').catch(() => {});
		}
	},

	/** The park door button's first and later presses in the fresh Jungle park (events.mjs; run after jungle). */
	async door(page, options) {
		return doorScenario(page, options);
	},

	async buy(page, options) {
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
			// with --shot, the buy window with each row's preview (the depth-tested preview image), after its 500 ms preview delay
			if (options.shot) {
				await new Promise(resolve => setTimeout(resolve, 1500));
				const shot = await page.send('Page.captureScreenshot', { format: 'png' });
				await fs.writeFile(options.shot.replace(/\.png$/i, '') + `-${name.replace(/[^A-Za-z0-9]+/g, '-').toLowerCase()}.png`, Buffer.from(shot.data, 'base64'));
			}
		}
		await page.call('PerfKey', 'Back');
		return result;
	},

	async revisit(page, options, context) {
		if (options.keep === 'off')
			throw new Error('The revisit needs files kept on the first visit: pass --keep lazy or --keep all');
		// The same profile, without ?data: only the kept files can start the game.
		const query = new URLSearchParams({ perf: '1', keep: options.keep, perfSeed: context.seed });
		const begin = Date.now();
		await page.send('Page.navigate', { url: `${context.origin}/index.html?${query}` });
		for (;;) {
			const shown = await page.eval("document.getElementById('kept') ? !document.getElementById('kept').hidden : false").catch(() => false);
			if (shown)
				break;
			if (Date.now() - begin > 60000)
				throw new Error(`No Play button after 60 s: ${await page.eval("document.getElementById('status')?.textContent").catch(() => '?')}`);
			await sleep(100);
		}
		const keptText = await page.eval("document.getElementById('keptSize').textContent");
		const playShownMs = Date.now() - begin;
		const click = Date.now();
		await page.eval("document.getElementById('play').click()");
		await page.waitFor('the first frame', state => state.started, 120000);
		const firstFrameMs = Date.now() - click;
		await page.waitFor('the lobby', state => state.menu === 'lobby', 60000);
		const lobbyMs = Date.now() - click;
		await sleep(2000);
		await page.call('PerfClick', 'enterPark');
		await page.waitFor('the game mode menu', current => current.menu === 'gameMode' || current.menu === 'themeStart', 10000);
		const park = Date.now();
		// The consent for new progress is answered on the way if the kept profile asks for it.
		const entering = page.enterFullSimulation('jungle', 120000).catch(error => error);
		for (;;) {
			const outcome = await Promise.race([entering, sleep(100).then(() => null)]);
			if (outcome instanceof Error)
				throw outcome;
			if (typeof outcome === 'number')
				break;
			const asked = await page.eval("!document.getElementById('setup').hidden && /choose your Theme Park World folder/i.test(document.getElementById('status').textContent)").catch(() => false);
			if (asked)
				throw new Error('Jungle asked for the folder: its files were not kept');
			if (Date.now() - park > 125000)
				throw new Error('Jungle did not open from the kept files within 120 s');
		}
		return { kept: keptText, playShownMs, firstFrameMs, lobbyMs, jungleReadyMs: Date.now() - park };
	},

	async reload(page, options, context) {
		if (!options.upstream)
			throw new Error('The reload scenario needs --upstream: it reads the transport of the production server');
		await page.send('Page.navigate', { url: context.url });
		await page.waitFor('the reloaded page', state => state.started, 120000);
		return { requests: context.server.log.length - context.server.marks.at(-1)[1] };
	},

	async transfer(page, options, context) {
		if (!options.upstream)
			throw new Error('The transfer scenario needs --upstream: it reads the transport of the production server');
		const { log, marks } = context.server;
		const phaseOf = index => marks.filter(([, start]) => start <= index).at(-1)?.[0] ?? 'start';
		const files = log.map((entry, index) => ({ phase: phaseOf(index), ...entry }));
		const totals = {};
		for (const file of files) {
			const total = totals[file.phase] ??= { requests: 0, notModified: 0, bytes: 0 };
			total.requests++;
			total.notModified += file.status === 304 ? 1 : 0;
			total.bytes += file.bytes;
		}
		return { totals, files };
	},

	async digest(page) {
		const state = await page.state();
		if (!state.level)
			throw new Error('The digest needs the park\'s files: run it after jungle');
		// One synchronous call: the page draws nothing meanwhile, so it gets a long deadline.
		return JSON.parse(await page.eval(`opentpwPerf.program.PerfDigest(${JSON.stringify(state.level)})`, 900000));
	},

	/**
	 * The cross-runtime digest of every theme (docs/DETERMINISM.md, "Browser and native"; CrossRuntimeDigest): the same work as the desktop's
	 * --determinism-digest, to compare value by value with tools/web-determinism/compare-digest.mjs. After jungle: each other theme's files are
	 * copied first. Minutes per theme; the page draws nothing meanwhile.
	 */
	async determinism(page) {
		const state = await page.state();
		if (!state.level)
			throw new Error('The determinism digest needs the game files: run it after jungle');
		const themes = [];
		for (const theme of ['jungle', 'hallow', 'space', 'fantasy']) {
			if (!await page.eval(`opentpwPerf.program.PerfLevelLoaded(${JSON.stringify(theme)})`)) {
				await page.eval(`opentpwPerf.loadLevel(${JSON.stringify(theme)})`);
				const deadline = Date.now() + 300000;
				while (!await page.eval(`opentpwPerf.program.PerfLevelLoaded(${JSON.stringify(theme)})`)) {
					if (Date.now() > deadline)
						throw new Error(`The files of ${theme} were not copied within 300 s`);
					await sleep(500);
				}
			}
			themes.push(JSON.parse(await page.eval(`opentpwPerf.program.PerfDeterminismDigest(${JSON.stringify(theme)}, true)`, 3600000)));
		}
		return { themes };
	},

	async switch(page, options) {
		const state = await page.state();
		if (!state.hud || state.level !== 'jungle')
			throw new Error('The switch scenario starts in Jungle: run it after jungle');
		const steps = [];
		// Each point after a full collection, so it shows live memory; wasm memory only grows, so it shows the peak so far.
		const record = async name => {
			// A sound sample decoded in the browser (cooperatively, in batches between frames) holds its growing PCM buffer and its MPEG input until it
			// ends; a point taken during one counts them (2-6 MB in the lobby, whose music segments are long), so wait for none to be in flight.
			const settleBegin = Date.now();
			while ((await page.eval('opentpwPerf.counters().soundDecodesInFlight')) > 0 && Date.now() - settleBegin < 60000)
				await sleep(250);
			if ((await page.eval('opentpwPerf.counters().soundDecodesInFlight')) > 0)
				console.warn(`switch ${name}: a sound decode was still in flight after 60 s; this point counts its buffers`);
			await page.eval('opentpwPerf.program.PerfCollect()');
			const memory = await page.memory();
			const stale = memory.levelsAlive > (name.startsWith('lobby') ? 0 : 1) ? JSON.parse(await page.eval('opentpwPerf.program.PerfStaleLevelPaths()', 120000)) : [];
			const textureGroups = JSON.parse(await page.eval('opentpwPerf.program.PerfTextureGroups()'));
			const textureBytes = JSON.parse(await page.eval('opentpwPerf.program.PerfTextureBytes()'));
			const roots = name.startsWith('lobby') ? JSON.parse(await page.eval('opentpwPerf.program.PerfMemoryRoots()', 180000)) : undefined;
			// the framework's own statics (the shared array pools among them), which the game's root walk does not see
			const runtimeRoots = name.startsWith('lobby') && options.runtimeRoots ? JSON.parse(await page.eval('opentpwPerf.program.PerfRuntimeRoots()', 300000)) : undefined;
			const objectCounts = name.startsWith('lobby') && options.runtimeRoots ? JSON.parse(await page.eval('opentpwPerf.program.PerfObjectCounts()', 300000)) : undefined;
			const assetBytes = JSON.parse(await page.eval('opentpwPerf.program.PerfAssetBytes()', 180000));
			// what the sound mixer reaches, by the field holding the bytes, and its object counts by type
			const audio = JSON.parse(await page.eval('opentpwPerf.program.PerfAudioBytes()', 180000));
			// the same 20 seconds later (a decode in flight at the point has ended by then)
			const audioLater = options.audioLater ? (await sleep(20000), JSON.parse(await page.eval('opentpwPerf.program.PerfAudioBytes()', 180000))) : undefined;
			steps.push({ step: name, ...memory, textureGroups, textureBytes, assetBytes, audio, ...(audioLater ? { audioLater } : {}), ...(roots ? { roots } : {}), ...(runtimeRoots ? { runtimeRoots } : {}), ...(objectCounts ? { objectCounts } : {}), ...(stale.length ? { staleLevelPaths: stale } : {}) });
		};
		const toLobby = async () => {
			await page.call('PerfOpenPauseMenu');
			await page.waitFor('the pause menu', current => current.hudScreen === 'pause', 10000);
			// The pause menu lays its lines out from their measured text on its first frames: a click before that misses them
			for (let frame = 0; frame < 3; frame++)
				await page.nextFrame();
			await page.call('PerfClick', 'ExitToLobby');
			// A checkpoint that cannot be written shows its failure dialog instead of leaving: say so rather than time out
			const left = await page.waitFor('the lobby', current => (current.menu === 'lobby' && !current.hud) || current.hudScreen === 'automaticFailed', 60000);
			if (left.hudScreen === 'automaticFailed')
				throw new Error(`Exit To Lobby ended in the automatic checkpoint failure dialog; state ${JSON.stringify(left)}`);
			await sleep(2000);
		};
		const enterPark = async island => {
			await page.call('PerfClick', 'enterPark');
			await page.waitFor('the game mode menu', current => current.menu === 'gameMode' || current.menu === 'themeStart', 10000);
			await page.enterFullSimulation(island);
			await sleep(3000);
		};
		await page.eval('opentpwPerf.program.PerfTrackTextureOrigins(true)');
		await record('jungle');
		await toLobby();
		await record('lobby');
		await page.call('PerfClick', 'nextIsland');
		const other = (await page.waitFor('the next island', current => current.island && current.island !== 'jungle', 10000)).island;
		let second = other;
		try {
			await enterPark(other);
		} catch (error) {
			// A fresh profile has not earned the keys of the next island (Full Simulation asks for them): leave the dialog and the
			// game mode screen, and Jungle is the second park instead, so the switch through the lobby is still measured.
			if (!/progress failure dialog/.test(error.message))
				throw error;
			await page.call('PerfClick', 'choice0');
			await page.waitFor('the game mode menu', current => current.menu === 'gameMode' && current.hudScreen == null, 10000);
			await page.nextFrame();
			await page.call('PerfKey', 'Back');
			await page.waitFor('the lobby', current => current.menu === 'lobby', 10000);
			await page.nextFrame();
			await page.call('PerfKey', 'Left');
			await page.waitFor('the jungle island', current => current.island === 'jungle', 10000);
			second = 'jungle (next island locked)';
			await enterPark('jungle');
		}
		await record(second);
		await toLobby();
		await record('lobby again');
		if (second === other) {
			await page.call('PerfKey', 'Left');
			await page.waitFor('the jungle island', current => current.island === 'jungle', 10000);
		}
		await enterPark('jungle');
		await record('jungle again');
		for (let visit = 4; visit <= options.visits; visit++) {
			await toLobby();
			await record(`lobby ${visit - 1}`);
			await page.waitFor('the jungle island', current => current.island === 'jungle', 10000);
			await enterPark('jungle');
			await record(`jungle ${visit}`);
		}
		return { steps };
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

/** One session in a fresh Chrome profile: the scenarios in order, their results and the page's uncaught errors. */
async function runSession(options, webRoot, gameRoot) {
	const report = { date: new Date().toISOString(), results: {} };
	let server, profile, chrome, cdp;
	try {
		server = await startServer(webRoot, gameRoot, options.upstream);
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
		if (options.frameCap)
			seed['/config/display.json'] = JSON.stringify({ FrameCap: options.frameCap });
		const encodedSeed = Buffer.from(JSON.stringify(seed)).toString('base64');
		const query = new URLSearchParams({ perf: '1', data: '/game/', keep: options.keep, perfSeed: encodedSeed, ...(options.gc ? { gc: options.gc } : {}) });
		const context = { navigationStart: Date.now(), origin: server.origin, seed: encodedSeed, server, url: `${server.origin}/index.html?${query}` };
		await page.send('Page.navigate', { url: context.url });
		report.renderer = await page.eval(`(() => { const gl = document.createElement('canvas').getContext('webgl2'); const info = gl?.getExtension('WEBGL_debug_renderer_info');
			return info ? gl.getParameter(info.UNMASKED_RENDERER_WEBGL) : null; })()`).catch(() => null);
		for (const name of options.scenarios) {
			if (!scenarios[name]) {
				report.results[name] = { error: 'unknown scenario' };
				continue;
			}
			server.marks.push([name, server.log.length]);
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
		muted: !options.music, frameCap: options.frameCap, scenarios: options.scenarios, chrome: sessions[0].chrome, renderer: sessions[0].renderer,
		results: options.runs > 1 ? medianResults(sessions.map(session => session.results)) : sessions[0].results,
		...(options.runs > 1 ? { runs: sessions } : { pageErrors: sessions[0].pageErrors }),
	};
	// The digest's hashes must agree between runs of one build; a difference means nondeterminism, not a slower build.
	const digests = new Set(sessions.map(session => session.results.digest).filter(entry => entry && !entry.error)
		.map(entry => `${entry.textureHash}/${entry.simulationHash}`));
	if (digests.size > 1)
		report.results.digest = { ...report.results.digest, error: `runs disagree: ${[...digests].join(', ')}` };
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
