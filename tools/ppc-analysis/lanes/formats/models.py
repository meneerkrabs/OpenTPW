"""Reference models of selected original decoder mechanics (Feral Mac PowerPC build).

Each function restates one statically read code path; the docstring names the
function offset in the identity-pinned binary (see format_witness.py and
docs/reverse/PPC-formats.md). Arithmetic is modelled in Python floats unless a
step is integer-exact in the original, so float rounding of single-precision
and fused multiply-add steps is approximate. No original bytes are embedded.
"""
from __future__ import annotations

import math
import struct

# ---------------------------------------------------------------- MD2 loader
M3D2_MAGIC = 0x1CD15D46
MESH_VERSION = 221
ANIM_VERSION = 203
LOAD_ALLOW_OLD = 1
LOAD_ANIMATION = 2


def load_status(major: int, minor: int, has_trailer: bool, flags: int, magic: int = M3D2_MAGIC):
    """engine_shared LoadM3D2Header (code 0x3f9d8): status label and whether a header is returned.

    The returned header may have its animation stripped (label DeadAnim) or be
    accepted with a warning label (OldCode, OldMesh); "Ok" is the clean path.
    """
    if magic != M3D2_MAGIC:
        return 'BadFileType', False
    if major > MESH_VERSION:
        return 'OldCode', False
    if major < MESH_VERSION and not flags & LOAD_ALLOW_OLD:
        return 'DeadMesh', False
    if not flags & LOAD_ANIMATION and has_trailer:
        return 'Master is Anim', False
    if flags & LOAD_ANIMATION and not has_trailer:
        return 'Anim is Master', False
    if has_trailer:
        if minor > ANIM_VERSION:
            return 'OldCode', True      # trailer dropped, header flag 0x20 cleared
        if minor < ANIM_VERSION:
            return 'DeadAnim', True     # trailer dropped, header flags at 0x30 zeroed
        return 'Ok', True
    if major < MESH_VERSION:
        return 'OldMesh', True
    return 'Ok', True


# ------------------------------------------------------- MD2 vertex animation
def _signed10(value: int) -> int:
    return value - 1024 if value & 512 else value


def unpack_vertex_key(word: int) -> tuple[int, int, int]:
    """Packed key word as stored little-endian: signed 10-bit X, Y, Z in bits 0-9, 10-19, 20-29.

    engine_shared 0x41cac rewrites each word into big-endian bit fields (bits
    30-31 cleared) and SimThemePark 0xa4344 extracts them with arithmetic
    shifts, so the fields are signed.
    """
    return _signed10(word & 1023), _signed10(word >> 10 & 1023), _signed10(word >> 20 & 1023)


def vertex_key_value(q: tuple[int, int, int], offset, scale) -> tuple[float, float, float]:
    """Per axis q * scale + offset (block +20 offset vec3, +32 scale vec3)."""
    return tuple(q[i] * scale[i] + offset[i] for i in range(3))


def lerp(a, b, t: float):
    return tuple(a[i] * (1.0 - t) + b[i] * t for i in range(3))


def group0_translation(current, following, t: float, scale) -> tuple[float, float, float]:
    """SimThemePark 0xa468c: first group's first vertex, minus one scale step and 0.25 per axis."""
    value = lerp(current, following, t)
    return tuple(value[i] - scale[i] - 0.25 for i in range(3))


# ------------------------------------------------------------ key sampling
KEY_STRIDES = {1: 4, 2: 20, 4: 16}  # position times, rotation keys, scale keys


def find_key(ticks: list[int], time: float, loop: bool):
    """SimThemePark 0xa3ff0: last key whose tick <= trunc(time), the following key and fraction.

    Returns None before the first key (the caller then leaves the channel
    unchanged). At the last key a looping channel pairs it with key 0; a
    non-looping channel pairs it with itself at tick + 1.
    """
    if time < 0:
        tick = 0
    else:
        tick = min(int(time), 0xffffffff)
    last = None
    for index, value in enumerate(ticks):
        if value > tick:
            break
        last = index
    if last is None:
        return None
    if last == len(ticks) - 1:
        if loop:
            following, following_tick = 0, ticks[0]
        else:
            following, following_tick = last, ticks[last] + 1
    else:
        following, following_tick = last + 1, ticks[last + 1]
    denominator = following_tick - ticks[last]
    fraction = (time - ticks[last]) / denominator if denominator else math.inf
    return last, following, fraction


