"""Immutable Mac gms.dat profile snapshot, reference byte writer and JSON envelope.

Standalone reference for a later profile implementation. The layout, widths and
read rules are the ones pinned by player_file_evidence.py (``MAC_PLAYER_FILE``
witnesses) for the identified Feral Mac executable; ``snapshot_witnesses`` pins
the few extra reader details this module relies on. Nothing here is a PC parser:
no PC player file exists in the supplied assets.

Two explicit read policies, no default:

- ``mac-partial`` mirrors the Mac reader: reset values overlaid by every member
  read before the first failure, themes inserted only when complete and not a
  duplicate key, unsigned version >= 12 accepted with the one layout, trailing
  bytes ignored. A short i32 player member holds the bytes delivered before the
  end of the file, unswapped (native_io_evidence.py). The result says whether the
  read was complete and where it stopped.
- ``strict-host`` is a host policy, not Mac behaviour: it accepts only bytes the
  Mac writer can produce for version 12 and raises ``StrictReject`` with a fixed
  reason otherwise (including ``unknown-version`` for 13..0xFFFFFFFF, which the
  Mac reads).

Snapshots are frozen; theme, settings and mystery order is file order (the Mac
writer's own order is ``mac_writer_order``). Derived
values (GameType at selection, keys, available tickets) are computed on demand
from the snapshot and caller-supplied runtime facts, never stored.
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import struct
import sys
from typing import Callable

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scenario_evidence import Evidence, pef  # noqa: E402
from key_display_evidence import _game_type, earned_tickets, mac_available_tickets, mac_keys  # noqa: E402
from player_file_evidence import OPTION, RESET, short_import  # noqa: E402
from profile_evidence import import_at  # noqa: E402

POLICIES = ('mac-partial', 'strict-host')
WRITTEN_VERSION = 12      # the only version the Mac writer produces
MIN_VERSION = 12          # cmplwi: unsigned
SETTING_NAMES = tuple(name for name, *_ in OPTION)
SETTING_WIDTH = {name: 8 if encoding != 'u8' else 1 for name, _, encoding, *_ in OPTION}
PLAYER_FIELDS = ('mEarnedGlobalTicket', 'mEarnedSecretTicket', 'mSpentTickets', 'mExtraKeys',
                 'mEasyModeUser', 'mSwearFilterOn', 'mFirstTimePlayer')
STRICT_REASONS = ('rejected-version', 'unknown-version', 'truncated', 'negative-count', 'duplicate-theme',
                  'nul-in-theme-name', 'duplicate-ride-id', 'trailing-bytes')
ENVELOPE_SCHEMA = 'opentpw.reference.mac-gms-snapshot'
ENVELOPE_VERSION = 1
SOURCE = ('Feral Mac SimThemePark.data 04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5, '
          'static trace (docs/reverse/PPC-scenarios.md); not a PC format')


class StrictReject(ValueError):
    def __init__(self, reason: str, offset: int, detail: str):
        assert reason in STRICT_REASONS
        super().__init__(f'{reason} at byte {offset}: {detail}')
        self.reason, self.offset, self.detail = reason, offset, detail


@dataclass(frozen=True)
class ThemeSnapshot:
    """One theme entry: raw name bytes (no terminator on disk) and the 160-byte record, in disk order."""
    name: bytes
    local_tickets: tuple[int, ...]               # mEarnedLocalTicket[0..5]
    awards: tuple[tuple[int, int], ...]          # (mAward[i] u8, mAwardScore[i] i32), i = 0..3
    sign_names: tuple[tuple[int, int], ...]      # (mSignNameA[i], mSignNameB[i]) u16, i = 0..32
    name_changed: int
    all_research_completed: int

    @property
    def map_key(self) -> bytes:
        """Mac map key: the NUL-terminated name buffer goes through the const char* string
        constructor (strlen + strcpy), so bytes from the first NUL on do not reach the key."""
        return self.name.split(b'\0', 1)[0]


@dataclass(frozen=True)
class ProfileSnapshot:
    policy: str
    version: int                                 # raw u32 as read (written as 12)
    global_tickets: tuple[int, ...]              # 4 bytes, raw values (any non-zero counts once)
    secret_tickets: tuple[int, ...]              # 2 bytes
    spent_tickets: int
    extra_keys: int
    easy_mode_user: int                          # raw byte; the mode is "non-zero"
    swear_filter_on: int                         # raw byte as read (the post-read swear hook is not applied)
    first_time_player: int
    theme_count: int                             # raw signed count as read (None until read)
    themes: tuple[ThemeSnapshot, ...]            # inserted themes, file order
    settings: tuple[tuple[str, bytes], ...]      # settings read, block order, raw bytes
    mystery_count: int
    mystery: tuple[int, ...]                     # rideIds in file order (the Mac set collapses repeats)
    complete: bool
    failed_at: str | None
    failed_offset: int | None
    fields_read: tuple[str, ...]                 # player members read in full, file order
    trailing: bytes                              # bytes after the mystery set (ignored by the Mac)
    issues: tuple[str, ...]

    @property
    def mystery_set(self) -> frozenset[int]:
        return frozenset(self.mystery)

    @property
    def settings_complete(self) -> bool:
        """Only a complete settings block is applied by the Mac (0x126460); values read before a
        failure sit in the game-wide settings object unapplied."""
        return len(self.settings) == len(SETTING_NAMES)


class _Short(Exception):
    pass


class _Cursor:
    def __init__(self, raw: bytes):
        self.raw, self.pos = raw, 0

    def take(self, n: int) -> bytes:
        if n < 0 or self.pos + n > len(self.raw):
            raise _Short
        chunk = self.raw[self.pos:self.pos + n]
        self.pos += n
        return chunk

    def u8(self) -> int:
        return self.take(1)[0]

    def u16(self) -> int:
        return struct.unpack('<H', self.take(2))[0]

    def i32(self) -> int:
        return struct.unpack('<i', self.take(4))[0]

    def u32(self) -> int:
        return struct.unpack('<I', self.take(4))[0]


def _theme_record(c: _Cursor) -> dict:
    local = tuple(c.u8() for _ in range(6))
    awards = tuple((c.u8(), c.i32()) for _ in range(4))
    signs = tuple((c.u16(), c.u16()) for _ in range(33))
    return {'local_tickets': local, 'awards': awards, 'sign_names': signs,
            'name_changed': c.u8(), 'all_research_completed': c.u8()}


def read_profile_snapshot(raw: bytes, policy: str) -> ProfileSnapshot:
    """Read gms.dat bytes under an explicit policy (see module docstring)."""
    if policy not in POLICIES:
        raise ValueError(f'policy must be one of {POLICIES}, got {policy!r}')
    if not isinstance(raw, (bytes, bytearray)):
        raise ValueError('raw must be bytes')
    raw = bytes(raw)
    strict = policy == 'strict-host'
    c = _Cursor(raw)
    state = {'version': None, 'mEarnedGlobalTicket': list(RESET['mEarnedGlobalTicket']),
             'mEarnedSecretTicket': list(RESET['mEarnedSecretTicket']),
             **{k: RESET[k] for k in PLAYER_FIELDS[2:]}}
    themes: list[ThemeSnapshot] = []
    keys: set[bytes] = set()
    settings: list[tuple[str, bytes]] = []
    mystery: list[int] = []
    fields_read: list[str] = []
    issues: list[str] = []
    counts = {'theme': None, 'mystery': None}
    step = ['version']
    start = [0]

    def at(name: str) -> None:
        step[0], start[0] = name, c.pos

    def reject(reason: str, detail: str, offset: int | None = None):
        raise StrictReject(reason, start[0] if offset is None else offset, detail)

    def snapshot(complete: bool) -> ProfileSnapshot:
        return ProfileSnapshot(
            policy=policy, version=state['version'], global_tickets=tuple(state['mEarnedGlobalTicket']),
            secret_tickets=tuple(state['mEarnedSecretTicket']), spent_tickets=state['mSpentTickets'],
            extra_keys=state['mExtraKeys'], easy_mode_user=state['mEasyModeUser'],
            swear_filter_on=state['mSwearFilterOn'], first_time_player=state['mFirstTimePlayer'],
            theme_count=counts['theme'], themes=tuple(themes), settings=tuple(settings),
            mystery_count=counts['mystery'], mystery=tuple(mystery), complete=complete,
            failed_at=None if complete else step[0], failed_offset=None if complete else start[0],
            fields_read=tuple(fields_read), trailing=raw[c.pos:] if complete else b'', issues=tuple(issues))

    try:
        at('version')
        state['version'] = c.u32()
        if state['version'] < MIN_VERSION:
            if strict:
                reject('rejected-version', f'version {state["version"]} < 12 (the Mac rejects it too)')
            return snapshot(False)
        if state['version'] != WRITTEN_VERSION:
            if strict:
                reject('unknown-version', f'version {state["version"]:#x}: accepted by the Mac (unsigned >= 12, '
                                          'one layout) but not a variant this host accepts')
            issues.append(f'version {state["version"]:#x} accepted by the unsigned Mac gate; layout of version 12')
        for name in PLAYER_FIELDS:
            at(name)
            if name in ('mEarnedGlobalTicket', 'mEarnedSecretTicket'):
                for i in range(len(state[name])):
                    state[name][i] = c.u8()
            elif name in ('mSpentTickets', 'mExtraKeys'):
                state[name] = c.i32()
            else:
                state[name] = c.u8()
            fields_read.append(name)
        at('theme count')
        counts['theme'] = c.i32()
        if counts['theme'] < 0:
            if strict:
                reject('negative-count', f'theme count {counts["theme"]}')
            issues.append(f'theme count {counts["theme"]} < 0: signed loop reads no themes')
        for _ in range(max(counts['theme'], 0)):
            at('theme name')
            length = c.u32()
            if length > len(raw) - c.pos:
                if not strict and length == 0xffffffff:
                    issues.append('theme name length 0xffffffff: the Mac allocates length+1 = 0 bytes unchecked and '
                                  'stores name bytes past the block until the file ends; outcome not defined by '
                                  'the trace')
                elif not strict:
                    issues.append(f'theme name length {length:#x} exceeds the file: the Mac allocates length+1 bytes '
                                  'unchecked, reads to the end of the file and fails without inserting the theme '
                                  '(a failed allocation is not traced)')
                raise _Short
            name = c.take(length)
            if strict and b'\0' in name:
                reject('nul-in-theme-name', 'host policy: the Mac map key stops at the first NUL')
            at('theme record')
            record = _theme_record(c)
            theme = ThemeSnapshot(name=name, **record)
            if theme.map_key in keys:
                at('duplicate theme')
                if strict:
                    reject('duplicate-theme', f'map key {theme.map_key!r} repeats', offset=c.pos)
                return snapshot(False)
            if theme.map_key != name:
                issues.append(f'theme name {name!r} has an embedded NUL; inferred map key {theme.map_key!r}')
            keys.add(theme.map_key)
            themes.append(theme)
        for name in SETTING_NAMES:
            at(name)
            settings.append((name, c.take(SETTING_WIDTH[name])))
        at('mystery count')
        counts['mystery'] = c.i32()
        if counts['mystery'] < 0:
            if strict:
                reject('negative-count', f'mystery count {counts["mystery"]}')
            issues.append(f'mystery count {counts["mystery"]} < 0: signed loop reads no ids')
        for _ in range(max(counts['mystery'], 0)):
            at('rideId')
            ride = c.u16()
            if ride in mystery:
                if strict:
                    reject('duplicate-ride-id', f'rideId {ride} repeats; the Mac writer iterates a set')
                issues.append(f'rideId {ride} repeats; the Mac set insert ignores it (result unchecked)')
            mystery.append(ride)
    except _Short:
        if strict:
            reject('truncated', f'{len(raw)} bytes end inside {step[0]}')
        delivered = raw[start[0]:]
        if step[0] in ('mSpentTickets', 'mExtraKeys') and delivered:
            state[step[0]] = short_import(state[step[0]], delivered)
            issues.append(f'{step[0]}: {len(delivered)} of 4 bytes delivered into the member, unswapped -> '
                          f'{state[step[0]]} (assumes FSRead stores the bytes before end of file; Mac OS)')
        elif step[0] in SETTING_NAMES and delivered:
            issues.append(f'{step[0]}: {len(delivered)} of 8 bytes delivered into the game-wide settings member '
                          'over bytes this snapshot does not hold, unswapped')
        return snapshot(False)
    if strict and c.pos != len(raw):
        reject('trailing-bytes', f'{len(raw) - c.pos} bytes after the mystery set', offset=c.pos)
    return snapshot(True)


# -- writer and envelope ----------------------------------------------------------------------------
def _check(cond: bool, what: str) -> None:
    if not cond:
        raise ValueError(what)


def _u(value, bits: int, what: str) -> int:
    _check(type(value) is int and 0 <= value < 1 << bits, f'{what} must be u{bits}, got {value!r}')
    return value


def _s32(value, what: str) -> int:
    _check(type(value) is int and -2 ** 31 <= value < 2 ** 31, f'{what} must be i32, got {value!r}')
    return value


def serialize_profile_snapshot(s: ProfileSnapshot) -> bytes:
    """Bytes in the traced layout and the snapshot's own order (raw version, raw counts, trailing bytes),
    so a complete read cycles byte-identically. Refuses partial snapshots: what the Mac would write after
    a partial read depends on the game-wide settings object, which the snapshot does not hold.
    Not the Mac writer's order: see ``mac_writer_order``."""
    _check(s.complete, 'only a complete snapshot can be serialized')
    _check(len(s.global_tickets) == 4 and len(s.secret_tickets) == 2, 'ticket byte counts')
    _check(tuple(name for name, _ in s.settings) == SETTING_NAMES, 'settings must be the 11 members in order')
    for name, raw in s.settings:
        _check(isinstance(raw, bytes) and len(raw) == SETTING_WIDTH[name], f'{name} width')
    for count, items, what in ((s.theme_count, s.themes, 'theme'), (s.mystery_count, s.mystery, 'mystery')):
        _s32(count, f'{what} count')
        _check(len(items) == max(count, 0), f'{what} count {count} does not match {len(items)} entries')
    out = bytearray(struct.pack('<I', _u(s.version, 32, 'version')))
    _check(s.version >= MIN_VERSION, 'version below 12 cannot be complete')
    out += bytes(_u(v, 8, 'ticket') for v in (*s.global_tickets, *s.secret_tickets))
    out += struct.pack('<ii', _s32(s.spent_tickets, 'spent'), _s32(s.extra_keys, 'extra keys'))
    out += bytes(_u(v, 8, 'flag') for v in (s.easy_mode_user, s.swear_filter_on, s.first_time_player))
    out += struct.pack('<i', s.theme_count)
    seen = set()
    for t in s.themes:
        _check(t.map_key not in seen, f'duplicate theme key {t.map_key!r}')
        seen.add(t.map_key)
        _check(len(t.local_tickets) == 6 and len(t.awards) == 4 and len(t.sign_names) == 33, 'theme shape')
        out += struct.pack('<I', len(t.name)) + t.name
        out += bytes(_u(v, 8, 'local ticket') for v in t.local_tickets)
        for award, score in t.awards:
            out += struct.pack('<Bi', _u(award, 8, 'award'), _s32(score, 'award score'))
        for a, b in t.sign_names:
            out += struct.pack('<HH', _u(a, 16, 'sign name'), _u(b, 16, 'sign name'))
        out += bytes((_u(t.name_changed, 8, 'flag'), _u(t.all_research_completed, 8, 'flag')))
    for _, raw in s.settings:
        out += raw
    out += struct.pack('<i', s.mystery_count)
    out += b''.join(struct.pack('<H', _u(r, 16, 'rideId')) for r in s.mystery)
    return bytes(out + s.trailing)


