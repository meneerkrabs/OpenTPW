"""Python statement of the procedural object base ("skirt") routines of tp.exe, the plan side of the x86 skirt targets
(docs/reverse/ORACLE.md section 7, X28 to X32). Pure Python: no executable bytes, nothing imported from the game; addresses appear only as
`[BIN:TPW-EXE:...]` tags.

* `mask`          the footprint edge mask [BIN:TPW-EXE:0x52C420]
* `uv_normalise`  the UV snap, per-face shift and wrap clear [BIN:TPW-EXE:0x467310]
* `flags`         the skirt entries and the opaque slot flags [BIN:TPW-EXE:0x46AA10]
* `conform`       the edge vertex conform of an instance copy [BIN:TPW-EXE:0x469A80 apply mode]
* `base_height`   the base height of a placed shape [BIN:TPW-EXE:0x466D70]

Every routine runs the x87 in the 53-bit significand mode the other oracle targets use (control word 0x27F, docs/reverse/ORACLE.md 7.5): a
Python float operation is then exactly one x87 operation, and an `f32()` marks a store to a 32-bit memory float (the places where the original
rounds). Operands that stay in an x87 register keep their double value; the comments say which is which.
"""
from __future__ import annotations

import math
import struct

# ---------------------------------------------------------------------------------------------------------------- arithmetic helpers


def f32(value: float) -> float:
    """Round to the nearest 32-bit float (a `fstp dword` or `fst dword`)."""
    try:
        return struct.unpack('<f', struct.pack('<f', value))[0]
    except OverflowError:
        return math.copysign(math.inf, value)


def f32_bits(value: float) -> int:
    return struct.unpack('<I', struct.pack('<f', f32(value)))[0]


def bits_f32(bits: int) -> float:
    return struct.unpack('<f', struct.pack('<I', bits & 0xFFFFFFFF))[0]


def ftol(value: float) -> int:
    """`_ftol`: truncate toward zero; the 32-bit result of the integer store (the 64-bit 'indefinite' keeps 0 in the low dword)."""
    if not math.isfinite(value) or abs(value) >= 2.0 ** 63:
        return 0
    whole = int(value) & 0xFFFFFFFF
    return whole - (1 << 32) if whole & 0x80000000 else whole


def s32(value: int) -> int:
    value &= 0xFFFFFFFF
    return value - (1 << 32) if value & 0x80000000 else value


# A reading can be run with every x87 intermediate rounded to a float instead (`single_precision()`): that is what float arithmetic per operation gives, so the
# difference between the two shows where a C# transcription with `float` temporaries would disagree with the original.
_SINGLE = False


def q(value: float) -> float:
    """An x87 register value: kept as a double (the 53-bit mode), or rounded to a float in the single precision variant."""
    return f32(value) if _SINGLE else value


class single_precision:
    def __enter__(self):
        global _SINGLE
        self._saved = _SINGLE
        _SINGLE = True

    def __exit__(self, *exc):
        global _SINGLE
        _SINGLE = self._saved


ONE_THIRD = f32(1.0 / 3.0)      # the constant 0.33333334 (single precision)
ZERO_POINT_NINE = f32(0.9)      # 0x3F666666
SNAP = f32(0.02)                # 0x3CA3D70A, the epsilon 0x46AA10 passes to the UV normaliser
TENTH = f32(0.1)


# ---------------------------------------------------------------------------------------------------------------- 0x52C420 the edge mask
MASK_ADDRESS = 0x8186F0
MASK_SPAN = 324                 # 16 x 16 mask bytes, then the two words the routine stores behind them, then whatever a 20 x 20 shape writes
FOOTPRINT_TYPES = (4, 9, 10, 0x17)
SHAPE_STRIDE = 20


def shape_type(shape: dict, col: int, row: int) -> int:
    """The type dword at shape + 8 + (row * 20 + col) * 8; the grid is 20 wide, so column 20 of a row is column 0 of the next, and reads past the last stored
    row see zero here (the real shape object holds other fields there, which no shipped shape makes a footprint type)."""
    index = row * SHAPE_STRIDE + col
    cells = shape['cells']
    return cells[index] if 0 <= index < len(cells) else 0


