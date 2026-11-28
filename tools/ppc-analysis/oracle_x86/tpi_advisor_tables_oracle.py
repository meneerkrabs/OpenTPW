#!/usr/bin/env python3
"""Verify TPI's advisor response table and Advisor.sam schema against the executable.

The response initializer 0x708BF0 runs in the sandbox and every row of the table it fills at
0xBEF1F0 (32 bytes per row, up to the id 0x270F that ends the lookups of 0x716D10) is compared with
content/data/tpi-advisor-responses.toml (--write-responses writes that file). The scorer's SAM
schema at 0x97C180 (60-byte records up to the end kind 12, which 0x71B6B0 binds through 0x418F60)
is compared with content/data/tpi-advisor-schema.toml (--write-schema writes it). No game, audio or
UI is launched.
"""
import argparse
import re
import struct
from pathlib import Path

from x86_harness import Image, Sandbox, executable_path

RESPONSES = 0xBEF1F0
RESPONSE_ROW = 32
RESPONSE_END = 0x270F
SCHEMA = 0x97C180
SCHEMA_ROW = 60
SCHEMA_END = 12

RESPONSE_PATTERN = re.compile(
    r'^\s*\{ response = (\d+), voice = (\d+), lip = (\d+), animation = (-?\d+), model = 0x([0-9a-f]+), accessory = (-?\d+), '
    r'accessory2 = (-?\d+), tag = (\d+) \},?$')
SCHEMA_PATTERN = re.compile(r'^\s*\{ kind = (\d+), name = "([^"]*)"(?:, minimum = (-?\d+), maximum = (-?\d+))?(?:, count = (\d+))? \},?$')

RESPONSE_HEADER = '''# Advisor responses of Theme Park Inc (no-CD Game.exe, SHA-256
# 77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a), for interoperability: every row of the
# response table at 0xBEF1F0 in table order, as the initializer 0x708BF0 fills it, written by
# tools/ppc-analysis/oracle_x86/tpi_advisor_tables_oracle.py --write-responses; see docs/reverse/TPI-ADVISOR.md.
#
# response: the response id (+0; lookups take the first row of an id, 0x716D10); voice: the speech event (+4);
# lip: the LIP number (+8); animation: -1 for a generated sequence, else the canned row plus 16 (+0xC); model: the
# word +0x10 (low word: the advisor model, high word: the level's speech bank); accessory and accessory2: the
# words +0x14 and +0x18; tag: the caption's tag (+0x1C, 151 = 0x97 for none).

responses = ['''

SCHEMA_HEADER = '''# The Advisor.sam schema of Theme Park Inc's advisor scorer (no-CD Game.exe, SHA-256
# 77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a), for interoperability: every record of the
# schema table at 0x97C180 up to its end record, which the scorer's constructor 0x71B6B0 binds through 0x418F60,
# written by tools/ppc-analysis/oracle_x86/tpi_advisor_tables_oracle.py --write-schema; see docs/reverse/TPI-ADVISOR.md.
#
# kind: 0 struct begin, 1 struct end, 2 array begin, 3 array end, 4 integer, 5 non-negative integer, 6 integer
# range, 7 float, 12 end; name: the key (a struct's or an array's on its end record); minimum and maximum: an
# integer range's bounds (+0x24, +0x28); count: an array's element count (+0x34).

records = ['''


def check(actual, expected, label):
    if actual != expected:
        raise AssertionError(f'{label}: expected {expected!r}, got {actual!r}')


def native_responses(image):
    arena = Sandbox(image, [(0x708BF0, 0x712D1C), (0x715570, 0x7155D9), (0x403CD3, 0x403CD8), (0x407248, 0x40724D),
                            (0x636F70, 0x636FE0), (0x637000, 0x637072)], budget=20_000_000)
    arena.call(0x708BF0)
    rows = []
    address = RESPONSES
    while True:
        words = struct.unpack('<8i', bytes(arena.uc.mem_read(address, RESPONSE_ROW)))
        if words[0] == RESPONSE_END:
            break
        rows.append(words)
        address += RESPONSE_ROW
    return rows


def response_lines(rows):
    return [f'  {{ response = {r[0]}, voice = {r[1]}, lip = {r[2]}, animation = {r[3]}, model = 0x{r[4] & 0xFFFFFFFF:x}, '
            f'accessory = {r[5]}, accessory2 = {r[6]}, tag = {r[7]} }},' for r in rows]


def native_schema(image):
    rows = []
    address = SCHEMA
    while True:
        raw = bytes(image.data[address - image.base:address - image.base + SCHEMA_ROW])
        kind = struct.unpack_from('<I', raw, 0)[0]
        name = raw[4:0x24].split(b'\0')[0].decode('ascii')
        minimum, maximum = struct.unpack_from('<ii', raw, 0x24)
        count = struct.unpack_from('<I', raw, 0x34)[0]
        rows.append((kind, name, minimum, maximum, count))
        if kind == SCHEMA_END:
            return rows
        address += SCHEMA_ROW


def schema_lines(rows):
    lines = []
    for kind, name, minimum, maximum, count in rows:
        check(kind in (0, 1, 2, 3, 4, 5, 6, 7, SCHEMA_END), True, f'schema kind {kind} ({name})')
        extra = ''
        if kind == 6:
            extra += f', minimum = {minimum}, maximum = {maximum}'
        if kind == 3:
            extra += f', count = {count}'
        lines.append(f'  {{ kind = {kind}, name = "{name}"{extra} }},')
    return lines


def compare(path, pattern, expected, label):
    lines = [line for line in Path(path).read_text(encoding='utf-8').splitlines() if pattern.match(line)]
    check(len(lines), len(expected), f'{path} {label}')
    for index, (line, native) in enumerate(zip(lines, expected)):
        check(line.rstrip(','), native.rstrip(','), f'{path} {label} {index}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--write-responses', type=Path)
    parser.add_argument('--write-schema', type=Path)
    parser.add_argument('--responses', type=Path, default=Path('content/data/tpi-advisor-responses.toml'))
    parser.add_argument('--schema', type=Path, default=Path('content/data/tpi-advisor-schema.toml'))
    args = parser.parse_args()

    image = Image.load(executable_path('tpi'))
    responses = native_responses(image)
    schema = native_schema(image)
    if args.write_responses or args.write_schema:
        if args.write_responses:
            args.write_responses.write_text(RESPONSE_HEADER + '\n' + '\n'.join(response_lines(responses)) + '\n]\n', encoding='utf-8')
            print(f'{len(responses)} responses written to {args.write_responses}')
        if args.write_schema:
            args.write_schema.write_text(SCHEMA_HEADER + '\n' + '\n'.join(schema_lines(schema)) + '\n]\n', encoding='utf-8')
            print(f'{len(schema)} schema records written to {args.write_schema}')
        return
    check(len(responses), 994, 'response rows (ids 0 to 1012; 833 twice, the lookups take its first row)')
    check(len(schema), 1007, 'schema records')
    compare(args.responses, RESPONSE_PATTERN, response_lines(responses), 'response')
    compare(args.schema, SCHEMA_PATTERN, schema_lines(schema), 'schema record')
    print(f'{len(responses)} response rows and {len(schema)} schema records match')


if __name__ == '__main__':
    main()
