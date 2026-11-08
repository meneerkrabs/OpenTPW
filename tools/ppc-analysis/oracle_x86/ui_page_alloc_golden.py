"""X91 (ORACLE.md section 7.43): native 0x58ECA0 synthetic page allocations, not a game run.

Runs the real placement routine, unsigned-key sort 0x57D090/0x57CED0,
format predicate 0x590FB0 and priority refresh 0x58CB40 (including __ftol).
No calls are stubbed. The identified tp.exe is required and never emitted.
The input requests are explicit synthetic texture records, not captured assets.
Page size/cache flags are caller inputs, not an assumed production device policy.

Named cases cover: fit alignment 0x58ED66..BE, reduction/reinsertion before any placement
0x58EDC6..EEB2, padding 0x58EEE3..EFA4, remainder 8/9 and all split branches
0x58EFB4..F0EF, tail/tie sort 0x58F0F3..F11B, failed fits and the 127-record
limit 0x58ED43..54/0x58F135..17A. --coverage prints the first three witness
case names per selected address (not all CFG edges; no original instructions).

Usage: python3 -I tools/ppc-analysis/oracle_x86/ui_page_alloc_golden.py
       ... --check source/OpenTPW.Tests/UiPageAllocGolden.cs
"""
from __future__ import annotations

import base64
import json
import struct
import sys
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path, seeded  # noqa: E402

ENTRY = 0x58ECA0
BRANCHES = (0x58ED70, 0x58ED96, 0x58EE02, 0x58EE09, 0x58EE0D, 0x58EE36,
            0x58EE5C, 0x58EE8F, 0x58EEA8, 0x58EEB7, 0x58EEEC, 0x58EF64,
            0x58EFEE, 0x58F036, 0x58F09E, 0x58F0CC, 0x58F116, 0x58F135,
            0x58F155, 0x58F17A)


def bits(value):
    return struct.unpack('<I', struct.pack('<f', value))[0]


def request(width, height, flags=0, priority=0.5, key=0):
    return [width, height, flags, bits(priority), key]


def cases():
    rows = []

    def add(name, page, textures, flags=0, mask=0, align=0, device=0):
        rows.append(dict(name=name, page=list(page), flags=flags, mask=mask, align=align,
                         device=device, requests=[[i + 1] + list(r) for i, r in enumerate(textures)]))

    add('empty', (64, 64), [])
    for width, height in ((64, 64), (56, 64), (55, 64), (64, 56), (64, 55), (56, 56), (55, 55)):
        add(f'remainder-{width}-{height}', (64, 64), [request(width, height), request(8, 8), request(9, 9)])
    add('both-split-tied-free-rectangles', (64, 64), [request(32, 32)] * 4)
    add('many-tied-rectangles', (256, 256), [request(16, 16)] * 130)
    add('127-placement-cap', (256, 256), [request(1, 1)] * 128)
    add('127-exact-count', (256, 256), [request(1, 1)] * 127)
    for w, h in ((65, 8), (8, 65), (65, 65)):
        add(f'failed-{w}-{h}-then-fit', (64, 64), [request(w, h), request(16, 16), request(8, 8)])
    for align in (0, 1):
        for padding in (0, 2):
            for mask in (0, 1, 3, 7, 15):
                add(f'align-{align}-padding-{padding}-mask-{mask}', (64, 64),
                    [request(17, 17), request(16, 16), request(33, 9), request(9, 33)],
                    flags=padding, mask=mask, align=align)
    add('alignment-pushes-over-edge', (63, 63), [request(63, 63)], mask=7, align=1)
    add('padding-independent-of-fit-gate', (63, 63), [request(63, 63)], flags=2, mask=7)
    for texture_flags in (0, 0x10, 0x20, 0x30, 0x4000, 0x80000, 0x4000 | 0x30, 0x80000 | 0x30):
        add(f'reduction-flags-{texture_flags:x}', (64, 64), [request(129, 129, texture_flags), request(8, 8)], flags=1)
    add('reduction-exhausts-levels', (8, 8), [request(257, 257, 0x30), request(8, 8)], flags=1)
    add('no-reduction-after-first-placement', (64, 64), [request(16, 16), request(129, 129, 0x30)], flags=1)
    add('reinsert-behind-higher-signed-key', (64, 64),
        [request(129, 65, 0x30), request(16, 16, key=0x7FFFFFFF), request(8, 8, key=0x7FFFFFFF)], flags=1)
    add('signed-reinsert-key-boundary', (64, 64),
        [request(129, 65, 0x30), request(16, 16, key=0xFFFFFFFF)], flags=1)
    for device in (0, 1):
        for width in (8, 9, 17):
            add(f'compressed-device-{device}-width-{width}', (4, 4),
                [request(width, 9, 0x10000 | 0x30)], flags=1, device=device)
    add('unsigned-free-key-high-bit', (65535, 256), [request(32767, 16), request(9, 9), request(32767, 32)])
    # Exact float neighbours exercise the single-precision rank refresh, not just integer priorities.
    for centre in (0x3F000000, 0x3F800000, 0x3FC00000, 0x3F7FEFFE):
        for offset in (-2, -1, 0, 1, 2):
            priority = struct.unpack('<f', struct.pack('<I', centre + offset))[0]
            add(f'priority-bits-{centre + offset:08x}', (64, 64),
                [request(65, 65, 0x30, priority)], flags=1)
    rng = seeded('ui-page-allocation-v1')
    for n in range(300):
        page = rng.choice((32, 64, 128, 256))
        count = rng.randrange(1, 45)
        items = [request(rng.randrange(1, page + 34), rng.randrange(1, page + 34),
                         rng.choice((0, 0x10, 0x20, 0x30, 0x4000 | 0x30, 0x80000 | 0x30, 0x10000 | 0x30)),
                         rng.choice((-0.25, 0.0, 0.5, 1.0, 1.5)), rng.getrandbits(32)) for _ in range(count)]
        add(f'mixed-{n:03}', (page, page), items, flags=rng.randrange(4),
            mask=rng.choice((0, 1, 3, 7, 15)), align=rng.randrange(2), device=rng.randrange(2))
    return rows


