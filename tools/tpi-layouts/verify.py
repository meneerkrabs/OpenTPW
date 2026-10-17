"""Checks ThemeParkIncBodyLayout.Data.cs against the emulated load routines.

For each model it runs the routine on streams whose ring counts are small random numbers and at capacity, parses the layout text of the C# file with the same grammar as
ThemeParkIncBodyLayout.Parse and compares the width and the (offset, size) of every read. Usage: python3 verify.py [path to ThemeParkIncBodyLayout.Data.cs]
"""
import os, random, re, sys
from run import run
from models import CONSTANTS

DATA = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', '..', 'source', 'OpenTPW.Files', 'Formats', 'Save', 'ThemeParkIncBodyLayout.Data.cs')
text = open(DATA).read()
def const(name):
    m = re.search(r'private const string ' + name + r' = """\n(.*?)\n\t*""";', text, re.S)
    return [l.strip() for l in m.group(1).split('\n') if l.strip()]
FRAGMENTS = {'person': const('PersonFragment'), 'staff': const('StaffFragment')}
ROLES = {'mechanic': 'MechanicBody', 'handyman': 'HandymanBody', 'entertainer': 'EntertainerBody', 'guard': 'GuardBody', 'researcher': 'ResearcherBody', 'gardener': 'GardenerBody'}

def parse(lines):
    out, stack = [], []
    cur = out
    for line in lines:
        t = line.split()
        if t[0] == 'use':
            cur.extend(parse(FRAGMENTS[t[1]]))
        elif t[0] == 'end':
            kind, name, count, extra, parent = stack.pop()
            parent.append((kind, name, count, extra, cur)); cur = parent
        elif t[0] in ('rep', 'group', 'table'):
            stack.append((t[0], t[-1], int(t[1]) if t[0] == 'rep' else 1, t[1] if t[0] == 'table' else '', cur)); cur = []
        elif t[0] == 'ring':
            cur.append(('ring', t[3], int(t[1]), int(t[2]), None))
        else:
            cur.append(('field', t[1], int(t[0]), 0, None))
    return out

def walk(buf, pos, segs, reads, caps):
    """Returns the position after the segments, appending (offset, size) for every read in the routine's order."""
    for kind, name, a, b, kids in segs:
        if kind == 'field':
            reads.append((pos, a)); pos += a
        elif kind == 'rep':
            for _ in range(a): pos = walk(buf, pos, kids, reads, caps)
        elif kind == 'group':
            pos = walk(buf, pos, kids, reads, caps)
        elif kind == 'table':
            n = int.from_bytes(buf[pos:pos + 4], 'little', signed=True); reads.append((pos, 4)); pos += 4
            for _ in range(max(n, 0)): pos = walk(buf, pos, kids, reads, caps)
        else:
            elem, cap = a, b
            n = int.from_bytes(buf[pos + 4:pos + 8], 'little')
            assert n <= cap, 'ring over capacity'
            reads += [(pos, 4), (pos + 4, 4), (pos + 8, 1), (pos + 9, elem)]; pos += 9 + elem
            for _ in range(n): reads.append((pos, elem)); pos += elem
    return pos

def tiles(layout, emulated):
    """Every emulated read is exactly one layout field, or a run of consecutive layout fields (the tag system's mail record is one 0xe2c-byte read, three fields in the layout)."""
    i = 0
    for offset, size in emulated:
        total = 0
        if i >= len(layout) or layout[i][0] != offset: return False
        while total < size and i < len(layout):
            total += layout[i][1]; i += 1
        if total != size: return False
    return i == len(layout)

def rings(segs):
    """(capacity of every ring and table in read order) for the 'at capacity' stream."""
    out = []
    def go(s):
        for kind, name, a, b, kids in s:
            if kind == 'rep':
                for _ in range(a): go(kids)
            elif kind in ('group',): go(kids)
            elif kind == 'table': out.append(2)
            elif kind == 'ring': out.append(b)
    go(segs); return out

failed = 0
for model, const_name in CONSTANTS.items():
    segs = parse(const(const_name))
    for label, counts in (('empty', []), ('random', [random.Random(i).choice([0, 1, 2, 3]) for i in range(60)]), ('capacity', rings(segs))):
        e = run(model, counts)
        reads, buf = [], e.buf
        try:
            width = walk(buf, 0, segs, reads, None)
        except Exception as exc:
            width = f'layout failed: {exc}'
        ok = e.fail is None and width == e.cur and tiles(reads, [(c, s) for c, s, *_ in e.events])
        failed += not ok
        print(f"{'ok  ' if ok else 'FAIL'} {model:12} {label:9} emulated {e.cur:6} layout {width}")
sys.exit(1 if failed else 0)
