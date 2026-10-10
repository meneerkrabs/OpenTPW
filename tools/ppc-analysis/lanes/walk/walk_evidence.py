"""Bounded static guest-walk witnesses (WALK-R); never execute or emit original instructions.

Usage: python3 -I walk_evidence.py /path/to/mac-feral/bin [--derive]

Pins the guest steering of the Feral Mac SimThemePark.data: the per-update steering step (0xfec9c),
the behaviour list (avoid_walls 0.9, follow_path 0.5, separation 0.1), the speed setter with its
655 floor (0xffe38), the per-update speed smoothing from guest +192/+194/+196 (0xe6b28), the
arrival rule of follow_path (0xfe628) and the route/destination conventions. Addresses are
section-relative. Instructions are decoded into fields and compared with expected operands; only
digests, decoded fields and derived values are reported. The pure functions below restate the decoded
rules in 16.16 fixed point so that the walk terms w and w2 of the BOARD-R boarding bound can be
derived by bounded enumeration. See docs/reverse/WALK-plan.md for the narrative and the bounds.
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
from timer_evidence import call_target, d_fields, glue_import, load_identified, require  # noqa: E402
import queue_evidence as queue  # noqa: E402  (rlwinm_fields)

TOC = 0x8000              # main transition vector data:0x8d48 -> TOC data:0x8000 (PPC-guests)
ONE = 0x10000             # 16.16 fixed point; the globals at data:0xecd84 / 0xec790 are set to it
RADIUS = 13107            # navigation +4, set once by the constructor (0xffc68): 0.2 cell
SPEED_FLOOR = 655         # speed setter floor on both +24 and +28 (0xffe88..0xffea4)
SPEED_CAP = 2.0           # speed setter input cap (data:0x5668)
MAX_SPEED_FACTOR = 0.2    # +28 = trunc(s * 0.2 * 65536) (data:0x5650)
MAX_FORCE_FACTOR = 0.4    # +24 = trunc(s * 0.4 * 65536) (data:0x5658)
WEIGHTS = {'avoid_walls': 58982, 'follow_path': 32768, 'separation': 6553}  # 0x10000-6554, 0x10000-0x8000, 6553
ARRIVE_FACTOR = 0x20000 - 26215  # 1.6: last segment arrives at octile < FixMul(1.6, radius) (0xfe7ac/0xfe7b4)
WAYPOINT_FACTOR = 0x20000        # 2.0: an intermediate waypoint is passed at octile < FixMul(2.0, radius) (0xfe850)
RAMP_DIVISOR = 0x20000           # arrival ramp: desired speed min(+28, |offset| / 2.0) (0xfe644, 0xfea08)
STUCK_FRACTION = 26214           # reroute when >= 0.4 of the last 15 steps did not improve (0xfe6c0, 0xff978)
STUCK_WINDOW = 15
SPEED_TABLE = (0, 25, 50, 60, 80, 100, 120, 140)  # data:0x41058 (u16); +192 takes entries 3..7, +194 entries 0/1
BASE_SPEEDS = SPEED_TABLE[3:]                     # constructor: +192 = data:0x4105e[rand % 5] (0xe48b8..0xe48d0)
TOILET_BONUS = SPEED_TABLE[1]                     # +194 = 25 while toilet > 80, else 0 (0xef0bc..0xef0e8)
SPEED_DIVISOR = SPEED_TABLE[5]                    # 0xe6b58 reads data:0x4105e + 4 = 100
SMOOTH_OLD, SMOOTH_NEW = 0.75, 0.25               # +200 = (3.0 * +200 + sum / 100) * 0.25 (data:0x550c, 0x5508)
BOOST_DECAY = 99                                  # +196 = trunc(+196 * 99 / 100) every update (0xe6bbc..0xe6bd4)
STAND_SCALE = 255.0                               # stand-point byte = trunc(255 * EntryCellStandPos) (data:0x54c0)
QUEUE_DEPTHS = (0, 63, 127, 191)                  # QUEUE-plan 3.4 (0xddcc4)
LATERALS = (114, 141)                             # rand mod 28 + 114
TURN_MS = 248

# (code start, end exclusive, sha256) of the blocks interpreted for this lane.
BLOCKS = {
    'walk_step': (0xe6454, 0xe6688, '32be24502c2875f8e64b94d5a138dce43373d8de0c59177d98acfa1d2bce3140'),
    'speed_update': (0xe6b28, 0xe6bfc, 'e05bb553789383bb6caf510f809a96739e1d1f3d21fce1cb68b42d71eeb97f43'),
    'set_destination': (0xe6864, 0xe68ec, '3453a1d360c57f346b96586cb412f325f08eb6a1be139bd8efae38b9f1fa7904'),
    'navigation_step': (0xfec9c, 0xfef10, 'e9a0179e10fe897177f0fb9f5572a8f166b522e8d58613867cef346e172eea1b'),
    'follow_path': (0xfe628, 0xfea88, '09bd717e7a2f08fdac5d5aaeeceed71b3bf45967f271cf67d800c5b0eae755c5'),
    'separation': (0xfd778, 0xfd8a8, 'd615b3c93ac5e6f0c00fc6515561ceead7f428f42c08823aa35bd50ea254056f'),
    'avoid_walls': (0xfdc70, 0xfe604, '451f5fbfa0db791d32f6502ac8a52500b9d143d3c70a82ff35e2c305a07d07eb'),
    'truncate': (0xff098, 0xff1c4, 'af8f025060634ab6450a2adf3e001496f58aad9126c3fce21a9215280954bfd1'),
    'speed_setter': (0xffe38, 0xffeb0, 'ca62577c5c8326c022a2556a2443ddf203ffc881307164a942e6213b5a6c94a3'),
    'nav_constructor': (0xffb88, 0xffcdc, '2eec3282e5db2ccad0f1e8d19f3132edbb2767452441618933f44c2eae420185'),
    'behaviour_list': (0xff99c, 0xffb88, '3d64f4b7826496681fd998998437f825a54336b2cb12a4e7582d7aa66a8ca9e7'),
    'progress': (0xff738, 0xff810, '9b6b8c714266656fddefe85ceae15aded61bfc7f1b5ba372a4dafdc9e9f9afb6'),
    'stuck_fraction': (0xff810, 0xff99c, 'fe51e125ace15cf8f7fd2f79443736f1a4affb68ff50c642f6572bfa62d50b12'),
    'arrival_progress': (0xffd60, 0xffe38, 'ca5b14768690fef18d872316d8bc7a2452c0a5183a2cbbda2149d87b5b466910'),
    'route_setup': (0xff224, 0xff738, '0bd62157967dab81dbf06c03dd0799fae65c5a3a053f4783e9c16a11781422fc'),
    'route_finish': (0x102894, 0x10292c, 'ea4b3f022c9551a5e966b0d77c4f17b7e0409f938713356baa59cde67dd81b95'),
    'speed_base_init': (0xe48a0, 0xe48e0, '176628069f6b6b72e98311732ce1b2e5127d723301aeb987d53133c1f3cce964'),
    'toilet_bonus': (0xef0bc, 0xef0ec, '8c30dbfa1dd648f3029e3be14212318db924312fb9de2bafb2a1232dc8d45d2a'),
    'stand_point_bytes': (0xde490, 0xde534, '2f55335f9c202d1fc3b6dc9d8ec96270fb9c00e3f20d8383805e86c54cf82f7c'),
    'vec_scale': (0xfd580, 0xfd5f4, '057cb18ffbea4ac33fe1e0b777756cc64d9a6cde2013005a087ed751fe6a5d22'),
    'vec_divide': (0xfd704, 0xfd778, 'a6c2c6366e1905d9fc2f72e0974d9e9b2121dc6f77c086cebcb9e4cb7e29192b'),
    'vec_sub': (0xfcb8c, 0xfcbb0, '866ce9db54c87890f8bc0183244b8f5aba771100800b4f0c2580742b090c6f7b'),
    'vec_add': (0xfe604, 0xfe628, 'c57b2b680d14e1b4f810b18a7bf4a27a0aa20dc5728b7ac7471b7ab2b17bb6be'),
    'static_init_one': (0x1002f4, 0x100330, '987554bb345439a09377130c3d8c0feefa48c46e28ff0daf1750ee5c6bb6a466'),
}

# (code offset, primary opcode, (rD/rS, rA, signed immediate), meaning). Decoded, never raw words.
FIELDS = (
    # Speed setter 0xffe38: s capped at 2.0; +24 = trunc(s 0.4 65536), +28 = trunc(s 0.2 65536); both >= 655.
    (0xffe38, 48, (0, 2, -10648), 'lfs cap 2.0 (data:0x5668)'),
    (0xffe48, 50, (0, 2, -10664), 'lfd 0.4 (data:0x5658)'),
    (0xffe4c, 50, (2, 2, -10656), 'lfd 65536.0 (data:0x5660)'),
    (0xffe64, 36, (0, 3, 24), 'max force -> navigation +24'),
    (0xffe68, 50, (0, 2, -10672), 'lfd 0.2 (data:0x5650)'),
    (0xffe80, 36, (0, 3, 28), 'max speed -> navigation +28'),
    (0xffe88, 11, (0, 0, SPEED_FLOOR), 'max force compared with 655'),
    (0xffe90, 14, (0, 0, SPEED_FLOOR), 'max force floor 655'),
    (0xffe9c, 11, (0, 0, SPEED_FLOOR), 'max speed compared with 655'),
    (0xffea4, 14, (0, 0, SPEED_FLOOR), 'max speed floor 655'),
    # Speed update 0xe6b28 (every guest update, before the state handler): smoothing of (+192 + +194 + +196) / 100.
    (0xe6b44, 40, (6, 3, 196), 'reads +196 (boost)'),
    (0xe6b48, 40, (0, 3, 194), 'reads +194 (toilet bonus)'),
    (0xe6b50, 40, (7, 31, 192), 'reads +192 (base speed)'),
    (0xe6b58, 40, (0, 4, 4), 'divisor: speed table entry 5 (100)'),
    (0xe6b74, 48, (5, 2, -10996), 'lfs 3.0 (data:0x550c)'),
    (0xe6b7c, 48, (4, 31, 200), 'reads smoothed speed +200'),
    (0xe6b90, 48, (0, 2, -11000), 'lfs 0.25 (data:0x5508)'),
    (0xe6ba4, 52, (1, 31, 200), 'stores smoothed speed +200 (passed to the setter)'),
    (0xe6bbc, 7, (0, 0, BOOST_DECAY), 'boost +196 times 99 ...'),
    (0xe6bb0, 15, (3, 0, 20972), '... divided by 100 (magic 0x51eb851f, high)'),
    (0xe6bb4, 14, (6, 3, -31457), '... (magic 0x51eb851f, low)'),
    # Constructor 0xe4870: +192 from data:0x4105e[rand % 5], +194 = data:0x41058[0] = 0, +200 = 0.0.
    (0xe48ac, 32, (5, 2, -27232), 'base-speed table data:0x4105e'),
    (0xe48c0, 7, (0, 0, 5), 'rand % 5'),
    (0xe48d0, 44, (0, 29, 192), '+192 = table[rand % 5]'),
    (0xe48d8, 44, (0, 29, 194), '+194 = table[0]'),
    (0xe4880, 52, (0, 29, 200), '+200 = 0.0'),
    # Needs block 0xef0bc: +194 = 25 while toilet (+428) > 80, else 0.
    (0xef0bc, 48, (0, 29, 428), 'reads toilet +428'),
    (0xef0d0, 10, (0, 0, 80), 'toilet > 80'),
    (0xef0d8, 40, (0, 30, 2), 'speed table entry 1 (25)'),
    (0xef0dc, 44, (0, 29, 194), '+194 = 25'),
    (0xef0e4, 40, (0, 30, 0), 'speed table entry 0 (0)'),
    # Navigation constructor 0xffb88: radius 0.2 at +4, arrived flag set, speed setter called with 1.0.
    (0xffc58, 38, (29, 30, 96), 'arrived flag +96 = 1'),
    (0xffc68, 14, (0, 0, RADIUS), 'radius 13107 ...'),
    (0xffc8c, 36, (0, 28, 4), '... stored at navigation +4'),
    (0xffcb0, 48, (1, 2, -10644), 'initial speed input 1.0 (data:0x566c)'),
    # Behaviour list 0xff99c (built at level load 0x104d20): names compared, weights pushed.
    (0xff9a4, 32, (29, 2, -26824), 'behaviour list (data:0xecd6c)'),
    (0xff9f0, 15, (21, 0, 1), 'lis 1 ...'),
    (0xffa08, 14, (4, 31, 564), 'name "avoid_walls"'),
    (0xffa24, 14, (27, 21, -6554), '... weight 0x10000 - 6554 (0.9)'),
    (0xffa74, 15, (27, 0, 1), 'lis 1 ...'),
    (0xffa8c, 14, (4, 31, 576), 'name "follow_path"'),
    (0xffaa8, 14, (25, 27, -32768), '... weight 0x10000 - 0x8000 (0.5)'),
    (0xffb0c, 14, (4, 31, 588), 'name "separation"'),
    (0xffb2c, 14, (23, 0, 6553), 'weight 6553 (0.1)'),
    # Steering step 0xfec9c: force truncation (+24), mass ONE, velocity truncation (+28), position update.
    (0xfecb4, 32, (29, 2, -26824), 'iterates the behaviour list'),
    (0xfed0c, 32, (5, 30, 24), 'each force truncated to +24'),
    (0xfed54, 32, (5, 30, 24), 'sum truncated to +24'),
    (0xfed64, 32, (5, 2, -18924), 'divided by ONE (data:0xecd84)'),
    (0xfed98, 32, (5, 30, 28), 'velocity truncated to +28'),
    (0xfedb8, 36, (0, 30, 16), 'velocity stored at +16'),
    (0xfee7c, 36, (0, 30, 8), 'position committed at +8 (walkable cell only)'),
    (0xfeed4, 32, (0, 30, 172), 'progress history: compared with the previous progress +172'),
    # follow_path 0xfe628.
    (0xfe644, 15, (28, 0, 2), 'arrival ramp divisor 2.0'),
    (0xfe6c0, 11, (0, 3, STUCK_FRACTION), 'reroute when the stuck fraction >= 0.4'),
    (0xfe71c, 34, (0, 31, 96), 'arrived: brake (-velocity)'),
    (0xfe7ac, 15, (3, 0, 2), 'arrival factor 1.6, high ...'),
    (0xfe7b4, 14, (3, 3, -26215), '... low'),
    (0xfe7b0, 32, (4, 31, 4), 'times the radius +4'),
    (0xfe7d8, 38, (0, 31, 96), 'arrived flag set'),
    (0xfe850, 15, (3, 0, 2), 'intermediate waypoint factor 2.0'),
    (0xfe854, 32, (4, 31, 4), 'times the radius +4'),
    (0xfea10, 32, (0, 31, 28), 'ramp speed min(+28, |offset| / 2)'),
    # separation 0xfd778: off near the last waypoint, neighbourhood 2 x radius.
    (0xfd810, 32, (3, 2, -18924), 'ONE ...'),
    (0xfd81c, 32, (0, 30, 4), '... plus the radius: separation off within this octile distance'),
    # avoid_walls 0xfdc70: radius margin, 2 x penetration.
    (0xfde4c, 32, (3, 31, 4), 'wall margin = radius'),
    (0xfde20, 32, (6, 31, 180), 'edge test with navigation mask +180'),
    # Walk step 0xe6454: arrived when progress == ONE (data:0xec790).
    (0xe64f4, 32, (4, 2, -19560), 'arrival compares with ONE (data:0xec790)'),
    (0xe64d8, 32, (0, 31, 396), 'guest +396 set -> result 2'),
    # Static initialisers: both ONE globals = 0x10000, ZERO = 0.
    (0x100304, 32, (4, 2, -18924), 'data:0xecd84 ...'),
    (0x10031c, 15, (0, 0, 1), '... = lis 1 ...'),
    (0x10032c, 36, (0, 4, 0), '... stored'),
    (0xe7484, 32, (3, 2, -19560), 'data:0xec790 ...'),
    (0xe7478, 15, (0, 0, 1), '... = lis 1 ...'),
    (0xe7488, 36, (0, 3, 0), '... stored'),
    # Route setup 0xff224: failure -> arrived; success -> five buffered waypoints; route finish appends the destination.
    (0xff294, 14, (0, 0, 1), 'route failure: flag 1 ...'),
    (0xff2c8, 38, (0, 31, 96), '... into arrived (+96)'),
    (0xff34c, 14, (0, 0, 5), 'at most 5 buffered waypoints'),
    (0xff708, 38, (0, 31, 96), 'route success: arrived cleared'),
    (0x1028d4, 44, (0, 3, 6), 'destination cell appended to the route'),
    # Stand point 0xde1d8: byte = trunc(255 x fraction) after rotation.
    (0xde500, 50, (0, 2, -11072), 'lfd 255.0 (data:0x54c0)'),
)

RLWINM = (  # (offset, rS, rA, SH, MB, ME)
    (0xe68cc, 4, 4, 8, 0, 23),   # destination 8.8 -> 16.16 (x)
    (0xe68d0, 0, 5, 8, 0, 23),   # (y)
    (0xfd870, 0, 4, 1, 0, 30),   # separation neighbourhood 2 x radius
    (0xfde6c, 3, 3, 1, 0, 30),   # wall force 2 x penetration
    (0xe6bd4, 0, 0, 27, 5, 31),  # boost / 100 (srwi 5)
)

CALLS = {
    0xe6488: 0xffd2c, 0xffd38: 0xfec9c, 0xe64f0: 0xffd60, 0xeed0c: 0xe6b28, 0xe6ba8: 0xffe38, 0xffcb4: 0xffe38,
    0xe488c: 0xffb88, 0x104d20: 0xff99c, 0x104e1c: 0xfeb1c, 0xfed10: 0xff098, 0xfed60: 0xff098,
    0xfeda4: 0xff098, 0xfed20: 0xfd580, 0xfed74: 0xfd704, 0xfed94: 0xfe604, 0xfedc4: 0xfe604,
    0xfeec4: 0xff738, 0xfe6a8: 0xff738, 0xfe6bc: 0xff810, 0xfe730: 0xfea88, 0xfe6d4: 0xff224,
    0xe68d4: 0xffcfc, 0xffd18: 0xff224, 0xfd874: 0x10087c, 0xfde2c: 0xd768c, 0xfdc9c: 0xff1c4,
}
GLUE = {  # import glue -> symbol
    0x1c73ec: 'FixMul', 0x1c73d4: 'FixDiv', 0x1c552c: 'sqrt',
}
GLUE_CALLS = {0xfe7b8: 0x1c73ec, 0xfe858: 0x1c73ec, 0xfea08: 0x1c73d4, 0xfea28: 0x1c73d4, 0xfea3c: 0x1c73d4,
              0xff168: 0x1c73d4, 0xff180: 0x1c73ec, 0xfd5b0: 0x1c73ec, 0xfd734: 0x1c73d4, 0xd2f00: 0x1c552c}

DATA_DOUBLES = {0x5650: 0.2, 0x5658: 0.4, 0x5660: 65536.0, 0x54c0: STAND_SCALE}
DATA_FLOATS = {0x5668: SPEED_CAP, 0x566c: 1.0, 0x550c: 3.0, 0x5508: 0.25}
SPEED_TABLE_AT = 0x41058
TOC_GLOBALS = {-18924: 0xecd84, -19560: 0xec790, -26824: 0xecd6c, -27232: 0x4105e, -27224: 0x41058}
NAME_BASE_SLOT = -18916    # TOC slot -> code:0x1d2e8d (string pool)
NAMES = {564: 'avoid_walls', 576: 'follow_path', 588: 'separation'}
BEHAVIOURS = {  # name -> (object TOC slot, vtable TOC slot, vtable, method): the static initialiser (0x1002f4..)
    # stores the vtable in the object; vtable +8 -> transition vector -> force method.
    'avoid_walls': (-26908, -26912, 0x447d8, 0xfdc70),
    'follow_path': (-26920, -26924, 0x447b8, 0xfe628),
    'separation': (-26872, -26876, 0x44838, 0xfd778),
}
BEHAVIOUR_OBJECTS = 0x4477c  # data: 8 registered behaviour objects, compared by name in 0xff99c


# ---------------------------------------------------------------- pure restatements (16.16 fixed point)

def fix_mul(a: int, b: int, rounding: str = 'nearest') -> int:
    """Toolbox FixMul. 'nearest' rounds the 32.32 product; 'floor' drops the low half (both are checked)."""
    product = a * b
    return (product + 0x8000) >> 16 if rounding == 'nearest' else product >> 16


def fix_div(a: int, b: int, rounding: str = 'nearest') -> int:
    """Toolbox FixDiv with saturation on division by zero."""
    if b == 0:
        return 0x7fffffff if a >= 0 else -0x80000000
    if rounding == 'nearest':
        return math.floor(a * ONE / b + 0.5)
    return int(a * ONE / b)


def isqrt(value: int) -> int:
    """0xd2edc: trunc(sqrt(double(unsigned value)))."""
    return int(math.sqrt(value & 0xffffffff))


def length(x: int, y: int) -> int:
    """Vector length as in 0xff098/0xfe958: exact below 2^12, else in units of 2^8 or 2^18."""
    ax, ay = abs(x), abs(y)
    bits = ax | ay
    if bits & 0xfffff000 == 0:
        return isqrt(ax * ax + ay * ay)
    if bits & 0xffc00000 == 0:
        return isqrt((ax >> 8) ** 2 + (ay >> 8) ** 2) << 8
    return isqrt((ax >> 18) ** 2 + (ay >> 18) ** 2) << 18


def octile(x: int, y: int) -> int:
    """|dx| + |dy| - min/2 (0xfe778..0xfe7c8, 0xff784..0xff7d8)."""
    ax, ay = abs(x), abs(y)
    return ax + ay - (min(ax, ay) >> 1)


def truncate(vector: tuple[int, int], maximum: int, rounding: str = 'nearest') -> tuple[int, int]:
    """0xff098: unchanged when shorter than the maximum, else scaled by FixDiv(maximum, length)."""
    size = length(*vector)
    if size < maximum:
        return vector
    scale = fix_div(maximum, size, rounding)
    return (fix_mul(vector[0], scale, rounding), fix_mul(vector[1], scale, rounding))


def speed_caps(speed: float) -> tuple[int, int]:
    """0xffe38: (+28 max speed, +24 max force) for the smoothed speed input s."""
    s = min(speed, SPEED_CAP)
    return (max(SPEED_FLOOR, int(s * MAX_SPEED_FACTOR * 65536)), max(SPEED_FLOOR, int(s * MAX_FORCE_FACTOR * 65536)))


def smooth_speed(previous: float, base: int, bonus: int = 0, boost: int = 0) -> float:
    """0xe6b28: +200 = 0.75 x +200 + 0.25 x (+192 + +194 + +196) / 100."""
    return SMOOTH_OLD * previous + SMOOTH_NEW * (base + bonus + boost) / SPEED_DIVISOR


def speed_lower_bound(updates: int, base: int = min(BASE_SPEEDS)) -> float:
    """Smallest +200 after `updates` speed updates from the constructor's 0.0, all inputs >= base / 100."""
    return base / SPEED_DIVISOR * (1 - SMOOTH_OLD ** updates)


