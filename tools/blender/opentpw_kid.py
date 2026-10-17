"""A parametric low-poly kid in the style of Theme Park World's pre-rendered kid guests, for Blender.

Everything here is original art made from code; nothing is read or copied from the game's files. The proportions and
colours were matched by eye (and by reading mean colours) against the original standing sprites, which is why the
numbers below look arbitrary: see docs/CONTENT-KIT.md, "Swimmer sprites".

Run inside Blender (``bpy`` is imported at module level). The module provides
- ``PRESETS``: one preset per original kid sprite set (BE, BI, CH, FR, KI, SA, SU, TA),
- ``build_kid``: builds a posable rig for a preset,
- ``setup_scene``/``render_frame``: the calibrated orthographic camera, lighting and the supersampled render that
  produces a sprite-sized RGBA frame (outline included),
- ``standing_pose`` for the calibration comparison; the swim poses live in ``opentpw_swim_sprites.py``.

Units: the kid is about 3.4 units tall standing (head 1.4 wide). The rig origin is the middle of the torso, so a
standing kid has its feet 1.45 units below the root. Blender is Z-up; the kid looks along +Y.
"""
import math
import bpy
import mathutils
import numpy as np

# ---- Camera and lighting constants (calibrated against the original standing sprites, see CONTENT-KIT.md) ----------
# The two games' sprites were rendered with different cameras and sprite sizes; each edition has its own constants.
# ``elevation`` is the camera elevation above the ground plane in degrees, ``ppu`` the orthographic scale (sprite
# pixels per Blender unit), ``cell`` the original's sprite cell and ``feet`` where the standing hotspot lands in it;
# ``swim_canvas``/``swim_origin`` are the (larger) canvas and hotspot of our swimmer frames.
EDITIONS = {
	"TPW": dict(elevation=32.0, ppu=9.7, cell=(48, 56), feet=(24, 53), swim_canvas=(64, 48), swim_origin=(32, 27)),
	"TPI": dict(elevation=42.0, ppu=12.8, cell=(64, 80), feet=(32, 70), swim_canvas=(80, 64), swim_origin=(40, 36)),
}
SUPERSAMPLE = 6              # the frame is rendered this many times larger and box-filtered down
OUTLINE_DARKEN = 0.58        # colour factor of the silhouette's outermost pixels (the originals have dark edges)
SUN_ENERGY = 3.4
SUN_ROTATION_DEG = (-48.0, -22.0, 0.0)   # relative to the camera: from above and the upper left
WORLD_COLOUR = (0.78, 0.8, 0.9)
WORLD_STRENGTH = 0.55
WATER_TINT = (0.42, 0.78, 1.0)           # multiplied into submerged colours
WATER_BLUE = (0.08, 0.42, 0.85)          # mixed into submerged colours
WATER_MIX = 0.34
SUBMERGED_ALPHA = 0.8

# Body proportions (Blender units). The kid's head is about 40 % of the sprite's height; the adults are taller with
# longer limbs and a smaller head. ``th``/``tw``/``td`` are the torso's half height, half width and half depth.
BODIES = {
	"kid": dict(th=0.52, tw=0.62, td=0.46, thigh=0.30, shin=0.26, arm_up=0.38, arm_low=0.34, head=1.14, limb=1.0, stoop=0.0, size=1.0),
	"mum": dict(th=0.68, tw=0.60, td=0.44, thigh=0.56, shin=0.52, arm_up=0.52, arm_low=0.48, head=1.0, limb=0.9, stoop=0.0, size=1.2),
	"dad": dict(th=0.78, tw=0.86, td=0.74, thigh=0.56, shin=0.50, arm_up=0.52, arm_low=0.48, head=1.05, limb=1.15, stoop=0.0, size=1.3),
	"granny": dict(th=0.6, tw=0.68, td=0.52, thigh=0.46, shin=0.42, arm_up=0.44, arm_low=0.40, head=1.04, limb=1.0, stoop=10.0, size=1.1),
	"grandpa": dict(th=0.64, tw=0.62, td=0.46, thigh=0.50, shin=0.46, arm_up=0.52, arm_low=0.48, head=1.0, limb=1.0, stoop=22.0, size=1.15),
}


def srgb(r, g, b):
	"""sRGB 0-255 to the linear colour Blender wants."""
	def lin(c):
		c = c / 255.0
		return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
	return (lin(r), lin(g), lin(b), 1.0)


