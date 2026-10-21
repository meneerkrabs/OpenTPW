// The event-hitch scenario (docs/BROWSER-PERFORMANCE.md, "Event hitches"): drives a running park through the events a player causes and records,
// per event, the frames that follow it, the slowest of them with the game's named work of that frame (FrameMarks) and, with --profile, the
// functions a CPU profile spent that frame in. Needs the park of `jungle` (run it first) and a build with the hooks of Program.Perf.cs.
import { sleep, summarizeFrames, percentile } from './lib.mjs';

const round = value => Math.round(value * 100) / 100;

let symbols = null;

/**
 * Names the AOT module's functions (the profile shows them as wasm-function[index]) from an Emscripten symbol map, "index:name" per line:
 * a publish with -p:WasmEmitSymbolMap=true writes it as obj/Release/<framework>/wasm/for-publish/dotnet.native.js.symbols (--symbols).
 */
export function useSymbols(text) {
	symbols = new Map();
	for (const line of text.split('\n')) {
		const colon = line.indexOf(':');
		if (colon > 0)
			symbols.set(Number(line.slice(0, colon)), line.slice(colon + 1).trim());
	}
}

/** The name of a CPU profile node's function, with its script and line. */
const functionName = node => {
	const frame = node?.callFrame;
	if (!frame)
		return '(unknown)';
	const wasm = symbols && /^wasm-function\[(\d+)\]$/.exec(frame.functionName);
	if (wasm && symbols.has(Number(wasm[1])))
		return symbols.get(Number(wasm[1])).replace(/^OpenTPW_Web_/, '');
	return `${frame.functionName || '(anonymous)'}${frame.url ? ` ${frame.url.split('/').pop()}:${frame.lineNumber}` : ''}`;
};

/**
 * Self time per function of the samples between the page times in windows ([begin, end] ms), the largest first. With `depth`, the time is
 * keyed by the sampled function and its `depth - 1` nearest callers instead ("leaf < caller < ..."), which names the code behind a hot
 * runtime helper (the Emscripten file system's lookupPath, say, under the system call that reached it). With `owners`, the callers kept are
 * the game's own functions only (named with --symbols), so the time of the runtime and the system calls goes to the game code that asked for it.
 */
export function attributeProfile(profile, pageNowAtStart, windows, top = 10, depth = 1, owners = false) {
	const byId = new Map(profile.nodes.map(node => [node.id, node]));
	const parents = new Map();
	for (const node of profile.nodes)
		for (const child of node.children ?? [])
			parents.set(child, node.id);
	const totals = new Map();
	let time = profile.startTime;
	profile.samples.forEach((id, index) => {
		time += profile.timeDeltas[index];
		const pageTime = pageNowAtStart + (time - profile.startTime) / 1000;
		const delta = (profile.timeDeltas[index + 1] ?? 0) / 1000;
		if (!windows.some(([begin, end]) => pageTime >= begin && pageTime < end))
			return;
		const names = [];
		for (let current = id; current != null && names.length < depth; current = parents.get(current)) {
			const node = byId.get(current);
			if (node?.callFrame?.functionName === '(root)')
				break;
			const name = functionName(node);
			// owners: the game's own frames only (the sampled function first), which names the game code a runtime helper's time is spent for
			if (!owners || names.length === 0 || name.startsWith('OpenTPW_'))
				names.push(name);
		}
		const name = names.join(' < ') || '(unknown)';
		totals.set(name, (totals.get(name) ?? 0) + delta);
	});
	return [...totals].sort((a, b) => b[1] - a[1]).slice(0, top).map(([name, ms]) => ({ name, ms: round(ms) }));
}

/** True for the page's frame callback (main.js `frame`, which calls Program.Frame), the root of every frame's samples. */
const isFrameCallback = frame => frame?.functionName === 'frame' && /(^|\/)main(\.[\w-]+)?\.js$/.test(frame.url ?? '');

/**
 * The frames a CPU profile saw: runs of consecutive samples under the page's frame callback, as [begin, end] page times. Samples taken
 * outside the callback (a garbage collection at the top of the stack, say) end nothing for `gapMs`; a longer pause ends the run.
 */
