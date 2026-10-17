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
	"TPW": dict(elevation=32.0, ppu=9.5, cell=(48, 56), feet=(24, 53), swim_canvas=(64, 48), swim_origin=(32, 27)),
	"TPI": dict(elevation=42.0, ppu=12.8, cell=(64, 80), feet=(32, 70), swim_canvas=(80, 64), swim_origin=(40, 36)),
}
SUPERSAMPLE = 6              # the frame is rendered this many times larger and box-filtered down
SHARPEN = 0.2                # unsharp-mask weight after the downsample (the originals are crisp)
QUANTISE_COLOURS = 255       # the sprite banks' palette size (index 0 is transparent)
DITHER = 0.0                 # ordered-dither amplitude (fraction of full range) applied before palette mapping
OUTLINE_DARKEN = 0.7        # colour factor of the silhouette's outermost pixels (the originals have dark edges)
# Three suns parented to the camera: a warm key from above and the upper left, a cool fill from the right and a rim
# from behind and above (it only catches edges, which gives the bright rim of the originals).
LIGHT_SHADOWS = False
SUN_ENERGY = 6.0
SUN_ROTATION_DEG = (-48.0, -22.0, 0.0)
FILL_ENERGY = 0.5
FILL_ROTATION_DEG = (-20.0, 55.0, 0.0)
RIM_ENERGY = 2.0
RIM_ROTATION_DEG = (-28.0, 180.0 - 18.0, 0.0)
WORLD_COLOUR = (0.78, 0.82, 0.95)
WORLD_STRENGTH = 0.34
AO_FLOOR = 0.8              # how dark creases get
GRADE_SATURATION = 1.0      # post-process grade towards the originals' saturated palette
GRADE_CONTRAST = 1.2
SHADOW_TINT = (0.07, 0.0, 0.02)
WATER_TINT = (0.66, 0.9, 1.0)            # multiplied into submerged colours
WATER_BLUE = (0.16, 0.58, 0.95)          # mixed into submerged colours
WATER_MIX = 0.3
SUBMERGED_ALPHA = 0.74

