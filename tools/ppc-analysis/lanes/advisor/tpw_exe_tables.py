"""Write or check the advisor's costume and help-message tables from the Windows tp.exe.

Two data files under content/data come from the Windows v2.0 executable (SHA-256 below), where the
advisor's tables live:

* ``advisor-costumes.toml``: fields +0x14 and +0x18 of the 610 response rows (32-byte records from
  0x768FB8, sentinel response 9999), the two accessory selectors the update 0x598DC0 hands to the selector
  0x598CA0. A selector is the dummy-attribute value of the mesh node to show (5 to 14 are the hats, the
  spatula and the bow tie of the global advisor), 0 shows no accessory, -1 the level's theme costume. Only
  responses with a nonzero selector are listed.
* ``advisor-help-messages.toml``: the scored-message descriptors whose score callback is the context-help
  getter 0x59E8C0 (82 descriptors; the 81 whose first response is in the response table, 291 names the absent 551). The descriptor table
  (0x76E300, 351 records of 0x38 bytes) is zero in the file; a straight-line initializer (0x5A0A50 to
  0x5A86FB: stores of immediates and registers loaded with immediates) fills it, so this script replays
  those stores instead of reading the file.

Usage: python3 tpw_exe_tables.py <tp.exe> --write DIR | --check DIR
"""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import struct
import sys

TPW_SHA = '5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805'
RESPONSE_TABLE = 0x768FB8
RESPONSE_RECORD = 32
SENTINEL = 9999
LIMIT = 4096
DESCRIPTOR_TABLE = 0x76E300
DESCRIPTOR_RECORD = 0x38
DESCRIPTOR_COUNT = 351
INITIALIZER = 0x5A0A50
INITIALIZER_END = 0x5A86FB
PUSHES = (0x53, 0x55, 0x56, 0x57)
POPS = (0x5B, 0x5D, 0x5E, 0x5F)
HELP_SCORE_CALLBACK = 0x59E8C0

REGISTERS = ['eax', 'ecx', 'edx', 'ebx', 'esp', 'ebp', 'esi', 'edi']


class Image:
    """A PE image: virtual-address reads through its section table."""

    def __init__(self, data: bytes):
        self.data = data
        pe = struct.unpack_from('<I', data, 0x3C)[0]
        count = struct.unpack_from('<H', data, pe + 6)[0]
        optional = struct.unpack_from('<H', data, pe + 20)[0]
        self.base = struct.unpack_from('<I', data, pe + 52)[0]
        section = pe + 24 + optional
        self.sections = [struct.unpack_from('<8sIIII', data, section + 40 * index) for index in range(count)]

    def offset(self, va: int) -> int:
        for _, _, address, raw_size, raw_offset in self.sections:
            if self.base + address <= va < self.base + address + raw_size:
                return raw_offset + va - self.base - address
        raise ValueError(f'address {va:#x} is not in the file')

    def read(self, va: int, length: int) -> bytes:
        offset = self.offset(va)
        return self.data[offset:offset + length]


def response_rows(image: Image) -> list[tuple[int, ...]]:
    rows = []
    address = RESPONSE_TABLE
    while True:
        if len(rows) > LIMIT:
            raise ValueError('response table has no sentinel')
        row = struct.unpack('<8i', image.read(address, RESPONSE_RECORD))
        if row[0] == SENTINEL:
            return rows
        rows.append(row)
        address += RESPONSE_RECORD


def replay_stores(image: Image, start: int, end: int | None = None, limit: int = 200000) -> dict[int, int | None]:
    """Replays the straight-line stores of an initializer: immediates into registers, registers and
    immediates into absolute addresses, pushes and pops. Any other instruction, or a store from a register
    that holds no replayed value, is an error; the replay must reach the ``ret`` at ``end`` (when given)."""
    registers: dict[str, int | None] = {name: None for name in REGISTERS}
    memory: dict[int, int | None] = {}
    address = start
    for _ in range(limit):
        code = image.read(address, 10)
        opcode = code[0]
        if opcode == 0xC3:
            if end is not None and address != end:
                raise ValueError(f'initializer returned at {address:#x}, expected {end:#x}')
            return memory
        if 0xB8 <= opcode <= 0xBF:
            registers[REGISTERS[opcode - 0xB8]] = struct.unpack_from('<I', code, 1)[0]
            address += 5
        elif opcode in (0x31, 0x33) and code[1] >> 6 == 3 and (code[1] >> 3) & 7 == code[1] & 7:
            registers[REGISTERS[code[1] & 7]] = 0
            address += 2
        elif opcode == 0x8B and code[1] >> 6 == 3:
            registers[REGISTERS[(code[1] >> 3) & 7]] = registers[REGISTERS[code[1] & 7]]
            address += 2
        elif opcode in PUSHES or opcode in POPS:
            address += 1
        elif opcode == 0x89 and code[1] & 0xC7 == 0x05:
            register = REGISTERS[(code[1] >> 3) & 7]
            if registers[register] is None:
                raise ValueError(f'store of {register} at {address:#x} before it was loaded')
            memory[struct.unpack_from('<I', code, 2)[0]] = registers[register]
            address += 6
        elif opcode == 0xA3:
            if registers['eax'] is None:
                raise ValueError(f'store of eax at {address:#x} before it was loaded')
            memory[struct.unpack_from('<I', code, 1)[0]] = registers['eax']
            address += 5
        elif opcode == 0xC7 and code[1] == 0x05:
            memory[struct.unpack_from('<I', code, 2)[0]] = struct.unpack_from('<I', code, 6)[0]
            address += 10
        else:
            raise ValueError(f'unsupported instruction byte {opcode:#04x} at {address:#x}')
    raise ValueError('initializer did not end')


