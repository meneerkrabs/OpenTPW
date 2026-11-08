"""X92 (ORACLE.md section 7.44): native 0x58EAE0 page-list merge on synthetic records.

Runs the real merge and its device-dependent predicate 0x590FB0 with no stubs on
caller-built linked lists. The identified tp.exe is required; it is never the game
process, and no original bytes or data are emitted. Inputs are synthetic records,
not captured pages.

The merge takes three stack arguments (RET 0xC) and does not read `this`:
  arg1 = output pointer array, null-terminated by the routine
  arg2 = pending list head
  arg3 = container whose +4 is the existing list head
It emits the pending list minus device-dependent records, interleaved with the
existing list's records that have flag 0x40000000, in descending signed
(+0x12, +0x10) order; full ties emit the existing record first. It returns the
sum of width x height over the emitted records. The leading existing record is
skipped once when it is device dependent.

Covered branches: the leading skip 0x58EB06..16, empty lists 0x58EBF5/0x58EB22/
0x58EB42, the existing-flag filter 0x58EB30, the pending device filter 0x58EB48,
both emit paths 0x58EB7B/0x58EBB4, the drain paths 0x58EBFD and 0x58EC47, and the
exit 0x58EC83. --coverage prints up to three named witnesses per selected address.

Usage: python3 -I tools/ppc-analysis/oracle_x86/ui_page_merge_golden.py
       ... --check source/OpenTPW.Tests/UiPageMergeGolden.cs
       ... --coverage
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

ENTRY = 0x58EAE0
DONE = 0x58EC95
BRANCHES = (0x58EB06, 0x58EB11, 0x58EB16, 0x58EB22, 0x58EB2A, 0x58EB30,
            0x58EB37, 0x58EB42, 0x58EB48, 0x58EB51, 0x58EB5C, 0x58EB6D,
            0x58EB6F, 0x58EB79, 0x58EB7B, 0x58EBB4, 0x58EBEB, 0x58EBED,
            0x58EBFB, 0x58EC04, 0x58EC45, 0x58EC4E, 0x58EC83)

EXISTING_ONLY = 0x40000000
DEVICE_DEPENDENT = 0x30000


def s32(value):
    """The register read as the signed 32-bit integer the caller keeps."""
    value &= 0xFFFFFFFF
    return value - (1 << 32) if value >> 31 else value


def node(identifier, flags=0, u=0, v=0, width=8, height=8):
    return dict(id=identifier, flags=flags, u=u, v=v, width=width, height=height)


def cases():
    rows = []

    def add(name, existing, pending, device=0):
        rows.append(dict(name=name, device=device, existing=existing, pending=pending))

    add('both-empty', [], [])
    add('pending-only', [], [node(1, v=0, u=0), node(2, v=0, u=5)])
    add('existing-only', [node(1, EXISTING_ONLY, v=1)], [])
    add('existing-without-flag-is-filtered', [node(1, 0, v=1), node(2, EXISTING_ONLY, v=2)], [])
    add('leading-device-dependent-skipped-once', [node(1, DEVICE_DEPENDENT | EXISTING_ONLY, v=9),
                                                  node(2, EXISTING_ONLY, v=1)], [], device=1)
    add('leading-device-dependent-kept-on-other-device', [node(1, DEVICE_DEPENDENT | EXISTING_ONLY, v=9),
                                                          node(2, EXISTING_ONLY, v=1)], [], device=2)
    add('pending-device-dependent-filtered', [], [node(1, DEVICE_DEPENDENT, v=5), node(2, 0, v=6)], device=1)
    add('pending-device-dependent-kept-on-other-device', [], [node(1, DEVICE_DEPENDENT, v=5), node(2, 0, v=6)], device=0)
    add('descending-v', [node(1, EXISTING_ONLY, v=2, u=0)], [node(2, 0, v=1, u=0), node(3, 0, v=3, u=0)])
    add('ascending-v', [node(1, EXISTING_ONLY, v=1, u=0)], [node(2, 0, v=2, u=0), node(3, 0, v=0, u=0)])
    add('tie-on-v-uses-u', [node(1, EXISTING_ONLY, v=2, u=1)], [node(2, 0, v=2, u=0), node(3, 0, v=2, u=2)])
    add('full-tie-emits-existing-first', [node(1, EXISTING_ONLY, v=2, u=1)], [node(2, 0, v=2, u=1)])
    add('signed-v-ordering', [node(1, EXISTING_ONLY, v=-1, u=0)], [node(2, 0, v=-2, u=0), node(3, 0, v=0, u=0)])
    add('signed-u-tie', [node(1, EXISTING_ONLY, v=0, u=-1)], [node(2, 0, v=0, u=-2)])
    add('area-sums-in-range', [], [node(1, 0, width=30000, height=30000), node(2, 0, width=30000, height=30000)])
    # three products of 900,000,000 exceed int32 and wrap: 2,700,000,000 -> -1,594,967,296
    add('area-sums-wrap', [], [node(1, 0, width=30000, height=30000), node(2, 0, width=30000, height=30000),
                               node(3, 0, width=30000, height=30000)])
    add('zero-sized-records', [], [node(1, 0, width=0, height=0), node(2, 0, width=0, height=5)])
    add('existing-longer-than-pending', [node(i, EXISTING_ONLY, v=i) for i in range(1, 7)], [node(50, 0, v=3)])
    add('pending-longer-than-existing', [node(1, EXISTING_ONLY, v=3)], [node(50 + i, 0, v=i) for i in range(1, 7)])
    add('all-existing-filtered', [node(i, 0, v=i) for i in range(1, 5)], [node(50, 0, v=2)])
    add('pending-all-device-dependent', [node(1, EXISTING_ONLY, v=3)], [node(50 + i, DEVICE_DEPENDENT, v=i) for i in range(1, 4)], device=1)
    rng = seeded('ui-page-merge-v1')
    for n in range(120):
        device = rng.choice((0, 1, 2))
        def make(count, prefix):
            out = []
            for _ in range(count):
                flags = rng.choice((0, 0, EXISTING_ONLY, DEVICE_DEPENDENT,
                                    DEVICE_DEPENDENT | EXISTING_ONLY, rng.getrandbits(32)))
                out.append(node(prefix + len(out) + 1, flags, u=rng.randrange(-4, 5), v=rng.randrange(-4, 5),
                                width=rng.randrange(0, 300), height=rng.randrange(0, 300)))
            return out
        add(f'mixed-{n:03}', make(rng.randrange(0, 9), 1000), make(rng.randrange(0, 9), 2000), device)
    return rows


def build(sb, ordered):
    address = {}
    for item in ordered:
        address[item['id']] = sb.object(0x40, {8: ('I', item['flags']), 0x10: ('h', item['u']),
                                               0x12: ('h', item['v']), 0x14: ('h', item['width']),
                                               0x16: ('h', item['height'])})
    for previous, item in zip(ordered, ordered[1:]):
        sb.write(address[previous['id']] + 4, 'I', address[item['id']])
    return address


def oracle(sb, row):
    sb.reset()
    sb.write(0x8BD524, 'I', row['device'])
    existing = build(sb, row['existing'])
    pending = build(sb, row['pending'])
    container = sb.object(8, {4: ('I', existing[row['existing'][0]['id']] if row['existing'] else 0)})
    output = sb.object(4 * (len(row['existing']) + len(row['pending']) + 1))
    pending_head = pending[row['pending'][0]['id']] if row['pending'] else 0
    result = sb.call(ENTRY, args=(output, pending_head, container), this=0)
    assert result.esp_delta == 0, result.esp_delta
    ids = {value: key for key, value in {**existing, **pending}.items()}
    order, at = [], 0
    while True:
        pointer = sb.read(output + 4 * at, 'I')
        if pointer == 0:
            break
        order.append(ids[pointer])
        at += 1
        if at > len(ids):
            raise AssertionError('merged list does not terminate')
    # The routine must leave every input record and its links untouched; read all of it back.
    unchanged = True
    for ordered, addresses in ((row['existing'], existing), (row['pending'], pending)):
        for index, item in enumerate(ordered):
            address = addresses[item['id']]
            successor = addresses[ordered[index + 1]['id']] if index + 1 < len(ordered) else 0
            fields = [sb.read(address + 8, 'I'), sb.read(address + 0x10, 'h'), sb.read(address + 0x12, 'h'),
                      sb.read(address + 0x14, 'h'), sb.read(address + 0x16, 'h')]
            if sb.read(address + 4, 'I') != successor or fields != [item['flags'], item['u'], item['v'],
                                                                    item['width'], item['height']]:
                unchanged = False
    return dict(order=order, area=s32(result.eax), unchanged=unchanged)


def render(coverage=False):
    path = executable_path('tpw')
    if path is None:
        raise SystemExit('requires the identified OPENTPW_TPW_EXE')
    sb = Sandbox(Image.load(path), [(ENTRY, DONE), (0x590FB0, 0x590FCB)], budget=200_000)
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
    output = ['// Generated by ui_page_merge_golden.py from isolated tp.exe 0x58EAE0.',
              '// Synthetic records and numeric results only; no original assets or executable bytes.',
              'namespace OpenTPW.Tests;', '', 'internal static class UiPageMergeGolden', '{',
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
                raise SystemExit('UiPageMergeGolden.cs differs from native output')
        elif len(sys.argv) == 1:
            print(text, end='')
        else:
            raise SystemExit('usage: ui_page_merge_golden.py [--coverage | --check FILE]')