export function profileFrames(profile, pageNowAtStart, gapMs = 1) {
	const byId = new Map(profile.nodes.map(node => [node.id, node]));
	const parents = new Map();
	for (const node of profile.nodes)
		for (const child of node.children ?? [])
			parents.set(child, node.id);
	const inFrame = new Map();
	const underFrame = id => {
		if (inFrame.has(id))
			return inFrame.get(id);
		let result = false;
		for (let current = id; current != null; current = parents.get(current))
			if (isFrameCallback(byId.get(current)?.callFrame)) {
				result = true;
				break;
			}
		inFrame.set(id, result);
		return result;
	};
	const runs = [];
	let run = null;
	let time = profile.startTime;
	profile.samples.forEach((id, index) => {
		time += profile.timeDeltas[index];
		const pageTime = pageNowAtStart + (time - profile.startTime) / 1000;
		if (!underFrame(id))
			return;
		const end = pageTime + (profile.timeDeltas[index + 1] ?? 0) / 1000;
		if (run && pageTime - run[1] <= gapMs)
			run[1] = end;
		else
			runs.push(run = [pageTime, end]);
	});
	return runs;
}

/**
 * How much later the CPU profile's clock reads than the page's (profile page time = page time + offset), from the frames themselves.
 * The two clocks are only related through one performance.now() read after Profiler.start, which can be off by a frame or more
 * (28 ms in the Jungle start). The busy times of the frame callback in the profile (profileFrames) are slid over the page's recorded
 * frames (`frames`: [begin, ms] each) in steps of `binMs`, up to `searchMs` either way, and the shift where the two agree on the most
 * time wins. Runs of back-to-back frames merge in the profile, but their pattern of lengths and gaps is what lines up. Null without
 * frames or samples to compare.
 */
export function profileClockOffset(runs, pageNowAtStart, frames, { binMs = 0.5, searchMs = 500 } = {}) {
	if (!runs.length || !frames.length)
		return null;
	const first = Math.min(...frames.map(([begin]) => begin));
	const last = Math.max(...frames.map(([begin, ms]) => begin + ms));
	const origin = Math.min(first, runs[0][0]) - searchMs;
	const length = Math.ceil((Math.max(last, runs.at(-1)[1]) + searchMs - origin) / binMs) + 1;
	const fill = (spans, into) => {
		for (const [begin, end] of spans)
			for (let bin = Math.max(0, Math.floor((begin - origin) / binMs)); bin < Math.min(length, Math.ceil((end - origin) / binMs)); bin++)
				into[bin] = 1;
	};
	const profileBusy = new Uint8Array(length);
	fill(runs, profileBusy);
	const pageBusy = new Uint8Array(length);
	fill(frames.map(([begin, ms]) => [begin, begin + ms]), pageBusy);
	// only the time the page recorded frames over is compared
	const from = Math.floor((first - origin) / binMs), to = Math.ceil((last - origin) / binMs);
	const steps = Math.round(searchMs / binMs);
	let best = null;
	for (let shift = -steps; shift <= steps; shift++) {
		let agree = 0;
		for (let bin = from; bin < to; bin++) {
			const shifted = bin + shift;
			if (shifted >= 0 && shifted < length && profileBusy[shifted] === pageBusy[bin])
				agree++;
		}
		if (!best || agree > best.agree || (agree === best.agree && Math.abs(shift) < Math.abs(best.shift)))
			best = { agree, shift };
	}
	return round(best.shift * binMs);
}

/**
 * The CPU profile's view of one slow frame (begin, ms in page time), read over the frame's window moved by the clock offset
 * (profileClockOffset; none when null): the hottest functions, the stacks six deep and the game functions behind the runtime's time.
 */
export function profileOfFrame(profile, pageNowAtStart, offsetMs, begin, ms, top = { functions: 10, stacks: 8, owners: 12 }) {
	const window = [begin + (offsetMs ?? 0), begin + (offsetMs ?? 0) + ms];
	return {
		profileOffsetMs: offsetMs,
		functions: attributeProfile(profile, pageNowAtStart, [window], top.functions),
		stacks: attributeProfile(profile, pageNowAtStart, [window], top.stacks, 6),
		owners: attributeProfile(profile, pageNowAtStart, [window], top.owners, 4, true),
	};
}

/**
 * One event: marks the page, runs act(), lets the park run for settleMs and returns what the frames and long tasks in between show.
 * A frame of at least `slowMs` is reported with its marks (and, when profiling, the functions it spent time in).
 */