# One preset per original kid sprite set. Colours are sRGB, picked from the original's standing frames.
PRESETS = {
	# TPW kids
	"BE": dict(edition="TPW", girl=False, skin=(224, 160, 130), hair=(98, 54, 24), outfit=(28, 122, 12), hair_style="spiky", spikes=11, spike_length=0.4, iris=(40, 90, 215), ears=True),
	"BI": dict(edition="TPW", girl=False, skin=(226, 166, 138), hair=(232, 176, 46), outfit=(34, 44, 190), hair_style="spiky", spikes=12, spike_length=0.5, iris=(40, 90, 215), ears=True),
	"CH": dict(edition="TPW", girl=True, skin=(226, 164, 136), hair=(34, 26, 26), outfit=(158, 44, 50), hair_style="pigtails", bobble=(225, 20, 20), iris=(40, 90, 215), ears=False),
	"FR": dict(edition="TPW", girl=False, skin=(224, 160, 130), hair=(20, 16, 16), outfit=(236, 22, 16), hair_style="spiky", spikes=12, spike_length=0.46, iris=(40, 90, 215), ears=True),
	"KI": dict(edition="TPW", girl=True, skin=(226, 166, 138), hair=(196, 78, 26), outfit=(56, 152, 52), hair_style="pigtails", topknot=True, iris=(40, 90, 215), ears=False),
	"SA": dict(edition="TPW", girl=True, skin=(142, 94, 54), hair=(100, 66, 12), outfit=(36, 42, 104), hair_style="bob", iris=(70, 40, 20), ears=False),
	"SU": dict(edition="TPW", girl=True, skin=(230, 170, 140), hair=(246, 216, 92), outfit=(60, 102, 204), hair_style="pigtails", bobble=(225, 20, 20), iris=(40, 90, 215), ears=False),
	"TA": dict(edition="TPW", girl=False, skin=(148, 94, 66), hair=(44, 48, 46), outfit=(196, 18, 18), hair_style="curly", iris=(60, 35, 20), ears=True),
	# TPI kids
	"BD": dict(edition="TPI", girl=False, skin=(160, 104, 70), hair=(66, 66, 66), outfit=(196, 18, 18), hair_style="curly", iris=(60, 35, 20), ears=True),
	"GB": dict(edition="TPI", girl=True, skin=(226, 164, 136), hair=(30, 24, 24), outfit=(168, 44, 60), hair_style="pigtails", bobble=(225, 20, 20), iris=(40, 90, 215), ears=False),
	"GK": dict(edition="TPI", girl=True, skin=(226, 164, 136), hair=(222, 192, 108), outfit=(226, 110, 156), hair_style="cap", cap=(232, 118, 160), tail=True, iris=(40, 90, 215), ears=False),
	"KB": dict(edition="TPI", girl=False, skin=(226, 164, 136), hair=(92, 52, 26), outfit=(50, 100, 160), hair_style="cap", cap=(240, 192, 40), fringe=True, iris=(40, 90, 215), ears=True),
	# TPI parents and grandparents (no hats or camera: swim caps or bare heads)
	"FA": dict(edition="TPI", body="dad", girl=False, skin=(214, 158, 128), hair=(112, 88, 56), outfit=(228, 186, 44), hair_style="short", mustache=True, iris=(60, 80, 160), ears=True),
	"MO": dict(edition="TPI", body="mum", girl=True, skin=(224, 170, 140), hair=(214, 182, 120), outfit=(166, 42, 92), hair_style="bun", iris=(40, 90, 215), ears=False),
	"GM": dict(edition="TPI", body="granny", girl=True, skin=(226, 176, 150), hair=(176, 176, 186), outfit=(232, 124, 152), hair_style="perm", iris=(40, 90, 215), ears=False),
	"GR": dict(edition="TPI", body="grandpa", girl=False, skin=(214, 162, 136), hair=(228, 228, 230), outfit=(100, 64, 42), hair_style="balding", iris=(70, 90, 160), ears=True),
}
SET_NAMES = [n for n, q in PRESETS.items() if q['edition'] == 'TPW']   # the TPW kid sets (the default edition)


def sets_of(edition):
	return [n for n, q in PRESETS.items() if q['edition'] == edition]


class Kid:
	"""The rig: pivots to pose, plus the end points used to place splashes."""
	def __init__(self):
		self.objects = []
		self.pivots = {}
		self.ends = {}