def arrival_radius(rounding: str = 'nearest') -> int:
    return fix_mul(ARRIVE_FACTOR, RADIUS, rounding)


def waypoint_radius(rounding: str = 'nearest') -> int:
    return fix_mul(WAYPOINT_FACTOR, RADIUS, rounding)


def desired_speed_floor(max_speed: int, rounding: str = 'nearest') -> int:
    """Smallest desired speed on the last segment before arrival: min(+28, d / 2) with the Euclidean d of a
    point whose octile distance is at least the arrival radius (octile <= 1.118 x Euclidean)."""
    euclid = math.floor(arrival_radius(rounding) / (1.25 / math.sqrt(1.25)))
    return min(max_speed, fix_div(euclid, RAMP_DIVISOR, rounding))


def cell_of(point: tuple[int, int]) -> tuple[int, int]:
    return (point[0] >> 16, point[1] >> 16)


def centre(cell: tuple[int, int]) -> tuple[int, int]:
    """Route waypoints are cell centres (cell << 16) + 32768 (0xff360..0xff384)."""
    return ((cell[0] << 16) + 32768, (cell[1] << 16) + 32768)


def byte_point(cell: tuple[int, int], bx: int, by: int) -> tuple[int, int]:
    """Destination bytes: (cell << 8 | byte) << 8 (0xe68cc/0xe68d0), i.e. byte / 256 of a cell."""
    return ((cell[0] << 16) + (bx << 8), (cell[1] << 16) + (by << 8))


