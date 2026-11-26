"""Python restatement of OpenTPW's port of tp.exe's walk (WalkEdges, WalkSearch, GuestNavigation.Navigate), the plan side of
the x86 walk targets (docs/reverse/ORACLE.md section 7, X25). Pure Python: no executable bytes, nothing imported from the game.

* `blocked` the edge test 0x4d8750 and `commit` the step commit 0x510510 over a synthetic cell map;
* `find` the route search 0x511420 (line search 0x511470 with its tracers 0x511c90, shortcuts 0x5110d0, refinement 0x5108a0);
* `avoid_walls` 0x50e4a0 with the ray test 0x50cc20;
* `Walk` the walk set-up 0x50f8e0 and the steering step 0x50f3b0 (follow_path 0x50ed10, progress 0x50fd40), separation without
  neighbours, with every member of the navigator object (NV2): the local axes the step writes, the five subpath_dist slots, the whole
  progress history word, and `record` / `load_record` the 177-byte serializer 0x50d150 (Theme Park Inc 0x63ad30).

A `Map` with `tpi` set reads Theme Park Inc's edge test 0x5d7b60 (WalkEdgeRules.ThemeParkInc): an obstacle's walkway cell (the obstacle
flag, cell +0x24) is no walkway fast path, unowned land (flag 0x40) blocks after the path and walkway rules, and type 8 blocks. Only masks 0
and 1 are read that way: no Theme Park Inc walk passes 2 or 3 (docs/reverse/TPI-NAVIGATOR.md). The commit and the search are the same.
A `Map` with `tpi` also takes the walk's integer square root (0x5d2060) as Game.exe runs it at the game thread's control word 0x7f: fsqrt
rounded to a single's 24-bit significand, then truncated (_ftol); tp.exe's 0x4d49a0 is the same code, read at 64 bits as X25 runs it.

NV3 (ThemeParkIncWalkStep, GuestSimulation.StepThemeParkInc, RequestRoute / RouteToTpiPoint, ThemeParkIncStaffWorld.StaffRoute, GuestSpeed.Update):
`walk_step` the rest of Theme Park Inc's walk step 0x60b460 around the steering step (the heading word, D1 and D2, the template and the
interval), `speed_step` 0x60bc90, `route_point` / `route_cell` the route requests 0x60b8e0 / 0x60b7f0 with the stranded test 0x60bb50,
and `staff_route` 0x62eb10, all with the stamp counter 0x5d81f0 counted at every call.
"""
import math
import struct
D = [(0, -1), (1, 0), (0, 1), (-1, 0)]
FAIL = 0x70000000

class Map:
    def __init__(self, types=None, links=None, qlinks=None, flags=None, obstacles=None, tpi=False):
        self.types = types or {}; self.links = links or {}; self.qlinks = qlinks or {}; self.flags = flags or {}
        self.obstacles = obstacles or set(); self.tpi = tpi
        self.records = {}  # cell -> (kind, flag nibble): the target structure record rule (tp.exe 0x4d89c3, TPI 0x5d7dfa); extensions are not modelled
    def t(self, x, y): return self.types.get((x, y), 0)
    def l(self, x, y): return self.links.get((x, y), 0)
    def q(self, x, y): return self.qlinks.get((x, y), 0)
    def f(self, x, y): return self.flags.get((x, y), 0)

def single_root(m):
    """Whether the walk's square root rounds to a single (Game.exe at the control word 0x7f); `root_64` reads it at 64 bits instead."""
    return m.tpi and not getattr(m, 'root_64', False)

def blocked(m, x, y, d, mask):
    x &= 0xff; y &= 0xff; d &= 0xff
    if x == 0 and d == 3: return 1
    if y == 0 and d == 0: return 1
    if x == 0x7f and d == 1: return 1
    if y == 0x7f and d == 2: return 1
    if d > 3:
        return None  # index not shifted: neighbour = the cell itself (not modelled)
    tx, ty = x + D[d][0], y + D[d][1]
    # cells beyond 127 in x wrap into the next row in the original's 1-based index; only reached at x == 127, handled above
    ft, tt = m.t(x, y), m.t(tx, ty)
    queue = lambda t: t in (3, 9)
    if tt == 1 and not queue(ft): return 0
    if ft == 4 and tt == 4: return 0
    if tt == 4: return 1
    if tt == 0x1e and not (m.tpi and (tx, ty) in m.obstacles):  # 0x5363a0; TPI 0x67f6f0 also needs +0x24 == 0
        if mask != 2: return 0
        go871 = True
    else:
        go871 = mask == 2
    if go871:
        if m.f(tx, ty) & 0x400: return 0
        # type 9 thing-list check: not modelled (no things)
    if m.tpi and m.f(tx, ty) & 0x40: return 1  # TPI 0x5d7d0d: unowned
    if tt in (5, 7, 2) or (m.tpi and tt == 8): return 1  # TPI 0x67f780 adds type 8
    if tt == 2: return 1
    if queue(tt):
        if ft == 1 or queue(ft):
            links = m.l(tx, ty)
            back = {0: 0x10, 1: 0x40, 2: 0x01, 3: 0x04}[d]
            return 1 if (links & back) == 0 else 0
        return 1
    if queue(ft) and tt == 1:
        links = m.l(tx, ty)
        back = {0: 0x10, 1: 0x40, 2: 0x01, 3: 0x04}[d]
        return 1 if (links & back) == 0 else 0
    # the target's structure record (0x5363f0 / TPI 0x67f890): a track kind with flag nibble 0 blocks (WalkEdges.TargetRecordBlocks)
    record = m.records.get((tx, ty))
    if record is not None and record[0] in (0xB, 0xD, 0x10, 0x12, 0x19) and record[1] & 0xF == 0: return 1
    if ft != 1 and not queue(ft): return 0
    if mask == 1 and ft == 1 and tt != 1 and not queue(tt) and tt != 4: return 0
    if mask == 2 and ft == 1 and tt == 0: return 0
    if ft != 10 and ft != 9: return 1
    if tt != 0: return 1
    ql = m.q(x, y)
    if d == 3 and ql == 4: return 0
    if d == 1 and ql == 0x40: return 0
    if d == 0 and ql == 0x10: return 0
    if d == 2 and ql == 1: return 0
    return 1