def mask_positions(shape: dict) -> list:
    """The mask positions (col * 16 + row) the routine sets to 1, in the order it visits the cells: a footprint cell (type 4, 9, 10 or 0x17) that does not have all
    four neighbours of those types. Neighbour tests: row - 1 >= 0, col - 1 >= 0, col + 1 <= width, row + 1 <= height (inclusive, so one step past the last column or
    row is read; the grid is 20 wide, so column 20 of a row is column 0 of the next)."""
    width, height = shape['width'], shape['height']

    def footprint(col: int, row: int) -> bool:
        return shape_type(shape, col, row) in FOOTPRINT_TYPES
    positions = []
    for row in range(height):
        for col in range(width):
            if not footprint(col, row):
                continue
            count = 0
            if row - 1 >= 0 and footprint(col, row - 1):
                count += 1
            if col - 1 >= 0 and footprint(col - 1, row):
                count += 1
            if col + 1 <= width and footprint(col + 1, row):
                count += 1
            if row + 1 <= height and footprint(col, row + 1):
                count += 1
            if count != 4:
                positions.append(col * 16 + row)
    return positions


def mask(shape: dict) -> bytes:
    """The bytes at 0x8186F0..+324 after the routine ran: the mask (`mask_positions` set to 1) behind which the routine has stored the width and the height as
    dwords (at 256 and 260, before it clears the 256 mask bytes); cells of a shape wider or higher than 16 write 1 over those words and beyond them."""
    memory = bytearray(MASK_SPAN)
    memory[256:260] = struct.pack('<I', shape['width'])
    memory[260:264] = struct.pack('<I', shape['height'])
    for position in mask_positions(shape):
        if position < MASK_SPAN:
            memory[position] = 1
    return bytes(memory)


# ---------------------------------------------------------------------------------------------------------------- shared node reading
# A node here is a dict:
#   'matrix'   16 floats (the node's own matrix, row-vector convention), 'parents' a list of 16-float matrices, nearest parent first
#   'vertices' [(x, y, z)], 'uvs' [(u, v)], 'corner_vertex' [vertex index of every corner], 'faces' [(c0, c1, c2)] corner ids
#   'groups'   [{'slot': int, 'first': int, 'count': int}], 'slots' [flag word per slot]
IDENTITY = [1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0]


def matrix_multiply(a: list, b: list) -> list:
    """[BIN:TPW-EXE:0x4700F0]: a = a * b for the rotation rows, a[12..14] gets the translation row; every output is rounded to a float once, the sum is
    (p + q) + r with the products taken in double (exact for two floats)."""
    out = list(a)
    for row in (0, 4, 8):
        l0, l1, l2 = a[row], a[row + 1], a[row + 2]
        out[row] = f32(q(q(q(l1 * b[4]) + q(l2 * b[8])) + q(l0 * b[0])))
        out[row + 1] = f32(q(q(q(l0 * b[1]) + q(l2 * b[9])) + q(l1 * b[5])))
        out[row + 2] = f32(q(q(q(l0 * b[2]) + q(l2 * b[10])) + q(l1 * b[6])))
    l0, l1, l2 = a[12], a[13], a[14]
    out[12] = f32(q(q(q(q(l1 * b[4]) + q(l2 * b[8])) + q(l0 * b[0])) + b[12]))
    out[13] = f32(q(q(q(q(l0 * b[1]) + q(l2 * b[9])) + q(l1 * b[5])) + b[13]))
    out[14] = f32(q(q(q(q(l0 * b[2]) + q(l2 * b[10])) + q(l1 * b[6])) + b[14]))
    return out


def world_matrix(node: dict) -> list:
    """[BIN:TPW-EXE:0x4628B0] head: the node's matrix times each parent's, nearest parent first."""
    m = [f32(v) for v in node['matrix']]
    for parent in node.get('parents', []):
        m = matrix_multiply(m, [f32(v) for v in parent])
    return m


def transform_vertices(node: dict) -> list:
    """[BIN:TPW-EXE:0x4628B0] tail: every vertex through the matrix (x' = ((m8 z + m4 y) + m0 x) + m12 and the like, one float store per axis)."""
    m = world_matrix(node)
    out = []
    for (x, y, z) in node['vertices']:
        out.append((f32(q(q(q(q(m[8] * z) + q(m[4] * y)) + q(m[0] * x)) + m[12])),
                    f32(q(q(q(q(m[9] * z) + q(m[5] * y)) + q(m[1] * x)) + m[13])),
                    f32(q(q(q(q(m[10] * z) + q(m[6] * y)) + q(m[2] * x)) + m[14]))))
    return out


# ---------------------------------------------------------------------------------------------------------------- 0x467310 UV normalisation
def snap_component(value: float, eps: float) -> float:
    """One UV component: whole part (modf truncates) plus the fraction snapped to -1, 0 or 1 within eps. The first test compares the exact fraction with the
    float -(1 - eps); the other two compare the fraction rounded to a float with eps and with 1 - eps."""
    whole = float(math.trunc(value))
    fraction = value - whole
    near_one = f32(1.0 - eps)
    if fraction < -near_one:
        snapped = -1.0
    else:
        fraction32 = f32(fraction)
        if fraction32 <= -eps or eps <= fraction32:
            snapped = 1.0 if fraction32 > near_one else fraction
        else:
            snapped = 0.0
    return f32(whole + snapped)


