"""Bounded sound selection algebra and identity-pinned native operands.

Inputs are explicit seed snapshots and descriptors. This does not advance an
RNG, schedule events, execute original code, or model an audio device.
"""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import struct

import audio_event_evidence as catalogs
import evidence as common

U32 = 0xffffffff


def candidate(seed: int) -> int:
    return ((seed & U32) * 1664525 + 1013904223) & U32


def runtime_weights(stored: list[int], cumulative: bool) -> list[int]:
    previous = 0
    result = []
    for value in stored:
        value &= U32
        result.append((value - previous) & U32 if cumulative else value)
        previous = value
    return result


def avoid_repeat(index: int, count: int, previous: int | None, enabled: bool):
    # The native temporary/history is signed byte. Bound the witness domain;
    # do not claim the native code rejects larger descriptor arrays.
    if count > 127:
        raise ValueError('signed-byte index domain above 127 is not modelled')
    return (index + 1) % count if enabled and count > 2 and index == previous else index


def choose_sample(thresholds: list[int], seed: int, previous=None, avoid=False):
    if not thresholds:
        return None, previous
    if len(thresholds) == 1:
        return 0, previous
    draw = candidate(seed) >> 16
    for index, threshold in enumerate(thresholds):
        if (threshold & U32) >= draw:
            chosen = avoid_repeat(index, len(thresholds), previous, avoid)
            return chosen, chosen
    return None, previous


def choose_event(weights: list[int], seed: int, previous=None, avoid=False):
    """Return index, new history, whether the selector-refresh path is taken."""
    if not weights:
        raise ValueError('empty native event-array fallback is not modelled')
    if len(weights) == 1:
        return 0, previous, False
    draw = candidate(seed) >> 16
    total = 0
    for index, weight in enumerate(weights):
        total = (total + weight) & U32
        if total >= draw:
            chosen = avoid_repeat(index, len(weights), previous, avoid)
            return chosen, chosen, True
    return 0, previous, False


def choose_branch(links: list[tuple[int, int, int, int]], parameter: int, seed: int):
    """Links: explicit event index, runtime weight, low byte, high byte."""
    eligible = [(index, weight & U32) for index, weight, low, high in links
                if low <= (parameter & U32) <= high]
    if not eligible:
        return None
    total = sum(weight for _, weight in eligible) & U32
    if total == 0:
        raise ValueError('native eligible-weight zero divisor is not modelled')
    draw = candidate(seed) % total
    cumulative = 0
    for index, weight in eligible:
        cumulative = (cumulative + weight) & U32
        if cumulative >= draw:
            return index
    return None


def range_value(low: int, high: int, seed: int, parameter: int | None = None,
                signed=False):
    def byte(value):
        value &= 255
        return value - 256 if signed and value >= 128 else value
    low, high = sorted((byte(low), byte(high)))
    span = high - low
    if parameter is not None:
        return low + ((span * (parameter & U32)) & U32) // 100
    return low + candidate(seed) % span if span else low


def update_parameters(selectors: list[int], values: list[int], code: int, value: int):
    if len(selectors) != 4 or len(values) != 4:
        raise ValueError('native parameter structure has four byte slots')
    result = [item & 255 for item in values]
    dependent = False
    for index, selector in enumerate(selectors):
        if (selector & 255) == (code & 255):
            result[index] = value & 255
            dependent |= index != 0
    return result, dependent


def parameter_value(mask_a: int, mask_b: int, values: list[int], requested: int):
    if mask_a & requested:
        return values[1] & 255
    if mask_b & requested:
        return values[2] & 255
    return None


def pitch_ratio(index: int):
    """Mathematical expression; not a bit-exact MathLib/float/device model."""
    if index == 0:
        return 1.0
    return 2.0 ** ((index + 1) / 96.0) if index > 0 else 2.0 ** ((index - 1) / 96.0)


