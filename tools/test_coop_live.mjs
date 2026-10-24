import test from 'node:test';
import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { PassThrough } from 'node:stream';
import { crc32, deflateSync } from 'node:zlib';
import { checkEvidence, checkFreshness, checkObservedEndurance, ENDURANCE_SAMPLE_SECONDS, logTo, parseArguments, redact, STATUS_STALL_SECONDS, throwawayPlayer } from './coop-live.mjs';
import { checkPageSides, checkPictures, keyEvent, pagePoint, pictureName, pngSize } from './coop-live-page.mjs';

// The live co-op acceptance's driver (tools/coop-live.mjs, docs/COOP.md "The live acceptance") without the games: its options, its throwaway players and their
// redaction, and the checks of both games' evidence against each other.

const HOST = 'Hostabc1234';
const GUEST = 'Guestdef567';

/** Both games' evidence of a passing baseline, as the games write it (CoopLiveTest), cut to what the checks read. */
function passingPair() {
	const session = {
		id: 'c730effcbba9f9b9', code: '7UZUHN', title: 'Live acceptance', listed: false, maximumPlayers: 2,
		members: [{ name: HOST, slot: 0, connected: true, host: true }, { name: GUEST, slot: 1, connected: true, host: false }],
	};
	const seen = (what, name) => ({ what, name, cell: [32.5, 17.5], drawnNames: [HOST, GUEST], drawnFigures: 2, picture: `${what}.png` });
	const links = () => ({ states: [{ state: 'Opening', at: 4.6 }, { state: 'Open', at: 4.7 }], resumes: 0, chatReconnects: 0 });
	const host = {
		completed: true, failure: null, player: HOST, session, links: links(), phases: ['host hosting', 'host ended'],
		build: { record: 9, charged: 120, cells: [[2, 2], [3, 2], [4, 2], [5, 2], [6, 2], [7, 2]] },
		sightings: [seen('guest', GUEST), seen('guest-moved', GUEST)],
		chat: { said: `Hello ${GUEST}, code 7UZUHN`, heard: `Hi ${HOST}, code 7UZUHN here` },
		ended: { outcome: 'Ended', ticks: 929 },
	};
	const guest = {
		completed: true, failure: null, player: GUEST, session, links: links(), phases: ['guest following', 'guest ended'], streams: 1,
		joinTimings: [{ Generation: 1, Outcome: 'joined' }],
		applied: { record: 9, lastRecord: 10, checksAtLine: 3, checksAfter: 5 },
		stream: { checks: 7, desyncs: 0, failedJoins: 0 },
		refusal: { refusalsBefore: 0, refusalsAfter: 1, cells: [[8, 2], [9, 2], [10, 2], [11, 2], [12, 2], [13, 2]] },
		sightings: [seen('host', HOST), seen('host-moved', HOST)],
		chat: { heard: host.chat.said, said: host.chat.heard },
		ride: { ride: 'Belly Bounce', cell: [51, 23] },
		goTo: { name: HOST, view: [5, 2.5] },
	};
	return { host, guest };
}

const failing = checks => checks.filter(check => !check.passed).map(check => check.what);

test('a baseline whose evidence holds passes every check', () => {
	const { host, guest } = passingPair();
	const checks = checkEvidence(host, guest, 'baseline', 30);
	assert.equal(checks.length, 12);
	assert.deepEqual(failing(checks), []);
});

