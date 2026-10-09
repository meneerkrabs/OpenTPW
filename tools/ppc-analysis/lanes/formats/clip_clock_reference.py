"""Reference model of the Mac MD2 channel clock and its two scene clocks (witness `md2_channel_clock_selector`).

A standalone restatement of traced arithmetic for tests and review; it is not wired to the OpenTPW runtime and
says nothing about the PC build. Inputs that the trace leaves open are explicit arguments: the raw
`LbTime_GetClock` values, clock A's rate (+0x18), its hold (+0x2c/+0x30) and the save offsets. Who sets the
rate, the hold and the offsets, and how the game maps its speed settings onto them, were not traced.
"""
from __future__ import annotations

import math
import struct
from dataclasses import dataclass

SELECT_CLOCK_B = 0x40
FREEZE_AT_START, FREEZE_AT_END, LOOP = 0x2, 0x4, 0x1
TICKS_PER_SECOND, MILLISECONDS = 30.0, 1000.0


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

    `held`/`held_value` are A's +0x2c/+0x30; `a_offset` and `b_offset` are the restore offsets +0x3c and
    +0x18. A's second offset +0x38 (added only when not held) is taken as 0; its setter is untraced.
    """
    a: ScaledTimer
    b: UnscaledTimer
    held: bool = False
    held_value: int = 0
    a_offset: int = 0
    b_offset: int = 0
    clock_a: int = 0
    clock_b: int = 0

    @classmethod
    def started(cls, raw: int, rate: float = 1.0) -> 'SceneClocks':
        return cls(ScaledTimer(rate=rate, last_raw=raw), UnscaledTimer(last_raw=raw))

    def refresh(self, raw: int) -> None:
        a = self.held_value if self.held else self.a.read(raw)
        self.clock_a = u32(a + self.a_offset)
        self.clock_b = u32(self.b.read(raw) + self.b_offset)

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