def to_envelope(s: ProfileSnapshot) -> dict:
    """JSON-safe reference envelope; bytes as hex, order preserved, derived values omitted."""
    return {
        'schema': ENVELOPE_SCHEMA, 'envelope_version': ENVELOPE_VERSION, 'source': SOURCE, 'policy': s.policy,
        'complete': s.complete, 'failed_at': s.failed_at, 'failed_offset': s.failed_offset,
        'version': s.version,
        'player': {'mEarnedGlobalTicket': list(s.global_tickets), 'mEarnedSecretTicket': list(s.secret_tickets),
                   'mSpentTickets': s.spent_tickets, 'mExtraKeys': s.extra_keys,
                   'mEasyModeUser': s.easy_mode_user, 'mSwearFilterOn': s.swear_filter_on,
                   'mFirstTimePlayer': s.first_time_player},
        'fields_read': list(s.fields_read), 'theme_count': s.theme_count,
        'themes': [{'name_hex': t.name.hex(), 'mEarnedLocalTicket': list(t.local_tickets),
                    'mAward_mAwardScore': [list(p) for p in t.awards],
                    'mSignNameA_mSignNameB': [list(p) for p in t.sign_names],
                    'mNameChanged': t.name_changed, 'mAllResearchCompleted': t.all_research_completed}
                   for t in s.themes],
        'settings': [[name, raw.hex()] for name, raw in s.settings],
        'mystery_count': s.mystery_count, 'mystery': list(s.mystery),
        'trailing_hex': s.trailing.hex(), 'issues': list(s.issues),
    }


