#!/usr/bin/env python3
"""Builds the native libraries that the pinned NuGet packages do not ship for a runtime.

Veldrid.SPIRV 1.0.14, Veldrid.SDL2 4.8.0 and ImGui.NET 1.87.2 only carry win-x64/x86,
linux-x64 and Intel/universal macOS binaries. This script builds the *same pinned
source revisions* for the remaining release targets and writes them, under the file
names the managed loaders probe, to native/<rid>/:

    veldrid-spirv  Veldrid.SPIRV v1.0.14 (shaderc/SPIRV-Cross from its known_good.json)
    cimgui         ImGui.NET-nativebuild v1.87.1 (Dear ImGui 1.87)
    sdl2           SDL release-2.32.10

Run it natively on the target (Apple Silicon, Windows on Arm, Linux arm64): the
architecture and ABI-export checks read the produced binary. It needs git, cmake,
a C/C++ toolchain and Python 3; it installs nothing and does not run .NET.

    python3 scripts/build-native.py --rid osx-arm64 veldrid-spirv sdl2
"""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import platform
import shutil
import struct
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent

SOURCES = {
    "veldrid-spirv": ("https://github.com/veldrid/veldrid-spirv.git", "v1.0.14", "f2e50faa56e9a4fa63394de0be7baa714043222d"),
    "cimgui": ("https://github.com/ImGuiNET/ImGui.NET-nativebuild.git", "v1.87.1", "54206de6510edd82db7a07d3b6c1d1c8e4dddc33"),
    "sdl2": ("https://github.com/libsdl-org/SDL.git", "release-2.32.10", "5d249570393f7a37e037abf22cd6012a4cc56a71"),
}

# Submodule revisions the pinned releases record; a mismatch means the checkout is not the release.
CIMGUI_SUBMODULE = "4492660bb9db72f27af09047f3c98d63bb53c91c"

# Symbols the managed bindings call; their absence means the wrong library was built.
EXPORTS = {
    "veldrid-spirv": ["CrossCompile", "CompileGlslToSpirv", "FreeResult"],
    "cimgui": ["igCreateContext", "igNewFrame", "igRender"],
    "sdl2": ["SDL_Init", "SDL_CreateWindow", "SDL_PollEvent"],
}

RIDS = {
    "osx-arm64": ("darwin", "arm64"),
    "osx-x64": ("darwin", "x64"),
    "win-arm64": ("windows", "arm64"),
    "win-x64": ("windows", "x64"),
    "win-x86": ("windows", "x86"),
    "linux-arm64": ("linux", "arm64"),
    "linux-x64": ("linux", "x64"),
}


def output_name(library: str, system: str) -> str:
    """File names probed by Veldrid.SPIRV, ImGui.NET (DllImport "cimgui") and Veldrid.SDL2."""
    names = {
        "veldrid-spirv": {"windows": "libveldrid-spirv.dll", "linux": "libveldrid-spirv.so", "darwin": "libveldrid-spirv.dylib"},
        "cimgui": {"windows": "cimgui.dll", "linux": "libcimgui.so", "darwin": "libcimgui.dylib"},
        "sdl2": {"windows": "SDL2.dll", "linux": "libSDL2-2.0.so.0", "darwin": "libsdl2.dylib"},
    }
    return names[library][system]


def run(*command: str, cwd: pathlib.Path | None = None) -> None:
    print("+", " ".join(command), flush=True)
    subprocess.run(command, cwd=cwd, check=True)


def output(*command: str) -> str:
    return subprocess.check_output(command, text=True).strip()


def host_matches(system: str, arch: str) -> bool:
    machine = platform.machine().lower()
    host_arch = {"arm64": "arm64", "aarch64": "arm64", "x86_64": "x64", "amd64": "x64", "x86": "x86", "i686": "x86"}.get(machine, machine)
    host_system = {"Darwin": "darwin", "Windows": "windows", "Linux": "linux"}[platform.system()]
    # A 64-bit Windows host can build x86 binaries with the Win32 generator platform.
    return host_system == system and (host_arch == arch or (system == "windows" and arch == "x86" and host_arch == "x64"))


def checkout(library: str, cache: pathlib.Path) -> pathlib.Path:
    url, tag, revision = SOURCES[library]
    source = cache / library
    if not source.exists():
        source.parent.mkdir(parents=True, exist_ok=True)
        run("git", "clone", "--depth", "1", "--branch", tag, url, str(source))
    if output("git", "-C", str(source), "rev-parse", "HEAD") != revision:
        sys.exit(f"Source cache must be at {library} {tag} ({revision}): {source}")
    if output("git", "-C", str(source), "status", "--porcelain", "--untracked-files=no", "--ignore-submodules=all"):
        sys.exit(f"Refusing to build modified release sources: {source}")
    run("git", "-C", str(source), "submodule", "update", "--init", "--recursive")
    status = output("git", "-C", str(source), "submodule", "status", "--recursive")
    if any(line[:1] in "+U-" for line in status.splitlines() if line):
        sys.exit(f"Submodule revisions do not match the {library} release.")
    return source