def route_cells(start: tuple[int, int], destination: tuple[int, int]) -> list[tuple[int, int]]:
    """Route of a straight strip: every visited cell from the start cell (0x101d6c) to the destination, which is
    appended on arrival (0x1028d4); a start in the destination cell gives [destination]."""
    if start == destination:
        return [destination]
    step = 1 if destination[1] > start[1] else -1
    require(start[0], destination[0], 'straight strip')
    return [(start[0], y) for y in range(start[1], destination[1] + step, step)]


def walk(position, velocity, target, route, speed, *, rounding='nearest', separation='off', fences=True,
         strip=None, limit=5000):
    """Steering updates until the walk step reports arrival (state 12/13 updates), or None with a reason.

    Per update (0xfec9c): avoid_walls, follow_path, separation forces, each truncated to +24 and weighted;
    the sum truncated to +24 and divided by ONE; velocity += force, truncated to +28; position += velocity.
    separation='oppose' applies the largest separation force (0.1 x +24) against the walking direction
    whenever the traced on/off rule leaves it on. fences=True puts blocked edges on both strip sides.
    The progress history (+176) and the 6-of-15 reroute rule are tracked; a reroute ends the run.
    """
    max_speed, max_force = speed_caps(speed)
    waypoints = [centre(cell) for cell in route]
    count, index, arrived = len(waypoints), 0, False
    arrive, passed = arrival_radius(rounding), waypoint_radius(rounding)
    history, previous, walls, separated = [], None, 0, 0
    p, v = position, velocity
    for update in range(1, limit + 1):
        # avoid_walls (0xfdc70): probe p + v; a blocked edge in the direction of motion within the radius.
        probe = (p[0] + v[0], p[1] + v[1])
        wall = (0, 0)
        if fences:
            low, high = ((probe[0] >> 16) << 16) + RADIUS, (((probe[0] >> 16) + 1) << 16) - RADIUS
            if v[0] >= 0 and probe[0] > high:
                wall = (2 * (high - probe[0]), 0)
            elif v[0] < 0 and probe[0] < low:
                wall = (2 * (low - probe[0]), 0)
            walls += wall != (0, 0)
        # follow_path (0xfe628).
        if arrived:
            path = (-v[0], -v[1])
            goal = target
        else:
            if index == count - 1:
                goal = target
                if octile(goal[0] - p[0], goal[1] - p[1]) < arrive:
                    arrived = True
            else:
                if octile(waypoints[index][0] - p[0], waypoints[index][1] - p[1]) < passed:
                    index += 1
                goal = waypoints[index]
            offset = (goal[0] - p[0], goal[1] - p[1])
            if index < count - 1:
                path = (offset[0] - v[0], offset[1] - v[1])
            else:
                size = length(*offset)
                if size == 0:
                    path = (0, 0)
                else:
                    half = fix_div(size, RAMP_DIVISOR, rounding)
                    scale = fix_div(max_speed if max_speed < half else half, size, rounding)
                    path = (fix_mul(offset[0], scale, rounding) - v[0], fix_mul(offset[1], scale, rounding) - v[1])
        # separation (0xfd778): off on the last segment (or once arrived) within ONE + radius of its waypoint.
        near = octile(p[0] - waypoints[index][0], p[1] - waypoints[index][1]) <= ONE + RADIUS
        push = (0, 0)
        if not ((index == count - 1 or arrived) and near) and separation == 'oppose':
            separated += 1
            direction = (goal[0] - p[0], goal[1] - p[1])
            scale = fix_div(max_force, length(*direction) or 1, rounding)
            push = (-fix_mul(direction[0], scale, rounding), -fix_mul(direction[1], scale, rounding))
        total = [0, 0]
        for force, name in ((wall, 'avoid_walls'), (path, 'follow_path'), (push, 'separation')):
            force = truncate(force, max_force, rounding)
            total[0] += fix_mul(force[0], WEIGHTS[name], rounding)
            total[1] += fix_mul(force[1], WEIGHTS[name], rounding)
        force = truncate((total[0], total[1]), max_force, rounding)
        v = truncate((v[0] + force[0], v[1] + force[1]), max_speed, rounding)
        p = (p[0] + v[0], p[1] + v[1])
        if strip is not None and cell_of(p) not in strip:
            return None, {'reason': 'left the strip', 'update': update}
        # progress (0xff738) against the current waypoint; a bit when it did not improve (0xfeec8..0xfeee8).
        remaining = octile(p[0] - waypoints[index][0], p[1] - waypoints[index][1]) + sum(
            octile(waypoints[j][0] - waypoints[j - 1][0], waypoints[j][1] - waypoints[j - 1][1])
            for j in range(index + 1, count))
        history.append(previous is not None and remaining >= previous)
        previous = remaining
        if not arrived and sum(history[-STUCK_WINDOW:]) * ONE // STUCK_WINDOW >= STUCK_FRACTION:
            return None, {'reason': 'reroute', 'update': update}
        if arrived:
            return update, {'walls': walls, 'separation': separated, 'position': p, 'velocity': v}
    return None, {'reason': 'limit', 'update': limit}


