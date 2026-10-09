"""Reproduce selected static timer facts for the identified Feral Mac binaries.

No original instructions are executed. Output contains identities, addresses and
selected interpreted operands only, never binary contents or disassembly.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

import pef

IDENTITIES = {
    'SimThemePark.data': '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5',
    'bullfrog_shared.data': 'b67b56b7b2b75962b8b34559e20fd97b623b2d0f7f08a0035f82072ffbf4ec06',
    'sams_utils_shared.data': '1959a54b2280c95ddcc25ec77c070b4d609dcca7b7e59683b460c92c7297bcd6',
}
GET_ABSOLUTE = 'GetAbsolute__Q213SamsUtilities6UTimerFv'
SET_RATE = 'SetRate__Q213SamsUtilities6UTimerFRCQ213SamsUtilities13UMicrosecondsUc'


def require(actual, expected, context: str):
    if actual != expected:
        raise pef.PEFError(f'{context}: unexpected interpreted value {actual!r}')
    return actual


def d_fields(container: pef.PEFContainer, offset: int, opcode: int) -> tuple[int, int, int]:
    word = pef._u32(container.code.data, offset)
    require(word >> 26, opcode, f'primary operation at code:{offset:#x}')
    immediate = word & 0xffff
    if immediate & 0x8000:
        immediate -= 0x10000
    return (word >> 21 & 31, word >> 16 & 31, immediate)


def call_target(container: pef.PEFContainer, offset: int) -> int:
    word = pef._u32(container.code.data, offset)
    require((word >> 26, word & 1), (18, 1), f'linked branch at code:{offset:#x}')
    displacement = word & 0x03fffffc
    if displacement & 0x02000000:
        displacement -= 0x04000000
    return displacement if word & 2 else offset + displacement


def vector(container: pef.PEFContainer, name: str) -> dict:
    exports = [e for e in container.exports if e.name == name]
    require(len(exports), 1, f'export {name}')
    export = exports[0]
    require(export.sym_class, 2, f'transition-vector class for {name}')
    relocs = container.relocs.get(export.section, {})
    target, toc = relocs.get(export.value), relocs.get(export.value + 4)
    if not target or not toc or target.kind != 'section' or toc.kind != 'section':
        raise pef.PEFError(f'missing relocated transition-vector words: {name}')
    require(target.target, container.code.index, f'code section for {name}')
    require(toc.target, container.data_section.index, f'TOC section for {name}')
    return {'symbol': name, 'vector_section': export.section, 'vector_offset': export.value,
            'code_section': target.target, 'code_offset': target.addend, 'toc_offset': toc.addend}


def glue_import(container: pef.PEFContainer, address: int, toc: int = 0) -> dict:
    rd, base, displacement = d_fields(container, address, 32)
    require((rd, base), (12, 2), 'CFM import glue TOC load')
    slot = toc + displacement
    target = container.relocs.get(container.data_section.index, {}).get(slot)
    if not target or target.kind != 'import':
        raise pef.PEFError('glue TOC slot is not an imported transition vector')
    imported = container.imports[target.target]
    return {'code_offset': address, 'toc_slot': slot, 'symbol': imported.name, 'library': imported.library}


def load_identified(path: Path) -> pef.PEFContainer:
    raw = path.read_bytes()
    require(hashlib.sha256(raw).hexdigest(), IDENTITIES[path.name], f'identity of {path.name}')
    return pef.PEFContainer(raw, path.name)


def inspect(root: Path) -> dict:
    app = load_identified(root / 'SimThemePark.data')
    bullfrog = load_identified(root / 'libraries/bullfrog_shared.data')
    sams = load_identified(root / 'libraries/sams_utils_shared.data')
    clock = vector(bullfrog, 'LbTime_GetClock__Fv')
    absolute = vector(sams, GET_ABSOLUTE)
    setter = vector(sams, SET_RATE)
    require((clock['code_offset'], absolute['code_offset'], setter['code_offset']),
            (0x38bb8, 0xa4f8, 0xa738), 'selected exported code addresses')
    clock_call = call_target(bullfrog, clock['code_offset'] + 16)
    imported_absolute = glue_import(bullfrog, clock_call, clock['toc_offset'])
    require(imported_absolute['symbol'], GET_ABSOLUTE, 'clock source import')
    require(d_fields(bullfrog, clock['code_offset'] + 28, 14), (5, 0, 0), 'divisor high word')
    require(d_fields(bullfrog, clock['code_offset'] + 36, 14), (6, 0, 1000), 'divisor low word')
    returned = pef._u32(bullfrog.code.data, clock['code_offset'] + 48)
    require((returned >> 26, returned >> 21 & 31, returned >> 16 & 31,
             returned >> 11 & 31, returned >> 1 & 0x3ff),
            (31, 4, 3, 4, 444), 'wrapper returns low quotient register')
    divider = call_target(bullfrog, clock['code_offset'] + 40)
    require(divider, 0x59d98, 'manually inspected unsigned division helper')
    microseconds_glue = call_target(sams, absolute['code_offset'] + 0x16c)
    microseconds = glue_import(sams, microseconds_glue, absolute['toc_offset'])
    require((microseconds['symbol'], microseconds['library']), ('Microseconds', 'InterfaceLib'), 'fallback source')
    # Validate the unchanged fallback two-word output copy.
    require(d_fields(sams, 0xa66c, 32), (3, 1, 0x48), 'fallback high word load')
    require(d_fields(sams, 0xa670, 32), (4, 1, 0x4c), 'fallback low word load')
    require(d_fields(sams, 0xa674, 36), (4, 31, 4), 'absolute low word output')
    require(d_fields(sams, 0xa678, 36), (3, 31, 0), 'absolute high word output')
    # SetRate copies a 64-bit interval and one flag without arithmetic conversion.
    for offset, opcode, fields in [(0, 32, (0, 4, 0)), (4, 32, (4, 4, 4)),
                                   (8, 36, (4, 3, 16)), (12, 36, (0, 3, 12)),
                                   (16, 38, (5, 3, 8))]:
        require(d_fields(sams, setter['code_offset'] + offset, opcode), fields, 'SetRate field copy')
    # The application builds the low word from a shifted immediate plus signed addend.
    high_literal = d_fields(app, 0x310, 15)
    addend = d_fields(app, 0x320, 14)
    require((high_literal[:2], addend[:2]), ((3, 0), (0, 3)), 'application interval construction')
    interval = (high_literal[2] << 16) + addend[2]
    require(interval, 100000, 'application SetRate interval')
    require(d_fields(app, 0x32c, 36), (0, 1, 0x8c), 'application interval low word store')
    require(d_fields(app, 0x334, 36), (26, 1, 0x88), 'application interval high word store')
    require(d_fields(app, 0x318, 14), (26, 0, 0), 'application interval high word zero')
    require(d_fields(app, 0x324, 14), (4, 1, 0x88), 'application interval pointer')
    require(d_fields(app, 0x328, 14), (3, 27, 0x5c), 'application timer object field')
    require(d_fields(app, 0x330, 14), (5, 0, 1), 'application timer flag')
    app_toc = app.relocs[app.main[0]][app.main[1] + 4].addend
    app_setter = glue_import(app, call_target(app, 0x338), app_toc)
    require(app_setter['symbol'], SET_RATE, 'application SetRate call')
    constants = {hex(offset): struct.unpack_from('>d', sams.data_section.data, offset)[0]
                 for offset in (0x8b8, 0x8c0, 0x8d0, 0x8d8)}
    return {'identities': IDENTITIES, 'clock': clock, 'absolute': absolute, 'set_rate': setter,
            'clock_source_import': imported_absolute, 'clock_divisor': 1000,
            'division_helper_code_offset': divider,
            'division_helper_sha256': hashlib.sha256(bytes(bullfrog.code.data[divider:0x59e84])).hexdigest(),
            'absolute_fallback_import': microseconds, 'absolute_fallback_call_offset': 0xa664,
            'application_set_rate_import': app_setter, 'application_set_rate_call_offset': 0x338,
            'application_interval_raw': interval, 'application_timer_flag': 1,
            'application_timer_object_offset': 0x5c, 'sams_selected_double_constants': constants,
            'resolved_gameplay_approximation_ids': [],
            'limitation': 'OS timer conversion and one timer initialization do not identify animation, park calendar or advisor clock consumers.'}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
    except (OSError, pef.PEFError) as error:
        parser.exit(1, f'timer evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()
