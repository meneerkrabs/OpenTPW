"""Python restatement of OpenTPW's port of tp.exe's walk (WalkEdges, WalkSearch, GuestNavigation.Navigate), the plan side of
the x86 walk targets (docs/reverse/ORACLE.md section 7, X25). Pure Python: no executable bytes, nothing imported from the game.

* `blocked` the edge test 0x4d8750 and `commit` the step commit 0x510510 over a synthetic cell map;
* `find` the route search 0x511420 (line search 0x511470 with its tracers 0x511c90, shortcuts 0x5110d0, refinement 0x5108a0);
* `avoid_walls` 0x50e4a0 with the ray test 0x50cc20;
* `Walk` the walk set-up 0x50f8e0 and the steering step 0x50f3b0 (follow_path 0x50ed10, progress 0x50fd40), separation without
  neighbours.
"""
D = [(0, -1), (1, 0), (0, 1), (-1, 0)]
FAIL = 0x70000000

class Map:
    def __init__(self, types=None, links=None, qlinks=None, flags=None):
        self.types = types or {}; self.links = links or {}; self.qlinks = qlinks or {}; self.flags = flags or {}
    def t(self, x, y): return self.types.get((x, y), 0)
    def l(self, x, y): return self.links.get((x, y), 0)
    def q(self, x, y): return self.qlinks.get((x, y), 0)
    def f(self, x, y): return self.flags.get((x, y), 0)

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
    if tt == 0x1e:
        if mask != 2: return 0
        go871 = True
    else:
        go871 = mask == 2
    if go871:
        if m.f(tx, ty) & 0x400: return 0
        # type 9 thing-list check: not modelled (no things)
    if tt in (5, 7, 2): return 1
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
    # objects (0x5363f0): none
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
def isqrt(v):
    import math
    return math.isqrt(v & M32)
def length(x, y):  # 0x50f600 / 0x50e4a0 length
    ax, ay = abs(x) & M32, abs(y) & M32
    bits = ax | ay
    if bits & 0xfffff000 == 0: return isqrt((ax * ax + ay * ay) & M32)
    if bits & 0xffc00000 == 0: return s32(isqrt((((ax >> 8) * (ax >> 8)) + ((ay >> 8) * (ay >> 8))) & M32) << 8)
    return s32(isqrt((((ax >> 18) * (ax >> 18)) + ((ay >> 18) * (ay >> 18))) & M32) << 18)

def ray(pos, d, corner, r):  # 0x50cc20
    mx = s32(pos[0] - corner[0]); my = s32(pos[1] - corner[1])
    a = s32(fmul(d[0], d[0]) + fmul(d[1], d[1]))
    b = s32((fmul(d[0], mx) + fmul(d[1], my)) * 2)
    c = s32(fmul(mx, mx) + fmul(my, my) - fmul(r, r))
    disc = s32(fmul(b, b) - s32(fmul(a, c) * 4))
    if disc <= 0: return -0x80000000
    s = isqrt(disc)
    t1 = s32(s - b); t2 = s32(-(s + b))
    return (t2 if t2 <= t1 else t1) >> 1

def avoid_walls(m, nav):
    """0x50e4a0: nav dict with r, x, y, vx, vy, mask"""
    r = nav['r']; mask = nav['mask']
    px = s32(nav['x'] + fmul(nav['vx'], 0x10000)); py = s32(nav['y'] + fmul(nav['vy'], 0x10000))
    cx = (nav['x'] & M32) >> 16; cy = (nav['y'] & M32) >> 16
    vx = s32(px - nav['x']); vy = s32(py - nav['y'])
    ln = length(vx, vy)
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
        t = ray((nav['x'], nav['y']), (dx, dy), pt, r)
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


def truncate(x, y, maximum):  # 0x50f600
    size = length(x, y)
    if size < maximum:
        return x, y
    scale = fdiv(maximum, size)
    return fmul(x, scale), fmul(y, scale)


def centre(cell):
    return (cell[0] << 16) + 0x8000, (cell[1] << 16) + 0x8000


def segment(a, b):
    return octile((b[0] - a[0]) << 16, (b[1] - a[1]) << 16)


