#!/usr/bin/env node
// The live co-op acceptance (docs/COOP.md, "The live acceptance"; C2d-5): two games share a park through the real server, with the game's data, driven through the
// game's own screens. Starts the real server (OpenTPW.Server) on 127.0.0.1 with a fresh data folder and no two-step login, registers two throwaway players with random
// names and passwords, and runs two games, each a native game (silent and offscreen, with its own configuration, online and save folders) or a page (a Chrome of its own,
// tools/coop-live-page.mjs): the host shares Jungle from its pause menu, and the guest, started once the host published its code, joins it from the lobby. The games meet at
// barriers through status files in the shared evidence folder (CoopLiveTest; a page's relayed by the driver); this driver starts the guest, restarts the server in the
// recovery scenario, stops both games when one fails, and checks both games' evidence
// against each other once both completed: one session, the guest's build of the host's park, the host's line applied by the guest with checkpoints compared after it,
// the figures seen both ways, the guest's refused line, the chat both ways, Go to, and the session's end; the baseline and the endurance run with no resync and no
// desync, the recovery run with the session back and a second line after the restart, the endurance run at the fastest speed for its minutes, and the management run
// (C4b-2) with the guest's line as its purchase, the attractions right given through the dialog, a guest's typed price and ride door at their records, and exactly one
// resync, the guest's forced one, with its queued price sent once after it.
//
// Usage:
//   node tools/coop-live.mjs --game <OpenTPW.dll> --server <OpenTPW.Server.dll> --out <directory> [--game-path <Theme Park World>] [--scenario baseline|recovery|endurance|management]
//        [--minutes M] [--config-source <directory>] [--host native|page] [--guest native|page] [--web <published wwwroot>] [--runtime aot|interpreter]
//        [--chrome <executable>] [--headed] [--timeout-minutes T]
// A page side (C2d-5b, tools/coop-live-page.mjs) is the game in a page in a Chrome of its own, through a local server of its own in front of the real one, which then
// serves the web build given with --web.
// --out (a directory that does not exist yet) gets run.json (every check, both games' evidence and the run's facts), host.log, guest.log and server.log, the shared
// evidence folder and both games' own folders. The passwords never reach the games' arguments, a file or a log: the games get them in their environment, and every
// log and failure this driver writes has them replaced. The games and the server run on the `dotnet` the DOTNET environment variable names, else the one on the path.
// It exits 1 when the acceptance fails.
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { createWriteStream, promises as fs } from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { performance } from 'node:perf_hooks';
import { fileURLToPath } from 'node:url';
import { sleep } from './web-perf/lib.mjs';
import { checkPageSides, checkPictures, PAGE_START_SECONDS, PageSide } from './coop-live-page.mjs';

const SCENARIOS = ['baseline', 'recovery', 'endurance', 'management'];
const SIDES = ['native', 'page'];
const DEFAULT_CHROME = {
	darwin: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
	linux: '/usr/bin/google-chrome',
	win32: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
}[process.platform];
/** How long a game may take from its start to its first status, and the guest to publish that it follows the host's park (a download and a build of the park). */
const START_SECONDS = 300;
/** How long the server is down in the recovery scenario before it starts again, in seconds. */
const OUTAGE_SECONDS = 3;
/** How long the logs may take to finish once their processes ended, in seconds. */
const LOG_SECONDS = 10;
/** A game whose status does not advance for this long while it runs fails: each publishes a heartbeat at least every five seconds (CoopLiveTest.HeartbeatSeconds). */
export const STATUS_STALL_SECONDS = 60;
/** How long the cleanup waits for a page still starting, once its side was stopped, in seconds. */
const START_JOIN_SECONDS = 30;

export function parseArguments(argv) {
	const options = {
		game: null, server: null, out: null, gamePath: process.env.OPENTPW_GAME_PATH ?? null, scenario: 'baseline', minutes: 30,
		configSource: process.env.OPENTPW_SMOKE_CONFIG_SOURCE ?? defaultConfigSource(), host: 'native', guest: 'native', timeoutMinutes: null,
		web: null, chrome: process.env.CHROME ?? DEFAULT_CHROME, headed: false, runtime: 'unknown',
	};
	for (let index = 2; index < argv.length; index++) {
		const [flag, inline] = argv[index].split(/=(.*)/s);
		const value = () => {
			const given = inline ?? argv[++index];
			if (given === undefined)
				throw new Error(`${flag} needs a value`);
			return given;
		};
		const positive = (text, what) => {
			if (!/^[0-9]+(\.[0-9]+)?$/.test(text) || Number(text) <= 0)
				throw new Error(`${flag} takes a positive number of ${what}, not '${text}'`);
			return Number(text);
		};
		switch (flag) {
			case '--game': options.game = value(); break;
			case '--server': options.server = value(); break;
			case '--out': options.out = value(); break;
			case '--game-path': options.gamePath = value(); break;
			case '--scenario': options.scenario = value(); break;
			case '--minutes': options.minutes = positive(value(), 'minutes'); break;
			case '--config-source': options.configSource = value(); break;
			case '--host': options.host = value(); break;
			case '--guest': options.guest = value(); break;
			case '--timeout-minutes': options.timeoutMinutes = positive(value(), 'minutes'); break;
			case '--web': options.web = value(); break;
			case '--chrome': options.chrome = value(); break;
			case '--headed': options.headed = true; break;
			case '--runtime': options.runtime = value(); break;
			default: throw new Error(`Unknown option ${flag}`);
		}
	}
	if (!options.game || !options.server || !options.out)
		throw new Error('Pass --game <OpenTPW.dll>, --server <OpenTPW.Server.dll> and --out <directory>.');
	if (!options.gamePath)
		throw new Error('Pass --game-path <Theme Park World> (or set OPENTPW_GAME_PATH).');
	if (!SCENARIOS.includes(options.scenario))
		throw new Error(`--scenario is one of ${SCENARIOS.join(', ')}, not '${options.scenario}'`);
	for (const side of ['host', 'guest'])
		if (!SIDES.includes(options[side]))
			throw new Error(`--${side} is ${SIDES.join(' or ')}, not '${options[side]}'`);
	if ((options.host === 'page' || options.guest === 'page') && (!options.web || !options.chrome))
		throw new Error('A page side needs --web <published wwwroot> and Chrome (--chrome <executable>).');
	if (!['aot', 'interpreter', 'unknown'].includes(options.runtime))
		throw new Error(`--runtime is aot or interpreter (the web build's), not '${options.runtime}'`);
	// the run's own deadline: the steps' deadlines come first; this one catches a driver that waits for ever
	options.timeoutMinutes ??= 30 + (options.scenario === 'endurance' ? options.minutes : 0);
	return options;
}