def native(root: Path):
    c = common.pef.PEFContainer(common.identified(root / 'libraries/sound_shared.data',
                                                common.IDENTITIES['sound_shared.data']))
    exports = {
        'ChooseRandomSample__17CAudioPlaceHolderFb': 0xfcb4,
        'ChooseRandomSound__17CAudioPlaceHolderFb': 0xff40,
        'GetRandomVolume__17CAudioPlaceHolderFv': 0xf32c,
        'GetRandomPitch__17CAudioPlaceHolderFv': 0xf45c,
        'GetParameterValue__17CAudioPlaceHolderFUiRUl': 0xf2e0,
        'UpdateParameter__17CAudioPlaceHolderFUcUi': 0xe9c0,
        'ReadEvents__13TbMapStreamerFPcP10CEventData': 0x165b0,
        'ConvertPitchIndexToFrequency__19TbSoundSystemModuleFi': 0xbbf8,
        'ChooseRandomSound__43CPlaceHolderOneShotBranchingSentenceElementFb': 0x18028,
        'AssignSoundToNextBranch__29CPlaceHolderBranchingSentenceFUl': 0x192f0,
    }
    for name, address in exports.items():
        common.require(common.vector(c, name)['code_offset'], address, name)
    operands = [
        (0x15e60, 32, (0, 4, 16)), (0x15e70, 14, (0, 0, 1)),
        (0x15e78, 14, (0, 0, 0)), (0x15e7c, 38, (0, 3, 60)),
        (0x15fac, 38, (26, 31, 60)), (0x16648, 34, (0, 29, 60)),
        (0x16658, 32, (6, 7, 30)), (0x16660, 36, (0, 7, 30)),
        (0xfd10, 32, (0, 6, 4)), (0xffb8, 32, (0, 5, 30)),
        (0xfd38, 10, (0, 8, 2)), (0x10004, 10, (0, 5, 2)),
        (0xfd44, 34, (0, 3, 81)), (0xfd88, 38, (0, 3, 81)),
        (0x10010, 34, (0, 31, 80)), (0x10058, 38, (0, 31, 80)),
        (0x10130, 36, (6, 31, 8)),
        (0xf354, 34, (3, 3, 12)), (0xf3a0, 34, (3, 3, 13)),
        (0xf484, 34, (5, 3, 14)), (0xf488, 34, (4, 3, 15)),
        (0xf3bc, 14, (4, 0, 1)), (0xf4fc, 14, (4, 0, 2)),
        (0xf2e4, 40, (0, 6, 24)), (0xf304, 40, (0, 6, 28)),
        (0xf2f8, 34, (0, 4, 5)), (0xf318, 34, (0, 4, 6)),
        (0x100d8, 40, (4, 4, 22)), (0x100dc, 38, (4, 3, 1)),
        (0x100e8, 40, (4, 4, 26)), (0x100ec, 38, (4, 3, 2)),
        (0xea60, 34, (0, 6, 0)), (0xea70, 38, (3, 6, 4)),
        (0x180c0, 34, (5, 7, 6)), (0x180cc, 34, (5, 7, 7)),
        (0x180e0, 32, (5, 5, 30)), (0x1815c, 32, (5, 5, 30)),
        (0x19364, 34, (0, 5, 6)), (0x19370, 34, (0, 5, 7)),
        (0x19400, 32, (0, 5, 30)),
        (0x1694c, 40, (7, 5, 4)),
        (0xbc18, 8, (3, 3, 1)), (0xbc5c, 14, (3, 3, 1)),
        (0x9400, 36, (4, 3, 28)), (0x9428, 36, (4, 3, 36)),
        (0x943c, 52, (1, 3, 32)),
        (0x118c8, 14, (4, 2, 17124)), (0x118cc, 36, (3, 4, 0)),
    ]
    for address, op, expected in operands:
        common.require(common.d_fields(c, address, op), expected, f'operand {address:#x}')
    common.require(common.x_fields(c, 0x1665c, 40), (0, 0, 6), 'loaded cumulative minus previous')
    for at, bit, target in [(0xfd18, 0, 0xfd94), (0xffc4, 0, 0x10128),
                            (0x180c8, 1, 0x180e8), (0x180d4, 0, 0x180e8),
                            (0x18168, 0, 0x18188), (0x1940c, 0, 0x19420)]:
        common.require(common.conditional_branch(c, at), (12, bit, target), f'selection boundary {at:#x}')
    for at, expected in [(0xf48c, (5, 0, 0)), (0xf490, (4, 3, 0))]:
        common.require(common.x_fields(c, at, 954), expected, 'signed pitch byte')
    for at, expected in [(0xf420, (0, 3, 30)), (0xf564, (0, 3, 31)),
                         (0x18120, (5, 6, 11)), (0x193c4, (0, 5, 10))]:
        common.require(common.x_fields(c, at, 459), expected, 'unsigned range remainder divisor')
    for at in [0xf3d8, 0xf518]:
        common.require(common.d_fields(c, at, 15)[2], 20972, 'percentage reciprocal high')
    for at in [0xf3e0, 0xf520]:
        common.require(common.d_fields(c, at, 14)[2], -31457, 'percentage reciprocal low')
    common.require(common.call_target(c, 0x3c), 0x118b4, 'static initializer seeds sound state')
    common.require(c.init, (1, 7948), 'PEF initializer vector identity')
    common.require(common.data_pointer(c, 7948, c.code.index), 0x1dfe8, 'PEF initializer code')
    common.require(common.call_target(c, 0x1e020), 0, 'PEF initializer calls static constructor list')
    clock = common.glue_import(c, common.call_target(c, 0x118c0), 0x8000)
    common.require(clock['symbol'], 'LbTime_GetClock__Fv', 'sound seed clock source')
    seed_sites = []
    for address in range(0, len(c.code.data), 4):
        word = common.pef._u32(c.code.data, address)
        if word >> 26 == 14 and word >> 16 & 31 == 2 and word & 65535 == 17124:
            seed_sites.append(address)
    common.require(seed_sites, [0xf404, 0xf548, 0xf764, 0xf988, 0xfce0, 0xff80,
                                0x118c8, 0x180f8, 0x1939c], 'direct seed address consumers')
    for start, end in [(0xf404, 0xf42c), (0xf548, 0xf570), (0xf764, 0xf78c),
                       (0xf988, 0xf9b8), (0xfce0, 0xfd08), (0xff80, 0xffac),
                       (0x180f8, 0x1812c), (0x1939c, 0x193d0)]:
        # The pitch FillSampleInfo variant spills the unchanged seed to stack.
        # No draw block stores its calculated successor into shared state.
        for at in range(start, end, 4):
            word = common.pef._u32(c.code.data, at)
            if word >> 26 == 36:
                common.require(common.d_fields(c, at, 36)[1], 1, 'draw store is stack-only')
    # Verify each draw's multiply/add constants, with no invented seed writeback.
    for hi, lo, add_hi, add_lo in [(0xf408, 0xf40c, 0xf418, 0xf41c),
                                  (0xf54c, 0xf550, 0xf55c, 0xf560),
                                  (0xfce4, 0xfcec, 0xfcfc, 0xfd00),
                                  (0xff84, 0xff8c, 0xffa0, 0xffa4),
                                  (0x180fc, 0x18104, 0x18118, 0x1811c),
                                  (0x193a0, 0x193a8, 0x193bc, 0x193c0)]:
        for at, op, imm in [(hi, 15, 25), (lo, 14, 26125),
                            (add_hi, 15, 15471), (add_lo, 14, -3233)]:
            common.require(common.d_fields(c, at, op)[2], imm, 'LCG constant')
    common.require(common.rotate_fields(c, 0xfd04), (0, 5, 16, 16, 31), 'sample high16 draw')
    common.require(common.rotate_fields(c, 0xffa8), (0, 3, 16, 16, 31), 'event high16 draw')
    common.require(common.rotate_fields(c, 0xea98), (0, 0, 0, 17, 17), 'dependent-update inhibit bit')
    common.require(common.rotate_fields(c, 0xeb38), (0, 0, 0, 21, 21), 'volume update inhibit bit')
    for at, expected in [(0x668, 96.0), (0x678, 1.0)]:
        common.require(struct.unpack_from('>f', c.data_section.data, at)[0], expected, 'pitch float constant')
    common.require(struct.unpack_from('>d', c.data_section.data, 0x670)[0], 2.0, 'pitch pow base')
    common.require(common.glue_import(c, common.call_target(c, 0xbc44), 0x8000)['symbol'], 'pow', 'pitch MathLib function')
    common.require(common.glue_import(c, common.call_target(c, 0xbc88), 0x8000)['symbol'], 'pow', 'pitch MathLib function')
    return {'exports': exports, 'operand_count': len(operands), 'seed_data': 0xc2e4,
            'seed_init': 0x118b4, 'seed_source': clock, 'seed_direct_sites': seed_sites,
            'seed_relocated_alias_slots': [slot for slot, target in c.relocs[c.data_section.index].items()
                if target.kind == 'section' and target.target == c.data_section.index and target.addend == 0xc2e4]}