class Walk:
    """The walk object's fields (walk +8 .. +0xb8) with block stamps kept by the caller's `stamps` dict."""

    def __init__(self, m, stamps, x, y, vx, vy, mask, max_speed, max_force):
        self.m, self.stamps = m, stamps
        self.x, self.y, self.vx, self.vy, self.mask = x, y, vx, vy, mask
        self.max_speed, self.max_force = max_speed, max_force
        self.tx = self.ty = 0
        self.pc = self.cnt = self.idx = 0
        self.arrived = True
        self.buf = [(0, 0)] * 5
        self.total = self.ahead = self.beyond = self.prog = self.hist = 0
        self.stuck = False
        self.stamp = 0
        self.counter = 0

    def setup(self, tx, ty, reroute):  # 0x50f8e0
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
            self.buf[i] = path[i]
        self.ahead = sum(segment(path[i], path[i + 1]) for i in range(self.cnt - 1))
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
        wx, wy = centre(self.buf[min(self.idx, 4)])
        remaining = octile(wx - self.x, wy - self.y) + self.ahead + self.beyond
        if self.total == 0:
            return ONE
        q = abs(remaining) * 65536 // self.total
        return s32(ONE - (q if remaining >= 0 else -q))

    def follow(self):  # 0x50ed10
        if self.stamp < self.stamps.get((self.x >> 20, self.y >> 20), 0):
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
            if octile(gx - self.x, gy - self.y) < fmul(RADIUS, 0x19999):
                self.arrived = True
        else:
            gx, gy = centre(self.buf[self.idx])
            if octile(gx - self.x, gy - self.y) < fmul(RADIUS, 0x20000):
                if self.idx + 1 < self.cnt:
                    self.ahead -= segment(self.buf[self.idx], self.buf[self.idx + 1])
                self.idx += 1
                if self.idx == self.cnt:
                    self.setup(self.tx, self.ty, False)
            gx, gy = centre(self.buf[min(self.idx, 4)])
        ox, oy = gx - self.x, gy - self.y
        if self.idx < self.pc - 1:
            return fmul(ox, ONE) - self.vx, fmul(oy, ONE) - self.vy
        size = length(ox, oy)
        if size == 0:
            return 0, 0
        desired = min(fdiv(size, 0x20000), self.max_speed)
        scale = fdiv(desired, size)
        return fmul(ox, scale) - self.vx, fmul(oy, scale) - self.vy

    def step(self):  # 0x50f3b0 through 0x4fa2a0; returns 0 arrived, 1 walking, 2 stuck
        wall = avoid_walls(self.m, dict(r=RADIUS, mask=self.mask, x=self.x, y=self.y, vx=self.vx, vy=self.vy))
        path = self.follow()
        ax, ay = truncate(*wall, self.max_force)
        fx, fy = truncate(*path, self.max_force)
        tx = fmul(ax, 0xE666) + fmul(fx, 0x8000)
        ty = fmul(ay, 0xE666) + fmul(fy, 0x8000)
        tx, ty = truncate(tx, ty, self.max_force)
        self.vx, self.vy = truncate(self.vx + tx, self.vy + ty, self.max_speed)
        nx, ny = s32(self.x + self.vx), s32(self.y + self.vy)
        f = ((self.x & M32) >> 16, (self.y & M32) >> 16)
        t = ((nx & M32) >> 16, (ny & M32) >> 16)
        if commit(self.m, f if f[0] < 128 and f[1] < 128 else (-1, -1), t if t[0] < 128 and t[1] < 128 else None, self.mask):
            self.x, self.y = nx, ny
        else:
            self.hist = ((self.hist << 1) | 1) & 0x7FFF
        p = self.progress()
        self.hist = ((self.hist << 1) | (1 if p <= self.prog else 0)) & 0x7FFF
        self.prog = p
        return 2 if self.stuck else 0 if p == ONE else 1

    def edit(self, cx, cy, kind):
        """A cell type change that stamps its block with the raised counter (0x5346d0 / 0x4d8c60)."""
        self.m.types[(cx, cy)] = kind
        self.counter += 1
        self.stamps[(cx >> 4, cy >> 4)] = self.counter

    def fields(self):
        return (self.x, self.y, self.vx, self.vy, self.tx, self.ty, self.pc, self.cnt, self.idx, int(self.arrived), self.total,
                self.ahead, self.beyond, self.prog, self.hist, int(self.stuck), tuple(self.buf[:self.cnt]))