export async function recordEvent(page, name, act, { settleMs = 2000, slowMs = 20, profile = false } = {}) {
	await page.eval('opentpwPerf.take(), opentpwPerf.takeSkipped(), 0');
	if (profile) {
		await page.send('Profiler.start');
	}
	const profileStart = profile ? await page.eval('performance.now()') : 0;
	const start = await page.mark();
	await act();
	await sleep(settleMs);
	const end = await page.mark();
	let captured = null;
	if (profile)
		captured = (await page.send('Profiler.stop', undefined, 60000)).profile;
	const frames = end.frames;
	const tasks = (await page.eval('globalThis.__opentpwLongTasks.splice(0)')).filter(([startTime]) => startTime >= start.now && startTime < end.now);
	const summary = summarizeFrames(frames, end.now - start.now);
	const offset = captured ? profileClockOffset(profileFrames(captured, profileStart), profileStart, frames) : null;
	const slow = frames.filter(([, duration]) => duration >= slowMs).sort((a, b) => b[1] - a[1]).slice(0, 4).map(([begin, duration, marks]) => {
		const entry = { atMs: round(begin - start.now), ms: round(duration), marks: marks ?? {} };
		if (captured)
			Object.assign(entry, profileOfFrame(captured, profileStart, offset, begin, duration));
		return entry;
	});
	return {
		name, frames: summary.frames, maxMs: summary.maxMs, p95Ms: summary.p95Ms, over20Ms: frames.filter(([, duration]) => duration >= 20).length,
		over50Ms: summary.over50Ms, longTasks: tasks.length, longestTaskMs: tasks.length ? round(Math.max(...tasks.map(([, duration]) => duration))) : 0,
		slow,
	};
}

const WINDOWS = ['buy', 'finance', 'research', 'map', 'loans', 'staff', 'parkInfo', 'entryPrice', 'staffCosts', 'attractions', 'visitors'];

/** The ordered list of events (name, action) the scenario runs; exported so tests of the harness can check it. */
export function eventPlan(options) {
	const plan = [];
	for (const window of WINDOWS) {
		plan.push([`window ${window} (first open)`, page => page.call('PerfOpenWindow', window), { settleMs: 1500 }]);
		plan.push([`window ${window} (close)`, page => page.call('PerfKey', 'Back'), { settleMs: 700 }]);
		plan.push([`window ${window} (reopen)`, page => page.call('PerfOpenWindow', window), { settleMs: 1000 }]);
		plan.push([`window ${window} (close again)`, page => page.call('PerfKey', 'Back'), { settleMs: 500 }]);
	}
	return plan;
}

/**
 * The park door button of the entry price window clicked three times (open, close, open again) in a park whose door is still closed: the first
 * opening runs the gate script's open, opens the attractions and starts admissions, the arrivals and the advisor's line. The clicks go through the
 * HUD's input like a player's. Each press gets its frames, worst frame, p95, the slow frames' marks (and profile) and `stateMs`: the wall time
 * from the click until the economy reports the new door state (it includes the harness's polling, a few ms), and for an opening `firstGuestMs`:
 * the time until a new guest is in the park, at most `waitSeconds`. Records into `events` with `run`.
 */
export async function doorEvents(page, run, waitSeconds = 20) {
	if (!await page.call('PerfOpenWindow', 'entryPrice'))
		throw new Error('the entry price window could not be opened');
	await sleep(1500);
	const presses = [['park door: first click (opens)', true, waitSeconds * 1000], ['park door: second click (closes)', false, 3000], ['park door: third click (opens again)', true, waitSeconds * 1000]];
	try {
		for (const [name, opens, settleMs] of presses) {
			let stateMs = null, firstGuestMs = null;
			const record = await run(name, async () => {
				if (await page.eval('opentpwPerf.program.PerfParkOpen()') === opens)
					throw new Error(`the park door is already ${opens ? 'open' : 'closed'}`);
				const guests = await page.eval('opentpwPerf.program.PerfGuests()');
				const clicked = Date.now();
				if (!await page.call('PerfClick', 'door'))
					throw new Error('the park door button is not shown');
				for (const begin = Date.now(); Date.now() - begin < 3000; await sleep(10))
					if (await page.eval('opentpwPerf.program.PerfParkOpen()') === opens) {
						stateMs = Date.now() - clicked;
						break;
					}
				// an opening: the time until the first new guest is in the park (the arrivals' own schedule; at most the settle time)
				if (opens)
					for (const begin = Date.now(); Date.now() - begin < settleMs; await sleep(50))
						if (await page.eval('opentpwPerf.program.PerfGuests()') > guests) {
							firstGuestMs = Date.now() - clicked;
							break;
						}
			}, { settleMs: opens ? 1000 : settleMs });
			record.stateMs = stateMs;
			record.firstGuestMs = firstGuestMs;
		}
	} finally {
		await page.call('PerfKey', 'Back').catch(() => {});
	}
}

