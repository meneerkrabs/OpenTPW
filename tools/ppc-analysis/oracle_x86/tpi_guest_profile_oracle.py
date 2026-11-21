"""Replay the Theme Park Inc guest profile fragment on synthetic input.

Requires the identified OPENTPW_TPI_EXE and the pinned Unicorn dependency. The
entry and stop addresses exclude the rest of the guest constructor. The world
RNG and thing-id accessor execute their original instructions, without stubs.
"""
from __future__ import annotations

import argparse
import re
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from tpi_themeengine_oracle import construct, load, resolve, sandbox as settings_sandbox  # noqa: E402
from x86_harness import Image, Sandbox, executable_path  # noqa: E402


PEEP_INFO = (
    ('ExitLevel', 0xA01A6C, 400),
    ('ExitLevelVar', 0xA01A70, 30),
    ('StartingCashVarPc', 0xA01A74, 15),
    ('PrankeryLikelihood', 0xA01AFC, 37),
    ('PercentageOfKids', 0xA01B50, 61),
    ('PercentageOfParents', 0xA01B54, 22),
    ('PercentageOfGrandparents', 0xA01B58, 17),
)


def verify_settings_layout(image: Image) -> None:
    sb = settings_sandbox(image)
    construct(sb)
    for key, address, _ in PEEP_INFO:
        assert resolve(sb, 'PeepInfo', key) == address, key
    text = ''.join(f'PeepInfo.{key} {value}\n' for key, _, value in PEEP_INFO)
    text += (
        'PeepTypes[0].PreferredExcitement.StartingCash.BoredomThreshold.Generation 90 300 40 0\n'
        'PeepTypes[23].PreferredExcitement.StartingCash.BoredomThreshold.Generation 15 800 75 2\n'
    )
    for line in text.splitlines(keepends=True):
        sb.executed = 0
        load(sb, line)
    for key, address, value in PEEP_INFO:
        assert sb.read(address, 'I') == value, key
    assert sb.read(0xA01E04, 'I') == 24
    for index, words in [(0, (90, 300, 40, 0)), (23, (15, 800, 75, 2))]:
        row = 0xA01C24 + 20 * index
        assert tuple(sb.read(row + 4 * offset, 'I') for offset in range(4)) == words

CASES = (
    (1, -1, 70, 20, 10),
    (7, -1, 70, 20, 10),
    (10, -1, 0, 0, 100),
    (7, 2, 100, 0, 0),
    (4, 0, 0, 0, 100),
    (1, -1, 0, 0, 0),  # The uncovered percentage falls back to variant 2 here.
    (0xFFFFFFFF, -1, 0, 100, 0),
    # Exact boundaries of the retail generation roll: 0, 69, 70, 89, 90, 99.
    (18, -1, 70, 20, 10),
    (57, -1, 70, 20, 10),
    (6, -1, 70, 20, 10),
    (26, -1, 70, 20, 10),
    (31, -1, 70, 20, 10),
    (200, -1, 70, 20, 10),
)


