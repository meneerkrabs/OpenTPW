"""Reference models of the animation paths traced by anim_witness.py (Feral Mac build).

Each function restates one statically read path; the docstring names its offset. Integer
steps are exact; single-precision steps are reproduced with struct round-trips where the
original rounds, otherwise approximated in Python floats. No original bytes are embedded.
"""
from __future__ import annotations

import math
import struct

NO_ANIMATION = 12  # channel AnimID / DeferredAnimID sentinel
TICKS_PER_SECOND = 30.0
INT32_MAX = 0x7fffffff


def f32(value: float) -> float:
    return struct.unpack('<f', struct.pack('<f', value))[0]


def fctiwz(value: float) -> int:
    """Truncating float -> int32 with PowerPC saturation (NaN -> INT32_MIN)."""
    if math.isnan(value):
        return -0x80000000
    if value >= 2 ** 31:
        return INT32_MAX
    if value < -2 ** 31:
        return -0x80000000
    return int(value)


# -------------------------------------------------------------- UV animation
def uv_key(ticks: list[int], time: float) -> tuple[int, float]:
    """0xa8304: the first key whose tick exceeds trunc(time) ends the segment.

    Returns (key, fraction) with the fraction measured from ticks[key] in the float time.
    No later key: (count - 2, 1.0), i.e. the last key holds. A first tick above
    trunc(time) would give key -1 (reads before the array); the corpus starts every track at 0.
    """
    tick = max(int(time), 0)
    for i, value in enumerate(ticks):
        if tick < value:
            if i == 0:
                raise ValueError('time before the first UV key reads outside the track')
            return i - 1, (time - ticks[i - 1]) / (value - ticks[i - 1])
    if len(ticks) < 2:
        raise ValueError('a UV track needs two keys')
    return len(ticks) - 2, 1.0


def uv_sample(ticks: list[int], keys: list[tuple[float, float]], time: float) -> tuple[float, float]:
    """0xa83e8: (1 - t)·uv[k] + t·uv[k + 1] per component (fmuls then fmadds)."""
    key, t = uv_key(ticks, time)
    (u0, v0), (u1, v1) = keys[key], keys[key + 1]
    return (1.0 - t) * u0 + t * u1, (1.0 - t) * v0 + t * v1


