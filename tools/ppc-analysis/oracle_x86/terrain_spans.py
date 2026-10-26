"""The reading of tp.exe's terrain visibility spans (docs/LND.md, "The visible cells"): 0x56BB00 and the routines it calls.

The original builds four corner rays through the screen corners, puts each on the ground between the level's lowest and highest height
(0x56B940), grows a convex hull in the ground plane from the camera and those four points (0x56BD60), clips it to the heightfield rectangle
(0x56C5A0 with 0x56D590 and 0x56D520), scales it to cell units and rasterises it into per row start and end columns (0x56D600). The
spans are the 128 signed byte start and end columns of the rows MIN..MAX; the procedural cell list of 0x56F090 and the static draw read them.

Every operation is a single precision operation, as the x87 does with the 24-bit precision control DDSCL_FPUSETUP gives (the oracle sets
the control word 0x7F); `f32` rounds. The ground plane is x and z, y is the height. Dev-only; no original bytes."""
from __future__ import annotations

import math
import struct


def f32(value: float) -> float:
    """Round to a single (an operation of a double on two singles rounded once is the single operation)."""
    try:
        return struct.unpack('<f', struct.pack('<f', value))[0]
    except OverflowError:
        return math.copysign(math.inf, value)


PI32 = f32(math.pi)                 # [0x701450]
SIXTIETH = struct.unpack('<f', bytes.fromhex('610b363b'))[0]   # [0x701494] 1/360 as a single
NEAR_ONE = f32(0.999999)            # [0x70148c]
EPSILON_Y = struct.unpack('<f', struct.pack('<I', 0x3A83126F))[0]  # [0x701488] 0.001


def ftol(value: float) -> int:
    """__ftol (0x67A830): truncation toward zero."""
    return int(value)


def corner_rays(fov: float, aspect: float) -> list[tuple[float, float, float]]:
    """0x56BB00's four rays, (-w, h, 1), (w, h, 1), (w, -h, 1) and (-w, -h, 1): h = tan(fov pi / 360) with the single roundings of the x87 sequence, w = h / aspect."""
    angle = f32(f32(fov * PI32) * SIXTIETH)
    h = f32(math.tan(angle))
    w = f32(h / aspect)
    return [(-w, h, 1.0), (w, h, 1.0), (w, -h, 1.0), (-w, -h, 1.0)]


def rotate(matrix: tuple, ray: tuple[float, float, float]) -> tuple[float, float, float]:
    """0x578D20 case 3: the ray through the matrix's columns (m0 m1 m2 right, m4 m5 m6 up, m8 m9 m10 forward); matrix is the 9 values (m0, m1, m2, m4, m5, m6, m8, m9, m10)."""
    m0, m1, m2, m4, m5, m6, m8, m9, m10 = matrix
    x, y, z = ray
    # the x87's order of the three products (found with the oracle: the other pairings differ in the last bit): the two nearer terms first
    out_x = f32(f32(f32(m0 * x) + f32(m8 * z)) + f32(m4 * y))
    out_y = f32(f32(f32(m5 * y) + f32(m9 * z)) + f32(m1 * x))
    out_z = f32(f32(f32(m6 * y) + f32(m10 * z)) + f32(m2 * x))
    return out_x, out_y, out_z


def ground_point(camera: tuple, direction: tuple, low: float, high: float, max_distance: float) -> tuple[float, float, float]:
    """0x56B940: the point of the ray on the ground. t1 and t2 are the ray's parameters at the lowest and the highest height, each replaced by half
    the maximum distance when it is negative or beyond it; the point is at t2 when they are equal and at their mean otherwise."""
    dx, dy, dz = direction
    cx, cy, cz = camera
    if dy == 0.0:
        dy = EPSILON_Y
    half = f32(max_distance * 0.5)
    t1 = f32(f32(low - cy) / dy)
    if t1 < 0.0 or t1 > half:
        t1 = half
    t2 = f32(f32(high - cy) / dy)
    if t2 < 0.0 or t2 > half:
        t2 = half
    t = t2 if t1 == t2 else f32(f32(t2 + t1) * 0.5)
    return f32(f32(t * dx) + cx), f32(f32(t * dy) + cy), f32(f32(t * dz) + cz)


def _cross(point: tuple, a: tuple, b: tuple) -> float:
    """(p.x - a.x) * -(b.z - a.z) + (p.z - a.z) * (b.x - a.x)"""
    return f32(f32(f32(point[0] - a[0]) * -f32(b[2] - a[2])) + f32(f32(point[2] - a[2]) * f32(b[0] - a[0])))


