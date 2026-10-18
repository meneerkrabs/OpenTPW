"""Shared conventions for authoring Theme Park World objects in Blender.

Units: one Blender unit is one MD2 unit, and one park cell is 10 units (docs/MAP.md). Blender is Z-up and right-handed;
the glTF exporter writes Y-up (also right-handed) and the content kit's converter mirrors it into MD2's left-handed Y-up
frame: Blender (x, y, z) becomes MD2 (x, z, y). A footprint therefore covers the Blender X/Y range 0..10*width and
0..10*height, seen from above exactly as its Info.Shape text reads (first text row at the top, columns to the right).
Turn on backface culling in every material: the game culls back faces.

Custom properties understood by the OpenTPW importer (written to glTF `extras`):
- on an object (usually an empty): ``opentpw_head`` (int >= 1) marks a rider head record with that head id, the
  attachment point a ride script's ADDHEAD uses (docs/GUESTS.md, "Riders' heads");
- on a material: ``opentpw_wrap`` (bool, default True) wraps the texture in U and V (MD2 slot flag 0x10), else clamp;
  ``opentpw_self_lit`` (bool, default False) draws the mesh in one constant colour instead of per-vertex lighting
  (MD2 material word +0xC bit 0x1, used by the original for water, signs and glow);
- on a point light object: ``opentpw_light`` (int 0..32767, optional) is the light number a script's ENABLELIGHT,
  DISABLELIGHT, SETLIGHT and COLOURLIGHT address (without it the lights take the lowest free numbers in node order) and
  ``opentpw_light_radius`` (int 1..65535, optional) the radius in model units (default 100); the light's colour is its
  base colour and its intensity is not carried (docs/LIGHTS.md). Spot and sun lights are skipped.
"""

CELL_UNITS = 10.0
MAX_TEXTURE_NAME = 19  # MD2 texture names are 20-byte NUL-terminated fields
HEAD_PROPERTY = "opentpw_head"
WRAP_PROPERTY = "opentpw_wrap"
SELF_LIT_PROPERTY = "opentpw_self_lit"
LIGHT_PROPERTY = "opentpw_light"
LIGHT_RADIUS_PROPERTY = "opentpw_light_radius"


def validate_scene(objects):
	"""Returns a list of problems that would make the OpenTPW import fail or behave unexpectedly."""
	problems = []
	heads = {}
	lights = {}
	for obj in objects:
		if obj.type == "LIGHT" and obj.data.type == "POINT":
			if LIGHT_PROPERTY in obj.keys():
				number = obj[LIGHT_PROPERTY]
				if not isinstance(number, int) or number < 0 or number > 32767:
					problems.append(f"{obj.name}: {LIGHT_PROPERTY} must be an integer from 0 to 32767")
				elif number in lights:
					problems.append(f"{obj.name}: light number {number} is also used by {lights[number]}")
				else:
					lights[number] = obj.name
			if LIGHT_RADIUS_PROPERTY in obj.keys():
				radius = obj[LIGHT_RADIUS_PROPERTY]
				if not isinstance(radius, int) or radius < 1 or radius > 65535:
					problems.append(f"{obj.name}: {LIGHT_RADIUS_PROPERTY} must be an integer from 1 to 65535 (model units)")
		if HEAD_PROPERTY in obj.keys():
			head = obj[HEAD_PROPERTY]
			if not isinstance(head, int) or head < 1:
				problems.append(f"{obj.name}: {HEAD_PROPERTY} must be an integer >= 1")
			elif head in heads:
				problems.append(f"{obj.name}: head id {head} is also used by {heads[head]}")
			else:
				heads[head] = obj.name
		if obj.type != "MESH":
			continue
		for slot in obj.material_slots:
			material = slot.material
			if material is None:
				continue
			for node in (material.node_tree.nodes if material.use_nodes else []):
				if node.type == "TEX_IMAGE" and node.image is not None:
					stem = node.image.name.rsplit(".", 1)[0]
					if len(stem) + 4 > MAX_TEXTURE_NAME:
						problems.append(f"{material.name}: texture name '{stem}.tga' is longer than {MAX_TEXTURE_NAME} bytes")
	if heads and sorted(heads) != list(range(1, len(heads) + 1)):
		problems.append(f"head ids must run from 1 without gaps (the script init counts ids from 1 until one is missing): {sorted(heads)}")
	return problems