def _principled(name, colour, underwater, rough=0.65):
	m = bpy.data.materials.new(name)
	m.use_nodes = True
	nt = m.node_tree
	b = nt.nodes["Principled BSDF"]
	b.inputs["Roughness"].default_value = rough
	for key in ("Specular IOR Level", "Specular"):
		if key in b.inputs:
			b.inputs[key].default_value = 0.25
	base = colour
	if underwater:
		m.surface_render_method = "DITHERED"
		geo = nt.nodes.new("ShaderNodeNewGeometry")
		sep = nt.nodes.new("ShaderNodeSeparateXYZ")
		under = nt.nodes.new("ShaderNodeMath")
		under.operation = "LESS_THAN"
		under.inputs[1].default_value = 0.0
		nt.links.new(geo.outputs["Position"], sep.inputs[0])
		nt.links.new(sep.outputs["Z"], under.inputs[0])
		mul = nt.nodes.new("ShaderNodeMix")
		mul.data_type = "RGBA"
		mul.blend_type = "MULTIPLY"
		mul.inputs[0].default_value = 1.0
		mul.inputs[6].default_value = base
		mul.inputs[7].default_value = (*WATER_TINT, 1.0)
		blue = nt.nodes.new("ShaderNodeMix")
		blue.data_type = "RGBA"
		blue.inputs[0].default_value = WATER_MIX
		nt.links.new(mul.outputs[2], blue.inputs[6])
		blue.inputs[7].default_value = (*WATER_BLUE, 1.0)
		final = nt.nodes.new("ShaderNodeMix")
		final.data_type = "RGBA"
		final.inputs[6].default_value = base
		nt.links.new(blue.outputs[2], final.inputs[7])
		nt.links.new(under.outputs[0], final.inputs[0])
		nt.links.new(final.outputs[2], b.inputs["Base Color"])
		alpha = nt.nodes.new("ShaderNodeMath")
		alpha.operation = "MULTIPLY_ADD"
		alpha.inputs[1].default_value = -(1.0 - SUBMERGED_ALPHA)
		alpha.inputs[2].default_value = 1.0
		nt.links.new(under.outputs[0], alpha.inputs[0])
		nt.links.new(alpha.outputs[0], b.inputs["Alpha"])
	else:
		b.inputs["Base Color"].default_value = base
	return m


def _emissive(name, colour, alpha):
	m = bpy.data.materials.new(name)
	m.use_nodes = True
	nt = m.node_tree
	for n in list(nt.nodes):
		nt.nodes.remove(n)
	out = nt.nodes.new("ShaderNodeOutputMaterial")
	em = nt.nodes.new("ShaderNodeEmission")
	em.inputs["Color"].default_value = colour
	em.inputs["Strength"].default_value = 1.0
	tr = nt.nodes.new("ShaderNodeBsdfTransparent")
	mix = nt.nodes.new("ShaderNodeMixShader")
	mix.inputs[0].default_value = alpha
	nt.links.new(tr.outputs[0], mix.inputs[1])
	nt.links.new(em.outputs[0], mix.inputs[2])
	nt.links.new(mix.outputs[0], out.inputs[0])
	m.surface_render_method = "DITHERED"
	return m


def _link(obj):
	for c in list(obj.users_collection):
		c.objects.unlink(obj)
	bpy.context.scene.collection.objects.link(obj)


def _pivot(kid, name, loc, parent=None):
	e = bpy.data.objects.new(name, None)
	bpy.context.scene.collection.objects.link(e)
	e.location = loc
	if parent is not None:
		e.parent = parent
	kid.objects.append(e)
	kid.pivots[name] = e
	return e


def _mesh(kid, kind, name, size, loc, material, parent, rot=(0, 0, 0), smooth=True, segments=14):
	if kind == "sphere":
		bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=max(6, segments // 2), radius=1)
	elif kind == "cone":
		bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=1, radius2=0, depth=2)
	else:
		bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=1, depth=2)
	o = bpy.context.active_object
	o.name = name
	o.scale = size
	o.location = loc
	o.rotation_euler = rot
	if smooth:
		bpy.ops.object.shade_smooth()
	o.data.materials.append(material)
	o.parent = parent
	kid.objects.append(o)
	return o


def _limb(kid, name, a, b, r1, r2, material, parent):
	"""A tapered limb segment from point a (radius r1) to b (radius r2), in the parent's space."""
	a, b = mathutils.Vector(a), mathutils.Vector(b)
	d = b - a
	bpy.ops.mesh.primitive_cone_add(vertices=10, radius1=r1, radius2=r2, depth=1)
	o = bpy.context.active_object
	o.name = name
	o.scale = (1, 1, d.length)
	o.location = (a + b) / 2
	o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
	bpy.ops.object.shade_smooth()
	o.data.materials.append(material)
	o.parent = parent
	kid.objects.append(o)
	return o