def from_envelope(env: dict) -> ProfileSnapshot:
    """Inverse of to_envelope; unknown schema or envelope versions are refused, not guessed."""
    _check(isinstance(env, dict) and env.get('schema') == ENVELOPE_SCHEMA, f'unknown schema {env.get("schema")!r}')
    _check(env.get('envelope_version') == ENVELOPE_VERSION,
           f'unknown envelope_version {env.get("envelope_version")!r}')
    _check(env.get('policy') in POLICIES, 'policy')
    p = env['player']
    _check(set(p) == {'mEarnedGlobalTicket', 'mEarnedSecretTicket', *PLAYER_FIELDS[2:]}, 'player members')
    themes = tuple(ThemeSnapshot(
        name=bytes.fromhex(t['name_hex']), local_tickets=tuple(t['mEarnedLocalTicket']),
        awards=tuple(tuple(x) for x in t['mAward_mAwardScore']),
        sign_names=tuple(tuple(x) for x in t['mSignNameA_mSignNameB']),
        name_changed=t['mNameChanged'], all_research_completed=t['mAllResearchCompleted']) for t in env['themes'])
    settings = tuple((name, bytes.fromhex(h)) for name, h in env['settings'])
    _check(tuple(n for n, _ in settings) == SETTING_NAMES[:len(settings)], 'settings order')
    return ProfileSnapshot(
        policy=env['policy'], version=env['version'], global_tickets=tuple(p['mEarnedGlobalTicket']),
        secret_tickets=tuple(p['mEarnedSecretTicket']), spent_tickets=p['mSpentTickets'],
        extra_keys=p['mExtraKeys'], easy_mode_user=p['mEasyModeUser'], swear_filter_on=p['mSwearFilterOn'],
        first_time_player=p['mFirstTimePlayer'], theme_count=env['theme_count'], themes=themes,
        settings=settings, mystery_count=env['mystery_count'], mystery=tuple(env['mystery']),
        complete=env['complete'], failed_at=env['failed_at'], failed_offset=env['failed_offset'],
        fields_read=tuple(env['fields_read']), trailing=bytes.fromhex(env['trailing_hex']),
        issues=tuple(env['issues']))