def rotation_pair(index: int, count: int) -> tuple[int, int]:
    """SimThemePark 0xa820c ignores the search's wrap: the second key is min(index + 1, count - 1)."""
    return index, index + 1 if index < count - 1 else index


EASING_SCALE = struct.unpack('>f', struct.pack('>f', 8.999995))[0]


def ease(table: bytes, fraction: float) -> float:
    """SimThemePark 0xa50d4: piecewise-linear easing through (0,0), eight byte samples /255 and (1,1).

    The fraction is scaled by 8.999995 (not 9) and truncated to pick a segment.
    """
    if len(table) < 8:
        raise ValueError('easing curve needs eight samples')
    s = fraction * EASING_SCALE
    segment = int(s) if s >= 0 else 0
    local = s - segment
    if segment == 0:
        low, high = 0.0, table[0] / 255.0
    elif segment < 8:
        low, high = table[segment - 1] / 255.0, table[segment] / 255.0
    else:
        low, high = table[7] / 255.0, 1.0
    return (1.0 - local) * low + local * high


def bezier(points: list[tuple[float, float, float]], base: int, t: float):
    """SimThemePark 0xa85ac: cubic Bezier over P[i-1], P[i], P[i+1], P[i+2] with i = base + 3*trunc(t).

    Indices wrap modulo the point count. The position sampler passes
    base = 3 * key + 1, i.e. control points P[3k] .. P[3k+3].
    """
    n = len(points)
    k = int(t)  # fctiwz: toward zero
    s = t - k
    i = base + 3 * k + n
    p0, p1, p2, p3 = (points[(i + d) % n] for d in (-1, 0, 1, 2))
    u = 1.0 - s
    return tuple(p0[a] * u ** 3 + 3 * p1[a] * s * u * u + 3 * p2[a] * s * s * u + p3[a] * s ** 3 for a in range(3))


# ------------------------------------------------------------- clock
TICKS_PER_SECOND = 30.0


def animation_time(elapsed_ms: int, speed: float) -> float:
    """SimThemePark 0xa6484: speed * (30 * elapsed_ms / 1000) ticks."""
    return speed * (TICKS_PER_SECOND * elapsed_ms / 1000.0)


def remaining_ms(duration: float, time: float) -> float:
    """SimThemePark 0xa65a0: (duration - time) * 33.333332."""
    return (duration - time) * struct.unpack('>f', struct.pack('>f', 1000.0 / 30.0))[0]


# --------------------------------------------------------------- BF4 blend
def bf4_blend_channel(destination: int, colour: int, coverage: int, alpha: int = 255) -> int:
    """ltms_shared TbOneColour4BitFontRenderMethod::DrawCharTo8888 (code 0xb41c), one 8-bit channel.

    weight = coverage (alpha 255) or trunc(coverage * alpha / 255); the channel
    adds weight * (colour - destination) / 15 computed with an unsigned
    multiply-high by 0x88888889, so a negative difference lands one coverage
    step short (e.g. 255 -> 17 rather than 0 at full coverage).
    """
    if not (0 <= destination <= 255 and 0 <= colour <= 255 and 0 <= coverage <= 15 and 0 <= alpha <= 255):
        raise ValueError('channel, coverage or alpha out of range')
    weight = coverage if alpha == 255 else int(coverage * alpha / 255) & 0xff
    product = (weight * (colour - destination)) & 0xffffffff
    quotient = (0x88888889 * product >> 32) >> 3
    return (destination + quotient) & 0xff


# ------------------------------------------------------------------ TPWS
# SimThemePark 0x11cb60: each subsystem block is followed by its 4-byte tag.
SECTION_ORDER = (
    ('World', b'DLRW'), ('Scripts', b'CSPS'), ('Particles', b'TRAP'),
    ('MessageCentre', b'SSEM'), ('Clock', b'KOLC'), ('VanillaTime', b'TNAV'),
    ('GameSystem', b'SYSG'), ('RideSystem', b'SYSR'), ('TrackRides', b'KART'),
    ('FlyingRides', b'RYLF'), ('RSSE', b'ESSR'), ('Camera', b'EMAK'),
    ('Coasters', b'SAOC'), ('Advisor', b'SVDA'), ('Sound', b'NUOS'),
    ('Cheat', b'STHC'), ('AdvisorScoring', b'CSDA'),
)