def descriptors(image: Image) -> dict[int, dict[str, int]]:
    """The 351 message descriptors: the file's own words with the initializer's stores applied."""
    stores = replay_stores(image, INITIALIZER, INITIALIZER_END)
    result = {}
    for message in range(DESCRIPTOR_COUNT):
        base = DESCRIPTOR_TABLE + message * DESCRIPTOR_RECORD
        words = list(struct.unpack('<14I', image.read(base, DESCRIPTOR_RECORD)))
        for index in range(14):
            value = stores.get(base + index * 4)
            if value is not None:
                words[index] = value
        result[message] = {'skip_scan': words[0], 'id': words[1], 'group': words[2], 'mode': words[3], 'callback': words[4],
                           'duplicates': words[8] & 0xFF, 'first': words[9], 'count': words[10]}
    return result


def costumes(image: Image) -> list[tuple[int, int, int]]:
    return [(row[0], row[5], row[6]) for row in response_rows(image) if row[5] != 0 or row[6] != 0]


def help_messages(image: Image) -> list[dict[str, int]]:
    responses = {row[0] for row in response_rows(image)}
    table = descriptors(image)
    rows = []
    for message in range(DESCRIPTOR_COUNT):
        row = table[message]
        if row['id'] != message:
            raise ValueError(f'descriptor {message} has id {row["id"]}')
        # 291 names response 551, which the response table does not have
        if row['callback'] == HELP_SCORE_CALLBACK and row['first'] in responses:
            rows.append(row | {'message': message})
    return rows


def render_costumes(rows: list[tuple[int, int, int]]) -> str:
    lines = [
        '# Advisor costumes of Sim Theme Park World (Windows v2.0), for interoperability: the accessory selectors of each',
        '# advisor response. Copied field by field from the response table at 0x768FB8 of tp.exe',
        f'# (SHA-256 {TPW_SHA}) by',
        '# tools/ppc-analysis/lanes/advisor/tpw_exe_tables.py; see docs/LIPS.md ("Costumes").',
        '#',
        '# response: response ID (the first field of its 32-byte record); hat and hat2: fields +0x14 and +0x18, the',
        '# dummy-attribute values of the mesh nodes to show (5-14 on the global advisor), -1 for the level\'s theme',
        '# costume (levels/<level>/Standard.sam ThemeAdvisorCostumes). Responses that are not listed have no accessory.',
        '',
        'costumes = [',
    ]
    for response, hat, hat2 in rows:
        lines.append(f'  {{ response = {response}, hat = {hat}, hat2 = {hat2} }},')
    lines.append(']')
    return '\n'.join(lines) + '\n'


def render_help(rows: list[dict[str, int]]) -> str:
    lines = [
        '# Advisor help messages of Sim Theme Park World (Windows v2.0), for interoperability: the scored messages whose',
        '# score callback is the context-help getter 0x59E8C0 (81 of the 82 descriptors; 291 names the absent response 551), which the window code posts through 0x486B00 and withdraws',
        '# through 0x486B40. Copied from the descriptor table at 0x76E300 of tp.exe',
        f'# (SHA-256 {TPW_SHA}),',
        '# as filled by its initializer at 0x5A0A50, by tools/ppc-analysis/lanes/advisor/tpw_exe_tables.py;',
        '# see docs/LIPS.md ("Contextual help").',
        '#',
        '# message: message ID; response: first response ID (response numbers are consecutive, count of them);',
        '# group: message group (1 = tutorial); duplicates: pending duplicates allowed; mode: the descriptor\'s game-mode selector',
        '# (0: full simulation only, 1: Instant Action only, 2: always).',
        '',
        'messages = [',
    ]
    for row in rows:
        lines.append(f'  {{ message = {row["message"]}, response = {row["first"]}, count = {row["count"]}, group = {row["group"]}, '
                     f'duplicates = {row["duplicates"]}, mode = {row["mode"]} }},')
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
    if digest != TPW_SHA:
        parser.exit(1, f'unexpected tp.exe SHA-256 {digest}\n')
    image = Image(raw)
    outputs = {
        'advisor-costumes.toml': render_costumes(costumes(image)),
        'advisor-help-messages.toml': render_help(help_messages(image)),
    }
    directory = args.write or args.check
    for name, text in outputs.items():
        path = directory / name
        if args.write:
            path.write_text(text, encoding='utf-8')
            print(f'wrote {path}')
        elif path.read_text(encoding='utf-8') != text:
            parser.exit(1, f'{path} differs from tp.exe\n')


if __name__ == '__main__':
    sys.exit(main())