/** The developer's own configuration folder, whose settings files and art folders the games' private configurations take (as the native smokes do). */
function defaultConfigSource() {
	return process.platform === 'darwin' ? path.join(os.homedir(), 'Library', 'Application Support', 'OpenTPW')
		: process.platform === 'win32' ? path.join(process.env.APPDATA ?? os.homedir(), 'OpenTPW')
			: path.join(os.homedir(), '.config', 'OpenTPW');
}

/** A throwaway player: a name the login field takes (at most 16 characters) and a random password of 16. */
export function throwawayPlayer(prefix) {
	return { name: `${prefix}${randomBytes(4).toString('hex').slice(0, 7)}`, password: randomBytes(12).toString('base64url') };
}

/** `text` with every secret replaced. */
export function redact(text, secrets) {
	let result = String(text);
	for (const secret of secrets)
		if (secret)
			result = result.split(secret).join('[redacted]');
	return result;
}

/** A free port on 127.0.0.1, which the server keeps over its restart. */
async function freePort() {
	const server = net.createServer();
	await new Promise((resolve, reject) => {
		server.once('error', reject);
		server.listen(0, '127.0.0.1', resolve);
	});
	const { port } = server.address();
	await new Promise(resolve => server.close(resolve));
	return port;
}

/**
 * A child's output into a log file, secrets replaced: each pipe's lines whole (a secret split over two chunks of one pipe is still found), written as they end, and a pipe's
 * last line once it ends. The file is finished when both pipes have ended: the returned promise resolves then, and rejects with the file's error if it cannot be written
 * (the run fails on it, after its cleanup).
 */
export function logTo(child, file, secrets) {
	const stream = createWriteStream(file, { flags: 'a' });
	const pipes = [child.stdout, child.stderr].filter(Boolean);
	let open = pipes.length;
	let failed = null;
	const finished = new Promise((resolve, reject) => {
		stream.once('finish', resolve);
		// every error of the file is the log's, not the driver's end
		stream.on('error', error => {
			failed ??= error;
			reject(error);
		});
	});
	finished.catch(() => {});
	for (const pipe of pipes) {
		let pending = '';
		let ended = false;
		pipe.on('data', chunk => {
			if (failed)
				return;
			pending += chunk;
			const lines = pending.split('\n');
			pending = lines.pop();
			for (const line of lines)
				stream.write(redact(line, secrets) + '\n');
		});
		const end = () => {
			if (ended)
				return;
			ended = true;
			if (pending && !failed)
				stream.write(redact(pending, secrets) + '\n');
			pending = '';
			if (--open === 0)
				stream.end();
		};
		pipe.once('end', end);
		pipe.once('close', end);
	}
	if (open === 0)
		stream.end();
	return finished;
}

/** Waits for `child` to exit, at most `milliseconds`; true when it did. */
async function exited(child, milliseconds) {
	if (!child || child.exitCode !== null || child.signalCode !== null)
		return true;
	await Promise.race([new Promise(resolve => child.once('exit', resolve)), sleep(milliseconds)]);
	return child.exitCode !== null || child.signalCode !== null;
}

/** Ends `child` with `signal`, then for good after a few seconds; true once it has exited. */
async function stop(child, signal = 'SIGTERM') {
	if (!child || child.pid === undefined || child.exitCode !== null || child.signalCode !== null)
		return true;
	child.kill(signal);
	if (await exited(child, 10000))
		return true;
	child.kill('SIGKILL');
	return exited(child, 3000);
}

/** The real server on a fixed port with its data folder (kept over a restart), its output into server.log. */
class Server {
	/** `web` is the web build the server serves its pages from (a page side's), or null. */
	constructor(dll, data, port, log, secrets, web = null) {
		Object.assign(this, { dll, data, port, log, secrets, web, child: null, starts: 0, logs: [] });
		this.origin = `http://127.0.0.1:${port}`;
	}

