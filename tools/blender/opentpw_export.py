"""Exports a Blender scene (or one collection) to a .glb file for the OpenTPW content kit.

Usage:
	blender -b scene.blend --python tools/blender/opentpw_export.py -- --out model.glb [--collection NAME]

The scene is validated first (opentpw_conventions.validate_scene); any problem aborts the export with exit code 1.
"""

import argparse
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_conventions as conventions  # noqa: E402


def parse_arguments():
	argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	parser = argparse.ArgumentParser(prog="opentpw_export")
	parser.add_argument("--out", required=True)
	parser.add_argument("--collection")
	return parser.parse_args(argv)


def export(out, collection=None):
	objects = list(bpy.data.collections[collection].all_objects) if collection else list(bpy.context.scene.objects)
	problems = conventions.validate_scene(objects)
	if problems:
		for problem in problems:
			print(f"opentpw_export: {problem}", file=sys.stderr)
		return False
	for warning in conventions.light_warnings(objects):
		print(f"opentpw_export: warning: {warning}", file=sys.stderr)
	if collection:
		layer = bpy.context.view_layer.layer_collection.children[collection]
		bpy.context.view_layer.active_layer_collection = layer
	bpy.ops.export_scene.gltf(
		filepath=os.path.abspath(out),
		export_format="GLB",
		use_active_collection=bool(collection),
		export_yup=True,
		export_apply=True,
		export_extras=True,
		export_cameras=False,
		export_lights=True,  # point lights become light dummies (docs/LIGHTS.md); other kinds are skipped by the converter with a warning
		export_animations=False,
		export_image_format="AUTO",
	)
	return True


if __name__ == "__main__":
	arguments = parse_arguments()
	sys.exit(0 if export(arguments.out, arguments.collection) else 1)
