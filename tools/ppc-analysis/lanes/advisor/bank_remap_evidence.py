"""Static category-bank fixup witnesses and explicit SDT ordinal/name resolution.

Never executes original code. Asset quality suffix is a caller input, not an
assumed native scheduling/device policy. Output is interpreted metadata only.
"""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import struct

import evidence as common
import audio_event_evidence as audio


def bank_records(raw: bytes):
    paths = audio.banks(raw)
    # ReadBankDataFromMap resets the serialized handle/pointer fields. Only the
    # ordinal, bounded path string and selected registration flag are reported.
    return [{'ordinal': index + 1, 'family': path, 'flags': raw[28 + index * 11 + 9]}
            for index, path in enumerate(paths)]


def resolve_choice(choice: dict, records: list[dict], entries_by_family: dict):
    ordinal = choice['bank_id']
    if not 1 <= ordinal <= len(records):
        raise common.pef.PEFError('catalog bank ordinal has no supplied BANK record')
    record = records[ordinal - 1]
    entries = entries_by_family[record['family']]
    sample = choice['sample_id']
    if not 1 <= sample <= len(entries):
        raise common.pef.PEFError('sample ordinal is outside the resolved SDT bank')
    entry = entries[sample - 1]
    return {'bank_ordinal': ordinal, 'family': record['family'], 'sample_ordinal': sample,
            'stored_name': entry['name'], 'entry_offset': entry['offset']}


def case_path(base: Path, relative: str):
    current = base
    for part in relative.replace('\\', '/').split('/'):
        if part in ('', '.', '..'):
            raise common.pef.PEFError('selected bank family is not a bounded relative path')
        children = list(current.iterdir())
        exact = [path for path in children if path.name == part]
        matches = exact or [path for path in children if path.name.casefold() == part.casefold()]
        if len(matches) != 1:
            raise common.pef.PEFError('bank family has no unique case-insensitive corpus path')
        # Return the actual stored spelling, avoiding duplicate cache identities on
        # case-insensitive filesystems when BANK records vary only by case.
        current = matches[0]
    return current


def native(root: Path):
    c = common.pef.PEFContainer(common.identified(root / 'libraries/sound_shared.data', common.IDENTITIES['sound_shared.data']))
    for at, target in [(0x14bc0, 0x15d4c), (0x15dfc, 0x157d0), (0x15954, 0x15100),
                       (0x14c00, 0x161c0), (0x16240, 0x15f00), (0x1618c, 0x162bc)]:
        common.require(common.call_target(c, at), target, 'category BANK before SFX reader call')
    common.require(common.d_fields(c, 0x15194, 7), (0, 3, 11), 'BANK record stride')
    common.require(common.d_fields(c, 0x15208, 14), (31, 31, 11), 'BANK record walk')
    common.require(common.d_fields(c, 0x151dc, 36), (0, 31, 0), 'serialized bank handle reset')
    common.require(common.d_fields(c, 0x151e8, 38), (0, 31, 4), 'serialized bank cache byte reset')
    common.require(common.d_fields(c, 0x151cc, 36), (28, 31, 5), 'serialized name pointer replaced')
    # New global bank: append global registry index to the transient logical map.
    for at, fields in [(0x15b8c, (18, 31, 52)), (0x15b94, (3, 31, 52)),
                       (0x15c24, (3, 31, 44))]:
        common.require(common.d_fields(c, at, 36 if at == 0x15b94 else 32), fields, 'new-bank logical fixup append')
    common.require(common.x_fields(c, 0x15c30, 151), (17, 3, 0), 'new bank fixup stores registry index')
    # Existing path: it also takes the next logical ordinal, not only new banks.
    common.require(common.d_fields(c, 0x15590, 32), (27, 25, 52), 'reused-bank next logical ordinal')
    common.require(common.d_fields(c, 0x15598, 36), (3, 25, 52), 'reused-bank logical ordinal advances')
    common.require(common.d_fields(c, 0x15628, 32), (5, 25, 44), 'reused-bank fixup vector')
    common.require(common.x_fields(c, 0x15638, 151), (30, 5, 0), 'reused-bank global registry index')
    common.require(common.d_fields(c, 0x16a58, 40), (28, 25, 12), 'sample bank ID from packed choice')
    common.require(common.d_fields(c, 0x16af4, 14), (0, 28, -1), 'one-based catalog bank index')
    common.require(common.d_fields(c, 0x16af8, 32), (3, 31, 44), 'sample bank fixup vector')
    common.require(common.d_fields(c, 0x16b04, 44), (0, 25, 12), 'global index replaces packed bank ordinal')
    for at, field in [(0x14c28, 44), (0x14c2c, 48), (0x14c34, 52),
                      (0x14fb8, 44), (0x14fbc, 48), (0x14fc8, 52)]:
        common.require(common.d_fields(c, at, 36)[2], field, 'registration temporary map reset')
    common.require(common.d_fields(c, 0xf5d0, 40), (4, 5, 12), 'placeholder resolves mapped bank')
    common.require(common.d_fields(c, 0xf5e8, 32), (4, 4, 0), 'placeholder preserves sample ID')
    common.require(common.d_fields(c, 0x6fa4, 14), (0, 4, -1), 'SDT member number is one-based')
    common.require(common.d_fields(c, 0x6a64, 14), (3, 0, 0), 'TbFileBank sample-name getter has no name lookup')
    return {'identity': common.IDENTITIES['sound_shared.data'], 'BANK_load': 0x14bc0,
            'SFX_load': 0x14c00, 'fixup_fields': {'vector': 44, 'capacity': 48, 'count': 52},
            'new_append': 0x15c30, 'reused_append': 0x15638,
            'sample_bank_rewrite': 0x16b04, 'sample_position': 0x6fa4,
            'sample_name_getter': 0x6a64,
            'range_hashes': {f'{lo:#x}-{hi:#x}': hashlib.sha256(c.code.data[lo:hi]).hexdigest()
                             for lo, hi in [(0x14bb8, 0x14c38), (0x15590, 0x15640),
                                            (0x15b8c, 0x15c34), (0x16a58, 0x16b08)]},
            'limitations': ['BANK/SFX registration success and file quality/path policy remain explicit dependencies.',
                           'Names are corpus identity metadata; no native event-by-name playback is established.']}


