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
	const slow = frames.filter(([, duration]) => duration >= slowMs).sort((a, b) => b[1] - a[1]).slice(0, 4).map(([begin, duration, marks]) => {
		const entry = { atMs: round(begin - start.now), ms: round(duration), marks: marks ?? {} };
		if (captured) {
			entry.functions = attributeProfile(captured, profileStart, [[begin, begin + duration]]);
			entry.stacks = attributeProfile(captured, profileStart, [[begin, begin + duration]], 8, 6);
			entry.owners = attributeProfile(captured, profileStart, [[begin, begin + duration]], 12, 4, true);
		}
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
		return { events, soak };
	} finally {
		await page.eval('opentpwPerf.marks = false, opentpwPerf.program.PerfFrameMarks(false), 0').catch(() => {});
	}
}