def split_sections(payload: bytes):
    """Blocks in writer order, each ending just before its delimiter; returns blocks and the untagged UI tail.

    The World block starts at payload offset 0 and includes the action-record
    prefix. Delimiters are located by forward search, which is ambiguous if a
    block happens to contain a later tag first; callers must cross-check sizes.
    """
    blocks = []
    position = 0
    for name, tag in SECTION_ORDER:
        found = payload.find(tag, position)
        if found < 0:
            raise ValueError(f'missing delimiter for {name}')
        blocks.append((name, tag.decode('ascii'), position, found))
        position = found + 4
    return blocks, (position, len(payload))


ACTION_RECORD_FIELDS = (('mLoadedPublishedPark', 4), ('recording_size', 4))
WORLD_VAR_FIELDS = (
    ('version', 4), ('mArrivalVehicle_Size1', 2), ('mArrivalVehicle_Size2', 2),
    ('mArrivalVehicle_Size3', 2), ('mBankAccount', 2), ('mCurrentArrivalVehicle', 2),
    ('mGameTick', 4), ('mMechanicHQ', 2), ('mParkAnalyser', 2), ('mParkClosed', 4),
    ('mNumberOfVisitorsToDate', 4), ('mParkGates', 2), ('mTrafficLights', 2),
    ('mRandomSeed', 4), ('mResearchLab', 2), ('mStaffHQ', 2), ('mTagSystem', 2),
    ('mUIMsgReceiver', 2), ('mWeather', 2), ('mWorldState', 4), ('mFirstHandyman', 2),
    ('mFirstMechanic', 2), ('mFirstEntertainer', 2), ('mFirstGuard', 2),
    ('mFirstResearcher', 2), ('mFirstObject', 2),
)
CELL_BASE_FIELDS = (
    ('mDirection', 1), ('mFlags', 2), ('mMeshInstance', 4), ('mNeighbours', 1),
    ('mOverlapCounter', 2), ('mParentID', 2), ('mTileData', 12), ('mType', 4),
    ('mHoardingNeighbours', 1),
)
MAP_CELL_FIELDS = CELL_BASE_FIELDS + (
    ('mLitter', 4), ('mLitterCollector', 2), ('mLitterScript', 4), ('mLitterScript2', 4),
    ('mPylonIndex', 2), ('mStatusFlags', 1), ('mTimeMarkedForLitterCollection', 4), ('mWho', 2),
)
TRACK_CELL_FIELDS = CELL_BASE_FIELDS + (('mSegmentNumber', 2),)
REGION_EFFECT_FIELDS = tuple((f'mpEffect[{i}]', 2) for i in range(5))
CELL_MAP, CELL_TRACK, CELL_REGION_EFFECT = 1, 2, 4
GRID_CELLS = 16384


def _fields(buf: bytes, offset: int, layout):
    values = {}
    for name, width in layout:
        if offset + width > len(buf):
            raise ValueError(f'truncated field {name}')
        raw = buf[offset:offset + width]
        values[name] = int.from_bytes(raw, 'little') if width in (1, 2, 4) else bytes(raw)
        offset += width
    return values, offset


def layout_size(layout) -> int:
    return sum(width for _, width in layout)


def parse_world_vars(payload: bytes):
    """Action-record prefix (u32 flag, u32 size, recording bytes) then the labelled world variables."""
    head, offset = _fields(payload, 0, ACTION_RECORD_FIELDS)
    offset += head['recording_size']
    world, end = _fields(payload, offset, WORLD_VAR_FIELDS)
    return head, offset, world, end


def parse_cell(buf: bytes, offset: int):
    """SimThemePark 0xd6c90: status byte, then map (52), track (31) and region-effect (10) cells as flagged."""
    if offset >= len(buf):
        raise ValueError('truncated cell status')
    status = buf[offset]
    if status & ~7:
        raise ValueError(f'unknown cell status {status:#x}')
    offset += 1
    cell = {'status': status}
    for bit, key, layout in ((CELL_MAP, 'map', MAP_CELL_FIELDS), (CELL_TRACK, 'track', TRACK_CELL_FIELDS),
                             (CELL_REGION_EFFECT, 'region_effect', REGION_EFFECT_FIELDS)):
        if status & bit:
            cell[key], offset = _fields(buf, offset, layout)
    return cell, offset


# --------------------------------------------------------------- TP2M MAP
def map_chunk_cell(row: int, column: int, origin_x: int = 0, origin_y: int = 0):
    """SimThemePark 0xd7e6c: file row -> X, column -> Y; out-of-range cells are skipped.

    Returns the zero-based X-fastest cell index or None.
    """
    x, y = row - origin_x, column - origin_y
    if not (0 <= x < 128 and 0 <= y < 128):
        return None
    return (y << 7) + x