/** The park door alone (`--scenarios start,jungle,door`): the presses of doorEvents in the fresh Jungle park, with marks and --profile. */
export async function doorScenario(page, options) {
	const state = await page.state();
	if (!state.hud || state.level !== 'jungle')
		throw new Error('The door scenario needs the Jungle park: run it after jungle');
	const profile = !!options.profile;
	if (profile) {
		await page.send('Profiler.enable');
		await page.send('Profiler.setSamplingInterval', { interval: 250 });
	}
	await page.eval('opentpwPerf.marks = true, opentpwPerf.program.PerfFrameMarks(true), 0');
	const events = {};
	const run = async (name, act, extra) => {
		process.stderr.write(`  ${name}\n`);
		const record = await recordEvent(page, name, act, { profile, ...extra }).catch(error => ({ name, missing: true, error: error.message }));
		events[name] = record;
		return record;
	};
	try {
		await run('steady before the door', () => sleep(0), { settleMs: 3000 });
		await doorEvents(page, run, options.doorWaitSeconds);
		return { events };
	} finally {
		await page.eval('opentpwPerf.marks = false, opentpwPerf.program.PerfFrameMarks(false), 0').catch(() => {});
	}
}

/**
 * One click on the lobby's enter-park button (the theme start, or the game mode screen when the island has no park to continue), then
 * back to the lobby. Records the frames and `screenMs`: the wall time from the click until the game reports the next screen (it includes
 * the harness's polling, a few ms). Records into `events` with `run`.
 */
export async function lobbyEnterClick(page, run, name, { profile = false } = {}) {
	let screenMs = null, screen = null;
	const record = await run(name, async () => {
		const clicked = Date.now();
		if (!await page.eval('opentpwPerf.program.PerfClick("enterPark")'))
			throw new Error('the enter-park button is not shown');
		for (const begin = Date.now(); Date.now() - begin < 10000; await sleep(5)) {
			const state = await page.state();
			if (state.menu === 'themeStart' || state.menu === 'gameMode') {
				screenMs = Date.now() - clicked;
				screen = state.menu;
				break;
			}
		}
	}, { settleMs: 1500, profile });
	record.screenMs = screenMs;
	record.screen = screen;
	await page.eval('opentpwPerf.program.PerfKey("Back")');
	await page.waitFor('the lobby', current => current.menu === 'lobby', 10000);
	await sleep(1500);
	return record;
}