BRANCHES: dict[str, int] = {}


def _hit(name: str) -> None:
    BRANCHES[name] = BRANCHES.get(name, 0) + 1


def hull_add(points: list, point: tuple) -> list:
    """0x56BD60: adds a point to the convex hull (counter clockwise by the cross product's sign) of at most 16 points in x and z."""
    n = len(points)
    if n == 0:
        _hit('hull0')
        return [point]
    if n == 1:
        _hit('hull1')
        return points + [point] if (point[0] != points[0][0] or point[2] != points[0][2]) else points
    if n == 2:
        side = _cross(point, points[0], points[1])
        _hit('hull2')
        if side > 0.0:
            _hit('hull2-left')
            return [points[0], point, points[1]]
        if side < 0.0:
            _hit('hull2-right')
        if side == 0.0:
            _hit('hull2-collinear')
            if f32(points[1][0] - points[0][0]) == 0.0:
                s = f32(f32(point[2] - points[0][2]) / f32(points[1][2] - points[0][2]))
            else:
                s = f32(f32(point[0] - points[0][0]) / f32(points[1][0] - points[0][0]))
            if s <= 1.0:
                if s < 0.0:
                    return [point, points[1]]
                return points
            return [points[0], point]
        return points + [point]
    flags = [False] * n
    for e in range(n - 1, -1, -1):
        j = e - 1 if e - 1 >= 0 else n - 1
        flags[e] = 0.0 < _cross(point, points[j], points[e])
    count = sum(flags)
    if count == 0:
        _hit('hull-inside')
        return points
    first = flags.index(True)
    if count < 2:
        _hit('hull-one-edge')
        return points[:first] + [point] + points[first:]
    _hit('hull-many-edges')
    high = max(e for e in range(n) if flags[e])
    cursor = high
    while cursor > -1 and flags[cursor]:
        cursor -= 1
    start = cursor + 1
    rotated = [points[(k + start) % n] for k in range(n)]
    rotated_flags = [flags[(k + start) % n] for k in range(n)]
    keep = count - 1
    result = [point, rotated[keep]]
    index = 1
    while index != 0:
        if not rotated_flags[index]:
            result.append(rotated[index])
        index += 1
        if index == n:
            index = 0
    return result


def _cut_z(cur: tuple, prev: tuple, bound: float) -> tuple[float, float, float]:
    """0x56D590: the edge's point at z = bound, f = (bound - prev.z) / (cur.z - prev.z)."""
    f = f32(f32(bound - prev[2]) / f32(cur[2] - prev[2]))
    return f32(f32(f32(cur[0] - prev[0]) * f) + prev[0]), f32(f32(f32(cur[1] - prev[1]) * f) + prev[1]), bound


def _cut_x(cur: tuple, prev: tuple, bound: float) -> tuple[float, float, float]:
    """0x56D520: the edge's point at x = bound, f = (bound - prev.x) / (cur.x - prev.x)."""
    f = f32(f32(bound - prev[0]) / f32(cur[0] - prev[0]))
    return bound, f32(f32(f32(cur[1] - prev[1]) * f) + prev[1]), f32(f32(f32(cur[2] - prev[2]) * f) + prev[2])


def _clip_axis(points: list, axis: int, low: float, high: float, cut) -> list:
    """One pass of 0x56C5A0: each edge (prev, cur) with the classes 0 (at or below low), 1 and 2 (above high) of its ends, code = prev + 3 cur."""
    out = []
    prev = points[-1] if points else None
    for cur in points:
        dcur = (low < cur[axis]) + (high < cur[axis])
        dprev = (low < prev[axis]) + (high < prev[axis])
        code = dprev + 3 * dcur
        if code == 1:
            out += [prev, cut(cur, prev, low)]
        elif code == 2:
            out += [cut(cur, prev, high), cut(cur, prev, low)]
        elif code == 3:
            out += [cut(cur, prev, low)]
        elif code == 4:
            out += [prev]
        elif code == 5:
            out += [cut(cur, prev, high)]
        elif code == 6:
            out += [cut(cur, prev, low), cut(cur, prev, high)]
        elif code == 7:
            out += [prev, cut(cur, prev, high)]
        prev = cur
    return out


def clip(points: list, x_low: float, x_high: float, z_low: float, z_high: float) -> list:
    """0x56C5A0 (param_1 .. param_4 = x_low, x_high, z_low, z_high): the z slab first, then the x slab."""
    return _clip_axis(_clip_axis(points, 2, z_low, z_high, _cut_z), 0, x_low, x_high, _cut_x)