# Body proportions (Blender units). The kid's head is about 40 % of the sprite's height; the adults are taller with
# longer limbs and a smaller head. ``th``/``tw``/``td`` are the torso's half height, half width and half depth.
WET_HAIR_DARKEN = 0.72
WET_HAIR_SCALE = 0.88    # wet hair sits closer to the head
SWIM_ARM_SCALE = 1.3
SWIM_LEG_SCALE = 1.12
BODIES = {
	"kid": dict(th=0.52, tw=0.62, td=0.46, thigh=0.30, shin=0.26, arm_up=0.38, arm_low=0.34, head=1.14, limb=1.0, stoop=0.0, size=1.0),
	"mum": dict(th=0.68, tw=0.60, td=0.44, thigh=0.56, shin=0.52, arm_up=0.52, arm_low=0.48, head=1.0, limb=0.9, stoop=0.0, size=1.2),
	"dad": dict(th=0.82, tw=0.86, td=0.74, belly=1, thigh=0.58, shin=0.53, arm_up=0.52, arm_low=0.48, head=1.05, limb=1.15, stoop=0.0, size=1.3),
	"granny": dict(th=0.6, tw=0.68, td=0.52, thigh=0.46, shin=0.42, arm_up=0.44, arm_low=0.40, head=1.04, limb=1.0, stoop=10.0, size=1.1),
	"grandpa": dict(th=0.66, tw=0.62, td=0.46, thigh=0.55, shin=0.5, arm_up=0.52, arm_low=0.48, head=1.0, limb=1.0, stoop=18.0, size=1.15),
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
	"BE": dict(edition="TPW", girl=False, skin=(236, 184, 158), hair=(98, 54, 24), outfit=(28, 122, 12), hair_style="spiky", spikes=11, spike_length=0.34, iris=(40, 90, 215), ears=True),
	"BI": dict(edition="TPW", girl=False, skin=(238, 188, 162), hair=(232, 176, 46), outfit=(34, 44, 190), hair_style="spiky", spikes=12, spike_length=0.4, iris=(40, 90, 215), ears=True),
	"CH": dict(edition="TPW", girl=True, skin=(238, 186, 160), hair=(34, 26, 26), outfit=(158, 44, 50), hair_style="pigtails", bobble=(225, 20, 20), iris=(40, 90, 215), ears=False),
	"FR": dict(edition="TPW", girl=False, skin=(236, 184, 158), hair=(20, 16, 16), outfit=(236, 22, 16), hair_style="spiky", spikes=12, spike_length=0.36, iris=(40, 90, 215), ears=True),
	"KI": dict(edition="TPW", girl=True, skin=(238, 188, 162), hair=(196, 78, 26), outfit=(56, 152, 52), hair_style="pigtails", topknot=True, iris=(40, 90, 215), ears=False),
	"SA": dict(edition="TPW", girl=True, skin=(142, 94, 54), hair=(100, 66, 12), outfit=(36, 42, 104), hair_style="bob", iris=(70, 40, 20), ears=False),
	"SU": dict(edition="TPW", girl=True, skin=(240, 190, 164), hair=(246, 216, 92), outfit=(60, 102, 204), hair_style="pigtails", bobble=(225, 20, 20), iris=(40, 90, 215), ears=False),
	"TA": dict(edition="TPW", girl=False, skin=(148, 94, 66), hair=(44, 48, 46), outfit=(196, 18, 18), hair_style="curly", iris=(60, 35, 20), ears=True),
	# TPI kids
	"BD": dict(edition="TPI", girl=False, skin=(160, 104, 70), hair=(66, 66, 66), outfit=(196, 18, 18), hair_style="curly", iris=(60, 35, 20), ears=True),
	"GB": dict(edition="TPI", girl=True, skin=(238, 186, 160), hair=(30, 24, 24), outfit=(168, 44, 60), hair_style="pigtails", bobble=(225, 20, 20), iris=(40, 90, 215), ears=False),
	"GK": dict(edition="TPI", girl=True, skin=(238, 186, 160), hair=(222, 192, 108), outfit=(226, 110, 156), hair_style="cap", cap=(232, 118, 160), cap_up=0.0, tail=True, iris=(40, 90, 215), ears=False),
	"KB": dict(edition="TPI", girl=False, skin=(238, 186, 160), hair=(92, 52, 26), outfit=(50, 100, 160), hair_style="cap", cap=(240, 192, 40), fringe=True, cap_up=0.18, iris=(40, 90, 215), ears=True),
	# TPI parents and grandparents (no hats or camera: swim caps or bare heads)
	"FA": dict(edition="TPI", body="dad", girl=False, skin=(228, 172, 144), hair=(112, 88, 56), outfit=(228, 186, 44), hair_style="short", mustache="hair", stubble=True, iris=(60, 80, 160), ears=True),
	"MO": dict(edition="TPI", body="mum", girl=True, skin=(236, 184, 158), hair=(214, 182, 120), outfit=(166, 42, 92), lips=(214, 28, 72), hair_style="bun", iris=(40, 90, 215), ears=False),
	"GM": dict(edition="TPI", body="granny", girl=True, skin=(238, 190, 166), hair=(176, 176, 186), outfit=(232, 124, 152), glasses=True, hair_style="perm", iris=(40, 90, 215), ears=False),
	"GR": dict(edition="TPI", body="grandpa", girl=False, skin=(228, 178, 152), hair=(228, 228, 230), outfit=(100, 64, 42), hair_style="balding", mustache="hair", iris=(70, 90, 160), ears=True),
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


def _noise(nt, tc, scale, detail=4.0, rough=0.55):
	n = nt.nodes.new("ShaderNodeTexNoise")
	n.inputs["Scale"].default_value = scale
	n.inputs["Detail"].default_value = detail
	n.inputs["Roughness"].default_value = rough
	nt.links.new(tc.outputs["Object"], n.inputs["Vector"])
	return n.outputs["Fac"]


def _ramp(nt, socket, lo, hi):
	r = nt.nodes.new("ShaderNodeValToRGB")
	r.color_ramp.elements[0].color = (lo, lo, lo, 1)
	r.color_ramp.elements[1].color = (hi, hi, hi, 1)
	nt.links.new(socket, r.inputs[0])
	return r.outputs[0]


def _mul(nt, a, b):
	m = nt.nodes.new("ShaderNodeMix")
	m.data_type = "RGBA"
	m.blend_type = "MULTIPLY"
	m.inputs[0].default_value = 1.0
	for idx, v in ((6, a), (7, b)):
		if isinstance(v, tuple):
			m.inputs[idx].default_value = v
		else:
			nt.links.new(v, m.inputs[idx])
	return m.outputs[2]


def _painted_albedo(nt, base, kind, texobj=None):
	"""The base colour times a painted variation: brush-stroke value variation, a warm/cool tint wander and, for hair
	and fabric, strands or folds. Returns a colour socket (or the constant when the kind has no pattern)."""
	if kind == "plain":
		return base
	tc = nt.nodes.new("ShaderNodeTexCoord")
	if texobj is not None:
		tc.object = texobj   # one shared texture space for the whole figure, so segments agree where they meet
	if kind == "hair":
		wave = nt.nodes.new("ShaderNodeTexWave")
		wave.wave_type = "BANDS"
		wave.bands_direction = "Z"
		wave.inputs["Scale"].default_value = 8.0
		wave.inputs["Distortion"].default_value = 7.0
		wave.inputs["Detail"].default_value = 3.0
		nt.links.new(tc.outputs["Object"], wave.inputs["Vector"])
		strokes = _ramp(nt, wave.outputs["Fac"], 0.5, 1.12)
		patch = _ramp(nt, _noise(nt, tc, 5.0, 3.0), 0.72, 1.0)
		var = _mul(nt, strokes, patch)
	elif kind == "fabric":
		wave = nt.nodes.new("ShaderNodeTexWave")
		wave.wave_type = "BANDS"
		wave.bands_direction = "Y"
		wave.inputs["Scale"].default_value = 5.0
		wave.inputs["Distortion"].default_value = 3.0
		wave.inputs["Detail"].default_value = 2.0
		nt.links.new(tc.outputs["Object"], wave.inputs["Vector"])
		folds = _ramp(nt, wave.outputs["Fac"], 0.7, 1.08)
		patch = _ramp(nt, _noise(nt, tc, 6.0, 4.0), 0.82, 1.05)
		var = _mul(nt, folds, patch)
	else:  # skin and other painted surfaces: dabs of slightly different value
		# Isotropic noise only: cell patterns (Voronoi) stretch into rings on thin limbs.
		dabs = _ramp(nt, _noise(nt, tc, 7.0, 3.0, 0.5), 0.93, 1.05)
		patch = _ramp(nt, _noise(nt, tc, 3.0, 2.0), 0.92, 1.03)
		var = _mul(nt, dabs, patch)
	out = _mul(nt, base, var)
	# A slow warm/cool wander of the hue, like paint mixed on the palette.
	wander = nt.nodes.new("ShaderNodeMix")
	wander.data_type = "RGBA"
	wander.inputs[0].default_value = 0.0
	nt.links.new(_ramp(nt, _noise(nt, tc, 2.5, 2.0), 0.0, 0.3), wander.inputs[0])
	nt.links.new(out, wander.inputs[6])
	wander.inputs[7].default_value = (1.0, 0.78, 0.62, 1.0) if kind in ("skin", "plain") else (0.85, 0.85, 1.0, 1.0)
	wmul = nt.nodes.new("ShaderNodeMix")
	wmul.data_type = "RGBA"
	wmul.blend_type = "MULTIPLY"
	wmul.inputs[0].default_value = 0.18
	nt.links.new(out, wmul.inputs[6])
	nt.links.new(wander.outputs[2], wmul.inputs[7])
	return wmul.outputs[2]


def _principled(name, colour, underwater, rough=0.65, kind="plain", spec=0.3, painted=True, texobj=None):
	"""A material. ``painted`` builds the pre-render look (a diffuse shading ramp with warm shadows, ambient occlusion
	and no specular, over a painted albedo); otherwise a plain glossy Principled BSDF (eyes)."""
	m = bpy.data.materials.new(name)
	m.use_nodes = True
	nt = m.node_tree
	out_node = nt.nodes["Material Output"]
	b = nt.nodes["Principled BSDF"]
	b.inputs["Roughness"].default_value = rough
	for key in ("Specular IOR Level", "Specular"):
		if key in b.inputs:
			b.inputs[key].default_value = spec
	base = colour
	albedo = _painted_albedo(nt, base, kind, texobj) if painted else base
	under = None
	if underwater:
		m.surface_render_method = "DITHERED"
		geo = nt.nodes.new("ShaderNodeNewGeometry")
		sep = nt.nodes.new("ShaderNodeSeparateXYZ")
		under = nt.nodes.new("ShaderNodeMath")
		under.operation = "LESS_THAN"
		under.inputs[1].default_value = 0.0
		nt.links.new(geo.outputs["Position"], sep.inputs[0])
		nt.links.new(sep.outputs["Z"], under.inputs[0])
		tinted = _mul(nt, albedo, (*WATER_TINT, 1.0))
		blue = nt.nodes.new("ShaderNodeMix")
		blue.data_type = "RGBA"
		blue.inputs[0].default_value = WATER_MIX
		nt.links.new(tinted, blue.inputs[6])
		blue.inputs[7].default_value = (*WATER_BLUE, 1.0)
		final = nt.nodes.new("ShaderNodeMix")
		final.data_type = "RGBA"
		if isinstance(albedo, tuple):
			final.inputs[6].default_value = albedo
		else:
			nt.links.new(albedo, final.inputs[6])
		nt.links.new(blue.outputs[2], final.inputs[7])
		nt.links.new(under.outputs[0], final.inputs[0])
		albedo = final.outputs[2]
	alpha = None
	if under is not None:
		alpha = nt.nodes.new("ShaderNodeMath")
		alpha.operation = "MULTIPLY_ADD"
		alpha.inputs[1].default_value = -(1.0 - SUBMERGED_ALPHA)
		alpha.inputs[2].default_value = 1.0
		nt.links.new(under.outputs[0], alpha.inputs[0])
	if not painted:
		if isinstance(albedo, tuple):
			b.inputs["Base Color"].default_value = albedo
		else:
			nt.links.new(albedo, b.inputs["Base Color"])
		if alpha is not None:
			nt.links.new(alpha.outputs[0], b.inputs["Alpha"])
		return m
	# Painted shading: a purely diffuse Principled BSDF (no specular) whose albedo is darkened in creases by ambient
	# occlusion; the warm shadow tint is applied in ``grade``.
	try:
		ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
		ao.inputs["Distance"].default_value = 0.45
		ao.samples = 8
		aomix = nt.nodes.new("ShaderNodeMix")
		aomix.data_type = "RGBA"
		aomix.inputs[6].default_value = (AO_FLOOR, AO_FLOOR * 0.92, AO_FLOOR * 0.9, 1)
		aomix.inputs[7].default_value = (1, 1, 1, 1)
		nt.links.new(ao.outputs["AO"], aomix.inputs[0])
		albedo = _mul(nt, albedo, aomix.outputs[2])
	except Exception:
		pass
	nt.links.new(albedo, b.inputs["Base Color"])
	if alpha is not None:
		nt.links.new(alpha.outputs[0], b.inputs["Alpha"])
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
	elif kind == "torus":
		bpy.ops.mesh.primitive_torus_add(major_segments=18, minor_segments=6, major_radius=1, minor_radius=0.16)
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
	"""A smooth tapered limb segment from point a (radius r1) to b (radius r2), in the parent's space: a capsule made
	from a UV sphere whose two halves are pulled apart (one smooth surface, so no stripes where cones would meet)."""
	a, b = mathutils.Vector(a), mathutils.Vector(b)
	d = b - a
	length = d.length
	bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=13, radius=1)
	o = bpy.context.active_object
	o.name = name
	dome = 0.5   # shallow end caps, so two segments meeting at a joint do not pinch into a chain of balls
	top = max(length - r2 * dome, r1 * dome + 0.001)
	for v in o.data.vertices:
		x, y, z = v.co
		if z > 0:
			v.co = (x * r2, y * r2, top + z * r2 * dome)
		else:
			v.co = (x * r1, y * r1, r1 * dome + z * r1 * dome)
	o.location = a
	o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
	bpy.ops.object.shade_smooth()
	# Analytic normals: the long band between the two caps otherwise shades as a ramp, which reads as rings on the limb.
	normals = []
	for v in o.data.vertices:
		x, y, z = v.co
		if z >= top:
			n = (x / (r2 * r2), y / (r2 * r2), (z - top) / (r2 * dome) ** 2)
		else:
			n = (x / (r1 * r1), y / (r1 * r1), (z - r1 * dome) / (r1 * dome) ** 2)
		normals.append(mathutils.Vector(n).normalized())
	try:
		o.data.normals_split_custom_set_from_vertices([tuple(n) for n in normals])
	except Exception:
		pass
	o.data.materials.append(material)
	o.parent = parent
	kid.objects.append(o)
	return o


