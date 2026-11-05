"""tp.exe's scene list insertion 0x576270 and the order its lists reach the UI part batcher in, run in the x86 oracle sandbox (docs/reverse/ORACLE.md, finding X82; UI-004).

Dev-only tooling like the rest of oracle_x86 (Unicorn and the identified tp.exe, OPENTPW_TPW_EXE, are needed). The scene object keeps one linked list per category (the add 0x576410 takes the category from bits 0x70 of its
flags: 0x10 is list +0x14, 0x20 is +0x18, ... 0x70 is +0x2C) and the insertion 0x576270 puts an object with the type [vtable+8] = 2 (every model instance) in front of the other type 2 objects and behind the leading run of
type 1 objects. A UI control is added with 0x20 when it is shown (0x476BF0 -> 0x45BD30, third argument 0). The scene draw (0x576B86) walks the lists in category order and calls the instance draw 0x57A750 per node, which
appends the instance's parts to the batcher's list (0x581CD0) in that order, so the part list is the scene list; `draw_order` runs the real insertion for a sequence of shows and then the real batcher (`ui_batch_oracle`)
on the resulting part list, with the textures' cache pages chosen by the caller.

Usage: python3 -I tools/ppc-analysis/oracle_x86/ui_scene_oracle.py   (prints the list order and the batch order of a few synthetic frames)
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import ui_batch_oracle as batch  # noqa: E402  (first: it moves the sandbox's stack for the batcher's large frame, and both sandboxes must agree)
from x86_harness import Image, Sandbox, executable_path  # noqa: E402

SCENE_INSERT = 0x576270
TYPE_ONE_STUB = 0x5775ED   # `mov eax, 1; ret` inside the registry add; stands for a vtable slot that answers 1 (the type the frame's flag 8 branch 0x576D1C hands to the light table 0x584BE0)
TYPE_TWO = 0x40F260        # `mov eax, 2; ret`: the instance's own slot 2 (vtable 0x7019F8)


def _sandbox():
    path = executable_path('tpw')
    if path is None:
        raise SystemExit('needs OPENTPW_TPW_EXE (tools/ppc-analysis/oracle_x86/README.md)')
    return Sandbox(Image.load(path), [(SCENE_INSERT, 0x576304), (TYPE_ONE_STUB, 0x5775F3), (TYPE_TWO, 0x40F266)], budget=200_000)


_sb = None


def scene_lists(adds, categories=8):
    """Runs 0x576270 for each (name, type, category) in add order and returns {category: [names in list order]}.

    `type` is what the object's vtable slot 2 answers (1 or 2; every model instance answers 2); category is 1..7 (the flags' bits 0x70 divided by 0x10)."""
    global _sb
    if _sb is None:
        _sb = _sandbox()
    sb = _sb
    sb.reset()
    scene = sb.alloc(0x100)
    # the node pool: the free list is [scene+4], the used count [scene+0xC]
    nodes = sb.alloc(8 * (len(adds) + 1))
    for index in range(len(adds)):
        sb.write(nodes + 8 * index + 4, 'I', nodes + 8 * (index + 1) if index + 1 < len(adds) else 0)
    sb.write(scene + 4, 'I', nodes)
    objects = {}
    for name, kind, category in adds:
        vtable = sb.alloc(0x10)
        sb.write(vtable + 8, 'I', TYPE_ONE_STUB if kind == 1 else TYPE_TWO)
        obj = sb.alloc(0x10)
        sb.write(obj, 'I', vtable)
        objects[obj] = name
        head = scene + 0x10 + 4 * category
        sb.call(SCENE_INSERT, args=(head, obj), this=scene)
    result = {}
    for category in range(1, categories):
        names = []
        node = sb.read(scene + 0x10 + 4 * category, 'I')
        while node:
            names.append(objects[sb.read(node, 'I')])
            node = sb.read(node + 4, 'I')
        if names:
            result[category] = names
    return result


def draw_order(shows):
    """The order the batcher draws the shown controls' material runs in.

    `shows` is the order the controls were shown in (oldest first), each (name, [(texture id, alpha, page or None), ...]); the texture id stands for the texture and the page for its cache page
    surface (default: one page per texture). Returns the control names in draw order, one entry per material run."""
    lists = scene_lists([(name, 2, 2) for name, _ in shows])
    order = lists.get(2, [])
    materials = dict(shows)
    parts = [dict(mats=materials[name]) for name in order]
    emitted = batch.build(parts)
    names = []
    for entry in emitted:
        if entry.startswith('e(p'):
            names.append(order[int(entry[3:entry.index(',')])])
    return names


if __name__ == '__main__':
    print('adds A(2) B(2) C(1) D(2) E(1) in category 2 ->', scene_lists([('A', 2, 2), ('B', 2, 2), ('C', 1, 2), ('D', 2, 2), ('E', 1, 2)]))
    print('categories 1 and 2 ->', scene_lists([('A', 2, 2), ('B', 2, 1), ('C', 2, 2), ('D', 2, 1)]))
    print('panel, button, label shown in that order; own pages ->', draw_order([('panel', [(1, 0)]), ('button', [(2, 0)]), ('label', [(3, 0)])]))
    print('panel and button on one page ->', draw_order([('panel', [(1, 0, 5)]), ('button', [(2, 0, 5)])]))
    print('panel and label share a texture ->', draw_order([('panel', [(1, 0)]), ('button', [(2, 0)]), ('label', [(1, 0)])]))