class G:  # line globals
    pass

def line_setup(g, s, e):
    dx = e[0] - s[0]; dy = e[1] - s[1]
    g.adx = abs(dx); g.ady = abs(dy)
    g.ydir = 2 if dy >= 1 else 0
    g.xdir = 1 if dx >= 1 else 3
    if s[0] == e[0]: g.xdir = g.ydir
    if s[1] == e[1]: g.ydir = g.xdir
    g.err = g.adx // 2 + g.ady // 2

def line_next(g):
    if g.err >= g.adx:
        g.err -= g.adx; return g.ydir
    g.err += g.ady; return g.xdir

def add(p, d):
    return ((p[0] + D[d][0]) & 0xff, (p[1] + D[d][1]) & 0xff)

class Tracer:
    def __init__(self, pos, d, turn, goal):
        self.pos = pos; self.hit = pos; self.dir = d; self.turn = turn; self.goal = goal; self.path = []

def trace_step(m, mask, t):
    """one tracer step (0x511c90, inlined for A in 0x511616..0x511852)"""
    if t.dir == 0x33: return
    if blocked(m, t.pos[0], t.pos[1], t.dir, mask):
        t.dir = (t.dir - t.turn) & 3
        record = True
    else:
        t.pos = add(t.pos, t.dir)
        nd = (t.dir + t.turn) & 3
        if not blocked(m, t.pos[0], t.pos[1], nd, mask):
            t.dir = nd; record = True
        else:
            record = False
    if record:
        t.path.append(t.pos)
        if len(t.path) & 0xff > 0xfb:
            t.dir = 0x33; return
    gx, gy = t.goal; x, y = t.pos
    c = (t.dir + t.turn) & 3
    if c == 0 and gy < y: return
    if c == 1 and gx > x: return
    if c == 2 and gy > y: return
    if c == 3 and gx < x: return
    c = t.dir
    if c == 0 and gy > y: return
    if c == 1 and gx < x: return
    if c == 2 and gy < y: return
    if c == 3 and gx > x: return
    hx, hy = t.hit
    if not (min(hx, gx) <= x <= max(hx, gx)): return
    if not (min(hy, gy) <= y <= max(hy, gy)): return
    if (hx, hy) != (x, y): t.dir = 0x32

class Buf:
    def __init__(self, start, goal):
        self.start = start; self.goal = goal; self.path = []
    @property
    def count(self): return len(self.path) & 0xff

def search(m, mask, buf, start, limit, steps):
    """0x511470: returns (result, steps)"""
    g = G()
    goal = buf.goal
    line_setup(g, start, goal)
    cur = start; first = True; d = 0
    def end():
        if len(buf.path) < 0xfa and cur == goal:
            buf.path.append(cur); return steps
        return FAIL
    if steps >= limit: return end(), steps
    while True:
        # 0x5114c0
        if cur == goal: return end(), steps
        prev = d
        d = line_next(g)
        if blocked(m, cur[0], cur[1], d, mask):
            buf.path.append(cur)
            a = Tracer(cur, (d + 1) & 3, -1, goal)
            b = Tracer(cur, (d - 1) & 3, 1, goal)
            outcome = None
            if steps < limit:
                while True:
                    trace_step(m, mask, a)
                    if a.dir not in (0x33, 0x32):
                        if add(a.pos, a.dir) == b.pos and ((a.dir - 2) & 3) == b.dir:
                            buf.path.append(cur); return FAIL, steps
                    trace_step(m, mask, b)
                    if a.dir not in (0x33, 0x32):
                        if add(a.pos, a.dir) == b.pos and ((a.dir - 2) & 3) == b.dir:
                            buf.path.append(cur); return FAIL, steps
                    if a.dir == 0x32: outcome = a; break
                    if b.dir == 0x32: outcome = b; break
                    if a.dir == 0x33 and b.dir == 0x33:
                        buf.path.append(cur); return FAIL, steps
                    steps += 1
                    if steps >= limit: break
                if outcome is not None:
                    if len(buf.path) + len(outcome.path) + 2 > 0xfc:
                        buf.path.append(cur); return FAIL, steps
                    buf.path.extend(outcome.path); buf.path.append(outcome.pos)
            # 0x511a55
            if steps == limit:
                buf.path.append(cur); return FAIL, steps
            simplify(m, mask, buf, 0)
            cur = buf.path[-1] if buf.path else buf.start   # word at path[count - 1]
            line_setup(g, cur, goal)
            first = True
        else:
            saved = cur
            cur = add(cur, d)
            if first:
                first = False
            elif d != prev:
                if not blocked(m, cur[0], cur[1], prev, mask):
                    n = add(cur, prev)
                    if blocked(m, n[0], n[1], d, mask):
                        buf.path.append(cur)
                        if len(buf.path) > 0xfa:
                            buf.path.append(cur); return FAIL, steps
                else:
                    if not blocked(m, saved[0], saved[1], d, mask):
                        n = add(saved, d)
                        if blocked(m, n[0], n[1], prev, mask):
                            buf.path.append(cur)
                            if len(buf.path) > 0xfa:
                                buf.path.append(cur); return FAIL, steps
        steps += 1
        if steps >= limit: return end(), steps