# -- derived values (computed, never stored) --------------------------------------------------------
def mac_writer_order(s: ProfileSnapshot) -> dict:
    """Container order the Mac writer would emit for the record this read built (native_io_evidence):
    the theme map ascending by key (TbStringBase<c>::operator<: unsigned bytes, shorter prefix first,
    which is Python bytes order for NUL-free keys), names written as the key, count = map size; the
    mystery set ascending unsigned, count = set size. Order only: version (always 12), the swear hook and
    later in-memory changes decide the remaining bytes of a rewrite."""
    themes = tuple(sorted(t.map_key for t in s.themes))
    mystery = tuple(sorted(set(s.mystery)))
    return {'themes': themes, 'theme_count': len(themes), 'mystery': mystery, 'mystery_count': len(mystery)}


def setting_words(raw: bytes) -> tuple[int, int]:
    """An 8-byte settings value: first word as the Mac stored it (native, big-endian), second word
    little-endian. For the four volume members the settings apply (0x126460) tests byte 0 for non-zero
    (enabled) and passes the second word on as the level; other uses of the words are not traced."""
    _check(isinstance(raw, bytes) and len(raw) == 8, '8-byte settings value')
    return struct.unpack('>I', raw[:4])[0], struct.unpack('<I', raw[4:])[0]


