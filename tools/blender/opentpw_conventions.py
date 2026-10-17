"""Shared conventions for authoring Theme Park World objects in Blender.

Units: one Blender unit is one MD2 unit, and one park cell is 10 units (docs/MAP.md). Blender is Z-up; the glTF
exporter writes Y-up, which is the MD2 axis convention, so no extra conversion is applied here. A model's text row
(the front of its footprint) faces model +Z, which is Blender -Y.

Custom properties understood by the OpenTPW importer (written to glTF `extras`):
- on an object (usually an empty): ``opentpw_head`` (int >= 1) marks a rider head record with that head id, the
  attachment point a ride script's ADDHEAD uses (docs/GUESTS.md, "Riders' heads");
- on a material: ``opentpw_wrap`` (bool, default True) wraps the texture in U and V (MD2 slot flag 0x10), else clamp;
  ``opentpw_self_lit`` (bool, default False) draws the mesh in one constant colour instead of per-vertex lighting
  (MD2 material word +0xC bit 0x1, used by the original for water, signs and glow).
"""

CELL_UNITS = 10.0
MAX_TEXTURE_NAME = 19  # MD2 texture names are 20-byte NUL-terminated fields
HEAD_PROPERTY = "opentpw_head"
WRAP_PROPERTY = "opentpw_wrap"
SELF_LIT_PROPERTY = "opentpw_self_lit"


def validate_scene(objects):
	"""Returns a list of problems that would make the OpenTPW import fail or behave unexpectedly."""
	problems = []
	heads = {}
	for obj in objects:
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