def uv_normalise(node: dict, eps: float, mask_bytes, vertices) -> dict:
    """[BIN:TPW-EXE:0x467310]. Returns {'uvs', 'slots', 'logged'}: `mask_bytes` is the 16 x 16 mask (or None), `vertices` the node's transformed vertex list
    (or None). Steps: snap every UV, shift the corners of every face by the floor of the face's smallest u and v (a corner only once, in face order),
    then clear the wrap bits 0x30 of a slot whose faces all lie flat (|centroid y| < 0.1) in a mask cell, unless the slot has bit 0x8000."""
    eps = f32(eps)
    uvs = [(snap_component(u, eps), snap_component(v, eps)) for (u, v) in node['uvs']]
    faces = node['faces']
    minimum = []
    for face in faces:
        min_u, min_v = 3.4028234663852886e+38, 3.4028234663852886e+38
        for corner in face:
            u, v = uvs[corner]
            if u < min_u:
                min_u = u
            if v < min_v:
                min_v = v
        minimum.append((min_u, min_v))
    visited = [False] * len(uvs)
    for face, (min_u, min_v) in zip(faces, minimum):
        shift_u = float(math.trunc(min_u))
        shift_v = float(math.trunc(min_v))
        if min_u - shift_u < 0:
            shift_u = f32(shift_u - 1.0)
        if min_v - shift_v < 0:
            shift_v = f32(shift_v - 1.0)
        for corner in face:
            if not visited[corner]:
                u, v = uvs[corner]
                uvs[corner] = (f32(u - shift_u), f32(v - shift_v))
                visited[corner] = True
    slots = list(node['slots'])
    logged = False
    have_mask = mask_bytes is not None and vertices is not None
    for group in node['groups']:
        bad = 0
        for face_index in range(group['first'], group['first'] + group['count']):
            face = faces[face_index]
            if have_mask:
                a, b, c = (vertices[node['corner_vertex'][k]] for k in face)
                # the sums run in a different order per axis (c + a) + b, (b + a) + c, (c + b) + a; the product with the float constant rounds once
                cx = f32(q(q(q(c[0] + a[0]) + b[0]) * ONE_THIRD))
                cy = f32(q(q(q(b[1] + a[1]) + c[1]) * ONE_THIRD))
                cz = f32(q(q(q(c[2] + b[2]) + a[2]) * ONE_THIRD))
            for corner in face:
                u, v = uvs[corner]
                if have_mask:
                    if cy <= -TENTH or not cy < TENTH:
                        bad += 1
                        continue
                    z_index = ftol(q(cz * -TENTH))
                    x_index = ftol(q(cx * TENTH))
                    position = s32((x_index << 4) - z_index)
                    value = mask_value(mask_bytes, position)
                    if value == 0:
                        bad += 1
                        continue
                if not logged and (u < 0.0 or not u <= 1.0 or v < 0.0 or not v <= 1.0):
                    logged = True
        slot = group['slot']
        if bad == 0 and slot is not None and not slots[slot] & 0x8000:
            slots[slot] &= 0xFFCF
    return {'uvs': uvs, 'slots': slots, 'logged': logged}


def mask_value(mask_bytes, position: int) -> int:
    """A byte of the mask region; indexes outside the bytes given read as zero (the neighbouring static data is zero)."""
    return mask_bytes[position] if 0 <= position < len(mask_bytes) else 0


# ---------------------------------------------------------------------------------------------------------------- 0x46AA10 the skirt entries
def divide(a: float, b: float) -> float:
    """An x87 divide: x / 0 is infinity, 0 / 0 the indefinite NaN."""
    if b == 0.0:
        return math.nan if a == 0.0 or math.isnan(a) else math.copysign(math.inf, a) * math.copysign(1.0, b)
    return a / b


