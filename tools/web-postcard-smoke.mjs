#!/usr/bin/env node
// Runs the actual Send Postcard handler and package round trips in the published .NET/WASM page, without original
// game assets, account credentials or server writes. CI runs this against the image it is about to publish.
// Usage: node tools/web-postcard-smoke.mjs http://127.0.0.1:8080 [Chrome executable]
import assert from 'node:assert/strict';
import { promises as fs } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { Cdp, launchChrome, sleep, stopChrome } from './web-perf/lib.mjs';

const url = new URL(process.argv[2] ?? 'http://127.0.0.1:8080');
if (!['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname) || !['http:', 'https:'].includes(url.protocol))
	throw new Error('The postcard smoke check only runs against a loopback server.');
url.search = '?perf=1&keep=off';
url.hash = '';
let executable = process.argv[3] ?? process.env.CHROME;
if (!executable) {
	for (const candidate of ['/usr/bin/google-chrome', '/usr/bin/chromium', '/usr/bin/chromium-browser', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome']) {
		try { await fs.access(candidate); executable = candidate; break; } catch { /* try the next installed browser */ }
	}
}
if (!executable)
	throw new Error('Chrome or Chromium is required; pass its executable as the second argument or set CHROME.');

const expectedChecks = [
	'compatibility-normalization', 'dotnet-identity-casing', 'canonical-name-validation', 'malformed-unicode-rejected',
	'compatibility-duplicates-rejected', 'postcard-create-write-read', 'send-handler-defaults-and-outbox',
	'send-handler-unicode-outbox', 'duplicate-send-preserves-draft', 'duplicate-send-can-retry',
	'platform-failure-preserves-draft', 'platform-failure-can-retry', 'temporary-state-cleaned',
];
const profile = await fs.mkdtemp(path.join(os.tmpdir(), 'opentpw-postcard-browser-'));
const errors = [];
const diagnostics = [];
let chrome, cdp, sessionId;
try {
	const launched = await launchChrome(executable, profile);
	chrome = launched.chrome;
	cdp = await Cdp.connect(launched.url);
	const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
	({ sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true }));
	const send = (method, params = {}) => cdp.send(method, params, sessionId);
	cdp.on(event => {
		if (event.sessionId !== sessionId) return;
		if (event.method === 'Runtime.exceptionThrown')
			errors.push(event.params.exceptionDetails.exception?.description ?? event.params.exceptionDetails.text);
		if (event.method === 'Runtime.consoleAPICalled') {
			const line = event.params.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
			diagnostics.push(line);
			if (event.params.type === 'error') errors.push(line);
		}
		if (event.method === 'Network.loadingFailed' && !event.params.canceled)
			errors.push(`Resource load failed: ${event.params.errorText}`);
	});
	await send('Runtime.enable');
	await send('Page.enable');
	await send('Network.enable');
	const evaluate = async expression => {
		const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
		if (result.exceptionDetails)
			throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
		return result.result.value;
	};
	await send('Page.navigate', { url: url.href });
	const deadline = Date.now() + 120000;
	for (;;) {
		let ready = false;
		try {
			ready = await evaluate('typeof globalThis.opentpwPerf?.program?.PostcardSmokeTest === "function"');
		} catch (error) {
			// Navigation may replace the initial about:blank context between the CDP command and its execution.
			if (!/Execution context was destroyed|Cannot find context with specified id|Cannot find default execution context/.test(error.message))
				throw error;
		}
		if (ready) break;
		if (errors.length) throw new Error(errors.join('\n'));
		if (Date.now() >= deadline) {
			const status = await evaluate('document.getElementById("status")?.textContent ?? document.title');
			throw new Error(`The postcard WASM export was not ready within 120 s; page status: ${status}`);
		}
		await sleep(100);
	}
	assert.equal(await evaluate('opentpwPerf.state().started'), false, 'The smoke check must not start a game.');
	const persistentBefore = await evaluate('opentpwPerf.program.PersistentFiles()');
	const results = [];
	// A second call proves the hook restored the shared state and left no test outbox behind.
	for (let iteration = 0; iteration < 2; iteration++) {
		const result = JSON.parse(await evaluate('opentpwPerf.program.PostcardSmokeTest()'));
		assert.equal(result.runtime, 'browser');
		assert.deepEqual(result.checks, expectedChecks);
		results.push(result);
	}
	assert.deepEqual(await evaluate('opentpwPerf.program.PersistentFiles()'), persistentBefore, 'Player saves and settings must stay unchanged.');
	assert.equal(await evaluate('opentpwPerf.state().started'), false);
	assert.deepEqual(errors, [], 'The browser must not report an unhandled runtime failure.');
	console.log(JSON.stringify({ passed: true, url: url.href, iterations: results.length, checks: expectedChecks }, null, 2));
} catch (error) {
	console.error(diagnostics.slice(-30).join('\n'));
	console.error(error.stack ?? error);
	process.exitCode = 1;
} finally {
	cdp?.close();
	await stopChrome(chrome);
	// Chrome's child processes can briefly finish writing after its main process exits.
	// Retry transient profile-directory races, but still fail if cleanup cannot complete.
	await fs.rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
}
