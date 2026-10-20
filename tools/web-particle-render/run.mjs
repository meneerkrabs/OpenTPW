#!/usr/bin/env node
// Strict, opt-in presented-framebuffer oracle. The unfixed baseline is expected to fail.
import { promises as fs } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { mismatchesOf } from './assertions.mjs';
import { Cdp, launchChrome, sleep, startServer, stopChrome } from '../web-perf/lib.mjs';

const names = ['red0', 'redEF', 'redF0', 'redFF', 'blueFF', 'additive0', 'oneOne0',
	'nearZeroThenFar', 'nearF0ThenFar', 'noWriteZeroThenFar', 'nearThenFar', 'farThenNear', 'noWriteNearThenFar', 'interleaved', 'screen0', 'screenFF', 'screenThenScreen'];
const options = { root: process.argv[2], chrome: '/usr/bin/google-chrome', report: null };
for (let i = 3; i < process.argv.length; i += 2) {
	if (process.argv[i] === '--chrome') options.chrome = process.argv[i + 1];
	else if (process.argv[i] === '--report') options.report = process.argv[i + 1];
	else throw new Error(`Unknown option ${process.argv[i]}`);
}
if (!options.root || !options.report) throw new Error('Usage: run.mjs <published wwwroot> --report <json> [--chrome <path>]');
const report = { contract: 'untextured opaque world particles; strict fixed-contract oracle, baseline expected to fail',
	started: new Date().toISOString(), cases: [], mismatches: [], errors: [] };
let temporary, server, chrome, cdp;
try {
	temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'opentpw-particle-oracle-'));
	await fs.mkdir(path.join(temporary, 'profile'));
	await fs.mkdir(path.join(temporary, 'empty-game'));
	server = await startServer(path.resolve(options.root), path.join(temporary, 'empty-game'));
	const launched = await launchChrome(options.chrome, path.join(temporary, 'profile'));
	chrome = launched.chrome;
	cdp = await Cdp.connect(launched.url);
	const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
	const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
	const send = (method, params = {}) => cdp.send(method, params, sessionId, 65000);
	cdp.on(event => {
		if (event.sessionId === sessionId && event.method === 'Runtime.exceptionThrown')
			report.errors.push(event.params.exceptionDetails.exception?.description ?? event.params.exceptionDetails.text);
	});
	const evaluate = async expression => {
		const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true, timeout: 60000 });
		if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
		return result.result.value;
	};
	await send('Runtime.enable');
	await send('Page.enable');
	await send('Page.navigate', { url: `${server.origin}/?perf=1&keep=off` });
	const deadline = Date.now() + 180000;
	for (;;) {
		if (await evaluate('typeof globalThis.opentpwPerf?.program?.ParticleOracleStart === "function"')) break;
		if (report.errors.length || Date.now() >= deadline) throw new Error('Particle oracle export unavailable; build with OpenTpwDeterminismPins=true.');
		await sleep(200);
	}
	try {
		await evaluate(`(() => {
			const canvas = document.getElementById('canvas');
			canvas.style.setProperty('display', 'block', 'important');
			canvas.style.setProperty('width', '320px', 'important');
			canvas.style.setProperty('height', '200px', 'important');
			if (!(canvas.clientWidth > 0 && canvas.clientHeight > 0)) throw new Error('Invisible oracle canvas');
			opentpwPerf.program.ParticleOracleStart();
		})()`);
		for (const name of names) {
			// No await or browser task boundary between the real draw and default-framebuffer readback.
			const observation = await evaluate(`(() => {
				const canvas = document.getElementById('canvas');
				const gl = canvas.getContext('webgl2');
				if (!gl || gl.isContextLost()) throw new Error('WebGL context unavailable');
				const beforeError = gl.getError();
				if (beforeError !== gl.NO_ERROR) throw new Error('Pre-render GL error ' + beforeError);
				const frame = JSON.parse(opentpwPerf.program.ParticleOracleDraw(${JSON.stringify(name)}));
				const ratio = window.devicePixelRatio || 1;
				const expectedWidth = Math.round(320 * ratio), expectedHeight = Math.round(200 * ratio);
				if (canvas.clientWidth !== 320 || canvas.clientHeight !== 200
					|| gl.drawingBufferWidth !== expectedWidth || gl.drawingBufferHeight !== expectedHeight
					|| frame.width !== expectedWidth || frame.height !== expectedHeight)
					throw new Error('Oracle canvas/world/output dimensions differ');
				const previous = gl.getParameter(gl.READ_FRAMEBUFFER_BINDING);
				try {
					gl.bindFramebuffer(gl.READ_FRAMEBUFFER, null);
					if (gl.checkFramebufferStatus(gl.READ_FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE) throw new Error('Incomplete read framebuffer');
					const pixels = new Uint8Array(3 * 3 * 4), corner = new Uint8Array(4);
					gl.readPixels(Math.floor(gl.drawingBufferWidth / 2) - 1, Math.floor(gl.drawingBufferHeight / 2) - 1, 3, 3, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
					gl.readPixels(1, 1, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, corner);
					const error = gl.getError();
					if (error !== gl.NO_ERROR) throw new Error('Render/readback GL error ' + error);
					return { ...frame, devicePixelRatio: ratio, drawingBufferWidth: gl.drawingBufferWidth, drawingBufferHeight: gl.drawingBufferHeight, pixels: Array.from(pixels), corner: Array.from(corner) };
				} finally {
					gl.bindFramebuffer(gl.READ_FRAMEBUFFER, previous);
					const restoreError = gl.getError();
					if (restoreError !== gl.NO_ERROR) throw new Error('Read binding restore GL error ' + restoreError);
				}
			})()`);
			report.cases.push(observation);
		}
	} finally {
		await evaluate('opentpwPerf.program.ParticleOracleStop()');
	}
	report.mismatches.push(...mismatchesOf(report.cases));
} catch (error) {
	report.errors.push(error.stack ?? String(error));
} finally {
	for (const cleanup of [() => cdp?.close(), () => stopChrome(chrome), () => server?.close(),
		() => temporary && fs.rm(temporary, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 })]) {
		try { await cleanup(); } catch (error) { report.errors.push('Cleanup: ' + (error.stack ?? error)); }
	}
	report.passed = report.cases.length === names.length && report.errors.length === 0 && report.mismatches.length === 0;
	await fs.writeFile(options.report, JSON.stringify(report, null, 2) + '\n');
	console.log(JSON.stringify({ passed: report.passed, cases: report.cases.length, mismatches: report.mismatches, errors: report.errors }, null, 2));
	if (!report.passed) process.exitCode = 1;
}
