"""Pose generators and props for the swimwear animation slots of a swimming-pool mod (slots 3 to 7 and 11 to 14 of the swim sets), for the
rig of ``opentpw_kid.py``. Everything is original art made from code.

The slot table of a swim set (docs/CONTENT-KIT.md, "Swimwear slots"):

  slot  name        frames  hotspot            outfit / water
  0     stand       1       feet on the deck   swimwear, dry
  1     walk        8       feet on the deck   swimwear, dry
  3     ladder      4       torso centre       dry: climbing a pool ladder, facing it
  4     dive        4       torso centre       dry: crouch, leap, arc, head-first entry
  5     lying       2       torso centre       dry: on the back on a lounger, feet towards the heading
  6     shower      4       feet on the deck   dry, with falling drops: hands in the hair
  7     slide       2       torso centre       dry: lying back, feet first, going down a slide
  8-10  swim        8 8 4   torso at the water line (existing: crawl, breaststroke, treading water)
  11    splash      4       torso at the water line: landing in the water, a crown of drops and growing rings
  12    spa-sit     2       torso at the water line: sitting in the water, knees up, arms on the rim
  13    ring        4       torso at the water line: sitting in a floating ring, paddling
  14    rope        4       torso centre       dry: swinging on a rope, hands overhead
  2, 15 are left empty (run is unused, 15 is spare).

A pose is the dict ``opentpw_kid.apply_pose`` takes. ``pitch`` rotates the body about the sideways axis: 0 standing, -90 lying face
down (head towards the heading), +90 lying on the back (head away from the heading), -180 upside down. Arm and leg ``swing`` is 0
hanging, 90 forward and 180 up/over the head, in the body's own frame, so a lying body keeps them relative to itself.
"""
import math

import bpy
import bmesh

import opentpw_guest_anims as A

# (slot, name, frames per direction, outfit/water kind, hotspot kind), in slot order
POOL_SLOTS = [
	(0, "stand", 1, "dry", "feet"), (1, "walk", 8, "dry", "feet"), (3, "ladder", 4, "dry", "torso"), (4, "dive", 4, "dry", "torso"),
	(5, "lying", 2, "dry", "torso"), (6, "shower", 4, "dry", "feet"), (7, "slide", 2, "dry", "torso"),
	(8, "swim-crawl", 8, "wet", "water"), (9, "swim-breaststroke", 8, "wet", "water"), (10, "swim-tread", 4, "wet", "water"),
	(11, "splash", 4, "wet", "water"), (12, "spa-sit", 2, "wet", "water"), (13, "ring", 4, "wet", "water"), (14, "rope", 4, "dry", "torso"),
]
TAU = 2 * math.pi


def pose(kid, root=(0, 0, 0), arms=((0, 14, 6), (0, 14, 6)), legs=((0, 5, 0), (0, 5, 0)), ankle=None, **kw):
	"""A pose from per-side tuples (arms (swing, abduction, elbow), legs (swing, abduction, knee); index 0 is side -1) and the body keys."""
	p = {"root": root, "arm": {}, "elbow": {}, "leg": {}, "knee": {}, "ankle": {}}
	p.update(kw)
	for i, s in enumerate((-1, 1)):
		p["arm"][s] = (arms[i][0], arms[i][1])
		p["elbow"][s] = arms[i][2]
		p["leg"][s] = (legs[i][0], legs[i][1])
		p["knee"][s] = legs[i][2]
		p["ankle"][s] = 0.0 if ankle is None else ankle
	return p


def ladder(kid, f, n=4):
	"""Climbing a ladder, seen climbing away from the camera at direction 0: the hands reach for rungs, the knees come up alternately."""
	ph = f / n
	arms, legs = [], []
	for s in (-1, 1):
		th = TAU * (ph + (0.0 if s < 0 else 0.5))
		arms.append((128 + 34 * math.sin(th), 14, 22 + 30 * max(0.0, -math.sin(th))))
		legs.append((22 + 36 * max(0.0, math.sin(th + 1.0)), 6, 20 + 62 * max(0.0, math.sin(th + 1.0))))
	return pose(kid, root=(0, 0.1, 0.05 * math.sin(TAU * 2 * ph)), arms=tuple(arms), legs=tuple(legs), stoop=-6, head_pitch=10, pitch=-8)


