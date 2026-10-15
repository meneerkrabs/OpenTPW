"""Bounded static boarding witnesses (BOARD-R); never execute or emit original instructions.

Usage: python3 -I board_evidence.py /path/to/mac-feral/bin [--pc-data /path/to/Data]

Derives the boarding latency of a BOUNCE ride (Belly Bounce) from traced rules: the host admission
handshake (states 11/13/14, ride update), the RSE loop of the BOUNCE scripts (slice ends at
CRIT_UNLOCK and WAIT), and the BOUNCE/UNBOUNCE slot records. Addresses are section-relative.
Instructions are decoded into fields and compared with expected operands; only digests, decoded
fields and derived values are reported. The pure functions below restate the decoded rules so that
tests can pin them without the binary. See docs/reverse/BOARD-plan.md for the narrative and bounds.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import random
import struct
import sys

HERE = Path(__file__).resolve().parent
TOOLS = HERE.parents[1]
REPO = TOOLS.parents[1]
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(TOOLS / 'lanes' / 'queue'))
import pef  # noqa: E402
from timer_evidence import call_target, d_fields, load_identified, require  # noqa: E402
import queue_evidence as queue  # noqa: E402  (bounce_deadline_ms, unbounce_releases, move_delay, SAM reader)

TURN_MS = 248            # PPC-clock: 8 substeps of 31 ms; one park update and one slice per ordinary script
SLICE_BUDGET = 50        # RSE header time slice (all 308 corpus scripts)
INTERLUDE_TURNS = 11     # state 8 restores when turn > +520 + 10 (0xef6d8)
NEEDS_WINDOW = 30        # a new interlude needs turn - +520 > 30 (0xed5a0)
MOVE_UP_GAP = 2          # state 11 waits only while 0 <= recorded - actual <= 2 (0xed4a4)

# (code start, end exclusive, sha256) of the blocks interpreted for this lane.
BLOCKS = {
    'state_13_called_walk': (0xed7e0, 0xeda04, '66542ad28d6e2607e4681a66e42e0ae6a8644af20ade2a0d1762f7bd84931470'),
    'admission_write_letmeon': (0xe03d0, 0xe05c8, '708496620a8eb46c922d37ef811e5fdb99119a10251eb89730a14a5348c809e9'),
    'letmeon_consumed': (0xe05c8, 0xe0620, '7b79933ea2ce723f6f0983807de49ffe7539877359af7fe3777123b2115dad86'),
    'state_14_wait_for_script': (0xef4f8, 0xef57c, '8da4653bfa57d72f315061c23c7b1ff306ebd87192933939aeb184c7a5333271'),
    'ride_update_letmeoff': (0xe1864, 0xe1a40, 'a0d6f243b6f0a846bb487d98577ecb35207ef68d46719c8e61adc1d24ef4b7ee'),
    'leave_ride_to_exit': (0xeeb30, 0xeece4, 'a5648ea4316e8cc7d4f6ae142fbb3e356dadbb104352b1ecd3e93b422a5eb269'),
    'forceunbounce': (0xae114, 0xae1f8, '27de460fc3d0a5ac33a6f84a89fe08d0d246b3d08635e6e955bc6807d3a8e1ce'),
    'visitor_cases_70_75': (0xb1498, 0xb15c8, 'c2b6d40456e12d5282d5807e68584e9a72c69e326892bb7b46d1448130490dd6'),
    'wait_case_44': (0xb0658, 0xb0710, 'fa9b84b849730247e742ba9ddc5886ab88ee1b286eeee65ee3bd66471179f267'),
    'crit_cases_1_2': (0xaf5d4, 0xaf5f0, 'da8dc8a6521f9a7eb9973d947d02d5ebb7e3bebfc14c80b43135dd6f1de0a6f0'),
    'manager_slice_loop': (0xb2880, 0xb2924, 'a37da9e2eb1edea5aa605c19925cd7764dff8e38b9d51d73ba5f84cb706068b5'),
    'guest_walk_step': (0xe6454, 0xe6688, '32be24502c2875f8e64b94d5a138dce43373d8de0c59177d98acfa1d2bce3140'),
    'navigation_step': (0xfec9c, 0xfef10, 'e9a0179e10fe897177f0fb9f5572a8f166b522e8d58613867cef346e172eea1b'),
}

# (code offset, primary opcode, (rD/rS, rA, signed immediate), meaning). Decoded, never raw words.
FIELDS = (
    # State 13 (0xed7e0): one walk step per update; on arrival the admission method writes LETMEON.
    (0xed80c, 11, (0, 3, 2), 'walk step result 2 is logged and treated as arrival (0)'),
    (0xed990, 14, (4, 0, 14), 'LETMEON written -> state 14'),
    # Admission method 0xe03d0: ride state, pending visitor, LETMEON written only while 0.
    (0xe03f0, 32, (3, 3, 408), 'ride state +408 (1 or 4 rejects)'),
    (0xe045c, 40, (3, 29, 104), 'pending visitor +104 must be this guest'),
    (0xe051c, 44, (0, 29, 104), 'pending visitor cleared on admission'),
    (0xe0528, 14, (4, 0, 0), 'reads script variable 0 (LETMEON)'),
    (0xe0544, 14, (4, 0, 0), 'writes the guest id into variable 0 (LETMEON)'),
    # 0xe05c8: consumed once LETMEON differs from the queue head id (+56).
    (0xe05e4, 14, (4, 0, 0), 'state 14 test reads variable 0'),
    (0xe05ec, 40, (0, 31, 56), 'state 14 test compares with the queue head (+56)'),
    # Guest state dispatch 0xef240 (22 states) and state 14 -> 16.
    (0xef26c, 10, (0, 0, 21), 'guest state jump table bound 21'),
    (0xef570, 14, (4, 0, 16), 'consumed -> removed from the list, state 16'),
    # Ride update 0xe0a8c -> 0xe1864: admission first, then breakdown var 7, then LETMEOFF (var 1).
    (0xe0ab4, 32, (0, 3, 408), 'ride update switches on +408 (0 -> 0xe1864)'),
    (0xe189c, 14, (4, 0, 7), 'variable 7 (breakdown status) checked after admission'),
    (0xe1964, 14, (4, 0, 1), 'reads variable 1 (LETMEOFF)'),
    (0xe1a14, 14, (4, 0, 1), 'clears variable 1 ...'),
    (0xe1a18, 14, (5, 0, 0), '... to 0 once the guest got an exit'),
    (0xeec80, 14, (4, 0, 15), 'released guest -> state 15 (leaving the ride)'),
    # BOUNCE 0xadf40: first free 16-byte slot; deadline = now + 1000*dur at +8, start = now at +12.
    (0xadf6c, 32, (0, 3, 100), 'slot count (header bounce size) at script +100'),
    (0xadf80, 32, (5, 31, 40), 'slot table at script +40'),
    (0xadfc8, 36, (0, 4, 8), 'slot +8 = deadline'),
    (0xadfd8, 36, (3, 4, 12), 'slot +12 = start time'),
    (0xadfe0, 42, (4, 31, 108), 'bouncing count (+108) ...'),
    (0xadfe4, 14, (0, 4, 1), '... plus one'),
    # UNBOUNCE 0xae020: first slot in index order with deadline < now and (now - start) mod 1000 < 200.
    (0xae040, 15, (5, 0, 0x1062), 'magic 0x10624dd3 (/1000), high'),
    (0xae04c, 14, (26, 5, 0x4dd3), 'magic 0x10624dd3 (/1000), low'),
    (0xae044, 15, (4, 0, 0x51ec), 'magic 0x51eb851f (/200), high'),
    (0xae050, 14, (25, 4, -0x7ae1), 'magic 0x51eb851f (/200), low'),
    (0xae0a4, 7, (0, 0, 1000), 'elapsed - 1000 * (elapsed / 1000)'),
    (0xae0c0, 42, (4, 27, 108), 'bouncing count (+108) ...'),
    (0xae0cc, 14, (4, 4, -1), '... minus one on release'),
    # Interpreter: BOUNCING reads the count; CRIT_UNLOCK and WAIT clear the remaining budget (+152).
    (0xb159c, 42, (5, 31, 108), 'BOUNCING returns the bouncing count'),
    (0xaf5e0, 14, (0, 0, 0), 'CRIT_UNLOCK: flag 0 ...'),
    (0xaf5e8, 36, (0, 31, 152), '... and remaining budget 0 (yields)'),
    (0xb0678, 32, (0, 31, 160), 'WAIT: deadline at script +160'),
    (0xb0688, 36, (0, 31, 160), 'WAIT resumes (deadline cleared) once now >= deadline'),
    (0xb0698, 14, (3, 3, -2), 'WAIT not due: program position back two words ...'),
    (0xb06a0, 36, (0, 31, 152), '... and remaining budget 0 (yields)'),
    (0xb06f8, 36, (3, 31, 160), 'WAIT first encounter stores now + trunc(operand / speed)'),
    (0xb0700, 14, (3, 3, -2), 'WAIT first encounter rewinds two words ...'),
    (0xb0708, 36, (0, 31, 152), '... and yields'),
    (0xaf568, 42, (0, 3, 192), 'speed bias (+192, 50 -> speed 1)'),
    # Script manager 0xb2838: phase filter, budget reload from the header slice, decrement outside CRIT.
    (0xb28a8, 34, (0, 24, 184), 'phase bypass flag +184'),
    (0xb28e0, 32, (0, 24, 148), 'budget reload from header time slice (+148) ...'),
    (0xb28e4, 36, (0, 24, 152), '... into remaining budget (+152)'),
    (0xb2904, 14, (0, 3, -1), 'budget decrement (only with the critical flag clear)'),
    # Guest walk step 0xe6454 and the steering step 0xfec9c (no minimum speed).
    (0xe64d8, 32, (0, 31, 396), 'guest +396 set -> walk step returns 2'),
    (0xfed0c, 32, (5, 30, 24), 'steering force truncated to navigation +24'),
    (0xfed98, 32, (5, 30, 28), 'velocity truncated to navigation +28 (max only)'),
)

SRAWI = (  # (offset, rS, rA, shift): signed shifts after mulhw complete the divisions by 1000 and 200
    (0xae098, 0, 0, 6),
    (0xae0b0, 0, 0, 6),
)

CLRLWI = (  # (offset, rS, rA, SH, MB, ME): ID low three bits vs manager pass low three bits
    (0xb28bc, 3, 3, 0, 29, 31),
    (0xb28c0, 0, 0, 0, 29, 31),
)

CALLS = {
    0xef4f0: 0xed7e0, 0xef528: 0xe05c8, 0xef548: 0xdcebc, 0xed808: 0xe6454, 0xed860: 0xea5a4,
    0xed94c: 0xe03d0, 0xe0530: 0xb5be0, 0xe0550: 0xb57d4, 0xe05e8: 0xb5be0, 0xe0ae4: 0xe1864,
    0xe1890: 0xe1404, 0xe196c: 0xb5be0, 0xe1a04: 0xeeb30, 0xe1a1c: 0xb57d4, 0xeebc0: 0xde1d8,
    0xeec84: 0xef700, 0xb1504: 0xadf40, 0xb1520: 0xae020, 0xb1560: 0xae114, 0xb28f0: 0xaf534,
    0xadfb0: 0x10e844, 0xadfcc: 0x10e844, 0xae03c: 0x10e844, 0xae074: 0x10e844, 0xb0674: 0x10e844,
    0xb06c4: 0x10e844, 0xe6488: 0xffd2c, 0xffd38: 0xfec9c, 0xfeda4: 0xff098, 0xef4b8: 0xe6454,
}

STATE_TABLE = 0x41314   # guest state jump table (data), 4-byte code offsets
STATE_HANDLERS = {11: 0xef4a8, 12: 0xef4b4, 13: 0xef4ec, 14: 0xef4f8, 16: 0xef608}
OPCODE_TABLE = 0x3f2a4  # interpreter switch table (data)
OPCODE_CASES = {1: 0xaf5d4, 2: 0xaf5e0, 6: 0xb0e4c, 44: 0xb0658, 72: 0xb14c8, 73: 0xb1510, 74: 0xb1550,
                75: 0xb1590}


# ---------------------------------------------------------------- RSE corpus reader (bounded)

OPCODES = {1: 'CRIT_LOCK', 2: 'CRIT_UNLOCK', 3: 'COPY', 6: 'ENDSLICE', 13: 'EVENT', 15: 'FLUSHANIM',
           16: 'TRIGANIM', 17: 'WAITANIM', 18: 'LOOPANIM', 19: 'TRIGWAITANIM', 24: 'WAITANIM_CH',
           26: 'TRIGWAITANIM_CH', 28: 'RAND', 29: 'JSR', 30: 'RETURN', 31: 'BRANCH', 32: 'BRANCH_Z',
           33: 'BRANCH_NZ', 34: 'BRANCH_NV', 35: 'BRANCH_PV', 37: 'NAME', 38: 'TEST', 39: 'CMP', 44: 'WAIT',
           45: 'WAITABS', 46: 'WAIT4ANIM', 47: 'ADD', 72: 'BOUNCE', 73: 'UNBOUNCE', 74: 'FORCEUNBOUNCE',
           75: 'BOUNCING', 86: 'STARTSCREAM', 87: 'STOPSCREAM'}
# Opcodes that end a slice. CRIT_UNLOCK and WAIT are proved here (budget store +152); ENDSLICE in
# PPC-clock. The animation waits are treated as yields of unknown length: a loop containing one is
# reported as untraced instead of being given a latency.
YIELDS = {2, 6, 44, 45}
UNTRACED_WAITS = {17, 19, 24, 26, 46}
CONDITIONAL = {32, 33, 34, 35}
OPEN_RIDE_GUARDS = {'VAR_RIDECLOSED', 'VAR_BREAKSTAT'}  # assumption A3: the ride is open and unbroken


def read_rse(raw: bytes) -> dict:
    """RSSEQ container (docs/RSE-SCRIPTS.md): header sizes, instructions with typed operands, names."""
    require(raw[:8], b'RSSEQ\x0f\x01\x00', 'RSE magic')
    variables, = struct.unpack_from('<I', raw, 8)
    stack, time_slice, limbo, bounce, walk = struct.unpack_from('<5i', raw, 12)
    count, = struct.unpack_from('<I', raw, 48)
    words = struct.unpack_from(f'<{count}I', raw, 52)
    at = 52 + 4 * count
    blob_size, = struct.unpack_from('<I', raw, at)
    at += 4 + blob_size
    names = []
    for _ in range(variables):
        size, = struct.unpack_from('<I', raw, at)
        names.append(raw[at + 4:at + 3 + size].decode('ascii'))
        at += 4 + size
    require(at, len(raw), 'RSE trailing bytes')
    program, index = {}, 0
    while index < count:
        require(words[index] >> 16, 0x8000, f'opcode word at {index}')
        opcode, operands, index_next = words[index] & 0xffff, [], index + 1
        while index_next < count and words[index_next] >> 16 != 0x8000:
            kind, value = words[index_next] >> 16, words[index_next] & 0xffff
            operands.append(('var', names[value]) if kind == 0x4000 else
                            ('branch', value) if kind == 0x2000 else
                            ('literal', value) if kind == 0 else ('string', value))
            index_next += 1
        program[index] = (opcode, tuple(operands), index_next)
        index = index_next
    return {'time_slice': time_slice, 'bounce_size': bounce, 'stack': stack, 'program': program,
            'sha256': hashlib.sha256(raw).hexdigest()}


def _successors(program: dict, index: int, previous: int | None) -> list[int]:
    opcode, operands, following = program[index]
    if opcode == 31:
        return [operands[0][1]]
    if opcode in CONDITIONAL:
        guard = program.get(previous)
        if guard and guard[0] == 38 and guard[1][0][0] == 'var' and guard[1][0][1] in OPEN_RIDE_GUARDS:
            return [following]  # TEST VAR_RIDECLOSED / VAR_BREAKSTAT is zero on an open, unbroken ride
        return [following, operands[0][1]]
    return [following]


def slice_paths(program: dict, start: int, stop_opcodes: set[int]) -> list[tuple[int, int, int]]:
    """Every path from `start` (executed first) to the first yield: (yield index, yield opcode, count).

    JSR descends into the subroutine and continues after it on RETURN. The count includes the first
    and the yielding instruction; all instructions are counted (conservative against the budget).
    """
    results, stack = [], [(start, None, 0, (), frozenset())]
    while stack:
        index, previous, executed, returns, seen = stack.pop()
        opcode, operands, following = program[index]
        executed += 1
        if executed > 4 * SLICE_BUDGET or (index, returns) in seen:
            raise ValueError(f'path from {start} does not reach a yield within the budget')
        seen = seen | {(index, returns)}
        if index != start or opcode not in (44,):
            if opcode in stop_opcodes or opcode in YIELDS:
                results.append((index, opcode, executed))
                continue
            if opcode in UNTRACED_WAITS:
                raise ValueError(f'animation wait {OPCODES[opcode]} at {index} on the loop path (untraced)')
        if opcode == 29:
            stack.append((operands[0][1], index, executed, returns + (following,), seen))
            continue
        if opcode == 30:
            stack.append((returns[-1], index, executed, returns[:-1], seen))
            continue
        for successor in _successors(program, index, previous):
            stack.append((successor, index, executed, returns, seen))
    return results


def bounce_loop(script: dict) -> dict:
    """The normal (open, unbroken) BOUNCE loop: admission slice A and release slice B.

    A starts when the loop's WAIT resumes and must end at the CRIT_UNLOCK after BOUNCE; B starts after
    it and must end at that same WAIT. Returns the WAIT operand and the per-slice instruction counts.
    """
    program = script['program']
    bounces = [index for index, (opcode, _, _) in program.items() if opcode == 72]
    require(len(bounces), 1, 'one BOUNCE instruction')
    bounce = bounces[0]
    unlock = next((index for index in sorted(program) if index > bounce and program[index][0] == 2), None)
    if unlock is None:
        raise ValueError('no CRIT_UNLOCK after BOUNCE: the admission slice end is untraced')
    release = slice_paths(program, program[unlock][2], set())
    waits = {index for index, opcode, _ in release}
    require({program[index][0] for index in waits}, {44}, 'release slice ends at WAIT')
    require(len(waits), 1, 'release slice ends at one WAIT')
    wait = waits.pop()
    admission = slice_paths(program, wait, set())
    require({index for index, _, _ in admission}, {unlock}, 'admission slice ends at the CRIT_UNLOCK after BOUNCE')
    opcode, operands, _ = program[wait]
    require(operands[0][0], 'literal', 'WAIT operand is a literal')
    longest = max(count for _, _, count in admission + release)
    require(longest <= script['time_slice'], True, 'loop slices fit the instruction budget')
    letmeon = program[bounce][1]
    return {'bounce_index': bounce, 'unlock_index': unlock, 'wait_index': wait, 'wait_ms': operands[0][1],
            'bounce_operands': [value for _, value in letmeon],
            'admission_paths': len(admission), 'release_paths': len(release), 'longest_slice': longest}


# ---------------------------------------------------------------- pure restatements

def wait_resume_slices(wait_ms: int, turn_ms: int = TURN_MS, speed_bias: int = 50) -> int:
    """Slices after the WAIT's first encounter until it resumes (0xb0658, now >= set + trunc(op / speed)).

    Slices of one ordinary script are one park turn apart (PPC-clock: eight 31 ms passes, ID phase).
    """
    speed = 0.5 + speed_bias / 100
    return math.ceil(int(wait_ms / speed) / turn_ms)


def loop_period(wait_ms: int, turn_ms: int = TURN_MS) -> int:
    """Turns between admission slices: A (ends at CRIT_UNLOCK), B (sets WAIT), then the WAIT's slices."""
    return 1 + wait_resume_slices(wait_ms, turn_ms)


