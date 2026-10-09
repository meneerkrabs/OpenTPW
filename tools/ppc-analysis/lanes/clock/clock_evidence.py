"""Bounded, identity-pinned static clock witnesses; never executes original code.

Only selected interpreted operands, relocations, constants and addresses leave
this process. Original bytes and raw disassembly are never emitted or saved.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef
import timer_evidence as timer


def word_at(code: bytes, offset: int) -> int:
    if offset < 0 or offset % 4 or offset + 4 > len(code):
        raise pef.PEFError(f'invalid aligned code offset {offset:#x}')
    return struct.unpack_from('>I', code, offset)[0]


def d_operand(code: bytes, offset: int, opcode: int) -> tuple[int, int, int]:
    word = word_at(code, offset)
    timer.require(word >> 26, opcode, f'operation at {offset:#x}')
    immediate = word & 0xffff
    if immediate & 0x8000:
        immediate -= 0x10000
    return (word >> 21 & 31, word >> 16 & 31, immediate)


def branch_target(code: bytes, offset: int, linked: bool = False) -> int:
    word = word_at(code, offset)
    opcode = word >> 26
    if opcode not in (16, 18):
        raise pef.PEFError(f'not an immediate branch at {offset:#x}')
    timer.require(bool(word & 1), linked, f'branch link at {offset:#x}')
    mask, sign = (0xfffc, 0x8000) if opcode == 16 else (0x3fffffc, 0x2000000)
    delta = word & mask
    if delta & sign:
        delta -= sign * 2
    return delta if word & 2 else offset + delta


def inspect(root: Path) -> dict:
    app = timer.load_identified(root / 'SimThemePark.data')
    sams = timer.load_identified(root / 'libraries/sams_utils_shared.data')
    records = []

    def d(c, at, op, expected, meaning):
        value = d_operand(c.code.data, at, op)
        timer.require(value, expected, meaning)
        records.append({'binary': c.name, 'at': hex(at), 'meaning': meaning,
                        'operands': list(value)})

    def call(c, at, target, meaning):
        actual = branch_target(c.code.data, at, True)
        timer.require(actual, target, meaning)
        records.append({'binary': c.name, 'at': hex(at), 'meaning': meaning,
                        'target': hex(actual)})

    for at, op, value, meaning in [
        (0x4448, 14, (3, 27, 92), 'application guard selects timer +0x5c'),
        (0x4470, 32, (0, 27, 112), 'guard threshold +0x70'),
        (0x44b0, 32, (12, 12, 52), 'guard resource query virtual offset 52'),
        (0x4514, 32, (0, 27, 120), 'guard threshold +0x78'),
        (0x452c, 32, (0, 27, 116), 'guard combined threshold +0x74'),
        (0x2cfc, 32, (12, 12, 28), 'game callback is independent of guard tick'),
    ]: d(sams, at, op, value, meaning)
    call(sams, 0x445c, 0xa698, 'guard consumes HasTicked')
    call(sams, 0x2ca8, 0x4434, 'UGamework Run calls guard')
    call(sams, 0x44a0, 0x60, 'guard constructs allocator query object')
    call(sams, 0x4594, 0xa28, 'guard constructs temporary allocator query object')
    d(sams, 0x68, 14, (0, 2, 13252), 'allocator vtable location')
    for slot, symbol in [(0x33f8, 'GetAvailable__Q213SamsUtilities10UAllocatorFv'),
                         (0x33fc, 'GetLargest__Q213SamsUtilities10UAllocatorFv')]:
        rel = sams.relocs[1][slot]
        vec = timer.vector(sams, symbol)
        timer.require((rel.kind, rel.target, rel.addend),
                      ('section', 1, vec['vector_offset']), 'allocator query vtable binding')
    for at, op, value, meaning in [
        (0x1c22e0, 14, (0, 3, 31), 'scheduler advances last time by 31 milliseconds'),
        (0x1c22ec, 14, (0, 3, 1), 'scheduler increments substep counter'),
        (0x1c2354, 10, (0, 3, 3), 'per-callback turn work cap comparison'),
        (0x10539c, 14, (0, 3, 1), 'park turn increment'),
        (0x1053a0, 36, (0, 4, -22772), 'park turn store at object+0x1da70c'),
        (0x10ec90, 14, (0, 0, 1000), 'forced clock numerator'),
        (0x1c2298, 14, (4, 0, 32), 'forced clock requested rate'),
        (0x10ed74, 32, (3, 31, 48), 'forced clock returns +48 instead of elapsed source'),
        (0x10e868, 14, (3, 3, 68), 'advisor clock selects embedded clock +68'),
        (0x117c14, 32, (0, 3, 20), 'unscaled elapsed clock freeze flag'),
        (0x117c34, 32, (3, 31, 16), 'frozen elapsed clock subtracts pause accumulator'),
        (0x10edd0, 32, (4, 31, 0), 'unscaled source previous millisecond reading'),
        (0xaf568, 42, (0, 3, 192), 'script signed speed-bias field'),
        (0xb3154, 14, (9, 0, 50), 'script speed bias initial value is separate from header budget'),
        (0xb318c, 44, (9, 31, 192), 'script speed bias initialized to 50'),
        (0xb2de0, 14, (3, 31, 148), 'loader reads fifth header word into slice budget'),
        (0xaf5d4, 14, (0, 0, 1), 'CRIT_LOCK sets global critical flag'),
        (0xaf5e0, 14, (0, 0, 0), 'CRIT_UNLOCK clears global critical flag'),
        (0xaf5e8, 36, (0, 31, 152), 'CRIT_UNLOCK also yields'),
        (0xb0e50, 36, (0, 31, 152), 'ENDSLICE clears remaining budget'),
        (0xb28e0, 32, (0, 24, 148), 'manager reloads header instruction budget'),
        (0xb28e4, 36, (0, 24, 152), 'manager initializes remaining budget'),
        (0xb2904, 14, (0, 3, -1), 'manager decrements remaining budget outside critical section'),
        (0xb06f8, 36, (3, 31, 160), 'WAIT stores absolute deadline'),
        (0xb0700, 14, (3, 3, -2), 'WAIT rewinds its opcode and operand while sleeping'),
        (0xb0708, 36, (0, 31, 152), 'WAIT yields by clearing remaining budget'),
        (0xb0788, 36, (3, 31, 160), 'WAITABS also adds operand to now'),
        (0xb1ff4, 36, (0, 31, 196), 'SETTIMER stores now plus requested duration'),
        (0xa6fd4, 52, (0, 31, 16404), 'animation delta seconds destination'),
        (0xa6fdc, 36, (3, 31, 16408), 'animation alternate clock is script/advisor milliseconds'),
        (0xa707c, 32, (4, 6, 16408), 'channel flag selects alternate clock'),
        (0xa7084, 32, (4, 6, 16400), 'default animation clock is scheduler milliseconds'),
        (0xa7044, 48, (4, 2, -11924), 'animation frame-rate constant TOC load'),
        (0xa704c, 48, (2, 2, -11920), 'animation milliseconds divisor TOC load'),
        (0xa7098, 32, (5, 7, 16), 'animation channel start timestamp'),
        (0xa70a0, 48, (1, 7, 12), 'per-channel speed multiplier'),
        (0xa70c4, 52, (0, 7, 32), 'animation frame advancement destination'),
        (0xa7ed4, 7, (0, 0, 1000), 'animation duration query milliseconds numerator'),
    ]:
        if value is not None: d(app, at, op, value, meaning)
    for at, target, meaning in [
        (0x6d8, 0x1c1208, 'ThemeParkWorld game callback runs scheduler'),
        (0x1c2314, 0xb2838, 'one script manager pass per active scheduler substep'),
        (0x1c23b8, 0x10536c, 'mode branch directly advances park turn'),
        (0x1c23ec, 0x10565c, 'other mode branch invokes park turn wrapper'),
        (0x1056ac, 0x10536c, 'wrapper advances park turn when mode equals one'),
        (0x10e850, 0x11a588, 'scheduler clock getter'),
        (0x11a59c, 0x10ed54, 'scheduler clock elapsed or forced selection'),
        (0x10ed80, 0x117d74, 'scheduler uses scaled pause-aware clock'),
        (0x117d98, 0x127cd0, 'scaled source consumer'),
        (0x10e874, 0x11a428, 'script/advisor clock getter'),
        (0x11a43c, 0x117c00, 'script/advisor pause-aware unscaled elapsed'),
        (0x117c24, 0x10edb4, 'unscaled accumulator source'),
        (0x10edc8, 0x1c4d64, 'unscaled accumulator imports LbTime_GetClock'),
        (0xaf6d8, 0x10e844, 'RSE GETTIME uses scaled scheduler clock'),
        (0xb06c4, 0x10e844, 'RSE WAIT samples scaled scheduler clock'),
        (0xb28f0, 0xaf534, 'manager dispatches one opcode per budget iteration'),
        (0xa6fa0, 0x10e844, 'animation default clock samples scheduler clock'),
        (0xa6fd8, 0x10e864, 'animation alternate clock samples script/advisor clock'),
    ]: call(app, at, target, meaning)
    arithmetic = {}
    for at, expected, meaning in [
        (0xaf58c, (59, 0, 0, 30, 18), 'speed-bias division by 100'),
        (0xaf590, (59, 31, 1, 0, 21), 'speed-bias addition of 0.5'),
        (0xb06e4, (59, 0, 0, 31, 18), 'WAIT divides by speed factor'),
        (0xb06e8, (63, 0, 0, 0, 15), 'WAIT truncates scaled duration'),
        (0xb06f4, (31, 3, 4, 3, 266), 'WAIT deadline adds duration to now'),
        (0xb0780, (31, 3, 27, 3, 266), 'WAITABS deadline also adds operand to now'),
        (0xb2014, (31, 0, 3, 0, 40), 'GETTIMER subtracts now from deadline'),
        (0xa6fd0, (59, 0, 1, 0, 18), 'animation delta converts milliseconds to seconds'),
        (0xa70b8, (59, 0, 4, 0, 25), 'animation frame formula multiplies by 30'),
        (0xa70bc, (59, 0, 0, 2, 18), 'animation frame formula divides by 1000'),
        (0xa70c0, (59, 0, 1, 0, 25), 'animation frame formula multiplies by channel speed'),
    ]:
        word = word_at(app.code.data, at)
        primary = word >> 26
        fields = (primary, word >> 21 & 31, word >> 16 & 31, word >> 11 & 31,
                  word >> 1 & (31 if primary == 59 else 1023))
        timer.require(fields, expected, meaning)
        arithmetic[hex(at)] = {'meaning': meaning, 'fields': list(fields)}
    for at, target in [(0xb28fc, 0xb290c), (0xb0680, 0xb0690),
                       (0x1c24cc, 0x1c22dc)]:
        timer.require(branch_target(app.code.data, at), target, f'control edge at {at:#x}')
    constants = {}
    for offset, expected in [(0x516c, 30.0), (0x5170, 1000.0), (0x52b0, 100.0),
                              (0x52d0, 0.5), (0x5958, 31.0), (0x5954, 62.0), (0x5950, 248.0)]:
        value = struct.unpack_from('>f', app.data_section.data, offset)[0]
        timer.require(value, expected, f'constant at data:{offset:#x}')
        constants[hex(offset)] = value
    timer.require(timer.glue_import(app, 0x1c4d64, 0x8000)['symbol'],
                  'LbTime_GetClock__Fv', 'unscaled millisecond clock import')
    table = app.relocs[1][0x2f58]
    timer.require((table.kind, table.target, table.addend), ('section', 1, 0x3f2a4), 'RSE jump table')
    dispatch = {}
    for opcode, target in [(6, 0xb0e4c), (7, 0xaf6c8), (44, 0xb0658), (45, 0xb0710),
                            (95, 0xb1fd0), (96, 0xb1ffc)]:
        relocation = app.relocs[1][table.addend + opcode * 4]
        timer.require((relocation.kind, relocation.target, relocation.addend),
                      ('section', 0, target), f'opcode {opcode} dispatch')
        dispatch[str(opcode)] = hex(target)
    # Rotate/mask controls are interpreted separately from D-form operands.
    masks = {}
    for at, expected in [(0x1c233c, (0, 0, 0, 29, 31)), (0x1c231c, (0, 0, 0, 31, 31))]:
        word = word_at(app.code.data, at)
        timer.require(word >> 26, 21, 'scheduler counter mask')
        fields = (word >> 21 & 31, word >> 16 & 31, word >> 11 & 31,
                  word >> 6 & 31, word >> 1 & 31)
        timer.require(fields, expected, f'mask at {at:#x}')
        masks[hex(at)] = list(fields)
    return {'identities': {key: value for key, value in timer.IDENTITIES.items()
                            if key in ('SimThemePark.data', 'sams_utils_shared.data')},
            'app_toc': '0x8000', 'sams_toc': '0x0', 'witnesses': records,
            'app_float_constants': constants, 'rse_dispatch': dispatch, 'counter_masks': masks,
            'arithmetic': arithmetic,
            'semantic_findings': {
                'application_100000_interval_consumer': 'resource threshold guard, not game cadence',
                'scheduler_step_ms': 31, 'park_turn_every_substeps': 8,
                'nominal_park_turn_ms_of_scaled_clock': 248,
                'animation_frames_per_second_of_selected_clock': 30,
                'animation_formula': '(now_ms-start_ms)*30/1000*channel_speed',
                'script_speed_formula': '0.5 + signed_field_192/100',
                'wait_formula': 'now_ms + trunc(operand/script_speed)',
                'waitabs_formula': 'now_ms + operand (no absolute operand or speed division)',
                'script_budget_field': 148, 'header_budget_file_offset': 16,
                'gettime_source': 'shared scaled scheduler milliseconds',
                'normal_script_schedule': 'ID low three bits must match manager pass low three bits',
                'script_speed_initial_field_192': 50},
            'limitations': ['Static Feral Mac evidence does not prove Windows Patch 2 behavior.',
                            'Counter phase, pause, scale and catch-up limits affect observed cadence.',
                            'No original code execution or audio/playback trace was performed.']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.bin_root), indent=2, sort_keys=True))
    except (OSError, pef.PEFError) as error:
        parser.exit(1, f'clock evidence: {error}\n')


if __name__ == '__main__':
    main()
