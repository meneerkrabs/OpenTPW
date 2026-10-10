"""Static advisor descriptor/callback/property bindings and scheduling witnesses.

No original code is executed. Detailed JSON is interpreted metadata and should
remain outside Git, alongside the locally supplied proprietary corpus.
"""
from __future__ import annotations

import argparse
from collections import Counter
import json
import hashlib
import re
from pathlib import Path
import struct

import evidence as common


def descriptors(c):
    base = common.toc_pointer(c, 0x148a8)
    common.require(base, 0x1f2b4, 'scored-message descriptor table')
    rows = [struct.unpack_from('>12I', c.data_section.data, base + 48 * i) for i in range(351)]
    common.require([r[1] for r in rows], list(range(351)), 'descriptor IDs')
    common.require({r[10] for r in rows}, {2}, 'shipped cyclic variant modes')
    return rows


def callback_bindings(c):
    """Track only proven straight-line load/store reference provenance.

    Restrict the initializer copy body to lwz/stw. Values are represented by
    their source data addresses; no arithmetic or original calls are run.
    """
    regs = {31: ('pointer', 0x1f2b4)}
    copied = {}
    for at in range(0x148d4, 0x16f38, 4):
        word = common.pef._u32(c.code.data, at)
        op = word >> 26
        rd, ra, offset = word >> 21 & 31, word >> 16 & 31, word & 0xffff
        if offset & 0x8000:
            offset -= 0x10000
        if op == 32:
            if ra == 2:
                pointer = common.data_pointer(c, 0x8000 + offset, c.data_section.index)
                regs[rd] = ('pointer', pointer)
            else:
                base = regs.get(ra)
                if not base or base[0] != 'pointer':
                    raise common.pef.PEFError(f'copy initializer unknown base at {at:#x}')
                regs[rd] = ('source', base[1] + offset)
        elif op == 36:
            base, value = regs.get(ra), regs.get(rd)
            if not base or base != ('pointer', 0x1f2b4) or not value or value[0] != 'source':
                raise common.pef.PEFError(f'copy initializer unknown store at {at:#x}')
            copied[offset] = {'source': value[1], 'store': at}
        else:
            raise common.pef.PEFError(f'copy initializer unexpected operation at {at:#x}')
    common.require(len(copied), 351 * 3, 'three copied member-pointer words per message')
    callbacks = []
    for message in range(351):
        source = 0x1e240 + message * 12
        for part in range(3):
            common.require(copied[48 * message + 16 + part * 4]['source'], source + part * 4,
                           f'callback copy for message {message}')
        common.require(struct.unpack_from('>ii', c.data_section.data, source), (0, -1),
                       'direct member callback adjustment/virtual-slot metadata')
        transition = common.data_pointer(c, source + 8, c.data_section.index)
        entry = common.data_pointer(c, transition, c.code.index)
        toc = common.data_pointer(c, transition + 4, c.data_section.index)
        common.require(toc, 0x8000, 'score callback TOC')
        callbacks.append({'code': entry, 'transition': transition,
                          'binding_store': copied[48 * message + 24]['store']})
    return callbacks