def build_kid(preset_name, underwater=True, tag=None):
	"""Builds the rig for one preset and returns a Kid. ``underwater`` adds the submerged tint to every material."""
	p = PRESETS[preset_name]
	b = BODIES[p.get("body", "kid")]
	tag = tag or preset_name
	kid = Kid()
	kid.size = b["size"]
	kid.stoop = b["stoop"]
	th, tw, td, limb = b["th"], b["tw"], b["td"], b["limb"]
	hip_z = -(th - 0.1)
	kid.torso_to_feet = -hip_z + b["thigh"] + b["shin"] + 0.14
	skin, hair = srgb(*p["skin"]), srgb(*p["hair"])
	outfit = srgb(*p["outfit"])
	mat = lambda n, c, r=0.65: _principled(f"{tag}_{n}", c, underwater, r)
	m_skin, m_hair, m_suit = mat("skin", skin, 0.6), mat("hair", hair, 0.55), mat("suit", outfit, 0.5)
	m_white = mat("white", srgb(248, 248, 248), 0.4)
	m_iris = mat("iris", srgb(*p["iris"]), 0.3)
	m_dark = mat("dark", srgb(20, 12, 12), 0.5)
	m_mouth = mat("mouth", srgb(150, 40, 40), 0.5)
	m_lip = mat("nose", tuple(min(1.0, c * 1.05) for c in skin[:3]) + (1.0,), 0.6)

	root = _pivot(kid, "root", (0, 0, 0))
	lay = _pivot(kid, "lay", (0, 0, 0), root)          # pitches the whole body from standing to lying
	roll = _pivot(kid, "roll", (0, 0, 0), lay)         # rolls about the body's long axis
	waist = hip_z + 0.2
	up = _pivot(kid, "upper", (0, 0, waist), roll)     # the upper body, which bends forward for a stooped standing pose
	uz = lambda z: z - waist                           # a z measured from the torso centre, relative to ``upper``
	# Torso: for boys skin with trunks, for girls a swimsuit in the outfit colour.
	torso_mat = m_suit if p["girl"] else m_skin
	_mesh(kid, "sphere", "torso", (tw, td, th), (0, 0, uz(0.0)), torso_mat, up)
	if p["girl"]:
		_mesh(kid, "sphere", "suitHips", (tw * 0.88, td * 0.93, 0.3), (0, 0, hip_z + 0.08), m_suit, roll)
	else:
		_mesh(kid, "sphere", "trunks", (tw * 1.03, td * 1.04, 0.3), (0, 0, uz(hip_z + 0.12)), m_suit, up)
	# Head: big, on the shoulders (no real neck).
	neck = _pivot(kid, "neck", (0, 0.0, uz(th - 0.12)), up)    # yaw: turning the head
	nod = _pivot(kid, "nod", (0, 0, 0), neck)                  # pitch: looking up and down
	head_centre = (0, 0.02, 0.5 + 0.1 * b["head"])
	head = _pivot(kid, "head", head_centre, nod)
	head.scale = (b["head"],) * 3
	_mesh(kid, "sphere", "skull", (0.72, 0.7, 0.67), (0, 0, 0), m_skin, head, segments=18)
	for s in (-1, 1):
		_mesh(kid, "sphere", f"eyeWhite{s}", (0.215, 0.14, 0.27), (0.27 * s, 0.6, 0.02), m_white, head)
		_mesh(kid, "sphere", f"iris{s}", (0.125, 0.09, 0.17), (0.28 * s, 0.7, 0.01), m_iris, head)
		_mesh(kid, "sphere", f"pupil{s}", (0.06, 0.05, 0.09), (0.28 * s, 0.77, 0.01), m_dark, head, segments=8)
		_mesh(kid, "sphere", f"brow{s}", (0.2, 0.08, 0.05), (0.27 * s, 0.57, 0.3), m_hair, head, rot=(0, math.radians(-14 * s), 0), segments=8)
		if p["ears"]:
			_mesh(kid, "sphere", f"ear{s}", (0.12, 0.17, 0.22), (0.7 * s, -0.02, -0.02), m_skin, head, segments=8)
	_mesh(kid, "sphere", "nose", (0.13, 0.15, 0.13), (0, 0.72, -0.2), m_lip, head, segments=8)
	_mesh(kid, "sphere", "mouth", (0.17, 0.07, 0.06), (0, 0.66, -0.4), m_mouth, head, segments=8)
	if p.get("mustache"):
		_mesh(kid, "sphere", "mustache", (0.3, 0.1, 0.07), (0, 0.66, -0.3), m_hair, head, segments=8)
	_build_hair(kid, p, head, m_hair, mat("bobble", srgb(*p.get("bobble", (225, 20, 20)))), mat("cap", srgb(*p.get("cap", (255, 255, 255))), 0.4))
	# Arms: shoulder (abduction, then swing), elbow, hand.
	au, al = b["arm_up"], b["arm_low"]
	for s in (-1, 1):
		sh = _pivot(kid, f"shoulder{s}", ((tw + 0.02) * s, 0, uz(th - 0.24)), up)
		sw = _pivot(kid, f"swing{s}", (0, 0, 0), sh)
		_mesh(kid, "sphere", f"shoulderBall{s}", (0.22 * limb,) * 3, (0, 0, 0), m_skin, sw)
		_limb(kid, f"upperArm{s}", (0, 0, 0), (0, 0, -au), 0.22 * limb, 0.19 * limb, m_skin, sw)
		el = _pivot(kid, f"elbow{s}", (0, 0, -au), sw)
		_limb(kid, f"foreArm{s}", (0, 0, 0), (0, 0, -al), 0.19 * limb, 0.17 * limb, m_skin, el)
		_mesh(kid, "sphere", f"hand{s}", (0.23 * limb, 0.22 * limb, 0.23 * limb), (0, 0, -al - 0.08), m_skin, el)
		kid.ends[f"hand{s}"] = _pivot(kid, f"handEnd{s}", (0, 0, -al - 0.12), el)
	# Legs: hip (abduction, swing), knee, ankle and a bare foot.
	t, sn = b["thigh"], b["shin"]
	for s in (-1, 1):
		hp = _pivot(kid, f"hip{s}", ((0.3 + 0.1 * (limb - 1)) * s * (tw / 0.62) ** 0.5, 0, hip_z), roll)
		sw = _pivot(kid, f"legSwing{s}", (0, 0, 0), hp)
		_limb(kid, f"thigh{s}", (0, 0, 0), (0, 0, -t), 0.28 * limb, 0.23 * limb, m_skin, sw)
		if not p["girl"]:
			_limb(kid, f"trunkLeg{s}", (0, 0, 0.1), (0, 0, -t * 0.85), 0.31 * limb, 0.3 * limb, m_suit, sw)
		kn = _pivot(kid, f"knee{s}", (0, 0, -t), sw)
		_limb(kid, f"shin{s}", (0, 0, 0), (0, 0, -sn), 0.23 * limb, 0.2 * limb, m_skin, kn)
		an = _pivot(kid, f"ankle{s}", (0, 0, -sn - 0.02), kn)
		_mesh(kid, "sphere", f"foot{s}", (0.26 * limb, 0.42 * limb, 0.18 * limb), (0, 0.12, -0.08), m_skin, an)
		kid.ends[f"foot{s}"] = _pivot(kid, f"footEnd{s}", (0, 0.05, -0.14), an)
	return kid