def build_kid(preset_name, underwater=True, tag=None, swim=False):
	"""Builds the rig for one preset and returns a Kid. ``underwater`` adds the submerged tint to every material;
	``swim`` lengthens the limbs a little (a cartoon exaggeration so strokes read at sprite size)."""
	p = PRESETS[preset_name]
	b = dict(BODIES[p.get("body", "kid")])
	if swim:
		b["arm_up"] *= SWIM_ARM_SCALE
		b["arm_low"] *= SWIM_ARM_SCALE
		b["thigh"] *= SWIM_LEG_SCALE
		b["shin"] *= SWIM_LEG_SCALE
	tag = tag or preset_name
	kid = Kid()
	kid.size = b["size"]
	kid.stoop = b["stoop"]
	th, tw, td, limb = b["th"], b["tw"], b["td"], b["limb"]
	hip_z = -(th - 0.1)
	kid.torso_to_feet = -hip_z + b["thigh"] + b["shin"] + 0.14
	skin, hair = srgb(*p["skin"]), srgb(*p["hair"])
	if swim:   # wet hair is darker
		hair = tuple(c * WET_HAIR_DARKEN for c in hair[:3]) + (1.0,)
	outfit = srgb(*p["outfit"])
	root = _pivot(kid, "root", (0, 0, 0))
	lay = _pivot(kid, "lay", (0, 0, 0), root)          # pitches the whole body from standing to lying
	roll = _pivot(kid, "roll", (0, 0, 0), lay)         # rolls about the body's long axis
	mat = lambda n, c, r=0.9, kind="plain", spec=0.04: _principled(f"{tag}_{n}", c, underwater, r, kind, spec, texobj=roll)
	m_skin, m_hair, m_suit = mat("skin", skin, 0.9, "skin"), mat("hair", hair, 0.9, "hair"), mat("suit", outfit, 0.9, "fabric")
	gloss = lambda n, c: _principled(f"{tag}_{n}", c, underwater, 0.2, "plain", 0.8, painted=False)
	m_white, m_iris, m_dark = gloss("white", srgb(248, 248, 248)), gloss("iris", srgb(*p["iris"])), gloss("dark", srgb(20, 12, 12))
	rosy = tuple(a * 0.78 + b * 0.22 for a, b in zip(skin[:3], srgb(255, 90, 90)[:3])) + (1.0,)
	m_rosy = mat("rosy", rosy, 0.9, "skin", 0.0)
	m_mouth = mat("mouth", srgb(*p.get("lips", (200, 70, 85) if p["girl"] else (170, 60, 55))), 0.35, spec=0.6)
	m_brow = mat("brow", tuple(c * 0.55 for c in hair[:3]) + (1.0,), 0.4)
	m_blush = mat("blush", tuple(a * 0.62 + b * 0.38 for a, b in zip(skin[:3], srgb(255, 110, 110)[:3])) + (1.0,), 0.5)
	m_lip = mat("nose", tuple(min(1.0, c * 1.05) for c in skin[:3]) + (1.0,), 0.6)

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
		_mesh(kid, "sphere", f"brow{s}", (0.23, 0.09, 0.065), (0.27 * s, 0.58, 0.31), m_brow, head, rot=(0, math.radians(-14 * s), 0), segments=8)
		if p["ears"]:
			_mesh(kid, "sphere", f"ear{s}", (0.12, 0.17, 0.22), (0.7 * s, -0.02, -0.02), m_rosy, head, segments=8)
	_mesh(kid, "sphere", "nose", (0.13, 0.15, 0.13), (0, 0.72, -0.2), m_rosy, head, segments=8)
	if p.get("stubble"):
		m_stub = mat("stubble", tuple(a * 0.72 + b * 0.28 for a, b in zip(skin[:3], hair[:3])) + (1.0,), 0.7, "skin", 0.1)
		_mesh(kid, "sphere", "stubble", (0.735, 0.69, 0.36), (0, 0.0, -0.34), m_stub, head, segments=14)
	lip = p.get("lips")
	_mesh(kid, "sphere", "mouth", (0.2, 0.08, 0.07) if lip else (0.17, 0.07, 0.06), (0, 0.67, -0.4), m_mouth, head, segments=8)
	for s in (-1, 1):
		_mesh(kid, "sphere", f"blush{s}", (0.17, 0.07, 0.1), (0.46 * s, 0.5, -0.2), m_blush, head, rot=(0, math.radians(-30 * s), 0), segments=8)
	if p.get("mustache"):
		_mesh(kid, "sphere", "mustache", (0.32, 0.1, 0.08), (0, 0.67, -0.29), m_hair if p.get("mustache") == "hair" else m_brow, head, segments=8)
	if p.get("glasses"):
		for s in (-1, 1):
			_mesh(kid, "torus", f"glass{s}", (0.22, 0.22, 0.22), (0.27 * s, 0.76, 0.02), m_dark, head, rot=(math.radians(90), 0, 0), segments=8)
		_mesh(kid, "cylinder", "bridge", (0.03, 0.03, 0.07), (0, 0.78, 0.04), m_dark, head, rot=(0, math.radians(90), 0), segments=6)
	if b.get("belly"):
		_mesh(kid, "sphere", "belly", (tw * 0.82, td * 0.9, 0.5), (0, td * 0.5, uz(-0.12)), torso_mat, up)
	first = len(kid.objects)
	_build_hair(kid, p, head, m_hair, mat("bobble", srgb(*p.get("bobble", (225, 20, 20)))), mat("cap", srgb(*p.get("cap", (255, 255, 255))), 0.9))
	if swim:
		_wet_hair(kid.objects[first:])
	# Arms: shoulder (abduction, then swing), elbow, hand.
	au, al = b["arm_up"], b["arm_low"]
	for s in (-1, 1):
		sh = _pivot(kid, f"shoulder{s}", ((tw + 0.02) * s, 0, uz(th - 0.24)), up)
		sw = _pivot(kid, f"swing{s}", (0, 0, 0), sh)
		_mesh(kid, "sphere", f"shoulderBall{s}", (0.22 * limb,) * 3, (0, 0, 0), m_skin, sw)
		_limb(kid, f"upperArm{s}", (0, 0, 0), (0, 0, -au), 0.22 * limb, 0.19 * limb, m_skin, sw)
		el = _pivot(kid, f"elbow{s}", (0, 0, -au), sw)
		_mesh(kid, "sphere", f"elbowBall{s}", (0.19 * limb,) * 3, (0, 0, 0), m_skin, el)
		_limb(kid, f"foreArm{s}", (0, 0, 0), (0, 0, -al), 0.19 * limb, 0.17 * limb, m_skin, el)
		_mesh(kid, "sphere", f"hand{s}", (0.23 * limb, 0.22 * limb, 0.23 * limb), (0, 0, -al - 0.08), m_rosy, el)
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
		_mesh(kid, "sphere", f"kneeBall{s}", (0.23 * limb,) * 3, (0, 0, 0), m_skin, kn)
		_limb(kid, f"shin{s}", (0, 0, 0), (0, 0, -sn), 0.23 * limb, 0.2 * limb, m_skin, kn)
		an = _pivot(kid, f"ankle{s}", (0, 0, -sn - 0.02), kn)
		_mesh(kid, "sphere", f"foot{s}", (0.26 * limb, 0.42 * limb, 0.18 * limb), (0, 0.12, -0.08), m_rosy, an)
		kid.ends[f"foot{s}"] = _pivot(kid, f"footEnd{s}", (0, 0.05, -0.14), an)
	return kid