/** Runs the whole scenario; returns { events: { name: record }, soak: record }. */
export async function eventsScenario(page, options) {
	const state = await page.state();
	if (!state.hud || state.level !== 'jungle')
		throw new Error('The events scenario needs the Jungle park: run it after jungle');
	const profile = !!options.profile;
	if (profile) {
		await page.send('Profiler.enable');
		await page.send('Profiler.setSamplingInterval', { interval: 250 });
	}
	await page.eval('opentpwPerf.marks = true, opentpwPerf.program.PerfFrameMarks(true), 0');
	const events = {};
	const run = async (name, act, extra) => {
		process.stderr.write(`  ${name}\n`);
		// an event whose hook is missing or refuses leaves a note; the others still run
		const record = await recordEvent(page, name, act, { profile, ...extra }).catch(error => ({ name, missing: true, error: error.message }));
		events[name] = record;
		return record;
	};
	const settle = ms => sleep(ms);
	try {
		await run('steady (normal speed)', () => sleep(0), { settleMs: 8000 });
		// advisor lines: the park's own welcome has played by now; post some advice and see whether it is said
		for (const message of options.advice ?? [])
			await run(`advisor advice ${message}`, async () => { await page.call('PerfAdvisorAdvice', message); }, { settleMs: 6000 });
		for (const [name, act, extra] of eventPlan(options))
			await run(name, () => act(page), extra);
		// builds: the first ride and shop placed, each previewed first as the buy window does
		await page.call('PerfCheat', 'money', 0).catch(() => {});
		await settle(1500);
		const placements = [['Rides', 'Belly Bounce'], ['Shops', 'Balloon Shop'], ['Shops', 'Burger Shop']];
		await page.call('PerfOpenWindow', 'buy');
		await settle(1500);
		for (const [category, name] of placements) {
			const items = JSON.parse(await page.eval(`opentpwPerf.program.PerfBuyItems(${JSON.stringify(category)})`));
			const item = items.find(entry => entry.name === name);
			if (!item) {
				events[`select ${name}`] = { missing: true };
				continue;
			}
			await run(`buy select ${name}`, () => page.call('PerfShowBuyItem', category, item.archive), { settleMs: 1500 });
			await run(`build ${name}`, async () => { await page.eval(`opentpwPerf.program.PerfPlaceObject(${JSON.stringify(category)}, ${JSON.stringify(item.archive)})`); }, { settleMs: 3000 });
		}
		await page.call('PerfKey', 'Back').catch(() => {});
		await settle(500);
		for (const [label, kind, argument] of [['rain starts', 'weather', 1], ['thunderstorm starts', 'weather', 2], ['weather clears', 'weather', 0], ['bus arrival', 'arrival', 0]])
			await run(label, async () => { await page.eval(`opentpwPerf.program.PerfCheat(${JSON.stringify(kind)}, ${argument})`); }, { settleMs: 10000 });
		for (const mode of ['dusk', 'night', 'day'])
			await run(`evening ${mode}`, async () => { await page.eval(`opentpwPerf.program.PerfEvening(${JSON.stringify(mode)})`); }, { settleMs: 3000 });
		await page.eval('opentpwPerf.program.PerfEvening("on")');
		// research: every category at full effort, the points of many researchers at once, with the park's events (message tube, advisor)
		await run('research completes', async () => { await page.eval('opentpwPerf.program.PerfResearch(100000)'); }, { settleMs: 6000 });
		// the autosave: its capture and slices, made due now
		await run('autosave (capture, slices)', async () => { await page.eval('opentpwPerf.program.PerfAutosaveDue()'); }, { settleMs: 6000 });
		// month and year ends: the economy runs to just before the boundary (the park's guests and rides do not run meanwhile), then the park runs at the fastest speed across it
		await page.call('PerfSpeed', 4);
		await page.eval('opentpwPerf.program.PerfSkipToBoundary("month", 45)');
		await run('month end', () => sleep(0), { settleMs: 4000 });
		await page.eval('opentpwPerf.program.PerfSkipToBoundary("year", 45)');
		await run('year end', () => sleep(0), { settleMs: 4000 });
		await page.call('PerfSpeed', 1);
		// a coaster station and its circuit built in one go (first use of the coaster's models and track mesh), then a crowd
		await run('coaster built (station and circuit)', async () => { await page.eval('opentpwPerf.program.PerfBuildCoaster(true)'); }, { settleMs: 4000 });
		await run(`crowd of ${options.crowd} guests added`, async () => { await page.eval(`opentpwPerf.program.PerfCrowd(${options.crowd})`); }, { settleMs: 3000 });
		// a soak at the fastest speed: month ends, arrivals, researchers, thoughts, ride runs and advisor lines as they happen
		await page.call('PerfSpeed', 4);
		const guests = await page.eval('opentpwPerf.program.PerfGuests()');
		const soak = await recordEvent(page, `soak at the fastest speed (${options.soakSeconds} s, ${guests} guests)`, () => sleep(0), { settleMs: options.soakSeconds * 1000, slowMs: 20, profile });
		await page.call('PerfSpeed', 1);
		// the park door last, so the events above keep the closed park they were measured in
		if (!await page.eval('opentpwPerf.program.PerfParkOpen?.() ?? true'))
			await doorEvents(page, run);
		return { events, soak };
	} finally {
		await page.eval('opentpwPerf.marks = false, opentpwPerf.program.PerfFrameMarks(false), 0').catch(() => {});
	}
}
