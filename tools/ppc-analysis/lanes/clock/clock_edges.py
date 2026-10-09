"""Pinned clock edge metadata plus bounded analytical models, not a game emulator.

Models express only the verified arithmetic/control slice. They execute no
original instructions and make no claims about untraced platform FP traps.
"""
from __future__ import annotations
import argparse
import json
import math
from pathlib import Path
import struct

import clock_evidence as evidence
import timer_evidence as timer
import pef

MASK = (1 << 32) - 1


def signed32(value: int) -> int:
    value &= MASK
    return value - (1 << 32) if value & (1 << 31) else value


def clock_uint32(value: float) -> int:
    """Finite-input result of application conversion helper code:0x1c3fbc."""
    if not math.isfinite(value):
        raise ValueError('model excludes NaN/infinity and FP exception modes')
    if value < 0:
        return 0
    if value >= 1 << 32:
        return MASK
    return math.trunc(value)


def accumulate(previous: int, current: int, total: float, scale: float = 1.0):
    delta = (current - previous) & MASK
    total += delta * scale
    return total, clock_uint32(total)


def catchup_prefix(previous: int, now: int, phase: int = 0, world_allowed: bool = True,
                   limit: int = 100) -> dict:
    """Bounded scheduler prefix for an advancing mode-2 world, empty work cap.

    No loop continuation beyond ``limit`` is executed. Signed comparisons and
    unsigned word wrap intentionally differ, matching the pinned control slice.
    The caller must not generalize the model to other startup/network branches.
    """
    if limit < 0:
        raise ValueError('negative prefix limit')
    previous &= MASK
    now &= MASK
    backlog_dropped = 0
    delta = signed32(now - previous)
    if delta > 2000:
        backlog_dropped = delta - 2000
        previous = (now - 2000) & MASK
    steps = turns = missed_turn_phases = script_passes = 0
    while signed32(now) > signed32(previous) and steps < limit:
        previous = (previous + 31) & MASK
        phase = (phase + 1) & MASK
        steps += 1
        if world_allowed:
            script_passes += 1
            if phase & 7 == 0:
                if turns < 3:
                    turns += 1
                else:
                    missed_turn_phases += 1
    return {'steps': steps, 'script_passes': script_passes, 'turns': turns,
            'missed_turn_phases': missed_turn_phases, 'previous': previous,
            'phase': phase, 'backlog_dropped_ms': backlog_dropped,
            'still_comparing_now_greater': signed32(now) > signed32(previous)}


def ordinary_script_passes(script_id: int, start_pass: int, count: int):
    """Manager increments its own counter before comparing ID phase."""
    if count < 0:
        raise ValueError('negative pass count')
    return [index for index in range(1, count + 1)
            if ((start_pass + index) & 7) == (script_id & 7)]


