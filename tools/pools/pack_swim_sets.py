"""Packs the generated swim sprite sets of the swimming pools into ESP/TPC files with the content kit.

The Blender generator (tools/blender/opentpw_guest_sprites.py) writes per set a folder of ``slotNN_dD_fF.png`` frames and a
generator-format ``set.json`` (a slot list with the frames of every direction and their origins). ``opentpw-kit sprites`` reads a
different ``set.json`` (a slot map and a frame-origin map), so this script converts the description into a staging folder and
runs the kit on it.

Usage (python3 -I tools/pools/pack_swim_sets.py ...):
	--frames <dir>   the generator output below which <edition>/Kids/<SET>/ folders lie (the --out of opentpw_guest_sprites.py)
	--out <dir>      where SPR_xx.ESP and SPR_xx.TPC go (content/pools/esprites/Swim)
	--stage <dir>    scratch folder for the converted sets (default: a temporary folder)
	--kit <command>  the kit command line, split like a shell (default: dotnet run --project source/OpenTPW.ContentKit -c Release --)

The sets are packed with detailFlag 0: a .TPC bank only (the pools' engine code loads the sets directly from the extension's folder, not
through the game's esprites.wad, so no .FPC copy is needed). The eight sets, sorted by name (BE BI CH FR KI SA SU TA), are guest types 0 to 7.
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
FOLDER = "Kids"


def convert(source, target):
	"""Converts one generator set folder into the kit's set folder; returns the number of frames."""
	with open(os.path.join(source, "set.json"), encoding="utf-8") as handle:
		description = json.load(handle)
	slots = {}
	frames = {}
	count = 0
	for slot in description["slots"]:
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
		"slots": slots,
		"frames": frames,
	}
	with open(os.path.join(target, "set.json"), "w", encoding="utf-8") as handle:
		json.dump(document, handle, indent=1)
		handle.write("\n")
	return count


def main():
	parser = argparse.ArgumentParser(prog="pack_swim_sets")
	parser.add_argument("--frames", required=True)
	parser.add_argument("--out", required=True)
	parser.add_argument("--stage")
	parser.add_argument("--kit", default="dotnet run --project source/OpenTPW.ContentKit -c Release --")
	arguments = parser.parse_args()

	stage = arguments.stage or tempfile.mkdtemp(prefix="pools-swim-stage-")
	if os.path.isdir(stage):
		shutil.rmtree(stage)
	command = shlex.split(arguments.kit) + ["sprites"]
	total = 0
	for name in SETS:
		source = os.path.join(arguments.frames, EDITION, FOLDER, name)
		if not os.path.isfile(os.path.join(source, "set.json")):
			sys.exit(f"pack_swim_sets: {source}/set.json is missing; render the sets first (opentpw_guest_sprites.py --edition TPW --part swim)")
		target = os.path.join(stage, f"SPR_{name}")
		os.makedirs(target)
		total += convert(source, target)
		command += ["--frames", target]
	if os.path.isdir(arguments.out):
		shutil.rmtree(arguments.out)
	command += ["--out", arguments.out, "--force"]
	print(f"pack_swim_sets: {len(SETS)} sets, {total} frames")
	result = subprocess.run(command)
	if arguments.stage is None:
		shutil.rmtree(stage, ignore_errors=True)
	sys.exit(result.returncode)


if __name__ == "__main__":
	main()