for (const [what, change, check] of [
	['a game that did not complete', ({ guest }) => { guest.completed = false; guest.failure = 'TimeoutException'; }, 'both games completed every step'],
	['another session', ({ guest }) => { guest.session = { ...guest.session, id: 'other' }; }, 'both games are in the same session, the host\'s'],
	['a session hosted by someone else', ({ guest }) => { guest.session = { ...guest.session, members: guest.session.members.map(member => ({ ...member, host: !member.host })) }; },
		'both games are in the same session, the host\'s'],
	['no build of the host\'s capture', ({ guest }) => { guest.joinTimings = [{ Outcome: 'failed its check' }]; }, 'the guest\'s park was built from the host\'s capture'],
	['a line applied at another record', ({ guest }) => { guest.applied.record = 8; }, 'the host\'s line, laid through its HUD, reached the guest\'s park at its record'],
	['a line short of its cells', ({ host }) => { host.build.cells.pop(); }, 'the host\'s line, laid through its HUD, reached the guest\'s park at its record'],
	['no checkpoint after the line', ({ guest }) => { guest.applied.checksAfter = guest.applied.checksAtLine; }, 'checkpoints compared after the line'],
	['a guest line the session did not refuse', ({ guest }) => { guest.refusal.refusalsAfter = 0; }, 'the guest\'s own line was refused, its cells left free'],
	['a figure drawn without its name', ({ host }) => { host.sightings[1].drawnNames = [HOST]; },
		'each game drew the other\'s figure with its name, and saw it where its view moved'],
	['a moved figure the guest did not see', ({ guest }) => { guest.sightings.pop(); }, 'each game drew the other\'s figure with its name, and saw it where its view moved'],
	['a chat line one way only', ({ guest }) => { guest.chat.heard = 'something else'; }, 'the chat both ways, through the chat mode\'s entry'],
	['no Go to', ({ guest }) => { delete guest.goTo; }, 'the guest rode a ride from the chat mode and went to the host\'s figure with Go to'],
	['a session the guest never saw end', ({ guest }) => { guest.phases = ['guest following']; }, 'the host ended the session, and its park ran on'],
	['a resync', ({ guest }) => { guest.streams = 2; guest.phases = ['guest following', 'guest resyncing', 'guest following', 'guest ended']; },
		'the session ran with no resync and no desync'],
	['a desync', ({ guest }) => { guest.stream.desyncs = 1; }, 'the session ran with no resync and no desync'],
	['a lost link', ({ host }) => { host.links.states.push({ state: 'Reconnecting', at: 9 }); }, 'neither game\'s link was lost'],
	['a chat that reconnected', ({ guest }) => { guest.links.chatReconnects = 1; }, 'neither game\'s link was lost'],
]) {
	test(`the baseline fails ${what}`, () => {
		const pair = passingPair();
		change(pair);
		assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'baseline', 30)), [check]);
	});
}

test('the recovery run allows a resync, and needs the session back, both links resumed after the restart and a second line', () => {
	const { host, guest } = passingPair();
	const atRestart = { states: [{ state: 'Opening', at: 4 }, { state: 'Open', at: 4.1 }], resumes: 0, chatReconnects: 0 };
	const links = { states: [...atRestart.states, { state: 'Reconnecting', at: 15 }, { state: 'Open', at: 19 }], resumes: 1, chatReconnects: 1 };
	host.rebuild = { record: 21, cells: [[8, 2]] };
	host.recovery = { session: host.session, linksAtRestart: atRestart, links };
	guest.reapplied = { record: 21, lastRecord: 23, checksAtLine: 7, checksAfter: 9 };
	guest.recovery = { linksAtRestart: atRestart, links, resynced: true };
	guest.sightings.push({ what: 'host-moved-again', name: HOST, drawnNames: [GUEST, HOST], drawnFigures: 2 });
	guest.streams = 2;
	guest.phases = ['guest following', 'guest resyncing', 'guest following', 'guest ended'];
	const checks = checkEvidence(host, guest, 'recovery', 30);
	assert.equal(checks.length, 14);
	assert.deepEqual(failing(checks), []);
	const reconnect = 'both games\' links resumed the session and their chats reconnected after the restart';
	// a reconnection before the restart's barrier proves nothing of the restart
	guest.recovery = { linksAtRestart: links, links };
	assert.deepEqual(failing(checkEvidence(host, guest, 'recovery', 30)), [reconnect]);
	// nor do counts that grew while the link never went down after the barrier
	guest.recovery = { linksAtRestart: atRestart, links: { ...links, states: atRestart.states } };
	assert.deepEqual(failing(checkEvidence(host, guest, 'recovery', 30)), [reconnect]);
	guest.recovery = { linksAtRestart: atRestart, links: { ...links, chatReconnects: 0 } };
	delete guest.reapplied;
	assert.deepEqual(failing(checkEvidence(host, guest, 'recovery', 30)), [
		'after the server\'s restart the guest followed again and the host\'s second line reached it', reconnect,
	]);
});