def _wet_hair(objs):
	"""Flattens hair for swimming: spikes and tufts shrink to stubs, curls and caps sit closer to the head, pigtails hang."""
	for o in objs:
		n = o.name
		if n.startswith(("spike", "fringe", "tuft", "topknot", "sideburn")) and o.type == "MESH" and n.startswith(("spike", "tuft", "topknot")):
			o.scale = o.scale * 0.4
		elif n.startswith("tail") and "_" in n:
			j = int(n.split("_")[1])
			o.location = (o.location.x * 0.9, o.location.y, o.location.z - 0.3 * j)
			o.scale = o.scale * 0.92
		elif n.startswith("bobble"):
			o.location = (o.location.x * 0.9, o.location.y, o.location.z - 0.1)
		elif n.startswith("curl"):
			o.location = o.location * 0.9
			o.scale = o.scale * 0.8
		elif n.startswith(("hairCap", "bobCap", "swimCap", "bobBack")):
			o.scale = o.scale * WET_HAIR_SCALE
			o.location = o.location * WET_HAIR_SCALE
		elif n.startswith("bun"):
			o.scale = o.scale * 0.7
			o.location = o.location * 0.85


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
			chain = [((0.68 * s, -0.04, 0.34), 0.3), ((0.96 * s, -0.05, 0.4), 0.28), ((1.1 * s, -0.05, 0.7), 0.23), ((1.1 * s, -0.05, 1.0), 0.14)]
			for j, (loc, r) in enumerate(chain):
				_mesh(kid, "sphere", f"tail{s}_{j}", (r, r, r * 1.15), loc, m_hair, head, segments=10)
			if p.get("bobble"):
				_mesh(kid, "sphere", f"bobble{s}", (0.14, 0.14, 0.14), (0.8 * s, -0.04, 0.3), m_bobble, head, segments=8)
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
			pos = (x * 0.78, y * 0.78 - 0.08, z * 0.64 + 0.2)
			_mesh(kid, "sphere", f"curl{k}", (0.22, 0.22, 0.22), pos, m_hair, head, segments=8)
			k += 1
	elif style == "cap":
		# A swim cap hugging the head, with optional fringe and ponytail peeking out.
		_mesh(kid, "sphere", "swimCap", (0.78, 0.77, 0.66 + p.get("cap_up", 0.16)), (0, -0.08, 0.2 + p.get("cap_up", 0.16) * 0.6), m_cap, head, segments=16)
		if p.get("fringe"):
			_mesh(kid, "sphere", "napeHair", (0.62, 0.35, 0.3), (0, -0.55, -0.22), m_hair, head, segments=10)
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
			_mesh(kid, "sphere", f"sideburn{s}", (0.14, 0.24, 0.3), (0.66 * s, 0.18, -0.26), m_hair, head, segments=8)
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
	for lname, energy, rot, colour, angle in (("sun", SUN_ENERGY, SUN_ROTATION_DEG, (1.0, 0.96, 0.88), 12),
			("fill", FILL_ENERGY, FILL_ROTATION_DEG, (0.75, 0.85, 1.0), 40), ("rim", RIM_ENERGY, RIM_ROTATION_DEG, (1.0, 1.0, 1.0), 8)):
		light = bpy.data.objects.new(lname, bpy.data.lights.new(lname, "SUN"))
		light.data.energy = energy
		light.data.color = colour
		light.data.angle = math.radians(angle)
		light.data.use_shadow = LIGHT_SHADOWS      # shadow-map banding showed as rings on thin limbs
		light.parent = st.cam
		light.rotation_euler = tuple(math.radians(a) for a in rot)
		scene.collection.objects.link(light)
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