# ---------------------------------------------------------------- the queue-front geometry (straight strip)

ENTRANCE, FRONT, BEHIND = (0, -1), (0, 0), (0, 1)    # local frame: the entrance cell is at -y of the front cell
STRIP = {ENTRANCE, FRONT, BEHIND}


def stand_point(fraction: float = 0.5) -> tuple[int, int]:
    """0xde1d8: byte trunc(255 x EntryCellStandPos) in the entrance cell; Belly Bounce uses the default 0.5."""
    byte = int(STAND_SCALE * fraction)
    return byte_point(ENTRANCE, byte, byte)


def slot_point(slot: int, lateral: int, reversed_axis: bool = False) -> tuple[int, int]:
    """Queue slot in the front cell: depth along y from the entrance edge (or from the far edge if reversed)."""
    depth = QUEUE_DEPTHS[slot]
    return byte_point(FRONT, lateral, 255 - depth if reversed_axis else depth)


def standing_starts(origin, speed, rounding='nearest', grid=9):
    """Where a guest that arrived at `origin` can stand: Euclidean distance <= arrival radius + 2 x +28 (the
    arrival update's own step plus any braking drift), inside the strip and at least a radius from its sides
    and ends (avoid_walls keeps a standing guest there)."""
    max_speed, _ = speed_caps(speed)
    reach = arrival_radius(rounding) + 2 * max_speed
    for i in range(grid):
        for j in range(grid):
            dx, dy = -reach + 2 * reach * i // (grid - 1), -reach + 2 * reach * j // (grid - 1)
            x, y = origin[0] + dx, origin[1] + dy
            if (dx * dx + dy * dy <= reach * reach and 0 <= x < ONE and RADIUS <= x <= ONE - RADIUS
                    and -ONE + RADIUS <= y <= 2 * ONE - RADIUS):
                yield (x, y)