def edge_axes(a: tuple, b: tuple, c: tuple) -> dict:
    """The three edges v[e] -> v[(e + 1) % 3] of a face, normalised, and the edge with the largest |x| and the one with the largest |z|.
    dx = x1 - x0 in double; len = float(sqrt((dx * dx + dy * float(dy)) + dz * dz)); the stored direction is (float(float(dx) / len), float(float(dy) / len),
    float(dz / len)); |x| (the unrounded quotient) must exceed the best |x| so far, which is held as a float, and float(|z|) the best |z|. Both start at 0 and
    only a strictly larger value replaces them (the first edge wins a tie)."""
    corners = (a, b, c)
    best_x = 0.0
    best_z = 0.0
    edge_x = edge_z = None
    direction_x = direction_z = None
    for e in range(3):
        start, end = corners[e], corners[(e + 1) % 3]
        dx, dy, dz = q(end[0] - start[0]), q(end[1] - start[1]), q(end[2] - start[2])
        length = f32(q(math.sqrt(q(q(q(dx * dx) + q(dy * f32(dy))) + q(dz * dz)))))
        nx, ny, nz = q(divide(f32(dx), length)), q(divide(f32(dy), length)), q(divide(dz, length))
        direction = (f32(nx), f32(ny), f32(nz))
        if abs(nx) > best_x:
            best_x = f32(abs(nx))
            edge_x, direction_x = e, direction
        if f32(abs(nz)) > best_z:
            best_z = f32(abs(nz))
            edge_z, direction_z = e, direction
    return {'mx': best_x, 'mz': best_z, 'edge_x': edge_x, 'edge_z': edge_z, 'dir_x': direction_x, 'dir_z': direction_z}


def face_cell(a: tuple, b: tuple, c: tuple, cell: tuple) -> tuple:
    """The cell (x, z) of a face's centroid: x = ftol(((x0 + x1) + x2) * 0.33333334 / sizeX) with the sum and the product kept in double, z = ftol(float((z2 + float(z0 + z1)) * 0.33333334) / sizeZ)."""
    cx = q(q(q(a[0] + b[0]) + c[0]) * ONE_THIRD)
    cz = f32(q(q(c[2] + f32(q(a[2] + b[2]))) * ONE_THIRD))
    return ftol(q(divide(cx, cell[0]))), ftol(q(divide(cz, cell[1])))


def flags(x: dict) -> dict:
    """[BIN:TPW-EXE:0x46AA10] on an input dict (see the target): the entries (code, slot) per footprint cell, the slot flag words (0x800 set or cleared
    per face, 0x30 cleared by the UV normaliser), the normalised UVs, the base record's first dword and the print counts."""
    node, shape = x['node'], x['shape']
    width, height, hdr80 = x['width'], x['height'], x['hdr80']
    mask_bytes = mask(shape)
    vertices = transform_vertices(node)
    slots = list(node['slots'])
    records: dict = {}

    def record(index: int) -> dict:
        if index not in records:
            col, row = index % SHAPE_STRIDE, index // SHAPE_STRIDE
            inside = 0 <= row < height and col < width
            records[index] = {'mx': 0.0, 'mz': 0.0, 'group': -1 if inside else 0, 'face': -1 if inside else 0}
        return records[index]
    for group_index, group in enumerate(node['groups']):
        for face_index in range(group['count']):
            face = node['faces'][group['first'] + face_index]
            a, b, c = (vertices[node['corner_vertex'][k]] for k in face)
            cell_x, cell_z = face_cell(a, b, c, x['cell'])
            slot = group['slot']
            if not hdr80 and mask_value(mask_bytes, s32((cell_x << 4) + cell_z)) == 0:
                slots[slot] &= 0xF7FF
            else:
                slots[slot] |= 0x0800
            axes = edge_axes(a, b, c)
            index = s32(cell_x + cell_z * SHAPE_STRIDE)
            current = record(index)
            if ZERO_POINT_NINE < axes['mx'] and ZERO_POINT_NINE < axes['mz'] and (current['mx'] < axes['mx'] or current['mz'] < axes['mz']):
                records[index] = dict(axes, group=group_index, face=face_index)
    # the UV normaliser runs between the two passes, on the transformed vertices and the mask
    normalised = uv_normalise(dict(node, slots=slots), SNAP, mask_bytes, vertices)
    uvs, slots = normalised['uvs'], normalised['slots']
    entries = []
    flipped = unflipped = 0
    errors = 0
    for row in range(height):
        for col in range(width):
            entry = (1, 10000)
            wanted = hdr80 or (mask_value(mask_bytes, col * 16 + row) != 0 and shape_type(shape, col, row) != 0)
            if wanted:
                current = record(col + row * SHAPE_STRIDE)
                if current['group'] == -1:
                    if errors == 0:
                        errors = 1
                elif ZERO_POINT_NINE < current['mx'] and ZERO_POINT_NINE < current['mz']:
                    group = node['groups'][current['group']]
                    face = node['faces'][group['first'] + current['face']]
                    entry, is_flipped = skirt_entry(current, face, uvs, group['slot'])
                    if is_flipped:
                        flipped += 1
                    else:
                        unflipped += 1
            entries.append(entry)
    return {'entries': entries, 'slots': slots, 'uvs': [(f32_bits(u), f32_bits(v)) for (u, v) in uvs], 'base_flags': 0x40, 'flipped': (flipped, unflipped),
            'errors': errors, 'uv_errors': int(normalised['logged'])}


