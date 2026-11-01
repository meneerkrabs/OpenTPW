"""Golden cases of the track tile chooser (docs/reverse/ORACLE.md, finding X51; BLUEPRINT-003, UI-100): tp.exe's own routine 0x539760 run on random tiles in the x86 oracle.

Every case is a 2 x 2 tile with random map cells (types, flags, owners, class-3 owners of overwrite priority 0 to 5), second-layer records (the tile's origin record of a track piece, a station, a post or none; extensions
of a record elsewhere; a foreign record), the line state (first, last, hovered start), the sticky words and the cost carried in, the tool (kart and water line, edit, cut, fence) and the park's money. The answer, the
sticky words 0x816D50, 0x816D58, 0x816D54, 0x816D5C, 0x816D4C, 0x816D60 and 0x816D48, the cost and the overwrite priority after it are recorded. OpenTPW.Tests replays every case on TrackTileChooser.
Usage: python3 -I tools/ppc-analysis/oracle_x86/ghost_tile_golden.py > source/OpenTPW.Tests/GhostTileGolden.cs (needs Unicorn and OPENTPW_TPW_EXE).
"""
from __future__ import annotations

import base64
import json
import random
import sys
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from ghost_oracle import ANCHORS, GhostOracle, TOOL_OWNER  # noqa: E402

MAP = 128
CASES = 600


def make(rng):
    mode=rng.choice((0xb,0x10,0xb,0x10,0xe,0x13,0xf,0x19,0x39))
    ox,oy=rng.randint(30,60),rng.randint(30,60)
    types=[0]*8+[1,3,4,9,10,7,0x1e,0x15,2]
    cells={}
    anchors={a:rng.randint(0,5) for a in (1,2,3) if rng.random()<0.7}
    for dx in (0,1):
        for dy in (0,1):
            t=rng.choice(types)
            owner=0
            if t in (4,9,10): owner=rng.choice((ANCHORS[1],ANCHORS[2],ANCHORS[3],TOOL_OWNER,0))
            cells[(ox+dx,oy+dy)]={'type':t,'flags':rng.choice([0]*10+[0x20,0x40]),'owner':owner}
    recs={}
    own_kinds=[0,0,0xb,0xb,0xd,0x10,0x10,0x12,0x19,7]
    k=rng.choice(own_kinds)
    if k or rng.random()<0.2:
        links=rng.choice([0,1,4,0x10,0x40,5,0x14,0x41,0x50,0x11,0x44,0x15,0x45,0x55,0x05|0x40])
        flags=rng.choice([0,0,0,1,2,4,8,0x10,0x11,0x40,0x20])
        recs[(ox,oy)]=(k,links,0,rng.choice((0,1,4,0x10,0x40)),flags,rng.choice((TOOL_OWNER,TOOL_OWNER,TOOL_OWNER+1,0)))
    for (dx,dy) in ((1,0),(1,1),(0,1)):
        r=rng.random()
        if r<0.25:
            recs[(ox+dx,oy+dy)]=(rng.choice((0xc,0x11)),0,(rng.choice((oy*MAP+ox+1,110*MAP+110+1))),0,0,0)
        elif r<0.35:
            recs[(ox+dx,oy+dy)]=(rng.choice((0xb,0xd,0x10,0x19)),0,0,0,0,rng.choice((TOOL_OWNER,TOOL_OWNER+1)))
    recs[(110,110)]=(rng.choice((0xb,0x10,0x19,0xd,0x12,0)),rng.choice((0x11,0x44)),0,0,0,rng.choice((TOOL_OWNER,TOOL_OWNER+1)))
    G={0x816D50:rng.choice((0,0,0,1)),0x816D58:rng.choice((0,0,0,1)),0x816D54:rng.choice((0,1)),0x816D5C:0,0x816D4C:0,0x816D60:0,
       0x81AF4C:rng.choice((0,0,10,100)),0x820AC8:rng.choice((0,1)),0x820AD0:rng.choice((0,1)),0x818688:rng.choice((0,1)),0x818698:TOOL_OWNER,
       0x818C10:rng.choice((2,3,4)),0x8186D4:rng.choice((0,0,0,1)),0x8186D0:rng.choice((0,1)),0x81AEBC:rng.choice((0,10,50)),0x81B43C:rng.choice((0,20,60)),
       0x7639F0:ox,0x7639F4:oy,0x7639F8:rng.choice((ox,ox+3)),0x7639FC:rng.choice((oy,oy+3))}
    if rng.random()<0.3: G[0x7639F0]=ox+5
    if rng.random()<0.65:
        mode=rng.choice((0xb,0x10,0xb,0x10,0xe,0x13,0xf))
        for key in cells: cells[key]={'type':rng.choice([0]*12+[4,9]),'flags':0,'owner':rng.choice((ANCHORS[1],0)) }
        own={0xb:(0xb,0xd),0x10:(0x10,0x12)}.get(mode,(0xb,0x10))
        k=rng.choice([own[0],own[0],own[0],own[1],0,own[0]])
        links=rng.choice([0,1,4,0x10,0x40,5,0x14,0x41,0x50,0x11,0x44,0x15,0x45,0x55,0x07,0x0f,0x77])
        recs[(ox,oy)]=(k,links,0,rng.choice((0,1,4,0x10,0x40)),rng.choice([0,0,0,1,2,4,8,0x10,0x11,0x12,0x14,0x18,0x0f]),rng.choice((TOOL_OWNER,TOOL_OWNER,TOOL_OWNER,TOOL_OWNER+1)))
        for d in ((1,0),(1,1),(0,1)): recs.pop((ox+d[0],oy+d[1]),None)
        G[0x816D50]=G[0x816D58]=0; G[0x8186D4]=0
        return {'mode':mode,'second':rng.choice((0,0,1)),'direction':rng.choice((0,1,4,0x10,0x40)),'origin':(ox,oy),'cells':cells,'records':recs,'anchors':anchors,'globals':G,'bank':None}
    return {'mode':mode,'second':rng.choice((0,0,1)),'direction':rng.choice((0,1,4,0x10,0x40)),'origin':(ox,oy),'cells':cells,'records':recs,'anchors':anchors,'globals':G,'bank':rng.choice((None,0,15,200,5000))}