def property_bindings(c):
    """Derived layout of the reviewed schema constructor, including its array."""
    table = common.toc_pointer(c, 0xbee8)
    common.require(table, 0x2349c, 'advisor property schema')
    rows = []
    for index in range(1000):
        off = table + index * 60
        type_ = common.pef._u32(c.data_section.data, off)
        if type_ == 12:
            break
        raw_name = c.data_section.data[off + 4:off + 36]
        name = raw_name.split(b'\0')[0].decode('ascii')
        rows.append((type_, name, common.pef._u32(c.data_section.data, off + 52)))
    else:
        raise common.pef.PEFError('property schema terminator missing')
    common.require(index, 774, 'property schema length')
    field = 1
    pending = []
    width = count = 0
    bindings = {}
    for index, (type_, name, size) in enumerate(rows):
        if type_ == 2:
            close = index + 1
            while close < len(rows) and rows[close][0] != 3:
                close += 1
            if close == len(rows):
                raise common.pef.PEFError('property array has no closer')
            width, count = close - index - 1, rows[close][2]
            common.require((width, count), (3, 10), 'selected advisor message-group array')
        elif type_ == 3:
            for key, offset, origin in pending:
                for group in range(count):
                    bindings[offset + group * width * 4] = {'key': f'{name}[{group}].{key}',
                                                           'schema_index': origin}
            pending = []
            field += (count - 1) * width + 1
        elif type_ == 1:
            for key, offset, origin in pending:
                bindings[offset] = {'key': f'{name}.{key}', 'schema_index': origin}
            pending = []
        elif type_ == 0:
            pass
        elif 4 <= type_ <= 9:
            pending.append((name, 20 + field * 4, index))
            field += 1
        else:
            raise common.pef.PEFError('unexpected advisor property schema kind')
    common.require(bindings[160]['key'], 'Welcome.Score', 'score-property field meaning')
    common.require(bindings[32]['key'], 'GeneralAdvisor.MinScoreForConsideration', 'minimum-score field meaning')
    common.require(bindings[36]['key'], 'MessageGroups[0].MinTimeSameMessage', 'group repeat field meaning')
    common.require(bindings[1584]['key'], 'WaitingTimes.DaysForStaff', 'last property field meaning')
    return bindings


def literal_score(c, entry: int, bindings: dict):
    # Only recognize a complete two-instruction field getter/constant function.
    first = common.pef._u32(c.code.data, entry)
    second = common.pef._u32(c.code.data, entry + 4)
    if (second >> 26, second >> 1 & 1023, second >> 21 & 31, second >> 16 & 31) != (19, 16, 20, 0):
        return {'kind': 'conditional_or_computed', 'code': entry}
    op, rd, ra, immediate = first >> 26, first >> 21 & 31, first >> 16 & 31, first & 0xffff
    if op == 32 and (rd, ra) == (3, 3) and immediate in bindings:
        return {'kind': 'configured_field', 'field': immediate, 'key': bindings[immediate]['key']}
    if op == 14 and (rd, ra) == (3, 0):
        if immediate & 0x8000:
            immediate -= 0x10000
        return {'kind': 'constant', 'value': immediate}
    return {'kind': 'conditional_or_computed', 'code': entry}


def scheduling(c):
    common.require(common.call_target(c, 0x87c8), 0xa130, 'active-action gate')
    common.require(common.conditional_branch(c, 0x87d0), (4, 2, 0x8afc), 'active-action early exit')
    common.require(common.d_fields(c, 0xa14c, 32), (4, 31, 216), 'last action start')
    common.require(common.d_fields(c, 0xa150, 32), (0, 31, 220), 'last action duration')
    common.require(common.conditional_branch(c, 0xa15c), (4, 0, 0xa168), 'busy only while now is below endpoint')
    common.require(common.call_target(c, 0x88a8), 0xd5ec, 'response count accessor')
    common.require(common.call_target(c, 0x88e4), 0xdc44, 'variant mode accessor')
    common.require(common.d_fields(c, 0x8974, 32), (3, 3, 228), 'last response variant history')
    common.require(common.d_fields(c, 0x8978, 14), (29, 3, 1), 'cyclic next variant')
    common.require(common.conditional_branch(c, 0x89dc), (12, 0, 0x89e4), 'keep variant only below count')
    common.require(common.d_fields(c, 0x89e0, 14), (29, 0, 0), 'cycle resets variant to zero')
    common.require(common.conditional_branch(c, 0x878c), (4, 1, 0x87c4), 'automatic score must exceed minimum')
    common.require(common.conditional_branch(c, 0x8850), (4, 1, 0x8ac8), 'queued score must exceed minimum')
    common.require(common.call_target(c, 0x8a14), 0xa1e4, 'action timing setter')
    common.require(common.d_fields(c, 0x8a10, 14), (4, 4, 1000), 'action reservation margin')
    common.require(common.d_fields(c, 0xa208, 36), (3, 30, 216), 'action start time store')
    common.require(common.d_fields(c, 0xa20c, 36), (31, 30, 220), 'action reserved duration store')
    common.require(common.rotate_fields(c, 0x12111c), (4, 3, 30, 2, 31), 'saved history counter divide by four')
    common.require(common.rotate_fields(c, 0x121120), (0, 0, 30, 2, 31), 'live history counter divide by four')
    common.require(common.call_target(c, 0x12110c), 0x10a9a4, 'history world getter')
    common.require(common.conditional_branch(c, 0x754c), (12, 1, 0x7594), 'pending speech waits only if deadline is greater')
    common.require(common.d_fields(c, 0x6ec0, 13), (18, 3, 200), 'length plus lead margin')
    common.require(common.d_fields(c, 0x6ef8, 14), (3, 18, 300), 'animation budget extra margin')
    common.require(common.d_fields(c, 0x7048, 14), (3, 3, 1000), 'returned action margin')
    return {'active_action_preemption': 'controller waits while now < start + reserved duration',
            'minimum_score_operator': 'strictly greater', 'history_counter_field': 0x1da70c,
            'history_expression': '(live_game_tick >> 2) - (saved_game_tick >> 2)',
            'pending_speech_operator': 'now >= pending deadline',
            'speech_pending_start_clock_units_from_request': 800,
            'lip_start_clock_units_immediate': -200,
            'lip_start_clock_units_deferred': 800,
            'extra_controller_reservation_clock_units': 1000}