def skirt_entry(current: dict, face: tuple, uvs: list, slot: int) -> tuple:
    """The entry (code, slot) of one cell from its best face: the right angle vertex picks the missing corner of the quad, which is completed from the other three,
    the corner nearest the minimum (u, v) names the orientation (0x80, 0x88, 0x90 or 0xA0) and an edge longer in u than in v flips it (0x40)."""
    if current['dir_x'][0] > 0:
        low_x, high_x = current['edge_x'], (current['edge_x'] + 1) % 3
    else:
        high_x, low_x = current['edge_x'], (current['edge_x'] + 1) % 3
    if current['dir_z'][2] > 0:
        low_z, high_z = current['edge_z'], (current['edge_z'] + 1) % 3
    else:
        high_z, low_z = current['edge_z'], (current['edge_z'] + 1) % 3
    order = [None] * 4
    if low_x == low_z:
        missing = 2
        order[0], order[1], order[3] = low_x, high_z, high_x
    elif high_z == low_x:
        missing = 3
        order[0], order[1], order[2] = low_z, high_z, high_x
    elif high_x == high_z:
        missing = 0
        order[1], order[2], order[3] = low_x, high_x, low_z
    else:
        missing = 1
        order[0], order[2], order[3] = low_x, high_z, high_x
    u = [0.0] * 4
    v = [0.0] * 4
    for k in range(4):
        if k != missing:
            u[k], v[k] = uvs[face[order[k]]]
    if missing == 0:
        u[0], v[0] = f32(q(q(u[3] - u[2]) + u[1])), f32(q(q(v[3] - v[2]) + v[1]))
    elif missing == 1:
        u[1], v[1] = f32(q(q(u[2] - u[3]) + u[0])), f32(q(q(v[2] - v[3]) + v[0]))
    elif missing == 2:
        u[2], v[2] = f32(q(q(u[3] - u[0]) + u[1])), f32(q(q(v[3] - v[0]) + v[1]))
    else:
        u[3], v[3] = f32(q(q(u[2] - u[1]) + u[0])), f32(q(q(v[2] - v[1]) + v[0]))
    umin, vmin = u[0], v[0]
    for k in range(1, 4):
        if u[k] < umin:
            umin = u[k]
        if vmin > v[k]:
            vmin = v[k]
    best, best_distance = 0, 3.4028234663852886e+38
    for k in range(4):
        du = q(u[k] - umin)
        dv = q(v[k] - vmin)
        distance = q(q(du * f32(du)) + q(dv * dv))
        if distance < best_distance:
            best, best_distance = k, f32(distance)
    code = (0x80, 0x88, 0x90, 0xA0)[best]
    following = (best + 1) & 3
    if abs(q(u[best] - u[following])) > abs(q(v[best] - v[following])):
        return (code | 0x40, slot), True
    return (code, slot), False


# ---------------------------------------------------------------------------------------------------------------- 0x469A80 the edge vertex conform
# [BIN:TPW-EXE:0x469940 wrapper, 0x469A80 apply branch, 0x4702D0 matrix chain, 0x46FC00 point transform, 0x470040 inverse with 0x46FE20 / 0x46FD40]
# A tree node is a dict: 'flags' (0x1000 conform list, 0x200 skip, 0x20000 drape), 'matrix' (16 floats), 'vertices' [(x, y, z)], 'bounds' (min3, max3), 'children'.
# The landscape is {'origin': (x, z), 'cell': (sx, sz), 'cells': (nx, nz), 'heights': [(nx + 1) * (nz + 1) floats], 'flags': [one byte per cell, bit 4 = other diagonal]}.
CONFORM_TOLERANCE = f32(0.01)
TRACE = None                    # a list: `conform` appends the branches it takes (the tests check that the inputs reach them all)
CONFORM_STRIDE_BYTE = 4        # the landscape's per cell flag table has 4 bytes per cell; bit 4 selects the other diagonal of the drape


def chain_matrix(node: list, parent: list) -> list:
    """[BIN:TPW-EXE:0x4702D0]: parent' = node * parent (a vertex goes through the node, then through the parents). Element [4a + b] = (L[8 + b] P[4a + 2] +
    L[4 + b] P[4a + 1]) + L[b] P[4a] for the three rotation rows (L = parent, P = node), the translation row adds L[12 + b] last; one float store per element,
    the elements 3, 7, 11 and 15 stay."""
    out = list(parent)
    for a in range(4):
        for b in range(3):
            total = q(q(parent[8 + b] * node[4 * a + 2]) + q(parent[4 + b] * node[4 * a + 1]))
            total = q(total + q(parent[b] * node[4 * a]))
            if a == 3:
                total = q(total + parent[12 + b])
            out[4 * a + b] = f32(total)
    return out