test('the endurance run needs its whole minutes at the fastest speed, on both sides', () => {
	const step = ENDURANCE_SAMPLE_SECONDS;
	const run = (minutes, rate = 240) => {
		const { host, guest } = passingPair();
		const count = minutes * 60 / step;
		host.endurance = { started: 20, startTicks: 1000, ended: 21 + minutes * 60,
			samples: Array.from({ length: count }, (_, index) => ({ at: 20 + (index + 1) * step, ticks: 1000 + (index + 1) * step * rate, speed: 'Fastest' })) };
		// the guest's ticks before the speed, a sample every few seconds, and the last one the host asks for before the normal speed
		guest.endurance = Array.from({ length: count + 1 }, (_, index) => ({ at: 19 + index * step, ticks: 900 + index * step * rate, final: index === count }));
		return { host, guest };
	};
	const named = minutes => `the fastest speed was followed for ${minutes} minutes`;
	let pair = run(2);
	assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'endurance', 2)), []);
	// a run a little short of its minutes
	pair = run(2);
	pair.host.endurance.ended = pair.host.endurance.started + 119;
	assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'endurance', 2)), [named(2)]);
	// a park at the normal or the fast speed
	pair = run(2, 120);
	assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'endurance', 2)), [named(2)]);
	pair = run(2);
	pair.host.endurance.samples[1].speed = 'Normal';
	assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'endurance', 2)), [named(2)]);
	// a stall in the first seconds, before the first sample
	pair = run(2);
	pair.host.endurance.startTicks = pair.host.endurance.samples[0].ticks - step * 60;
	assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'endurance', 2)), [named(2)]);
	pair = run(2);
	delete pair.host.endurance.startTicks;
	assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'endurance', 2)), [named(2)]);
	// samples missing on one side
	pair = run(2);
	pair.guest.endurance.splice(1);
	assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'endurance', 2)), [named(2)]);
	// a guest without its last sample, or stalled before it
	pair = run(2);
	pair.guest.endurance.pop();
	assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'endurance', 2)), [named(2)]);
	// the guest's last sample right after a regular one, with no tick between them (its ticks come in bursts): its stretch joins the one before
	pair = run(2);
	pair.guest.endurance.at(-2).final = false;
	pair.guest.endurance.push({ ...pair.guest.endurance.at(-1), at: pair.guest.endurance.at(-1).at + 0.1, final: true });
	assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'endurance', 2)), []);
	pair = run(2);
	pair.guest.endurance.at(-1).ticks = pair.guest.endurance.at(-2).ticks;
	assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'endurance', 2)), [named(2)]);
	pair = run(3);
	assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'endurance', 4)), [named(4)]);
	// a side that stopped sampling for a while, though its rates hold over the longer stretch
	pair = run(2);
	pair.host.endurance.samples.splice(5, 3);
	assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'endurance', 2)), [named(2)]);
	pair = run(2);
	pair.guest.endurance.splice(5, 3);
	assert.deepEqual(failing(checkEvidence(pair.host, pair.guest, 'endurance', 2)), [named(2)]);
});

test('the driver\'s own clock must see the fastest speed for the run\'s minutes too', () => {
	assert.equal(checkObservedEndurance({ started: 1000, done: 1000 + 120 * 1000 }, 2).passed, true);
	// a status read lags each end a little
	assert.equal(checkObservedEndurance({ started: 1000, done: 1000 + 119 * 1000 }, 2).passed, true);
	assert.equal(checkObservedEndurance({ started: 1000, done: 1000 + 110 * 1000 }, 2).passed, false);
	assert.equal(checkObservedEndurance({ started: 1000, done: null }, 2).passed, false);
	assert.equal(checkObservedEndurance({ started: null, done: null }, 2).passed, false);
});

test('a game\'s log keeps each pipe\'s lines whole, so a password split across chunks is still replaced, and its last line', async () => {
	const folder = await mkdtemp(path.join(os.tmpdir(), 'coop-live-log-'));
	try {
		const child = Object.assign(new EventEmitter(), { stdout: new PassThrough(), stderr: new PassThrough() });
		const file = path.join(folder, 'game.log');
		const logged = logTo(child, file, ['hunter2-secret']);
		child.stdout.write('login hunter2');
		child.stderr.write('a warning\n');
		child.stdout.write('-secret accepted\n');
		child.stdout.write('the last line');
		child.stdout.end();
		child.stderr.end('no newline either');
		await logged;
		const lines = (await readFile(file, 'utf8')).split('\n');
		assert.ok(lines.includes('login [redacted] accepted'), lines.join(' | '));
		assert.ok(lines.includes('a warning') && lines.includes('the last line') && lines.includes('no newline either'), lines.join(' | '));
		assert.ok(!lines.join('\n').includes('hunter2'));
	} finally {
		await rm(folder, { recursive: true, force: true });
	}
});

