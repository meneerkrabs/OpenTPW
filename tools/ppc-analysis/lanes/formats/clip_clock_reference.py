"""Reference model of the Mac MD2 channel clock and its two scene clocks (witness `md2_channel_clock_selector`).

A standalone restatement of traced arithmetic for tests and review; it is not wired to the OpenTPW runtime and
says nothing about the PC build. The raw `LbTime_GetClock` values and the save offsets are explicit inputs.
Clock A's rate starts at 1.0 and changes only through three "game" key-table handlers (witness
`md2_scene_clock_controls`): x1.25 and /1.25 clamped to [0.25, 2.0], and back to 1.0. The hold is a fixed-step
mode (1000 // n ms per main-loop step, n = 32 at both call sites) used while loading and while continuous
screenshot capture is on. Which physical keys reach the handlers was not traced.
"""
from __future__ import annotations

import math
import struct
from dataclasses import dataclass

SELECT_CLOCK_B = 0x40
FREEZE_AT_START, FREEZE_AT_END, LOOP = 0x2, 0x4, 0x1
TICKS_PER_SECOND, MILLISECONDS = 30.0, 1000.0
RATE_STEP, RATE_MIN, RATE_MAX, RATE_RESET = 1.25, 0.25, 2.0, 1.0
HOLD_STEPS_PER_SECOND = 32


def f32(value: float) -> float:
    return struct.unpack('>f', struct.pack('>f', value))[0]


def u32(value: int) -> int:
    return value & 0xffffffff


def to_unsigned(value: float) -> int:
    """0x1c3fbc: double to unsigned, truncating; below zero gives 0, at or above 2^32 saturates."""
    if math.isnan(value):
        raise ValueError('NaN is not modelled')
    if value < 0.0:
        return 0
    if value >= 4294967296.0:
        return 0xffffffff
    return int(value)


@dataclass
class ScaledTimer:
    """Clock A's base (0x127cd0, pause 0x117c54/0x117c9c, read 0x117d74)."""
    rate: float = 1.0
    last_raw: int = 0
    accumulator: float = 0.0
    paused: bool = False
    paused_at: int = 0
    paused_total: int = 0

    def scaled(self, raw: int) -> int:
        self.accumulator += float(u32(raw - self.last_raw)) * self.rate
        self.last_raw = raw
        return to_unsigned(self.accumulator)

    def read(self, raw: int) -> int:
        if self.paused:
            return u32(self.paused_at - self.paused_total)
        return u32(self.scaled(raw) - self.paused_total)

    def pause(self, raw: int) -> None:
        if not self.paused:
            self.paused_at, self.paused = self.scaled(raw), True

    def resume(self, raw: int) -> None:
        if self.paused:
            self.paused_total, self.paused = u32(self.paused_total + self.scaled(raw) - self.paused_at), False


@dataclass
class UnscaledTimer:
    """Clock B's base (0x10edb4, pause 0x117ae8/0x117b30, read 0x117c00): the same shape without a rate."""
    last_raw: int = 0
    accumulator: float = 0.0
    paused: bool = False
    paused_at: int = 0
    paused_total: int = 0

    def unscaled(self, raw: int) -> int:
        self.accumulator += float(u32(raw - self.last_raw))
        self.last_raw = raw
        return to_unsigned(self.accumulator)

    def read(self, raw: int) -> int:
        if self.paused:
            return u32(self.paused_at - self.paused_total)
        return u32(self.unscaled(raw) - self.paused_total)

    def pause(self, raw: int) -> None:
        if not self.paused:
            self.paused_at, self.paused = self.unscaled(raw), True

    def resume(self, raw: int) -> None:
        if self.paused:
            self.paused_total, self.paused = u32(self.paused_total + self.unscaled(raw) - self.paused_at), False