def _signed(value: int) -> int:
    value &= 0xFF
    return value - 256 if value > 127 else value


def rasterise(points: list) -> tuple[int, int, list, list]:
    """0x56D600: per row start (the smallest column the edges touch) and end (the largest column + 0.999999) bytes of the polygon in cell units."""
    start = [0x7F] * 128
    end = [0] * 128
    min_row, max_row = 0x7F, 0
    n = len(points)
    prev_index = n - 1
    for index in range(n):
        cur, prev = points[index], points[prev_index]
        z_cur, z_prev = cur[2], prev[2]
        if z_cur > z_prev:
            a, b = ftol(prev[0]), ftol(cur[0])
            z_min, z_max = ftol(z_prev), ftol(f32(z_cur + NEAR_ONE))
            slope = f32(f32(cur[0] - prev[0]) / f32(z_cur - z_prev))
            x = prev[0]
            if ftol(z_cur) > ftol(z_prev):
                k = ftol(f32(z_prev + NEAR_ONE))
                x = f32(f32(f32(k - z_prev) * slope) + x)
            min_row = min(min_row, z_min)
            max_row = max(max_row, z_max)
            if start[z_min] > a:
                start[z_min] = _signed(a)
            if start[z_max] > b:
                start[z_max] = _signed(b)
            if start[z_min] > ftol(x):
                start[z_min] = _signed(ftol(x))
            row = z_min + 1
            while row < z_max:
                if start[row] > ftol(x):
                    start[row] = _signed(ftol(x))
                if row < z_max - 1:
                    x = f32(x + slope)
                    if start[row] > ftol(x):
                        start[row] = _signed(ftol(x))
                row += 1
            if start[z_max] > ftol(x):
                start[z_max] = _signed(ftol(x))
        elif z_cur < z_prev:
            a, b = ftol(f32(prev[0] + NEAR_ONE)), ftol(f32(cur[0] + NEAR_ONE))
            z_hi, z_lo = ftol(f32(z_prev + NEAR_ONE)), ftol(z_cur)
            slope = f32(f32(prev[0] - cur[0]) / f32(z_prev - z_cur))
            x = f32(cur[0] + NEAR_ONE)
            if ftol(z_cur) < ftol(z_prev):
                k = ftol(f32(z_cur + NEAR_ONE))
                x = f32(f32(f32(k - z_cur) * slope) + x)
            min_row = min(min_row, z_lo)
            max_row = max(max_row, z_hi)
            if end[z_hi] < a:
                end[z_hi] = _signed(a)
            if end[z_lo] < b:
                end[z_lo] = _signed(b)
            if end[z_lo] < ftol(x):
                end[z_lo] = _signed(ftol(x))
            row = z_lo + 1
            while row < z_hi:
                if end[row] < ftol(x):
                    end[row] = _signed(ftol(x))
                if row < z_hi - 1:
                    x = f32(x + slope)
                    if end[row] < ftol(x):
                        end[row] = _signed(ftol(x))
                row += 1
            if end[z_hi] < ftol(x):
                end[z_hi] = _signed(ftol(x))
        prev_index = index
    for row in range(min_row, max_row + 1):
        if end[row] < start[row]:
            end[row] = start[row]
    return min_row, max_row, start, end


def spans(x: dict) -> dict:
    """0x56BB00 on the input: {'camera', 'matrix' (9 values), 'low', 'high', 'fov', 'aspect', 'max_distance', 'origin' (x, z), 'cells' (x, z), 'size' (x, z)}.
    Returns the rows, the start and end bytes of the narrow spans (out 1, +0xC4) and the largest corner distance handed to 0x578CA0."""
    camera = tuple(x['camera'])
    far = 10.0
    hull = [camera]
    for ray in corner_rays(x['fov'], x['aspect']):
        direction = rotate(tuple(x['matrix']), ray)
        point = ground_point(camera, direction, x['low'], x['high'], x['max_distance'])
        hull = hull_add(hull, point)
        dx, dy, dz = f32(point[0] - camera[0]), f32(point[1] - camera[1]), f32(point[2] - camera[2])
        distance = f32(math.sqrt(f32(f32(f32(dx * dx) + f32(dy * dy)) + f32(dz * dz))))
        if not distance <= far:
            far = distance
    origin, cells, size = x['origin'], x['cells'], x['size']
    x_low, z_low = origin
    x_high = f32(f32(origin[0] + f32(cells[0] * size[0])) - size[0])
    z_high = f32(f32(origin[1] + f32(cells[1] * size[1])) - size[1])
    hull = clip(hull, x_low, x_high, z_low, z_high)
    inv_x, inv_z = f32(1.0 / size[0]), f32(1.0 / size[1])
    hull = [(f32(p[0] * inv_x), p[1], f32(p[2] * inv_z)) for p in hull]
    min_row, max_row, start, end = rasterise(hull)
    return {'min': min_row, 'max': max_row, 'start': start, 'end': end, 'far': far}


