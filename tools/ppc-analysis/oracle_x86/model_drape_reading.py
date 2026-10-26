"""The reading of tp.exe's model drape (docs/PATHS.md, "Queue models"; docs/TRACKS.md, "Rendering"): 0x469940 -> 0x469A80 on the nodes that carry the load flag 0x20000.

Every mesh node of a model loaded with the drape load flag (0x4629D0 flag 0x40, set by the queue models 0x522900 and the kart and water kits) gets the node flag 0x20000. The instance
placement 0x463060 (creation flag 8) then walks the instance's nodes: each vertex goes through the node's chain matrix (0x4702D0, 0x46FC00) into the grid's frame, its height becomes the
landscape triangle under its x and z, always split along the diagonal that the cell word's bit 0x4 names, minus the instance height plus the height it had there, and it goes back through the
inverse of the chain (0x470040). The plan side of the x86 target `model_drape` and the reading that OpenTPW's `ModelDrape` / `QueueModelDrape` follow.

Every operation is one single-precision operation (the x87 control word 0x7F that DDSCL_FPUSETUP leaves, COAST-032): `f32` rounds. Dev-only; no original bytes.

A tree node is a dict: 'flags' (0x20000 drape, 0x200 skip), 'matrix' (16 floats, row-vector), 'vertices' [(x, y, z)], 'children'. The landscape is {'origin': (x, z), 'cell': (sx, sz),
'cells': (nx, nz), 'heights': [(nx + 1) * (nz + 1) floats], 'flags': bytes, four per cell, the first of each with bit 4 = the other diagonal}.
"""
from __future__ import annotations

import math
import struct
from fractions import Fraction

import skirt_reading  # (the chain 0x4702D0, the point transform 0x46FC00 and the float LU inverse 0x470040 are pinned there, X31)
from skirt_reading import chain_matrix, f32, f32_bits, inverse, transform_point  # noqa: F401

TRACE = None                    # a list: `drape` appends the branches it takes (the tests check that the inputs reach them all)
IDENTITY = skirt_reading.IDENTITY


def inverse_exact(matrix: list) -> list:
    """The inverse in exact rational arithmetic, each element rounded to a single: used only to put the inputs' vertices where the chain maps them on the grid (the original's float LU, `inverse`,
    differs from it in the last bit on some translations)."""
    n = 4
    a = [[Fraction(matrix[4 * i + j]) for j in range(n)] + [Fraction(1 if i == j else 0) for j in range(n)] for i in range(n)]
    for col in range(n):
        pivot = next(r for r in range(col, n) if a[r][col] != 0)
        a[col], a[pivot] = a[pivot], a[col]
        scale = a[col][col]
        a[col] = [v / scale for v in a[col]]
        for r in range(n):
            if r != col and a[r][col] != 0:
                factor = a[r][col]
                a[r] = [v - factor * w for v, w in zip(a[r], a[col])]
    return [f32(float(a[i][n + j])) for i in range(n) for j in range(n)]


def drape_in_cell(a: float, b: float, c: float, d: float, fx: float, fz: float, anti: bool) -> float:
    """[BIN:TPW-EXE:0x469A80 drape triangle] the heights of the cell's corners a = (x, z), b = (x + 1, z), c = (x, z + 1), d = (x + 1, z + 1) at the fractions (fx, fz); the cell flag 0x4 clear:
    fz <= fx takes ((1 - fx) a + b fx) + fz (d - b), else ((1 - fx) c + d fx) + (1 - fz) (-(c - a)); set: fx < 1 - fz takes ((1 - fx) a + b fx) + fz (c - a), else ((1 - fx) c + d fx) + (1 - fz) (-(d - b))."""
    if not anti:
        upper, mirrored = (True, False) if not fx < fz else (False, True)
    else:
        upper, mirrored = (False, False) if fx < f32(1.0 - fz) else (True, True)
    if TRACE is not None:
        TRACE.append(f'triangle-{int(anti)}-{int(upper)}{int(mirrored)}')
    difference = f32(d - b) if upper else f32(c - a)
    one_minus_x = f32(1.0 - fx)
    if mirrored:
        return f32(f32(f32(one_minus_x * c) + f32(d * fx)) + f32(f32(1.0 - fz) * -difference))
    return f32(f32(f32(one_minus_x * a) + f32(b * fx)) + f32(fz * difference))


def landscape_height(land: dict, x: float, z: float) -> float:
    """The landscape value under a point of the grid the way the drape reads it: per axis t = (w - origin) / cell as a single, 0 when negative, whole = trunc(t), fraction = t - whole; the cell's four corners
    by index arithmetic over the (nx + 1) wide array (zero past its end), the diagonal from the cell word at index cell * 4 (zero past its end)."""
    cells, heights, flags = land['cells'], land['heights'], land['flags']
    t, whole, fraction = [], [], []
    for value, origin, cell in ((x, land['origin'][0], land['cell'][0]), (z, land['origin'][1], land['cell'][1])):
        v = f32(f32(value - origin) / cell)
        if v < 0.0 and TRACE is not None:
            TRACE.append('clamp')
        v = 0.0 if v < 0.0 else v
        w = float(math.trunc(v))
        t.append(v)
        whole.append(int(w))
        fraction.append(f32(v - w))
    stride = cells[0] + 1
    e4 = whole[0] + stride * whole[1]
    if TRACE is not None and (whole[0] >= cells[0] or whole[1] >= cells[1]):
        TRACE.append('far-edge')

    def height(index: int) -> float:
        return heights[index] if 0 <= index < len(heights) else 0.0
    cell_index = whole[0] + whole[1] * cells[0]
    word = flags[cell_index * 4] if 0 <= cell_index * 4 < len(flags) else 0
    return drape_in_cell(height(e4), height(e4 + 1), height(e4 + stride), height(e4 + stride + 1), fraction[0], fraction[1], bool(word & 4))