def nominal_hold_turns(duration_s: int, period: int, turn_ms: int = TURN_MS, limit: int = 100000) -> int:
    """Turns from BOUNCE (slice A) to the UNBOUNCE that frees the slot (a later slice B).

    Nominal clock: every slice reads now on the turn grid. Polls fall 4j+1 turns after BOUNCE; the
    release needs deadline < now and (now - start) mod 1000 < 200 (0xae088..0xae0bc).
    """
    start = 0
    deadline = queue.bounce_deadline_ms(start, duration_s)
    for j in range(limit):
        turns = period * j + 1
        if queue.unbounce_releases(turns * turn_ms, start, deadline):
            return turns
    raise ValueError('no release within the search limit')


# Per-boarding latency terms (park turns) once a slot is free and the previous LETMEON was consumed.
LATENCY_TERMS = (
    ('removal', 1, 'previous guest notices consumption in state 14 and leaves the list (0xef528, 0xef548)'),
    ('move_up_delay', MOVE_UP_GAP, 'new head waits trunc(1.2 x recorded) <= 2 turns at gap <= 2 (0xed4a4, 0xef8ac)'),
    ('move_up_start', 1, 'then 0xee604 sets state 12 on the next update (0xed4c0)'),
    ('interlude', INTERLUDE_TURNS, 'at most one interlude: state 8 for 11 turns, next only after 30 (0xef6d8, 0xed5a0)'),
    ('call', 1, 'ride update calls the ready head (0xe1404 -> 0xee794), live-list order'),
    ('notice', 1, 'called head leaves state 11 for state 13 on its next update (0xed2c8)'),
    ('script', None, 'next admission slice A of the loop (period from the RSE WAIT, 0xb0658)'),
)


