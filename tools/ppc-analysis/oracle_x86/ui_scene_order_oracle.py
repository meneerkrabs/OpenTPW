"""Check the original UI scene attachment list, not the creation registry.

Dev-only, identified OPENTPW_TPW_EXE and Unicorn required. Calls the real category
router 0x576410, list insertion 0x576270 and removal 0x576540 on synthetic scene
nodes. Only the nodes' kind getter is stubbed (1 light, 2 model). This proves list
ordering/lifetime, not a window's show-event order, page packing or final pixels.
It also runs the width classifier 0x590CF0, including its explicit-class override.
No original executable bytes are included or printed.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

INSERT = 0x576410
REMOVE = 0x576540
KIND = 0x78000010  # a synthetic entry on the harness's mapped executable sentinel page


def setup(image: Image):
    if image.identity != 'tpw':
        raise ValueError('requires the identified tp.exe')
    sb = Sandbox(image, [(0x576270, 0x576304), (INSERT, 0x57653E), (REMOVE, 0x5765B7)],
                 stubs={KIND: lambda sb: (sb.read(sb.reg('ecx') + 4, 'I'), 0)}, budget=20_000)
    sb.reset()
    scene = sb.object(0x80)
    # A controlled free-list pool: scene+4 is the free head, +0xc its used count.
    nodes = [sb.object(8) for _ in range(16)]
    for node, following in zip(nodes, nodes[1:] + [0]):
        sb.write(node + 4, 'I', following)
    sb.write(scene + 4, 'I', nodes[0])
    vtable = sb.object(16, {8: ('I', KIND)})
    subjects = [sb.object(8, {0: ('I', vtable), 4: ('I', kind)}) for kind in (2, 2, 2, 1, 1)]
    return sb, scene, subjects


def contents(sb: Sandbox, scene: int, category: int):
    node = sb.read(scene + 0x10 + (category >> 4) * 4, 'I')
    result = []
    while node:
        if len(result) >= 16:
            raise AssertionError('scene list cycle')
        result.append(sb.read(node, 'I'))
        node = sb.read(node + 4, 'I')
    return result


def change(sb: Sandbox, entry: int, scene: int, subject: int, category: int):
    result = sb.call(entry, args=(subject, category), this=scene)
    assert result.esp_delta == 0, result.esp_delta
    assert result.eax == 1, hex(entry)


def verify(image: Image) -> int:
    cases = 0
    for category in (0x10, 0x20, 0x70):
        sb, scene, (a, b, c, light, light2) = setup(image)
        for subject in (a, b, c):
            change(sb, INSERT, scene, subject, category)
        assert contents(sb, scene, category) == [c, b, a]
        cases += 1
        change(sb, REMOVE, scene, b, category)
        assert contents(sb, scene, category) == [c, a]
        change(sb, INSERT, scene, b, category)
        assert contents(sb, scene, category) == [b, c, a]
        cases += 1
        # Type-1 nodes always prepend. Later models go after the leading lights,
        # while a new light goes before the existing lights and models.
        change(sb, INSERT, scene, light, category)
        change(sb, REMOVE, scene, a, category)
        change(sb, INSERT, scene, a, category)
        change(sb, INSERT, scene, light2, category)
        assert contents(sb, scene, category) == [light2, light, a, b, c]
        cases += 1
        # A free node can be reused without moving the other attached instances.
        change(sb, REMOVE, scene, light, category)
        assert contents(sb, scene, category) == [light2, a, b, c]
        assert sb.read(scene + 0xC, 'I') == 4
        cases += 1
    # Width classes are only a page allocator input, not the actual page key.
    classifier = Sandbox(image, [(0x590CF0, 0x590D3E)], budget=100)
    for width in (0, 1, 8, 9, 16, 17, 32, 33, 64, 65, 128, 129, 256):
        classifier.reset()
        slot = classifier.object(0x20, {8: ('I', 0), 0x14: ('h', width)})
        result = classifier.call(0x590CF0, this=slot)
        expected = next((i for i, threshold in enumerate((128, 64, 32, 16, 8)) if width > threshold), 5)
        assert result.eax == expected, (width, result.eax, expected)
        cases += 1
    for forced in range(8):
        classifier.reset()
        slot = classifier.object(0x20, {8: ('I', 0x30 | forced), 0x14: ('h', 256)})
        assert classifier.call(0x590CF0, this=slot).eax == forced
        cases += 1
    return cases


if __name__ == '__main__':
    path = executable_path('tpw')
    if path is None:
        raise SystemExit('OPENTPW_TPW_EXE must name the identified no-CD tp.exe')
    print(f'{verify(Image.load(path))} scene order/lifetime cases verified')