def _build_hair(kid, p, head, m_hair, m_bobble, m_cap):
	style = p["hair_style"]
	hx, hy, hz = 0.72, 0.7, 0.67
	if style in ("spiky", "pigtails", "curly"):
		_mesh(kid, "sphere", "hairCap", (0.77, 0.76, 0.62), (0, -0.1, 0.22), m_hair, head, segments=16)
	if style == "spiky":
		n = p["spikes"]
		length = p["spike_length"]
		golden = math.pi * (3 - math.sqrt(5))
		k = 0
		for i in range(n * 3):
			# Fibonacci points on the sphere, keep the upper and back part (leave the face clear).
			z = 1 - 2 * (i + 0.5) / (n * 3)
			r = math.sqrt(max(0.0, 1 - z * z))
			x, y = r * math.cos(golden * i), r * math.sin(golden * i)
			if z < 0.32 or (y > 0.35 and z < 0.75):
				continue
			d = mathutils.Vector((x, y * 0.9 - 0.1, z + 0.25)).normalized()
			base = mathutils.Vector((d.x * 0.7, d.y * 0.7 - 0.1, d.z * 0.62 + 0.22))
			tip = base + d * (length * (0.8 + 0.4 * ((i * 7) % 5) / 4))
			_limb(kid, f"spike{k}", base, tip, 0.2, 0.01, m_hair, head)
			k += 1
		# A swept fringe over the forehead.
		_limb(kid, "fringe0", (-0.2, 0.52, 0.48), (-0.55, 0.78, 0.62), 0.2, 0.02, m_hair, head)
		_limb(kid, "fringe1", (0.15, 0.55, 0.5), (0.42, 0.85, 0.6), 0.2, 0.02, m_hair, head)
	elif style == "pigtails":
		for s in (-1, 1):
			chain = [((0.68 * s, -0.04, 0.3), 0.3), ((0.95 * s, -0.05, 0.26), 0.28), ((1.12 * s, -0.05, 0.42), 0.22), ((1.18 * s, -0.05, 0.62), 0.13)]
			for j, (loc, r) in enumerate(chain):
				_mesh(kid, "sphere", f"tail{s}_{j}", (r, r, r * 1.15), loc, m_hair, head, segments=10)
			if p.get("bobble"):
				_mesh(kid, "sphere", f"bobble{s}", (0.14, 0.14, 0.14), (0.8 * s, -0.04, 0.27), m_bobble, head, segments=8)
		# Fringe and a little top curl so it does not look bald from the front.
		_mesh(kid, "sphere", "fringe", (0.55, 0.3, 0.2), (0, 0.52, 0.5), m_hair, head, segments=10)
		if p.get("topknot"):
			_limb(kid, "topknot", (0, -0.1, 0.8), (0.0, -0.15, 1.25), 0.3, 0.06, m_hair, head)
	elif style == "bob":
		_mesh(kid, "sphere", "bobCap", (0.84, 0.82, 0.74), (0, -0.1, 0.12), m_hair, head, segments=16)
		_mesh(kid, "sphere", "bobBack", (0.82, 0.5, 0.6), (0, -0.34, -0.22), m_hair, head, segments=12)
		for s in (-1, 1):
			_mesh(kid, "sphere", f"bobSide{s}", (0.2, 0.42, 0.5), (0.72 * s, -0.05, -0.12), m_hair, head, segments=10)
			_mesh(kid, "sphere", f"bobTail{s}", (0.16, 0.16, 0.2), (0.76 * s, -0.2, -0.62), m_hair, head, segments=8)
		_mesh(kid, "sphere", "fringe", (0.64, 0.3, 0.2), (0, 0.5, 0.42), m_hair, head, segments=10)
	elif style == "curly":
		golden = math.pi * (3 - math.sqrt(5))
		k = 0
		for i in range(70):
			z = 1 - 2 * (i + 0.5) / 70
			r = math.sqrt(max(0.0, 1 - z * z))
			x, y = r * math.cos(golden * i), r * math.sin(golden * i)
			if z < 0.18 and y > -0.1:
				continue
			if y > 0.55 and z < 0.7:
				continue
			pos = (x * 0.8, y * 0.8 - 0.08, z * 0.72 + 0.22)
			_mesh(kid, "sphere", f"curl{k}", (0.23, 0.23, 0.23), pos, m_hair, head, segments=8)
			k += 1
	elif style == "cap":
		# A swim cap hugging the head, with optional fringe and ponytail peeking out.
		_mesh(kid, "sphere", "swimCap", (0.78, 0.77, 0.66), (0, -0.08, 0.2), m_cap, head, segments=16)
		if p.get("fringe"):
			_mesh(kid, "sphere", "fringe", (0.45, 0.2, 0.12), (0, 0.6, 0.38), m_hair, head, segments=10)
			_limb(kid, "tuft", (0.1, 0.5, 0.5), (0.35, 0.8, 0.45), 0.14, 0.02, m_hair, head)
		if p.get("tail"):
			_mesh(kid, "sphere", "tail0", (0.24, 0.22, 0.26), (0, -0.78, 0.0), m_hair, head, segments=10)
			_mesh(kid, "sphere", "tail1", (0.2, 0.2, 0.3), (0, -0.82, -0.38), m_hair, head, segments=10)
	elif style == "short":
		_mesh(kid, "sphere", "hairCap", (0.75, 0.74, 0.6), (0, -0.1, 0.2), m_hair, head, segments=16)
	elif style == "bun":
		_mesh(kid, "sphere", "hairCap", (0.77, 0.76, 0.62), (0, -0.1, 0.22), m_hair, head, segments=16)
		_mesh(kid, "sphere", "bun", (0.38, 0.38, 0.34), (0, -0.28, 0.82), m_hair, head, segments=12)
		_mesh(kid, "sphere", "fringe", (0.55, 0.28, 0.18), (0, 0.5, 0.48), m_hair, head, segments=10)
	elif style == "perm":
		_mesh(kid, "sphere", "hairCap", (0.78, 0.76, 0.62), (0, -0.1, 0.22), m_hair, head, segments=16)
		for k, (x, y, z) in enumerate([(0.55, 0.0, 0.62), (-0.55, 0.0, 0.62), (0, -0.4, 0.8), (0.3, 0.3, 0.8), (-0.3, 0.3, 0.8), (0.74, -0.1, 0.2), (-0.74, -0.1, 0.2), (0.5, -0.5, 0.45), (-0.5, -0.5, 0.45), (0, 0.5, 0.74)]):
			_mesh(kid, "sphere", f"curl{k}", (0.27, 0.27, 0.27), (x, y, z), m_hair, head, segments=8)
	elif style == "balding":
		# White hair at the sides and back only, bare on top.
		for s in (-1, 1):
			_mesh(kid, "sphere", f"side{s}", (0.22, 0.42, 0.34), (0.68 * s, -0.12, 0.06), m_hair, head, segments=10)
			_mesh(kid, "sphere", f"tuft{s}", (0.2, 0.2, 0.2), (0.62 * s, -0.1, 0.52), m_hair, head, segments=8)
		_mesh(kid, "sphere", "back", (0.6, 0.3, 0.34), (0, -0.55, 0.0), m_hair, head, segments=10)
		_mesh(kid, "sphere", "brow", (0.5, 0.18, 0.08), (0, 0.55, 0.32), m_hair, head, segments=8)
	else:
		raise ValueError(style)