def oracle(sb, row):
    sb.reset()
    sb.set_fpcw(0x7F)
    cache = sb.object(0x1930, {0x18CC: ('I', row['flags']), 0x18E4: ('I', row['mask']),
                               0x18E8: ('I', row['align']), 0x1918: ('I', 999), 0x191C: ('I', 999),
                               0x1924: ('I', 999)})
    sb.write(0x8BD524, 'I', row['device'])
    sb.write(0x8C4B68, 'I', 71)
    slots, ids = {}, {}
    for identifier, width, height, flags, priority, key in row['requests']:
        address = sb.object(0x40, {8: ('I', flags), 0x14: ('h', width), 0x16: ('h', height),
                                  0x30: ('I', priority), 0x38: ('I', key)})
        slots[identifier] = address
        ids[address] = identifier
    pending = sb.object(4 * (len(slots) + 1))
    for n, address in enumerate(slots.values()):
        sb.write(pending + n * 4, 'I', address)
    placements = sb.object(128 * 20)
    count = sb.object(4)
    page = sb.object(16, {8: ('i', row['page'][0]), 12: ('i', row['page'][1])})
    # RET 0x14 also pops a fifth argument which this routine never reads.
    result = sb.call(ENTRY, args=(pending, placements, count, page, 0), this=cache)
    assert result.esp_delta == 0, result.esp_delta
    size = sb.read(count, 'I')
    packed = []
    for n in range(size):
        address = placements + n * 20
        packed.append([*[sb.read(address + d, 'i') for d in (0, 4, 8, 12)], ids[sb.read(address + 16, 'I')]])
    return dict(success=result.eax, max=[sb.read(cache + d, 'i') for d in (0x1918, 0x191C)],
                retry=sb.read(cache + 0x1924, 'I'), placements=packed,
                order=[ids[sb.read(pending + 4 * n, 'I')] for n in range(len(slots))],
                textures=[[identifier, sb.read(address + 0x14, 'h'), sb.read(address + 0x16, 'h'),
                           sb.read(address + 8, 'I'), sb.read(address + 0x38, 'I')]
                          for identifier, address in slots.items()],
                terminator=sb.read(placements + size * 20 + 16, 'I'), placed=sb.read(0x8C4B6C, 'I'))


def render(coverage=False):
    path = executable_path('tpw')
    if path is None:
        raise SystemExit('requires the identified OPENTPW_TPW_EXE')
    sb = Sandbox(Image.load(path), [(ENTRY, 0x58F18C), (0x57CED0, 0x57CFF4), (0x57D090, 0x57D0BB),
                                   (0x58CB40, 0x58CB92), (0x590FB0, 0x590FCB), (0x67A830, 0x67A857)],
                 budget=1_000_000)
    import unicorn
    active, witnesses = [''], {address: [] for address in BRANCHES}

    def visit(uc, address, size, user):
        if address in witnesses and len(witnesses[address]) < 3 and active[0] not in witnesses[address]:
            witnesses[address].append(active[0])
    sb.uc.hook_add(unicorn.UC_HOOK_CODE, visit)
    rows = cases()
    for row in rows:
        active[0] = row['name']
        row['expected'] = oracle(sb, row)
    if coverage:
        for address, names in witnesses.items():
            print(f'{address:#x}: {", ".join(names) if names else "NOT COVERED"}')
        if any(not names for names in witnesses.values()):
            raise SystemExit('missing native branch witness')
        return ''
    raw = json.dumps(rows, sort_keys=True, separators=(',', ':')).encode()
    packed = base64.b64encode(zlib.compress(raw, 9)).decode()
    output = ['// Generated by ui_page_alloc_golden.py from isolated tp.exe 0x58ECA0 at x87 control word 0x7F.',
              '// Synthetic rectangles and numeric results only; no original assets or executable bytes.',
              'namespace OpenTPW.Tests;', '', 'internal static class UiPageAllocGolden', '{',
              f'\tpublic const int RowCount = {len(rows)};', '\tpublic const string Packed =']
    chunks = [packed[i:i + 120] for i in range(0, len(packed), 120)]
    output += ['\t\t"' + s + ('" +' if i + 1 < len(chunks) else '";') for i, s in enumerate(chunks)]
    return '\n'.join(output + ['}', ''])


if __name__ == '__main__':
    if sys.argv[1:] == ['--coverage']:
        render(coverage=True)
    else:
        text = render()
        if len(sys.argv) == 3 and sys.argv[1] == '--check':
            if Path(sys.argv[2]).read_text() != text:
                raise SystemExit('UiPageAllocGolden.cs differs from native output')
        elif len(sys.argv) == 1:
            print(text, end='')
        else:
            raise SystemExit('usage: ui_page_alloc_golden.py [--coverage | --check FILE]')
