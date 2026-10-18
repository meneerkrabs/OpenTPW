"""Packs the umbrella sprite set rendered by tools/blender/opentpw_umbrella.py into SPR_UM.ESP/.TPC with the content kit.

Usage (python3 -I tools/umbrellas/pack_sprites.py ...):
	--frames <dir>   the output folder of opentpw_umbrella.py (the set is <dir>/tpw/Umbrella/UM)
	--out <dir>      where SPR_UM.ESP and SPR_UM.TPC go (content/umbrellas/esprites/Umbrellas)
	--stage <dir>    scratch folder for the converted set (default: a temporary folder)
	--kit <command>  the kit command line, split like a shell (default: dotnet run --project source/OpenTPW.ContentKit -c Release --)

It reuses the converter of the pools' packer (tools/pools/pack_sprite_sets.py), which this script does not change: detailFlag 0 (a .TPC bank only), pixels per unit 256 for the 2x set.
"""

import argparse
import importlib.util
import os
import shlex
import shutil
import subprocess
import sys
import tempfile

here = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("pack_sprite_sets", os.path.join(here, "..", "pools", "pack_sprite_sets.py"))
packer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packer)


def main():
	parser = argparse.ArgumentParser(prog="pack_sprites")
	parser.add_argument("--frames", required=True)
	parser.add_argument("--out", required=True)
	parser.add_argument("--stage")
	parser.add_argument("--kit", default="dotnet run --project source/OpenTPW.ContentKit -c Release --")
	arguments = parser.parse_args()
	source = os.path.join(arguments.frames, "tpw", "Umbrella", "UM")
	if not os.path.isfile(os.path.join(source, "set.json")):
		sys.exit(f"pack_sprites: {source}/set.json is missing; render the set first (tools/blender/opentpw_umbrella.py)")
	stage = arguments.stage or tempfile.mkdtemp(prefix="umbrellas-stage-")
	if os.path.isdir(stage):
		shutil.rmtree(stage)
	target = os.path.join(stage, "SPR_UM")
	os.makedirs(target)
	count = packer.convert(source, target)
	if os.path.isdir(arguments.out):
		shutil.rmtree(arguments.out)
	command = shlex.split(arguments.kit) + ["sprites", "--frames", target, "--out", arguments.out, "--force"]
	print(f"pack_sprites: {count} frames")
	result = subprocess.run(command)
	if arguments.stage is None:
		shutil.rmtree(stage, ignore_errors=True)
	sys.exit(result.returncode)


if __name__ == "__main__":
	main()
