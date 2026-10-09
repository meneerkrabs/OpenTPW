"""Identity-pinned consumer dependencies and bounded word arithmetic.

Emits addresses, interpreted operands and hashes only. This does not execute
native code or replace the live engine's clocks. Nonfinite animation arithmetic
and floating-point exceptions are outside the mathematical examples.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct

import clock_evidence as evidence
import clock_edges as edges
import pef
import timer_evidence as timer


def timer_remaining(deadline: int, now: int) -> int:
    """GETTIMER uses a wrapped word subtraction followed by a signed zero clamp."""
    return max(0, edges.signed32(deadline - now))


def manager_phase(previous: int, initialized: bool) -> int:
    """Initialization state gates the increment; an empty initialized list does not."""
    return (previous + int(initialized)) & edges.MASK


def animation_frame(now: int, start: int, speed: float) -> float:
    """Unsigned elapsed word, then separately rounded single-precision operations."""
    def f32(value: float) -> float:
        if not math.isfinite(value):
            raise ValueError('nonfinite animation arithmetic is outside the model')
        try:
            return struct.unpack('>f', struct.pack('>f', value))[0]
        except OverflowError as error:
            raise ValueError('single-precision overflow is outside the model') from error
    elapsed = f32((now - start) & edges.MASK)
    return f32(f32(f32(elapsed * 30.0) / 1000.0) * f32(speed))


def inspect(root: Path) -> dict:
    app = timer.load_identified(root / 'SimThemePark.data')
    records = []

    def d(at, op, expected, meaning):
        timer.require(evidence.d_operand(app.code.data, at, op), expected, meaning)
        records.append({'at': hex(at), 'meaning': meaning, 'operands': list(expected)})

    def call(at, target, meaning):
        timer.require(evidence.branch_target(app.code.data, at, True), target, meaning)
        records.append({'at': hex(at), 'meaning': meaning, 'target': hex(target)})

    def condition(at, expected, meaning):
        word = evidence.word_at(app.code.data, at)
        timer.require(word >> 26, 16, meaning)
        fields = (word >> 21 & 31, word >> 16 & 31,
                  evidence.branch_target(app.code.data, at))
        timer.require(fields, expected, meaning)
        records.append({'at': hex(at), 'meaning': meaning,
                        'bo': fields[0], 'bi': fields[1], 'target': hex(fields[2])})

    for at, op, expected, meaning in [
        (0x1c1358, 32, (0, 21, 0), 'main callback state word'),
        (0x1c135c, 10, (0, 0, 15), 'main callback state dispatch maximum'),
        (0x1c1364, 32, (3, 2, -14480), 'main callback dispatch TOC slot'),
        (0x1c1378, 14, (0, 0, 5), 'callback state4 selects state5'),
        (0x1c137c, 36, (0, 21, 0), 'callback state4 stores state5'),
        (0x105398, 32, (3, 4, -22772), 'world counter load'),
        (0x10539c, 14, (0, 3, 1), 'world counter increments before state4 check'),
        (0x1053a0, 36, (0, 4, -22772), 'world counter store'),
        (0x1053a4, 32, (0, 4, -22728), 'world state field +0x1da738'),
        (0x1053a8, 11, (0, 0, 4), 'world state4 comparison'),
        (0xd6814, 14, (3, 31, 672), 'calendar member is player+672'),
        (0xb27a0, 14, (4, 0, 1), 'manager initialization value1'),
        (0xb27a4, 14, (0, 0, 0), 'manager reset value0'),
        (0xb27a8, 36, (4, 5, 0), 'manager initialized state+0 set'),
        (0xb27b8, 36, (0, 5, 16), 'manager initialized with an empty list+16'),
        (0xb27c0, 36, (0, 5, 4), 'manager phase+4 initialized zero'),
        (0xb2870, 32, (0, 4, 0), 'manager tests initialization state'),
        (0xb2874, 11, (0, 0, 0), 'manager initialization zero comparison'),
        (0xb2884, 32, (3, 4, 4), 'manager phase load after initialization guard'),
        (0xb2888, 14, (0, 3, 1), 'manager phase increments before list traversal'),
        (0xb288c, 36, (0, 4, 4), 'manager phase store'),
        (0xb2890, 32, (24, 4, 16), 'manager reads list after phase increment'),
        (0xb2b60, 14, (0, 0, 0), 'manager shutdown zero literal'),
        (0xb2b68, 36, (0, 31, 0), 'manager shutdown clears initialized state'),
        (0xb1ff4, 36, (0, 31, 196), 'SETTIMER deadline word destination'),
        (0xb2010, 32, (0, 31, 196), 'GETTIMER deadline word load'),
        (0xb2018, 36, (0, 31, 72), 'GETTIMER accumulator raw difference'),
        (0xb2020, 11, (0, 0, 0), 'GETTIMER signed difference comparison'),
        (0xb2028, 14, (0, 0, 0), 'GETTIMER negative difference becomes zero'),
        (0xb202c, 36, (0, 31, 72), 'GETTIMER negative difference clamp store'),
        (0x11b53c, 32, (4, 2, -26524), 'lifecycle reset request TOC slot0x1864'),
        (0x11b540, 14, (0, 0, 1), 'lifecycle requests cadence reset'),
        (0x11b548, 36, (0, 4, 0), 'lifecycle reset request store'),
        (0x1c227c, 14, (0, 0, 0), 'scheduler clears reset request literal'),
        (0x1c2280, 36, (0, 3, 0), 'scheduler clears reset request store'),
        (0x1c3540, 36, (3, 4, 0), 'scheduler reset stores current clock as previous'),
        (0x1c3560, 14, (0, 0, 0), 'scheduler reset phase literal0'),
        (0x1c3570, 36, (0, 4, 0), 'scheduler reset clears substep phase'),
    ]:
        d(at, op, expected, meaning)
    for at, target, meaning in [
        (0x10563c, 0xd67f0, 'world common tail calls player update after state4 guard'),
        (0xd6818, 0xe3f0c, 'player common update calls calendar update'),
        (0xb1fec, 0x10e844, 'SETTIMER samples shared scheduler clock'),
        (0xb200c, 0x10e844, 'GETTIMER samples shared scheduler clock'),
        (0x1c2274, 0x1c3520, 'callback state10 invokes optional scheduler reset'),
        (0x1c3538, 0x10e844, 'scheduler reset samples shared scheduler clock'),
    ]:
        call(at, target, meaning)
    for at, expected, meaning in [
        (0x1053ac, (12, 2, 0x105470), 'world state4 skips things after increment'),
        (0xb2878, (12, 2, 0xb2a74), 'uninitialized manager bypasses phase increment'),
        (0xb2024, (4, 0, 0xb2030), 'GETTIMER nonnegative signed difference skips clamp'),
        (0x1c2270, (12, 2, 0x1c2284), 'zero reset request skips scheduler reset'),
    ]:
        condition(at, expected, meaning)
    arithmetic = {}
    for at, expected, meaning in [
        (0xb1ff0, (31, 0, 27, 3, 266), 'SETTIMER wrapped word addition'),
        (0xb2014, (31, 0, 3, 0, 40), 'GETTIMER wrapped word subtraction'),
        (0xa70a4, (31, 4, 5, 4, 40), 'animation elapsed word subtraction'),
    ]:
        word = evidence.word_at(app.code.data, at)
        fields = (word >> 26, word >> 21 & 31, word >> 16 & 31,
                  word >> 11 & 31, word >> 1 & 1023)
        timer.require(fields, expected, meaning)
        arithmetic[hex(at)] = {'meaning': meaning, 'fields': list(fields)}
    d(0xa7040, 15, (0, 0, 17200), 'unsigned word-to-float construction high half0x4330')
    d(0xa7048, 50, (3, 2, -11864), 'animation unsigned conversion bias TOC load')
    d(0xa70ac, 36, (0, 1, -16), 'animation conversion high word without signed xor')
    bias = struct.unpack_from('>d', app.data_section.data, 0x51a8)[0]
    timer.require(bias, float(1 << 52), 'animation unsigned conversion bias')
    table = app.relocs[1][0x4770]
    timer.require((table.kind, table.target, table.addend), ('section', 1, 0x52cc8),
                  'callback state dispatch table relocation')
    dispatch = {}
    for state, target in [(4, 0x1c1378), (10, 0x1c2264)]:
        rel = app.relocs[1][table.addend + state * 4]
        timer.require((rel.kind, rel.target, rel.addend), ('section', 0, target),
                      'callback state dispatch code target')
        dispatch[str(state)] = hex(target)
    # These RSE opcodes use a different time domain from the virtual park date.
    date_dispatch = app.relocs[1][0x2f58]
    wall_fields = {}
    for opcode, start, time_at, localtime_at, load_at, expected, field in [
        (97, 0xb2144, 0xb2148, 0xb2154, 0xb215c, (0, 3, 20), 'tm_year'),
        (98, 0xb2194, 0xb2198, 0xb21a4, 0xb21ac, (4, 3, 16), 'tm_mon+1'),
        (99, 0xb21e8, 0xb21ec, 0xb21f8, 0xb2200, (0, 3, 12), 'tm_mday'),
        (100, 0xb2238, 0xb223c, 0xb2248, 0xb2250, (0, 3, 8), 'tm_hour'),
    ]:
        rel = app.relocs[1][date_dispatch.addend + opcode * 4]
        timer.require((rel.kind, rel.target, rel.addend), ('section', 0, start),
                      'RSE wall-date opcode dispatch')
        for at, target, name in [(time_at, 0x1c6474, 'time'),
                                 (localtime_at, 0x1c648c, 'localtime')]:
            call(at, target, 'RSE host civil time import call')
            imported = timer.glue_import(app, target, 0x8000)
            timer.require((imported['library'], imported['symbol']), ('c/c++ shared', name),
                          'RSE host time import identity')
        d(load_at, 32, expected, 'RSE host civil field load')
        wall_fields[str(opcode)] = {'dispatch': hex(start), 'field': field}
    d(0xb21b4, 14, (0, 4, 1), 'RSE MONTH is one-based')
    clib_path = root / 'libraries/c_c++_shared.data'
    clib_bytes = clib_path.read_bytes()
    clib_identity = '5e04f9c00c922dc78a787d1b93067c75d37a3e65b0a0202e50c2f449f131b27f'
    timer.require(hashlib.sha256(clib_bytes).hexdigest(), clib_identity, 'identified C runtime')
    clib = pef.PEFContainer(clib_bytes, clib_path.name)
    for name, vector, code in [('time', 0x1dcc, 0x23200), ('localtime', 0x1dac, 0x23974)]:
        resolved = timer.vector(clib, name)
        timer.require((resolved['vector_offset'], resolved['code_offset'], resolved['toc_offset']),
                      (vector, code, 0x8000), 'C runtime time transition vector')
    timer.require(timer.call_target(clib, 0x23214), 0x248a8, 'time enters OS sample wrapper')
    timer.require(timer.call_target(clib, 0x239cc), 0x22d00, 'localtime enters field converter')
    imported = timer.glue_import(clib, timer.call_target(clib, 0x248b8), 0x8000)
    timer.require((imported['library'], imported['symbol']), ('InterfaceLib', 'GetDateTime'),
                  'C time samples host clock')
    timer.require(timer.d_fields(clib, 0x248c4, 15), (3, 3, 1925), 'host time epoch offset high')
    timer.require(timer.d_fields(clib, 0x248c8, 14), (3, 3, -12800), 'host time epoch offset low')
    timer.require(timer.d_fields(clib, 0x22d3c, 14), (31, 0, 0), 'localtime year count starts zero')
    timer.require(timer.d_fields(clib, 0x22de8, 14), (31, 31, 1), 'localtime year loop increment')
    timer.require(timer.d_fields(clib, 0x22df0, 36), (31, 27, 20), 'localtime year count stored directly')
    runtime_hashes = {}
    for start, end, expected in [
        (0x23200, 0x23238, 'c6204a1ccb57d75cb4a6fc406c7bcacc2af20eee9c3a0395f798fa8e285d9049'),
        (0x23974, 0x239e8, '62f742d0cab5f3ff2941eb3a6845494d90c4f3db2ad60c4d693b2e262bb87c13'),
        (0x248a8, 0x248dc, 'bff42db7f76a3d90295aed47a88ad33d8e055f6d56d69cac12fecb9768d3e95c'),
        (0x22d00, 0x22f48, '5c1f2f6618ec4b36109047288c42a48fcd3fa2d1ba7a2c6f12a4d505f59c6e31'),
    ]:
        actual = hashlib.sha256(clib.code.data[start:end]).hexdigest()
        timer.require(actual, expected, 'C time consumer code region hash')
        runtime_hashes[f'{start:#x}..{end:#x}'] = actual
    hashes = {}
    for start, end, expected in [
        (0x1c1348, 0x1c1380, '4e20c5aa7b61ff2d7f5a10405be93fc5d60c0e2180fa5373a85b1ecc8b8acf75'),
        (0x105398, 0x1053b0, 'bb9c0490ec3b920657b229eb92b8c96209580994c7bceee59bd36f16344e1d3f'),
        (0x105630, 0x105648, '47c143497270caa6c03b47b36f4f0cfc84f7223c1984f95efe8573481f5a4fb8'),
        (0xd67f0, 0xd6854, 'ac19d5aeda4bd969cf79d3430d2be7a89ce1707b4f78cd783fbc0fe15a016a03'),
        (0xb2760, 0xb27cc, 'e033dee538ba480e1a7ef61d6d3264095a09ab025e6a169122b2c34e90bbf645'),
        (0xb2870, 0xb28cc, 'cc1ebc9308ee2bc0e604c742988ade573f680034993066a81adfc7a7330c550e'),
        (0xb1fd0, 0xb2058, 'f3ce5028ff8ebd81955eca313012959bb20087b858b31a07ad9a95254401ef6f'),
        (0xa7098, 0xa70c8, '53b98b2a990bb3a486f40055401c324bba16b75b3ac82a8707248367fa2701e8'),
    ]:
        actual = hashlib.sha256(app.code.data[start:end]).hexdigest()
        timer.require(actual, expected, 'consumer code region hash')
        hashes[f'{start:#x}..{end:#x}'] = actual
    return {'identity': timer.IDENTITIES['SimThemePark.data'], 'toc': '0x8000',
            'witnesses': records, 'arithmetic': arithmetic, 'region_sha256': hashes,
            'callback_state_dispatch': dispatch, 'animation_unsigned_bias': bias,
            'host_civil_opcodes': wall_fields, 'c_runtime_identity': clib_identity,
            'c_runtime_region_sha256': runtime_hashes, 'c_runtime_time_epoch_offset': 126144000,
            'findings': ['World state4 skips regular thing iteration after world tick increment; common calendar update remains.',
                         'Callback state4 is a transition to5; callback state10 enters the scheduler.',
                         'Manager initialization state gates phase; empty initialized list does not stop phase.',
                         'GETTIMER clamps signed wrapped deadline-minus-now; WAIT uses a different unsigned deadline compare.',
                         'Animation elapsed word is unsigned before three separately rounded f32 operations.',
                         'RSE YEAR/MONTH/DAY/HOUR sample time/localtime ultimately backed by InterfaceLib GetDateTime, not virtual park date.'],
            'limitations': ['Static identified Mac slices, not native execution or Windows/Patch2 proof.',
                            'Higher-level state changes and message dispatch can independently pause or stop work.',
                            'Scheduler signed timestamp crossing remains unsupported by the standalone C# contract.']}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.bin_root), indent=2, sort_keys=True))
    except (OSError, pef.PEFError, KeyError) as error:
        parser.exit(1, f'clock consumers: {error}\n')