test('the driver needs the game, the server and a fresh out directory, and a page side needs a web build', () => {
	const base = ['node', 'coop-live.mjs', '--game', 'g.dll', '--server', 's.dll', '--out', 'o', '--game-path', 'tpw'];
	const options = parseArguments(base);
	assert.deepEqual([options.scenario, options.host, options.guest, options.timeoutMinutes], ['baseline', 'native', 'native', 30]);
	assert.equal(parseArguments([...base, '--scenario', 'endurance', '--minutes', '45']).timeoutMinutes, 75);
	assert.throws(() => parseArguments(base.slice(0, 6)), /--out/);
	assert.throws(() => parseArguments([...base, '--scenario', 'longer']), /baseline, recovery, endurance/);
	assert.throws(() => parseArguments([...base, '--guest', 'page']), /--web/);
	const page = parseArguments([...base, '--guest', 'page', '--web', 'wwwroot', '--runtime', 'interpreter']);
	assert.deepEqual([page.host, page.guest, page.web, page.runtime], ['native', 'page', 'wwwroot', 'interpreter']);
	assert.equal(parseArguments([...base, '--host', 'page', '--web', 'wwwroot']).runtime, 'unknown');
	assert.throws(() => parseArguments([...base, '--host', 'tablet']), /native or page/);
	assert.throws(() => parseArguments([...base, '--host', 'page', '--web', 'wwwroot', '--runtime', 'jit']), /aot or interpreter/);
	assert.throws(() => parseArguments([...base, '--minutes', '0']), /positive number/);
	assert.throws(() => parseArguments([...base, '--minutes']), /needs a value/);
	assert.throws(() => parseArguments([...base, '--speed', 'fast']), /Unknown option/);
});

test('a throwaway player fits the login field, and its password never reaches a log', () => {
	const players = Array.from({ length: 20 }, () => throwawayPlayer('Guest'));
	for (const player of players) {
		assert.match(player.name, /^Guest[0-9a-f]{7}$/);
		assert.ok(player.name.length <= 16);
		assert.equal(player.password.length, 16);
	}
	assert.equal(new Set(players.map(player => player.password)).size, players.length);
	const [one, two] = players;
	assert.equal(redact(`login ${one.password} then ${one.password} and ${two.password}`, [one.password, two.password, '']),
		'login [redacted] then [redacted] and [redacted]');
});

test('a log that cannot be written fails its promise, not the driver', async () => {
	const child = Object.assign(new EventEmitter(), { stdout: new PassThrough(), stderr: new PassThrough() });
	const logged = logTo(child, path.join(os.tmpdir(), `coop-live-missing-${process.pid}`, 'no', 'game.log'), []);
	child.stdout.write('a line\n');
	child.stdout.end();
	child.stderr.end();
	await assert.rejects(logged, /ENOENT/);
});

test('a page\'s keys are the keyboard\'s: named keys, letters with their shift, digits and punctuation with their text', () => {
	assert.deepEqual(keyEvent('Enter'), { key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13, modifiers: 0 });
	assert.deepEqual(keyEvent('F4'), { key: 'F4', code: 'F4', windowsVirtualKeyCode: 115, modifiers: 0 });
	assert.deepEqual(keyEvent('h'), { key: 'h', code: 'KeyH', windowsVirtualKeyCode: 72, text: 'h', modifiers: 0 });
	assert.deepEqual(keyEvent('H'), { key: 'H', code: 'KeyH', windowsVirtualKeyCode: 72, text: 'H', modifiers: 8 });
	assert.deepEqual(keyEvent('7'), { key: '7', code: 'Digit7', windowsVirtualKeyCode: 55, text: '7', modifiers: 0 });
	assert.deepEqual(keyEvent(','), { key: ',', code: 'Comma', windowsVirtualKeyCode: 188, text: ',', modifiers: 0 });
	assert.deepEqual(keyEvent(' '), { key: ' ', code: 'Space', windowsVirtualKeyCode: 32, text: ' ', modifiers: 0 });
	// a chat line's every character can be typed
	for (const character of `Hello ${GUEST}, code 7UZUHN`)
		assert.equal(keyEvent(character).key, character);
	assert.throws(() => keyEvent('Tab'), /named keys/);
});