def corpus(root: Path, suffix: str):
    if suffix not in ('HD.sdt', 'LD.sdt'):
        raise common.pef.PEFError('select an explicit bounded corpus quality suffix')
    cache = {}
    summaries = []
    selected = []
    total = 0
    for path in sorted(root.rglob('*')):
        if not path.name.casefold().endswith('sfx.map'):
            continue
        bank_path = case_path(path.parent, path.name[:-7] + 'BANK.map')
        records = bank_records(bank_path.read_bytes())
        banks = {}
        for record in records:
            if record['flags'] & 0x20:
                raise common.pef.PEFError('selected corpus requires an unreviewed alternate root route')
            sdt_path = case_path(path.parent.parent, record['family'] + suffix)
            if sdt_path not in cache:
                raw = sdt_path.read_bytes()
                entries = common.sdt_entries(raw)
                duplicates = {name: count for name, count in Counter(row['name'] for row in entries).items() if count > 1}
                cache[sdt_path] = (entries, {'path': str(sdt_path.relative_to(root)),
                    'sha256': hashlib.sha256(raw).hexdigest(), 'entries': len(entries), 'duplicate_stored_names': duplicates})
            banks[record['family']] = cache[sdt_path][0]
        choices = 0
        for category in audio.catalog(path.read_bytes()):
            for row in category['sounds']:
                mapped = [resolve_choice(choice, records, banks) for element in row['elements'] for choice in element['samples']]
                choices += len(mapped)
                if ((path.name.casefold() == 'cat_ridessfx.map' and row['catalog_id'] in (145, 175, 204)) or
                    (path.name.casefold() == 'cat_uisfx.map' and row['catalog_id'] == 31)):
                    selected.append({'catalog': str(path.relative_to(root)), 'catalog_id': row['catalog_id'],
                                     'choices': mapped})
        total += choices
        summaries.append({'catalog': str(path.relative_to(root)), 'BANK_sha256': hashlib.sha256(bank_path.read_bytes()).hexdigest(),
                          'SFX_sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'bank_records': records,
                          'resolved_choices': choices})
    return {'quality_suffix_input': suffix, 'catalogs': summaries, 'resolved_choices': total,
            'banks': [value[1] for _, value in sorted(cache.items())], 'selected': selected,
            'limitation': 'Explicit supplied corpus family/suffix lookup; no original device/mixer/event-element policy is inferred.'}


def compare_selected(pc: dict, mac: dict):
    pc_rows = {(row['catalog'], row['catalog_id']): row for row in pc['selected']}
    mac_rows = {(row['catalog'], row['catalog_id']): row for row in mac['selected']}
    return [{'catalog': key[0], 'catalog_id': key[1],
             'resolved_choices_equal': pc_rows[key]['choices'] == mac_rows[key]['choices']}
            for key in sorted(pc_rows.keys() & mac_rows.keys())]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--pc-data', type=Path)
    parser.add_argument('--mac-data', type=Path)
    parser.add_argument('--quality-suffix', default='HD.sdt')
    args = parser.parse_args()
    try:
        result = {'native': native(args.bin_root)}
        for label, root in [('pc', args.pc_data), ('mac', args.mac_data)]:
            if root:
                result[label] = corpus(root, args.quality_suffix)
        if 'pc' in result and 'mac' in result:
            result['selected_cross_edition_comparison'] = compare_selected(result['pc'], result['mac'])
    except (OSError, common.pef.PEFError, ValueError, KeyError, struct.error) as error:
        parser.error(str(error))
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()