def render() -> str:
    oracle = GhostOracle()
    oracle.start([], [])
    rng = random.Random('ghost-tile-x49')  # the seed text is a label only (the finding was renumbered later); changing it changes every case
    lines = []
    for _ in range(CASES):
        case = make(rng)
        result = oracle.tile(case)
        lines.append(json.dumps({
            'mode': case['mode'], 'second': case['second'], 'direction': case['direction'], 'origin': list(case['origin']),
            'cells': [[x, y, c['type'], c['flags'], c['owner']] for (x, y), c in sorted(case['cells'].items())],
            'records': [[x, y, *r] for (x, y), r in sorted(case['records'].items())],
            'anchors': {str(ANCHORS[a]): p for a, p in sorted(case['anchors'].items())},
            'globals': {str(k): v for k, v in sorted(case['globals'].items())},
            'bank': case['bank'],
            'answer': result['answer'], 'latches': result['latches'], 'cost': result['cost'], 'priority': result['prio']}, separators=(',', ':')))
    packed = base64.b64encode(zlib.compress('\n'.join(lines).encode(), 9)).decode()
    out = ['// Generated by tools/ppc-analysis/oracle_x86/ghost_tile_golden.py from tp.exe\'s own track tile chooser 0x539760 run in the x86 oracle (docs/reverse/ORACLE.md, X51).',
           '// Synthetic tiles, no original data. One JSON object per line when unpacked: the tool and line state, the four cells, the second-layer records, the globals carried in, the money, and the answer, the sticky',
           '// words (0x816D50, 0x816D58, 0x816D54, 0x816D5C, 0x816D4C, 0x816D60, 0x816D48), the cost 0x81AF4C and the priority 0x818C14 after it.',
           'namespace OpenTPW.Tests;', '', 'internal static class GhostTileGolden', '{',
           f'\t/// <summary>The oracle\'s {len(lines)} cases, packed.</summary>', '\tpublic const string Packed =']
    chunk = 120
    parts = [packed[i:i + chunk] for i in range(0, len(packed), chunk)]
    out += [f'\t\t"{part}"' + (' +' if i + 1 < len(parts) else ';') for i, part in enumerate(parts)]
    out += ['', f'\tpublic const int CaseCount = {len(lines)};', '}', '']
    return '\n'.join(out)


def main() -> int:
    if len(sys.argv) == 3 and sys.argv[1] == '--check':
        if render() != Path(sys.argv[2]).read_text():
            print('GhostTileGolden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())