test('a point of the game\'s canvas lands on the page\'s canvas element, in CSS pixels', () => {
	const element = { left: 10, top: 20, width: 1280, height: 800 };
	assert.deepEqual(pagePoint([640, 400], [1280, 800], element), { x: 650, y: 420 });
	// a canvas drawn at twice the element's pixels
	assert.deepEqual(pagePoint([1280, 800], [2560, 1600], element), { x: 650, y: 420 });
	assert.throws(() => pagePoint([1, 2], [0, 800], element), /cannot place/);
	assert.throws(() => pagePoint([1], [1280, 800], element), /cannot place/);
});

test('a page asks only for its own role\'s pictures, by a flat name', () => {
	assert.ok(pictureName('guest', 'guest-host-moved.png'));
	assert.ok(!pictureName('guest', 'host-guest.png'));
	assert.ok(!pictureName('guest', 'guest-../x.png'));
	assert.ok(!pictureName('guest', 'guest-host.png/../x'));
	assert.ok(!pictureName('host', undefined));
});

/** A whole PNG of `width` by `height` RGBA pixels; `cut` drops that many bytes from its end. */
function png(width, height, cut = 0) {
	const chunk = (type, body) => {
		const head = Buffer.alloc(8);
		head.writeUInt32BE(body.length, 0);
		head.write(type, 4, 'latin1');
		const sum = Buffer.alloc(4);
		sum.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), body])), 0);
		return Buffer.concat([head, body, sum]);
	};
	const header = Buffer.alloc(13);
	header.writeUInt32BE(width, 0);
	header.writeUInt32BE(height, 4);
	header[8] = 8;
	header[9] = 6;
	const rows = Buffer.alloc(height * (1 + width * 4));
	const bytes = Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', header), chunk('IDAT', deflateSync(rows)), chunk('IEND', Buffer.alloc(0))]);
	return bytes.subarray(0, bytes.length - cut);
}

test('a picture counts only when the whole PNG decodes', () => {
	assert.deepEqual(pngSize(png(64, 40)), { width: 64, height: 40 });
	// a cut file, a damaged chunk, image data short of its rows, or no picture at all
	assert.equal(pngSize(png(64, 40, 5)), null);
	const damaged = Buffer.from(png(64, 40));
	damaged[40] ^= 0xff;
	assert.equal(pngSize(damaged), null);
	const short = png(64, 40);
	const header = Buffer.from(short);
	header.writeUInt32BE(41, 20);
	// the header's checksum mended, so only the rows are wrong
	header.writeUInt32BE(crc32(header.subarray(12, 29)), 29);
	assert.equal(pngSize(header), null);
	assert.equal(pngSize(Buffer.from('not a picture at all, longer than a header')), null);
});

test('every sighting\'s picture must be kept as a whole PNG of the view, at the size captured', async () => {
	const { host, guest } = passingPair();
	for (const sighting of host.sightings)
		sighting.pictureSize = [1512, 843];
	// the guest is a page: the driver took its pictures, and knows their size
	const pageSizes = Object.fromEntries(guest.sightings.map(sighting => [sighting.picture, [64, 40]]));
	const files = new Map([...host.sightings.map(sighting => [sighting.picture, png(1512, 843)]), ...guest.sightings.map(sighting => [sighting.picture, png(64, 40)])]);
	const read = async name => files.get(name) ?? null;
	assert.equal((await checkPictures({ host, guest }, read, pageSizes)).passed, true);
	// a picture of another size than its view, one missing, one that does not decode, one with no size known
	files.set(guest.sightings[0].picture, png(32, 40));
	assert.equal((await checkPictures({ host, guest }, read, pageSizes)).passed, false);
	files.set(guest.sightings[0].picture, png(64, 40));
	files.delete(guest.sightings[1].picture);
	assert.equal((await checkPictures({ host, guest }, read, pageSizes)).passed, false);
	files.set(guest.sightings[1].picture, png(64, 40, 3));
	assert.equal((await checkPictures({ host, guest }, read, pageSizes)).passed, false);
	files.set(guest.sightings[1].picture, png(64, 40));
	assert.equal((await checkPictures({ host, guest }, read, {})).passed, false);
	assert.equal((await checkPictures({ host: { sightings: [] }, guest: { sightings: [] } }, read, pageSizes)).passed, false);
});

