"""Bounded saved-script framing and ID graph; no bytecode execution or live restore.

Interprets an explicit manager offset. Native serialized pointer tokens are never
dereferenced. Strict graph/count/size rejection is evidence-tool policy, not the
original reader's malformed-input behavior. Opaque payload bytes are not emitted.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import struct

import save_phase_evidence as phase

MAX_PAYLOAD = 8 * 1024 * 1024
MAX_SCRIPTS = 1024
MAX_BLOB = 1024 * 1024
# name, fixed-header count/size field, element width, wire length is record count
BLOCKS = [('code_words', 80, 4, False), ('label_words', 84, 4, False),
          ('variable_words', 140, 4, False), ('literal_bytes', 144, 1, False),
          ('metadata8', 88, 8, False), ('metadata16', 100, 16, False),
          ('metadata32', 124, 32, True), ('auxiliary_words', 76, 4, False),
          ('name_bytes', None, 1, False)]


class Reader:
    def __init__(self, payload, offset):
        if len(payload) > MAX_PAYLOAD or offset < 0 or offset > len(payload):
            raise ValueError('payload/offset outside bounded witness domain')
        self.payload = payload
        self.position = offset

    def span(self, size):
        if size < 0 or size > MAX_BLOB or self.position + size > len(self.payload):
            raise ValueError('truncated or oversized declared record span')
        start = self.position
        self.position += size
        return start

    def word(self):
        return struct.unpack_from('<I', self.payload, self.span(4))[0]

    def marker(self, expected):
        start = self.span(4)
        phase.require(self.payload[start:start + 4], expected, 'script framing marker')


def read_graph(payload: bytes, offset: int) -> dict:
    header = phase.manager_header(payload, offset)
    if header['initialized_word'] == 0:
        raise ValueError('only initialized-manager framing is qualified')
    reader = Reader(payload, offset + 28)
    for _ in range(5):
        reader.marker(b'PAD_')
    count, fixed_size = reader.word(), reader.word()
    if count > MAX_SCRIPTS:
        raise ValueError('declared script count exceeds1024 witness limit')
    phase.require(fixed_size, 244, 'fixed script record width')
    records = []
    for _ in range(count):
        start = reader.span(fixed_size)
        def field(relative, kind='I'):
            return struct.unpack_from('<' + kind, payload, start + relative)[0]
        record = {'fixed_header_offset': start, 'script_id': field(8),
                  'saved_next_token': field(0), 'saved_previous_token': field(4),
                  'child_id': field(12), 'parent_id': field(16), 'secondary_script_id': field(20),
                  'program_word_index': field(60, 'i'), 'slice_budget': field(148, 'i'),
                  'wait_deadline': field(160), 'animation_wait_deadline': field(164),
                  'phase_override': field(184, 'B'), 'speed_bias': field(192, 'h'),
                  'timer_deadline': field(196), 'blocks': {}}
        for name, source, width, is_count in BLOCKS:
            declared = reader.word()
            byte_size = declared * width if is_count else declared
            if source is not None:
                phase.require(byte_size, field(source) * width, f'{name} fixed/wire size correspondence')
            block_offset = reader.span(byte_size)
            record['blocks'][name] = {'offset': block_offset, 'bytes': byte_size,
                                     'element_width': width, 'wire_is_count': is_count}
        reader.marker(b'OBJ ')
        object_count, object_size = reader.word(), reader.word()
        phase.require(object_size, 28, 'object-binding record width')
        record['object_bindings'] = {'offset': reader.span(object_count * object_size),
                                     'count': object_count, 'record_bytes': object_size}
        records.append(record)
    # Source ignores saved next/previous pointers and rebuilds them by head insertion.
    # Validate their serialized consistency for this supported witness contract only.
    token = header['opaque_head_reference']
    previous = 0
    tokens = set()
    ids = {}
    for record in records:
        if token == 0 or token in tokens:
            raise ValueError('null/cyclic serialized list token')
        tokens.add(token)
        phase.require(record['saved_previous_token'], previous, 'serialized previous-token chain')
        identifier = record['script_id']
        if identifier == 0 or identifier in ids:
            raise ValueError('null/duplicate script ID outside supported graph')
        ids[identifier] = record
        previous, token = token, record['saved_next_token']
    phase.require(token, 0, 'serialized list terminates at declared count')
    # IDs resolve through the global manager list; no serialized address translation.
    for record in records:
        for role in ('child_id', 'parent_id', 'secondary_script_id'):
            reference = record[role]
            if reference and reference not in ids:
                raise ValueError(f'unresolved {role} in bounded script graph')
    serialized_order = [record['script_id'] for record in records]
    restored_order = list(reversed(serialized_order))
    edges = [{'from': record['script_id'], 'role': role, 'to': record[role]}
             for record in records for role in ('child_id', 'parent_id', 'secondary_script_id') if record[role]]
    return {'manager_header': header, 'manager_offset': offset, 'manager_end_offset': reader.position,
            'serialized_order': serialized_order, 'restored_order': restored_order,
            'id_reference_edges': edges, 'records': records,
            'source_reader_counter_after_insertions': (header['list_count'] + count) & 0xffffffff,
            'qualification': 'Static framing/ID projection; no code/payload emission or native execution. Counter arithmetic excludes untraced callee mutation.',
            'limits': 'Explicit manager offset,0..1024 scripts,1MiB individual blocks,8MiB payload; invalid graph rejection is tool policy.'}


def inspect(path: Path) -> dict:
    # Reuse the pinned container validation; decompress only this identified fixture.
    phase.inspect(path)
    import hashlib
    import zlib
    raw = path.read_bytes()
    decoder = zlib.decompressobj()
    payload = decoder.decompress(raw[0x629:], phase.PAYLOAD_LENGTH + 1)
    phase.require(hashlib.sha256(payload).hexdigest(), phase.PAYLOAD_SHA, 'graph decoded payload identity')
    result = read_graph(payload, phase.CANDIDATE_OFFSET)
    phase.require(result['serialized_order'], [15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 4, 3, 2, 1], 'identified serialized IDs')
    phase.require(result['manager_end_offset'], 1606398, 'identified manager block endpoint')
    phase.require(result['id_reference_edges'], [], 'identified fixture has no script-ID edges')
    return {'save_sha256': phase.SAVE_SHA, 'payload_sha256': phase.PAYLOAD_SHA, **result}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('save_path', type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.save_path), indent=2, sort_keys=True))
    except (OSError, ValueError) as error:
        parser.exit(1, f'saved script graph: {error}\n')
