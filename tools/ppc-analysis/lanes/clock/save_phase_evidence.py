"""One identified PC save's manager-phase candidate, not a production locator.

Only interpreted fields/hashes are emitted. Native Mac serialization explains
field correspondence; this does not prove Windows execution or reconstruct the
script list. Container rules agree with SaveReader at root9e40f52.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import struct
import zlib

SAVE_SHA = '6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a'
PAYLOAD_SHA = 'a3c9a28252c37ad49a8eb78e4a0c5e1d5229d01548fa35801db67015d2589173'
SAVE_LENGTH = 38479
PAYLOAD_LENGTH = 1608309
CANDIDATE_OFFSET = 1595542


def require(actual, expected, label):
    if actual != expected:
        raise ValueError(f'{label}: unexpected interpreted value {actual!r}')


def manager_header(payload: bytes, offset: int) -> dict:
    """Interpret just the marker, declared20-byte header and its five words."""
    if offset < 0 or offset + 28 > len(payload):
        raise ValueError('truncated manager header candidate')
    require(payload[offset:offset + 4], b'RSSE', 'manager marker')
    require(struct.unpack_from('<I', payload, offset + 4)[0], 20, 'manager header length')
    names = ('initialized_word', 'pass_counter', 'next_script_id', 'list_count', 'opaque_head_reference')
    return dict(zip(names, struct.unpack_from('<5I', payload, offset + 8)))


def inspect(path: Path) -> dict:
    require(path.stat().st_size, SAVE_LENGTH, 'identified save size')
    raw = path.read_bytes()
    require(hashlib.sha256(raw).hexdigest(), SAVE_SHA, 'identified save SHA256')
    require(raw[0x608], 133, 'offline save version')
    require(raw[0x609], 0, 'offline save flag')
    require(raw[0x60d:0x611], b'BILZ', 'save compression marker')
    declared, chunk_length = struct.unpack_from('<II', raw, 0x611)
    require(declared, PAYLOAD_LENGTH, 'declared bounded payload size')
    require(chunk_length, SAVE_LENGTH - 0x60d, 'compressed chunk length')
    decoder = zlib.decompressobj()
    payload = decoder.decompress(raw[0x629:], PAYLOAD_LENGTH + 1)
    require(len(payload), PAYLOAD_LENGTH, 'decoded payload size')
    if not decoder.eof or decoder.unused_data or decoder.unconsumed_tail:
        raise ValueError('truncated, oversized or trailing compressed stream')
    require(hashlib.sha256(payload).hexdigest(), PAYLOAD_SHA, 'identified decoded payload SHA256')
    matches = []
    position = 0
    while (position := payload.find(b'RSSE', position)) != -1:
        if position + 28 <= len(payload) and struct.unpack_from('<I', payload, position + 4)[0] == 20:
            matches.append(position)
        position += 4
    require(matches, [CANDIDATE_OFFSET], 'unique matching manager candidate')
    fields = manager_header(payload, CANDIDATE_OFFSET)
    require(tuple(fields.values()), (1, 6055, 16, 14, 80650884), 'identified manager state fields')
    return {'save_sha256': SAVE_SHA, 'payload_sha256': PAYLOAD_SHA,
            'decoded_length': PAYLOAD_LENGTH, 'candidate_offset': CANDIDATE_OFFSET,
            'manager_fields': fields, 'limits': 'One identified fixture, no general save locator or list reconstruction.',
            'qualification': 'PC asset structure corroborates Mac field mapping; Windows runtime cadence remains unproved.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('save_path', type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.save_path), indent=2, sort_keys=True))
    except (OSError, ValueError, zlib.error) as error:
        parser.exit(1, f'save phase evidence: {error}\n')