@dataclass
class SceneClocks:
    """Global block +16400 (A) and +16408 (B), refreshed together by 0xa6f70; channels read the stored values.

    `held`/`held_value`/`held_step` are A's +0x2c/+0x30/+0x34; `release_offset` is +0x38 (added only when not
    held, set only by `release`); `a_offset` and `b_offset` are the restore offsets +0x3c and +0x18.
    """
    a: ScaledTimer
    b: UnscaledTimer
    held: bool = False
    held_value: int = 0
    held_step: int = 0
    release_offset: int = 0
    a_offset: int = 0
    b_offset: int = 0
    clock_a: int = 0
    clock_b: int = 0

    @classmethod
    def started(cls, raw: int, rate: float = 1.0) -> 'SceneClocks':
        return cls(ScaledTimer(rate=rate, last_raw=raw), UnscaledTimer(last_raw=raw))

    def unheld(self, raw: int) -> int:
        """0x10ed54: the held value, or the scaled timer plus +0x38."""
        return self.held_value if self.held else u32(self.a.read(raw) + self.release_offset)

    def refresh(self, raw: int) -> None:
        self.clock_a = u32(self.unheld(raw) + self.a_offset)
        self.clock_b = u32(self.b.read(raw) + self.b_offset)

    def hold(self, raw: int, steps_per_second: int = HOLD_STEPS_PER_SECOND) -> None:
        """0x10ec60: freeze A at its current value and step it by 1000 // n (unsigned) ms; no-op when held."""
        if not self.held:
            self.held_value = self.unheld(raw)
            self.held_step = 1000 // steps_per_second
            self.held = True

    def step(self) -> None:
        """0x10ed10, once per main-loop pass (0x1104d8): +0x30 += +0x34 unless A is paused (held or not)."""
        if not self.a.paused:
            self.held_value = u32(self.held_value + self.held_step)

    def release(self, raw: int) -> None:
        """0x10ecc0: A continues from the held value (+0x38 = held - scaled timer)."""
        if self.held:
            self.release_offset = u32(self.held_value - self.a.read(raw))
            self.held = False

    def pause(self, raw: int) -> None:
        """0x10e888 pauses both timers; 0x10e8bc resumes both. No other caller pauses either one."""
        self.a.pause(raw)
        self.b.pause(raw)

    def resume(self, raw: int) -> None:
        self.a.resume(raw)
        self.b.resume(raw)

    def selected(self, channel_flags: int) -> int:
        return self.clock_b if channel_flags & SELECT_CLOCK_B else self.clock_a


@dataclass
class Channel:
    """The clock fields of one 56-byte channel: flags +0, speed +12, start +16, AnimTime +20, frames +28/+32."""
    flags: int = 0
    speed: float = 1.0
    start: int = 0
    anim_time: int = 0
    no_pause_time: int = 0
    total_frames: float = 0.0
    frame: float = 0.0


def faster(rate: float) -> float:
    """0x127c48 (handler 0x113184): rate * 1.25, then clamped to [0.25, 2.0]."""
    return clamp_rate(rate * RATE_STEP)


def slower(rate: float) -> float:
    """0x127c88 (handler 0x11315c): rate / 1.25, then the same clamp."""
    return clamp_rate(rate / RATE_STEP)


def clamp_rate(rate: float) -> float:
    """`fcmpo` against 0.25 then 2.0; an unordered (NaN) rate fails the first test and becomes 0.25."""
    if not rate >= RATE_MIN:
        return RATE_MIN
    return rate if rate <= RATE_MAX else RATE_MAX


def fresh_start(channel: Channel, duration_ticks: int, clocks: SceneClocks) -> None:
    """0xa6398 with r5 != 0: start = AnimTime = NoPauseAnimTime = the selected clock."""
    channel.total_frames = f32(float(duration_ticks))
    channel.start = channel.anim_time = channel.no_pause_time = clocks.selected(channel.flags)


def update(channel: Channel, clocks: SceneClocks) -> float:
    """0xa7190's clock step (object flag 0x8000 clear)."""
    now = clocks.selected(channel.flags)
    if channel.flags & (FREEZE_AT_START | FREEZE_AT_END):
        if channel.flags & FREEZE_AT_START:
            channel.anim_time, channel.frame = channel.start, 0.0
        else:
            hold = to_unsigned(f32(f32(f32(MILLISECONDS * channel.total_frames) / TICKS_PER_SECOND) / channel.speed))
            channel.anim_time, channel.frame = u32(channel.start + hold), channel.total_frames
        channel.no_pause_time = now
        return channel.frame
    channel.anim_time = channel.no_pause_time = now
    elapsed = f32(float(u32(channel.anim_time - channel.start)))
    channel.frame = f32(channel.speed * f32(f32(TICKS_PER_SECOND * elapsed) / MILLISECONDS))
    return channel.frame


def reset_channels(channels: list[Channel], use_clock_b: bool) -> None:
    """0xa7bb8's selector step: ORs 0x40 into every channel when asked and never clears it."""
    for channel in channels:
        if use_clock_b:
            channel.flags |= SELECT_CLOCK_B


def instantiate(template: list[Channel]) -> list[Channel]:
    """0x543cc: the instance gets a byte copy of the template's channels, selector bit included."""
    return [Channel(**vars(channel)) for channel in template]
