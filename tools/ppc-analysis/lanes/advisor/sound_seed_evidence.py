"""Sound seed ownership/linkage witnesses; no original instructions executed.

The exported data word permits external mutation. This bounds the identified
bundle's linkage and selected address consumers, not arbitrary loaded modules.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

import evidence as common
import sound_selection_evidence as selection

SEED = 'mRandomSeed__17CAudioPlaceHolder'
BUNDLE = {
    'SimThemePark.data': common.IDENTITIES['SimThemePark.data'],
    'libraries/bullfrog_shared.data': 'b67b56b7b2b75962b8b34559e20fd97b623b2d0f7f08a0035f82072ffbf4ec06',
    'libraries/c_c++_shared.data': '5e04f9c00c922dc78a787d1b93067c75d37a3e65b0a0202e50c2f449f131b27f',
    'libraries/engine_shared.data': 'c549123f647dcf33c3e2c3d515bffe2cfcb19d11680f743f02ca42bb09dbd73b',
    'libraries/indirectx_shared.data': 'cb5473b799b4c6d4effd11e00a8eaa8fdc6aae87a00caaadd4368de5d56574a4',
    'libraries/libjpeg_shared.data': '72603ebc8a31b5cf17a611410228a95538c63c3faa6d50b13fff6353594057eb',
    'libraries/ltms_shared.data': '2b0f7ac92c1f8b67761d271dd5832fca1bd19a8dd8d494b7b89b6ffa693e6ee8',
    'libraries/macdoze_shared.data': 'ba11331a70bce77140ae6e7fe73145fe4e13cce5f042707a494cb77654b32f0d',
    'libraries/mail_shared.data': 'c27a22a457469077e5c832e2f29424ba3b79fa815c62a37d0f34e337f327ba67',
    'libraries/more_files_shared.data': '27f3e7c03e97ee1bc305cd767ce07bb04199574c2d64a84b9b7e3904767bf506',
    'libraries/online_shared.data': '05c21d2eda5fa4b800859a874ab986433adae2e303de230476329df6c019b2cb',
    'libraries/runtime_shared.data': '18af42bc9ffce8a11c0a03815ada9518386f7634dc09e860799093974ebc974d',
    'libraries/sams_utils_shared.data': '1959a54b2280c95ddcc25ec77c070b4d609dcca7b7e59683b460c92c7297bcd6',
    'libraries/sound_shared.data': common.IDENTITIES['sound_shared.data'],
    'libraries/winsock_shared.data': '459f84d3980c3abe89b058472779424afea6ad4c42e6985c17f914f93c0dc55b',
    'libraries/zlib_shared.data': 'b5f3195f1e5d0022af5453b8c721f24e9aa4156d02b2323bb62ecf65dd52a895',
}


def direct_alias_accesses(c, alias: int, toc: int):
    """D-form accesses/address construction via r2; no indexed-flow claim."""
    result = []
    for address in range(0, len(c.code.data), 4):
        word = common.pef._u32(c.code.data, address)
        opcode = word >> 26
        if opcode not in (14, 15, *range(32, 56)) or word >> 16 & 31 != 2:
            continue
        displacement = word & 65535
        if displacement & 32768:
            displacement -= 65536
        if toc + displacement == alias:
            result.append(address)
    return result


def ownership(c):
    exported = [item for item in c.exports if item.name == SEED]
    common.require(len(exported), 1, 'unique sound seed export')
    symbol = exported[0]
    common.require((symbol.sym_class, symbol.section, symbol.value),
                   (1, c.data_section.index, 0xc2e4), 'seed is data, not a transition vector')
    aliases = [(section, slot) for section, relocs in c.relocs.items()
               for slot, target in relocs.items() if target.kind == 'section'
               and target.target == symbol.section and target.addend == symbol.value]
    common.require(aliases, [(1, 0x348)], 'sole relocated seed pointer')
    incoming_aliases = [(section, slot) for section, relocs in c.relocs.items()
                        for slot, target in relocs.items() if target.kind == 'section'
                        and target.target == 1 and target.addend == 0x348]
    return {'export': SEED, 'export_class': symbol.sym_class, 'data_offset': symbol.value,
            'alias_section': 1, 'alias_offset': 0x348,
            'alias_direct_accesses': direct_alias_accesses(c, 0x348, 0x8000),
            'alias_incoming_relocations': incoming_aliases}


def pascal(c, section: int, offset: int):
    raw = c.sections[section].data
    if offset >= len(raw) or offset + 1 + raw[offset] > len(raw):
        raise common.pef.PEFError('Pascal lookup string outside section')
    return bytes(raw[offset + 1:offset + 1 + raw[offset]]).decode('ascii')


def callers(c, target: int):
    return [at for at in range(0, len(c.code.data), 4)
            if common.pef._u32(c.code.data, at) >> 26 == 18
            and common.pef._u32(c.code.data, at) & 1
            and common.call_target(c, at) == target]


def native(root: Path):
    files = {str(path.relative_to(root)) for path in root.rglob('*.data')}
    common.require(files, set(BUNDLE), 'identified selected bundle file set')
    containers = {name: common.pef.PEFContainer(common.identified(root / name, digest))
                  for name, digest in BUNDLE.items()}
    sound = containers['libraries/sound_shared.data']
    report = ownership(sound)
    common.require(report['alias_direct_accesses'], [], 'seed alias has no direct TOC consumers')
    common.require(report['alias_incoming_relocations'], [], 'no relocated pointer to seed alias cell')
    imports = [(name, item.index) for name, c in containers.items() for item in c.imports if item.name == SEED]
    common.require(imports, [], 'no identified bundle imports seed data')
    payload_names = [(name, section.index) for name, c in containers.items()
                     for section in (c.code, c.data_section) if SEED.encode() in section.data]
    common.require(payload_names, [], 'no seed-name literal in bundle code/data')
    symbol_lookups = [(name, item.index) for name, c in containers.items() for item in c.imports
                      if item.name == 'FindSymbol']
    common.require(symbol_lookups, [('libraries/sams_utils_shared.data', 193)], 'bundle dynamic symbol lookup owner')
    sams = containers['libraries/sams_utils_shared.data']
    common.require(common.glue_import(sams, 0x1f6fc)['symbol'], 'FindSymbol', 'dynamic symbol lookup glue')
    common.require(callers(sams, 0x1f6fc), [0xa86c], 'single FindSymbol caller')
    common.require(callers(sams, 0xa808), [0xa0b4, 0xa0d0], 'lookup helper callers')
    resolver_pointers = [(section, slot) for section, relocs in sams.relocs.items()
                         for slot, target in relocs.items() if target.kind == 'section'
                         and target.target == sams.code.index and target.addend == 0xa808]
    common.require(resolver_pointers, [], 'lookup helper has no relocated indirect entry')
    common.require(common.glue_import(sams, common.call_target(sams, 0xa840))['symbol'],
                   'GetSharedLibrary', 'lookup library resolver')
    common.require(common.d_fields(sams, 0xa07c, 32), (23, 2, 2040), 'lookup string base TOC operand')
    pointer = sams.relocs[1][2040]
    common.require((pointer.kind, pointer.target, pointer.addend), ('section', 0, 0x212e1), 'lookup Pascal string base')
    common.require(common.x_fields(sams, 0xa088, 444), (23, 3, 23), 'dynamic lookup library argument')
    for at, expected in [(0xa090, (4, 23, 18)),
                         (0xa0c8, (3, 23, 0)), (0xa0cc, (4, 23, 25))]:
        common.require(common.d_fields(sams, at, 14), expected, 'dynamic lookup argument')
    names = [pascal(sams, 0, pointer.addend + offset) for offset in [0, 18, 25]]
    common.require(names, ['DriverServicesLib', 'UpTime', 'AbsoluteToNanoseconds'], 'actual dynamic lookup targets')
    # Bound the seed pointer's local consumers, including pointer kills before
    # arithmetic/calls. This complements alias/linkage evidence above.
    for at, opcode, operands in [
        (0xf410, 32, (3, 4, 0)), (0xf554, 32, (3, 4, 0)),
        (0xf770, 32, (3, 4, 0)), (0xf994, 32, (3, 4, 0)),
        (0xfcf0, 32, (5, 6, 0)), (0xfcf4, 14, (6, 7, 0)),
        (0xff90, 32, (3, 5, 0)), (0xff98, 14, (5, 6, 0)),
        (0x18108, 32, (6, 6, 0)), (0x193ac, 32, (5, 6, 0)),
        (0x193b0, 14, (6, 9, 0))]:
        common.require(common.d_fields(sound, at, opcode), operands, 'seed pointer read/local replacement')
    report.update({'bundle_sha256': BUNDLE, 'seed_imports': imports,
                   'seed_payload_name_literals': payload_names, 'dynamic_lookup_targets': names,
                   'dynamic_resolver_relocated_entries': resolver_pointers,
                   'direct_consumer_evidence': selection.native(root),
                   'qualification': 'Recovered bundle selection does not advance the seed; external exported-data mutation is not excluded.'})
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('binary_root', type=Path)
    print(json.dumps(native(parser.parse_args().binary_root), indent=2))


if __name__ == '__main__':
    main()
