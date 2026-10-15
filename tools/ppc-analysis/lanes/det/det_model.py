"""Reference arithmetic for the determinism contract pinned by det_evidence.py.

Pure functions over supplied state. They restate the native operands (scheduler catch-up, the
recovered generators and their projections) so DET-I can test a C# port against the same vectors.
They are not an original-runtime trace and do not choose seeds, interleaving or save policy.
"""
from __future__ import annotations

from dataclasses import dataclass, field

MASK32 = 0xffffffff
SLICE_MS = 31
BACKLOG_LIMIT_MS = 2000
PARK_CAP = 3

# Canonical replay fields DET-I must hash per park turn (see docs/reverse/DET-plan.md).
CANONICAL_REPLAY_FIELDS = (
    'schema_version', 'scheduler_previous_ms', 'substep_counter', 'manager_pass_counter',
    'park_turn', 'world_rng_state', 'coaster_rng_state', 'clib_rng_state', 'sound_seed',
    'scale', 'paused', 'game_type_mode', 'world_state', 'calendar_funny_start', 'calendar_rate',
    'thing_table_digest', 'script_table_digest', 'economy_digest', 'input_log_cursor',
)


def s32(value: int) -> int:
    value &= MASK32
    return value - (1 << 32) if value & 0x80000000 else value


def wrapping_abs32(value: int) -> int:
    """PowerPC conditional negation on a signed word: abs(INT_MIN) keeps the same bits."""
    signed = s32(value)
    return (-signed) & MASK32 if signed < 0 else signed


# --- World context generator (0x105328 / setter 0x105360) -------------------------------------
def world_step(state: int) -> int:
    return (state * 1664525 + 1013904223) & MASK32


def world_next(state: int) -> tuple[int, int]:
    """Returns (new_state, returned_word). One advance despite the duplicate native store."""
    new = world_step(state)
    return new, wrapping_abs32(new)


def world_reseed(thing_id: int) -> int:
    """Setter stores the unsigned 16-bit thing ID copied from thing+0 (zero-extended)."""
    if not 0 <= thing_id <= 0xffff:
        raise ValueError('thing ID is an unsigned halfword')
    return thing_id


def rse_rand(returned: int, bound_word: int) -> int:
    """RSE RAND projection: labs(labs(returned >>> 1) % (signed16(bound)+1)); bound -1 unsupported."""
    bound = bound_word & 0xffff
    bound = bound - 0x10000 if bound & 0x8000 else bound
    divisor = bound + 1
    if divisor == 0:
        raise ValueError('RAND bound -1 divides by zero; native result unqualified')
    shifted = (returned & MASK32) >> 1
    first = s32(wrapping_abs32(shifted))
    remainder = abs(first) % abs(divisor) * (1 if first >= 0 else -1)  # C truncating remainder
    return wrapping_abs32(remainder)


# --- Generators with their own state -------------------------------------------------------------
def ms_step(state: int) -> int:
    return (state * 214013 + 2531011) & MASK32


def coaster_next(state: int) -> tuple[int, int]:
    """Coaster boarding (module global): logical high half (srwi 16)."""
    new = ms_step(state)
    return new, new >> 16


def object_ms_next(state: int) -> tuple[int, int]:
    """Kart object +0x38 and particle object +0x10: arithmetic high half (srawi 16)."""
    new = ms_step(state)
    return new, s32(new) >> 16


def weather_next(state: int) -> tuple[int, int]:
    """Weather object +0x34 (0x904a8): returns the full new state, no absolute value."""
    new = world_step(state)
    return new, new


def clib_rand(state: int) -> tuple[int, int]:
    """Bundled C library rand: ANSI LCG, 15-bit result. Static initial state is 1."""
    new = (state * 1103515245 + 12345) & MASK32
    return new, (new >> 16) & 32767


def sound_candidate(seed: int) -> int:
    """Sound placeholder draw: a candidate computed from the static seed; the seed is not advanced."""
    return world_step(seed)


# --- Scheduler catch-up (0x1c1208, loop 0x1c22dc..0x1c24cc) ------------------------------------
@dataclass
class SchedulerState:
    previous: int = 0          # scheduled time, 32-bit word of the scaled clock
    substep: int = 0           # substep counter word
    park_work: int = 0         # park turns this callback, reset at the callback tail
    park_turn: int = 0         # world mGameTick
    events: list = field(default_factory=list)


def catch_up(state: SchedulerState, now: int, *, mode: int = 0, application_flag8: bool = False,
             gameplay_flag1: bool = False, max_steps: int = 100000) -> SchedulerState:
    """One game-callback catch-up. Events: (substep, kind) in native order.

    kinds: 'work' (particles, vehicles, script pass), 'even', 'turn', 'turn_capped', 'eighth',
    'thirty_second', 'excluded'. `max_steps` bounds synthetic runs near the signed boundary.
    """
    now &= MASK32
    if s32(now - state.previous) > BACKLOG_LIMIT_MS:
        state.previous = (now - BACKLOG_LIMIT_MS) & MASK32
    steps = 0
    while s32(now) > s32(state.previous):
        if steps == max_steps:
            raise RuntimeError('catch-up did not terminate within the synthetic bound')
        steps += 1
        state.previous = (state.previous + SLICE_MS) & MASK32
        state.substep = (state.substep + 1) & MASK32
        if not (application_flag8 or not gameplay_flag1):
            state.events.append((state.substep, 'excluded'))
            continue
        state.events.append((state.substep, 'work'))
        if state.substep & 1 == 0:
            state.events.append((state.substep, 'even'))
        if state.substep & 7 == 0:
            if state.park_work < PARK_CAP:
                state.park_work += 1
                if mode in (0, 1, 2):
                    state.park_turn = (state.park_turn + 1) & MASK32
                    state.events.append((state.substep, 'turn'))
            else:
                state.events.append((state.substep, 'turn_capped'))
            state.events.append((state.substep, 'eighth'))
        if state.substep & 31 == 0:
            state.events.append((state.substep, 'thirty_second'))
    state.park_work = 0  # callback tail 0x1c27a4
    return state


def interpolation_alphas(now: int, previous: int, even_time: int, turn_time: int) -> tuple[float, float, float]:
    """Render-phase fractions passed to 0x107b0c/0x65e80/0x107958 (non-positive for the 31 ms one)."""
    return (s32(now - previous) / 31.0, s32(now - even_time) / 62.0, s32(now - turn_time) / 248.0)