def queue_rule_edges(c):
    common.require(common.call_target(c, 0x8c38), 0x92ec, 'first free slot scan before any score eviction comparison')
    common.require(common.conditional_branch(c, 0x8c40), (12, 0, 0x8c84), 'full queue only if no free slot')
    common.require(common.d_fields(c, 0x8d10, 14), (3, 0, 1), 'full queue eligible acknowledgement even without replacement')
    common.require(common.x_fields(c, 0xa158, 32), (0, 3, 0), 'UNSIGNED active-action endpoint comparison')
    common.require(common.x_fields(c, 0xa154, 266), (0, 4, 0), 'plain wrapping endpoint add')
    common.require(common.x_fields(c, 0x121124, 40), (3, 3, 0), 'plain wrapping difference of shifted history counters')
    common.require(common.rotate_fields(c, 0x1210f0), (0, 3, 30, 2, 31), 'saved-quarter history sentinel')
    common.require(common.d_fields(c, 0x90a0, 10), (0, 3, 0), 'unsigned saved-quarter zero check')
    common.require(common.conditional_branch(c, 0x90a4), (12, 2, 0x90fc), 'zero saved quarter skips repeat gate')
    common.require(common.x_fields(c, 0x90ec, 32), (0, 3, 22), 'UNSIGNED repeat interval comparison')
    common.require(common.conditional_branch(c, 0x90f0), (4, 0, 0x90fc), 'repeat equality passes')
    common.require(common.conditional_branch(c, 0x9100), (4, 2, 0x915c), 'override skips once-only check')
    common.require(common.conditional_branch(c, 0x9160), (4, 2, 0x91b4), 'override skips slap check')
    common.require(common.x_fields(c, 0xe0fc, 0), (0, 31, 0), 'SIGNED slap-count threshold comparison')
    common.require(common.x_fields(c, 0x9230, 0), (0, 23, 0), 'pending duplicate count comparison')
    common.require(common.conditional_branch(c, 0x9234), (12, 0, 0x9240), 'duplicates below maximum only')
    common.require(common.x_fields(c, 0x89d8, 0), (0, 29, 3), 'SIGNED cyclic variant bound after wrapping increment')
    common.require(common.d_fields(c, 0xb7d8, 38), (0, 30, 20), 'playback consumes pending record before score revalidation')
    common.require(common.x_fields(c, 0xb854, 0), (0, 3, 0), 'SIGNED recomputed playback score')
    common.require(common.conditional_branch(c, 0xb858), (4, 0, 0xb864), 'playback score equality passes revalidation')
    common.require(common.d_fields(c, 0x8a3c, 36), (29, 3, 228), 'history stores controller-requested variant without playback fallback normalization')
    common.require(common.x_fields(c, 0xb8c8, 0), (0, 24, 3), 'playback compares requested variant with count')
    common.require(common.conditional_branch(c, 0xb8cc), (12, 0, 0xb910), 'variant below count adds to first response')
    common.require(common.call_target(c, 0xb904), 0xd468, 'out-of-range explicit variant selects first response')
    common.require(common.d_fields(c, 0x8a4c, 38), (4, 3, 232), 'successful playback marks history as played')
    return {'admission_has_minimum_score_gate': False,
            'full_queue_equal_or_lower_score': 'eligible acknowledgement, no replacement',
            'busy_comparison': 'unsigned now < unchecked(start + duration)',
            'repeat_zero_sentinel': '(saved_game_tick >> 2) == 0 skips interval gate',
            'repeat_comparison': 'unsigned elapsed >= interval passes',
            'override_only_once': 'bypasses once and slap checks; repeat/tutorial/duplicate gates still apply',
            'slap_comparison': 'signed bit-pattern comparison against nonzero limit',
            'playback_score_revalidation': 'pending consumed first; recomputed score >= minimum passes',
            'variant_increment': 'unchecked signed previous + 1; reset only if value >= count',
            'explicit_variant_fallback': 'playback chooses first for override >= count; history retains requested override',
            'code_range_sha256': {f'{lo:#x}-{hi:#x}': hashlib.sha256(c.code.data[lo:hi]).hexdigest()
                                  for lo, hi in ((0x8b78, 0x8d34), (0x9008, 0x92ec), (0xb798, 0xba08))}}