def sync_shaderc(source: pathlib.Path) -> None:
    """Checks out shaderc's transitive dependencies at the release's ext/known_good.json revisions."""
    with (source / "ext/known_good.json").open() as manifest:
        commits = json.load(manifest)["commits"]
    for dependency in sorted(commits, key=lambda entry: entry.get("subdir", ".")):
        target = source / "ext/shaderc" / dependency.get("subdir", ".")
        revision = dependency["commit"]
        remote = "https://github.com/" + dependency["subrepo"] + ".git"
        if not (target / ".git").exists():
            target.mkdir(parents=True, exist_ok=True)
            run("git", "init", str(target))
            run("git", "-C", str(target), "remote", "add", "origin", remote)
        if output("git", "-C", str(target), "status", "--porcelain", "--untracked-files=no"):
            sys.exit(f"Refusing to overwrite modified pinned dependency: {target}")
        if subprocess.run(["git", "-C", str(target), "cat-file", "-e", revision + "^{commit}"],
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode:
            run("git", "-C", str(target), "fetch", "--depth", "1", remote, revision)
        run("git", "-C", str(target), "checkout", "--detach", revision)
        if output("git", "-C", str(target), "rev-parse", "HEAD") != revision:
            sys.exit(f"Pinned dependency revision mismatch: {target}")


def cmake_arguments(system: str, arch: str) -> list[str]:
    arguments = ["-DCMAKE_BUILD_TYPE=Release", "-DCMAKE_POLICY_VERSION_MINIMUM=3.5"]
    if system == "darwin":
        arguments += [
            "-DCMAKE_OSX_ARCHITECTURES=" + ("arm64" if arch == "arm64" else "x86_64"),
            "-DCMAKE_OSX_DEPLOYMENT_TARGET=11.0",
            "-DCMAKE_INSTALL_NAME_DIR=@rpath",
            "-DCMAKE_BUILD_WITH_INSTALL_NAME_DIR=ON",
        ]
    elif system == "windows":
        # Static CRT, so the DLLs do not need a Visual C++ redistributable on the player's machine.
        arguments += [
            "-A", {"arm64": "ARM64", "x64": "x64", "x86": "Win32"}[arch],
            "-DCMAKE_POLICY_DEFAULT_CMP0091=NEW",
            "-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded",
        ]
    else:
        arguments += ["-DCMAKE_SHARED_LINKER_FLAGS=-static-libstdc++ -static-libgcc"]
    return arguments


def build(source: pathlib.Path, build_dir: pathlib.Path, target: str, system: str, arch: str, extra: list[str], jobs: int) -> None:
    run("cmake", "-S", str(source), "-B", str(build_dir), *cmake_arguments(system, arch), *extra)
    run("cmake", "--build", str(build_dir), "--config", "Release", "--target", target, "--parallel", str(jobs))


def find_built(build_dir: pathlib.Path, patterns: list[str]) -> pathlib.Path:
    for pattern in patterns:
        matches = sorted(path for path in build_dir.rglob(pattern) if path.is_file() and not path.is_symlink())
        if matches:
            return matches[0]
    sys.exit(f"No build output matching {patterns} under {build_dir}")


def binary_arch(path: pathlib.Path) -> str:
    """Reads the CPU type from a Mach-O, ELF or PE header."""
    data = path.read_bytes()
    if data[:4] in (b"\xcf\xfa\xed\xfe", b"\xce\xfa\xed\xfe"):
        cpu = struct.unpack_from("<I", data, 4)[0]
        return {0x0100000C: "arm64", 0x01000007: "x64", 7: "x86"}.get(cpu, hex(cpu))
    if data[:4] == b"\x7fELF":
        machine = struct.unpack_from("<H", data, 18)[0]
        return {0xB7: "arm64", 0x3E: "x64", 0x03: "x86"}.get(machine, hex(machine))
    if data[:2] == b"MZ":
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        machine = struct.unpack_from("<H", data, pe + 4)[0]
        return {0xAA64: "arm64", 0x8664: "x64", 0x14C: "x86"}.get(machine, hex(machine))
    return "unknown"


def pe_exports(data: bytes) -> set[str]:
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    optional = pe + 24
    magic = struct.unpack_from("<H", data, optional)[0]
    directories = optional + (112 if magic == 0x20B else 96)
    export_rva = struct.unpack_from("<I", data, directories)[0]
    if not export_rva:
        return set()
    count = struct.unpack_from("<H", data, pe + 6)[0]
    sections = optional + struct.unpack_from("<H", data, pe + 20)[0]

    def offset(rva: int) -> int:
        for index in range(count):
            _, size, address, raw_size, raw = struct.unpack_from("<8sIIII", data, sections + 40 * index)
            if address <= rva < address + max(size, raw_size):
                return raw + rva - address
        raise ValueError(f"RVA {rva:#x} is outside every section")

    directory = offset(export_rva)
    names, name_table = struct.unpack_from("<I", data, directory + 24)[0], struct.unpack_from("<I", data, directory + 32)[0]
    result = set()
    for index in range(names):
        start = offset(struct.unpack_from("<I", data, offset(name_table) + 4 * index)[0])
        result.add(data[start:data.index(b"\0", start)].decode("ascii"))
    return result


def exported_symbols(path: pathlib.Path, system: str) -> set[str]:
    if system == "windows":
        return pe_exports(path.read_bytes())
    command = ["nm", "-gU", str(path)] if system == "darwin" else ["nm", "-D", "--defined-only", str(path)]
    return {line.split()[-1].lstrip("_") if system == "darwin" else line.split()[-1] for line in output(*command).splitlines() if line.strip()}


def verify(library: str, path: pathlib.Path, system: str, arch: str) -> None:
    actual = binary_arch(path)
    if actual != arch:
        sys.exit(f"{path} is {actual}, expected {arch}")
    missing = [symbol for symbol in EXPORTS[library] if symbol not in exported_symbols(path, system)]
    if missing:
        sys.exit(f"{path} lacks the managed ABI exports {missing}")


def build_library(library: str, rid: str, cache: pathlib.Path, jobs: int) -> pathlib.Path:
    system, arch = RIDS[rid]
    source = checkout(library, cache)
    build_dir = source / "build" / f"opentpw-{rid}"
    if library == "veldrid-spirv":
        sync_shaderc(source)
        build(source, build_dir, "veldrid-spirv", system, arch, ["-DPYTHON_EXECUTABLE=" + sys.executable], jobs)
        built = find_built(build_dir, ["libveldrid-spirv.dylib", "libveldrid-spirv.so", "libveldrid-spirv.dll"])
    elif library == "cimgui":
        submodule = output("git", "-C", str(source / "cimgui"), "rev-parse", "HEAD")
        if submodule != CIMGUI_SUBMODULE:
            sys.exit(f"cimgui submodule is at {submodule}, expected {CIMGUI_SUBMODULE}")
        build(source / "cimgui", build_dir, "cimgui", system, arch, [], jobs)
        built = find_built(build_dir, ["cimgui.dll", "cimgui.so", "libcimgui.so", "cimgui.dylib", "libcimgui.dylib"])
    else:
        build(source, build_dir, "SDL2", system, arch, ["-DSDL_SHARED=ON", "-DSDL_STATIC=OFF", "-DSDL_TEST=OFF"], jobs)
        built = find_built(build_dir, ["SDL2.dll", "libSDL2-2.0.0.dylib", "libSDL2-2.0.so.0.*", "libSDL2-2.0.dylib"])
    verify(library, built, system, arch)
    destination = ROOT / "native" / rid / output_name(library, system)
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(built, destination)
    print(f"Built {library} {SOURCES[library][1]} for {rid}: {destination}", flush=True)
    return destination


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--rid", required=True, choices=sorted(RIDS))
    parser.add_argument("--cache-dir", type=pathlib.Path, default=ROOT.parent / "tooling",
        help="external source cache (default: ../tooling next to the checkout)")
    parser.add_argument("--jobs", type=int, default=int(os.environ.get("NATIVE_BUILD_JOBS", "4")))
    parser.add_argument("libraries", nargs="+", choices=sorted(SOURCES))
    arguments = parser.parse_args()
    system, arch = RIDS[arguments.rid]
    if not host_matches(system, arch):
        sys.exit(f"Run this natively on {arguments.rid}: the checks read the produced binaries.")
    for tool in ("git", "cmake"):
        if not shutil.which(tool):
            sys.exit(f"Missing prerequisite: {tool}")
    for library in arguments.libraries:
        build_library(library, arguments.rid, arguments.cache_dir.resolve(), arguments.jobs)


if __name__ == "__main__":
    main()