# ---- Posing ------------------------------------------------------------------------------------------------------

def apply_pose(kid, pose):
	"""Sets every pivot from a pose dict (angles in degrees); missing keys default to the neutral standing pose.

	Keys: ``root`` (x, y, z), ``pitch`` (0 standing, -90 lying face down), ``yaw``, ``roll``, ``head_yaw``,
	``head_pitch``; per side s in (-1, 1): ``arm`` {s: (swing, abduction)}, ``elbow`` {s: flex}, ``leg`` {s: (swing,
	abduction)}, ``knee`` {s: flex}, ``ankle`` {s: angle}."""
	P = kid.pivots
	r = math.radians
	P["root"].location = pose.get("root", (0, 0, 0))
	P["root"].rotation_euler = (0, 0, r(pose.get("yaw", 0)))
	P["lay"].rotation_euler = (r(pose.get("pitch", 0)), 0, 0)
	P["roll"].rotation_euler = (0, 0, r(pose.get("roll", 0)))
	P["upper"].rotation_euler = (r(-pose.get("stoop", 0)), 0, 0)
	P["neck"].rotation_euler = (0, 0, r(pose.get("head_yaw", 0)))
	P["nod"].rotation_euler = (r(pose.get("head_pitch", 0)), 0, 0)
	for s in (-1, 1):
		swing, abd = pose.get("arm", {}).get(s, (0, 12))
		P[f"shoulder{s}"].rotation_euler = (0, r(-s * abd), 0)
		P[f"swing{s}"].rotation_euler = (r(swing), 0, 0)
		P[f"elbow{s}"].rotation_euler = (r(pose.get("elbow", {}).get(s, 6)), 0, 0)
		swing, abd = pose.get("leg", {}).get(s, (0, 4))
		P[f"hip{s}"].rotation_euler = (0, r(-s * abd), 0)
		P[f"legSwing{s}"].rotation_euler = (r(swing), 0, 0)
		P[f"knee{s}"].rotation_euler = (r(-pose.get("knee", {}).get(s, 0)), 0, 0)
		P[f"ankle{s}"].rotation_euler = (r(pose.get("ankle", {}).get(s, 0)), 0, 0)
	bpy.context.view_layer.update()


