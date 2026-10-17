"""Builds the water slide tower (a stair tower with a platform, 2 x 2 cells) for each theme and profile and exports one .glb per theme and profile.

Usage:
	blender -b --factory-startup --python tools/blender/opentpw_slide_tower.py -- --out <dir> [--themes jungle,hallow,space,fantasy] [--preview <dir>]

Writes ``<dir>/<theme>-flume.glb`` and ``<dir>/<theme>-tube.glb`` (and, with --preview, ``.png`` renders for looking at the result; they are not part of the content). Everything is
generated here, geometry and textures (the textures are those of the changing room, tools/blender/opentpw_changing_room.py), so the tower needs no game data and contains no original art.

Footprint: 2 x 2 cells = the Blender X/Y range 0..20, with the Info.Shape (first row = top = +Y)

	**
	**

The stairs face the pool, on the +Y face (the shape's front, the local -V side at rotation 0, which is where ``Pools.SlideTower`` looks for water): a flight of twelve steps along the
left edge (X 0..7) rises from the ground at Y = 20 to the platform at Y = 8. The platform is an L of three slabs whose top lies exactly 25 units (five engine units) above the ground,
the height at which ``PoolSlideTowers.TopHeight`` puts the track and the waiting guests' feet. The other three sides are open: the track starts on any of them. Corner posts carry a
finial that tells the two profiles apart: a flat pennant for the open flume, a wide cap for the closed tube.
"""

import argparse
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_changing_room as cabin  # noqa: E402
import opentpw_export  # noqa: E402

SIZE = 20.0
TOP = 25.0                 # the platform surface: PoolSlideTowers.TopHeight (5 engine units) x 5 model units
SLAB = 1.2                 # platform thickness
POST = 1.6
STEPS = 12
STAIR_WIDTH = 7.0
STAIR_TOP_Y = 8.0          # where the flight meets the platform
RAIL = 4.0

THEMES = cabin.THEMES
PROFILES = ("flume", "tube")


def add_tower(b, theme, profile):
	# the platform: an L of slabs (the stair flight leaves the front left corner free)
	b.box("roof", (0.0, 0.0, TOP - SLAB), (SIZE, STAIR_TOP_Y, TOP), skip=())
	b.box("roof", (STAIR_WIDTH, STAIR_TOP_Y, TOP - SLAB), (SIZE, SIZE, TOP), skip=("-x",))
	# the stairs: twelve steps, each a solid block from the ground to its tread
	depth = (SIZE - STAIR_TOP_Y) / STEPS
	for i in range(STEPS):
		y_hi = SIZE - i * depth
		y_lo = y_hi - depth
		height = TOP * (i + 1) / STEPS
		b.box("wall", (0.0, y_lo, 0.0), (STAIR_WIDTH, y_hi, height), skip=("-z", "-y") if i > 0 else ("-z",),
			faces_material={"+z": "trim"})
	# corner posts under and above the platform (the front left one stands beside the flight)
	for px, py in ((0.0, 0.0), (SIZE - POST, 0.0), (SIZE - POST, SIZE - POST), (STAIR_WIDTH, STAIR_TOP_Y)):
		b.box("wall", (px, py, 0.0), (px + POST, py + POST, TOP + RAIL), skip=("-z",))
	# a low rail along the front of the platform (over the stairs' side the flight's own edge keeps the climbers in) and along the back wall
	b.box("trim", (STAIR_WIDTH + POST, SIZE - 0.6, TOP), (SIZE - POST, SIZE, TOP + 2.2), skip=("-z",))
	b.box("trim", (POST, 0.0, TOP), (SIZE - POST, 0.6, TOP + 2.2), skip=("-z",))
	# finial on the front right post: a pennant for the open flume, a wide cap for the closed tube
	post_x, post_y = SIZE - POST, SIZE - POST
	if profile == "flume":
		b.box("trim", (post_x + 0.6, post_y + 0.6, TOP + RAIL), (post_x + 1.0, post_y + 1.0, TOP + RAIL + 5.0), skip=("-z",))
		b.box("light" if theme == "space" else "roof", (post_x - 3.5, post_y + 0.7, TOP + RAIL + 3.0), (post_x + 1.0, post_y + 0.9, TOP + RAIL + 5.0), skip=("-z",))
	else:
		# a wide cap on the post, and a bead on top of it
		b.box("roof", (post_x - 1.6, post_y - 1.6, TOP + RAIL), (SIZE, SIZE, TOP + RAIL + 1.4), skip=("-z",))
		b.box("light" if theme == "space" else "trim", (post_x + 0.3, post_y + 0.3, TOP + RAIL + 1.4), (post_x + 1.3, post_y + 1.3, TOP + RAIL + 2.8), skip=("-z",))
	if theme == "hallow":
		b.box("trim", (0.0, 0.0, TOP + RAIL), (POST, POST, TOP + RAIL + 3.0), skip=("-z",))
	elif theme == "space":
		b.box("light", (0.0, 0.3, TOP - SLAB + 0.2), (SIZE, 0.7, TOP - 0.1), skip=("-z", "-y", "+z"))