def drape(case: dict) -> dict:
    """[BIN:TPW-EXE:0x469940, 0x469A80 drape branch]: walks the instance tree from the root with the identity as the incoming chain, in pre-order. A node with 0x20000 and without 0x200 moves every
    vertex: through the chain, the height ((landscape - base height) + height), back through the inverse of the chain. Returns the vertices in the node's own space ('nodes', as float bits, pre-order,
    with the node's flags) and in the grid's frame after the drape ('world', the values before the inverse)."""
    land, base_height = case['landscape'], case['base_height']
    nodes, world = [], []

    def walk(node: dict, incoming: list) -> None:
        matrix = chain_matrix([f32(v) for v in node['matrix']], incoming)
        moved_nodes, moved_world = [tuple(v) for v in node['vertices']], []
        if TRACE is not None and not node['flags'] & 0x20000:
            TRACE.append('plain-node')
        if TRACE is not None and node['flags'] & 0x200:
            TRACE.append('skipped-node')
        if node['flags'] & 0x20000 and not node['flags'] & 0x200:
            inverse_chain = inverse(matrix)
            for index, source in enumerate(node['vertices']):
                w = list(transform_point(matrix, tuple(f32(c) for c in source)))
                w[1] = f32(f32(landscape_height(land, w[0], w[2]) - base_height) + w[1])
                moved_world.append(tuple(w))
                moved_nodes[index] = transform_point(inverse_chain, tuple(w))
        nodes.append((node['flags'], [tuple(f32_bits(c) for c in v) for v in moved_nodes]))
        world.append([tuple(f32_bits(c) for c in v) for v in moved_world])
        for child in node['children']:
            walk(child, matrix)
    walk(case['tree'], list(IDENTITY))
    # 0x469940 marks the root once the walk is done
    nodes[0] = (nodes[0][0] | 0x10000, nodes[0][1])
    return {'nodes': nodes, 'world': world}


# ---------------------------------------------------------------------------------------------------------------- 0x467030 the placement of the root
# [BIN:TPW-EXE:0x004708D0 sine table] the table at 0x7b0968 is filled at start-up (0x449eb0, 0x44a1d0: N = 4096): entry i is the single sin(float(i / 4096) * 2 pi) (the constant 0x6feba0, 6.2831855), the scale 0x7b095c
# is float(4096) * the constant 0x6feb9c (0.15915494) and the mask 0x7b0960 is N - 1. Entry 2048 is sin(3.1415927), -8.74e-8, not 0.
TABLE_SIZE = 4096
RADIANS = struct.unpack('<f', bytes.fromhex('35fa8e3c'))[0]        # [0x6fe774] 0.017453292
QUARTER = struct.unpack('<f', bytes.fromhex('db0fc9bf'))[0]         # [0x6fe778] -1.5707964
TURN = struct.unpack('<f', bytes.fromhex('db0fc940'))[0]           # [0x6feba0] 6.2831855
PER_TURN = struct.unpack('<f', bytes.fromhex('83f9223e'))[0]       # [0x6feb9c] 0.15915494
SCALE = f32(TABLE_SIZE * PER_TURN)


def table_entry(index: int) -> float:
    return f32(math.sin(f32(f32(index / TABLE_SIZE) * TURN)))


def round_even(value: float) -> int:
    """FISTP at the default rounding: to nearest, ties to even."""
    return round(value)


def yaw_sin_cos(degrees: int) -> tuple:
    """[BIN:TPW-EXE:0x467030]: a = float(degrees) * 0.017453292 (a single); the sine is the table entry round(a * scale) & mask, the cosine that of round((a - (-1.5707964)) * scale) & mask."""
    a = f32(degrees * RADIANS)
    sin_index = round_even(f32(SCALE * a)) & (TABLE_SIZE - 1)
    cos_index = round_even(f32(f32(a - QUARTER) * SCALE)) & (TABLE_SIZE - 1)
    return table_entry(sin_index), table_entry(cos_index)


def rotate_columns(matrix: list, cos: float, sin: float) -> list:
    """[BIN:TPW-EXE:0x46F650]: each row of the matrix (a, b, c) turns in its x and z elements: x' = x cos + z sin, z' = z cos - x sin (elements 0 / 2, 4 / 6 and 8 / 10)."""
    out = list(matrix)
    for row in (0, 4, 8):
        x, z = matrix[row], matrix[row + 2]
        out[row] = f32(f32(x * cos) + f32(z * sin))
        out[row + 2] = f32(f32(z * cos) - f32(x * sin))
    return out


def placement(case: dict) -> dict:
    """[BIN:TPW-EXE:0x467030, flag 0x20]: the root node's matrix after the placement: its own matrix turned by the yaw (0x46F650 with the table's cosine and sine), the translation words (+0x40, +0x44,
    +0x48 of the node) overwritten with (cell x * size + origin, the instance height, cell z * size + origin); the record's rotation word gets the angle added (taken modulo 360). The cell is the
    caller's adjusted one (0x466F90: 90 adds a row, 180 a column and a row, 270 a column)."""
    sin, cos = yaw_sin_cos(case["rotation"])
    matrix = rotate_columns([f32(v) for v in case['root']], cos, sin)
    land = case['landscape']
    matrix[12] = f32(f32(case['x'] * land['cell'][0]) + land['origin'][0])
    matrix[13] = case['height']
    matrix[14] = f32(f32(case['z'] * land['cell'][1]) + land['origin'][1])
    return {'matrix': [f32_bits(v) for v in matrix], 'angle': case['rotation'] % 360}
