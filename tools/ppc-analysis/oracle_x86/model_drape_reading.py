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

TRACE = None                    # a list: `drape` appends the branches it takes (the tests check that the inputs reach them all)
IDENTITY = [1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0]


def f32(value: float) -> float:
    """Round to the nearest single (an operation of a double on two singles rounded once is the single operation)."""
    try:
        return struct.unpack('<f', struct.pack('<f', value))[0]
    except OverflowError:
        return math.copysign(math.inf, value)


def f32_bits(value: float) -> int:
    return struct.unpack('<I', struct.pack('<f', f32(value)))[0]


def chain_matrix(node: list, parent: list) -> list:
    """[BIN:TPW-EXE:0x4702D0]: the node's matrix times the chain (a vertex goes through the node, then through the parents). Element [4a + b] = (L[8 + b] P[4a + 2] + L[4 + b] P[4a + 1])
    + L[b] P[4a] for the three rotation rows (L = the chain, P = the node), the translation row adds L[12 + b] last; one store per element, the elements 3, 7, 11 and 15 stay."""
    out = list(parent)
    for a in range(4):
        for b in range(3):
            total = f32(f32(parent[8 + b] * node[4 * a + 2]) + f32(parent[4 + b] * node[4 * a + 1]))
            total = f32(total + f32(parent[b] * node[4 * a]))
            if a == 3:
                total = f32(total + parent[12 + b])
            out[4 * a + b] = total
    return out


def transform_point(m: list, p: tuple) -> tuple:
    """[BIN:TPW-EXE:0x46FC00]: p * m with the translation row: x' = ((x m0 + m8 z) + m4 y) + m12, y' = ((m9 z + m5 y) + m1 x) + m13, z' = ((m10 z + m6 y) + m2 x) + m14."""
    x, y, z = p
    nx = f32(f32(f32(f32(x * m[0]) + f32(m[8] * z)) + f32(m[4] * y)) + m[12])
    ny = f32(f32(f32(f32(m[9] * z) + f32(m[5] * y)) + f32(m[1] * x)) + m[13])
    nz = f32(f32(f32(f32(m[10] * z) + f32(m[6] * y)) + f32(m[2] * x)) + m[14])
    return nx, ny, nz


def divide(a: float, b: float) -> float:
    """An x87 divide: x / 0 is infinity, 0 / 0 the indefinite NaN."""
    if b == 0.0:
        return math.nan if a == 0.0 or math.isnan(a) else math.copysign(math.inf, a) * math.copysign(1.0, b)
    return a / b


def lu_decompose(matrix: list) -> tuple:
    """[BIN:TPW-EXE:0x46FE20]: LU decomposition with implicit scaling and partial pivoting in place (the classic Crout scheme). Row scale = float(1 / largest |element|);
    sums run in double, the stored element is the float of the sum, the pivot test uses |sum| * scale of the double sum against the float of the best so far
    (>= wins, so the last row among equals); a zero pivot becomes 1e-20; the column below the pivot is multiplied by float(1 / pivot)."""
    a = [list(matrix[4 * i:4 * i + 4]) for i in range(4)]
    scale = []
    for i in range(4):
        largest = 0.0
        for v in a[i]:
            if abs(v) > largest:
                largest = abs(v)
        scale.append(f32(divide(1.0, largest)))
    index = [0] * 4
    sign = 1.0
    for j in range(4):
        for i in range(j):
            total = a[i][j]
            for k in range(i):
                total = f32(total - f32(a[k][j] * a[i][k]))
            a[i][j] = f32(total)
        best = 0.0
        pivot = 0
        for i in range(j, 4):
            total = a[i][j]
            for k in range(j):
                total = f32(total - f32(a[k][j] * a[i][k]))
            a[i][j] = f32(total)
            value = f32(abs(total) * scale[i])
            if value >= best:
                best = f32(value)
                pivot = i
        if j != pivot:
            a[pivot], a[j] = a[j], a[pivot]
            sign = -sign
            scale[pivot] = scale[j]
        index[j] = pivot
        if a[j][j] == 0.0:
            a[j][j] = f32(1e-20)
        if j != 3:
            reciprocal = f32(divide(1.0, a[j][j]))
            for i in range(j + 1, 4):
                a[i][j] = f32(f32(reciprocal * a[i][j]))
    return a, index, sign


def lu_solve(a: list, index: list, b: list) -> list:
    """[BIN:TPW-EXE:0x46FD40]: forward and back substitution, sums in double, one float store per element, the back substitution divides by the stored pivot."""
    b = list(b)
    first = -1
    for i in range(4):
        total = b[index[i]]
        b[index[i]] = b[i]
        if first >= 0:
            for j in range(first, i):
                total = f32(total - f32(b[j] * a[i][j]))
        elif total != 0.0:
            first = i
        b[i] = f32(total)
    for i in range(3, -1, -1):
        total = b[i]
        for j in range(i + 1, 4):
            total = f32(total - f32(a[i][j] * b[j]))
        b[i] = f32(divide(total, a[i][i]))
    return b


def inverse(matrix: list) -> list:
    """[BIN:TPW-EXE:0x470040]: the matrix inverse by LU decomposition, one column per unit vector; column j of the result is the solution for e_j."""
    a, index, _ = lu_decompose([f32(v) for v in matrix])
    out = [0.0] * 16
    for column in range(4):
        unit = [0.0] * 4
        unit[column] = 1.0
        solved = lu_solve(a, index, unit)
        for row in range(4):
            out[row * 4 + column] = solved[row]
    return out




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