def standing_pose(kid):
	return {"root": (0, 0, kid.torso_to_feet), "stoop": kid.stoop, "arm": {-1: (0, 24), 1: (0, 24)}, "leg": {-1: (0, 5), 1: (0, 5)}}


# ---- Scene, camera and rendering ---------------------------------------------------------------------------------

class Stage:
	pass


def setup_scene(canvas, origin, edition="TPW"):
	"""Configures render settings, lights and an orthographic camera. ``canvas`` is the sprite size in pixels and
	``origin`` the pixel (from the top left) where the world origin lands."""
	scene = bpy.context.scene
	for o in list(bpy.data.objects):   # the factory startup scene's cube, light and camera
		bpy.data.objects.remove(o, do_unlink=True)
	st = Stage()
	st.scene, st.canvas, st.origin = scene, canvas, origin
	st.elevation, st.ppu = EDITIONS[edition]["elevation"], EDITIONS[edition]["ppu"]
	for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
		try:
			scene.render.engine = engine
			break
		except TypeError:
			continue
	scene.render.film_transparent = True
	scene.render.image_settings.file_format = "PNG"
	scene.render.image_settings.color_mode = "RGBA"
	scene.render.image_settings.color_depth = "8"
	scene.render.filter_size = 0.5
	scene.view_settings.view_transform = "Standard"
	scene.view_settings.look = "None"
	scene.display_settings.display_device = "sRGB"
	try:
		scene.eevee.taa_render_samples = 24
		scene.eevee.use_shadows = True
	except Exception:
		pass
	scene.render.resolution_percentage = 100
	scene.render.resolution_x, scene.render.resolution_y = canvas[0] * SUPERSAMPLE, canvas[1] * SUPERSAMPLE
	scene.world = bpy.data.worlds.new("kidworld")
	scene.world.use_nodes = True
	bg = scene.world.node_tree.nodes["Background"]
	bg.inputs["Color"].default_value = (*WORLD_COLOUR, 1)
	bg.inputs["Strength"].default_value = WORLD_STRENGTH
	st.cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
	st.cam.data.type = "ORTHO"
	st.cam.data.ortho_scale = max(canvas) / st.ppu
	st.cam.data.clip_start, st.cam.data.clip_end = 0.1, 200
	m = max(canvas)
	st.cam.data.shift_x = (canvas[0] / 2 - origin[0]) / m
	st.cam.data.shift_y = (origin[1] - canvas[1] / 2) / m
	scene.collection.objects.link(st.cam)
	scene.camera = st.cam
	st.sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
	st.sun.data.energy = SUN_ENERGY
	st.sun.data.angle = math.radians(18)
	st.sun.parent = st.cam
	st.sun.rotation_euler = tuple(math.radians(a) for a in SUN_ROTATION_DEG)
	scene.collection.objects.link(st.sun)
	return st


