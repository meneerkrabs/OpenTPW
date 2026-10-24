#!/usr/bin/env node
// The browser's link to a real server, the transport only (docs/COOP.md, "The browser's link to a real server"; C2c-2b-5). Starts the real server
// (OpenTPW.Server) on 127.0.0.1 with a fresh data folder, no two-step login and the published web build as its own page; registers two players; and opens two
// pages of that server in headless, muted Chrome, each in a browser context of its own, with ?coopLinkSmoke=1: a host and a guest, each running the game's own
// online client over the browser runtime's WebSocket and HttpClient (Program.CoopLinkSmoke.cs, CoopLinkProbe). The host creates a session and the guest joins
// it; the host stores a snapshot, which the guest downloads; the host's frame and a message each way are relayed; and the host's leave ends the session for the
// guest. The driver chooses every payload (seeded bytes) and checks each record the pages give back against its own lengths and SHA-256. No game file is used,
// and no token or password is written anywhere.
//
// Usage:
//   node tools/web-coop-link.mjs --web <published wwwroot> --server <OpenTPW.Server.dll> [--out <directory>] [--chrome <executable>] [--headed]
//        [--timeout-seconds S] [--proxy]
// With --out (a directory that does not exist yet) it writes link-run.json, both pages' records and every comparison, and server.log. It exits 1 when the
// check fails. The server runs on the `dotnet` the DOTNET environment variable names, else the one on the path. With --proxy the pages are opened through the
// drivers' own local server (tools/web-perf/lib.mjs) with the server as its upstream, as the live acceptance's pages are (docs/COOP.md, "The live acceptance"):
// their HTTP requests and their sockets' upgrades go through it, and the run checks that the sockets did.
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { promises as fs } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { Cdp, launchChrome, sleep, startServer as startLocalServer, stopChrome } from './web-perf/lib.mjs';

const DEFAULT_CHROME = {
	darwin: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
	linux: '/usr/bin/google-chrome',
	win32: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
}[process.platform];

const PASSWORD = 'link-check-password';
const PLAYERS = { host: 'LinkHost', guest: 'LinkGuest' };

function parseArguments(argv) {
	const options = { web: null, server: null, out: null, chrome: process.env.CHROME ?? DEFAULT_CHROME, headed: false, timeoutSeconds: 60, proxy: false };
	for (let index = 2; index < argv.length; index++) {
		const [flag, inline] = argv[index].split(/=(.*)/s);
		const value = () => inline ?? argv[++index];
		switch (flag) {
			case '--web': options.web = value(); break;
			case '--server': options.server = value(); break;
			case '--out': options.out = value(); break;
			case '--chrome': options.chrome = value(); break;
			case '--headed': options.headed = true; break;
			case '--proxy': options.proxy = true; break;
			case '--timeout-seconds': {
				const seconds = value();
				if (!/^[1-9][0-9]*$/.test(seconds))
					throw new Error(`--timeout-seconds takes a whole number of seconds, not '${seconds}'`);
				options.timeoutSeconds = Number(seconds);
				break;
			}
			default: throw new Error(`Unknown option ${flag}`);
		}
	}
	if (!options.web || !options.server)
		throw new Error('Pass --web <published wwwroot> and --server <OpenTPW.Server.dll>.');
	return options;
}

/** Seeded bytes: SHA-256 of the seed and a block counter, block after block. */
function seeded(length, seed) {
	const bytes = Buffer.alloc(length);
	for (let block = 0, at = 0; at < length; block++, at += 32)
		createHash('sha256').update(`opentpw-coop-link-${seed}-${block}`).digest().copy(bytes, at);
	return bytes;
}

const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');

/** `promise`, or a failure naming `what` once `milliseconds` have passed. */
function within(promise, milliseconds, what) {
	let timer;
	const deadline = new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(`${what} did not answer within ${milliseconds / 1000} s`)), milliseconds); });
	return Promise.race([promise, deadline]).finally(() => clearTimeout(timer));
}