def anchor_of(buf, i):
    return buf.start if i == 0 else buf.path[i - 1]

def simplify(m, mask, buf, frm):
    any_change = False
    while True:
        read = frm; write = frm; anchor = anchor_of(buf, frm); changed = False
        n = len(buf.path)
        while read < n:
            k = 4; shortcut = False
            while k:
                if k + read < n:
                    target = buf.path[read + k]
                    g = G(); line_setup(g, anchor, target)
                    p = anchor; prev = -1; first = True; saved = None; ok = None
                    while True:
                        if p == target: ok = True; break
                        prevd = prev
                        dd = line_next(g); prev = dd
                        if blocked(m, p[0], p[1], dd, mask): ok = False; break
                        if first: first = False
                        elif dd != prevd:
                            if not blocked(m, p[0], p[1], prevd, mask):
                                q = add(p, prevd)
                                if blocked(m, q[0], q[1], dd, mask): ok = False; break
                            if not blocked(m, saved[0], saved[1], dd, mask):
                                q = add(saved, dd)
                                if blocked(m, q[0], q[1], prevd, mask): ok = False; break
                        saved = p; p = add(p, dd)
                    if ok:
                        read += k; changed = True; any_change = True; shortcut = True; break
                k -= 1
            if not shortcut:
                v = buf.path[read]; anchor = v
                buf.path[write] = v; write += 1; read += 1
        del buf.path[write:]
        if not changed: return any_change

def refine(m, mask, buf, result):
    simplify(m, mask, buf, 0)
    budget = 9
    while True:
        i = 0; done = True
        while i + 1 < len(buf.path):
            s = anchor_of(buf, i); e = buf.path[i + 1]
            local = Buf(s, e)
            r, _ = search(m, mask, local, s, 60000, 0)
            if r != FAIL:
                n = len(local.path)
                if not (n > 1 and buf.path[i] in local.path[:n - 1]) and len(buf.path) + n - 2 <= 0xfc:
                    buf.path[i:i + 2] = local.path
                    simplify(m, mask, buf, max(0, i - 4))
                    budget -= 1
                    if budget == 0: return result
                    done = False; break
            i += 1
        if done: break
    j = 0
    while j < len(buf.path):
        a = anchor_of(buf, j); b = buf.path[j]
        dx = b[0] - a[0]; dy = b[1] - a[1]
        if abs(dx) == 1 and abs(dy) == 1:
            sx, sy = dx, dy
            toQb = 0 if sy > 0 else 2; toPb = 3 if sx > 0 else 1
            toPa = 2 if sy > 0 else 0; toQa = 1 if sx > 0 else 3
            viaP = (-sx, 0); viaQ = (0, -sy); off = None
            B = lambda p, d: blocked(m, p[0], p[1], d, mask)
            if B(b, toQb) and not B(b, toPb): off = viaP
            elif B(b, toPb) and not B(b, toQb): off = viaQ
            elif B(a, toPa) and not B(a, toQa): off = viaQ
            elif B(a, toQa) and not B(a, toPa): off = viaP
            if off is not None and len(buf.path) + 1 < 0xfc:
                buf.path.insert(j, ((b[0] + off[0]) & 0xff, (b[1] + off[1]) & 0xff))
                j += 1
        j += 1
    return result

def find(m, start, goal, mask):
    """0x511420"""
    buf = Buf(start, goal)
    r, steps = search(m, mask, buf, start, 60000, 0)
    if r == FAIL:
        return FAIL, buf.path
    refine(m, mask, buf, r)
    return r, buf.path

def commit(m, f, t, mask):
    """0x510510: f, t cells (or None for outside the map); 1 accept, 0 refuse"""
    if t is None: return 0
    fx, fy = f; tx, ty = t
    if abs(fx - tx) > 1 or abs(fy - ty) > 1: return 0
    up = fy - ty == 1; right = tx - fx == 1; down = ty - fy == 1; left = fx - tx == 1
    B = lambda x, y, d: blocked(m, x, y, d, mask)
    if up + right + down + left != 2:
        if up and B(fx, fy, 0): return 0
        if right and B(fx, fy, 1): return 0
        if down and B(fx, fy, 2): return 0
        if left and B(fx, fy, 3): return 0
        return 1
    if up and right:
        if not B(fx, fy, 0) and not B(fx, fy - 1, 1): return 1
        if B(fx, fy, 1): return 0
        return 0 if B(fx + 1, fy, 0) else 1
    if right and down:
        if not B(fx, fy, 1) and not B(fx + 1, fy, 2): return 1
        if B(fx, fy, 2): return 0
        return 0 if B(fx, fy + 1, 1) else 1
    if down and left:
        if not B(fx, fy, 2) and not B(fx, fy + 1, 3): return 1
        if B(fx, fy, 3): return 0
        return 0 if B(fx - 1, fy, 2) else 1
    if up and left:
        if not B(fx, fy, 0) and not B(fx, fy - 1, 3): return 1
        if B(fx, fy, 3): return 0
        return 0 if B(fx - 1, fy, 0) else 1
    return 0

