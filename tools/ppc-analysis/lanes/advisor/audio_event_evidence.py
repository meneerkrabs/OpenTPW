"""Identity-pinned sound-category/event routes and bounded catalog metadata.

Never executes original code. Corpus reports are interpreted metadata only and
must be written outside Git. No event names are inferred from numeric aliases.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

import evidence as common
import controller_evidence as controller


def catalog(raw: bytes):
    """Reviewed packed layout; leave flags, thresholds and scheduling opaque."""
    if len(raw) < 28 or len(raw) > 16 * 1024 * 1024:
        raise common.pef.PEFError('sound catalog length outside bounded profile')
    count = struct.unpack_from('<I', raw, 24)[0]
    position = 28

    def take(count, stride):
        nonlocal position
        if count > 65536 or count * stride > len(raw) - position:
            raise common.pef.PEFError('sound catalog record count exceeds input')
        offset = position
        position += count * stride
        return offset

    category_start = take(count, 24)
    categories = []
    for index in range(count):
        sound_count = struct.unpack_from('<I', raw, category_start + index * 24)[0]
        sound_start = take(sound_count, 20)
        sounds = []
        seen = set()
        for row in range(sound_count):
            offset = sound_start + row * 20
            identifier, event_count = struct.unpack_from('<II', raw, offset)
            if identifier in seen:
                raise common.pef.PEFError('duplicate sound identifier in category')
            seen.add(identifier)
            event_start = take(event_count, 42)
            events = []
            for element in range(event_count):
                event_offset = event_start + element * 42
                flags_count, children = struct.unpack_from('<II', raw, event_offset)
                sample_count = flags_count & 65535
                samples_start = take(sample_count, 16)
                samples = []
                for sample in range(sample_count):
                    off = samples_start + sample * 16
                    sample_id, threshold, span, bank_id, opaque = struct.unpack_from('<IIIHH', raw, off)
                    samples.append({'sample_id': sample_id, 'threshold_raw': threshold,
                                    'span_raw': span, 'bank_id': bank_id, 'opaque': opaque})
                child_start = take(children, 8)
                links = [struct.unpack_from('<I', raw, child_start + child * 8)[0]
                         for child in range(children)]
                # ReadEvents 0x1667c subtracts one before linking an event index.
                if any(link == 0 or link > event_count for link in links):
                    raise common.pef.PEFError('child event reference outside sound elements')
                events.append({'record_offset': event_offset, 'samples': samples,
                               'child_element_indices_one_based': links})
            sounds.append({'catalog_id': identifier, 'record_offset': offset, 'elements': events})
        categories.append({'record_offset': category_start + index * 24, 'sounds': sounds})
    if position != len(raw):
        raise common.pef.PEFError('sound catalog has unexplained trailing bytes')
    return categories


def banks(raw: bytes):
    if len(raw) < 28 or len(raw) > 1024 * 1024:
        raise common.pef.PEFError('bank map length outside bounded profile')
    count = struct.unpack_from('<I', raw, 24)[0]
    if count > 65536 or count * 11 > len(raw) - 28:
        raise common.pef.PEFError('bank map records exceed input')
    position = 28 + count * 11
    paths = []
    for _ in range(count):
        if position + 4 > len(raw):
            raise common.pef.PEFError('bank path length missing')
        length = struct.unpack_from('<I', raw, position)[0]
        position += 4
        if not 1 <= length <= 1024 or position + length > len(raw):
            raise common.pef.PEFError('bank path exceeds input')
        data = raw[position:position + length]
        if data[-1] != 0 or b'\0' in data[:-1]:
            raise common.pef.PEFError('bank path is not a single terminated string')
        try:
            paths.append(data[:-1].decode('ascii'))
        except UnicodeDecodeError as error:
            raise common.pef.PEFError('bank path is not ASCII') from error
        position += length
    if position != len(raw):
        raise common.pef.PEFError('bank map has unexplained trailing bytes')
    return paths


def native(root: Path):
    app = common.pef.PEFContainer(common.identified(root / 'SimThemePark.data', common.IDENTITIES['SimThemePark.data']))
    sound = common.pef.PEFContainer(common.identified(root / 'libraries/sound_shared.data', common.IDENTITIES['sound_shared.data']))
    common.require(common.toc_pointer(app, 0xae93c), 0x97f8c, 'category handle array')
    table = common.toc_pointer(app, 0xae968)
    common.require(table, 0x3f278, 'RSE EVENT effect switch')
    entries = [common.data_pointer(app, table + 4 * index, app.code.index) for index in range(11)]
    common.require(entries, [0xaeee0, 0xae97c, 0xaea58, 0xaeb64, 0xaebc8, 0xaee18,
                            0xaee7c, 0xaed50, 0xaecec, 0xaedb4, 0xaec2c], 'RSE EVENT destinations')
    routes = [(3, 0xaeba0, 28, 'local rides', 0xaebc0), (4, 0xaec04, 24, 'local ambient', 0xaec24),
              (5, 0xaee54, 8, 'global rides', 0xaee74), (6, 0xaeeb8, 4, 'global kids', 0xaeed8),
              (7, 0xaed8c, 16, 'global staff', 0xaedac), (8, 0xaed28, 0, 'global ambient', 0xaed48),
              (9, 0xaedf0, 12, 'global UI', 0xaee10)]
    for _, at, offset, _, call in routes:
        common.require(common.d_fields(app, at, 32), (4, 24, offset), 'EVENT category handle operand')
        common.require(common.call_target(app, call), 0xbb4fc, 'EVENT sound submit')
    common.require(common.call_target(app, 0xaf9e8), 0xae930, 'EVENT opcode dispatcher call')
    common.require(common.call_target(app, 0xafa60), 0xae930, 'EVENT_EXT dispatcher call')
    common.require(common.d_fields(app, 0xaf9e4, 14), (7, 0, 1000), 'EVENT default final control')
    common.require(common.call_target(app, 0xbcb20), 0xb5c5c, 'sound child variable resolver')
    common.require(common.call_target(app, 0xbcb40), 0xbb4fc, 'resolved sound child catalog submit')
    common.require(common.conditional_branch(app, 0xbcb28), (12, 2, 0xbcb48), 'zero variable suppresses submit')
    common.require(common.d_fields(app, 0xb5cdc, 32), (4, 4, 20), 'sound child script ID field')
    common.require(common.d_fields(app, 0xb5d44, 32), (3, 3, 28), 'sound child variable array')
    common.require(common.d_fields(app, 0xb5d38, 32), (0, 3, 140), 'sound child variable bound')
    common.require(common.d_fields(app, 0xb12a0, 36), (3, 31, 20), 'SPAWNSOUND child ID destination')
    registrations = [(0xbc7d8, 'cat_ambient', 0xbc7e4, 31, 0),
                     (0xbc7e8, 'cat_rides', 0xbc7f4, 28, 0),
                     (0xbc7f8, 'cat_ui', 0xbc804, 27, 0),
                     (0xbc808, 'cat_kids', 0xbc814, 26, 0),
                     (0xbc818, 'cat_staff', 0xbc824, 25, 0)]
    names = {587: 'cat_ambient', 599: 'cat_rides', 609: 'cat_ui', 616: 'cat_kids', 625: 'cat_staff'}
    _, _, string_slot = common.d_fields(app, 0xbc6f8, 32)
    string_base = common.data_pointer(app, 0x8000 + string_slot, app.code.index)
    for at, name, store, register, offset in registrations:
        rd, ra, immediate = common.d_fields(app, at, 14)
        common.require((rd, ra, names[immediate], common.cstring(app, string_base + immediate)),
                       (3, 30, name, name), 'category registration name')
        common.require(common.call_target(app, at + 8), 0xba5a8, 'category registration helper')
        common.require(common.d_fields(app, store, 36), (3, register, offset), 'category handle store')
    for at, rd, offset in [(0xbc744, 27, 12), (0xbc760, 26, 4), (0xbc77c, 25, 16), (0xbc728, 28, 8)]:
        common.require(common.d_fields(app, at, 14), (rd, 31, offset), 'registration handle address')
    common.require(common.d_fields(app, 0xbc170, 32), (4, 4, 36), 'music local category')
    common.require(common.d_fields(app, 0xbc150, 14), (5, 0, 2), 'music catalog ID')
    common.require(common.call_target(app, 0xbc174), 0xbb4fc, 'music submit')
    for at, target in ((0x1c1c40, 0xbc144), (0x1c246c, 0xbc1d4), (0x1c2994, 0xbc1a4)):
        common.require(common.call_target(app, at), target, 'main local music call')
    common.require(common.call_target(app, 0xbc188), 0xbaf70, 'local music parameter wrapper')
    common.require(common.d_fields(app, 0xbc180, 14), (5, 0, 0), 'music control argument')
    for at, code in [(0x162d8, 24), (0x1642c, 20), (0x165fc, 42)]:
        common.require(common.d_fields(sound, at, 7)[2], code, 'packed catalog stride')
    common.require(common.d_fields(sound, 0x16884, 40), (11, 6, 14), 'sample trailing field endian conversion')
    common.require(common.d_fields(sound, 0xf5d0, 40), (4, 5, 12), 'placeholder selects sample bank')
    common.require(common.d_fields(sound, 0xf5e8, 32), (4, 4, 0), 'placeholder selects sample ID')
    common.require(common.d_fields(sound, 0x6fa4, 14), (0, 4, -1), 'bank sample position is one-based')
    common.require(common.d_fields(sound, 0x15194, 7), (0, 3, 11), 'bank map record stride')
    event_table = common.toc_pointer(app, 0x9510)
    common.require(event_table, 0x1e0f4, 'advisor CMsgEvent switch')
    common.require(common.toc_pointer(app, 0x116530), 0x408a4, 'CMsgEvent constructor RTTI table')
    common.require(common.d_fields(app, 0x116540, 36), (4, 3, 8), 'CMsgEvent event field')
    common.require(common.d_fields(app, 0xad40, 32), (4, 29, 8), 'advisor receives CMsgEvent field')
    common.require(common.call_target(app, 0xad48), 0x94dc, 'advisor CMsgEvent handler')
    common.require(common.d_fields(app, 0x9708, 14), (4, 0, 323), 'mode two extra advice')
    common.require(common.call_target(app, 0x9720), 0xb6d8, 'mode two pending record constructor')
    triggers = [(0, 0x9548, 0x9530, 0), (2, 0x98d4, 0x98bc, 106),
                (3, 0x9a88, 0x9a70, 128), (4, 0x9c3c, 0x9c24, 129)]
    rows = controller.descriptors(app)
    responses = {row[0]: row for row in [struct.unpack_from('>8i', app.data_section.data, 0x18ff4 + i * 32) for i in range(610)]}
    trigger_rows = []
    for event, call, literal, message in triggers:
        common.require(common.d_fields(app, literal, 14), (4, 0, message), 'automatic advisor message ID')
        common.require(common.call_target(app, call), 0xb6d8, 'automatic advisor pending record constructor')
        first, count = rows[message][8:10]
        mapped = [responses[i] for i in range(first, first + count)]
        trigger_rows.append({'event': event, 'message': message, 'call': call,
                             'responses': [{'response': row[0], 'sample': row[1], 'lip': row[2],
                                            'bank': 'local' if row[4] >> 16 else 'global'} for row in mapped]})
    producers = [(0xcc464, 0xcc460, 2), (0x108fd4, 0x108fd0, 3), (0x109118, 0x109114, 4),
                 (0x104d2c, 0x104d28, 0), (0x1c2108, 0x1c2104, 10), (0x1c2174, 0x1c216c, 0)]
    for call, literal, identifier in producers:
        common.require(common.call_target(app, call), 0x116528, 'CMsgEvent producer constructor')
        common.require(common.d_fields(app, literal, 14), (4, 0, identifier), 'CMsgEvent producer ID')
    ranges = [(app, 'app', 0xae930, 0xaef0c), (app, 'app', 0xb5c5c, 0xb5da4),
              (app, 'app', 0xbc6f0, 0xbc9a0), (sound, 'sound', 0x162bc, 0x16cb0)]
    return {'identities': common.IDENTITIES, 'event_table': table,
            'sound_category_routes': [{'type': type_, 'category': name, 'handle_field': offset,
                                       'load': at, 'submit': call} for type_, at, offset, name, call in routes],
            'custom_bank_type': 10, 'automatic_advisor_triggers': trigger_rows,
            'event_producers': [{'call': call, 'literal': literal, 'event': id_} for call, literal, id_ in producers],
            'range_hashes': {f'{binary}:{lo:#x}-{hi:#x}': hashlib.sha256(c.code.data[lo:hi]).hexdigest()
                             for c, binary, lo, hi in ranges},
            'limitations': ['RSE sound IDs and CMsgEvent IDs are separate namespaces.',
                            'Catalog records do not prove complete scheduling, weighting or callback policy.',
                            'Mac code and supplied Windows baseline assets remain distinct evidence sources.']}


def inspect_assets(root: Path, event_map_report: Path | None = None):
    summaries = []
    for path in sorted(root.rglob('*')):
        if path.name.lower().endswith('sfx.map'):
            data = path.read_bytes()
            common.require(hashlib.sha256(data[:16]).hexdigest(),
                           'aaf5481c582d22620874ee0ae86b5fa90fffd9feffe31906a82ee0fb52c0f8ac',
                           'selected SFX catalog header identity')
            categories = catalog(data)
            summaries.append({'path': str(path.relative_to(root)), 'sha256': hashlib.sha256(data).hexdigest(),
                              'categories': len(categories), 'catalog_ids': sum(len(row['sounds']) for row in categories)})
    selected = root / 'levels/fantasy/Music/cat_musicSFX.map'
    music = catalog(selected.read_bytes())
    paths = banks((root / 'levels/fantasy/Music/cat_musicBANK.map').read_bytes())
    common.require(paths, ['Music\\Music'], 'selected music bank family')
    common.require([row['catalog_id'] for row in music[0]['sounds']], [2], 'selected local music catalog ID')
    samples = [sample for event in music[0]['sounds'][0]['elements'] for sample in event['samples']]
    common.require({sample['bank_id'] for sample in samples}, {1}, 'selected music bank references')
    entries = common.sdt_entries((root / 'levels/fantasy/Music/MusicHD.sdt').read_bytes())
    if any(sample['sample_id'] < 1 or sample['sample_id'] > len(entries) for sample in samples):
        raise common.pef.PEFError('selected music sample outside supplied bank')
    result = {'catalogs': summaries, 'selected_music': {'catalog_id': 2, 'bank_family': paths[0],
            'sample_references': len(samples), 'distinct_sample_ids': len({sample['sample_id'] for sample in samples}),
            'first_sample': entries[samples[0]['sample_id'] - 1],
            'catalog_sha256': hashlib.sha256(selected.read_bytes()).hexdigest(),
            'bank_map_sha256': hashlib.sha256((root / 'levels/fantasy/Music/cat_musicBANK.map').read_bytes()).hexdigest(),
            'sdt_sha256': hashlib.sha256((root / 'levels/fantasy/Music/MusicHD.sdt').read_bytes()).hexdigest()}}
    if event_map_report:
        reports = json.loads(event_map_report.read_text())
        common.require(len(reports), 28, 'supplied parsed baseline EventMap count')
        selected = [('fantasy', 'b_drip', '7a9f27fc9985447fe10218f7f6936f2555ae4362c72cf38259aeb15c832806a8'),
                    ('hallow', 'c_hade', 'a95869817d3bfa2bc0a667902b7a9e476501e8936cb948db7949b01beaaf85a7'),
                    ('jungle', 'coaster1', 'c2d33b651803bc1429b806085e03b78c4f9146246eec5d0160826c265ae0d501')]
        bindings = []
        for theme, archive, digest in selected:
            report = next(row for row in reports if row['Wad'] == f'levels/{theme}/rides/{archive}.wad')
            common.require(report['Sha256'], digest, 'selected baseline EventMap member identity')
            assignment = next(row for row in report['Assignments'] if row['Name'] == 'VAR_EVT0' and row['Index'] == 0)
            catalog_path = root / f'levels/{theme}/Sound/cat_ridesSFX.map'
            choices = catalog(catalog_path.read_bytes())
            row = next(row for category in choices for row in category['sounds'] if row['catalog_id'] == assignment['Value'])
            samples = [sample for element in row['elements'] for sample in element['samples']]
            bindings.append({'theme': theme, 'archive': archive, 'variable_index': 0,
                             'catalog_id': row['catalog_id'], 'elements': len(row['elements']),
                             'sample_choices': len(samples), 'bank_ids_requiring_remap': sorted({sample['bank_id'] for sample in samples}),
                             'map_sha256': hashlib.sha256(catalog_path.read_bytes()).hexdigest(),
                             'script_sha256': digest})
        result['selected_event_map_catalog_bindings'] = bindings
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--assets', type=Path)
    parser.add_argument('--event-maps', type=Path, help='external interpreted report from AudioEventAssets')
    args = parser.parse_args()
    try:
        result = native(args.bin_root)
        if args.assets:
            result['assets'] = inspect_assets(args.assets, args.event_maps)
    except (OSError, ValueError, common.pef.PEFError, struct.error) as error:
        parser.error(str(error))
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()