/**
 * Starts the server with a fresh data folder, no two-step login and the page as its own, and hands its process to `started` at once (so a start that fails can be
 * stopped); resolves once it says where it listens (a port of its own choice on 127.0.0.1). Its output goes to `log` as it comes.
 */
async function startServer(dll, web, data, log, started) {
	const args = [dll, '--urls', 'http://127.0.0.1:0', `--OpenTPW:DataDirectory=${data}`, '--OpenTPW:RequireTotp=false', `--OpenTPW:WebClientDirectory=${web}`,
		'--Logging:LogLevel:Default=Warning', '--Logging:LogLevel:Microsoft.Hosting.Lifetime=Information'];
	const child = spawn(process.env.DOTNET ?? 'dotnet', args, { cwd: path.dirname(dll), stdio: ['ignore', 'pipe', 'pipe'], env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1' } });
	// a process that could not be started says so here, and never ends (no exit follows a spawn error)
	let failed = null;
	child.once('error', error => failed = error);
	started(child);
	let output = '';
	const take = chunk => {
		output += chunk;
		log.push(String(chunk));
	};
	child.stdout?.on('data', take);
	child.stderr?.on('data', take);
	const origin = await new Promise((resolve, reject) => {
		if (failed)
			return reject(new Error(`The server could not start: ${failed.message}`));
		const timer = setTimeout(() => reject(new Error(`The server did not say where it listens within 60 s:\n${output.slice(-2000)}`)), 60000);
		const listening = () => {
			const match = /Now listening on: (http:\/\/127\.0\.0\.1:\d+)/.exec(output);
			if (match) {
				clearTimeout(timer);
				resolve(match[1]);
			}
		};
		child.stdout?.on('data', listening);
		child.stderr?.on('data', listening);
		child.once('error', error => { clearTimeout(timer); reject(new Error(`The server could not start: ${error.message}`)); });
		child.once('exit', code => { clearTimeout(timer); reject(new Error(`The server exited (${code}) before it listened:\n${output.slice(-2000)}`)); });
	});
	return origin;
}

/** Ends the server and waits until it has exited (at most a few seconds); a process that never started (no pid) has nothing to end. */
async function stopServer(child) {
	if (!child || child.pid === undefined || child.exitCode !== null || child.signalCode !== null)
		return;
	const exited = new Promise(resolve => child.once('exit', resolve));
	child.kill('SIGTERM');
	await Promise.race([exited, sleep(10000)]);
	if (child.exitCode === null && child.signalCode === null) {
		child.kill('SIGKILL');
		await Promise.race([exited, sleep(2000)]);
	}
}

/** Registers a player on the server (the server takes it at once: no two-step login), within 30 seconds. */
async function register(origin, name) {
	const response = await fetch(`${origin}/api/v1/accounts`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ name, password: PASSWORD }),
		signal: AbortSignal.timeout(30000) });
	if (response.status !== 201)
		throw new Error(`Registering ${name} answered ${response.status}: ${(await response.text()).slice(0, 300)}`);
}

/** One page of the check, in a browser context of its own, over a flattened DevTools session. */
class Page {
	constructor(cdp, sessionId, role) { this.cdp = cdp; this.sessionId = sessionId; this.role = role; this.errors = []; this.crashed = null; this.status = null; }

