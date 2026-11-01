"""tp.exe's camera update 0x42B1C0 in camcorder walk mode (camera flags 2) run in the x86 oracle sandbox (docs/reverse/ORACLE.md, finding X58; UI-042).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified tp.exe (OPENTPW_TPW_EXE, SHA-256 pinned) are needed, nothing here is shipped. The whole update runs, one frame per call, with its real
callees (the matrix builders 0x46FB50, 0x46F150, 0x4702D0, 0x46F6F0, 0x46F870, the matrix compare 0x42B070, the ground height 0x452560 over a synthetic terrain object, the cell clamp 0x42CD40, the
float-to-cell helper 0x42CD80, the CRT's float-to-integer 0x67A830, modf 0x67B280 and fmod 0x67B24A). Stubbed, each answering from the case: the millisecond clock 0x402D80, the key state 0x40CA20 (by
action number), the walk edge test 0x4D8750 (called with mask 2), the ride entrance lookup 0x42A340, the ride it routine 0x4E15B0, the type lookup 0x412E90, the camera set 0x578BC0 and the matrix
copy 0x470040. The synthetic terrain is 128 x 128 cells of 10 units; heights and cell words come from the case.
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

UPDATE = 0x42B1C0
# every function the update reaches and the stubs leave to the real code: the update, the cell helpers, the matrix builders, the height lookup and the CRT's float helpers
RANGES = [
    (0x42B070, 0x42B1B6), (0x42B1C0, 0x42CD36), (0x42CD40, 0x42CD7F), (0x42CD80, 0x42CDB8), (0x452560, 0x4527E3), (0x46F150, 0x46F176), (0x46F350, 0x46F411), (0x46F5C0, 0x46F64E),
    (0x46F650, 0x46F6E4), (0x46F6F0, 0x46F865), (0x46F870, 0x46F909), (0x46FB50, 0x46FBA4), (0x4702D0, 0x4704B2), (0x67A830, 0x67A857), (0x67B24A, 0x67B254), (0x67B280, 0x67B3D1),
    (0x67B3E0, 0x67B40B), (0x6843E0, 0x684800), (0x6855B0, 0x6855C5), (0x6855D0, 0x685620), (0x685620, 0x6856DC), (0x6856E0, 0x685A04), (0x685EE0, 0x685F3C), (0x686080, 0x6860A8),
]
FLAGS, KEYS = 0x790AB0, 0x790AAC
POINTER = (0x790AC8, 0x790ACC)
CENTRE, HALF = (0x7A1C54, 0x7A1C58), (0x74D104, 0x74D108)
POS = (0x7908F0, 0x7908F4, 0x7908F8)
YAW, SMOOTH_YAW, ZOOM = 0x790A38, 0x7909E0, 0x7909EC
VEL = (0x790A88, 0x790A8C, 0x790A90)
LOOK_PITCH, LOOK_TURN = 0x790ADC, 0x790AE0
EYE = (0x790AA0, 0x790AA4, 0x790AA8)
MATRIX, LAST_TICK, INIT_FLAG = 0x790990, 0x7909E4, 0x7908E8
TERRAIN_OBJECT, MAP_WIDTH, MAP_DEPTH, KEY_TABLE = 0x7A0854, 0x7A0314, 0x7A0374, 0x787C14
TICK, KEY_STATE, EDGE, RIDE_AT, RIDE_IT, SET_CAMERA, LOOKUP, MATRIX_POINTER = 0x402D80, 0x40CA20, 0x4D8750, 0x42A340, 0x4E15B0, 0x578BC0, 0x412E90, 0x470040
WALK = 2
# the action numbers of the key table 0x748158 the update reads: Left (6) and numpad 9 (0x12), Right (7) and numpad 7 (0x13), Up (0) and numpad 1 (0x15), Down (1) and numpad 3 (0x16)
ACTIONS = {'left': (6, 0x12), 'right': (7, 0x13), 'forward': (0, 0x15), 'back': (1, 0x16)}


class WalkOracle:
    def __init__(self, exe: Path | None = None):
        path = exe or executable_path('tpw')
        if path is None:
            raise SystemExit('needs OPENTPW_TPW_EXE (tools/ppc-analysis/oracle_x86/README.md)')
        self.log: list = []
        self.pressed: set[int] = set()
        self.blocked = lambda cx, cz, direction: False
        self.ride = lambda cx, cz: False
        self.tick = 0
        self.things: dict[tuple[int, int], int] = {}
        stubs = {TICK: lambda sb: (self.tick, 0), KEY_STATE: self._key, EDGE: self._edge, RIDE_AT: self._ride_at, RIDE_IT: self._ride_it,
                 SET_CAMERA: lambda sb: (0, 4), LOOKUP: lambda sb: (0, 4), MATRIX_POINTER: lambda sb: (self.zero_matrix, 0)}
        self.sandbox = Sandbox(Image.load(path), RANGES, stubs=stubs, budget=400_000)
        self.zero_matrix = 0

    def _key(self, sb):
        return (1 if sb.arg(0) in self.pressed else 0), 4

    def _edge(self, sb):
        # the test reads the cell and the direction as bytes (0x4D8750: cl, al and bl); the caller's direction dword has stale upper bytes
        cx, cz, direction, mask = sb.arg(0) & 0xFF, sb.arg(1) & 0xFF, sb.arg(2) & 0xFF, sb.arg(3)
        blocked = 1 if self.blocked(cx, cz, direction) else 0
        self.log.append(('edge', cx, cz, direction, mask, blocked))
        return blocked, 0

    def _ride_at(self, sb):
        cx, cz = struct.unpack('<ii', struct.pack('<II', sb.arg(0), sb.arg(1)))
        if not self.ride(cx, cz):
            return 0, 0
        if (cx, cz) not in self.things:
            self.things[(cx, cz)] = sb.alloc(0x40)
            sb.write(self.things[(cx, cz)] + 0xE, 'H', 7)
        self.log.append(('ride_at', cx, cz))
        return self.things[(cx, cz)], 0

    def _ride_it(self, sb):
        self.log.append(('ride_it', sb.arg(0)))
        return 0, 4

    def run(self, case: dict) -> dict:
        """One frame of the update with the camera flags 2. `case` holds the terrain (`height(i, j)`, `cell(i, j)`), the key actions (`pressed`), the right button (`button`), the pointer and
        its reference, the position, yaw, zoom and velocity, the clock (`tick`, `last_tick`) and the stubs' answers (`blocked`, `ride`)."""
        sb = self.sandbox
        sb.reset()
        self.log = []
        self.things = {}
        self.pressed = set(case.get('pressed', ()))
        self.blocked = case.get('blocked', lambda cx, cz, direction: False)
        self.ride = case.get('ride', lambda cx, cz: False)
        sb.set_fpcw(0x7F)
        width = height = 128
        holder = sb.alloc(0x100)
        terrain = sb.alloc(0x40)
        heights = sb.alloc(4 * (width + 1) * (height + 1))
        words = sb.alloc(4 * width * height)
        for j in range(height + 1):
            for i in range(width + 1):
                sb.write(heights + 4 * (j * (width + 1) + i), 'f', case['height'](i, j))
        for j in range(height):
            for i in range(width):
                sb.write(words + 4 * (j * width + i), 'I', case['cell'](i, j))
        sb.write(holder + 0x6C, 'I', terrain)
        for offset, value in ((0x8, 0.0), (0xC, 0.0), (0x10, 10.0), (0x14, 10.0)):
            sb.write(terrain + offset, 'f', value)
        sb.write(terrain + 0x18, 'I', width); sb.write(terrain + 0x1C, 'I', height)
        sb.write(terrain + 0x28, 'I', heights); sb.write(terrain + 0x2C, 'I', words)
        sb.write(TERRAIN_OBJECT, 'I', holder)
        sb.write(MAP_WIDTH, 'I', width); sb.write(MAP_DEPTH, 'I', height)
        self.zero_matrix = sb.alloc(64, 16)
        sb.write(FLAGS, 'I', WALK)
        sb.write(KEYS, 'I', 0x80 | (4 if case.get('button') else 0))
        for address, value in zip(POINTER, case['pointer']):
            sb.write(address, 'f', value)
        for address, value in zip(CENTRE, case['centre']):
            sb.write(address, 'f', value)
        for address, value in zip(HALF, case['half']):
            sb.write(address, 'f', value)
        sb.write(POS[0], 'f', case['pos'][0]); sb.write(POS[1], 'f', 0.0); sb.write(POS[2], 'f', case['pos'][1])
        sb.write(YAW, 'f', case['yaw']); sb.write(SMOOTH_YAW, 'f', case['yaw']); sb.write(ZOOM, 'f', case['zoom'])
        for address, value in zip(VEL, case['vel']):
            sb.write(address, 'f', value)
        sb.write(LAST_TICK, 'I', case['last_tick'])
        sb.write(INIT_FLAG, 'I', 0)
        sb.write(KEY_TABLE, 'I', 0x7000)
        self.tick = case['tick']
        sb.call(UPDATE)
        read = sb.read
        return {
            'pos': (read(POS[0], 'f'), read(POS[2], 'f')), 'yaw': read(YAW, 'f'), 'smooth': read(SMOOTH_YAW, 'f'), 'vel': tuple(read(a, 'f') for a in VEL),
            'pitch': read(LOOK_PITCH, 'f'), 'turn': read(LOOK_TURN, 'f'), 'eye': tuple(read(a, 'f') for a in EYE),
            'matrix': tuple(read(MATRIX + 4 * i, 'f') for i in range(16)), 'flags': read(FLAGS, 'I'), 'zoom': read(ZOOM, 'f'), 'log': list(self.log), 'executed': sb.executed,
        }