def selection_game_type(s: ProfileSnapshot, current_game_type: int) -> int:
    """GameType after selection (0x13781c): 1 stays 1; otherwise 2 if the mode byte's low 8 bits are
    non-zero (clrlwi.), else 0. ``current_game_type`` is runtime state, not part of the profile."""
    return 1 if _game_type(current_game_type) == 1 else (2 if s.easy_mode_user & 0xff else 0)


def key_counters(s: ProfileSnapshot, usable: Callable[[bytes], bool]) -> dict:
    """Keys() and available tickets on this snapshot. ``usable(map_key)`` is required: whether the theme's
    global.sam loads is a runtime fact (the Mac loads it on demand), so no default is assumed.
    Spent tickets lower available tickets only; Keys() never reads them. The Instant Action bypass lives
    at the theme door, not here."""
    _check(callable(usable), 'usable must be a callable over theme map keys')
    themes = []
    for t in s.themes:
        flag = usable(t.map_key)
        _check(type(flag) is bool, 'usable must return a bool')
        themes.append((list(t.local_tickets), flag))
    earned = earned_tickets(list(s.global_tickets), list(s.secret_tickets), themes)
    return {'earned': earned, 'keys': mac_keys(s.extra_keys, earned), 'spent': s.spent_tickets,
            'available': mac_available_tickets(earned, s.spent_tickets)}