def aim_camera(st, direction):
	"""Direction 0 looks at the kid's back (moving away), 2 sees it facing screen right, 4 facing the camera."""
	az = math.radians(-45 * direction)
	el = math.radians(st.elevation)
	dist = 40.0
	st.cam.location = (-dist * math.cos(el) * math.sin(az), -dist * math.cos(el) * math.cos(az), dist * math.sin(el))
	look = -st.cam.location
	st.cam.rotation_euler = look.to_track_quat("-Z", "Y").to_euler()
	bpy.context.view_layer.update()


def _read_png(path):
	img = bpy.data.images.load(path)
	img.colorspace_settings.name = "Non-Color"
	img.alpha_mode = "CHANNEL_PACKED"
	w, h = img.size
	a = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1]   # top-down
	bpy.data.images.remove(img)
	return a


def downsample(a, factor):
	h, w, _ = a.shape
	a = a.reshape(h // factor, factor, w // factor, factor, 4)
	alpha = a[..., 3]
	premul = a[..., :3] * alpha[..., None]
	cov = alpha.mean(axis=(1, 3))
	rgb = premul.sum(axis=(1, 3)) / np.maximum(alpha.sum(axis=(1, 3)), 1e-6)[..., None]
	return np.dstack([rgb, cov])


def outline(a, threshold=0.4, factor=OUTLINE_DARKEN):
	"""Darkens the silhouette's outermost pixels, which is what gives the original sprites their dark edges."""
	solid = a[..., 3] >= threshold
	pad = np.pad(solid, 1)
	inner = pad[1:-1, 1:-1] & pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:]
	edge = solid & ~inner
	out = a.copy()
	out[edge, :3] *= factor
	return out


def render_frame(st, path, binary_alpha=False):
	"""Renders the current scene state and returns a (h, w, 4) float array (straight alpha, sRGB), sprite sized."""
	st.scene.render.filepath = path
	bpy.ops.render.render(write_still=True)
	hi = _read_png(path)
	lo = downsample(hi, SUPERSAMPLE)
	lo = outline(lo)
	if binary_alpha:
		lo[..., 3] = (lo[..., 3] >= 0.4).astype(np.float32)
	else:
		lo[..., 3] = np.where(lo[..., 3] < 0.12, 0.0, lo[..., 3])
	lo[lo[..., 3] == 0, :3] = 0
	return lo


def write_png(a, path):
	"""Writes a (h, w, 4) float array (straight alpha, sRGB values) to a PNG file."""
	h, w, _ = a.shape
	img = bpy.data.images.new("out", w, h, alpha=True, float_buffer=False)
	img.colorspace_settings.name = "Non-Color"
	img.alpha_mode = "CHANNEL_PACKED"
	img.pixels = np.ascontiguousarray(a[::-1].astype(np.float32)).ravel().tolist()
	img.filepath_raw = path
	img.file_format = "PNG"
	img.save()
	bpy.data.images.remove(img)


def clear_kid(kid):
	for o in kid.objects:
		if o.name in bpy.data.objects:
			bpy.data.objects.remove(o, do_unlink=True)