	async eval(expression, timeoutMs = 30000) {
		const result = await this.cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }, this.sessionId, timeoutMs);
		if (result.exceptionDetails)
			throw new Error(`${this.role}: ${expression.slice(0, 120)}: ${result.exceptionDetails.exception?.description ?? result.exceptionDetails.text}`);
		return result.result.value;
	}

	/**
	 * One of the page's exports with its arguments, bytes as base64 made a byte array in the page: the export's name and its arguments travel as the protocol's
	 * arguments of one fixed function, never inside code the page compiles.
	 */
	async call(name, ...args) {
		const { result: global } = await this.cdp.send('Runtime.evaluate', { expression: 'globalThis', objectGroup: 'opentpw-driver' }, this.sessionId);
		try {
			const result = await this.cdp.send('Runtime.callFunctionOn', {
				functionDeclaration: `(name, args) => opentpwLink.program[name](...args.map(arg => typeof arg === 'object' && arg !== null
					? Uint8Array.from(atob(arg.base64), c => c.charCodeAt(0)) : arg))`,
				objectId: global.objectId,
				arguments: [{ value: name }, { value: args.map(arg => Buffer.isBuffer(arg) ? { base64: arg.toString('base64') } : arg) }],
				returnByValue: true, awaitPromise: true,
			}, this.sessionId, 30000);
			if (result.exceptionDetails)
				throw new Error(`${this.role}: ${name}: ${result.exceptionDetails.exception?.description ?? result.exceptionDetails.text}`);
			return result.result.value;
		} finally {
			await this.cdp.send('Runtime.releaseObjectGroup', { objectGroup: 'opentpw-driver' }, this.sessionId).catch(() => {});
		}
	}

	/** Steps the page's probe (it pumps its own session) and keeps its status. */
	async step() {
		if (this.crashed)
			throw new Error(`The ${this.role} page crashed: ${this.crashed}`);
		this.status = JSON.parse(await this.eval('opentpwLink.program.CoopLinkSmokeStatus()'));
		if (this.status.failure)
			throw new Error(`The ${this.role} page's probe failed: ${this.status.failure}`);
		return this.status;
	}

	records(kind) { return (this.status?.events ?? []).filter(item => item.kind === kind); }
}

/** Steps both pages, the host first, until `done` holds; a failure of either or the deadline throws. */
async function until(pages, done, what, timeoutSeconds) {
	const begin = Date.now();
	for (;;) {
		for (const page of pages)
			await page.step();
		if (done())
			return;
		if (Date.now() - begin > timeoutSeconds * 1000)
			throw new Error(`Timed out after ${timeoutSeconds} s waiting for ${what}; ${pages.map(page => `${page.role} ${JSON.stringify(page.status)}`).join('; ')}`);
		await sleep(20);
	}
}

/** Opens a page of the server in a browser context of its own and waits for the page's harness. */
async function openPage(cdp, origin, role) {
	const { browserContextId } = await cdp.send('Target.createBrowserContext', { disposeOnDetach: true });
	const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank', browserContextId });
	const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
	const page = new Page(cdp, sessionId, role);
	cdp.on(message => {
		if (message.method === 'Target.targetCrashed' && message.params?.targetId === targetId)
			page.crashed = `the renderer ended (${message.params.status}, ${message.params.errorCode})`;
		if (message.method === 'Target.detachedFromTarget' && message.params?.sessionId === sessionId)
			page.crashed ??= 'the page was detached';
		if (message.sessionId !== sessionId)
			return;
		if (message.method === 'Runtime.exceptionThrown')
			page.errors.push(message.params.exceptionDetails?.exception?.description ?? message.params.exceptionDetails?.text);
		if (message.method === 'Runtime.consoleAPICalled' && message.params.type === 'error')
			page.errors.push(message.params.args.map(arg => arg.value ?? arg.description ?? '').join(' '));
	});
	await cdp.send('Runtime.enable', {}, sessionId);
	await cdp.send('Page.enable', {}, sessionId);
	await cdp.send('Page.navigate', { url: `${origin}/index.html?coopLinkSmoke=1` }, sessionId);
	const begin = Date.now();
	while (!await page.eval('Boolean(globalThis.opentpwLink)').catch(() => false)) {
		if (page.crashed)
			throw new Error(`The ${role} page crashed: ${page.crashed}`);
		if (Date.now() - begin > 120000)
			throw new Error(`The ${role} page did not start its link check mode within 120 s; its errors: ${page.errors.slice(0, 5).join(' | ')}`);
		await sleep(250);
	}
	return page;
}

