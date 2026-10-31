#!/usr/bin/env node
// The cross-runtime determinism check (docs/DETERMINISM.md, "Browser and native"): runs the asset-free unit tests that pin simulation
// digests inside the browser build, in headless and muted Chrome, so the WebAssembly runtime (Mono AOT as published, or the
// interpreter) must compute the bits the desktop tests assert. The page must be published with -p:OpenTpwDeterminismPins=true, which
// compiles the tests into it (the published page never carries them):
//
//   dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -p:RunAOTCompilation=true -p:OpenTpwDeterminismPins=true -o out/web-pins
//   node tools/web-determinism/run.mjs out/web-pins/wwwroot [--classes A,B] [--report report.json] [--chrome path] [--measure]
//
// Without --classes it runs the pin classes below. It prints one line per test and a summary; it exits 1 when a test fails (a pin the
// browser does not reproduce) unless --measure is given, which only reports. No game data is used.
import { promises as fs } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { Cdp, launchChrome, sleep, startServer, stopChrome } from '../web-perf/lib.mjs';

/** The test classes whose tests pin simulation digests or exact simulation values without game data. */
export const PIN_CLASSES = [
	'DetMathTests', 'DeterminismTests', 'WorldRngTests', 'GuestMeasurementTests', 'WinterWeatherTests', 'WinterAlwaysTests',
	'PoolSlideActivityTests', 'PathBuilderTests', 'CheatsRideVehicleTests', 'CheatsExtensionTests', 'ParkWeatherTests',
	'CoasterTests', 'CoasterCollisionTests', 'CoasterRollTests', 'CoasterDispatchTests', 'CoasterTrainLayoutTests', 'CoasterStationTests',
	'CoasterTagKnotTests', 'CoasterRatingGoldenTests', 'CoasterX87GoldenTests', 'CoasterSaveTests', 'OriginalCoasterImportTests', 'TourControllerTests', 'BumpModuleTests', 'BumpControllerTests', 'BumperControllerTests',
	'TrackSurfaceTests', 'TrackCircuitTests', 'PoolsTests', 'PoolsAttractionsTests', 'PoolsSwimSessionTests',
	'PoolSwimmingTests', 'PoolSlidesTests', 'StaffWorldTests', 'StaffSteeringTests', 'LoansSummaryTests', 'BankWordsTests', 'ParkEconomyTests', 'MonoInterpreterTryFinallyProbeTests',
];

/** Tests of those classes that read the repository's own files (sources, shaders), which the page does not have: skipped, with the reason. */
export const SKIPPED = new Map([
	['WorldRngTests.PresentationCodeNeverReachesTheWorldGenerator', 'scans the repository sources'],
	['PoolsAttractionsTests.TheWaterSurfaceRisesWithTheWaveAndTheSwimmersRideIt', 'reads the repository shaders'],
]);

function parseArguments(argv) {
	const options = { webRoot: null, classes: PIN_CLASSES, report: null, chrome: process.env.CHROME ?? null, measure: false, timeoutMs: 30 * 60000 };
	for (let index = 0; index < argv.length; index++) {
		const argument = argv[index];
		if (argument === '--classes') options.classes = argv[++index].split(',').map(name => name.trim()).filter(Boolean);
		else if (argument === '--report') options.report = argv[++index];
		else if (argument === '--chrome') options.chrome = argv[++index];
		else if (argument === '--measure') options.measure = true;
		else if (argument === '--timeout-minutes') options.timeoutMs = Number(argv[++index]) * 60000;
		else if (!options.webRoot) options.webRoot = argument;
		else throw new Error(`Unknown argument ${argument}`);
	}
	if (!options.webRoot)
		throw new Error('Usage: node tools/web-determinism/run.mjs <published wwwroot> [--classes A,B] [--report file] [--chrome path] [--measure]');
	return options;
}

