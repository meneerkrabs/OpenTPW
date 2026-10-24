#!/usr/bin/env node
// The live co-op acceptance (docs/COOP.md, "The live acceptance"; C2d-5): two games share a park through the real server, with the game's data, driven through the
// game's own screens. Starts the real server (OpenTPW.Server) on 127.0.0.1 with a fresh data folder and no two-step login, registers two throwaway players with random
// names and passwords, and runs two native games (silent and offscreen), each with its own configuration, online and save folders: the host shares Jungle from its pause
// menu, and the guest, started once the host published its code, joins it from the lobby. The games meet at barriers through status files in the shared evidence
// folder (CoopLiveTest); this driver starts the guest, restarts the server in the recovery scenario, stops both games when one fails, and checks both games' evidence
// against each other once both completed: one session, the guest's build of the host's park, the host's line applied by the guest with checkpoints compared after it,
// the figures seen both ways, the guest's refused line, the chat both ways, Go to, and the session's end; the baseline and the endurance run with no resync and no
// desync, the recovery run with the session back and a second line after the restart, and the endurance run at the fastest speed for its minutes.
//
// Usage:
//   node tools/coop-live.mjs --game <OpenTPW.dll> --server <OpenTPW.Server.dll> --out <directory> [--game-path <Theme Park World>] [--scenario baseline|recovery|endurance]
//        [--minutes M] [--config-source <directory>] [--host native] [--guest native] [--timeout-minutes T]
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
import { fileURLToPath } from 'node:url';
import { sleep } from './web-perf/lib.mjs';

const SCENARIOS = ['baseline', 'recovery', 'endurance'];
const SIDES = ['native'];
/** How long a game may take from its start to its first status, and the guest to publish that it follows the host's park (a download and a build of the park). */
const START_SECONDS = 300;
/** How long the server is down in the recovery scenario before it starts again, in seconds. */
const OUTAGE_SECONDS = 3;