# -- witnesses for the reader details this module relies on -----------------------------------------
def snapshot_witnesses(e: Evidence) -> dict:
    """Name-length loop, key construction, unchecked set insert and the mode test."""
    e.d(0x129794, 14, 3, 3, 1)                       # new[](length + 1)
    import_at(e, 0x129798, '__nwa__FUl')
    for o in range(0x12979c, 0x1297a8, 4):           # no null test on the buffer
        w = e.word(o)
        if w >> 26 in (11, 16) or (w >> 26 == 31 and w >> 1 & 0x3ff in (0, 32)):
            e.fail(o, 'allocation checked', hex(w))
    e.checked += 1
    e.d(0x1297a4, 14, 24, 3, 0)
    e.d(0x1297d4, 32, 3, 1, 304)                     # loop test first: length 0 reads nothing
    e.x(0x1297d8, 31, 32, 0, 22, 3)                  # cmplw: unsigned byte index
    e.bc(0x1297dc, 12, 0, 0x1297ac)
    e.d(0x1297e4, 14, 4, 24, 0)
    e.d(0x1297ec, 14, 3, 1, 284)
    import_at(e, 0x1297f0, '__ct__26TbDynamicStringTemplate<c>FPCc')
    import_at(e, 0x1297fc, '__dla__FPv')
    e.bl(0x129960, 0x12b5a8)
    e.d(0x129964, 14, 24, 24, 1)                     # set insert result unused
    e.d(0x128f4c, 34, 3, 3, 36)
    e.bl(0x13798c, 0x128f4c)
    e.rlwinm(0x137990, 3, 0, 0, 24, 31)              # clrlwi.: low byte non-zero -> Instant Action
    return {'theme_name': 'length+1 bytes allocated unchecked; unsigned byte loop tested first; key built by '
                          'a const char* string constructor from the NUL-terminated buffer',
            'mystery_insert': 'set insert result unused: a repeated rideId is silently absorbed',
            'mode_test': 'GameType 2 iff the mEasyModeUser byte is non-zero (unless GameType is 1)'}