def derived_scheduler(request: int, length: int, pending: bool, sequence_duration: int, ending_duration: int):
    """Arithmetic witness of reviewed margins, independent of audio amplitude."""
    base = request - 200
    if pending:
        base += 1000
    return {'animation_budget': length + 500, 'lip_base': base,
            'speech_deadline': request + 800 if pending else None,
            'controller_reserved_span': sequence_duration + ending_duration + 2000}


def cyclic_variant(previous: int, count: int):
    if count <= 0:
        raise ValueError('variant count must be positive')
    value = (previous + 1) & 0xFFFFFFFF
    if value & 0x80000000:
        value -= 0x100000000
    return value if value < count else 0


def history_elapsed(current: int, saved: int):
    return ((current >> 2) - (saved >> 2)) & 0xFFFFFFFF


def inspect_geometry(c, root):
    digest = 'c549123f647dcf33c3e2c3d515bffe2cfcb19d11680f743f02ca42bb09dbd73b'
    engine = common.pef.PEFContainer(common.identified(root / 'libraries/engine_shared.data', digest))
    for at, offset in ((0xa1ecc, 0), (0xa1ed0, 20), (0xa1ed4, 40), (0xa1ed8, 60)):
        common.require(common.d_fields(c, at, 52), (0, 31, offset), 'identity matrix diagonal')
    common.require(common.call_target(c, 0x290b8), 0xa1e9c, 'advisor identity matrix initialization')
    common.require(common.d_fields(c, 0x291d8, 14), (5, 28, 8), 'advisor local matrix address')
    common.require(common.d_fields(c, 0x291e4, 36), (5, 3, 200), 'advisor matrix reference retained on actor')
    common.require(common.d_fields(c, 0xa6d18, 32), (4, 27, 200), 'animation caller retrieves retained matrix')
    common.require(common.call_target(c, 0xa6d20), 0x55f54, 'animation geometry setup')
    glue = common.glue_import(c, common.call_target(c, 0x55f84), 0x8000)
    common.require(glue['symbol'], 'SetLocalMatrix__5CMeshFiR7sMatrix', 'actual advisor matrix consumer')
    local = common.vector(engine, glue['symbol'])
    common.require(local['code_offset'], 0x1903c, 'engine local matrix entry')
    common.require(common.d_fields(c, 0x55f6c, 14), (4, 0, 1), 'advisor local matrix flags')
    common.require(common.d_fields(c, 0x291f8, 14), (5, 0, 16), 'advisor MapWho linking selector')
    link = common.vector(engine, 'Link__7CMapWhoFP12CMapLinkBasei')
    common.require(link['code_offset'], 0x13e50, 'MapWho link implementation')
    common.require(common.x_fields(engine, 0x13ef0, 824), (5, 4, 4), 'MapWho selector shift')
    common.require(common.d_fields(engine, 0x13ef8, 14), (0, 4, -1), 'MapWho bucket offset')
    common.require(common.d_fields(engine, 0x13f04, 14), (4, 4, 20), 'MapWho first bucket origin')
    def float_toc(at):
        _, ra, offset = common.d_fields(c, at, 48)
        common.require(ra, 2, 'geometry float TOC base')
        return struct.unpack_from('>f', c.data_section.data, 0x8000 + offset)[0]
    translation = [float_toc(at) for at in (0x290bc, 0x290c8, 0x290d0)]
    scales = [float_toc(0x29208) * float_toc(0x29218), float_toc(0x29210), float_toc(0x29208)]
    return {'engine_identity': digest, 'context_data': 0x559f4, 'context_stride': 188,
            'identity_matrix_relative_offset': 8, 'translation': translation,
            'model_scale': scales, 'matrix_consumer': local,
            'mapwho_selector': 16, 'mapwho_bucket_index': 0,
            'limitation': 'Shared viewport config/global projection path still needs advisor-specific capture and final coordinate interpretation.'}