async function findChrome(explicit) {
	for (const candidate of [explicit, '/usr/bin/google-chrome', '/usr/bin/chromium', '/usr/bin/chromium-browser', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome']) {
		if (!candidate) continue;
		try { await fs.access(candidate); return candidate; } catch { /* the next one */ }
	}
	throw new Error('Chrome or Chromium is required; pass --chrome or set CHROME.');
}

async function main() {
	const options = parseArguments(process.argv.slice(2));
	const chromePath = await findChrome(options.chrome);
	const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'opentpw-determinism-'));
	const profile = path.join(temporary, 'profile');
	const emptyGame = path.join(temporary, 'game');
	await fs.mkdir(profile);
	await fs.mkdir(emptyGame);
	const server = await startServer(path.resolve(options.webRoot), emptyGame);
	let chrome, cdp;
	const errors = [];
	const report = { started: new Date().toISOString(), classes: options.classes, math: null, tests: [] };
	try {
		const launched = await launchChrome(chromePath, profile);
		chrome = launched.chrome;
		cdp = await Cdp.connect(launched.url);
		const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
		const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
		const send = (method, params = {}, timeoutMs) => cdp.send(method, params, sessionId, timeoutMs);
		cdp.on(event => {
			if (event.sessionId === sessionId && event.method === 'Runtime.exceptionThrown')
				errors.push(event.params.exceptionDetails.exception?.description ?? event.params.exceptionDetails.text);
		});
		await send('Runtime.enable');
		await send('Page.enable');
		const evaluate = async (expression, timeoutMs = 60000) => {
			const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true, timeout: timeoutMs }, timeoutMs + 5000);
			if (result.exceptionDetails)
				throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
			return result.result.value;
		};
		await send('Page.navigate', { url: `${server.origin}/?perf=1&keep=off` });
		const deadline = Date.now() + 180000;
		for (;;) {
			let ready = false;
			try {
				ready = await evaluate('typeof globalThis.opentpwPerf?.program?.DeterminismRun === "function"');
			} catch (error) {
				if (!/Execution context was destroyed|Cannot find context|Cannot find default execution context/.test(error.message)) throw error;
			}
			if (ready) break;
			if (errors.length) throw new Error(errors.join('\n'));
			if (Date.now() >= deadline)
				throw new Error('The page has no DeterminismRun export within 180 s: publish it with -p:OpenTpwDeterminismPins=true.');
			await sleep(200);
		}
		report.userAgent = await evaluate('navigator.userAgent');
		report.math = JSON.parse(await evaluate('opentpwPerf.program.DeterminismMathProbe()', 600000));
		console.log('Elementary functions in this runtime (digests; "a != b" counts the inputs where the platform function differs from DetMath):');
		for (const [name, value] of Object.entries(report.math))
			console.log(`  ${name.padEnd(40)} ${value}`);
		const tests = JSON.parse(await evaluate(`opentpwPerf.program.DeterminismTests(${JSON.stringify(options.classes.join(','))})`));
		for (const { class: className, method, informative } of tests) {
			let result;
			if (SKIPPED.has(`${className}.${method}`)) {
				result = { outcome: 'skipped', message: SKIPPED.get(`${className}.${method}`), ms: 0 };
				report.tests.push({ class: className, method, informative, ...result });
				console.log(`skipped      ${className}.${method} (${result.message})`);
				continue;
			}
			try {
				result = JSON.parse(await evaluate(`opentpwPerf.program.DeterminismRun(${JSON.stringify(className)}, ${JSON.stringify(method)})`, options.timeoutMs));
			} catch (error) {
				result = { outcome: 'failed', message: `harness: ${error.message}`, ms: null };
			}
			// A comparison with the runtime's own C library (musl here) informs about the platform; it does not gate the simulation.
			if (informative && result.outcome === 'failed')
				result.outcome = 'differs';
			report.tests.push({ class: className, method, informative, ...result });
			console.log(`${result.outcome.padEnd(12)} ${className}.${method} (${result.ms ?? '?'} ms)${result.message && result.outcome !== 'passed' ? `\n             ${result.message.split('\n')[0]}` : ''}`);
		}
	} finally {
		cdp?.close();
		await stopChrome(chrome);
		await server.close();
		await fs.rm(temporary, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
	}
	const count = outcome => report.tests.filter(test => test.outcome === outcome).length;
	report.summary = { passed: count('passed'), failed: count('failed'), inconclusive: count('inconclusive'), skipped: count('skipped'), platformLibraryDiffers: count('differs') };
	console.log(`\n${report.summary.passed} passed, ${report.summary.failed} failed, ${report.summary.inconclusive} inconclusive, ${report.summary.skipped} skipped, ${report.summary.platformLibraryDiffers} platform-library comparisons differ (informative)`);
	if (options.report)
		await fs.writeFile(options.report, JSON.stringify(report, null, 2) + '\n');
	// A test may end inconclusive only for a reason the browser page cannot meet (no game data, language pack or repository sources);
	// any other inconclusive outcome means a pin silently stopped running, and fails the run like a failure.
	const expectedInconclusive = /OPENTPW_[A-Z_]+|Repository sources are not next to the test binaries/;
	const unexpected = report.tests.filter(test => test.outcome === 'inconclusive' && !expectedInconclusive.test(test.message ?? ''));
	for (const test of unexpected)
		console.log(`unexpected inconclusive: ${test.class}.${test.method}`);
	if ((report.summary.failed > 0 || unexpected.length > 0) && !options.measure)
		process.exitCode = 1;
}

main().catch(error => {
	console.error(error.stack ?? error);
	process.exitCode = 1;
});