def replay(sb: Sandbox, seed: int, forced: int, kids: int, parents: int, grandparents: int) -> tuple[int, ...]:
    sb.reset()
    guest = sb.object(0x300)
    world = sb.object(0x1EAB40)
    sb.write(world + 0x1EAB3C, 'I', seed)
    for address, value in (
        (0xADE6B0, world), (0xA01A6C, 400), (0xA01A70, 30), (0xA01A74, 15),
        (0xA01B50, kids), (0xA01B54, parents), (0xA01B58, grandparents), (0xA01E04, 24),
        (0xA029C4, int(forced >= 0)), (0xA029C8, max(0, forced)),
    ):
        sb.write(address, 'I', value)
    for row in range(24):
        for column, value in enumerate((90 - row, 300 + 20 * row, 40 + row, row // 8)):
            sb.write(0xA01C24 + 20 * row + 4 * column, 'I', value)
    sb.call(0x60C58F, regs={'esi': guest, 'ebx': 0}, stop=0x60C712)
    profile = (sb.read(guest + 0x21A, 'B'), *(sb.read(guest + offset, 'i') for offset in (0x1E0, 0x1C0, 0x2A4, 0x2A8, 0x2AC)), sb.read(world + 0x1EAB3C, 'I'))
    thing_id = seed & 0xFFFF
    prank_chance = 0 if seed in (1, 18) else 100
    sb.write(guest, 'H', thing_id)
    sb.write(guest + 0xD4, 'H', 100)
    sb.write(0xA01C34 + 20 * profile[0], 'f', 1.0)
    sb.write(0xA01AFC, 'I', prank_chance)
    sb.call(0x60C712, regs={'esi': guest, 'ebx': 0, 'edx': profile[0] * 5}, stop=0x60C830)
    meters = tuple(int(sb.read(guest + offset, 'f')) for offset in (0x1BC, 0x1C8, 0x1CC, 0x1D0))
    return (*profile, thing_id, prank_chance, *meters, sb.read(guest + 0x1E4, 'i'), sb.read(world + 0x1EAB3C, 'I'))


def appearance_rows(image: Image) -> list[str]:
    sb = Sandbox(image, [
        (0x60C830, 0x60C912), (0x4080E9, 0x4080EE), (0x6375C0, 0x6375CB),
        (0x403508, 0x40350D), (0x649430, 0x64943B), (0x402680, 0x402685),
        (0x6902F0, 0x690315), (0x40A92F, 0x40A934), (0x6493E0, 0x649420),
    ], budget=1000)
    rows = []
    for thing_id, generation, forced in [(0, 0, -1), (1, 1, -1), (65535, 2, -1), (42, 1, 3), (190, 2, 0), (513, 0, 2)]:
        sb.reset()
        guest, world = sb.object(0x300), sb.object(0x1EAB40)
        for address, value in [
            (0xADE6B0, world), (0xAA1484, world), (world + 0x1EAB3C, 0x12345678),
            (guest + 0x2A4, generation), (0x9D7008, 2), (0x9D7014, 3), (0x9D7020, 4),
            (0xA029CC, int(forced >= 0)), (0xA029D0 + 4 * generation, max(0, forced)),
        ]:
            sb.write(address, 'I', value)
        sb.write(guest, 'H', thing_id)
        sb.call(0x60C830, regs={'esi': guest, 'ebx': 0}, stop=0x60C912)
        assert sb.read(guest + 0x34, 'I') == generation
        sprite = sb.read(guest + 0x30, 'I')
        appearance = sb.read(guest + 0x248, 'I')
        state = sb.read(world + 0x1EAB3C, 'I')
        rows.append(f'[DataRow({thing_id}, {generation}, {forced}, {sprite}, {appearance}, {state}u)]')
    return rows


def speed_rows(image: Image) -> list[str]:
    sb = Sandbox(image, [(0x60C712, 0x60C734), (0x86FAB0, 0x86FAD7)], budget=100)
    rows = []
    for speed, multiplier in [(60, .95), (80, .45), (100, .89), (120, .35), (140, .33), (100, 1.0)]:
        results = []
        bits, = struct.unpack('<I', struct.pack('<f', multiplier))
        for control_word in (0x7F, 0x27F, 0x37F):
            sb.reset()
            sb.set_fpcw(control_word)
            guest = sb.object(0x300)
            sb.write(guest + 0xD4, 'H', speed)
            sb.write(0xA01C34, 'I', bits)
            sb.call(0x60C712, regs={'esi': guest, 'edx': 0}, stop=0x60C734)
            results.append(sb.read(guest + 0xD4, 'H'))
        rows.append(f'[DataRow({speed}, {bits}u, {results[0]}, {results[1]}, {results[2]})]')
    return rows


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', type=Path, help='Verify the generated cases against a C# fixture.')
    args = parser.parse_args()
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('Set OPENTPW_TPI_EXE to the identified Theme Park Inc executable.')
    image = Image.load(path)
    verify_settings_layout(image)
    sb = Sandbox(image, [
        (0x60C58F, 0x60C830), (0x40A92F, 0x40A934), (0x6493E0, 0x649420),
        (0x86FAB0, 0x86FAD7), (0x4080E9, 0x4080EE), (0x6375C0, 0x6375CB),
    ], budget=1000)
    rows = []
    for case in CASES:
        row = (*case, *replay(sb, *case))
        fields = [f'{value}u' if index in (0, 11, len(row) - 1) else str(value) for index, value in enumerate(row)]
        rows.append(f'[DataRow({", ".join(fields)})]')
    rows.extend(appearance_rows(image))
    rows.extend(speed_rows(image))
    if args.check is not None:
        actual = re.findall(r'^\s*(\[DataRow\([^\r\n]+\)\])$', args.check.read_text(encoding='utf-8'), re.MULTILINE)
        if actual != rows:
            raise SystemExit(f'Guest profile fixtures differ from native output: {args.check}')
        print(f'{len(rows)} guest profile fixtures match')
    else:
        print('\n'.join(rows))


if __name__ == '__main__':
    main()