def inspect(root: Path) -> dict:
    app = timer.load_identified(root / 'SimThemePark.data')
    records = []
    def d(at, opcode, expected, label):
        timer.require(evidence.d_operand(app.code.data, at, opcode), expected, label)
        records.append({'at': hex(at), 'meaning': label, 'operands': list(expected)})
    def call(at, target, label):
        timer.require(evidence.branch_target(app.code.data, at, True), target, label)
        records.append({'at': hex(at), 'meaning': label, 'target': hex(target)})
    for at, op, expected, label in [
        (0x4b6b0, 15, (3, 0, 12), 'startup flags high literal'),
        (0x4b6cc, 14, (0, 3, 3605), 'startup flags become 0x000c0e15'),
        (0x4b6d0, 36, (0, 5, 0), 'startup default flags stored'),
        (0x4b6e8, 36, (0, 5, 0), 'loaded startup configuration may override flags'),
        (0x12bb94, 14, (4, 0, 2), 'mode flag bit25 selects mode2'),
        (0x12bbac, 14, (4, 0, 1), 'mode flag bit24 selects mode1'),
        (0x12bbd4, 14, (4, 0, 0), 'no recognized mode flag selects mode0'),
        (0x971f4, 14, (4, 0, 1), 'OnlineIslands action selects mode1'),
        (0x1c2a94, 14, (4, 0, 2), 'successful transition selects mode2'),
        (0x1c2ac0, 14, (4, 0, 0), 'failed transition selects mode0'),
        (0x1c2384, 11, (0, 0, 0), 'mode0 skips turn advance'),
        (0x1c23ac, 11, (0, 0, 2), 'mode2 calls direct turn advance'),
        (0x1c23e0, 11, (0, 0, 1), 'mode1 calls wrapper turn advance'),
        (0x1c27a4, 14, (0, 0, 0), 'normal callback completion clears turn work cap'),
        (0x1c27a8, 36, (0, 18, 0), 'normal callback completion work count reset'),
        (0x1c22c8, 11, (0, 0, 2000), 'backlog limit compares signed delta'),
        (0x1c22d0, 14, (0, 3, -2000), 'excess backlog drops previous scheduled time'),
        (0x1c24c8, 31, None, 'signed now versus previous comparison'),
        (0x110538, 11, (0, 0, 1), 'pause entry requires object state1'),
        (0x110544, 11, (0, 4, 1), 'pause flag controls optional action'),
        (0x110548, 36, (0, 30, 28), 'pause stores state1 at object+28'),
        (0x1105c0, 36, (0, 31, 28), 'resume clears pause state at object+28'),
        (0x10e8a0, 14, (3, 31, 68), 'freeze also selects unscaled clock'),
        (0x10e8d4, 14, (3, 31, 68), 'resume also selects unscaled clock'),
        (0x1c3fc0, 14, (3, 0, 0), 'unsigned conversion negative fallback0'),
        (0x1c3fdc, 14, (3, 3, -1), 'unsigned conversion positive fallback0xffffffff'),
        (0x1c4004, 15, (3, 3, -32768), 'unsigned conversion reconstructs high half'),
        (0xb2888, 14, (0, 3, 1), 'script phase counter increments before matching'),
        (0xb28a8, 34, (0, 24, 184), 'script flag bypasses ID phase filter'),
        (0xb068c, 18, None, 'WAIT deadline satisfied returns from one opcode'),
        (0xb0700, 14, (3, 3, -2), 'WAIT initial encounter always rewinds'),
        (0xb0708, 36, (0, 31, 152), 'WAIT initial encounter always yields'),
        (0xb0790, 14, (3, 3, -2), 'WAITABS initial encounter always rewinds'),
        (0xb0798, 36, (0, 31, 152), 'WAITABS initial encounter always yields'),
    ]:
        if expected is not None: d(at, op, expected, label)
    for at, target, label in [
        (0x971fc, 0x12bbf4, 'OnlineIslands mode setter'),
        (0x1c2a9c, 0x12bbf4, 'successful transition mode setter'),
        (0x1c2ac8, 0x12bbf4, 'failed transition mode setter'),
        (0x1128a0, 0x110604, 'pause action toggles high level state'),
        (0x10e904, 0x117cf4, 'pause toggle toggles scaled clock'),
        (0x10e90c, 0x117b88, 'pause toggle toggles unscaled clock'),
        (0x11055c, 0x10e888, 'pause freezes both clocks'),
        (0x1105cc, 0x10e8bc, 'resume resumes both clocks'),
        (0x110638, 0x10e8f0, 'pause toggle toggles both clocks'),
        (0x10e89c, 0x117c54, 'scaled clock freeze'),
        (0x10e8a4, 0x117ae8, 'unscaled clock freeze'),
        (0x10e8d0, 0x117c9c, 'scaled clock resume'),
        (0x10e8d8, 0x117b30, 'unscaled clock resume'),
        (0x11316c, 0x127c88, 'slow action divides scale by1.25'),
        (0x113194, 0x127c48, 'fast action multiplies scale by1.25'),
        (0x10ee04, 0x1c3fbc, 'unscaled accumulator uses unsigned saturating conversion'),
        (0x127d24, 0x1c3fbc, 'scaled accumulator uses unsigned saturating conversion'),
    ]: call(at, target, label)
    control = {}
    for at, target in [(0x1c22fc, 0x1c230c), (0x1c2308, 0x1c24c0),
                       (0x1c2358, 0x1c2424), (0x1c22cc, 0x1c24c0),
                       (0xb28c8, 0xb2a38), (0xb28b0, 0xb28cc)]:
        timer.require(evidence.branch_target(app.code.data, at), target, 'guard control edge')
        control[hex(at)] = hex(target)
    branch_conditions = {}
    for at, expected in [(0x1c22fc, (4, 2)), (0x1c2308, (4, 2)),
                         (0x1c2358, (4, 0)), (0x1c22cc, (4, 1)),
                         (0x1c24cc, (12, 1)), (0xb28c8, (4, 2))]:
        word = evidence.word_at(app.code.data, at)
        timer.require(word >> 26, 16, 'conditional branch operation')
        fields = (word >> 21 & 31, word >> 16 & 31)
        timer.require(fields, expected, 'exact conditional branch BO/BI')
        branch_conditions[hex(at)] = list(fields)
    masks = {}
    for at, expected in [(0x12bb88, (3, 0, 0, 6, 6)),
                         (0x12bba0, (3, 0, 0, 7, 7)),
                         (0x1c22f8, (0, 0, 0, 28, 28)),
                         (0x1c2304, (0, 0, 0, 31, 31)),
                         (0xb28bc, (3, 3, 0, 29, 31)),
                         (0xb28c0, (0, 0, 0, 29, 31))]:
        word = evidence.word_at(app.code.data, at)
        timer.require(word >> 26, 21, 'rotate/mask operation')
        fields = (word >> 21 & 31, word >> 16 & 31, word >> 11 & 31,
                  word >> 6 & 31, word >> 1 & 31)
        timer.require(fields, expected, 'mode/world/script mask')
        masks[hex(at)] = list(fields)
    for at, fields in [(0x1c24c8, (31, 0, 3, 0, 0)),
                       (0x10eddc, (31, 4, 4, 3, 40)),
                       (0x127cf8, (31, 4, 4, 3, 40))]:
        word = evidence.word_at(app.code.data, at)
        actual = (word >> 26, word >> 21 & 31, word >> 16 & 31,
                  word >> 11 & 31, word >> 1 & 1023)
        timer.require(actual, fields, 'comparison or source delta operation')
    controls = {}
    for base, expected, symbol_target in [
        (0x4515c, (0, 0x50, 0), 0x11288c),
        (0x452c4, (6, 0x6d00, 0), 0x11315c),
        (0x452d8, (7, 0x6b00, 0), 0x113184),
    ]:
        fields = struct.unpack_from('>hhh', app.data_section.data, base)
        timer.require(fields, expected, 'key binding action/key/modifier fields')
        ptr = app.relocs[1][base + 12]
        timer.require((ptr.kind, ptr.target), ('section', 1), 'release-side action pointer')
        vec = app.relocs[1][ptr.addend]
        timer.require((vec.kind, vec.target, vec.addend), ('section', 0, symbol_target),
                      'key binding callback code')
        controls[hex(base)] = {'action_id': fields[0], 'key_token': hex(fields[1]),
                              'modifiers': fields[2], 'vector': hex(ptr.addend),
                              'callback': hex(vec.addend)}
    d(0x114dd0, 14, (6, 0, 15), 'game key group record count')
    d(0x114d8c, 14, (6, 0, 12), 'system key group record count')
    d(0x114bf0, 42, (0, 10, 2), 'release key-token match')
    d(0x114bfc, 42, (0, 10, 4), 'release modifier match')
    d(0x114c3c, 32, (12, 4, 12), 'release action pointer selection')
    d(0x114c5c, 44, (5, 4, 6), 'release clears held state')
    constants = {}
    for at, expected in [(0x52e24, 0.0), (0x52e2c, 4294967296.0),
                         (0x52e34, 2147483648.0), (0x5710, 1.0),
                         (0x5708, 1.25), (0x5700, 0.25), (0x56f8, 2.0)]:
        value = struct.unpack_from('>d', app.data_section.data, at)[0]
        timer.require(value, expected, 'clock conversion or scale constant')
        constants[hex(at)] = value
    return {'identity': timer.IDENTITIES['SimThemePark.data'], 'toc': '0x8000',
            'witnesses': records, 'control_edges': control, 'branch_conditions': branch_conditions, 'masks': masks,
            'double_constants': constants, 'key_controls': controls,
            'examples': {'one_millisecond_backlog': catchup_prefix(100, 101),
                         'two_second_backlog': catchup_prefix(0, 2000),
                         'five_second_backlog': catchup_prefix(0, 5000),
                         'excluded_world': catchup_prefix(0, 2000, world_allowed=False),
                         'signed_boundary_prefix': catchup_prefix(0x7ffffffe, 0x7fffffff, limit=2),
                         'ordinary_id1_passes': ordinary_script_passes(1, 0, 16)},
            'limitations': ['Models are bounded mathematical slices, not original code execution.',
                            'Mode2 is an exact numeric branch; complete user mode naming remains separate.',
                            'NaN, FP traps, unusual direct scale/bias setters and Windows behavior remain unqualified.']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.bin_root), indent=2, sort_keys=True))
    except (OSError, pef.PEFError) as error:
        parser.exit(1, f'clock edges: {error}\n')


if __name__ == '__main__':
    main()