def dive(kid, f, n=4):
	"""A head-first dive: crouch with the arms back, leap with the arms overhead, arc over, and the straight entry."""
	if f == 0:
		return pose(kid, root=(0, 0, -0.35), arms=((-52, 20, 10), (-52, 20, 10)), legs=((34, 8, 84), (34, 8, 84)), stoop=34, head_pitch=14)
	if f == 1:
		return pose(kid, root=(0, 0, 0.1), arms=((168, 18, 6), (168, 18, 6)), legs=((-14, 6, 40), (-14, 6, 40)), pitch=-34, head_pitch=14)
	if f == 2:
		return pose(kid, root=(0, 0, 0), arms=((176, 6, 0), (176, 6, 0)), legs=((-6, 3, 10), (-6, 3, 10)), pitch=-112, head_pitch=6)
	return pose(kid, root=(0, 0, 0), arms=((178, 4, 0), (178, 4, 0)), legs=((0, 3, 2), (0, 3, 2)), pitch=-170, head_pitch=0)


def lying(kid, f, n=2):
	"""Lying back on a lounger with the head away from the heading: the arms at the sides, then the hands behind the head."""
	if f == 0:
		return pose(kid, arms=((4, 22, 8), (4, 22, 8)), legs=((0, 8, 6), (0, 8, 4)), pitch=84, head_pitch=-8)
	return pose(kid, arms=((150, 48, 104), (150, 48, 104)), legs=((0, 8, 6), (6, 10, 52)), pitch=84, head_pitch=-14, head_yaw=10)


def shower(kid, f, n=4):
	"""Standing under a shower, washing the hair: the hands go over the head one after the other, the head tips up into the water."""
	ph = f / n
	arms = []
	for s in (-1, 1):
		th = TAU * (ph + (0.0 if s < 0 else 0.5))
		arms.append((150 + 16 * math.sin(th), 34, 104 + 18 * math.cos(th)))
	p = pose(kid, arms=tuple(arms), legs=((0, 6, 2), (0, 7, 2)), head_pitch=18 + 4 * math.sin(TAU * ph), roll=3 * math.sin(TAU * ph))
	p["root"] = (0, 0, A.grounded(kid, p))
	return p


def slide(kid, f, n=2):
	"""Going down a slide on the back, feet first: the arms up for the thrill, then crossed over the chest."""
	if f == 0:
		return pose(kid, arms=((158, 40, 12), (158, 40, 12)), legs=((8, 6, 8), (8, 6, 8)), pitch=62, head_pitch=-12)
	return pose(kid, arms=((62, 8, 118), (62, 8, 118)), legs=((14, 4, 10), (22, 4, 6)), pitch=66, head_pitch=-10, head_yaw=-8)


def rope(kid, f, n=4):
	"""Swinging on a rope, both hands overhead: back with the legs forward, down through the middle, forward with the legs trailing."""
	lean = (24, 0, -24, 0)[f % 4]
	legs = {0: (46, 20), 1: (14, 6), 2: (-24, 28), 3: (14, 6)}[f % 4]
	return pose(kid, root=(0, 0, 0), arms=((176, 10, 8), (176, 10, 8)), legs=((legs[0], 6, legs[1]), (legs[0] - 6, 7, legs[1] + 6)), pitch=lean, head_pitch=-lean * 0.3)


def splash(kid, f, n=4):
	"""Landing feet first: the body sinks (the water line climbs from the shins to the shoulders), the arms thrown up."""
	z = (1.15, 0.62, 0.22, -0.02)[f % 4]
	up = (128, 150, 112, 70)[f % 4]
	return pose(kid, root=(0, 0, z), arms=((up, 30, 18), (up, 30, 18)), legs=((0, 5, 4), (0, 6, 4)), head_pitch=8)


