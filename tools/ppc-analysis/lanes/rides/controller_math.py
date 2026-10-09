"""Pure math extracted from static Mac controller paths, not a gameplay model.

The validated domains below are helper restrictions, not claims that native
code performs equivalent validation. Structure names/units remain conditional.
"""
import math
import struct


def rse_divmod(dividend: int, divisor: int) -> tuple[int, int]:
    """Opcode49/50: signed32 truncation; explicit zero-divisor result is zero.

    PPC signed-division overflow is deliberately outside this witness domain.
    This rule must not be applied to unrelated controller/geometry divisions.
    """
    if not -(1 << 31) <= dividend < (1 << 31) or not -(1 << 31) <= divisor < (1 << 31):
        raise ValueError("signed32 operands required")
    if divisor == 0:
        return 0, 0
    if dividend == -(1 << 31) and divisor == -1:
        raise ValueError("native signed division overflow remains unqualified")
    quotient = abs(dividend) // abs(divisor)
    if (dividend < 0) != (divisor < 0):
        quotient = -quotient
    return quotient, dividend - quotient * divisor


def ring_next(index: int, capacity: int) -> int:
    """Normalized index version of pointer advance in coaster passenger rings."""
    if capacity <= 0 or not 0 <= index < capacity:
        raise ValueError("valid nonempty ring required")
    return (index + 1) % capacity


def distribute_capacity(total: int, eligible_count: int) -> list[int]:
    """0x3eb84..3eb94: distribute remainder over remaining eligible cars.

    car types that differ from the eligible type receive zero outside this
    helper. Global/config/physical clamps precede this path in native code.
    """
    if not 0 <= total < 1 << 32 or eligible_count <= 0:
        raise ValueError("unsigned total and positive eligible count required")
    output = []
    remaining = total
    for left in range(eligible_count, 0, -1):
        assigned = remaining // left
        output.append(assigned)
        remaining -= assigned
    return output


def f32(value: float) -> float:
    return struct.unpack(">f", struct.pack(">f", value))[0]


def centered_offsets(spacings: list[float], center: int, track_length: float) -> tuple[list[float], list[float]]:
    """0x3ebe8: per-car signed offset and normalized track offset.

    spacings are already selected from car definition fields+28/+32/+36.
    The left side adds that car's spacing; the right side subtracts it.
    Every step is rounded as a PowerPC single-precision operation. Native
    exceptional floats and zero track length are outside the qualified domain.
    """
    if not spacings or not 0 <= center < len(spacings):
        raise ValueError("valid car center required")
    if not math.isfinite(track_length) or track_length <= 0:
        raise ValueError("positive finite track length required")
    if any(not math.isfinite(value) or value < 0 for value in spacings):
        raise ValueError("nonnegative finite car spacings required")
    length = f32(track_length)
    if length == 0:
        raise ValueError("track length underflows single precision")
    offsets = [0.0] * len(spacings)
    distance = 0.0
    for index in range(center - 1, -1, -1):
        distance = f32(distance + f32(spacings[index]))
        offsets[index] = distance
    distance = 0.0
    for index in range(center + 1, len(spacings)):
        distance = f32(distance - f32(spacings[index]))
        offsets[index] = distance
    return offsets, [f32(offset / length) for offset in offsets]


def tour_group_ordinal(occupancy: int, group_size: int) -> int | None:
    """Tour node ordinal = occupancy % group-size + 1; size0 skips binding.

    Occupancy is the magnitude at native boarding/departure update callsites.
    This is not a proof that group-size equals a seat capacity.
    """
    if occupancy < 0 or group_size < 0:
        raise ValueError("nonnegative occupancy and grouping required")
    return None if group_size == 0 else occupancy % group_size + 1


def tour_departure_slot(signed_occupancy: int) -> tuple[int, int]:
    """0x66874..0x668a8: advance negative occupancy and read the last guest."""
    if signed_occupancy >= 0:
        raise ValueError("normal departure requires negative occupancy")
    next_count = signed_occupancy + 1
    return next_count, -next_count


def tour_heading_input(source_angle: int) -> int:
    """Create path converts node field+16 as 4096 units/360, then 1024-input.

    Calling the source angle "degrees", or binding it to TrackInfo.Direction,
    requires further schema proof. This helper qualifies only 0..359 integers.
    """
    if not 0 <= source_angle < 360:
        raise ValueError("only the observed angular domain is qualified")
    return 1024 - source_angle * 4096 // 360