def transform_point(m: list, p: tuple) -> tuple:
    """[BIN:TPW-EXE:0x46FC00]: p * m for a point (translation row added): x' = ((x m0 + m8 z) + m4 y) + m12, y' = ((m9 z + m5 y) + m1 x) + m13,
    z' = ((m10 z + m6 y) + m2 x) + m14, each rounded to a float once."""
    x, y, z = p
    nx = f32(q(q(q(q(x * m[0]) + q(m[8] * z)) + q(m[4] * y)) + m[12]))
    ny = f32(q(q(q(q(m[9] * z) + q(m[5] * y)) + q(m[1] * x)) + m[13]))
    nz = f32(q(q(q(q(m[10] * z) + q(m[6] * y)) + q(m[2] * x)) + m[14]))
    return nx, ny, nz


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
                total = q(total - q(a[k][j] * a[i][k]))
            a[i][j] = f32(total)
        best = 0.0
        pivot = 0
        for i in range(j, 4):
            total = a[i][j]
            for k in range(j):
                total = q(total - q(a[k][j] * a[i][k]))
            a[i][j] = f32(total)
            value = q(abs(total) * scale[i])
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
                a[i][j] = f32(q(reciprocal * a[i][j]))
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
                total = q(total - q(b[j] * a[i][j]))
        elif total != 0.0:
            first = i
        b[i] = f32(total)
    for i in range(3, -1, -1):
        total = b[i]
        for j in range(i + 1, 4):
            total = q(total - q(a[i][j] * b[j]))
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


IDENTITY_MATRIX = IDENTITY


def inverse_exact(matrix: list) -> list:
    """A plain inverse (Gauss-Jordan with partial pivoting, double arithmetic, one float rounding at the end): what a transcription would use instead of the
    original's float LU; used only to show how often the two differ."""
    n = 4
    a = [[float(matrix[4 * i + j]) for j in range(n)] + [1.0 if i == j else 0.0 for j in range(n)] for i in range(n)]
    for col in range(n):
        pivot = max(range(col, n), key=lambda r: abs(a[r][col]))
        a[col], a[pivot] = a[pivot], a[col]
        scale = a[col][col]
        a[col] = [v / scale for v in a[col]]
        for r in range(n):
            if r != col:
                factor = a[r][col]
                a[r] = [v - factor * w for v, w in zip(a[r], a[col])]
    return [f32(a[i][n + j]) for i in range(n) for j in range(n)]


