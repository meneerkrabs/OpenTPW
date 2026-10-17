"""Packs the generated guest sprite sets of the swimming pools into ESP/TPC files with the content kit.

The Blender generator (tools/blender/opentpw_guest_sprites.py) writes per set a folder of ``slotNN_dD_fF.png`` frames and a
generator-format ``set.json`` (a slot list with the frames of every direction and their origins, and the ``pixelsPerUnit`` the frames
were rendered for). ``opentpw-kit sprites`` reads a different ``set.json`` (a slot map and a frame-origin map), so this script
converts the description into a staging folder and runs the kit on it.

Usage (python3 -I tools/pools/pack_sprite_sets.py ...):
	--kind <kind>    pool (the swimwear table, <edition>/Pool/<SET>), kids (the clothes sets, <edition>/Kids/<SET>) or heads (<edition>/Kidsheads/<SET>)
	--frames <dir>   the generator output below which the <edition>/... folders lie (the --out of opentpw_guest_sprites.py)
	--slots <list>   only these animation slots (the shipped kid sets carry 0, 1 and 14, the slots the land renderers draw)
	--sets <list>    only these sets (the other sets already in --out stay)
	--out <dir>      where SPR_xx.ESP and SPR_xx.TPC go (content/pools/esprites/Swim, Generic/Kids or Generic/Kidsheads)
	--stage <dir>    scratch folder for the converted sets (default: a temporary folder)
	--kit <command>  the kit command line, split like a shell (default: dotnet run --project source/OpenTPW.ContentKit -c Release --)

The sets are packed with detailFlag 0: a .TPC bank only (the pools' engine code loads the sets directly from the extension's folder, not
through the game's esprites.wad, so no .FPC copy is needed). Every frame carries the pixels per unit of its set (256 for the 2x sets, so the
game draws it at the size of the 1x frame it replaces). The eight sets, sorted by name (BE BI CH FR KI SA SU TA), are guest types 0 to 7.
"""

import argparse
import json
import os
import shlex
import shutil
import subprocess
import sys
import tempfile

SETS = ("BE", "BI", "CH", "FR", "KI", "SA", "SU", "TA")
EDITION = "tpw"
FOLDERS = {"pool": "Pool", "kids": "Kids", "heads": "Kidsheads"}


def convert(source, target, wanted=None):
	"""Converts one generator set folder into the kit's set folder (only the animation slots in ``wanted`` when it is given; the others stay
	empty in the ESP table, so the game falls back to standing for them); returns the number of frames."""
	with open(os.path.join(source, "set.json"), encoding="utf-8") as handle:
		description = json.load(handle)
	slots = {}
	frames = {}
	count = 0
	for slot in description["slots"]:
		if wanted is not None and slot["slot"] not in wanted:
			continue
		slots[str(slot["slot"])] = {"directions": slot["directions"], "framesPerDirection": slot["framesPerDirection"]}
		for direction in slot["frames"]:
			for frame in direction:
				stem = frame["file"][:-4] if frame["file"].endswith(".png") else frame["file"]
				frames[stem] = {"originX": frame["originX"], "originY": frame["originY"]}
				shutil.copyfile(os.path.join(source, frame["file"]), os.path.join(target, frame["file"]))
				count += 1
	document = {
		"name": description["name"],
		"detailFlag": 0,
		"kidFlag": 1 if description.get("kidFlag") else 0,
		"pixelsPerUnit": description.get("pixelsPerUnit", 128),
		"slots": slots,
		"frames": frames,
	}
	with open(os.path.join(target, "set.json"), "w", encoding="utf-8") as handle:
		json.dump(document, handle, indent=1)
		handle.write("\n")
	return count


def main():
	parser = argparse.ArgumentParser(prog="pack_sprite_sets")
	parser.add_argument("--kind", choices=sorted(FOLDERS), default="pool")
	parser.add_argument("--frames", required=True)
	parser.add_argument("--out", required=True)
	parser.add_argument("--sets", default=",".join(SETS), help="the sets to pack (default: all eight)")
	parser.add_argument("--slots", help="only these animation slots (comma separated; default: every slot of the set)")
	parser.add_argument("--stage")
	parser.add_argument("--kit", default="dotnet run --project source/OpenTPW.ContentKit -c Release --")
	arguments = parser.parse_args()

	stage = arguments.stage or tempfile.mkdtemp(prefix="pools-swim-stage-")
	if os.path.isdir(stage):
		shutil.rmtree(stage)
	command = shlex.split(arguments.kit) + ["sprites"]
	wanted = {int(slot) for slot in arguments.slots.split(",")} if arguments.slots else None
	total = 0
	for name in arguments.sets.split(","):
		source = os.path.join(arguments.frames, EDITION, FOLDERS[arguments.kind], name)
		if not os.path.isfile(os.path.join(source, "set.json")):
			sys.exit(f"pack_sprite_sets: {source}/set.json is missing; render the sets first (opentpw_guest_sprites.py --edition TPW --part pool|land|heads --scale 2)")
		target = os.path.join(stage, f"SPR_{name}")
		os.makedirs(target)
		total += convert(source, target, wanted)
		command += ["--frames", target]
	if os.path.isdir(arguments.out) and sorted(arguments.sets.split(",")) == sorted(SETS):
		shutil.rmtree(arguments.out)    # a full run replaces the folder; a run of some sets leaves the other sets alone
	command += ["--out", arguments.out, "--force"]
	print(f"pack_sprite_sets: {len(arguments.sets.split(','))} sets, {total} frames")
	result = subprocess.run(command)
	if arguments.stage is None:
		shutil.rmtree(stage, ignore_errors=True)
	sys.exit(result.returncode)


if __name__ == "__main__":
	main()