def spa_sit(kid, f, n=2):
	"""Sitting in the water with the knees up and an arm along the rim; the other arm waves on the second frame."""
	wave = 0 if f == 0 else 1
	return pose(kid, root=(0, 0, 0.16), arms=((52, 62, 40), (122 + 22 * wave, 54, 96 - 30 * wave)), legs=((84, 8, 82), (80, 10, 86)), ankle=-10, head_yaw=-10 + 20 * wave, stoop=-4)


def ring_float(kid, f, n=4):
	"""Sitting in a floating ring with the legs through it, paddling with the hands and bobbing a little."""
	ph = f / n
	arms = []
	for s in (-1, 1):
		th = TAU * (ph + (0.0 if s < 0 else 0.5))
		arms.append((46 + 22 * math.sin(th), 54, 52 + 20 * math.cos(th)))
	return pose(kid, root=(0, 0, 0.06 + 0.04 * math.sin(TAU * ph)), arms=tuple(arms), legs=((64, 10, 56 + 14 * math.sin(TAU * ph)), (58, 12, 54 - 14 * math.sin(TAU * ph))), head_yaw=8 * math.sin(TAU * ph), roll=3 * math.sin(TAU * ph))


POSES = {"ladder": ladder, "dive": dive, "lying": lying, "shower": shower, "slide": slide, "rope": rope, "splash": splash, "spa-sit": spa_sit, "ring": ring_float}


# ---- Props -------------------------------------------------------------------------------------------------------

def make_torus(name, material, major, minor, major_segments=36, minor_segments=10, location=(0, 0, 0)):
	"""An inflatable ring lying flat (axis Z) at ``location``."""
	bm = bmesh.new()
	rings = []
	for i in range(major_segments):
		a = TAU * i / major_segments
		ring = []
		for j in range(minor_segments):
			b = TAU * j / minor_segments
			r = major + minor * math.cos(b)
			ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), minor * math.sin(b))))
		rings.append(ring)
	for i in range(major_segments):
		for j in range(minor_segments):
			bm.faces.new((rings[i][j], rings[(i + 1) % major_segments][j], rings[(i + 1) % major_segments][(j + 1) % minor_segments], rings[i][(j + 1) % minor_segments]))
	me = bpy.data.meshes.new(name)
	bm.to_mesh(me)
	bm.free()
	for p in me.polygons:
		p.use_smooth = True
	o = bpy.data.objects.new(name, me)
	me.materials.append(material)
	bpy.context.scene.collection.objects.link(o)
	o.location = location
	return o


def spawn_drops(points, radius, material):
	"""Droplets (ico spheres) at the given (x, y, z) points; returns the objects to remove after the render."""
	out = []
	for i, c in enumerate(points):
		bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=radius * (1.0 - 0.18 * (i % 3)), location=c)
		o = bpy.context.active_object
		o.data.materials.append(material)
		out.append(o)
	return out


def splash_drops(f, size, around=(0.0, 0.0, 0.0)):
	"""A crown of drops thrown up and out where the body enters the water (frames 0 to 3 grow and fall)."""
	spread = (0.5, 0.95, 1.3, 1.45)[f % 4] * size
	height = (0.35, 0.95, 0.85, 0.4)[f % 4] * size
	pts = []
	for k in range(12):
		a = TAU * k / 12 + 0.3 * f
		r = spread * (0.75 + 0.25 * ((k * 5) % 3) / 2)
		pts.append((around[0] + r * math.cos(a), around[1] + r * math.sin(a) * 0.8, around[2] + 0.05 + height * (0.45 + 0.55 * ((k * 7) % 4) / 3)))
	return pts


def shower_drops(f, size):
	"""Beads falling round the kid from the shower head above, standing on the floor (z 0); they move down the frame by frame."""
	pts = []
	for k in range(16):
		a = TAU * k / 16 + 0.4
		r = (0.85 + 0.5 * ((k * 3) % 4) / 3) * size
		z = 3.9 * size * (1.0 - ((k * 5 + f * 4) % 16) / 16.0)
		pts.append((r * math.cos(a) * 0.9, r * math.sin(a) * 0.6, z))
	return pts