test('a page side ran the build given and went through its own local server: its login, its sockets, after a restart again, and the park\'s files whole', () => {
	const upgrade = (route, at, status = 101, bytes = 100) => ({ path: route, upgrade: true, status, bytesIn: bytes, bytesOut: bytes, at });
	const page = () => ({
		kind: 'page',
		build: { files: { 'index.html': 'aa' }, nativeWasm: 'dotnet.native.x1.wasm', measuredRuntime: 'interpreter' },
		frames: 4000, frameError: null, startedAt: 900, parkFiles: 2,
		upgrades: [upgrade('/api/v1/coop', 1000), upgrade('/api/v1/chat', 1001), upgrade('/api/v1/coop', 5000, 502, 0), upgrade('/api/v1/coop', 6000), upgrade('/api/v1/chat', 6001)],
		requests: [{ path: '/api/v1/sessions', method: 'POST', status: 200, bytes: 120 }],
		network: [{ path: '/_framework/dotnet.native.x1.wasm', status: 200 }, { path: '/game/Data/levels/jungle/Jungle.wad', status: 200 },
			{ path: '/game/Data/levels/jungle/Music/Jungle.sdt', status: 200 }],
		levels: [{ level: 'jungle', requestedAt: 950, loadedAt: 980, failure: null }],
	});
	const native = { kind: 'native' };
	assert.deepEqual(failing(checkPageSides({ host: native, guest: page() }, null)), []);
	assert.equal(checkPageSides({ host: native, guest: page() }, null).length, 4);
	assert.deepEqual(failing(checkPageSides({ host: page(), guest: page() }, 4000)), []);
	assert.equal(checkPageSides({ host: page(), guest: page() }, 4000).filter(check => check.what.startsWith('the host page')).length, 5);
	const only = (guest, restartedAt = null) => failing(checkPageSides({ host: native, guest }, restartedAt));
	// no socket back after the restart
	let guest = page();
	guest.upgrades = guest.upgrades.slice(0, 3);
	assert.deepEqual(only(guest, 4000), ['the guest page\'s sockets came back through its local server after the restart']);
	// a chat socket that carried nothing back
	guest = page();
	guest.upgrades[1].bytesOut = 0;
	guest.upgrades[4].bytesOut = 0;
	assert.deepEqual(only(guest), ['the guest page\'s co-op and chat sockets went through its local server, with bytes both ways']);
	// a login the local server did not carry, or refused
	guest = page();
	guest.requests = [{ path: '/api/v1/sessions', method: 'POST', status: 401, bytes: 40 }];
	assert.deepEqual(only(guest), ['the guest page logged in through its local server']);
	// the native module not loaded, or a frame that failed after the run
	const ran = 'the guest page ran the web build given, its interpreter runtime loaded, and no frame of it failed';
	guest = page();
	guest.network = guest.network.slice(1);
	assert.deepEqual(only(guest), [ran]);
	guest = page();
	guest.frameError = 'InvalidOperationException: the drawing failed';
	assert.deepEqual(only(guest), [ran]);
	// the park's files: asked for before the run, a copy that failed, one of them missing, or another park's
	const park = 'the guest page fetched the park\'s files from the game folder during the run, whole';
	guest = page();
	guest.levels[0].requestedAt = 800;
	assert.deepEqual(only(guest), [park]);
	guest = page();
	guest.levels[0].failure = 'HTTP 404';
	assert.deepEqual(only(guest), [park]);
	guest = page();
	guest.network.pop();
	assert.deepEqual(only(guest), [park]);
	guest = page();
	guest.levels = [{ level: 'space', requestedAt: 950, loadedAt: 980, failure: null }];
	assert.deepEqual(only(guest), [park]);
});

test('a game\'s status keeps its run and must advance while the game runs', () => {
	const freshness = {};
	checkFreshness(freshness, 'guest', { run: 'r1', revision: 1 }, null, 0);
	checkFreshness(freshness, 'guest', { run: 'r1', revision: 1 }, null, 30000);
	checkFreshness(freshness, 'guest', { run: 'r1', revision: 2 }, null, 59000);
	assert.throws(() => checkFreshness(freshness, 'guest', { run: 'r1', revision: 2 }, null, 59000 + STATUS_STALL_SECONDS * 1000 + 1), /did not advance/);
	// an ended game publishes no more
	checkFreshness(freshness, 'guest', { run: 'r1', revision: 2 }, 'completed', 59000 + STATUS_STALL_SECONDS * 1000 + 1);
	assert.throws(() => checkFreshness(freshness, 'guest', { run: 'r2', revision: 9 }, null, 60000), /another run/);
	assert.throws(() => checkFreshness(freshness, 'guest', { run: 'r1', revision: 1 }, null, 60000), /went back/);
	// no status yet: nothing to check
	checkFreshness(freshness, 'host', null, null, 1e9);
});
