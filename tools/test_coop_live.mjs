import test from 'node:test';
import assert from 'node:assert/strict';
import { checkEvidence, parseArguments, redact, throwawayPlayer } from './coop-live.mjs';

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

test('the recovery run allows a resync, and needs the session back, both links resumed and a second line after the restart', () => {
	const { host, guest } = passingPair();
	const links = { states: [{ state: 'Open', at: 4 }, { state: 'Reconnecting', at: 15 }, { state: 'Open', at: 19 }], resumes: 1, chatReconnects: 1 };
	host.rebuild = { record: 21, cells: [[8, 2]] };
	host.recovery = { session: host.session, links };
	guest.reapplied = { record: 21, lastRecord: 23, checksAtLine: 7, checksAfter: 9 };
	guest.recovery = { links, resynced: true };
	guest.sightings.push({ what: 'host-moved-again', name: HOST, drawnNames: [GUEST, HOST], drawnFigures: 2 });
	guest.streams = 2;
	guest.phases = ['guest following', 'guest resyncing', 'guest following', 'guest ended'];
	const checks = checkEvidence(host, guest, 'recovery', 30);
	assert.equal(checks.length, 14);
	assert.deepEqual(failing(checks), []);
	guest.recovery = { links: { ...links, chatReconnects: 0 } };
	delete guest.reapplied;
	assert.deepEqual(failing(checkEvidence(host, guest, 'recovery', 30)), [
		'after the server\'s restart the guest followed again and the host\'s second line reached it',
		'both games\' links resumed the session and their chats reconnected after the restart',
	]);
});

test('the endurance run needs its minutes of samples on both sides', () => {
	const { host, guest } = passingPair();
	host.endurance = Array.from({ length: 4 }, (_, index) => ({ at: 40 + index * 30 }));
	guest.endurance = Array.from({ length: 4 }, (_, index) => ({ at: 35 + index * 30 }));
	assert.deepEqual(failing(checkEvidence(host, guest, 'endurance', 2)), []);
	assert.deepEqual(failing(checkEvidence(host, guest, 'endurance', 3)), ['the fastest speed was followed for 3 minutes']);
});

test('the driver needs the game, the server and a fresh out directory, and runs native sides only', () => {
	const base = ['node', 'coop-live.mjs', '--game', 'g.dll', '--server', 's.dll', '--out', 'o', '--game-path', 'tpw'];
	const options = parseArguments(base);
	assert.deepEqual([options.scenario, options.host, options.guest, options.timeoutMinutes], ['baseline', 'native', 'native', 30]);
	assert.equal(parseArguments([...base, '--scenario', 'endurance', '--minutes', '45']).timeoutMinutes, 75);
	assert.throws(() => parseArguments(base.slice(0, 6)), /--out/);
	assert.throws(() => parseArguments([...base, '--scenario', 'longer']), /baseline, recovery, endurance/);
	assert.throws(() => parseArguments([...base, '--guest', 'page']), /C2d-5b/);
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