def synthesis_window(half):
    if len(half) != 257:
        raise ValueError('expected 257 clean-room half-window coefficients')
    result = [0] * 512
    for index, value in enumerate(half):
        result[index] = value
        if 0 < index < 256:
            result[512 - index] = value if index % 64 == 0 else -value
    return result


def original_window_coordinates():
    return [(row, tap, 32 * tap + row) for row in range(17) for tap in range(16)]


def inspect_audio(c, root):
    sound = common.pef.PEFContainer(common.identified(root / 'libraries/sound_shared.data',
                                                    common.IDENTITIES['sound_shared.data']))
    file_retrieve = common.vector(sound, 'RetrieveSample__10TbFileBankFPP14TbSampleHeaderUl')
    memory_retrieve = common.vector(sound, 'RetrieveSample__12TbMMFileBankFPP14TbSampleHeaderUl')
    common.require(file_retrieve['code_offset'], 0x691c, 'file bank retrieval entry')
    common.require(memory_retrieve['code_offset'], 0x6e6c, 'memory bank retrieval entry')
    common.require(common.d_fields(sound, 0x6928, 14), (0, 6, -1), 'file-bank one-based cache index')
    common.require(common.d_fields(sound, 0x6e84, 14), (0, 6, -1), 'memory-bank one-based sample index')
    common.require(common.rotate_fields(sound, 0x6944), (6, 6, 2, 0, 29), 'SDT offset entry uses sample ID times four')
    poly = common.vector(sound, 'PolySynth__9CMpegBaseFPsPf')
    common.require(poly['code_offset'], 0x53a0, 'original synthesis entry')
    common.require(common.d_fields(sound, 0x53bc, 14), (3, 2, 1040), 'original synthesis window base')
    common.require(common.d_fields(sound, 0x545c, 48), (1, 4, 0), 'original synthesis coefficient consumer')
    source = Path(__file__).resolve().parents[4] / 'source/OpenTPW.Files/Formats/Sound/Mp2Decoder.cs'
    text = source.read_text(encoding='utf-8-sig')
    match = re.search(r'private static readonly int\[\] HalfWindow\s*=\s*\{([^}]+)\}', text)
    if match is None:
        raise common.pef.PEFError('clean-room HalfWindow declaration not found')
    half = [int(value) for value in re.findall(r'-?\d+', match.group(1))]
    window = synthesis_window(half)
    original = struct.unpack_from('>544f', sound.data_section.data, 0x8410)
    covered = set()
    for row, tap, index in original_window_coordinates():
        covered.add(min(index, 512 - index))
        expected = -window[index] / 2
        common.require(original[row * 32 + tap], expected, 'original/clean-room synthesis coefficient correspondence')
        common.require(original[row * 32 + tap + 16], expected, 'original duplicated synthesis coefficient')
    common.require(covered, set(range(257)), 'all half-window coefficients covered')
    return {'sound_identity': common.IDENTITIES['sound_shared.data'],
            'sample_ids': 'one-based', 'bank_retrieval': [file_retrieve, memory_retrieve],
            'synthesis': poly, 'coefficient_data_offset': 0x8410,
            'original_coefficient_entries_compared': 544,
            'clean_room_half_window_entries_covered': len(covered),
            'coefficient_relationship': 'original[row*32+tap] = -0.5 * Dint[32*tap+row]; duplicated at +16',
            'coefficient_data_sha256': hashlib.sha256(sound.data_section.data[0x8410:0x8c90]).hexdigest(),
            'limitation': 'Exact table correspondence does not establish full published ISO provenance or complete decoder arithmetic/device equivalence.'}


