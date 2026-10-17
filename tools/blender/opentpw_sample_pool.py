"""Builds the content kit's sample object, a 3 x 3 cell pool with six swimmer head points, and exports it.

Usage:
	blender -b --factory-startup --python tools/blender/opentpw_sample_pool.py -- --out pool.glb

Everything is generated here (geometry and textures), so the sample needs no game data and contains no original art.
"""

import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_conventions as conventions  # noqa: E402
import opentpw_export  # noqa: E402

CELLS = 3
SIZE = CELLS * conventions.CELL_UNITS
RIM = 1.5
RIM_HEIGHT = 1.0
WATER_HEIGHT = 0.6


def clear_scene():
	for obj in list(bpy.data.objects):
		bpy.data.objects.remove(obj, do_unlink=True)


def checker_image(name, size, colour_a, colour_b, squares):
	image = bpy.data.images.new(name, width=size, height=size, alpha=False)
	pixels = []
	step = size // squares
	for y in range(size):
		for x in range(size):
			colour = colour_a if ((x // step) + (y // step)) % 2 == 0 else colour_b
			pixels.extend((*colour, 1.0))
	image.pixels = pixels
	image.file_format = "PNG"
	image.pack()
	return image


def textured_material(name, image, wrap=True, self_lit=False):
	material = bpy.data.materials.new(name)
	material.use_nodes = True
	nodes = material.node_tree.nodes
	shader = nodes.get("Principled BSDF")
	texture = nodes.new("ShaderNodeTexImage")
	texture.image = image
	material.node_tree.links.new(texture.outputs["Color"], shader.inputs["Base Color"])
	material[conventions.WRAP_PROPERTY] = wrap
	material[conventions.SELF_LIT_PROPERTY] = self_lit
	return material


def box(name, minimum, maximum, material):
	bpy.ops.mesh.primitive_cube_add(size=1.0)
	obj = bpy.context.active_object
	obj.name = name
	obj.scale = tuple(maximum[i] - minimum[i] for i in range(3))
	obj.location = tuple((maximum[i] + minimum[i]) / 2 for i in range(3))
	bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
	bpy.ops.object.mode_set(mode="EDIT")
	bpy.ops.uv.cube_project(cube_size=conventions.CELL_UNITS)
	bpy.ops.object.mode_set(mode="OBJECT")
	obj.data.materials.append(material)
	return obj


def build():
	clear_scene()
	tile = textured_material("pool_tile", checker_image("pooltile", 64, (0.85, 0.85, 0.8), (0.55, 0.7, 0.8), 8))
	water = textured_material("pool_water", checker_image("poolwatr", 64, (0.2, 0.5, 0.85), (0.25, 0.6, 0.9), 4), self_lit=True)
	half = SIZE / 2
	root = bpy.data.objects.new("pool", None)
	bpy.context.scene.collection.objects.link(root)
	parts = [
		box("rim_n", (-half, half - RIM, 0), (half, half, RIM_HEIGHT), tile),
		box("rim_s", (-half, -half, 0), (half, -half + RIM, RIM_HEIGHT), tile),
		box("rim_e", (half - RIM, -half + RIM, 0), (half, half - RIM, RIM_HEIGHT), tile),
		box("rim_w", (-half, -half + RIM, 0), (-half + RIM, half - RIM, RIM_HEIGHT), tile),
		box("water", (-half + RIM, -half + RIM, 0), (half - RIM, half - RIM, WATER_HEIGHT), water),
	]
	for part in parts:
		part.parent = root
	# Six swimmer heads just above the water, ids 1..6 in reading order.
	inner = half - RIM - 3.0
	positions = [(-inner, inner), (0.0, inner), (inner, inner), (-inner, -inner), (0.0, -inner), (inner, -inner)]
	for index, (x, y) in enumerate(positions, start=1):
		head = bpy.data.objects.new(f"head{index:02d}", None)
		head.location = (x, y, WATER_HEIGHT + 0.3)
		head[conventions.HEAD_PROPERTY] = index
		head.parent = root
		bpy.context.scene.collection.objects.link(head)


if __name__ == "__main__":
	arguments = opentpw_export.parse_arguments()
	build()
	sys.exit(0 if opentpw_export.export(arguments.out) else 1)
