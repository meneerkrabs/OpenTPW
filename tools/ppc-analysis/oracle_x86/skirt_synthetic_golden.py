"""Golden vectors of the procedural object base routines of tp.exe (docs/reverse/ORACLE.md X28 to X32; issue 829, package F).

Small synthetic inputs (not original data) run through the original routines in the x86 oracle: the footprint edge mask 0x52c420, the UV normaliser 0x467310, the skirt
entries and opaque slot flags 0x46aa10, the edge vertex conform 0x469940 / 0x469a80 and the base height 0x466d70. Prints C# source for `ObjectSkirtSyntheticGolden.cs`; every
case's expected values are what the ORIGINAL routine produced, and each is checked against the Python reading (skirt_reading.py) while it is generated.

Usage: OPENTPW_TPW_EXE=/path/tp.exe python3 -I skirt_synthetic_golden.py > source/OpenTPW.Tests/ObjectSkirtSyntheticGolden.cs
Dev-only: needs Unicorn (pip install unicorn==2.1.4) and the identified tp.exe. Deterministic: the cases come from seeded streams, the routines run at the x87 control
word 0x7F, tp.exe's mode (docs/reverse/ORACLE.md 7.21).
"""
from __future__ import annotations

import struct
import sys
from decimal import Decimal
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import skirt_reading as reading  # noqa: E402
import x86_targets as targets  # noqa: E402
from x86_harness import UNICORN_HINT, Image, executable_path, have_unicorn, seeded  # noqa: E402


# ---------------------------------------------------------------------------------------------------------------- C# literals
def f(value: float) -> str:
    """The shortest decimal that reads back as the same float32, as a C# literal."""
    value = reading.f32(value)
    if value == 0.0:
        return '-0f' if str(value).startswith('-') else '0f'
    for digits in range(1, 10):
        text = f'{value:.{digits}g}'
        if reading.f32(float(text)) == value:
            break
    if 'e' in text:
        exponent = int(text.split('e')[1])
        if -5 <= exponent < 9:
            text = format(Decimal(text), 'f')
        else:
            mantissa = text.split('e')[0]
            text = f'{mantissa}e{exponent}'
    return text + 'f'


def floats(values) -> str:
    return '[' + ', '.join(f(v) for v in values) + ']'


def ints(values) -> str:
    return '[' + ', '.join(str(int(v)) for v in values) + ']'


def flat(rows) -> list:
    return [c for row in rows for c in row]


def bits_floats(bits) -> str:
    return floats(reading.bits_f32(b) for b in bits)


class Writer:
    def __init__(self) -> None:
        self.mismatches = 0
        self.lines: list = []

    def check(self, name: str, original, other) -> None:
        if original != other:
            self.mismatches += 1
            print(f'MISMATCH {name}', file=sys.stderr)


# ---------------------------------------------------------------------------------------------------------------- cases
def mask_cases(writer: Writer, sandbox, target) -> list:
    rng = seeded('skirt-golden:mask')
    shapes = []
    named = [('1x1', 1, 1, 'full'), ('2x2', 2, 2, 'full'), ('3x3', 3, 3, 'full'), ('4x3', 4, 3, 'full'), ('5x5', 5, 5, 'full'), ('1x4', 1, 4, 'full'),
             ('16x16', 16, 16, 'full'), ('17x2', 17, 2, 'full'), ('20x1', 20, 1, 'full'), ('20x20', 20, 20, 'full')]
    for name, width, height, style in named:
        shapes.append((name, targets.skirt_shape(rng, width, height, style)))
    for index in range(26):
        width, height = rng.randint(1, 6), rng.randint(1, 6)
        style = ('holes', 'footprint', 'mixed')[index % 3]
        shapes.append((f'random-{index:02d}-{style}', targets.skirt_shape(rng, width, height, style)))
    shapes.append(('wide-random-a', targets.skirt_shape(rng, 20, 3, 'footprint')))
    shapes.append(('wide-random-b', targets.skirt_shape(rng, 18, 4, 'holes')))
    out = []
    for name, shape in shapes:
        original = targets.run_binary(target, sandbox, shape)
        writer.check('mask ' + name, original, reading.mask(shape))
        types = [shape['cells'][row * 20 + col] for row in range(shape['height']) for col in range(shape['width'])]
        out.append((name, shape['width'], shape['height'], types, sorted(reading.mask_positions(shape))))
    return out