def pack(result: dict) -> bytes:
    """The comparable form of a result: min row, max row (int32), the 128 start and 128 end bytes, the far distance as a single."""
    return struct.pack('<ii128b128bf', result['min'], result['max'], *result['start'], *result['end'], result['far'])


def random_scene(rng) -> dict:
    """A camera over the heightfield: a random yaw, a pitch from slightly up to straight down, sometimes a roll or a mirrored (left handed) matrix, a
    random field of view, aspect, heights, maximum distance and heightfield rectangle (an origin of at least 0: the row bytes are signed and the clip keeps rows >= 0)."""
    yaw = rng.uniform(0, 2 * math.pi)
    pitch = rng.uniform(math.radians(-10), math.radians(89))
    roll = rng.uniform(-0.3, 0.3) if rng.random() < 0.3 else 0.0
    fwd = (math.cos(yaw) * math.cos(pitch), -math.sin(pitch), math.sin(yaw) * math.cos(pitch))
    up0 = (math.cos(yaw) * math.sin(pitch), math.cos(pitch), math.sin(yaw) * math.sin(pitch))
    right = (fwd[1] * up0[2] - fwd[2] * up0[1], fwd[2] * up0[0] - fwd[0] * up0[2], fwd[0] * up0[1] - fwd[1] * up0[0])
    if rng.random() < 0.25:
        right = tuple(-v for v in right)
    right, up = (tuple(math.cos(roll) * a + math.sin(roll) * b for a, b in zip(right, up0)),
                 tuple(-math.sin(roll) * a + math.cos(roll) * b for a, b in zip(right, up0)))
    camera = tuple(f32(c) for c in (rng.uniform(-100, 1100), rng.uniform(40, 320), rng.uniform(-200, 1000)))
    return {
        'camera': camera,
        'matrix': tuple(f32(v) for v in (*right, *up, *fwd)),
        'low': f32(rng.uniform(-20, 10)), 'high': f32(rng.uniform(10, 80)),
        'fov': rng.choice([90.0, 90.0, 60.0, f32(rng.uniform(40, 120))]),
        'aspect': f32(rng.choice([0.75, 0.5625, 0.6, 0.5])),
        'max_distance': f32(rng.uniform(200, 1500)),
        'origin': rng.choice([(0.0, 0.0), (f32(rng.uniform(0, 30)), f32(rng.uniform(0, 30)))]),
        'cells': rng.choice([(96, 85), (96, 85), (40, 30), (10, 10)]),
        'size': rng.choice([(10.0, 10.0), (10.0, 10.0), (f32(rng.uniform(4, 16)), f32(rng.uniform(4, 16)))]),
    }


def special_scenes() -> list[dict]:
    """Scenes for the branches random cameras rarely reach: a vanishing width (equal and collinear hull points), a singular matrix (a ray with no height, y = 0 replaced by 0.001) and the level's usual 96 x 85 field straight above."""
    base = {'camera': (480.0, 200.0, 420.0), 'matrix': (1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, -1.0, 0.0), 'low': 0.0, 'high': 30.0, 'fov': 90.0,
            'aspect': 0.75, 'max_distance': 600.0, 'origin': (0.0, 0.0), 'cells': (96, 85), 'size': (10.0, 10.0)}
    return [
        dict(base),                                                              # straight down
        dict(base, aspect=f32(1e30)),                                            # w vanishes: the corner points coincide in pairs
        dict(base, aspect=f32(3e38)),
        dict(base, matrix=(1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0), camera=(480.0, 0.0, 420.0)),   # a ray with no height: dy = 0
        dict(base, camera=(-200.0, 100.0, 420.0)),                               # the hull lies outside the rectangle on one side
        dict(base, camera=(480.0, 5000.0, 420.0), max_distance=300.0),           # every ray's parameter beyond the half distance
    ]