def stale_velocities(speed):
    """Velocity left from the previous walk (route setup does not reset +16): zero or any of 16 directions
    at full and half +28."""
    max_speed, _ = speed_caps(speed)
    yield (0, 0)
    for size in (max_speed, max_speed // 2):
        for k in range(8):
            yield (int(size * math.cos(k * math.pi / 4)), int(size * math.sin(k * math.pi / 4)))


def worst_walk(kind: str, speed: float, *, rounding='nearest', separation='off', fences=True, reversed_axis=False,
               laterals=(114, 127, 141), grid=9):
    """Largest update count of walk `kind` ('w': slot 0 -> stand point, state 13; 'w2': slots 1..3 -> slot 0,
    state 12) over standing starts, stale velocities and lateral bytes; failures are listed."""
    worst, worst_case, failures = 0, None, []
    for lateral in laterals:
        if kind == 'w':
            origins, target, destination = [slot_point(0, lateral, reversed_axis)], stand_point(), ENTRANCE
        else:
            origins = [slot_point(slot, side, reversed_axis) for slot in (1, 2, 3) for side in LATERALS]
            target, destination = slot_point(0, lateral, reversed_axis), FRONT
        for origin in origins:
            for start in standing_starts(origin, speed, rounding, grid):
                route = route_cells(cell_of(start), destination)
                for velocity in stale_velocities(speed):
                    updates, info = walk(start, velocity, target, route, speed, rounding=rounding,
                                         separation=separation, fences=fences, strip=STRIP)
                    if updates is None:
                        failures.append((start, velocity, info['reason']))
                    elif updates > worst:
                        worst, worst_case = updates, (start, velocity, lateral)
    return worst, worst_case, failures


VARIANTS = tuple((separation, fences, rounding) for separation in ('off', 'oppose') for fences in (True, False)
                 for rounding in ('nearest', 'floor'))


def derive_bounds(speeds, reversed_axis=False, variants=VARIANTS, grid=9) -> dict:
    """w and w2 maxima over the speed grid and variants; any failure is reported with its speed."""
    result = {}
    for kind in ('w', 'w2'):
        best, failures = 0, []
        for speed in speeds:
            for separation, fences, rounding in variants:
                worst, _, failed = worst_walk(kind, speed, rounding=rounding, separation=separation, fences=fences,
                                              reversed_axis=reversed_axis, grid=grid)
                best = max(best, worst)
                failures += [(speed, reason) for _, _, reason in failed]
        result[kind] = {'max_updates': best, 'failures': len(failures),
                        'failure_speeds': sorted({speed for speed, _ in failures})}
    return result


def varying_speed_check(seeds: int, bound_w: int, bound_w2: int, minimum: float) -> int:
    """Walks whose speed input changes every update by the traced smoothing (inputs >= minimum, up to 3.0)."""
    checked = 0
    for seed in range(seeds):
        rng = random.Random(seed)
        speeds = [rng.uniform(minimum, SPEED_CAP)]
        for _ in range(400):
            speeds.append(max(minimum, SMOOTH_OLD * speeds[-1] + SMOOTH_NEW * rng.uniform(minimum, 3.0)))
        for kind, bound in (('w', bound_w), ('w2', bound_w2)):
            lateral = rng.choice((114, 127, 141))
            if kind == 'w':
                origin, target, destination = slot_point(0, lateral), stand_point(), ENTRANCE
            else:
                origin, target, destination = slot_point(rng.choice((1, 2, 3)), rng.choice(LATERALS)), \
                    slot_point(0, lateral), FRONT
            start = rng.choice(list(standing_starts(origin, speeds[0])))
            velocity = rng.choice(list(stale_velocities(speeds[0])))
            updates = walk_varying(start, velocity, target, route_cells(cell_of(start), destination), speeds)
            require(updates is not None and updates <= bound, True, f'varying-speed walk {kind} seed {seed}')
            checked += 1
    return checked


def walk_varying(position, velocity, target, route, speeds, rounding='nearest'):
    """walk() with the speed caps reset every update (the setter runs before each state handler)."""
    p, v = position, velocity
    waypoints = [centre(cell) for cell in route]
    index, count = 0, len(waypoints)
    for update, speed in enumerate(speeds, 1):
        max_speed, max_force = speed_caps(speed)
        arrived = False
        if index == count - 1:
            goal = target
            arrived = octile(goal[0] - p[0], goal[1] - p[1]) < arrival_radius(rounding)
        else:
            if octile(waypoints[index][0] - p[0], waypoints[index][1] - p[1]) < waypoint_radius(rounding):
                index += 1
            goal = waypoints[index]
        offset = (goal[0] - p[0], goal[1] - p[1])
        if index < count - 1:
            path = (offset[0] - v[0], offset[1] - v[1])
        else:
            size = length(*offset) or 1
            half = fix_div(size, RAMP_DIVISOR, rounding)
            scale = fix_div(min(max_speed, half), size, rounding)
            path = (fix_mul(offset[0], scale, rounding) - v[0], fix_mul(offset[1], scale, rounding) - v[1])
        force = truncate(path, max_force, rounding)
        force = (fix_mul(force[0], WEIGHTS['follow_path'], rounding), fix_mul(force[1], WEIGHTS['follow_path'], rounding))
        v = truncate((v[0] + force[0], v[1] + force[1]), max_speed, rounding)
        p = (p[0] + v[0], p[1] + v[1])
        if arrived:
            return update
    return None


# ---------------------------------------------------------------- OpenTPW comparison and gate arithmetic

def implementation_walk_turns(distance_cells: float, speed_cells_s: float = 1.0, turn_ms: int = TURN_MS,
                              slow_factor: float = 0.7) -> int:
    """OpenTPW's walk (GuestSimulation.Speed, APPROX): straight line at the slowest speed plus one turn."""
    return math.ceil(distance_cells / (speed_cells_s * slow_factor * turn_ms / 1000)) + 1


def cells_per_second(max_speed: int, turn_ms: int = TURN_MS) -> float:
    """Top speed in cells per second at one steering update per park turn."""
    return max_speed / ONE * 1000 / turn_ms


def boarding_latency(walk_stand: int, walk_up: int, fixed: int = 21) -> int:
    """BOARD-plan 7.1: H = 21 + w + w2 (Belly Bounce, P = 4)."""
    return fixed + walk_stand + walk_up


def wait_bound(position: int, latency: int, capacity: int = 5, hold: int = 121) -> int:
    return (position + 1) * latency + (position // capacity + 1) * hold + 1


# ---------------------------------------------------------------- binary witness

def check_fields(c) -> int:
    for at, op, expected, meaning in FIELDS:
        require(d_fields(c, at, op), expected, f'{meaning} at code:{at:#x}')
    for at, *expected in RLWINM:
        require(queue.rlwinm_fields(c, at), tuple(expected), f'shift at code:{at:#x}')
    return len(FIELDS) + len(RLWINM)


def check_data(c) -> dict:
    data = bytes(c.data_section.data)
    for at, value in DATA_DOUBLES.items():
        require(struct.unpack_from('>d', data, at)[0], value, f'double at data:{at:#x}')
    for at, value in DATA_FLOATS.items():
        require(struct.unpack_from('>f', data, at)[0], value, f'float at data:{at:#x}')
    require(struct.unpack_from('>8H', data, SPEED_TABLE_AT), SPEED_TABLE, 'speed table data:0x41058')
    relocs = c.relocs.get(c.data_section.index, {})
    for slot, target in TOC_GLOBALS.items():
        entry = relocs.get(TOC + slot)
        require((entry.kind, entry.target, entry.addend) if entry else None, ('section', 1, target),
                f'TOC slot {slot}')
    names = relocs.get(TOC + NAME_BASE_SLOT)
    require((names.kind, names.target) if names else None, ('section', 0), 'string pool slot')
    for offset, name in NAMES.items():
        at = names.addend + offset
        require(bytes(c.code.data[at:at + len(name) + 1]), name.encode() + b'\0', f'behaviour name {name}')
    methods = {}
    registered = {relocs[BEHAVIOUR_OBJECTS + 4 * i].addend for i in range(7)}
    for name, (instance_slot, table_slot, table, method) in BEHAVIOURS.items():
        instance = relocs.get(TOC + instance_slot)
        require(instance is not None and instance.addend in registered, True, f'{name} registered object')
        vtable = relocs.get(TOC + table_slot)
        require((vtable.kind, vtable.target, vtable.addend) if vtable else None, ('section', 1, table), f'{name} vtable')
        vector = relocs.get(table + 8)
        require(vector is not None and vector.kind == 'section' and vector.target == 1, True, f'{name} slot +8')
        code = relocs.get(vector.addend)
        require((code.kind, code.target, code.addend) if code else None, ('section', 0, method), f'{name} method')
        methods[name] = hex(method)
    return methods


def check_code(c) -> dict:
    blocks = {}
    for name, (start, end, expected) in BLOCKS.items():
        digest = hashlib.sha256(c.code.data[start:end]).hexdigest()
        require(digest, expected, f'{name} bounded code digest')
        blocks[name] = {'code_start': start, 'code_end_exclusive': end, 'sha256': digest}
    witnesses = check_fields(c)
    for at, target in CALLS.items():
        require(call_target(c, at), target, f'call at code:{at:#x}')
    for at, glue in GLUE_CALLS.items():
        require(call_target(c, at), glue, f'glue call at code:{at:#x}')
    for glue, symbol in GLUE.items():
        require(glue_import(c, glue, TOC)['symbol'], symbol, f'glue {glue:#x}')
    methods = check_data(c)
    return {'blocks': blocks, 'field_witnesses': witnesses, 'call_witnesses': len(CALLS) + len(GLUE_CALLS),
            'behaviour_methods': methods}


def inspect(bin_root: Path) -> dict:
    c = load_identified(bin_root / 'SimThemePark.data')
    require((c.code.index, c.data_section.index), (0, 1), 'section indices')
    report = check_code(c)
    report['binary_sha256'] = hashlib.sha256(c.raw).hexdigest()
    report['witness_source'] = Path(__file__).resolve().relative_to(REPO).as_posix()
    report['plan'] = 'docs/reverse/WALK-plan.md'
    report['rules'] = {'speed_floor': SPEED_FLOOR, 'radius': RADIUS, 'arrival_radius': arrival_radius(),
                       'waypoint_radius': waypoint_radius(), 'weights': WEIGHTS, 'base_speeds': list(BASE_SPEEDS)}
    report['limitation'] = ('Static, bounded: separation formula and neighbour query, corner wall branch, route '
                            'search and the +196 boost sources are not interpreted; w and w2 come from the '
                            'restated rules by bounded enumeration.')
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--derive', action='store_true', help='run the full enumeration (minutes)')
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
    except (OSError, KeyError, ValueError, pef.PEFError) as error:
        parser.exit(1, f'walk evidence: {error}\n')
    if args.derive:
        speeds = [i / 100 for i in range(59, 201)]
        result['bounds'] = {'slot0_at_entrance_edge': derive_bounds(speeds),
                            'slot0_at_far_edge': derive_bounds(speeds, reversed_axis=True),
                            'below_0.59': derive_bounds([0.0] + [i / 100 for i in range(5, 59)],
                                                        variants=(('oppose', True, 'nearest'),
                                                                  ('off', True, 'nearest')))}
        result['varying_speed_walks'] = varying_speed_check(200, 20, 15, speed_lower_bound(15))
    print(json.dumps(result, sort_keys=True, indent=2))


if __name__ == '__main__':
    main()