def uv_cases(writer: Writer, sandbox, target) -> list:
    rng = seeded('skirt-golden:uv')
    out = []
    kinds = ['snap', 'negative', 'shared', 'ground', 'off-ground', 'slot-8000', 'no-mask', 'mask-ones', 'mask-zeros', 'eps']
    for index in range(40):
        kind = kinds[index % len(kinds)]
        count = rng.randint(1, 2)
        cells = list(dict.fromkeys((rng.randrange(0, 3), rng.randrange(0, 3)) for _ in range(count)))
        node = targets.skirt_floor_node(rng, cells, rotate=kind != 'snap' or index % 2 == 0, share_corners=kind == 'shared', matrix_kind='identity',
                                        elevated=3.0 if kind == 'off-ground' else (0.09 if index % 7 == 3 else 0.0), slots=min(2, len(cells)))
        if kind == 'slot-8000':
            node['slots'] = [0x8030 for _ in node['slots']]
        if kind in ('ground', 'snap', 'negative', 'shared', 'eps'):
            node['slots'] = [0x0030 | (0x0800 if index % 3 == 0 else 0) for _ in node['slots']]
        vertices = [tuple(v) for v in reading.transform_vertices(node)]
        if kind == 'no-mask':
            mask, verts = None, None
        else:
            if kind == 'mask-zeros':
                mask = bytes(256)
            elif kind == 'mask-ones':
                mask = bytes([1]) * 256
            else:
                mask = bytes(1 if any((col * 16 + row) == i for col, row in cells) or i % 7 == 3 else 0 for i in range(256))
            verts = vertices
        x = {'node': node, 'eps': reading.SNAP if kind != 'eps' else reading.f32(rng.choice((0.0, 0.01, 0.05, 0.1, 0.25))), 'mask': mask, 'vertices': verts}
        original = targets.run_binary(target, sandbox, x)
        writer.check(f'uv {index}', original, targets.skirt_uv_plan(x))
        out.append((f'uv-{index:02d}-{kind}', x, original))
    return out


def flags_cases(writer: Writer, sandbox, target) -> list:
    rng = seeded('skirt-golden:flags')
    out = []
    shapes = [(1, 1), (2, 1), (1, 2), (2, 2), (2, 2), (3, 3), (2, 2), (1, 1)]
    for index in range(40):
        width, height = shapes[index % len(shapes)]
        style = ('footprint', 'holes', 'full', 'footprint')[index % 4]
        shape = targets.skirt_shape(rng, width, height, style)
        if (width, height) == (3, 3):
            shape = targets.skirt_shape(rng, 3, 3, 'full')
            if index % 2:
                shape['cells'][1 * 20 + 1] = 11
        footprint = [(col, row) for row in range(height) for col in range(width) if shape['cells'][row * 20 + col]]
        if not footprint:
            shape['cells'][0] = 4
            footprint = [(0, 0)]
        matrix_kind = ('identity', 'identity', 'turn', 'identity', 'general', 'turn')[index % 6]
        node = targets.skirt_floor_node(rng, footprint, rotate=index % 5 != 4, share_corners=index % 3 == 2, elevated=2.5 if index % 11 == 10 else 0.0,
                                        skew=index % 13 == 12, matrix_kind=matrix_kind)
        if index % 17 == 16:
            node = targets.skirt_merge_nodes(node, targets.skirt_wall_node(rng, footprint[:2]))
        x = {'shape': shape, 'width': width, 'height': height, 'hdr80': index % 6 == 5, 'cell': (10.0, 10.0), 'node': node, 'ground': True}
        original = targets.run_binary(target, sandbox, x)
        writer.check(f'flags {index}', original, targets.skirt_flags_plan(x))
        out.append((f'flags-{index:02d}-{matrix_kind}{"-hdr80" if x["hdr80"] else ""}{"-skewed" if index % 13 == 12 else ""}', x, original))
    # a footprint cell no face lies in: the error message, the default entry {1, 10000}; with the header bit 0x80 the empty cell is asked for too
    for extra, hdr80 in enumerate((False, True)):
        shape = {'width': 2, 'height': 1, 'cells': [4, 4] + [0] * 398}
        node = targets.skirt_floor_node(rng, [(0, 0)], rotate=True, matrix_kind='identity')
        x = {'shape': shape, 'width': 2, 'height': 1, 'hdr80': hdr80, 'cell': (10.0, 10.0), 'node': node, 'ground': True}
        original = targets.run_binary(target, sandbox, x)
        writer.check(f'flags empty cell {extra}', original, targets.skirt_flags_plan(x))
        out.append((f'flags-empty-cell-{"hdr80" if hdr80 else "plain"}', x, original))
    return out