def sharpen(a, amount=None):
	"""A mild unsharp mask on the colour (3 x 3 box blur subtracted), restoring pixel crispness after the downsample."""
	amount = SHARPEN if amount is None else amount
	if amount <= 0:
		return a
	rgb = a[..., :3]
	pad = np.pad(rgb, ((1, 1), (1, 1), (0, 0)), mode="edge")
	blur = sum(pad[dy:dy + rgb.shape[0], dx:dx + rgb.shape[1]] for dy in range(3) for dx in range(3)) / 9.0
	out = a.copy()
	out[..., :3] = np.clip(rgb + (rgb - blur) * amount * 2.0, 0.0, 1.0)
	return out


def grade(a):
	"""Saturation and contrast grade (about the mid grey), applied to the colour only."""
	rgb = a[..., :3]
	grey = (rgb * np.array([0.299, 0.587, 0.114], np.float32)).sum(-1, keepdims=True)
	rgb = grey + (rgb - grey) * GRADE_SATURATION
	rgb = 0.45 + (rgb - 0.45) * GRADE_CONTRAST
	shadow = np.clip(1.0 - grey * 1.7, 0.0, 1.0) ** 1.3          # shadows lean warm, like painted pre-renders
	rgb = rgb + shadow * np.array(SHADOW_TINT, np.float32)
	out = a.copy()
	out[..., :3] = np.clip(rgb, 0.0, 1.0)
	return out


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
	lo = sharpen(lo)
	lo = grade(lo)
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