M32 = 0xffffffff
def s32(v):
    v &= M32
    return v - (1 << 32) if v & 0x80000000 else v
def fmul(a, b):  # 0x50df70
    return s32((a * b) >> 16)
def fdiv(a, b):  # 0x50e080: (a << 16) / b truncated toward zero
    n = a * 65536
    q = abs(n) // abs(b)
    return s32(q if (n >= 0) == (b > 0) else -q)
def f32(value):
    """A double rounded to a single (round to nearest even), as the x87 rounds a result at the control word 0x7f."""
    return struct.unpack('<f', struct.pack('<f', value))[0]
def isqrt(v, single=False):
    """0x4d49a0 / 0x5d2060: fild of the unsigned dword, fsqrt, _ftol. At 64 bits the floor of the root; at 24 bits (single) the root
    rounded to a single first, which can reach the next integer (the double root of a 32-bit integer is never a single's midpoint)."""
    v &= M32
    return int(f32(math.sqrt(v))) if single else math.isqrt(v)
def length(x, y, single=False):  # 0x50f600 / 0x50e4a0 length
    ax, ay = abs(x) & M32, abs(y) & M32
    bits = ax | ay
    if bits & 0xfffff000 == 0: return isqrt((ax * ax + ay * ay) & M32, single)
    if bits & 0xffc00000 == 0: return s32(isqrt((((ax >> 8) * (ax >> 8)) + ((ay >> 8) * (ay >> 8))) & M32, single) << 8)
    return s32(isqrt((((ax >> 18) * (ax >> 18)) + ((ay >> 18) * (ay >> 18))) & M32, single) << 18)

def normalize(x, y, single=False):  # 0x63e150 (tp.exe's twin writes the axes at 0x50f57e)
    """The unit vector of (x, y) in 16.16, (0, 0) for a zero vector: below 2^12 the root of (x^2 + y^2) << 6 and the factor 0x80000 / root
    times each component (a 32-bit product); below 2^22 the root of the components >> 8 and 0x1000000 / root, else >> 18 and 0x4000 / root,
    each component FixMul'ed by it."""
    ax, ay = abs(x) & M32, abs(y) & M32
    bits = ax | ay
    def quotient(numerator, root):
        return numerator // root if root else 0
    if bits & 0xfffff000 == 0:
        root = isqrt(((ax * ax + ay * ay) << 6) & M32, single)
        f = quotient(0x80000, root)
        return s32(x * f), s32(y * f)
    shift, numerator = (8, 0x1000000) if bits & 0xffc00000 == 0 else (18, 0x4000)
    ax, ay = s32(ax) >> shift, s32(ay) >> shift
    root = isqrt((ax * ax + ay * ay) & M32, single)
    f = quotient(numerator, root)
    return fmul(x, f), fmul(y, f)

def ray(pos, d, corner, r, single=False):  # 0x50cc20
    mx = s32(pos[0] - corner[0]); my = s32(pos[1] - corner[1])
    a = s32(fmul(d[0], d[0]) + fmul(d[1], d[1]))
    b = s32((fmul(d[0], mx) + fmul(d[1], my)) * 2)
    c = s32(fmul(mx, mx) + fmul(my, my) - fmul(r, r))
    disc = s32(fmul(b, b) - s32(fmul(a, c) * 4))
    if disc <= 0: return -0x80000000
    s = isqrt(disc, single)
    t1 = s32(s - b); t2 = s32(-(s + b))
    return (t2 if t2 <= t1 else t1) >> 1

def avoid_walls(m, nav):
    """0x50e4a0: nav dict with r, x, y, vx, vy, mask"""
    r = nav['r']; mask = nav['mask']; single = single_root(m)
    px = s32(nav['x'] + fmul(nav['vx'], 0x10000)); py = s32(nav['y'] + fmul(nav['vy'], 0x10000))
    cx = (nav['x'] & M32) >> 16; cy = (nav['y'] & M32) >> 16
    vx = s32(px - nav['x']); vy = s32(py - nav['y'])
    ln = length(vx, vy, single)
    dx, dy = (vx, vy) if ln == 0 else (fdiv(vx, ln), fdiv(vy, ln))
    xpos = dx >= 0; ypos = dy >= 0
    B = lambda x, y, d: blocked(m, x, y, d, mask)
    if xpos and B(cx, cy, 1):
        lim = s32(((cx + 1) << 16) - r)
        if px > lim: return (s32((lim - px) * 2), 0)
    if not xpos and B(cx, cy, 3):
        lim = s32((cx << 16) + r)
        if px < lim: return (s32((lim - px) * 2), 0)
    if ypos and B(cx, cy, 2):
        lim = s32(((cy + 1) << 16) - r)
        if py > lim: return (0, s32((lim - py) * 2))
    if not ypos and B(cx, cy, 0):
        lim = s32((cy << 16) + r)
        if py < lim: return (0, s32((lim - py) * 2))
    def corner(diag, dd1, dd2, side1, sd1, side2, sd2, pt):
        if not (B(diag[0], diag[1], dd1) or B(diag[0], diag[1], dd2)): return None
        if B(side1[0], side1[1], sd1) or B(side2[0], side2[1], sd2): return None
        t = ray((nav['x'], nav['y']), (dx, dy), pt, r, single)
        if t == 0x7fffffff or t <= 0 or t > ln: return None
        hx = s32(nav['x'] + fmul(dx, t)); hy = s32(nav['y'] + fmul(dy, t))
        return (fdiv(s32(hx - pt[0]), r), fdiv(s32(hy - pt[1]), r))
    res = None
    if ypos and xpos:
        res = corner((cx + 1, cy + 1), 0, 3, (cx, cy + 1), 0, (cx + 1, cy), 3, ((cx + 1) << 16, (cy + 1) << 16))
    if res is None and ypos and not xpos:
        res = corner((cx - 1, cy + 1), 0, 1, (cx, cy + 1), 0, (cx - 1, cy), 1, (cx << 16, (cy + 1) << 16))
    if res is None and not ypos and not xpos:
        res = corner((cx - 1, cy - 1), 2, 1, (cx - 1, cy), 1, (cx, cy - 1), 2, (cx << 16, cy << 16))
    if res is None and not ypos and xpos:
        res = corner((cx + 1, cy - 1), 2, 3, (cx + 1, cy), 3, (cx, cy - 1), 2, ((cx + 1) << 16, cy << 16))
    return res if res is not None else (0, 0)


