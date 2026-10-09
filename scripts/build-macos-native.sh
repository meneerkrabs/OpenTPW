#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd -P)"
source_dir="${VELDRID_SPIRV_SOURCE_DIR:-$(dirname "$root")/tooling/veldrid-spirv}"
build_dir="$source_dir/build/opentpw-osx-arm64"
output_dir="$root/native/osx-arm64"
revision=f2e50faa56e9a4fa63394de0be7baa714043222d

if [[ "$(uname -s)" != Darwin || "$(uname -m)" != arm64 ]]; then
    echo "Run this script natively on an Apple Silicon Mac." >&2
    exit 1
fi

for tool in git cmake python3 clang lipo nm otool; do
    command -v "$tool" >/dev/null || { echo "Missing prerequisite: $tool" >&2; exit 1; }
done

if [[ ! -d "$source_dir" ]]; then
    mkdir -p "$(dirname "$source_dir")"
    git clone --branch v1.0.14 https://github.com/veldrid/veldrid-spirv.git "$source_dir"
fi
if [[ "$(git -C "$source_dir" rev-parse HEAD)" != "$revision" ]]; then
    echo "Source cache must be at Veldrid.SPIRV v1.0.14 ($revision): $source_dir" >&2
    exit 1
fi
if [[ -n "$(git -C "$source_dir" status --porcelain --untracked-files=no --ignore-submodules=all)" ]]; then
    echo "Refusing to build modified release sources: $source_dir" >&2
    exit 1
fi
git -C "$source_dir" submodule update --init --recursive
if git -C "$source_dir" submodule status --recursive | /usr/bin/grep -qE "^[+U-]"; then
    echo "Submodule revisions do not match the release." >&2
    exit 1
fi

python3 - "$source_dir" <<PY
import json
import pathlib
import subprocess
import sys

source_dir = pathlib.Path(sys.argv[1])
with (source_dir / "ext/known_good.json").open() as manifest:
    commits = json.load(manifest)["commits"]
for dependency in sorted(commits, key=lambda entry: entry.get("subdir", ".")):
    checkout = source_dir / "ext/shaderc" / dependency.get("subdir", ".")
    revision = dependency["commit"]
    if not (checkout / ".git").exists():
        checkout.mkdir(parents=True, exist_ok=True)
        subprocess.run(["git", "init", str(checkout)], check=True)
        subprocess.run(["git", "-C", str(checkout), "remote", "add", "origin",
                        "https://github.com/" + dependency["subrepo"] + ".git"], check=True)
    git_command = ["git", "-C", str(checkout)]
    if subprocess.check_output(git_command + ["status", "--porcelain", "--untracked-files=no"]):
        sys.exit("Refusing to overwrite modified pinned dependency: " + str(checkout))
    if subprocess.run(git_command + ["cat-file", "-e", revision + "^{commit}"],
                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode:
        subprocess.run(git_command + ["fetch", "--depth", "1",
                        "https://github.com/" + dependency["subrepo"] + ".git", revision], check=True)
    subprocess.run(git_command + ["checkout", "--detach", revision], check=True)
    actual = subprocess.check_output(git_command + ["rev-parse", "HEAD"], text=True).strip()
    if actual != revision:
        sys.exit("Pinned dependency revision mismatch: " + str(checkout))
cross_dir = source_dir / "ext/SPIRV-Cross"
if subprocess.check_output(["git", "-C", str(cross_dir), "status", "--porcelain",
                            "--untracked-files=no"]):
    sys.exit("Refusing to build modified SPIRV-Cross sources.")
PY

cmake -S "$source_dir" -B "$build_dir" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_OSX_ARCHITECTURES=arm64 \
    -DCMAKE_OSX_DEPLOYMENT_TARGET=11.0 \
    -DCMAKE_INSTALL_NAME_DIR=@rpath \
    -DCMAKE_BUILD_WITH_INSTALL_NAME_DIR=ON \
    -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
    -DPYTHON_EXECUTABLE="$(command -v python3)"
cmake --build "$build_dir" --target veldrid-spirv --parallel "${NATIVE_BUILD_JOBS:-4}"

library="$build_dir/libveldrid-spirv.dylib"
lipo "$library" -verify_arch arm64
exports="$(nm -gU "$library")"
for symbol in CrossCompile CompileGlslToSpirv FreeResult; do
    if ! /usr/bin/grep -qE "[[:space:]]_${symbol}$" <<< "$exports"; then
        echo "Missing managed ABI export: $symbol" >&2
        exit 1
    fi
done
mkdir -p "$output_dir"
cp "$library" "$output_dir/libveldrid-spirv.dylib"
file "$output_dir/libveldrid-spirv.dylib"
otool -L "$output_dir/libveldrid-spirv.dylib"
echo "Built Veldrid.SPIRV 1.0.14 for arm64: $output_dir/libveldrid-spirv.dylib"