# ---- Palette quantisation ----------------------------------------------------------------------------------------

_BAYER4 = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], np.float32) + 0.5) / 16.0 - 0.5


def _median_cut(colours, counts, target):
	"""Weighted median cut over RGBA like SpriteSetBuilder: repeatedly split the box with the most pixels along its widest
	channel at the weighted median; each entry is the rounded weighted mean of its box."""
	boxes = [np.arange(len(colours))]
	while len(boxes) < target:
		best, best_count = -1, -1
		for i, box in enumerate(boxes):
			if len(box) > 1:
				c = counts[box].sum()
				if c > best_count:
					best, best_count = i, c
		if best < 0:
			break
		box = boxes.pop(best)
		cols = colours[box].astype(np.int32)
		axis = int(np.argmax(cols.max(0) - cols.min(0)))
		order = box[np.argsort(colours[box][:, axis], kind="stable")]
		cum = np.cumsum(counts[order])
		cut = int(np.searchsorted(cum, cum[-1] / 2.0)) + 1
		cut = min(max(cut, 1), len(order) - 1)
		boxes += [order[:cut], order[cut:]]
	entries = []
	for box in boxes:
		w = counts[box].astype(np.float64)
		entries.append(np.rint((colours[box] * w[:, None]).sum(0) / w.sum()))
	return np.array(entries, np.uint8)