ONE = 0x10000
RADIUS = 13107


def octile(x, y):
    ax, ay = abs(x), abs(y)
    return ax + ay - (min(ax, ay) >> 1)


def truncate(x, y, maximum, single=False):  # 0x50f600
    size = length(x, y, single)
    if size < maximum:
        return x, y
    scale = fdiv(maximum, size)
    return fmul(x, scale), fmul(y, scale)


def centre(cell):
    return (cell[0] << 16) + 0x8000, (cell[1] << 16) + 0x8000


def segment(a, b):
    return octile((b[0] - a[0]) << 16, (b[1] - a[1]) << 16)


class Walk:
    """The navigator object (walk +0 .. +0xb8) with block stamps kept by the caller's `stamps` dict. The members start as the constructor
    0x50ffe0 / 0x63ed10 leaves them; `fill` stands for the two it does not write (path_timestamp +0x48 and subpath_dist +0x8c..), which
    hold whatever the allocator's memory held. The waypoint buffer +0x64 keeps the raw 16.16 centres (zero until a set-up writes a slot)."""

    def __init__(self, m, stamps, x, y, vx, vy, mask, max_speed, max_force, fill=0):
        self.m, self.stamps = m, stamps
        self.single = single_root(m)
        self.mass, self.radius = ONE, RADIUS
        self.x, self.y, self.vx, self.vy, self.mask = x, y, vx, vy, mask
        self.max_speed, self.max_force = max_speed, max_force
        self.xaxis, self.yaxis = (0, 0), (0, 0)
        self.formation, self.force = (0, 0), (0, 0)
        self.tx = self.ty = 0
        self.pc = self.cnt = self.idx = 0
        self.arrived = True
        self.buf = [(0, 0)] * 5
        self.subdist = [s32(fill)] * 5
        self.total = self.ahead = self.beyond = self.prog = self.hist = 0
        self.stuck = False
        self.stamp = s32(fill)
        self.counter = 0

    def setup(self, tx, ty, reroute):  # 0x50f8e0 / 0x63e3a0
        start = ((self.x >> 16) & 0xFF, (self.y >> 16) & 0xFF)
        goal = ((tx >> 16) & 0xFF, (ty >> 16) & 0xFF)
        self.tx, self.ty = tx, ty
        r, path = find(self.m, start, goal, self.mask)
        if r == FAIL:
            self.pc = self.cnt = self.total = self.ahead = self.beyond = self.prog = self.hist = self.idx = 0
            self.arrived = True
            self.stuck = True
            return False
        path = list(path)
        if reroute:
            path.insert(0, start)
            del path[0xFC:]
        self.pc = len(path)
        self.cnt = min(self.pc, 5)
        fx, fy = centre(path[0])
        self.total = octile(fx - self.x, fy - self.y) + sum(segment(path[i], path[i + 1]) for i in range(self.pc - 1))
        for i in range(self.cnt):
            self.buf[i] = centre(path[i])
        # the buffered segments, each in its slot (+0x8c + 4 i); the slots from cnt - 1 on keep their old values
        for i in range(self.cnt - 1):
            self.subdist[i] = segment(path[i], path[i + 1])
        self.ahead = sum(self.subdist[i] for i in range(self.cnt - 1))
        self.beyond = sum(segment(path[i], path[i + 1]) for i in range(self.cnt - 1, self.pc - 1))
        self.prog = self.hist = self.idx = 0
        self.arrived = False
        self.stuck = False
        self.counter += 1
        self.stamp = self.counter
        return True

    def progress(self):  # 0x50fd40
        if self.arrived:
            return ONE
        wx, wy = self.buf[min(self.idx, 4)]
        remaining = octile(wx - self.x, wy - self.y) + self.ahead + self.beyond
        if self.total == 0:
            return ONE
        q = abs(remaining) * 65536 // self.total
        return s32(ONE - (q if remaining >= 0 else -q))

    def follow(self):  # 0x50ed10 / 0x63d060
        # an unsigned comparison (jbe at 0x50ed6f / 0x63d0bf): a block stamped after the set-up's counter value
        if (self.stamp & M32) < (self.stamps.get((self.x >> 20, self.y >> 20), 0) & M32):
            if not self.setup(self.tx, self.ty, True):
                self.stuck = True
        if not self.arrived and self.progress() != ONE:
            bits = bin(self.hist & 0x7FFF).count('1')
            if (bits << 32) // 0xF0000 > 0x6665:
                if not self.setup(self.tx, self.ty, True):
                    self.stuck = True
        if self.pc == 0 or self.arrived:
            return -self.vx, -self.vy
        if self.idx == self.pc - 1:
            gx, gy = self.tx, self.ty
            if octile(gx - self.x, gy - self.y) < fmul(self.radius, 0x19999):
                self.arrived = True
        else:
            gx, gy = self.buf[self.idx]
            if octile(gx - self.x, gy - self.y) < fmul(self.radius, 0x20000):
                # the passed waypoint's slot, read whatever it holds (0x63d2f5): the last buffered one's is stale, and the set-up below
                # writes +0xa4 again
                self.ahead = s32(self.ahead - self.subdist[self.idx])
                self.idx += 1
                if self.idx == self.cnt:
                    self.setup(self.tx, self.ty, False)
            gx, gy = self.buf[min(self.idx, 4)]
        ox, oy = gx - self.x, gy - self.y
        if self.idx < self.pc - 1:
            return fmul(ox, ONE) - self.vx, fmul(oy, ONE) - self.vy
        size = length(ox, oy, self.single)
        if size == 0:
            return 0, 0
        desired = min(fdiv(size, 0x20000), self.max_speed)
        scale = fdiv(desired, size)
        return fmul(ox, scale) - self.vx, fmul(oy, scale) - self.vy

    def step(self):  # 0x50f3b0 / 0x63dd20 through 0x4fa2a0; returns 0 arrived, 1 walking, 2 stuck
        single = self.single
        wall = avoid_walls(self.m, dict(r=self.radius, mask=self.mask, x=self.x, y=self.y, vx=self.vx, vy=self.vy))
        path = self.follow()
        ax, ay = truncate(*wall, self.max_force, single)
        fx, fy = truncate(*path, self.max_force, single)
        tx = fmul(ax, 0xE666) + fmul(fx, 0x8000)
        ty = fmul(ay, 0xE666) + fmul(fy, 0x8000)
        tx, ty = truncate(tx, ty, self.max_force, single)
        tx, ty = fdiv(tx, ONE), fdiv(ty, ONE)
        self.vx, self.vy = truncate(s32(self.vx + tx), s32(self.vy + ty), self.max_speed, single)
        nx, ny = s32(self.x + self.vx), s32(self.y + self.vy)
        f = ((self.x & M32) >> 16, (self.y & M32) >> 16)
        t = ((nx & M32) >> 16, (ny & M32) >> 16)
        if commit(self.m, f if f[0] < 128 and f[1] < 128 else (-1, -1), t if t[0] < 128 and t[1] < 128 else None, self.mask):
            self.x, self.y = nx, ny
        else:
            self.hist = s32((self.hist << 1) | 1)
        # the local axes (0x63decf..0x63df02, tp.exe 0x50f57e / 0x50f5a1): the unit velocity and its perpendicular (-y, x)
        self.xaxis = normalize(self.vx, self.vy, single)
        self.yaxis = (s32(-self.xaxis[1]), self.xaxis[0])
        p = self.progress()
        self.hist = s32((self.hist << 1) | (1 if p <= self.prog else 0))
        self.prog = p
        return 2 if self.stuck else 0 if p == ONE else 1

    def record(self) -> bytes:
        """The 177 bytes the serializer's save half writes (0x63ad30 mode 1, tp.exe 0x50d150): the members in alphabetical order."""
        i = lambda *values: b''.join(struct.pack('<i', s32(v)) for v in values)
        out = i(*self.force, *self.formation, *self.xaxis, *self.yaxis, self.mass, self.max_force, self.max_speed, int(self.stuck), self.mask,
                self.cnt, self.idx) + bytes([1 if self.arrived else 0])
        out += i(self.prog, self.hist, self.ahead, self.beyond, self.tx, self.ty, self.stamp, self.pc, self.total, self.x, self.y, self.radius)
        for slot in range(5):
            out += i(*self.buf[slot], self.subdist[slot])
        out += i(self.vx, self.vy)
        assert len(out) == RECORD_BYTES
        return out

    def edit(self, cx, cy, kind):
        """A cell type change that stamps its block with the raised counter (0x5346d0 / 0x4d8c60)."""
        self.m.types[(cx, cy)] = kind
        self.counter += 1
        self.stamps[(cx >> 4, cy >> 4)] = self.counter

    def fields(self):
        cells = tuple(((bx >> 16) & 0xFF, (by >> 16) & 0xFF) for bx, by in self.buf[:self.cnt])
        return (self.x, self.y, self.vx, self.vy, self.tx, self.ty, self.pc, self.cnt, self.idx, int(self.arrived), self.total,
                self.ahead, self.beyond, self.prog, self.hist & 0x7FFF, int(self.stuck), cells)