def build(theme, profile):
	cabin.clear_scene()
	images = cabin.make_textures(theme)
	materials = {part: cabin.make_material(f"cr_{cabin.CODES[theme]}_{part}", image, self_lit=(part == "light")) for part, image in images.items()}
	if "light" not in materials:
		materials["light"] = materials["trim"]
	b = cabin.Builder(materials)
	add_tower(b, theme, profile)
	obj = b.build("tower")
	bpy.context.view_layer.objects.active = obj
	return obj


def render_preview(path):
	"""The changing room's workbench render, framed for a tower three times as tall as the cabin."""
	import mathutils
	import numpy as np
	scene = bpy.context.scene
	scene.render.engine = "BLENDER_WORKBENCH"
	scene.render.resolution_x, scene.render.resolution_y = 800, 700
	shading = scene.display.shading
	shading.light = "STUDIO"
	shading.color_type = "TEXTURE"
	shading.show_cavity = False
	shading.show_shadows = True
	scene.view_settings.view_transform = "Standard"
	world = bpy.data.worlds.new("preview") if scene.world is None else scene.world
	scene.world = world
	world.color = (0.55, 0.75, 0.55)
	camera_data = bpy.data.cameras.new("cam")
	camera_data.type = "ORTHO"
	camera_data.ortho_scale = 62.0
	camera = bpy.data.objects.new("cam", camera_data)
	scene.collection.objects.link(camera)
	# from the stairs' side (+Y), the right (+X) and above
	target = np.array([10.0, 10.0, 15.0])
	direction = np.array([0.6, 0.75, 0.55])
	direction /= np.linalg.norm(direction)
	camera.location = tuple(target + direction * 90.0)
	camera.rotation_euler = mathutils.Vector(-direction).to_track_quat("-Z", "Y").to_euler()
	scene.camera = camera
	scene.render.filepath = path
	bpy.ops.render.render(write_still=True)


def parse_arguments():
	argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	parser = argparse.ArgumentParser(prog="opentpw_slide_tower")
	parser.add_argument("--out", required=True)
	parser.add_argument("--themes", default=",".join(THEMES))
	parser.add_argument("--preview")
	return parser.parse_args(argv)


def main():
	arguments = parse_arguments()
	themes = [t.strip() for t in arguments.themes.split(",") if t.strip()]
	for theme in themes:
		if theme not in THEMES:
			raise SystemExit(f"unknown theme {theme}; use {','.join(THEMES)}")
	os.makedirs(arguments.out, exist_ok=True)
	for theme in themes:
		for profile in PROFILES:
			obj = build(theme, profile)
			print(f"{theme} {profile}: {len(obj.data.polygons)} polygons, {sum(len(p.vertices) - 2 for p in obj.data.polygons)} triangles")
			if not opentpw_export.export(os.path.join(arguments.out, f"{theme}-{profile}.glb")):
				sys.exit(1)
			if arguments.preview:
				os.makedirs(arguments.preview, exist_ok=True)
				render_preview(os.path.join(arguments.preview, f"{theme}-{profile}.png"))


if __name__ == "__main__":
	main()