def boarding_latency_turns(period: int, walk_stand: int, walk_up: int) -> int:
    """H: traced terms plus the two walk terms (state 13 to the stand point, state 12 to slot 0)."""
    fixed = sum(period if turns is None else turns for _, turns, _ in LATENCY_TERMS)
    return fixed + walk_stand + walk_up


def wait_bound_turns(position: int, capacity: int, hold: int, latency: int) -> int:
    """W(p) <= (p + 1) H + (floor(p / CAP) + 1) R + 1 (the last turn: own removal in state 14)."""
    if capacity <= 0:
        raise ValueError('capacity must be positive')
    return (position + 1) * latency + (position // capacity + 1) * hold + 1


def tau_max_turns(capacity: int, hold: int, latency: int, duration_s: int, turn_ms: int = TURN_MS) -> float:
    """The same bound in QUEUE-plan 9b form: per turnover DUR + 1 s + tau with tau = CAP H + R + 1 - (DUR + 1 s)."""
    return capacity * latency + hold + 1 - (duration_s + 1) * 1000 / turn_ms


def implementation_walk_turns(distance_cells: float, speed_cells_s: float, turn_ms: int = TURN_MS,
                              slow_factor: float = 0.7) -> int:
    """OpenTPW walk (APPROX): straight line at the slowest speed, plus one turn of granularity."""
    return math.ceil(distance_cells / (speed_cells_s * slow_factor * turn_ms / 1000)) + 1


# ---------------------------------------------------------------- discrete model of the traced rules

def simulate(seed: int, capacity: int = 5, duration_s: int = 30, walk_stand: int = 1, walk_up: int = 1,
             turns: int = 3000, join_gap: tuple[int, int] = (0, 3), script_first: bool = True,
             ride_first: bool = True, interlude_chance: float = 0.5, queue_max: int = 100, phase: int = 0,
             wait_ms: int = 500) -> list[tuple[int, int]]:
    """Turn-by-turn model of states 11/8/12/13/14, the ride update and the BOUNCE loop; returns (p, W).

    Adversary: random live-list order of new guests, script slice before or after the park update,
    interludes whenever allowed. Walks take a fixed number of updates per slot.
    """
    rng = random.Random(seed)
    slots, mode, deadline = [], 'wait', TURN_MS * phase
    variables = {'LETMEON': 0, 'LETMEOFF': 0, 'ONRIDE': 0}
    pending, queue_list, guests, order, waits = 0, [], {}, ['ride'], []
    next_id, next_join = 1, 0

    def script(turn):
        nonlocal mode, deadline
        now = TURN_MS * turn
        if mode == 'wait':
            if now >= deadline:  # slice A: WAIT resumed, admission, CRIT_UNLOCK yields
                if len(slots) < capacity and variables['LETMEON']:
                    slots.append((variables['LETMEON'], now, queue.bounce_deadline_ms(now, duration_s)))
                    variables['LETMEON'] = 0
                mode = 'release'
        else:  # slice B: UNBOUNCE when LETMEOFF is 0, BOUNCING VAR_ONRIDE, WAIT set
            if variables['LETMEOFF'] == 0:
                for slot in slots:
                    if queue.unbounce_releases(now, slot[1], slot[2]):
                        slots.remove(slot)
                        variables['LETMEOFF'] = slot[0]
                        break
            variables['ONRIDE'] = len(slots)
            deadline, mode = now + wait_ms, 'wait'

    def enter_standing(guest, turn):
        guest.update(state=11, delay=queue.move_delay(guest['recorded']))

    def ride(turn):
        nonlocal pending
        if (variables['LETMEON'] == 0 and variables['ONRIDE'] < capacity and pending == 0 and queue_list):
            head = guests[queue_list[0]]
            if head['state'] == 11 and head['recorded'] == 0:
                head['called'], pending = 1, head['id']
        if variables['LETMEOFF']:
            guests[variables['LETMEOFF']]['state'] = 15
            variables['LETMEOFF'] = 0

    def update(guest, turn):
        nonlocal pending
        state = guest['state']
        if state == 11:
            if guest['recorded'] == 0 and guest['called'] and pending == guest['id']:
                guest.update(called=0, state=13, walk=walk_stand)
                return
            position = queue_list.index(guest['id'])
            if position != guest['recorded']:
                if guest['delay'] and 0 <= guest['recorded'] - position <= MOVE_UP_GAP:
                    guest['delay'] -= 1
                else:
                    slots_moved = max(1, guest['recorded'] - position)
                    guest.update(walk=walk_up * slots_moved, recorded=position, state=12)
                return
            if turn - guest['interlude'] > NEEDS_WINDOW and rng.random() < interlude_chance:
                guest.update(state=8, interlude=turn)
        elif state == 8:
            if turn > guest['interlude'] + INTERLUDE_TURNS - 1:
                enter_standing(guest, turn)
        elif state == 12:
            guest['walk'] -= 1
            if guest['walk'] <= 0:
                enter_standing(guest, turn)
        elif state == 13:
            guest['walk'] -= 1
            if guest['walk'] <= 0:
                require((variables['LETMEON'], pending), (0, guest['id']), 'model: LETMEON free on arrival')
                variables['LETMEON'], pending, guest['state'] = guest['id'], 0, 14
        elif state == 14 and variables['LETMEON'] != queue_list[0]:
            queue_list.pop(0)
            guest['state'] = 16
            waits.append((guest['position'], turn - guest['joined']))

    for turn in range(turns):
        if turn >= next_join and len(queue_list) < queue_max:
            guest = {'id': next_id, 'state': 12, 'recorded': len(queue_list), 'walk': walk_up, 'called': 0,
                     'delay': 0, 'interlude': -10 ** 6, 'joined': turn, 'position': len(queue_list)}
            guests[next_id] = guest
            queue_list.append(next_id)
            order.insert(rng.randrange(len(order) + 1), next_id)
            next_id += 1
            next_join = turn + rng.randint(*join_gap)
        if script_first:
            script(turn)
        sequence = order if ride_first else order[1:] + order[:1]
        for item in list(sequence):
            if item == 'ride':
                ride(turn)
            elif guests[item]['state'] not in (15, 16):
                update(guests[item], turn)
        if not script_first:
            script(turn)
    return waits


# ---------------------------------------------------------------- binary witness

def x_srawi(c, at: int) -> tuple[int, int, int]:
    word = pef._u32(c.code.data, at)
    require((word >> 26, word >> 1 & 0x3ff), (31, 824), f'srawi at code:{at:#x}')
    return (word >> 21 & 31, word >> 16 & 31, word >> 11 & 31)


def check_fields(c) -> int:
    for at, op, expected, meaning in FIELDS:
        require(d_fields(c, at, op), expected, f'{meaning} at code:{at:#x}')
    for at, source, target, shift in SRAWI:
        require(x_srawi(c, at), (source, target, shift), f'division shift at code:{at:#x}')
    for at, *expected in CLRLWI:
        require(queue.rlwinm_fields(c, at), tuple(expected), f'phase mask at code:{at:#x}')
    return len(FIELDS) + len(SRAWI) + len(CLRLWI)


def check_tables(c) -> dict:
    data = bytes(c.data_section.data)
    states = {state: struct.unpack_from('>I', data, STATE_TABLE + 4 * state)[0] for state in STATE_HANDLERS}
    require(states, STATE_HANDLERS, 'guest state handlers 11..16')
    cases = {op: struct.unpack_from('>I', data, OPCODE_TABLE + 4 * op)[0] for op in OPCODE_CASES}
    require(cases, OPCODE_CASES, 'interpreter cases')
    return {'states': {str(k): hex(v) for k, v in states.items()}, 'cases': {str(k): hex(v) for k, v in cases.items()}}


def check_code(c) -> dict:
    blocks = {}
    for name, (start, end, expected) in BLOCKS.items():
        digest = hashlib.sha256(c.code.data[start:end]).hexdigest()
        require(digest, expected, f'{name} bounded code digest')
        blocks[name] = {'code_start': start, 'code_end_exclusive': end, 'sha256': digest}
    witnesses = check_fields(c)
    for at, target in CALLS.items():
        require(call_target(c, at), target, f'call at code:{at:#x}')
    tables = check_tables(c)
    return {'blocks': blocks, 'field_witnesses': witnesses, 'call_witnesses': len(CALLS), 'tables': tables}


def inspect(bin_root: Path) -> dict:
    c = load_identified(bin_root / 'SimThemePark.data')
    require((c.code.index, c.data_section.index), (0, 1), 'section indices')
    report = check_code(c)
    report['binary_sha256'] = hashlib.sha256(c.raw).hexdigest()
    report['witness_source'] = Path(__file__).resolve().relative_to(REPO).as_posix()
    report['plan'] = 'docs/reverse/BOARD-plan.md'
    report['limitation'] = ('Static, bounded: nominal clock (slices on the 248 ms turn grid); the walk to '
                            'the stand point and the move-up walk are not derived (steering model).')
    return report


# ---------------------------------------------------------------- PC data (optional)

BOUNCE_RIDES = (  # theme, wad, script, sam (case-insensitive member names)
    ('jungle', 'bouncy.wad', 'bouncy.rse', 'bouncy.sam'),
    ('space', 'bouncy.wad', 'bouncy.rse', 'bouncy.sam'),
    ('fantasy', 'jelly.wad', 'jelly.rse', 'jelly.sam'),
    ('hallow', 'brainb.wad', 'brainb.rse', 'brainb.sam'),
)


def bounce_ride(pc_data: Path, theme: str, wad_name: str, script_name: str, sam_name: str) -> dict:
    sys.path.insert(0, str(TOOLS / 'lanes' / 'ui'))
    import corpus  # noqa: E402  (ui lane WAD reader, bounded)
    rides = pc_data / 'levels' / theme / 'rides'
    files = {name.lower(): data for name, data in corpus.wad(rides / wad_name).items()}
    script = read_rse(files[script_name])
    loop = bounce_loop(script)
    values = queue.sam_values((rides / 'Rides.sam').read_text(encoding='latin-1'))
    values.update(queue.sam_values(files[sam_name].decode('latin-1')))

    def number(key):
        return int(values[key])
    capacity = queue.clamp_parameter(number('Upgrades[0].InitCapacity'), number('UsageInfo.MinCapacity'),
                                     number('UsageInfo.MaxCapacity'))
    duration = queue.clamp_parameter(number('Upgrades[0].InitDuration'), number('UsageInfo.MinDuration'),
                                     number('UsageInfo.MaxDuration'))
    period = loop_period(loop['wait_ms'])
    return {'theme': theme, 'id': number('Info.Id'), 'script_sha256': script['sha256'],
            'time_slice': script['time_slice'], 'bounce_size': script['bounce_size'], 'loop': loop,
            'period_turns': period, 'capacity': capacity, 'duration': duration,
            'runs_continuously': number('Info.RunsContinuously') if 'Info.RunsContinuously' in values else 0,
            'hold_turns': nominal_hold_turns(duration, period)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--pc-data', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
        if args.pc_data:
            result['bounce_rides'] = [bounce_ride(args.pc_data, *ride) for ride in BOUNCE_RIDES]
    except (OSError, KeyError, ValueError, pef.PEFError) as error:
        parser.exit(1, f'board evidence: {error}\n')
    print(json.dumps(result, sort_keys=True, indent=2))


if __name__ == '__main__':
    main()