RECORD_BYTES = 177
# (name, navigator offset, bytes) in the serializer's order (0x63ad30 / 0x50d150): the alphabetical member names, the five
# (subpath_buffer[p], subpath_dist[p]) pairs interleaved; the unnamed +0x30 is not written and the loader zeroes it
RECORD_LAYOUT = ([('force', 0x40, 8), ('formation_pos', 0x38, 8), ('local_xaxis', 0x20, 8), ('local_yaxis', 0x28, 8), ('mass', 0, 4),
                  ('max_force', 0x18, 4), ('max_speed', 0x1C, 4), ('mCantReachDest', 0xB8, 4), ('nav_mode', 0xB4, 4),
                  ('path_buffer_count', 0x58, 4), ('path_count', 0x5C, 4), ('path_finished', 0x60, 1), ('path_last_progress', 0xAC, 4),
                  ('path_stuck_buffer', 0xB0, 4), ('path_subpath_dist', 0xA4, 4), ('path_tail_dist', 0xA8, 4), ('path_target_pos', 0x4C, 8),
                  ('path_timestamp', 0x48, 4), ('path_total_count', 0x54, 4), ('path_total_dist', 0xA0, 4), ('pos', 8, 8), ('radius', 4, 4)]
                 + [entry for p in range(5) for entry in ((f'subpath_buffer[{p}]', 0x64 + 8 * p, 8), (f'subpath_dist[{p}]', 0x8C + 4 * p, 4))]
                 + [('vel', 0x10, 8)])