def quantise_set(frames, colours=None, dither=None):
	"""Quantises the RGBA frames of one sprite set to one shared palette of ``colours`` entries (fully transparent pixels
	stay transparent), the way the sprite banks store them. ``dither`` adds a 4 x 4 ordered dither to the colour before the
	nearest-entry lookup. Returns (frames, palette)."""
	colours = QUANTISE_COLOURS if colours is None else colours
	dither = DITHER if dither is None else dither
	px8 = [np.rint(f * 255).astype(np.uint8) for f in frames]
	opaque = np.concatenate([f[f[..., 3] > 0] for f in px8])
	keys = (opaque[:, 0].astype(np.uint32) << 24) | (opaque[:, 1].astype(np.uint32) << 16) | (opaque[:, 2].astype(np.uint32) << 8) | opaque[:, 3]
	uniq, counts = np.unique(keys, return_counts=True)
	uc = np.stack([(uniq >> 24) & 255, (uniq >> 16) & 255, (uniq >> 8) & 255, uniq & 255], axis=1).astype(np.uint8)
	palette = uc if len(uc) <= colours else _median_cut(uc, counts, colours)
	pal = palette.astype(np.float32)
	out = []
	for f in px8:
		h, w, _ = f.shape
		g = f.copy()
		ys, xs = np.nonzero(f[..., 3] > 0)
		src = f[ys, xs].astype(np.float32)
		if dither > 0:
			src[:, :3] += _BAYER4[ys % 4, xs % 4][:, None] * dither * 255.0
		best = np.empty(len(src), np.int64)
		for a0 in range(0, len(src), 8192):
			chunk = src[a0:a0 + 8192]
			d = ((chunk[:, None, :] - pal[None, :, :]) ** 2).sum(-1)
			best[a0:a0 + 8192] = d.argmin(1)
		g[ys, xs] = palette[best]
		out.append(g.astype(np.float32) / 255.0)
	return out, palette