def inspect(root: Path, detailed=False):
    raw = common.identified(root / 'SimThemePark.data', common.IDENTITIES['SimThemePark.data'])
    c = common.pef.PEFContainer(raw)
    rows, callbacks, properties = descriptors(c), callback_bindings(c), property_bindings(c)
    response_rows = [struct.unpack_from('>8i', c.data_section.data, 0x18ff4 + i * 32) for i in range(610)]
    response_ids = {r[0] for r in response_rows}
    messages = []
    missing = []
    for index, (row, callback) in enumerate(zip(rows, callbacks)):
        responses = list(range(row[8], row[8] + row[9]))
        for id_ in responses:
            if id_ not in response_ids:
                missing.append({'message': index, 'response': id_})
        messages.append({'message': index, 'background_candidate': row[0] == 0,
            'group': row[2], 'game_mode_selector': row[3], 'pending_duplicate_limit': row[7] & 255,
            'response_first': row[8], 'response_count': row[9], 'variant_mode': 'cyclic',
            'score_callback': callback, 'score': literal_score(c, callback['code'], properties)})
    common.require(missing, [{'message': 291, 'response': 551}], 'unresolved descriptor response reference')
    common.require(Counter(m['score']['kind'] for m in messages),
                   Counter(configured_field=170, constant=46, conditional_or_computed=135), 'score callback coverage')
    # Property names and callback addresses are derived; asset values are not substituted.
    result = {'identity': common.IDENTITIES['SimThemePark.data'],
        'scored_message_count': 351, 'response_count': 610,
        'distinct_callback_code_entries': len({item['code'] for item in callbacks}),
        'callback_copy_words_verified': 1053, 'named_property_fields': len(properties),
        'score_kinds': dict(Counter(m['score']['kind'] for m in messages)),
        'background_candidates': sum(m['background_candidate'] for m in messages),
        'all_shipped_variant_modes': 'cyclic', 'missing_response_references': missing,
        'queue_edges': queue_rule_edges(c), 'scheduling': scheduling(c), 'geometry': inspect_geometry(c, root), 'audio': inspect_audio(c, root),
        'selected_messages': [messages[i] for i in (0, 1, 2, 3, 4, 5, 6, 7, 33, 34, 93, 94, 95, 96, 97, 291, 350)],
        'limitations': ['135 message slots require conditional/computed callback analysis.',
                       'Accessor and timer operands do not prove runtime capture or device latency.',
                       'Missing response reference is static; no original execution observed.']}
    if detailed:
        result['messages'] = messages
        result['property_fields'] = {str(off): meaning for off, meaning in sorted(properties.items())}
        result['response_bindings'] = [{'response': r[0], 'sample': r[1], 'lip_number': r[2],
            'sequence': r[3], 'model': r[4] & 65535,
            'bank_selector': 'local' if r[4] >> 16 else 'global',
            'visibility_controls': [r[5], r[6]], 'metadata_lookup_value': r[7]} for r in response_rows]
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--details', action='store_true')
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root, args.details)
    except (OSError, ValueError, struct.error, common.pef.PEFError) as error:
        parser.exit(1, f'advisor controller evidence: {error}\n')
    print(json.dumps(result, sort_keys=True, indent=2))


if __name__ == '__main__':
    main()