	async start() {
		const args = [this.dll, '--urls', this.origin, `--OpenTPW:DataDirectory=${this.data}`, '--OpenTPW:RequireTotp=false',
			...(this.web ? [`--OpenTPW:WebClientDirectory=${this.web}`] : []),
			'--Logging:LogLevel:Default=Warning', '--Logging:LogLevel:Microsoft.Hosting.Lifetime=Information'];
		const child = this.child = spawn(process.env.DOTNET ?? 'dotnet', args, { cwd: path.dirname(this.dll), stdio: ['ignore', 'pipe', 'pipe'],
			env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1' } });
		this.starts++;
		this.logs.push(logTo(child, this.log, this.secrets));
		// the start's own view of the output: its last few kilobytes, until the server listens (the log keeps all of it)
		let output = '';
		await new Promise((resolve, reject) => {
			const settle = (error) => {
				clearTimeout(timer);
				child.stdout.off('data', listening);
				child.stderr.off('data', listening);
				child.off('exit', exited);
				error ? reject(error) : resolve();
			};
			const listening = chunk => {
				output = (output + chunk).slice(-8192);
				if (output.includes(`Now listening on: ${this.origin}`))
					settle();
			};
			const exited = code => settle(new Error(`The server exited (${code}) before it listened:\n${redact(output.slice(-2000), this.secrets)}`));
			const timer = setTimeout(() => settle(new Error(`The server did not listen within 60 s:\n${redact(output.slice(-2000), this.secrets)}`)), 60000);
			child.stdout.on('data', listening);
			child.stderr.on('data', listening);
			child.once('error', error => settle(new Error(`The server could not start: ${error.message}`)));
			child.once('exit', exited);
		});
	}

	/** A crash and a start again on the same port and data (the recovery scenario): the server is killed, not stopped. */
	async restart() {
		await stop(this.child, 'SIGKILL');
		await sleep(OUTAGE_SECONDS * 1000);
		await this.start();
	}

	stop() { return stop(this.child); }
}

/** Registers a player on the server (no two-step login: the account is taken at once), within 30 seconds. */
async function register(origin, player, secrets) {
	const response = await fetch(`${origin}/api/v1/accounts`, { method: 'POST', headers: { 'content-type': 'application/json' },
		body: JSON.stringify({ name: player.name, password: player.password }), signal: AbortSignal.timeout(30000) });
	if (response.status !== 201)
		throw new Error(`Registering ${player.name} answered ${response.status}: ${redact((await response.text()).slice(0, 300), secrets)}`);
}

/**
 * A game's own folders: a configuration with copies of the developer's small settings files and links to its art folders (never its online folder; the runs never write
 * the real configuration), an online folder and a save folder.
 */
async function sideFolders(root, configSource) {
	const folders = { root, config: path.join(root, 'config'), online: path.join(root, 'online'), save: path.join(root, 'save') };
	for (const folder of [folders.config, folders.online, folders.save])
		await fs.mkdir(folder, { recursive: true });
	const entries = await fs.readdir(configSource, { withFileTypes: true }).catch(() => []);
	for (const entry of entries) {
		if (entry.isFile() && entry.name.endsWith('.json'))
			await fs.copyFile(path.join(configSource, entry.name), path.join(folders.config, entry.name));
		else if (entry.isDirectory() && ['art', 'hero-art', 'texture-packs'].includes(entry.name))
			await fs.symlink(path.join(configSource, entry.name), path.join(folders.config, entry.name));
	}
	return folders;
}

/** Starts one native game of the acceptance: the evidence folder is shared, everything else its own; its credentials and the code in its environment only. */
function startGame(role, options, folders, evidence, player, origin, code, secrets) {
	const args = [path.resolve(options.game), '--game-path', options.gamePath, '--coop-live', role === 'host' ? 'host' : 'join', '--coop-live-evidence', evidence,
		'--online-dir', folders.online, '--no-advisor'];
	const env = {
		...process.env, OPENTPW_SILENT: '1', OPENTPW_OFFSCREEN: '1', OPENTPW_CONFIG_DIR: folders.config, OPENTPW_SAVE_DIR: folders.save,
		OPENTPW_COOP_LIVE_SERVER: origin, OPENTPW_COOP_LIVE_NAME: player.name, OPENTPW_COOP_LIVE_PASSWORD: player.password,
		OPENTPW_COOP_LIVE_SCENARIO: options.scenario, OPENTPW_COOP_LIVE_MINUTES: String(options.minutes), DOTNET_CLI_TELEMETRY_OPTOUT: '1',
	};
	if (code)
		env.OPENTPW_COOP_LIVE_CODE = code;
	const child = spawn(process.env.DOTNET ?? 'dotnet', args, { cwd: folders.root, env, stdio: ['ignore', 'pipe', 'pipe'] });
	child.role = role;
	child.once('error', error => child.failedToStart = error.message);
	child.logged = logTo(child, path.join(options.out, `${role}.log`), secrets);
	return child;
}

/** A native side: the game as a process of its own (startGame), its evidence written straight into the shared folder. */
class NativeSide {
	constructor({ role, options, folders, evidence, player, server, secrets }) {
		Object.assign(this, { role, options, folders, evidence, player, server, secrets, kind: 'native', child: null, logged: Promise.resolve() });
	}

	start(code) {
		this.child = startGame(this.role, this.options, this.folders, this.evidence, this.player, this.server.origin, code, this.secrets);
		this.logged = this.child.logged;
	}

	/** A native game writes its status itself: nothing to relay. A promise, as a page's relay is, so a caller's catch holds for both (the stop after a failure relays every side). */
	async relay() { return null; }

	ending() { return ending(this.child); }

	facts() { return { kind: 'native' }; }

	/** A game the run left behind gets a little time to end on the driver's stop, then is ended; true once it has exited. */
	async stop() { return await exited(this.child, 20000) || await stop(this.child); }
}

/** A party's status (`host`, `guest` or `driver`), or null while it has none. */
async function readStatus(evidence, role) {
	try {
		return JSON.parse(await fs.readFile(path.join(evidence, `${role}-status.json`), 'utf8'));
	} catch {
		return null;
	}
}

/** Writes the driver's status whole (the games read it as they read each other's). */
async function writeStatus(evidence, status) {
	const file = path.join(evidence, 'driver-status.json');
	await fs.writeFile(`${file}.tmp`, JSON.stringify(status, null, 2));
	await fs.rename(`${file}.tmp`, file);
}

/** How a game ended, in words, or null while it runs. */
function ending(child) {
	if (!child)
		return null;
	if (child.failedToStart)
		return `could not start: ${child.failedToStart}`;
	if (child.exitCode === null && child.signalCode === null)
		return null;
	return child.exitCode === 0 ? 'completed' : `exited with ${child.exitCode ?? child.signalCode}`;
}

/**
 * Checks both games' evidence against each other (docs/COOP.md, "The live acceptance"): every check is recorded with what it compared, and the acceptance passes only
 * when every one holds.
 */
export function checkEvidence(host, guest, scenario, minutes) {
	const checks = [];
	const check = (what, passed, detail) => checks.push({ what, passed: Boolean(passed), detail });
	const sighting = (evidence, what, name) => (evidence?.sightings ?? []).find(item => item.what === what && item.name === name && (item.drawnNames ?? []).includes(name)
		&& item.drawnFigures >= 2);
	check('both games completed every step', host?.completed && guest?.completed, { host: host?.failure ?? null, guest: guest?.failure ?? null });
	check('both games are in the same session, the host\'s', host?.session?.id && host.session.id === guest?.session?.id && host.session.code === guest.session.code
		&& host.session.title === guest.session.title && guest.session.members?.some(member => member.host && member.name === host.player),
	{ host: host?.session, guest: guest?.session });
	check('the guest\'s park was built from the host\'s capture', (guest?.joinTimings ?? []).some(timing => timing.Outcome === 'joined'), guest?.joinTimings);
	check('the host\'s line, laid through its HUD, reached the guest\'s park at its record', Number.isInteger(host?.build?.record) && guest?.applied?.record === host.build.record
		&& guest.applied.lastRecord >= host.build.record && host.build.cells?.length === 6, { build: host?.build, applied: guest?.applied });
	check('checkpoints compared after the line', guest?.applied?.checksAfter > guest?.applied?.checksAtLine && guest?.stream?.checks > 0,
		{ applied: guest?.applied, stream: guest?.stream });
	check('the guest\'s own line was refused, its cells left free', guest?.refusal?.refusalsAfter === guest?.refusal?.refusalsBefore + 1, guest?.refusal);
	check('once the host let the guest build, the guest\'s next line was laid at its record on both parks', host?.grant?.seat > 0 && host.grant.seat === guest?.ownBuild?.seat
		&& Number.isInteger(guest.ownBuild.record) && host?.guestBuild?.record === guest.ownBuild.record && guest.ownBuild.cells?.length === 6
		&& JSON.stringify(host.guestBuild.cells) === JSON.stringify(guest.ownBuild.cells) && guest.ownBuild.checksAfter > guest.ownBuild.checksAtLine,
	{ grant: host?.grant, ownBuild: guest?.ownBuild, guestBuild: host?.guestBuild });
	check('each game drew the other\'s figure with its name, and saw it where its view moved', sighting(host, 'guest', guest?.player) && sighting(host, 'guest-moved', guest?.player)
		&& sighting(guest, 'host', host?.player) && sighting(guest, 'host-moved', host?.player), { host: host?.sightings, guest: guest?.sightings });
	check('the chat both ways, through the chat mode\'s entry', host?.chat?.said && host.chat.said === guest?.chat?.heard && guest.chat.said && guest.chat.said === host.chat.heard,
		{ host: host?.chat, guest: guest?.chat });
	check('the guest rode a ride from the chat mode and went to the host\'s figure with Go to', guest?.ride?.ride && guest?.goTo?.name === host?.player,
		{ ride: guest?.ride, goTo: guest?.goTo });
	check('the host ended the session, and its park ran on', host?.ended?.outcome === 'Ended' && (guest?.phases ?? []).includes('guest ended'),
		{ ended: host?.ended, guestPhases: guest?.phases });
	if (scenario === 'recovery') {
		check('after the server\'s restart the guest followed again and the host\'s second line reached it', Number.isInteger(host?.rebuild?.record)
			&& guest?.reapplied?.record === host.rebuild.record && guest.reapplied.checksAfter > guest.reapplied.checksAtLine,
		{ rebuild: host?.rebuild, reapplied: guest?.reapplied, recovery: guest?.recovery });
		check('the session came back with both players connected', (host?.recovery?.session?.members ?? []).length === 2
			&& host.recovery.session.members.every(member => member.connected), host?.recovery?.session);
		check('both games\' links resumed the session and their chats reconnected after the restart', [host, guest].every(evidence => reconnectedAfterRestart(evidence?.recovery)),
			{ host: host?.recovery && { atRestart: host.recovery.linksAtRestart, after: host.recovery.links },
				guest: guest?.recovery && { atRestart: guest.recovery.linksAtRestart, after: guest.recovery.links } });
		check('the guest saw the host\'s figure move after the restart', sighting(guest, 'host-moved-again', host?.player), guest?.sightings);
	} else {
		if (scenario !== 'management')
			check('the session ran with no resync and no desync', guest?.streams === 1 && guest?.stream?.desyncs === 0 && guest?.stream?.failedJoins === 0
				&& !(guest?.phases ?? []).includes('guest resyncing'), { streams: guest?.streams, stream: guest?.stream, phases: guest?.phases });
		check('neither game\'s link was lost', [host, guest].every(evidence => evidence?.links?.resumes === 0
			&& !(evidence.links.states ?? []).some(item => item.state === 'Reconnecting') && evidence.links.chatReconnects === 0), { host: host?.links, guest: guest?.links });
	}
	if (scenario === 'management')
		checkManagement(host, guest, check);
	if (scenario === 'endurance') {
		const endurance = host?.endurance;
		const samples = endurance?.samples ?? [];
		const seconds = Number.isFinite(endurance?.ended - endurance?.started) ? endurance.ended - endurance.started : 0;
		// the rates run from the speed's setting (its tick count then) to the last sample
		const rates = Number.isFinite(endurance?.startTicks) ? tickRates([{ at: endurance.started, ticks: endurance.startTicks }, ...samples]) : [];
		// the guest's samples run from its ticks before the speed to its last sample, which the host asks for before it sets the normal speed back
		const guestSamples = guest?.endurance ?? [];
		const guestSpan = guestSamples.length > 1 ? guestSamples.at(-1).at - guestSamples[0].at : 0;
		const guestRates = tickRates(guestSamples);
		// both sampled all the way: no two samples further apart than twice the sampling step, from the speed's setting (the guest: its first ticks) to the last one
		const hostTimes = Number.isFinite(endurance?.started) ? [endurance.started, ...samples.map(sample => sample.at)] : [];
		const guestTimes = guestSamples.map(sample => sample.at);
		const hostGap = largestGap(hostTimes), guestGap = largestGap(guestTimes);
		check(`the fastest speed was followed for ${minutes} minutes`, seconds >= minutes * 60 && samples.length > 0 && hostGap <= 2 * ENDURANCE_SAMPLE_SECONDS
			&& samples.every(sample => sample.speed === 'Fastest') && rates.length > 0 && rates.every(rate => rate >= FASTEST_TICKS_PER_SECOND)
			&& guestSamples.at(-1)?.final === true && guestSpan >= minutes * 60 && guestGap <= 2 * ENDURANCE_SAMPLE_SECONDS && guestRates.length > 0
			&& guestRates.every(rate => rate >= FASTEST_TICKS_PER_SECOND),
		{ seconds, guestSeconds: guestSpan, hostSamples: samples.length, guestSamples: guestSamples.length, largestHostSampleGap: hostGap, largestGuestSampleGap: guestGap,
			slowestHostRate: Math.min(...rates), slowestGuestRate: Math.min(...guestRates), longestFrameGaps: { host: host?.longestFrameGap ?? null, guest: guest?.longestFrameGap ?? null } });
	}
	return checks;
}

/** The reason the guest's forced desync logs (CoopLiveTest.ForcedDesyncReason). */
export const FORCED_DESYNC_REASON = 'forced by the live test';

/**
 * The management run's checks (C4b-2, docs/COOP.md "The live acceptance"): the guest's own line as its purchase on both parks, the attractions right through the dialog, the
 * guest's typed price and its ride door at their records on both parks, and exactly one resync, the guest's forced one, whose queued price was sent once after it. The forced
 * desync takes the stream's own path, so within the session's early window after the join it counts as the join's failure, as a real one would: at most that one.
 */
function checkManagement(host, guest, check) {
	const purchase = host?.guestPurchase, own = guest?.ownPurchase;
	const same = (left, right, keys) => keys.every(key => left?.[key] !== undefined && left[key] === right?.[key]);
	check('the guest\'s line was a purchase: each park\'s balance at its record fell by its charge, the same on both', Number.isFinite(purchase?.charged) && purchase.charged > 0
		&& purchase.before - purchase.after === purchase.charged && purchase.cells === 6 && same(purchase, own, ['before', 'after', 'charged', 'cells']),
	{ host: purchase, guest: own });
	check('the host gave the attractions right through the rights dialog', host?.attractionsGrant?.seat > 0 && host.attractionsGrant.seat === guest?.ownBuild?.seat
		&& /\bAttractions\b/.test(host.attractionsGrant.rights ?? ''), host?.attractionsGrant);
	const price = guest?.management?.price, hostPrice = host?.management?.price;
	check('the guest\'s typed price applied at its record on both parks', Number.isInteger(price?.record) && same(price, hostPrice, ['shop', 'price', 'record']),
		{ guest: price, host: hostPrice });
	const doors = guest?.management?.doors ?? [], hostDoors = host?.management?.doors ?? [];
	check('the guest\'s ride door closed and opened again, each at its record on both parks', doors.length === 2 && doors[0].open === false && doors[1].open === true
		&& doors.every(door => Number.isInteger(door.record)) && hostDoors.length === 2 && doors.every((door, index) => same(door, hostDoors[index], ['ride', 'open', 'record'])),
	{ guest: doors, host: hostDoors });
	const forced = guest?.management?.forced, seen = host?.management?.forced;
	check('exactly one resync, the guest\'s forced one: its price waited unsent at the cut and applied once after the swap', guest?.streams === 2 && forced?.entry === 'Unsent'
		&& forced?.desync?.kind === 'Protocol' && forced.desync.reason === FORCED_DESYNC_REASON && forced?.after?.generation === forced.generation + 1 && forced.after.checks >= 2
		&& forced.after.desyncs === forced.desync.id && guest?.stream?.desyncs === forced.desync.id && forced.failedJoinsBefore === 0 && forced.failedJoinsAfter <= 1
		&& guest?.stream?.failedJoins === forced.failedJoinsAfter && seen?.desyncs?.length === 1 && seen.desyncs[0].id === forced.desync.id
		&& seen.desyncs[0].kind === 'Protocol' && seen.capturesAfter === seen.capturesBefore + 1 && seen.secondPriceRecords === 1,
	{ guest: forced, host: seen, streams: guest?.streams, stream: guest?.stream });
}

/** How often both games sample the park's ticks at the fastest speed, in seconds (CoopLiveTest.EnduranceSampleSeconds). */
export const ENDURANCE_SAMPLE_SECONDS = 5;

/** The largest wait between consecutive times (seconds), or Infinity without two of them. */
function largestGap(times) {
	return times.length < 2 ? Infinity : Math.max(...times.slice(1).map((time, index) => time - times[index]));
}

/** The fewest park ticks a second of wall time the endurance run's samples may show: three times the normal speed's 60, so only the fastest (240) passes. */
export const FASTEST_TICKS_PER_SECOND = 180;

/** The shortest stretch of wall time a tick rate is taken over, in seconds: a guest's ticks come in bursts with the host's frames (every 100 ms at most). */
export const RATE_SECONDS = 5;

/**
 * The park's ticks a second of wall time between samples, each over at least RATE_SECONDS: a sample closer than that to the last one kept (the guest's last sample, which
 * the host asks for right after a regular one) takes that one's place, so its stretch joins the one before.
 */
function tickRates(samples) {
	const kept = [];
	for (const sample of samples) {
		if (kept.length === 0 || sample.at - kept.at(-1).at >= RATE_SECONDS)
			kept.push(sample);
		else if (kept.length > 1)
			kept[kept.length - 1] = sample;
	}
	return kept.slice(1).map((sample, index) => (sample.ticks - kept[index].ticks) / (sample.at - kept[index].at));
}

/**
 * Checks a game's status as the driver reads it: once seen, its run stays the same (a page that loaded again would start another), and while the game runs its revision
 * must grow at least every STATUS_STALL_SECONDS (its heartbeat). `freshness` keeps what each role showed last; `ended` is the side's ending, null while it runs.
 */
export function checkFreshness(freshness, role, status, ended, now) {
	if (!status)
		return;
	const known = freshness[role];
	if (known && status.run !== known.run)
		throw new Error(`The ${role}'s status comes from another run (${status.run}, not ${known.run})`);
	if (!known || status.revision > known.revision) {
		freshness[role] = { run: status.run, revision: status.revision, at: now };
		return;
	}
	if (status.revision < known.revision)
		throw new Error(`The ${role}'s status went back from revision ${known.revision} to ${status.revision}`);
	if (ended === null && now - known.at > STATUS_STALL_SECONDS * 1000)
		throw new Error(`The ${role}'s status did not advance for ${STATUS_STALL_SECONDS} s (at ${status.step ?? 'its start'}, revision ${status.revision})`);
}

/** How far the driver's observation of the endurance interval may fall short of the games' own: its status reads lag each end by up to a relay cycle or two, in seconds. */
export const OBSERVATION_SLACK_SECONDS = 2;

/** The fastest speed held for the run's minutes on the driver's own clock too, from the host's word that it set it to its word that the normal speed is back. */
export function checkObservedEndurance(observed, minutes) {
	const seconds = observed?.started != null && observed?.done != null ? (observed.done - observed.started) / 1000 : 0;
	return { what: `the driver saw the fastest speed held for ${minutes} minutes on its own clock`, passed: seconds >= minutes * 60 - OBSERVATION_SLACK_SECONDS,
		detail: { seconds } };
}

/** Whether a game's link resumed its session and its chat reconnected after the restart's barrier: both counts grew, and its link went from reconnecting to open after it. */
function reconnectedAfterRestart(recovery) {
	const before = recovery?.linksAtRestart, after = recovery?.links;
	if (!before || !after || !(after.resumes > before.resumes) || !(after.chatReconnects > before.chatReconnects))
		return false;
	const states = (after.states ?? []).slice((before.states ?? []).length).map(item => item.state);
	const lost = states.indexOf('Reconnecting');
	return lost >= 0 && states.indexOf('Open', lost) > lost;
}

async function run(options) {
	const out = path.resolve(options.out);
	const evidence = path.join(out, 'evidence');
	const record = { started: new Date().toISOString(), scenario: options.scenario, minutes: options.scenario === 'endurance' ? options.minutes : null,
		host: options.host, guest: options.guest, runtime: options.host === 'page' || options.guest === 'page' ? options.runtime : null, state: 'failed', checks: [] };
	const players = { host: throwawayPlayer('Host'), guest: throwawayPlayer('Guest') };
	const secrets = [players.host.password, players.guest.password];
	const sides = {};
	let server, guestStarting = null;
	try {
		await fs.mkdir(evidence);
		await fs.access(options.game).catch(() => { throw new Error(`${options.game} does not exist: build the game first`); });
		await fs.access(options.server).catch(() => { throw new Error(`${options.server} does not exist: build the server first`); });
		await fs.access(options.gamePath).catch(() => { throw new Error(`${options.gamePath} does not exist: pass the Theme Park World folder`); });
		const pages = options.host === 'page' || options.guest === 'page';
		if (pages)
			await fs.access(path.join(options.web, 'index.html')).catch(() => { throw new Error(`${options.web} has no index.html: publish the web build first`); });
		const data = path.join(out, 'server-data');
		await fs.mkdir(data);
		server = new Server(path.resolve(options.server), data, await freePort(), path.join(out, 'server.log'), secrets, pages ? path.resolve(options.web) : null);
		await server.start();
		record.server = server.origin;
		await register(server.origin, players.host, secrets);
		await register(server.origin, players.guest, secrets);
		record.players = { host: players.host.name, guest: players.guest.name };
		// the driver's status, which the games read as they read each other's: the restart, a stop, and the pictures and page inputs it has given
		const driver = { restarted: null, abort: null, pictures: [], inputs: {} };
		driver.publish = () => writeStatus(evidence, { restarted: driver.restarted, abort: driver.abort, pictures: driver.pictures, inputs: driver.inputs });
		await driver.publish();
		for (const role of ['host', 'guest'])
			sides[role] = options[role] === 'page'
				? new PageSide({ role, options, out, evidence, player: players[role], server, redact: text => redact(text, secrets) })
				: new NativeSide({ role, options, folders: await sideFolders(path.join(out, role), options.configSource), evidence, player: players[role], server, secrets });

		const begun = performance.now();
		const deadline = begun + options.timeoutMinutes * 60000;
		await sides.host.start(null);
		let restartedAt = null;
		let guestStarted = null;
		let guestStartError = null;
		// the fastest speed on the driver's own clock: from the host's word that it set it to its word that the normal speed is back
		const observed = { started: null, done: null };
		// each game's status: its run, its revision, and when that last grew, on the driver's own clock
		const freshness = {};
		// a page's start takes longer than a process's: its Chrome and its game's start, which copies the game's files
		const guestStartSeconds = START_SECONDS + (options.guest === 'page' ? PAGE_START_SECONDS : 0);
		for (;;) {
			// the page sides' relay: their statuses out, the others' in, their pictures and inputs (a stalled or crashed page throws)
			for (const side of Object.values(sides))
				await side.relay(driver);
			const host = await readStatus(evidence, 'host');
			const guest = await readStatus(evidence, 'guest');
			// the guest starts once the host published its session's code; the host's relay goes on while a page starts
			if (guestStarted === null && typeof host?.code === 'string' && host.code.length > 0) {
				record.code = host.code;
				guestStarted = performance.now();
				guestStarting = Promise.resolve().then(() => sides.guest.start(host.code)).catch(error => guestStartError = error);
			}
			if (guestStartError)
				throw new Error(`The guest could not start: ${guestStartError.message}`);
			for (const [role, status] of [['host', host], ['guest', guest]])
				checkFreshness(freshness, role, status, sides[role].ending(), performance.now());
			if (options.scenario === 'endurance') {
				if (observed.started === null && host?.enduranceStarted === true)
					observed.started = performance.now();
				if (observed.done === null && host?.enduranceDone === true)
					observed.done = performance.now();
			}
			// the recovery scenario: the server crashes and starts again once both games stand ready for it
			if (options.scenario === 'recovery' && restartedAt === null && host?.readyForRestart === true && guest?.readyForRestart === true) {
				await server.restart();
				restartedAt = new Date().toISOString();
				record.restart = { at: restartedAt, outageSeconds: OUTAGE_SECONDS, starts: server.starts };
				driver.restarted = restartedAt;
				await driver.publish();
			}
			const ends = Object.fromEntries(Object.entries(sides).map(([role, side]) => [role, side.ending()]));
			const failed = Object.entries(ends).find(([, end]) => end && end !== 'completed');
			if (failed) {
				// one game failed: the other is told to stop (a page through one more relay), and its own failure follows
				driver.abort = `the ${failed[0]} ${failed[1]}`;
				await driver.publish();
				// a page hears the stop through its relay, and ends with its evidence written: a few seconds for it
				const until = performance.now() + 10000;
				while (Object.values(sides).some(side => side.kind === 'page' && side.started && !side.ending()) && performance.now() < until) {
					for (const side of Object.values(sides))
						await side.relay(driver).catch(() => {});
					await sleep(250);
				}
				throw new Error(`The ${failed[0]} ${failed[1]} (at ${(failed[0] === 'host' ? host : guest)?.step ?? 'its start'}: ${(failed[0] === 'host' ? host : guest)?.failed ?? 'see its log'})`);
			}
			if (ends.host === 'completed' && ends.guest === 'completed')
				break;
			// a game that never reaches its first frames fails at its start, not at the run's deadline
			if (guestStarted === null && performance.now() - begun > START_SECONDS * 1000 && !host?.code)
				throw new Error(`The host published no code within ${START_SECONDS} s (at ${host?.step ?? 'its start'})`);
			if (guestStarted !== null && !guest && performance.now() - guestStarted > guestStartSeconds * 1000)
				throw new Error(`The guest published no status within ${guestStartSeconds} s of its start`);
			if (performance.now() > deadline) {
				driver.abort = `the run's deadline of ${options.timeoutMinutes} minutes`;
				await driver.publish();
				throw new Error(`The run did not end within ${options.timeoutMinutes} minutes (host at ${host?.step}, guest at ${guest?.step})`);
			}
			await sleep(250);
		}
		const hostEvidence = JSON.parse(await fs.readFile(path.join(evidence, 'host-evidence.json'), 'utf8'));
		const guestEvidence = JSON.parse(await fs.readFile(path.join(evidence, 'guest-evidence.json'), 'utf8'));
		record.environment = hostEvidence.environment;
		// a page's last word: its frames went on, and none failed, to the run's end
		for (const side of Object.values(sides))
			await side.refresh?.();
		record.sides = { host: sides.host.facts(), guest: sides.guest.facts() };
		const endurance = options.scenario === 'endurance' ? [checkObservedEndurance(observed, options.minutes)] : [];
		record.checks = [
			...checkEvidence(hostEvidence, guestEvidence, options.scenario, options.minutes),
			...endurance,
			await checkPictures({ host: hostEvidence, guest: guestEvidence }, name => typeof name === 'string' && !name.includes('/') && !name.includes('\\')
				? fs.readFile(path.join(evidence, name)).catch(() => null) : null, { ...record.sides.host.pictureSizes, ...record.sides.guest.pictureSizes }),
			...checkPageSides(record.sides, restartedAt === null ? null : Date.parse(restartedAt)),
		];
		record.evidence = { host: hostEvidence, guest: guestEvidence };
		const failedChecks = record.checks.filter(item => !item.passed);
		if (failedChecks.length)
			throw new Error(`${failedChecks.length} check(s) failed: ${failedChecks.map(item => item.what).join('; ')}`);
		record.state = 'passed';
	} catch (error) {
		record.message = redact(error.message, secrets);
	} finally {
		// every side ends: a game the run left behind gets a little time to end on the driver's stop, then is ended; what could not be ended fails the run
		const cleanup = [];
		for (const [role, side] of Object.entries(sides))
			if (!await side.stop().catch(() => false))
				cleanup.push(`the ${role} had not exited`);
		// a page still starting ends with its Chrome, which its side's stop ended; it is not waited for longer than that may take
		if (guestStarting && await Promise.race([guestStarting.then(() => true), sleep(START_JOIN_SECONDS * 1000).then(() => false)]) === false)
			cleanup.push(`the guest's start had not ended ${START_JOIN_SECONDS} s after its stop`);
		if (server && !await server.stop())
			cleanup.push(`the server (process ${server.child?.pid}) had not exited`);
		// every log is whole before the run's record is written: one that could not be written, or did not finish, fails the run
		const logs = [...Object.entries(sides).map(([role, side]) => [`the ${role}'s log`, side.logged]), ...(server?.logs ?? []).map(log => ['the server\'s log', log])];
		const settled = logs.map(() => null);
		await Promise.race([Promise.all(logs.map(([, log], index) => log.then(() => settled[index] = 'finished', error => settled[index] = `could not be written: ${error.message}`))),
			sleep(LOG_SECONDS * 1000)]);
		logs.forEach(([name], index) => {
			if (settled[index] !== 'finished')
				cleanup.push(`${name} ${settled[index] ?? `did not finish within ${LOG_SECONDS} s`}`);
		});
		if (cleanup.length) {
			record.cleanup = cleanup;
			if (record.state === 'passed') {
				record.state = 'failed';
				record.message = `The run passed, but its cleanup did not: ${cleanup.join('; ')}`;
			}
		}
		// each side's facts, also for a run that failed before its checks
		record.sides ??= Object.fromEntries(Object.entries(sides).map(([role, side]) => [role, side.facts()]));
		record.finished = new Date().toISOString();
		// a run without its record has not passed
		try {
			await fs.writeFile(path.join(out, 'run.json'), redact(JSON.stringify(record, null, 2), secrets) + '\n');
		} catch (error) {
			record.state = 'failed';
			record.message = `The run's record could not be written: ${redact(error.message, secrets)}`;
		}
	}
	return record;
}

// the driver runs when it is the script node was started with (a link to it included); the tests import its checks only
if (process.argv[1] && await fs.realpath(process.argv[1]).catch(() => null) === fileURLToPath(import.meta.url)) {
	const options = parseArguments(process.argv);
	await fs.mkdir(options.out); // a fresh directory: an earlier run's records are never mixed in
	const record = await run(options);
	const passed = record.checks.filter(item => item.passed).length;
	console.log(`live co-op acceptance (${record.scenario}${record.minutes ? `, ${record.minutes} minutes` : ''}, ${record.host}/${record.guest}${record.runtime ? `, ${record.runtime}` : ''}): ${record.state}`
		+ ` (${passed} of ${record.checks.length} checks)` + (record.message ? `: ${record.message}` : ''));
	process.exit(record.state === 'passed' ? 0 : 1);
}
