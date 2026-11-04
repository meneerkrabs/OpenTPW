"""tp.exe's sky draw 0x5863C0 (and its constructor 0x584EF0) run in the x86 oracle sandbox (docs/reverse/ORACLE.md, finding X78; UI-107).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified tp.exe (OPENTPW_TPW_EXE, SHA-256 pinned) are needed, nothing here is shipped. The sky object is made by its constructor 0x584EF0
(the grid 0x585720 with its rand() colours, the cylinder, the layer scroll) and drawn by 0x5863C0 with the render flags [0x8BCBC8] of the case. The device is stubbed: the record allocator 0x565240
hands out a record with a vertex buffer, the vertex transform (device vtable +4) copies the positions it is given with w = 1 and the clipper 0x565960 reports the index list it is handed, so every
record the draw makes can be read back: its state word, its vertices (position, colour, u, v) and the triangles. The run is the evidence of the globe's sky variant (render flag 0x2000000: the layer grid
mirrored below the horizon, the cylinder's other V rows).

Usage: python3 -I tools/ppc-analysis/oracle_x86/sky_oracle.py   (prints a summary of the records for the normal flags and the globe's)
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

CONSTRUCT, GRID, DRAW = 0x584EF0, 0x5856C0, 0x5863C0
ALLOC, CLIP, KEY = 0x565240, 0x565960, 0x5658C0
FLAGS, MODE, DEVICE, TRANSFORM_OBJECT, MATRIX_SOURCE, MASK_OBJECT = 0x8BCBC8, 0x8BD524, 0x879AB0, 0x87B1D8, 0x87B154, 0x87A248
RAND, FTOL = 0x67B5C0, 0x67A830
STRIDE = 0x20          # the vertex stride the draw uses (ebx): position x, y, z, w at +0, colour at +0x10, u at +0x18, v at +0x1C
GLOBE = 0x2000000


class SkyOracle:
    def __init__(self, exe: Path | None = None):
        path = exe or executable_path('tpw')
        if path is None:
            raise SystemExit('needs OPENTPW_TPW_EXE (tools/ppc-analysis/oracle_x86/README.md)')
        self.records: list = []
        self.rand_state = 0x1234
        stubs = {ALLOC: self._alloc, CLIP: self._clip, KEY: lambda sb: (1, 4), RAND: self._rand, 0x67A7D0: lambda sb: (0, 0)}
        allowed = [(CONSTRUCT, 0x586CC8), (FTOL, 0x67A860), (0x67B5C0, 0x67B5D0)]
        self.sandbox = Sandbox(Image.load(path), allowed, stubs=stubs, budget=3_000_000)
        trap = 0x71000000
        self.sandbox.uc.mem_map(trap, 0x1000, 5)
        self.sandbox.uc.mem_write(trap, b'\xCC' * 0x100)
        self.sandbox.stubs[trap] = self._transform
        self.sandbox.stubs[trap + 0x10] = lambda s: (0, 0)
        self.trap = trap

    def _rand(self, sb):
        self.rand_state = (self.rand_state * 214013 + 2531011) & 0xFFFFFFFF
        return (self.rand_state >> 16) & 0x7FFF, 0

    def _alloc(self, sb):
        """0x565240(device, 2, 4, 0x1C4, state, page), thiscall: a record with room for 0x100 vertices at +0x28; +4 holds the state word."""
        state, page = sb.arg(3), sb.arg(4)
        record = sb.alloc(0x60)
        vertices = sb.alloc(0x100 * STRIDE + 0x100)
        sb.write(record + 0x28, 'I', vertices)
        sb.write(record + 4, 'I', state)
        self.records.append({'record': record, 'vertices': vertices, 'state': state, 'page': page, 'triangles': None, 'index': len(self.records)})
        return record, 20

    def _clip(self, sb):
        """0x565960(device, &vertex count, indices, index count, mask), thiscall: reports the index list and leaves the vertex count."""
        out, indices, count, mask = sb.arg(0), sb.arg(1), sb.arg(2), sb.arg(3)
        sb.write(out, 'I', 0x100)
        last = self.records[-1]
        last['indices'] = [sb.read(indices + 2 * i, 'H') for i in range(count)]
        last['mask'] = mask
        return count // 3, 16

    def _transform(self, sb):
        """Device vtable +4, cdecl (object, matrix, vertices out, positions, out stride, position stride, count, flags): copies the positions with w = 1."""
        out, positions, out_stride, stride, count = sb.arg(2), sb.arg(3), sb.arg(4), sb.arg(5), sb.arg(6)
        for index in range(count):
            x, y, z = struct.unpack('<3f', bytes(sb.uc.mem_read(positions + index * stride, 12)))
            sb.write(out + index * out_stride, 'f', x)
            sb.write(out + index * out_stride + 4, 'f', y)
            sb.write(out + index * out_stride + 8, 'f', z)
            sb.write(out + index * out_stride + 12, 'f', 1.0)
        return 0, 0

    def run(self, flags: int = 0, layers: int | None = None, scale=(1.0, 1.0), offset=(0.0, 0.0), scroll=None):
        """The records one draw of a freshly built sky makes, in order, for the render flags `flags`; `scale` and `offset` are the textures' page values (+0x20, +0x24 and +0x28, +0x2C), `scroll` the
        per-layer offsets of the clouds ((u, v) per layer at +0x1FA0 + 0xC * layer + 8 and +0)."""
        sb = self.sandbox
        sb.reset()
        self.records = []
        self.rand_state = 0x1234
        sb.write(FLAGS, 'I', flags)
        sb.write(MODE, 'I', 1)
        # the device object: vtable slot +4 of the transform object is the stub, the record pool the draw checks ([device + 0x20]) has room
        pool = sb.alloc(0x40)
        for slot, value in ((0x20, 0), (0x24, 0), (0x28, 0), (0x2C, 0)):
            sb.write(pool + slot, 'I', value)
        device = sb.alloc(0x100)
        sb.write(device + 0x20, 'I', pool)
        sb.write(device + 0x1C, 'I', STRIDE)
        sb.write(device + 0x28, 'I', 0x7FFFFFFF)
        sb.write(device + 0x2C, 'I', 0x7FFFFFFF)
        sb.write(DEVICE, 'I', device)
        transform_object = sb.alloc(0x20)
        vtable = sb.alloc(0x40)
        sb.write(transform_object, 'I', vtable)
        sb.write(vtable + 4, 'I', self.trap)
        sb.write(vtable + 0x18, 'I', self.trap + 0x10)
        sb.write(TRANSFORM_OBJECT, 'I', transform_object)
        matrix = sb.alloc(64)
        sb.write(MATRIX_SOURCE, 'I', matrix)
        mask_object = sb.alloc(0x40)
        sb.write(mask_object + 0x2C, 'I', 0xFFFFFFFF)
        sb.write(MASK_OBJECT, 'I', mask_object)
        sky = sb.alloc(0x3100)
        sb.call(CONSTRUCT, this=sky)
        sb.call(GRID, this=sky)      # the park's grid: centre height 300 (0x5856C0 sets it and builds the grid 0x585720)
        # textures: any object with the cache page flag 0x1000 (byte +9 bit 0x10) and the page values
        for texture_slot in (0x1FD0, 0x1FD8):
            texture = sb.alloc(0x60)
            sb.write(texture + 8, 'I', 0x1000)
            sb.write(texture + 0x1C, 'I', 0x77)
            for field, value in ((0x20, scale[0]), (0x24, scale[1]), (0x28, offset[0]), (0x2C, offset[1])):
                sb.write(texture + field, 'f', value)
            sb.write(sky + texture_slot, 'I', texture)
        if layers is not None:
            sb.write(sky + 0x2EC8, 'I', layers)
        self.layer_count = sb.read(sky + 0x2EC8, 'I')
        for layer, (u, v) in enumerate(scroll or []):
            sb.write(sky + 0x1FA0 + 0xC * layer, 'f', v)
            sb.write(sky + 0x1FA0 + 0xC * layer + 8, 'f', u)
        self.records = []
        sb.call(DRAW, this=sky)
        return sky, self.records

    def vertices(self, record: dict, count: int = 0x100):
        sb = self.sandbox
        result = []
        for index in range(count):
            base = record['vertices'] + index * STRIDE
            x, y, z, w = struct.unpack('<4f', bytes(sb.uc.mem_read(base, 16)))
            colour = sb.read(base + 0x10, 'I')
            u, v = struct.unpack('<2f', bytes(sb.uc.mem_read(base + 0x18, 8)))
            result.append((x, y, z, colour, u, v))
        return result


def main() -> None:
    oracle = SkyOracle()
    for flags in (0, GLOBE):
        sky, records = oracle.run(flags)
        print(f'flags {flags:#x}: {len(records)} records')
        for record in records:
            print(f"  record {record['index']}: state {record['state']:#x}, page {record['page']:#x}, indices {len(record.get('indices', []))}, mask {record.get('mask')}")


if __name__ == '__main__':
    main()
