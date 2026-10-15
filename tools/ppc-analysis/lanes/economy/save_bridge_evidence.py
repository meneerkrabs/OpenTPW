"""Read metadata of one identified PC save for economy bridge planning.

This is a fixture witness, not a production locator/importer. The calendar
candidate lacks complete preceding-block framing. No state or assets are
written; only independently interpreted field values are printed.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timedelta
import hashlib
import json
from pathlib import Path
import struct
import zlib

SAVE_SHA = '6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a'
PAYLOAD_SHA = 'a3c9a28252c37ad49a8eb78e4a0c5e1d5229d01548fa35801db67015d2589173'
LIMIT = 8 * 1024 * 1024
CHUNK_OFFSET = 0x60d
PAYLOAD_OFFSET = CHUNK_OFFSET + 28
FUNNY_EPOCH = 125911584000000000
RATE = 15000
# Fixed serializer widths verified by staff_evidence.py, not runtime sizes.
PERSON_BYTES = 390
STAFF_BYTES = 105
GUEST_BYTES = 135
RESEARCHER_BYTES = 6


def require(actual, expected, label):
    if actual != expected:
        raise ValueError(f'{label}: expected {expected!r}, got {actual!r}')


def decode_container(raw: bytes, limit=LIMIT) -> bytes:
    if len(raw) > limit or len(raw) < PAYLOAD_OFFSET + 6:
        raise ValueError('container outside bounded input')
    require(struct.unpack_from('<I', raw)[0] in (400, 500), True, 'container magic')
    require(raw[0x608], 133, 'container version')
    require(raw[0x609], 0, 'offline container')
    require(raw[CHUNK_OFFSET:CHUNK_OFFSET + 4], b'BILZ', 'compressed chunk marker')
    length, chunk_length = struct.unpack_from('<II', raw, CHUNK_OFFSET + 4)
    if length > limit:
        raise ValueError('decoded length outside bounded output')
    require(chunk_length, len(raw) - CHUNK_OFFSET, 'chunk length')
    decoder = zlib.decompressobj()
    payload = decoder.decompress(raw[PAYLOAD_OFFSET:], length + 1)
    if len(payload) != length or not decoder.eof or decoder.unused_data or decoder.unconsumed_tail:
        raise ValueError('compressed payload truncated, oversized, or trailing')
    return payload


def world_prefix(payload: bytes) -> dict:
    if len(payload) < 8:
        raise ValueError('truncated action-record prefix')
    published, recording_size = struct.unpack_from('<II', payload)
    if published not in (0, 1):
        raise ValueError('non-boolean action-record flag')
    start = 8 + recording_size
    if start + 64 > len(payload):
        raise ValueError('truncated action recording or world prefix')
    require(struct.unpack_from('<I', payload, start)[0], 2, 'world version')
    result = {'action_record_size': recording_size, 'world_prefix_offset': start}
    for name, relative, kind in [
        ('bank_id', 10, 'H'), ('game_tick', 14, 'I'), ('mechanic_hq_id', 18, 'H'),
        ('analyser_id', 20, 'H'), ('park_closed_word', 22, 'i'),
        ('research_lab_id', 38, 'H'), ('staff_hq_id', 40, 'H'), ('world_state_word', 48, 'i'),
    ]:
        result[name] = {'offset': start + relative, 'value': struct.unpack_from('<' + kind, payload, start + relative)[0]}
    return result


def calendar_at(payload: bytes, offset: int) -> dict:
    if offset < 0 or offset + 28 > len(payload):
        raise ValueError('truncated calendar candidate')
    funny, session, month, day, rate = struct.unpack_from('<QQiiI', payload, offset)
    return {'offset': offset, 'funny_start': funny, 'session_start': session,
            'month_cache': month, 'day_cache': day, 'funny_seconds_per_real_second': rate}


def calendar_candidates(payload: bytes) -> list[dict]:
    if len(payload) > LIMIT:
        raise ValueError('payload outside candidate-search bound')
    needle = struct.pack('<Q', FUNNY_EPOCH)
    candidates = []
    position = 0
    while (position := payload.find(needle, position)) != -1:
        if position + 28 <= len(payload):
            row = calendar_at(payload, position)
            if row['funny_seconds_per_real_second'] == RATE:
                candidates.append(row)
        position += 1
    return candidates


def researcher_prefix(payload: bytes, used_head_offset: int, maximum_records=16) -> dict:
    """Bounded actor-chain prefix for model1/model8 only, with no scanning.

    The caller must qualify used_head_offset. inspect_save uses the formats
    lane's map end for the identified fixture. Unsupported types stop rather
    than inventing a size; this is not a complete World parser.
    """
    if used_head_offset < 0 or used_head_offset + 4 > len(payload) or not 1 <= maximum_records <= 64:
        raise ValueError('invalid actor-prefix bound')
    current = struct.unpack_from('<I', payload, used_head_offset)[0]
    offset = used_head_offset + 4
    seen = set()
    rows = []
    for _ in range(maximum_records):
        if current == 0 or current in seen:
            raise ValueError('researcher absent or cyclic actor prefix')
        seen.add(current)
        if offset + 8 > len(payload):
            raise ValueError('truncated actor header')
        next_id, model = struct.unpack_from('<II', payload, offset)
        if model not in (1, 8):
            raise ValueError('unsupported actor model before researcher')
        body = offset + 8
        size = PERSON_BYTES + (GUEST_BYTES if model == 1 else STAFF_BYTES + RESEARCHER_BYTES)
        if body + size > len(payload):
            raise ValueError('truncated actor body')
        rows.append({'id': current, 'offset': offset, 'next_id': next_id, 'model': model,
                     'body_bytes': size, 'end_exclusive': body + size})
        if model == 8:
            staff = body + PERSON_BYTES
            result = {'actor_prefix': rows, 'actor_id': current, 'header_offset': offset,
                      'body_offset': body, 'end_exclusive': body + size}
            for name, relative, kind in [
                ('grade', 0, 'i'), ('happiness_saved_float', 4, 'f'), ('jobs_done', 8, 'I'),
                ('percentage_byte', 82, 'B'), ('rest_area_id', 83, 'H'), ('staff_state', 85, 'i'),
                ('started_idling_tick', 89, 'I'), ('hired_timestamp', 93, 'Q'),
                ('energy_saved_float', 101, 'f'), ('started_researching_tick', 105, 'I'),
                ('next_researcher_id', 109, 'H'),
            ]:
                result[name] = {'offset': staff + relative,
                                'value': struct.unpack_from('<' + kind, payload, staff + relative)[0]}
            result['inline_name'] = {'offset': staff + 12, 'utf16_code_units': 33,
                                     'sha256': hashlib.sha256(payload[staff + 12:staff + 78]).hexdigest(),
                                     'name_index': 'not stored by the native staff serializer'}
            return result
        current, offset = next_id, body + size
    raise ValueError('researcher outside bounded prefix')


def inspect_save(path: Path) -> dict:
    if path.stat().st_size > LIMIT:
        raise ValueError('save outside fixture input bound')
    raw = path.read_bytes()
    require(hashlib.sha256(raw).hexdigest(), SAVE_SHA, 'identified PC save')
    payload = decode_container(raw)
    require(hashlib.sha256(payload).hexdigest(), PAYLOAD_SHA, 'identified decoded payload')
    world = world_prefix(payload)
    require((world['world_prefix_offset'], world['bank_id']['value'], world['game_tick']['value']),
            (1179, 8, 755), 'identified world prefix')
    rows = calendar_candidates(payload)
    require(len(rows), 1, 'unique epoch/rate candidate in identified fixture')
    calendar = rows[0]
    require(calendar, {'offset': 6719, 'funny_start': FUNNY_EPOCH, 'session_start': 125850128932900000,
                       'month_cache': 1, 'day_cache': 2, 'funny_seconds_per_real_second': RATE},
            'identified calendar candidate')
    funny_seconds = world['game_tick']['value'] * calendar['funny_seconds_per_real_second'] // 4
    # Gregorian host calculation is a cross-check, not execution of the legacy
    # Mac OS conversion routines or proof of the PC runtime calendar equation.
    date = datetime(1601, 1, 1) + timedelta(microseconds=(calendar['funny_start'] + funny_seconds * 10000000) // 10)
    bank_start = 1411362
    bank = {'offset': bank_start}
    for index, name in enumerate(['admission_fee', 'balance', 'batch_balance', 'withdrawals_enabled_word',
                                  'last_balance', 'turn_entered_red', 'profit_this_year']):
        kind = 'I' if name == 'turn_entered_red' else 'i'
        bank[name] = struct.unpack_from('<' + kind, payload, bank_start + 4 * index)[0]
    require([bank[key] for key in list(bank)[1:]], [25, 87987, 0, 1, 87787, 0, -12013], 'reviewed bank fields')
    loans = []
    names = ['available_word', 'amount', 'apr_percent', 'months', 'monthly_repayment',
             'bought_word', 'months_repaid', 'lender_name_index']
    for index in range(8):
        offset = bank_start + 28 + 32 * index
        loans.append({'index': index, 'offset': offset, **dict(zip(names, struct.unpack_from('<8i', payload, offset)))})
    researcher = researcher_prefix(payload, 1385521)
    require((researcher['actor_id'], researcher['header_offset'], researcher['grade']['value'],
             researcher['percentage_byte']['value'], researcher['happiness_saved_float']['value'],
             researcher['energy_saved_float']['value'], researcher['staff_state']['value'],
             researcher['started_researching_tick']['value'], researcher['next_researcher_id']['value']),
            (30, 1391921, 2, 0, 97.0, 93.0, 1, 697, 0), 'framed PC researcher')
    require(struct.unpack_from('<H', payload, 1239)[0], researcher['actor_id'], 'world FirstResearcher ID')
    return {'container_sha256': SAVE_SHA, 'payload_sha256': PAYLOAD_SHA, 'decoded_bytes': len(payload),
            'world': world, 'bank': bank, 'loans': loans,
            'calendar_candidate': calendar, 'mac_equation_host_gregorian_date': date.isoformat(),
            'researcher': researcher,
            'calendar_suffix_context': {'arrival_bytes': 18, 'map_offset_formats_lane': 6765,
                                        'candidate_end_plus_arrival': calendar['offset'] + 28 + 18},
            'limitation': 'One identified byte-identical Mac/PC save. Bank/loans reviewed; world schema matches. '
                          'Calendar candidate preceding blocks unframed; date uses Mac equation and host Gregorian conversion. '
                          'No PC algorithm equivalence, production locator, or simulation state import.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('save', type=Path)
    args = parser.parse_args()
    try:
        result = inspect_save(args.save)
    except (OSError, ValueError, zlib.error) as error:
        parser.exit(1, f'save bridge evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()