/** Checks a condition of the run, recording it either way; a failed one fails the run once every one is recorded. */
function compare(checks, what, expected, actual) {
	checks.push({ what, expected, actual, passed: JSON.stringify(expected) === JSON.stringify(actual) });
}

async function run(options) {
	const record = { started: new Date().toISOString(), state: 'failed', checks: [] };
	const serverLog = [];
	let server, origin, profile, data, chrome, cdp, host, guest, proxy, emptyGame;
	try {
		await fs.access(path.join(options.web, 'index.html')).catch(() => { throw new Error(`${options.web} has no index.html: pass the published wwwroot`); });
		await fs.access(options.server).catch(() => { throw new Error(`${options.server} does not exist: pass the built OpenTPW.Server.dll`); });
		data = await fs.mkdtemp(path.join(os.tmpdir(), 'opentpw-coop-link-data-'));
		origin = await startServer(path.resolve(options.server), path.resolve(options.web), data, serverLog, child => server = child);
		await register(origin, PLAYERS.host);
		await register(origin, PLAYERS.guest);
		if (options.proxy) {
			// the drivers' own local server in front of the real one: the pages see its origin, and the server's page with its policy comes through it
			emptyGame = await fs.mkdtemp(path.join(os.tmpdir(), 'opentpw-coop-link-game-'));
			proxy = await startLocalServer(path.resolve(options.web), emptyGame, origin);
			const policy = (await fetch(`${proxy.origin}/index.html`, { signal: AbortSignal.timeout(30000) })).headers.get('content-security-policy');
			record.proxy = { policy };
			compare(record.checks, 'the page through the proxy lets its sockets reach its own origin', true, /connect-src 'self'/.test(policy ?? ''));
		}
		const pageOrigin = proxy?.origin ?? origin;
		profile = await fs.mkdtemp(path.join(os.tmpdir(), 'opentpw-coop-link-chrome-'));
		const launched = await launchChrome(options.chrome, profile, { headed: options.headed, audible: false });
		chrome = launched.chrome;
		cdp = await within(Cdp.connect(launched.url), 30000, 'Chrome\'s DevTools');
		record.chrome = (await cdp.send('Browser.getVersion')).product;
		host = await openPage(cdp, pageOrigin, 'host');
		guest = await openPage(cdp, pageOrigin, 'guest');
		const pages = [host, guest];
		const wait = (done, what) => until(pages, done, what, options.timeoutSeconds);

		// both log in over HTTP and open the co-op socket, the token in its first frame
		await host.call('CoopLinkSmokeStart', PLAYERS.host, PASSWORD);
		await guest.call('CoopLinkSmokeStart', PLAYERS.guest, PASSWORD);
		await wait(() => host.status.phase === 'open' && guest.status.phase === 'open', 'both welcomes');
		record.protocol = host.status.protocol;

		// the host creates a listed session; the guest finds it on the open list (HTTP), the host invites the guest (the socket), and the guest joins with the
		// invitation's code; the host hears the guest join
		if (!await host.call('CoopLinkSmokeCreate', 'coop link check', true))
			throw new Error('The host page\'s link did not take the create request');
		await wait(() => host.records('session').length === 1, 'the created session');
		const created = host.records('session')[0];
		await guest.call('CoopLinkSmokeList');
		await wait(() => guest.records('listed').length === 1, 'the open list');
		const row = guest.records('listed')[0].sessions.find(item => item.code === created.code);
		compare(record.checks, 'the open list shows the listed session with its host and players', ['coop link check', PLAYERS.host, 1, 2],
			[row?.title, row?.host, row?.players, row?.maximum]);
		if (!await host.call('CoopLinkSmokeInvite', PLAYERS.guest))
			throw new Error('The host page\'s link did not take the invite request');
		await wait(() => guest.records('invited').length === 1, 'the invitation');
		const invited = guest.records('invited')[0];
		compare(record.checks, 'the guest receives the host\'s invitation to the session', [created.id, created.code, 'coop link check', PLAYERS.host],
			[invited.session, invited.code, invited.title, invited.host]);
		if (!await guest.call('CoopLinkSmokeJoin', invited.code))
			throw new Error('The guest page\'s link did not take the join request');
		await wait(() => guest.records('session').length === 1 && host.records('member').some(item => item.state === 'Joined'), 'the join');
		const joined = guest.records('session')[0];
		const member = host.records('member').find(item => item.state === 'Joined');
		if (proxy) {
			// each page's own chat is open: its socket can only have come through the proxy, the page's only origin
			await wait(() => host.status.chat?.open === true && guest.status.chat?.open === true, 'both pages\' chats');
			record.proxy.chats = { host: host.status.chat, guest: guest.status.chat };
		}
		compare(record.checks, 'the joined session is the created one', created.id, joined.id);
		compare(record.checks, 'the guest names the host as its host', created.you, joined.host);
		compare(record.checks, 'the host hears the guest join', joined.you, member.id);

		// the host's snapshot over HTTP PUT once a member joined, the guest's download of it over HTTP GET
		const snapshot = seeded(150 * 1024, 'snapshot');
		await host.call('CoopLinkSmokeUpload', snapshot);
		await wait(() => host.records('uploaded').length === 1, 'the upload');
		compare(record.checks, 'the server stores the snapshot under its SHA-256', sha256(snapshot), host.records('uploaded')[0].sha256);
		compare(record.checks, 'the host page hashes what it uploads as the driver does', sha256(snapshot), host.records('upload')[0].sha256);
		await guest.call('CoopLinkSmokeDownload', sha256(snapshot), snapshot.length);
		await wait(() => guest.records('downloaded').length === 1, 'the download');
		compare(record.checks, 'the guest downloads the snapshot whole', [snapshot.length, sha256(snapshot)],
			[guest.records('downloaded')[0].length, guest.records('downloaded')[0].sha256]);

		// the host's frame to the guest, a message each way
		const frame = seeded(700, 'frame');
		await host.call('CoopLinkSmokeSendFrame', 1, frame);
		await wait(() => guest.records('frame').length === 1, 'the relayed frame');
		compare(record.checks, 'the guest receives the host\'s frame', [1, frame.length, sha256(frame)],
			[guest.records('frame')[0].seq, guest.records('frame')[0].length, guest.records('frame')[0].sha256]);
		const toHost = seeded(300, 'to-host');
		await guest.call('CoopLinkSmokeSend', created.you, toHost);
		await wait(() => host.records('message').length === 1, 'the guest\'s message');
		compare(record.checks, 'the host receives the guest\'s message', [joined.you, toHost.length, sha256(toHost)],
			[host.records('message')[0].from, host.records('message')[0].length, host.records('message')[0].sha256]);
		const toGuest = seeded(200, 'to-guest');
		await host.call('CoopLinkSmokeSend', joined.you, toGuest);
		await wait(() => guest.records('message').length === 1, 'the host\'s message');
		compare(record.checks, 'the guest receives the host\'s message', [created.you, toGuest.length, sha256(toGuest)],
			[guest.records('message')[0].from, guest.records('message')[0].length, guest.records('message')[0].sha256]);

		// the host's leave ends the session, which the guest hears
		if (!await host.call('CoopLinkSmokeLeave', 'The check ended.'))
			throw new Error('The host page\'s link did not take the leave');
		await wait(() => guest.status.ended === true, 'the session\'s end');
		const ended = guest.records('ended')[0];
		compare(record.checks, 'the guest hears the host\'s leave end its session', [created.id, 'HostLeft', 'The check ended.'], [ended?.session, ended?.reason, ended?.text]);
		for (const page of pages)
			await page.call('CoopLinkSmokeEnd');
		for (const page of pages)
			await page.step();
		if (proxy) {
			// each page's co-op socket and chat socket were upgrades the proxy forwarded, the server took, and that carried bytes both ways
			record.proxy.upgrades = proxy.log.filter(entry => entry.upgrade);
			const carried = route => record.proxy.upgrades.filter(entry => entry.path === route && entry.status === 101 && entry.bytesIn > 0 && entry.bytesOut > 0).length;
			record.proxy.carried = { coop: carried('/api/v1/coop'), chat: carried('/api/v1/chat') };
			compare(record.checks, 'both pages\' co-op and chat sockets went through the proxy to the server', [true, true, true, true],
				[record.proxy.carried.coop >= 2, record.proxy.carried.chat >= 2, record.proxy.chats.host.open, record.proxy.chats.guest.open]);
		}
		const failed = record.checks.filter(check => !check.passed);
		if (failed.length)
			throw new Error(`${failed.length} check(s) failed: ${failed.map(check => check.what).join('; ')}`);
		record.state = 'passed';
	} catch (error) {
		record.message = error.message;
	} finally {
		// what each page recorded and said, then everything stopped; what could not be cleaned up is reported with the run
		for (const page of [host, guest].filter(Boolean)) {
			record[page.role] = page.status;
			record[`${page.role}Errors`] = page.errors.slice(0, 20);
		}
		const cleanup = [];
		try {
			cdp?.close();
		} catch (error) {
			cleanup.push(`DevTools: ${error.message}`);
		}
		await stopChrome(chrome).catch(error => cleanup.push(`Chrome: ${error.message}`));
		// the shared helper sends its last kill without waiting for the exit: wait for it here, a little, before calling Chrome left behind
		if (chrome && chrome.exitCode === null && chrome.signalCode === null)
			await Promise.race([new Promise(resolve => chrome.once('exit', resolve)), sleep(3000)]);
		if (chrome && chrome.pid !== undefined && chrome.exitCode === null && chrome.signalCode === null)
			cleanup.push(`Chrome (process ${chrome.pid}) had not exited`);
		await proxy?.close().catch(error => cleanup.push(`the proxy: ${error.message}`));
		await stopServer(server).catch(error => cleanup.push(`the server: ${error.message}`));
		if (server && server.pid !== undefined && server.exitCode === null && server.signalCode === null)
			cleanup.push(`the server (process ${server.pid}) had not exited`);
		for (const folder of [profile, data, emptyGame].filter(Boolean))
			await fs.rm(folder, { recursive: true, force: true }).catch(error => cleanup.push(`${folder}: ${error.message}`));
		if (cleanup.length) {
			// what was left running or on disk fails the run, which would otherwise pass with it
			record.cleanup = cleanup;
			if (record.state === 'passed') {
				record.state = 'failed';
				record.message = `The run passed, but its cleanup did not: ${cleanup.join('; ')}`;
			}
		}
	}
	return { record, serverLog: serverLog.join('') };
}

const options = parseArguments(process.argv);
if (options.out)
	await fs.mkdir(options.out); // a fresh directory: an earlier run's records are never mixed in
const { record, serverLog } = await run(options);
if (options.out) {
	await fs.writeFile(path.join(options.out, 'link-run.json'), JSON.stringify(record, null, 2) + '\n');
	await fs.writeFile(path.join(options.out, 'server.log'), serverLog);
}
const passedChecks = record.checks.filter(check => check.passed).length;
console.log(`browser co-op link check${options.proxy ? ' through the proxy' : ''}: ${record.state} (${passedChecks} of ${record.checks.length} checks, protocol ${record.protocol ?? 'unknown'}, ${record.chrome ?? 'no Chrome'})`
	+ (record.message ? `: ${record.message}` : ''));
process.exit(record.state === 'passed' ? 0 : 1);