def record_of_object(read) -> bytes:
    """The record a navigator object's bytes give (`read(offset, size)`), in the serializer's order."""
    return b''.join(read(offset, size) for _, offset, size in RECORD_LAYOUT)


def object_of_record(record: bytes, old: bytes) -> bytes:
    """The serializer's load half (0x63b583..): the 0xbc-byte object `old` with every recorded member replaced and the unnamed +0x30 zeroed."""
    out, at = bytearray(old), 0
    for _, offset, size in RECORD_LAYOUT:
        out[offset:offset + size] = record[at:at + size]
        at += size
    out[0x30:0x38] = bytes(8)
    return bytes(out)


# ---- NV3: Theme Park Inc's walk step 0x60b460, speed step 0x60bc90, route requests 0x60b7f0 / 0x60b8e0 and staff route 0x62eb10

ATAN = [math.floor(math.atan(i / 256) * 1024 / math.pi) for i in range(257)]   # 0x91e1b4 (tp.exe 0x6e71b4): the same 257 words


def heading_angle(first, second):
    """0x91e074 (tp.exe 0x6e7074, HeadingAngle.Of): 2048 units to the turn; the smaller magnitude over the larger indexes the table."""
    if first == 0 and second == 0:
        return 0
    def entry(n, d):
        return ATAN[((n << 8) & M32) // d]
    if first >= 0:
        if second >= 0:
            a = 0x400 - entry(first, second) if first < second else 0x200 + entry(second, first)
        else:
            a = entry(first, -second) if first < -second else 0x200 - entry(-second, first)
    elif second >= 0:
        a = 0x400 + entry(-first, second) if -first < second else 0x600 - entry(second, -first)
    else:
        a = 0x800 - entry(-first, -second) if -first < -second else 0x600 + entry(-second, -first)
    return a & 0x7FF


def heading_word(x0, y0, x1, y1):
    """+0x2c as the walk step writes it: the angle of (x0 - x1, y1 - y0), masked to a word."""
    return heading_angle(s32(x0 - x1), s32(y1 - y0)) & 0xFFFF


def ftol_low(value):
    """_ftol 0x86fab0's low dword: truncation; an infinity or a NaN is the integer indefinite, whose low dword is 0."""
    if math.isnan(value) or math.isinf(value) or abs(value) >= 2.0 ** 63:
        return 0
    return s32(int(value))


SPEED_PER_UNIT = f32(3.829657e-05)   # 0x93e5b4


def step_interval(x0, y0, x1, y1, run):
    """0x60b5f8 and 0x5d10f0 at the control word 0x7f: the root of the 32-bit squared step length and its product with 0x93e5b4 rounded to
    singles, doubled without the run; 62.0 over it rounded to a single, _ftol, negative 0, above 0xfa 0xfa."""
    dx, dy = s32(x1 - x0), s32(y1 - y0)
    squared = s32(dx * dx + dy * dy)
    root = f32(math.sqrt(squared)) if squared >= 0 else math.nan
    speed = f32(root * SPEED_PER_UNIT)
    if not run:
        speed = f32(speed + speed)
    quotient = f32(62.0 / speed) if speed != 0 else (math.nan if math.isnan(speed) else math.inf)
    interval = ftol_low(quotient)
    return 0 if interval < 0 else min(interval, 0xFA)


WALK_PROGRAM, RUN_PROGRAM, CARRY_PROGRAM = 0x9AFBA8, 0x9AFC04, 0x9B07D4   # the sprite programs of the templates 1, 2 and 0x1f


def walk_step(walk, person, restarts):
    """0x60b460 on a person whose navigator is `walk` (the steering step is Walk.step): person holds the kind byte +2, the generation +0x2a4,
    the bonus word +0xd6 and the state +0x1b4; restarts(program) answers the restart test 0x4e4ab0. Returns (result, the chain's words, the
    heading word or None (left), the template queued or None, the interval stored or None); the stranded mark is cleared by every call."""
    x0, y0 = walk.x, walk.y
    result = walk.step()
    x1, y1 = walk.x, walk.y
    chain = ((x1 >> 8) & 0xFFFF, (y1 >> 8) & 0xFFFF)
    if result == 2:
        return 2, chain, None, None, None
    heading = heading_word(x0, y0, x1, y1)
    if result == 0:
        return 0, chain, heading, None, None
    kind = person['kind']
    run = (person['bonus'] & 0xFFFF) > 0 and (kind == 1 and person['generation'] == 0 or kind == 7)
    walk_program, walk_template = (CARRY_PROGRAM, 0x1F) if kind == 7 and person['state'] == 0x1C else (WALK_PROGRAM, 1)
    program, template = (RUN_PROGRAM, 2) if run else (walk_program, walk_template)
    queued = None if restarts(program) else template
    return 1, chain, heading, queued, step_interval(x0, y0, x1, y1, run)


def x87_single(value):
    """An exact rational rounded once to a 24-bit significand, nearest even (a product or a quotient at the control word 0x7f)."""
    from fractions import Fraction
    value = Fraction(value)
    if value == 0:
        return 0.0
    sign = -1 if value < 0 else 1
    value = abs(value)
    exponent = value.numerator.bit_length() - value.denominator.bit_length()
    scaled = value / Fraction(2) ** (exponent - 24)
    while scaled >= 1 << 24:
        exponent += 1
        scaled /= 2
    while scaled < 1 << 23:
        exponent -= 1
        scaled *= 2
    whole = scaled.numerator // scaled.denominator
    rest = scaled - whole
    if rest > Fraction(1, 2) or rest == Fraction(1, 2) and whole & 1:
        whole += 1
    return sign * math.ldexp(whole, exponent - 24)


def speed_caps(speed):
    """The speed setter 0x63ef90 (tp.exe 0x510190): min(s, 2.0); +0x18 = _ftol(s x 26214.4), +0x1c = _ftol(s x 13107.2), the products rounded
    to singles; each below 0x28f raised to 0x28f."""
    from fractions import Fraction
    s = 2.0 if speed > 2.0 else speed
    force = ftol_low(x87_single(Fraction(s) * Fraction(26214.4)))
    top = ftol_low(x87_single(Fraction(s) * Fraction(13107.2)))
    return max(top, 0x28F), max(force, 0x28F)


def speed_step(base, bonus, boost, previous):
    """0x60bc90: +0xdc = ((+0xd4 + +0xd6 + +0xd8) / 100 + +0xdc x 3.0) x 0.25, each operation rounded to a single (fidiv, fmul, faddp, fmul);
    the caps; +0xd8 = (u16)(boost x 99) / 100. Returns (speed, max speed, max force, boost)."""
    quotient = f32((base + bonus + boost) / 100)
    speed = f32(f32(quotient + f32(previous * 3.0)) * 0.25)
    top, force = speed_caps(speed)
    return speed, top, force, ((boost * 99) & 0xFFFF) // 100


NINE = [(-1, 1), (0, 1), (1, 1), (-1, 0), (0, 0), (1, 0), (-1, -1), (0, -1), (1, -1)]   # 0xaa0060, set by 0x60ba80


def stranded_holds(m, stamps, cell, mark, behind):
    """0x60bb50: from the person's cell (a queue cell's back through the links `behind`, word -> word, 0x5e0a50), the largest block stamp
    (0x5d8260) of the nine cells around, stopping once above the mark; 1 while it is below the mark."""
    word = (cell[1] << 7) + cell[0] + 1
    if m.t(*cell) in (3, 9):
        while behind.get(word, 0):
            word = behind[word]
    largest = 0
    for dx, dy in NINE:
        if largest > mark:
            break
        w = ((dy << 7) + word + dx) & 0xFFFF
        offset = (w - 1) & M32
        largest = max(largest, stamps.get((((offset >> 4) & 7), (offset >> 11)), 0) & M32)
    return largest < mark


class Person:
    """The walk words of a person: the cell bytes +5/+7, the target words +0x28/+0x2a, the stranded mark +0x1b0 and its navigator `walk`,
    whose counter is the stamp counter 0x5d81f0."""

    def __init__(self, walk, cell, mark, behind):
        self.walk, self.cell, self.mark, self.behind = walk, cell, mark, behind
        self.words = None

    def route_point(self, xw, yw):
        """0x60b8e0: the counter raised; below the mark it clears it; a mark still held returns 0 and changes nothing; else the words, the mark
        cleared and SetDest 0x63ee80 to the words << 8 (signed words)."""
        walk = self.walk
        walk.counter = (walk.counter + 1) & M32
        if walk.counter < self.mark:
            self.mark = 0
        if self.mark and stranded_holds(walk.m, walk.stamps, self.cell, self.mark, self.behind):
            return 0
        self.words = (xw & 0xFFFF, yw & 0xFFFF)
        self.mark = 0
        sw = lambda w: (w & 0xFFFF) - 0x10000 if w & 0x8000 else w & 0xFFFF
        return 1 if walk.setup(s32(sw(xw) << 8), s32(sw(yw) << 8), False) else 0

    def route_cell(self, word):
        """0x60b7f0: the words of the cell word's centre (sub-cell bytes 0x80), then 0x60b8e0's body."""
        w = (word - 1) & M32
        return self.route_point(((w & 0x7F) << 8) | 0x80, (((w >> 7) & 0xFF) << 8) | 0x80)

    def staff_route(self, xw, yw, closed):
        """0x62eb10: a closed park refuses the cells (0x2f, 0x11) and (0x30, 0x11) and returns 0; else the walk mask is 1 when the person's
        cell or the target's is empty (type 0), else 0; then 0x60b8e0."""
        if closed and (xw >> 8) & 0xFF in (0x2F, 0x30) and (yw >> 8) & 0xFF == 0x11:
            return 0
        m = self.walk.m
        self.walk.mask = 1 if m.t(*self.cell) == 0 or m.t((xw >> 8) & 0xFF, (yw >> 8) & 0xFF) == 0 else 0
        return self.route_point(xw, yw)