def inspect_corpus(root: Path):
    counts = Counter()
    selected = []
    hashes = {}
    for path in sorted(root.rglob('*')):
        if not path.is_file() or not path.name.lower().endswith('sfx.map'):
            continue
        raw = path.read_bytes()
        parsed = catalogs.catalog(raw)
        relative = str(path.relative_to(root))
        hashes[relative] = hashlib.sha256(raw).hexdigest()
        cumulative = struct.unpack_from('<I', raw, 16)[0] == 0
        counts['catalogs'] += 1
        counts['cumulative_catalogs'] += cumulative
        for category in parsed:
            for sound in category['sounds']:
                weights = runtime_weights([struct.unpack_from('<I', raw, event['record_offset'] + 30)[0]
                                           for event in sound['elements']], cumulative)
                counts['sounds'] += 1
                payload = sound['elements'][0]['record_offset'] + 42 * len(sound['elements']) if sound['elements'] else 0
                for index, event in enumerate(sound['elements']):
                    off = event['record_offset']
                    low_v, high_v, low_p, high_p = struct.unpack_from('<BBbb', raw, off + 12)
                    code_a, mask_a, code_b, mask_b = struct.unpack_from('<4H', raw, off + 22)
                    counts['events'] += 1
                    counts['samples'] += len(event['samples'])
                    counts['children'] += len(event['child_element_indices_one_based'])
                    payload += 16 * len(event['samples'])
                    links = []
                    for link_index, one_based in enumerate(event['child_element_indices_one_based']):
                        low, high = struct.unpack_from('<BB', raw, payload + 8 * link_index + 6)
                        links.append({'event_index_one_based': one_based, 'runtime_weight': weights[one_based - 1],
                                      'parameter_range': [low, high]})
                        counts['child_variable_ranges'] += low != high
                    payload += 8 * len(links)
                    counts['variable_volume'] += low_v != high_v
                    counts['variable_pitch'] += low_p != high_p
                    counts['volume_parameter_masks'] += bool((mask_a | mask_b) & 1)
                    counts['pitch_parameter_masks'] += bool((mask_a | mask_b) & 2)
                    if len(event['samples']) > 1 and event['samples'][-1]['threshold_raw'] < 65535:
                        counts['sample_arrays_below_full_draw_domain'] += 1
                    thresholds = [sample['threshold_raw'] for sample in event['samples']]
                    counts['nonmonotone_sample_threshold_arrays'] += thresholds != sorted(thresholds)
                    if sound['catalog_id'] in (31, 145, 175, 204) and path.name.lower() in ('cat_uisfx.map', 'cat_ridessfx.map'):
                        selected.append({'catalog': relative, 'catalog_id': sound['catalog_id'], 'element': index,
                            'volume': [low_v, high_v], 'pitch_index': [low_p, high_p],
                            'parameter_selectors': [code_a, code_b], 'parameter_masks': [mask_a, mask_b],
                            'runtime_weight': weights[index], 'sample_count': len(event['samples']),
                            'last_sample_threshold': event['samples'][-1]['threshold_raw'] if event['samples'] else None,
                            'branch_links': links})
    return {'counts': dict(counts), 'catalog_sha256': hashes, 'selected': selected}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('binary_root', type=Path)
    parser.add_argument('--pc-data', type=Path)
    parser.add_argument('--mac-data', type=Path)
    args = parser.parse_args()
    report = {'native': native(args.binary_root)}
    for label, root in [('pc', args.pc_data), ('mac', args.mac_data)]:
        if root:
            report[label] = inspect_corpus(root)
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