def conform(x: dict) -> dict:
    """[BIN:TPW-EXE:0x469940, 0x469A80 apply mode, second pass (node flag 0x40) not modelled]: walks the instance tree from the root with the identity as the incoming matrix,
    in pre-order. A node with 0x200, or a non-drape node without 0x1000, takes no vertex. A conform node takes the vertices whose index equals the next list entry
    (the list pointer advances once per vertex taken, and the list reads as zero past its end); a drape node takes every vertex. A conform vertex: p * M, per axis
    t = float((w - origin) / cell) (0 when negative), the corner is trunc(t) or one more when the float of the fraction is at least 0.5, the walk of the node ends
    (and its children are skipped) when that corner is past the last one; a fraction within 0.01 of a grid line snaps w onto it; y is the corner height; the
    result goes back through the inverse of M and the node's bounds widen. A drape vertex blends the heights of the cell's triangle instead, minus the base height."""
    land = x['landscape']
    origin, cell, cells, heights = land['origin'], land['cell'], land['cells'], land['heights']
    base_height = x['base_height']
    entries = list(x['list'])
    position = [0]

    def next_entry() -> int:
        return entries[position[0]] if position[0] < len(entries) else 0

    def cell_flags(index: int) -> int:
        table = land['flags']
        return table[index] if 0 <= index < len(table) else 0

    def height(index: int) -> float:
        return heights[index] if 0 <= index < len(heights) else 0.0

    def walk(node: dict, incoming: list) -> None:
        matrix = chain_matrix([f32(v) for v in node['matrix']], incoming)
        flags = node['flags']
        drape = bool(flags & 0x20000)
        if not drape and not flags & 0x1000:
            pass_vertices = False
        else:
            pass_vertices = True
        if flags & 0x200:
            pass_vertices = False
        if pass_vertices:
            inverse_matrix = inverse(matrix)
            for index, source in enumerate(node['source']):
                if not drape and index != next_entry():
                    continue
                w = list(transform_point(matrix, tuple(f32(c) for c in source)))
                fractions = []
                corners = []
                whole_parts = []
                for axis in (0, 1):
                    a = 0 if axis == 0 else 2
                    value = q(q(w[a] - origin[axis]) / cell[axis]) if cell[axis] != 0 else math.inf
                    t = f32(value) if not value < 0.0 else 0.0
                    if value < 0.0 and TRACE is not None:
                        TRACE.append('clamp')
                    whole = float(math.trunc(t))
                    fraction = f32(t - whole)
                    fractions.append(fraction)
                    whole_parts.append(whole)
                    if drape:
                        continue
                    upper = fraction >= 0.5
                    if upper:
                        if q(whole + 1.0) < 0.0 or not q(whole + 1.0) < cells[axis] + 1:
                            if TRACE is not None:
                                TRACE.append(f'return-upper-{axis}')
                            return
                    else:
                        if whole < 0.0 or not whole < cells[axis] + 1:
                            if TRACE is not None:
                                TRACE.append(f'return-lower-{axis}')
                            return
                    corners.append(int(whole) + (1 if upper else 0))
                    if abs(fraction) < CONFORM_TOLERANCE or abs(q(fraction - 1.0)) < CONFORM_TOLERANCE:
                        w[a] = f32(q(q((whole + 1.0 if upper else whole) * cell[axis]) + origin[axis]))
                        if TRACE is not None:
                            TRACE.append(f'snap-{"upper" if upper else "lower"}-{axis}')
                stride = cells[0] + 1
                if drape:
                    cell_index = int(whole_parts[0]) + int(whole_parts[1]) * cells[0]
                    e4 = int(whole_parts[0]) + stride * int(whole_parts[1])
                    d4 = e4 + 1
                    dc = e4 + stride
                    d8 = dc + 1
                    fx, fz = fractions
                    if not cell_flags(cell_index * CONFORM_STRIDE_BYTE) & 4:
                        upper_triangle, mirrored = (True, False) if not fx < fz else (False, True)
                    else:
                        upper_triangle, mirrored = (False, False) if fx < q(1.0 - fz) else (True, True)
                    if TRACE is not None:
                        TRACE.append(f'drape-{int(bool(cell_flags(cell_index * CONFORM_STRIDE_BYTE) & 4))}-{int(upper_triangle)}{int(mirrored)}')
                    if upper_triangle:
                        difference = q(height(d8) - height(d4))
                    else:
                        difference = q(height(dc) - height(e4))
                    one_minus_fx = q(1.0 - fx)
                    if mirrored:
                        total = q(q(q(one_minus_fx * height(dc)) + q(height(d8) * fx)) + q(q(1.0 - fz) * -difference))
                    else:
                        total = q(q(q(one_minus_fx * height(e4)) + q(height(d4) * fx)) + q(fz * difference))
                    w[1] = f32(q(q(total - base_height) + w[1]))
                else:
                    w[1] = height(corners[0] + (cells[0] + 1) * corners[1])
                    if TRACE is not None:
                        TRACE.append('conform-vertex')
                moved = transform_point(inverse_matrix, tuple(w))
                node['result'][index] = moved
                low, high = node['bounds']
                for axis in range(3):
                    if moved[axis] < low[axis]:
                        low[axis] = moved[axis]
                    elif moved[axis] > high[axis]:
                        high[axis] = moved[axis]
                if flags & 0x1000:
                    position[0] += 1
        for child in node['children']:
            walk(child, matrix)
    root = x['tree']
    work = clone_tree(root)
    walk(work, list(IDENTITY_MATRIX))
    work['flags'] |= 0x10000                  # 0x469940 marks the root once the walk is done
    return {'nodes': flatten_tree(work)}


def clone_tree(node: dict) -> dict:
    """A working copy: the instance's own vertex list starts as the template's, the bounds as given."""
    return {'flags': node['flags'], 'matrix': list(node['matrix']), 'source': [tuple(v) for v in node['vertices']],
            'result': [tuple(v) for v in node['vertices']], 'bounds': [list(node['bounds'][0]), list(node['bounds'][1])],
            'children': [clone_tree(c) for c in node['children']]}


def flatten_tree(node: dict) -> list:
    """Pre-order: (flags, result vertices as float bits, bounds as float bits)."""
    out = [(node['flags'], [tuple(f32_bits(c) for c in v) for v in node['result']],
            tuple(f32_bits(c) for c in node['bounds'][0]) + tuple(f32_bits(c) for c in node['bounds'][1]))]
    for child in node['children']:
        out += flatten_tree(child)
    return out


# ---------------------------------------------------------------------------------------------------------------- 0x466D70 the base height
LERP_BIAS = f32(0.1)            # the corner mapper adds 0.1 (a subtraction of the float -0.1) before it truncates


def box_origin(x: int, z: int, rotation: int, width: int, height: int) -> tuple:
    """[BIN:TPW-EXE:0x466F90]: the footprint box's lowest corner cell from the anchor cell: the anchor itself at 0, (x, z + 1 - width) at 90,
    (x + 1 - width, z + 1 - height) at 180 and (x + 1 - height, z) at 270."""
    if rotation == 90:
        return x, z + 1 - width
    if rotation == 180:
        return x + 1 - width, z + 1 - height
    if rotation == 270:
        return x + 1 - height, z
    return x, z