def conform_cases(writer: Writer, sandbox, target) -> list:
    rng = seeded('skirt-golden:conform')
    out = []
    for index in range(40):
        x = targets.conform_case(rng, index, small=True)
        original = targets.run_binary(target, sandbox, x)
        writer.check(f'conform {index}', original, targets.conform_plan(x))
        out.append((f'conform-{index:02d}', x, original))
    return out


def base_height_cases(writer: Writer, sandbox, target) -> list:
    rng = seeded('skirt-golden:base-height')
    out = []
    for index in range(40):
        width, height = rng.randint(1, 3), rng.randint(1, 3)
        rotation = (0, 90, 180, 270)[index % 4]
        shape = targets.skirt_shape(rng, width, height, ('mixed', 'footprint', 'holes', 'full')[(index // 4) % 4])
        cells_x, cells_z = rng.randint(max(width, height), 5), rng.randint(max(width, height), 5)
        extent = (width, height) if rotation in (0, 180) else (height, width)
        box_x, box_z = rng.randint(0, cells_x - extent[0]), rng.randint(0, cells_z - extent[1])
        anchor = {0: (box_x, box_z), 90: (box_x, box_z + width - 1), 180: (box_x + width - 1, box_z + height - 1), 270: (box_x + height - 1, box_z)}[rotation]
        count = (cells_x + 1) * (cells_z + 1)
        values = [float(rng.randint(0, 9)) if index % 2 else reading.f32(rng.uniform(0.0, 12.0)) for _ in range(count)]
        x = {'anchor': anchor, 'rotation': rotation, 'width': width, 'height': height, 'shape': shape, 'flags': 0,
             'heights': {'cells_x': cells_x, 'cells_z': cells_z, 'values': values}}
        original = targets.run_binary(target, sandbox, x)
        writer.check(f'base height {index}', original, targets.base_height_plan(x))
        writer.check(f'base height C# rule {index}', original, targets.base_height_csharp_rule(x))
        out.append((f'base-height-{index:02d}-rot{rotation}', x, original))
    return out


# ---------------------------------------------------------------------------------------------------------------- C# output
HEADER = '''// Generated by tools/ppc-analysis/oracle_x86/skirt_synthetic_golden.py from the original routines run in the x86 oracle (docs/reverse/ORACLE.md X28 to X32).
// Synthetic inputs (not original data); every Expected* value is what tp.exe produced, and the Python reading skirt_reading.py agreed on every case when this was
// generated. The routines ran at the x87 control word 0x7F (24 bit significand, tp.exe's mode after the device start): every operation rounds to a float.
//
// LAYOUT (all arrays are flat; floats are float32 literals that read back bit-exactly; compare floats with BitConverter.SingleToInt32Bits)
//   MaskCase       [0x52C420 edge mask]    Types: Width * Height shape cell types, row-major (index = row * Width + column; row 0 is the grid's first row, the bottom
//                                          text row: the shape parser reverses the rows, as ObjectMapGrid.TypeAt( column, row ) reads them). Ones: every index
//                                          column * 16 + row at which the original wrote 1 (a 16 x 16 mask; indexes of 256 and more come only from shapes wider or higher
//                                          than 16, which write behind the mask).
//   UvCase         [0x467310]              Uv: u, v per corner. Faces: three corner ids per face. CornerVertex: the vertex of every corner. Vertices: x, y, z per vertex, the
//                                          node's TRANSFORMED vertices. Groups: slot, first face, face count per group. SlotsIn/SlotsOut: the slot flag words.
//                                          Mask: the 256 mask bytes or null (then no vertices are given either). Eps: the snap epsilon. ExpectedUv: u, v per corner after
//                                          the snap and the per face shift. ExpectedLogged: the out of range UV message was printed.
//   FlagsCase      [0x46AA10]              Types: Width * Height shape cell types, row-major. Header80: the model header bit 0x80 (drape). CellX/CellZ: the landscape cell sizes.
//                                          The node: Matrix (16 floats, row vectors, translation in elements 12..14), Parents (a list of 16 float matrices, nearest first),
//                                          Vertices (x, y, z per vertex in node space), Uv, Faces, CornerVertex and Groups as above. ExpectedEntries: code, slot per cell, index
//                                          row * Width + column ({1, 10000} when the cell has none). ExpectedSlots: slot words after the 0x800 decisions and the 0x30 clearing.
//                                          ExpectedUv: the normalised UVs the entries were built from. ExpectedErrors: 1 when the 'could not generate skirt flags' message was
//                                          printed. ExpectedFlipped / ExpectedUnflipped: the entries with and without the 0x40 flag.
//   ConformCase    [0x469940 / 0x469A80]   Landscape: origin, cell sizes, cell counts, corner heights ((CellsX + 1) * z + x) and the per cell flag bytes (4 bytes per cell,
//                                          bit 4 of the first = the other diagonal). BaseHeight: the drape's base height. List: the recorded vertex indices, consumed in walk order by
//                                          the nodes with flag 0x1000. Nodes: pre-order, Parent = index or -1. Matrix: the node's own matrix; the chain starts at the identity and
//                                          each node's matrix is its own times its parent's chain. Vertices: x, y, z of the instance copy before and (VerticesOut) after; Bounds / BoundsOut:
//                                          min xyz then max xyz. Flags 0x1000 conform list, 0x200 skip, 0x20000 drape; FlagsOut has 0x10000 on the root.
//   BaseHeightCase [0x466D70]              Anchor cell, Rotation (0, 90, 180, 270), Width, Height, Types (row-major) and the landscape corner heights. Expected: the result
//                                          as a float (lowest and highest corner height of the cells whose type has a non-zero low nibble, times 0.5).
//   Readings: skirt_reading.py (mask, uv_normalise, flags, conform, base_height) is the executable statement of every rule.
'''


def mask_literal(mask) -> str:
    """A UV case's mask: null, the shared all-ones or all-zero array, or MaskOf(indices of the ones)."""
    if mask is None:
        return 'null'
    ones = [i for i in range(256) if mask[i]]
    if len(ones) == 256:
        return 'AllOnes'
    if not ones:
        return 'Zeros'
    return 'MaskOf( ' + ', '.join(str(i) for i in ones) + ' )'


def record(groups: list, indent: str = '\t\t\t') -> str:
    """A record literal with named arguments, one line per group of fields."""
    return 'new(\n' + ',\n'.join(indent + ', '.join(f'{name}: {value}' for name, value in group) for group in groups) + ' )'


def emit(writer: Writer, masks, uvs, flagss, conforms, bases) -> str:
    out = [HEADER, 'namespace OpenTPW.Tests;', '', 'internal static class ObjectSkirtSyntheticGolden', '{']
    out += ['\tpublic sealed record MaskCase( string Name, int Width, int Height, int[] Types, int[] Ones );', '',
            '\tpublic sealed record UvCase( string Name, float Eps, float[] Uv, int[] Faces, int[] CornerVertex, float[] Vertices, int[] Groups, int[] SlotsIn, byte[]? Mask,',
            '\t\tfloat[] ExpectedUv, int[] ExpectedSlots, bool ExpectedLogged );', '',
            '\tpublic sealed record FlagsCase( string Name, int Width, int Height, int[] Types, bool Header80, float CellX, float CellZ, float[] Matrix, float[] Parents,',
            '\t\tfloat[] Vertices, float[] Uv, int[] Faces, int[] CornerVertex, int[] Groups, int[] SlotsIn, int[] ExpectedEntries, int[] ExpectedSlots, float[] ExpectedUv,',
            '\t\tint ExpectedErrors, int ExpectedFlipped, int ExpectedUnflipped );', '',
            '\tpublic sealed record ConformNode( int Parent, int Flags, float[] Matrix, float[] Vertices, float[] Bounds, int FlagsOut, float[] VerticesOut, float[] BoundsOut );', '',
            '\tpublic sealed record ConformCase( string Name, float OriginX, float OriginZ, float CellX, float CellZ, int CellsX, int CellsZ, float[] Heights, byte[] CellFlags,',
            '\t\tfloat BaseHeight, int[] List, ConformNode[] Nodes );', '',
            '\tpublic sealed record BaseHeightCase( string Name, int AnchorX, int AnchorZ, int Rotation, int Width, int Height, int[] Types, int CellsX, int CellsZ,',
            '\t\tfloat[] Heights, float Expected );', '',
            '\tpublic static readonly byte[] AllOnes = [.. System.Linq.Enumerable.Repeat( (byte)1, 256 )];',
            '\tpublic static readonly byte[] Zeros = new byte[256];', '',
            '\t/// <summary>A 256 byte mask with a 1 at each given index.</summary>',
            '\tpublic static byte[] MaskOf( params int[] ones )',
            '\t{', '\t\tvar mask = new byte[256];', '\t\tforeach ( var index in ones )', '\t\t\tmask[index] = 1;', '\t\treturn mask;', '\t}', '']
    out += ['\tpublic static readonly MaskCase[] MaskCases =', '\t[']
    for name, width, height, types, ones in masks:
        out.append(f'\t\tnew( "{name}", {width}, {height}, {ints(types)}, {ints(ones)} ),')
    out += ['\t];', '']
    out += ['\tpublic static readonly UvCase[] UvCases =', '\t[']
    for name, x, original in uvs:
        node = x['node']
        groups = flat((g['slot'], g['first'], g['count']) for g in node['groups'])
        vertices = x['vertices'] if x['vertices'] is not None else []
        out.append('\t\t' + record([[('Name', f'"{name}"'), ('Eps', f(x['eps'])), ('Mask', mask_literal(x['mask'])), ('SlotsIn', ints(node['slots'])),
                                       ('ExpectedSlots', ints(original['slots'])), ('ExpectedLogged', 'true' if original['logged'] else 'false')],
                                      [('Uv', floats(flat(node['uvs'])))], [('ExpectedUv', bits_floats(flat(original['uvs'])))],
                                      [('Faces', ints(flat(node['faces']))), ('CornerVertex', ints(node['corner_vertex'])), ('Groups', ints(groups))],
                                      [('Vertices', floats(flat(vertices)))]]) + ',')
    out += ['\t];', '']
    out += ['\tpublic static readonly FlagsCase[] FlagsCases =', '\t[']
    for name, x, original in flagss:
        node, shape = x['node'], x['shape']
        types = [shape['cells'][row * 20 + col] for row in range(x['height']) for col in range(x['width'])]
        groups = flat((g['slot'], g['first'], g['count']) for g in node['groups'])
        entries = flat(original['entries'])
        flipped, unflipped = original['flipped']       # the counts the routine printed ("Has %d flipped and %d non-flipped skirt textures")
        writer.check('flipped counts ' + name, (flipped, unflipped), (sum(1 for code, slot in original['entries'] if code != 1 and code & 0x40),
                                                                       sum(1 for code, slot in original['entries'] if code != 1 and not code & 0x40)))
        out.append('\t\t' + record([[('Name', f'"{name}"'), ('Width', x['width']), ('Height', x['height']), ('Types', ints(types)),
                                       ('Header80', 'true' if x['hdr80'] else 'false'), ('CellX', f(x['cell'][0])), ('CellZ', f(x['cell'][1]))],
                                      [('Matrix', floats(node['matrix'])), ('Parents', floats(flat(node['parents'])))],
                                      [('Vertices', floats(flat(node['vertices'])))], [('Uv', floats(flat(node['uvs'])))],
                                      [('Faces', ints(flat(node['faces']))), ('CornerVertex', ints(node['corner_vertex'])), ('Groups', ints(groups)),
                                       ('SlotsIn', ints(node['slots']))],
                                      [('ExpectedEntries', ints(entries)), ('ExpectedSlots', ints(original['slots']))], [('ExpectedUv', bits_floats(flat(original['uvs'])))],
                                      [('ExpectedErrors', original['errors']), ('ExpectedFlipped', flipped), ('ExpectedUnflipped', unflipped)]]) + ',')
    out += ['\t];', '']
    out += ['\tpublic static readonly ConformCase[] ConformCases =', '\t[']
    for name, x, original in conforms:
        land = x['landscape']
        walk = []

        def collect(node, parent):
            walk.append((node, parent))
            index = len(walk) - 1
            for child in node['children']:
                collect(child, index)
        collect(x['tree'], -1)
        nodes = []
        for (node, parent), (flags_out, vertices_out, bounds_out) in zip(walk, original['nodes']):
            nodes.append('\t\t\t\tnew( ' + ', '.join(f'{n}: {v}' for n, v in [('Parent', parent), ('Flags', node['flags']), ('Matrix', floats(node['matrix'])),
                                                                       ('Vertices', floats(flat(node['vertices']))), ('Bounds', floats(node['bounds'][0] + node['bounds'][1])),
                                                                       ('FlagsOut', flags_out), ('VerticesOut', bits_floats(flat(vertices_out))),
                                                                       ('BoundsOut', bits_floats(bounds_out))]) + ' )')
        out.append('\t\t' + record([[('Name', f'"{name}"'), ('OriginX', f(land['origin'][0])), ('OriginZ', f(land['origin'][1])), ('CellX', f(land['cell'][0])),
                                       ('CellZ', f(land['cell'][1])), ('CellsX', land['cells'][0]), ('CellsZ', land['cells'][1]), ('BaseHeight', f(x['base_height'])),
                                       ('List', ints(x['list']))],
                                      [('Heights', floats(land['heights']))], [('CellFlags', ints(land['flags']))],
                                      [('Nodes', '[\n' + ',\n'.join(nodes) + ']')]]) + ',')
    out += ['\t];', '']
    out += ['\tpublic static readonly BaseHeightCase[] BaseHeightCases =', '\t[']
    for name, x, original in bases:
        shape = x['shape']
        types = [shape['cells'][row * 20 + col] for row in range(x['height']) for col in range(x['width'])]
        land = x['heights']
        out.append(f'\t\tnew( "{name}", {x["anchor"][0]}, {x["anchor"][1]}, {x["rotation"]}, {x["width"]}, {x["height"]}, {ints(types)}, {land["cells_x"]}, {land["cells_z"]}, '
                   f'{floats(land["values"])}, {f(original)} ),')
    out += ['\t];', '}', '']
    return '\n'.join(out)


def main() -> int:
    if not have_unicorn():
        print(UNICORN_HINT, file=sys.stderr)
        return 2
    exe = executable_path('tpw')
    if exe is None:
        print('OPENTPW_TPW_EXE must name the identified tp.exe', file=sys.stderr)
        return 2
    image = Image.load(exe)
    writer = Writer()
    sections = []
    for build, target in ((mask_cases, targets.SKIRT_MASK_TARGET), (uv_cases, targets.SKIRT_UV_TARGET), (flags_cases, targets.SKIRT_FLAGS_TARGET),
                          (conform_cases, targets.CONFORM_TARGET), (base_height_cases, targets.BASE_HEIGHT_TARGET)):
        sections.append(build(writer, target.sandbox(image), target))
    print(emit(writer, *sections))
    print(f'cases: ' + ', '.join(f'{len(s)}' for s in sections) + f'; reading vs tp.exe: {writer.mismatches} problems', file=sys.stderr)
    return 1 if writer.mismatches else 0


if __name__ == '__main__':
    sys.exit(main())