export function parseArguments(argv) {
	const options = {
		game: null, server: null, out: null, gamePath: process.env.OPENTPW_GAME_PATH ?? null, scenario: 'baseline', minutes: 30,
		configSource: process.env.OPENTPW_SMOKE_CONFIG_SOURCE ?? defaultConfigSource(), host: 'native', guest: 'native', timeoutMinutes: null,
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
			throw new Error(`--${side} is ${SIDES.join(' or ')} (a page's side comes with C2d-5b), not '${options[side]}'`);
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
 * last line once it ends. The file is finished when both pipes have ended; the returned promise resolves then.
 */
export function logTo(child, file, secrets) {
	const stream = createWriteStream(file, { flags: 'a' });
	const pipes = [child.stdout, child.stderr].filter(Boolean);
	let open = pipes.length;
	const finished = new Promise(resolve => stream.once('finish', resolve));
	for (const pipe of pipes) {
		let pending = '';
		let ended = false;
		pipe.on('data', chunk => {
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
			if (pending)
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
	constructor(dll, data, port, log, secrets) {
		Object.assign(this, { dll, data, port, log, secrets, child: null, starts: 0 });
		this.origin = `http://127.0.0.1:${port}`;
	}

	async start() {
		const args = [this.dll, '--urls', this.origin, `--OpenTPW:DataDirectory=${this.data}`, '--OpenTPW:RequireTotp=false',
			'--Logging:LogLevel:Default=Warning', '--Logging:LogLevel:Microsoft.Hosting.Lifetime=Information'];
		const child = this.child = spawn(process.env.DOTNET ?? 'dotnet', args, { cwd: path.dirname(this.dll), stdio: ['ignore', 'pipe', 'pipe'],
			env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1' } });
		this.starts++;
		logTo(child, this.log, this.secrets);
		let output = '';
		await new Promise((resolve, reject) => {
			const timer = setTimeout(() => reject(new Error(`The server did not listen within 60 s:\n${redact(output.slice(-2000), this.secrets)}`)), 60000);
			const listening = chunk => {
				output += chunk;
				if (output.includes(`Now listening on: ${this.origin}`)) {
					clearTimeout(timer);
					resolve();
				}
			};
			child.stdout.on('data', listening);
			child.stderr.on('data', listening);
			child.once('error', error => { clearTimeout(timer); reject(new Error(`The server could not start: ${error.message}`)); });
			child.once('exit', code => { clearTimeout(timer); reject(new Error(`The server exited (${code}) before it listened:\n${redact(output.slice(-2000), this.secrets)}`)); });
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
		check('the session ran with no resync and no desync', guest?.streams === 1 && guest?.stream?.desyncs === 0 && guest?.stream?.failedJoins === 0
			&& !(guest?.phases ?? []).includes('guest resyncing'), { streams: guest?.streams, stream: guest?.stream, phases: guest?.phases });
		check('neither game\'s link was lost', [host, guest].every(evidence => evidence?.links?.resumes === 0
			&& !(evidence.links.states ?? []).some(item => item.state === 'Reconnecting') && evidence.links.chatReconnects === 0), { host: host?.links, guest: guest?.links });
	}
	if (scenario === 'endurance') {
		const endurance = host?.endurance;
		const samples = endurance?.samples ?? [];
		const seconds = Number.isFinite(endurance?.ended - endurance?.started) ? endurance.ended - endurance.started : 0;
		const rates = tickRates(samples);
		const guestSamples = guest?.endurance ?? [];
		check(`the fastest speed was followed for ${minutes} minutes`, seconds >= minutes * 60 && samples.length >= Math.floor(minutes * 2)
			&& samples.every(sample => sample.speed === 'Fastest') && rates.length > 0 && rates.every(rate => rate >= FASTEST_TICKS_PER_SECOND)
			&& guestSamples.length >= Math.floor(minutes * 2) - 1 && tickRates(guestSamples).every(rate => rate >= FASTEST_TICKS_PER_SECOND),
		{ seconds, hostSamples: samples.length, guestSamples: guestSamples.length, slowestHostRate: Math.min(...rates), slowestGuestRate: Math.min(...tickRates(guestSamples)) });
	}
	return checks;
}

/** The fewest park ticks a second of wall time the endurance run's samples may show: three times the normal speed's 60, so only the fastest (240) passes. */
export const FASTEST_TICKS_PER_SECOND = 180;

/** The park's ticks a second of wall time between consecutive samples. */
function tickRates(samples) {
	return samples.slice(1).map((sample, index) => (sample.ticks - samples[index].ticks) / (sample.at - samples[index].at));
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
		host: options.host, guest: options.guest, state: 'failed', checks: [] };
	const players = { host: throwawayPlayer('Host'), guest: throwawayPlayer('Guest') };
	const secrets = [players.host.password, players.guest.password];
	const games = {};
	let server;
	try {
		await fs.mkdir(evidence);
		await fs.access(options.game).catch(() => { throw new Error(`${options.game} does not exist: build the game first`); });
		await fs.access(options.server).catch(() => { throw new Error(`${options.server} does not exist: build the server first`); });
		await fs.access(options.gamePath).catch(() => { throw new Error(`${options.gamePath} does not exist: pass the Theme Park World folder`); });
		const data = path.join(out, 'server-data');
		await fs.mkdir(data);
		server = new Server(path.resolve(options.server), data, await freePort(), path.join(out, 'server.log'), secrets);
		await server.start();
		record.server = server.origin;
		await register(server.origin, players.host, secrets);
		await register(server.origin, players.guest, secrets);
		record.players = { host: players.host.name, guest: players.guest.name };
		const folders = { host: await sideFolders(path.join(out, 'host'), options.configSource), guest: await sideFolders(path.join(out, 'guest'), options.configSource) };
		await writeStatus(evidence, { restarted: null });

		const deadline = Date.now() + options.timeoutMinutes * 60000;
		games.host = startGame('host', options, folders.host, evidence, players.host, server.origin, null, secrets);
		let restartedAt = null;
		let guestStarted = null;
		for (;;) {
			const host = await readStatus(evidence, 'host');
			const guest = await readStatus(evidence, 'guest');
			// the guest starts once the host published its session's code
			if (!games.guest && typeof host?.code === 'string' && host.code.length > 0) {
				record.code = host.code;
				games.guest = startGame('guest', options, folders.guest, evidence, players.guest, server.origin, host.code, secrets);
				guestStarted = Date.now();
			}
			// the recovery scenario: the server crashes and starts again once both games stand ready for it
			if (options.scenario === 'recovery' && restartedAt === null && host?.readyForRestart === true && guest?.readyForRestart === true) {
				await server.restart();
				restartedAt = new Date().toISOString();
				record.restart = { at: restartedAt, outageSeconds: OUTAGE_SECONDS, starts: server.starts };
				await writeStatus(evidence, { restarted: restartedAt });
			}
			const ends = Object.fromEntries(Object.entries(games).map(([role, child]) => [role, ending(child)]));
			const failed = Object.entries(ends).find(([, end]) => end && end !== 'completed');
			if (failed) {
				// one game failed: the other is told to stop, and its own failure follows
				await writeStatus(evidence, { restarted: restartedAt, abort: `the ${failed[0]} ${failed[1]}` });
				throw new Error(`The ${failed[0]} ${failed[1]} (at ${(failed[0] === 'host' ? host : guest)?.step ?? 'its start'}: ${(failed[0] === 'host' ? host : guest)?.failed ?? 'see its log'})`);
			}
			if (ends.host === 'completed' && ends.guest === 'completed')
				break;
			// a game that never reaches its first frames fails at its start, not at the run's deadline
			if (!games.guest && Date.now() - Date.parse(record.started) > START_SECONDS * 1000 && !host?.code)
				throw new Error(`The host published no code within ${START_SECONDS} s (at ${host?.step ?? 'its start'})`);
			if (guestStarted !== null && !guest && Date.now() - guestStarted > START_SECONDS * 1000)
				throw new Error(`The guest published no status within ${START_SECONDS} s of its start`);
			if (Date.now() > deadline) {
				await writeStatus(evidence, { restarted: restartedAt, abort: `the run's deadline of ${options.timeoutMinutes} minutes` });
				throw new Error(`The run did not end within ${options.timeoutMinutes} minutes (host at ${host?.step}, guest at ${guest?.step})`);
			}
			await sleep(250);
		}
		const hostEvidence = JSON.parse(await fs.readFile(path.join(evidence, 'host-evidence.json'), 'utf8'));
		const guestEvidence = JSON.parse(await fs.readFile(path.join(evidence, 'guest-evidence.json'), 'utf8'));
		record.environment = hostEvidence.environment;
		record.checks = checkEvidence(hostEvidence, guestEvidence, options.scenario, options.minutes);
		record.evidence = { host: hostEvidence, guest: guestEvidence };
		const failedChecks = record.checks.filter(item => !item.passed);
		if (failedChecks.length)
			throw new Error(`${failedChecks.length} check(s) failed: ${failedChecks.map(item => item.what).join('; ')}`);
		record.state = 'passed';
	} catch (error) {
		record.message = redact(error.message, secrets);
	} finally {
		// a game the run left behind gets a little time to end on the driver's stop, then is ended; what could not be ended fails the run
		const cleanup = [];
		for (const [role, child] of Object.entries(games))
			if (!await exited(child, 20000) && !await stop(child))
				cleanup.push(`the ${role} (process ${child.pid}) had not exited`);
		if (server && !await server.stop())
			cleanup.push(`the server (process ${server.child?.pid}) had not exited`);
		// the games' last lines are in their logs before the run's record is written
		await Promise.race([Promise.all(Object.values(games).map(child => child.logged)), sleep(5000)]);
		if (cleanup.length) {
			record.cleanup = cleanup;
			if (record.state === 'passed') {
				record.state = 'failed';
				record.message = `The run passed, but its cleanup did not: ${cleanup.join('; ')}`;
			}
		}
		record.finished = new Date().toISOString();
		await fs.writeFile(path.join(out, 'run.json'), redact(JSON.stringify(record, null, 2), secrets) + '\n').catch(() => {});
	}
	return record;
}

// the driver runs when it is the script node was started with (a link to it included); the tests import its checks only
if (process.argv[1] && await fs.realpath(process.argv[1]).catch(() => null) === fileURLToPath(import.meta.url)) {
	const options = parseArguments(process.argv);
	await fs.mkdir(options.out); // a fresh directory: an earlier run's records are never mixed in
	const record = await run(options);
	const passed = record.checks.filter(item => item.passed).length;
	console.log(`live co-op acceptance (${record.scenario}${record.minutes ? `, ${record.minutes} minutes` : ''}, ${record.host}/${record.guest}): ${record.state}`
		+ ` (${passed} of ${record.checks.length} checks)` + (record.message ? `: ${record.message}` : ''));
	process.exit(record.state === 'passed' ? 0 : 1);
}