def corner_vertex(rotation: int, x: float, z: float, x1: float, z1: float, fa: float, fb: float) -> tuple:
    """[BIN:TPW-EXE:0x454050]: the grid vertex (truncated, after adding 0.1) of the position (fa along the shape's width, fb along its height) of a box from (x, z)
    to (x1, z1); the box is turned by the rotation. Each coordinate is f * b + (1 - f) * a in double."""
    def lerp(a: float, b: float, f: float) -> int:
        return ftol(((f * b) + ((1.0 - f) * a)) + LERP_BIAS)
    if rotation == 90:
        return lerp(x, x1, fb), lerp(z1, z, fa)
    if rotation == 180:
        return lerp(x1, x, fa), lerp(z1, z, fb)
    if rotation == 270:
        return lerp(x1, x, fb), lerp(z, z1, fa)
    return lerp(x, x1, fa), lerp(z, z1, fb)


def base_height(x: dict) -> float:
    """[BIN:TPW-EXE:0x466D70]: for every cell of the shape's width x height rectangle whose type has a non-zero low nibble (every cell for a NULL shape or flag 0x10) the
    four corners are mapped to terrain vertices (the position fractions are float accumulators of 1/width and 1/height, stepped per column and row); the result is
    (lowest + highest corner height) * 0.5, 0 for an empty footprint (the extremes start at +-10000)."""
    anchor_x, anchor_z = x['anchor']
    rotation, width, height = x['rotation'], x['width'], x['height']
    box_x, box_z = box_origin(anchor_x, anchor_z, rotation, width, height)
    origin_x, origin_z = float(box_x), float(box_z)
    span_x, span_z = (width, height) if rotation in (0, 180) else (height, width)
    far_x, far_z = f32(span_x + origin_x), f32(span_z + origin_z)
    step_w, step_h = f32(divide(1.0, float(width))), f32(divide(1.0, float(height)))
    land = x['heights']
    lowest, highest = 10000.0, -10000.0
    column_fraction = 0.0
    for column in range(width):
        row_fraction = 0.0
        for row in range(height):
            kind = shape_type(x['shape'], column, row) if x['shape'] is not None else 0
            if x['shape'] is None or kind & 0xF or x['flags'] & 0x10:
                for corner in range(4):
                    fa = f32(column_fraction + (step_w if corner & 1 else 0.0))
                    fb = f32(row_fraction + (step_h if corner & 2 else 0.0))
                    vx, vz = corner_vertex(rotation, origin_x, origin_z, far_x, far_z, fa, fb)
                    index = (land['cells_x'] + 1) * vz + vx
                    value = land['values'][index] if 0 <= index < len(land['values']) else 0.0
                    if value > highest:
                        highest = value
                    if value < lowest:
                        lowest = value
            row_fraction = f32(step_h + row_fraction)
        column_fraction = f32(step_w + column_fraction)
    return f32((lowest + highest) * 0.5)


def base_height_csharp(x: dict) -> float:
    """The rule OpenTPW's ParkObjects.GetBaseHeight states: the four corners of every shape cell whose type has a non-zero low nibble, put on the grid with
    ObjectFootprint.ToGrid at the anchor and rotation (an integer after the rounding), lowest and highest height, (lowest + highest) * 0.5 in single precision;
    0 when no cell counts."""
    anchor_x, anchor_z = x['anchor']
    rotation = x['rotation']
    land = x['heights']
    lowest, highest = math.inf, -math.inf
    for row in range(x['height']):
        for column in range(x['width']):
            kind = shape_type(x['shape'], column, row) if x['shape'] is not None else 4
            if not (kind & 0xF or x['flags'] & 0x10 or x['shape'] is None):
                continue
            for corner in range(4):
                lx, ly = column + (corner & 1), row + (corner >> 1)
                if rotation == 0:
                    px, pz = anchor_x + lx, anchor_z + ly
                elif rotation == 90:
                    px, pz = anchor_x + ly, anchor_z + 1 - lx
                elif rotation == 180:
                    px, pz = anchor_x + 1 - lx, anchor_z + 1 - ly
                else:
                    px, pz = anchor_x + 1 - ly, anchor_z + lx
                index = (land['cells_x'] + 1) * pz + px
                value = land['values'][index] if 0 <= index < len(land['values']) else 0.0
                lowest, highest = min(lowest, value), max(highest, value)
    return 0.0 if math.isinf(lowest) else f32(f32(lowest + highest) * 0.5)
