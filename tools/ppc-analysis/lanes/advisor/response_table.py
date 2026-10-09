"""Write or check content/data/advisor-responses.toml from the Mac application.

The original advisor looks up a response ID in a table of 32-byte records at
initialized-data offset 0x18FF4 of SimThemePark (sentinel response 9999) and
plays the record's speech sample with its LIP file (playback 0x6BF8-0x6E94,
docs/reverse/PPC-advisor.md). This script copies only the fields OpenTPW uses:
response ID (+0), sample (+4), LIP number (+8), animation (+12), advisor model
(+16 low half) and the local-speech selector (+16 high half). Fields +20, +24
and +28 are left out until their meaning is traced.

Usage: python3 response_table.py <SimThemePark data fork> [--write PATH | --check PATH]
"""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef  # noqa: E402

APP_SHA = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
TABLE = 0x18FF4
RECORD = 32
SENTINEL = 9999
LIMIT = 4096


def records(data: bytes) -> list[tuple[int, int, int, int, int, bool]]:
    rows = []
    offset = TABLE
    while True:
        if offset + RECORD > len(data) or len(rows) > LIMIT:
            raise ValueError('response table has no sentinel')
        response, sample, lip, animation, selector = struct.unpack_from('>iiiiI', data, offset)
        if response == SENTINEL:
            return rows
        rows.append((response, sample, lip, animation, selector & 0xFFFF, (selector >> 16) != 0))
        offset += RECORD


def render(rows) -> str:
    lines = [
        '# Advisor responses of Sim Theme Park (Mac), for interoperability: the speech clip, LIP',
        '# file, animation and speech bank each advisor response plays. Copied field by field from',
        '# the response table at initialized-data offset 0x18FF4 of the application',
        f'# (data fork SHA-256 {APP_SHA}) by',
        '# tools/ppc-analysis/lanes/advisor/response_table.py; see docs/AUDIO.md.',
        '#',
        '# id: response ID the game asks for; sample: 1-based entry of the speech bank;',
        '# lip: N of sp_NNN.lip; animation: advisor sequence (-1 = generated);',
        '# model: advisor model index; local: the level speech bank and Speech/lips instead of global.',
        '',
        'responses = [',
    ]
    for response, sample, lip, animation, model, local in rows:
        lines.append(f'  {{ id = {response}, sample = {sample}, lip = {lip}, animation = {animation}, model = {model}, local = {str(local).lower()} }},')
    lines.append(']')
    return '\n'.join(lines) + '\n'


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('binary', type=Path)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--write', type=Path)
    group.add_argument('--check', type=Path)
    args = parser.parse_args()
    raw = args.binary.read_bytes()
    digest = hashlib.sha256(raw).hexdigest()
    if digest != APP_SHA:
        parser.exit(1, f'unexpected application SHA-256 {digest}\n')
    text = render(records(pef.load(str(args.binary)).data_section.data))
    if args.write:
        args.write.write_text(text, encoding='utf-8')
        print(f'wrote {args.write}')
    elif args.check.read_text(encoding='utf-8') != text:
        parser.exit(1, f'{args.check} differs from the application table\n')
    else:
        print(f'{args.check} matches the application table')


if __name__ == '__main__':
    main()