def uv_destination(corner: int) -> tuple[int, int]:
    """Byte offsets of (U, V) for a corner in the mesh record's +104 array (blocks of four)."""
    base = 32 * (corner // 4) + 4 * (corner % 4)
    return base, base + 16


def uv_pass_runs(record_flags: int, option_word: int, node_state: int, header_flags: int) -> bool:
    """0xa56bc gate: record 0x10000 and (option bit 0x10 and node bit 0x4000 clear, or header flag 0x4)."""
    if not record_flags & 0x10000:
        return False
    if header_flags & 0x4:
        return True
    return not option_word & 0x10 and not node_state & 0x4000


# ------------------------------------------------------------- 12-byte block
def bind_node_bits(node_state: int, record_flags: int, category: int) -> int:
    """0xa3e24 (bind at channel reset 0xa7bb8): 0x4000 records set node 0x800; outside category 0 clear 0x100."""
    if record_flags & 0x4000:
        node_state |= 0x800
        if category != 0:
            node_state &= ~0x100
    return node_state


def reset_node_bits(node_state: int) -> int:
    """0xa7c88: the channel reset sets 0x100 on every node before binding."""
    return node_state | 0x100


def clear_pass(node_state: int) -> int:
    """0xa7140 / 0xa7ac0: a node with 0x100 loses 0x800."""
    return node_state & ~0x800 if node_state & 0x100 else node_state


# ------------------------------------------------------------- trigger/start
def start_length_ms(duration_ticks: int) -> int:
    """0xa67d8: trunc(1000 × ticks / 30) in single precision; the speed argument does not enter."""
    return fctiwz(f32(f32(1000.0 * f32(float(duration_ticks))) / TICKS_PER_SECOND))


def trigger(channel: dict, anim: int, sub: int, flags: int, speed: float, clip_ticks: int | None,
            current_ticks: int | None = None) -> int:
    """0xa6cc0 on one channel record; returns the milliseconds reported to the caller.

    channel: {'anim', 'sub', 'flags', 'frame', 'total', 'deferred', 'deferred_sub'}.
    clip_ticks: duration of (anim, sub), None when that clip does not exist.
    current_ticks: duration of the channel's current clip, None when it has none.
    """
    current_valid = channel['anim'] != NO_ANIMATION and current_ticks is not None
    defer = (current_valid and channel['frame'] <= channel['total'] and not channel['flags'] & 0x6
             and not 13 <= anim <= 14 and flags & 0x2)
    if defer:
        channel.update(deferred=anim, deferred_sub=sub, deferred_flags=flags, deferred_speed=speed)
        remaining = fctiwz(f32(33.333332 * f32(channel['total'] - channel['frame']))) if current_valid else 0
        return remaining + (start_length_ms(clip_ticks) if clip_ticks is not None else 1000)
    if flags & 0x2:
        channel.update(deferred=NO_ANIMATION, deferred_sub=0, deferred_speed=0.0)
    if clip_ticks is None:
        return 1000
    channel.update(anim=anim, sub=sub, frame=0.0, total=float(clip_ticks))
    channel['flags'] = (channel['flags'] & ~0x21) | (0x1 if flags & 0x1 else 0) | (0x20 if flags & 0x4 else 0)
    return start_length_ms(clip_ticks)


def flush(channel: dict) -> bool:
    """0xa6c90 (FLUSHANIM, FLUSHANIM_CH): clears only the deferred clip; True when one was queued."""
    if channel['deferred'] == NO_ANIMATION:
        return False
    channel['deferred'] = NO_ANIMATION
    return True


# ---------------------------------------------------------------- RSE waits
def duration_out(returned_ms: int) -> int:
    """TRIGANIM/TRIGWAITANIM/TRIGANIM_CH (0xafb20): signed max(returned - 300, 300)."""
    return max(returned_ms - 300, 300)


def wait4anim_deadline(now_ms: int, returned_ms: int, script_speed: float) -> int:
    """TRIGANIM family: script +164 = now + trunc(duration_out / script speed) (overwrites)."""
    return now_ms + fctiwz(f32(float(duration_out(returned_ms)) / script_speed))


def waitanim_wait(returned_ms: int, script_speed: float) -> int:
    """WAITANIM (0xafc78): (returned - 300) converted UNSIGNED, / speed, truncated, then max(·, 300) unsigned.

    A returned length below 300 ms wraps to ~4.29e9, which fctiwz saturates to INT32_MAX: the
    script then waits about 24.8 days. WAITANIM_CH differs: see waitanim_ch_deadline.
    """
    unsigned = (returned_ms - 300) & 0xffffffff
    value = fctiwz(f32(f32(float(unsigned)) / script_speed))
    return value if value & 0xffffffff >= 300 else 300


def waitanim_ch_deadline(now_ms: int, returned_ms: int) -> int:
    """WAITANIM_CH (0xb0330-0xb0360): max(unsigned(returned - 300), 300) with no float step and no
    fctiwz, added to the 32-bit clock as a wrapping add (script +160). A returned length below 300 ms
    gives 2^32 - (300 - returned), so the deadline lands 300 - returned ms before now; the resume
    compare (cmplw now, deadline at 0xb02bc) then continues at the next execution.
    """
    wait = (returned_ms - 300) & 0xffffffff
    if wait < 300:
        wait = 300
    return (now_ms + wait) & 0xffffffff


def triganimspeed_deadline(now_ms: int, returned_ms: int, operand: int) -> int:
    """TRIGANIMSPEED (0xb00dc): now + duration_out × 1000 / operand (signed divw, truncating)."""
    numerator = duration_out(returned_ms) * 1000
    quotient = abs(numerator) // abs(operand)
    return now_ms + (quotient if (numerator < 0) == (operand < 0) else -quotient)


def triganimspeed_speed(script_speed: float, operand: int) -> float:
    """TRIGANIMSPEED clip speed: script speed × operand / 1000 (per mille)."""
    return f32(f32(script_speed * operand) / 1000.0)


def wait4anim_blocks(deadline: int, now_ms: int) -> bool:
    """WAIT4ANIM (0xb07a0): a zero deadline never blocks; otherwise block while now < deadline (unsigned)."""
    return deadline != 0 and (now_ms & 0xffffffff) < (deadline & 0xffffffff)


def loopanim_token(anim: int, sub: int) -> int:
    """LOOPANIM (0xafd2c): script +168 token; an equal token makes LOOPANIM a no-op."""
    return (anim + (sub << 16)) & 0xffffffff


def getanim(channel_flags: int, anim: int) -> int:
    """GETANIM / GETANIM_CH (0xaff80, 0xb0614): -1 once channel flag 0x4 is set (a finished
    non-looping clip, AnimID 14 path), else the channel's AnimID (12 = none)."""
    return -1 if channel_flags & 0x4 else anim


def trigwaitanim_resumes(current_anim: int, waited_anim: int) -> bool:
    """TRIGWAITANIM (0xafe14): resume once channel 0's AnimID equals the triggered one (+188 = id + 1)."""
    return current_anim + 1 == waited_anim + 1


# ------------------------------------------------------------- channel mix
def node_winner(channels: list[tuple[int, bool]]) -> int | None:
    """0xa7960: channels update in ascending index; for a component several channels set, the last wins.

    channels: (index, writes_this_component_now) — returns the winning channel index or None.
    """
    winner = None
    for index, writes in sorted(channels):
        if writes:
            winner = index
    return winner


def idle_restart(object_flags: int, channel0_ended: bool) -> tuple[int, int] | None:
    """0xa798c: objects with +4 & 0x8004 clear restart (AnimID 5, sub 0) while channel 0 has ended (≤ 3 times)."""
    if object_flags & 0x8004 or not channel0_ended:
        return None
    return 5, 0


# ----------------------------------------------------------------- advisor
def advisor_clip(animation_id: int, level_suffix: str = '') -> str:
    """0x29414: AnimID 5 ('m'), sub = id - 1; the loader names member sub + 1 (`Advisor%s` + 'm' + n)."""
    if animation_id < 1:
        raise ValueError('advisor animation ids start at 1')
    sub = animation_id - 1
    return f'Advisor{level_suffix}m{sub + 1}.MD2'


# ----------------------------------------------------------------- loader
def loads(major: int, flags: int) -> bool:
    """engine 0x3fc74: major > 221 rejected; major < 221 needs argument bit 0x1, which no call site passes."""
    if major > 221:
        return False
    return major == 221 or bool(flags & 0x1)
