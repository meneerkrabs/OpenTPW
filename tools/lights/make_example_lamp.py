"""Writes the example lamp post as a binary glTF (standard library only): a base, a post and a glowing bulb, and a
KHR_lights_punctual point light on a dummy above the bulb (docs/LIGHTS.md, "The example mod").

Usage:
	python3 -I tools/lights/make_example_lamp.py OUT.glb

The scene is in glTF's frame (right-handed, Y up); the content kit's converter mirrors it into MD2's. A park cell is 10 units,
so the lamp stands on the cell (x 0..10, z -10..0, which is Blender's y 0..10) and is about 18 units tall, a little taller
than a guest. build-example-mod.sh turns the glb into the mod folder.
"""

import json
import struct
import sys
import zlib


def png(width, height, rgba):
	"""A solid-colour PNG (RGBA 8 bit)."""
	row = b"\x00" + bytes(rgba) * width
	raw = row * height

	def chunk(kind, data):
		body = kind + data
		return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

	return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


def box(x0, y0, z0, x1, y1, z1):
	"""A box with outward normals and a texture coordinate square per face: positions, normals, uvs, indices (counter-clockwise from outside)."""
	faces = [
		((0, 0, 1), [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]),
		((0, 0, -1), [(x1, y0, z0), (x0, y0, z0), (x0, y1, z0), (x1, y1, z0)]),
		((1, 0, 0), [(x1, y0, z1), (x1, y0, z0), (x1, y1, z0), (x1, y1, z1)]),
		((-1, 0, 0), [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)]),
		((0, 1, 0), [(x0, y1, z1), (x1, y1, z1), (x1, y1, z0), (x0, y1, z0)]),
		((0, -1, 0), [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)]),
	]
	positions, normals, uvs, indices = [], [], [], []
	for normal, corners in faces:
		base = len(positions)
		for corner, uv in zip(corners, [(0, 1), (1, 1), (1, 0), (0, 0)]):
			positions.append(corner)
			normals.append(normal)
			uvs.append(uv)
		indices += [base, base + 1, base + 2, base, base + 2, base + 3]
	return positions, normals, uvs, indices


def build():
	meshes = [
		("lamp_base", box(3.5, 0, -6.5, 6.5, 1.5, -3.5), 0),
		("lamp_post", box(4.4, 1.5, -5.6, 5.6, 16.0, -4.4), 0),
		("lamp_bulb", box(4.0, 16.0, -6.0, 6.0, 18.2, -4.0), 1),
	]
	images = [png(8, 8, (70, 74, 84, 255)), png(8, 8, (255, 214, 140, 255))]
	binary = bytearray()
	views, accessors, gltf_meshes = [], [], []

	def add_view(data):
		while len(binary) % 4:
			binary.append(0)
		views.append({"buffer": 0, "byteOffset": len(binary), "byteLength": len(data)})
		binary.extend(data)
		return len(views) - 1

	for name, (positions, normals, uvs, indices), material in meshes:
		flat = [c for p in positions for c in p]
		low = [min(p[i] for p in positions) for i in range(3)]
		high = [max(p[i] for p in positions) for i in range(3)]
		pos = len(accessors)
		accessors.append({"bufferView": add_view(struct.pack(f"<{len(flat)}f", *flat)), "componentType": 5126, "count": len(positions), "type": "VEC3", "min": low, "max": high})
		flat = [c for n in normals for c in n]
		accessors.append({"bufferView": add_view(struct.pack(f"<{len(flat)}f", *flat)), "componentType": 5126, "count": len(normals), "type": "VEC3"})
		flat = [c for t in uvs for c in t]
		accessors.append({"bufferView": add_view(struct.pack(f"<{len(flat)}f", *flat)), "componentType": 5126, "count": len(uvs), "type": "VEC2"})
		accessors.append({"bufferView": add_view(struct.pack(f"<{len(indices)}H", *indices)), "componentType": 5123, "count": len(indices), "type": "SCALAR"})
		gltf_meshes.append({"name": name, "primitives": [{"attributes": {"POSITION": pos, "NORMAL": pos + 1, "TEXCOORD_0": pos + 2}, "indices": pos + 3, "material": material}]})
	image_views = [add_view(image) for image in images]
	document = {
		"asset": {"version": "2.0", "generator": "tools/lights/make_example_lamp.py"},
		"extensionsUsed": ["KHR_lights_punctual"],
		"extensions": {"KHR_lights_punctual": {"lights": [{"type": "point", "name": "lamp_light", "color": [1.0, 0.85, 0.55], "intensity": 1.0, "range": 60.0}]}},
		"scene": 0,
		"scenes": [{"nodes": [0]}],
		"nodes": [
			{"name": "lamp", "children": [1, 2, 3, 4]},
			{"name": "lamp_base", "mesh": 0},
			{"name": "lamp_post", "mesh": 1},
			{"name": "lamp_bulb", "mesh": 2},
			{"name": "light0", "translation": [5.0, 17.1, -5.0], "extensions": {"KHR_lights_punctual": {"light": 0}}, "extras": {"opentpw_light": 0}},
		],
		"meshes": gltf_meshes,
		"materials": [
			{"name": "lamp_metal", "pbrMetallicRoughness": {"baseColorTexture": {"index": 0}}, "extras": {"opentpw_wrap": True}},
			{"name": "lamp_glow", "pbrMetallicRoughness": {"baseColorTexture": {"index": 1}}, "extras": {"opentpw_self_lit": True}},
		],
		"textures": [{"source": 0}, {"source": 1}],
		"images": [{"name": "lamp_metal", "mimeType": "image/png", "bufferView": image_views[0]}, {"name": "lamp_glow", "mimeType": "image/png", "bufferView": image_views[1]}],
		"accessors": accessors,
		"bufferViews": views,
		"buffers": [{"byteLength": len(binary)}],
	}
	return document, bytes(binary)


def glb(document, binary):
	text = json.dumps(document, separators=(",", ":")).encode()
	text += b" " * (-len(text) % 4)
	binary = binary + b"\x00" * (-len(binary) % 4)
	total = 12 + 8 + len(text) + 8 + len(binary)
	return struct.pack("<III", 0x46546C67, 2, total) + struct.pack("<II", len(text), 0x4E4F534A) + text + struct.pack("<II", len(binary), 0x004E4942) + binary


if __name__ == "__main__":
	if len(sys.argv) != 2:
		sys.exit(__doc__)
	with open(sys.argv[1], "wb") as out:
		out.write(glb(*build()))
