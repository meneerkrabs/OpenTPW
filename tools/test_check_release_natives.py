"""Windows release SDL2 guard (issue #754): synthetic PE files, no network or .NET."""
import os
from pathlib import Path
import re
import struct
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_release_natives as guard  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
MACHINES = {"x64": 0x8664, "x86": 0x14C, "arm64": 0xAA64}
NUGET_SDL2 = Path(os.environ.get("NUGET_PACKAGES", Path.home() / ".nuget" / "packages")) / "veldrid.sdl2" / "4.8.0" / "runtimes" / "win-x64" / "native" / "SDL2.dll"


def fake_pe(arch="x64", version=(2, 32, 10, 0)):
    """An MZ/PE header with the CPU type and, unless version is None, a VS_FIXEDFILEINFO."""
    header = bytearray(0x40)
    header[:2] = b"MZ"
    struct.pack_into("<I", header, 0x3C, 0x40)
    data = bytes(header) + b"PE\0\0" + struct.pack("<H", MACHINES[arch]) + bytes(18)
    if version is not None:
        major, minor, build, revision = version
        data += "VS_VERSION_INFO".encode("utf-16-le") + bytes(4)
        data += struct.pack("<IIII", 0xFEEF04BD, 0x00010000, (major << 16) | minor, (build << 16) | revision)
    return data


class ReleaseNativeGuardTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.folder = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def write(self, relative, data):
        path = self.folder / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)

    def test_pin_is_the_sdl2_release_tag(self):
        self.assertEqual(guard.BUILD_NATIVE.pinned_version("sdl2"), (2, 32, 10))

    def test_file_version_is_read_from_the_fixed_info(self):
        self.assertEqual(guard.BUILD_NATIVE.pe_file_version(fake_pe(version=(2, 0, 8, 0))), (2, 0, 8, 0))
        self.assertIsNone(guard.BUILD_NATIVE.pe_file_version(fake_pe(version=None)))

    def test_pinned_sdl2_passes_for_both_windows_x86_runtimes(self):
        for rid, arch in (("win-x64", "x64"), ("win-x86", "x86"), ("win-arm64", "arm64")):
            with self.subTest(rid=rid):
                self.write("SDL2.dll", fake_pe(arch))
                self.assertEqual(guard.check(self.folder, rid), [])

    def test_newer_sdl2_passes(self):
        self.write("SDL2.dll", fake_pe(version=(2, 32, 12, 0)))
        self.assertEqual(guard.check(self.folder, "win-x64"), [])

    def test_package_sdl2_2_0_8_fails(self):
        self.write("SDL2.dll", fake_pe(version=(2, 0, 8, 0)))
        problems = guard.check(self.folder, "win-x64")
        self.assertEqual(len(problems), 1)
        self.assertIn("older than the pinned 2.32.10", problems[0])

    def test_second_copy_fails(self):
        self.write("SDL2.dll", fake_pe())
        self.write("runtimes/win-x64/native/SDL2.dll", fake_pe(version=(2, 0, 8, 0)))
        self.assertIn("exactly one SDL2.dll, found 2", guard.check(self.folder, "win-x64")[0])

    def test_missing_sdl2_fails(self):
        self.assertIn("found 0", guard.check(self.folder, "win-x86")[0])

    def test_wrong_cpu_fails(self):
        self.write("SDL2.dll", fake_pe("x64"))
        self.assertEqual(guard.check(self.folder, "win-x86"), ["SDL2.dll is x64, expected x86"])

    def test_missing_version_resource_fails(self):
        self.write("SDL2.dll", fake_pe(version=None))
        self.assertEqual(guard.check(self.folder, "win-x64"), ["SDL2.dll has no FileVersion resource"])

    def test_non_windows_runtimes_have_no_bundled_sdl2_check(self):
        for rid in ("linux-x64", "linux-arm64", "osx-arm64"):
            self.assertEqual(guard.check(self.folder, rid), [])

    @unittest.skipUnless(NUGET_SDL2.is_file(), "Veldrid.SDL2 4.8.0 is not in the NuGet cache")
    def test_real_veldrid_sdl2_package_library_is_rejected(self):
        self.assertEqual(guard.BUILD_NATIVE.pe_file_version(NUGET_SDL2.read_bytes()), (2, 0, 8, 0))
        self.write("SDL2.dll", NUGET_SDL2.read_bytes())
        self.assertIn("older than the pinned", guard.check(self.folder, "win-x64")[0])

    def test_command_line_exit_codes(self):
        script = ROOT / "tools" / "check_release_natives.py"
        self.write("SDL2.dll", fake_pe(version=(2, 0, 8, 0)))
        failed = subprocess.run([sys.executable, "-I", str(script), "--rid", "win-x64", str(self.folder)], capture_output=True, text=True)
        self.assertEqual(failed.returncode, 1, failed.stderr)
        self.write("SDL2.dll", fake_pe())
        passed = subprocess.run([sys.executable, "-I", str(script), "--rid", "win-x64", str(self.folder)], capture_output=True, text=True)
        self.assertEqual(passed.returncode, 0, passed.stderr)


class ReleaseWorkflowContractTests(unittest.TestCase):
    """Every Windows package builds SDL2 from the pin and runs the guard before archiving."""

    def setUp(self):
        self.workflow = (ROOT / ".github" / "workflows" / "release.yml").read_text()

    def test_windows_runtimes_build_sdl2_from_source(self):
        for rid in ("win-x64", "win-x86", "win-arm64"):
            with self.subTest(rid=rid):
                entry = re.search(rf"- rid: {rid}\n\s+os: (\S+)\n\s+libraries: ([^\n]+)", self.workflow)
                self.assertIsNotNone(entry, f"no natives entry for {rid}")
                self.assertIn("sdl2", entry.group(2).split())

    def test_windows_publish_entries_use_the_built_natives(self):
        for rid in ("win-x64", "win-x86", "win-arm64"):
            with self.subTest(rid=rid):
                self.assertRegex(self.workflow, rf"\{{ rid: {rid}, os: windows-[^,]+, native: true \}}")

    def test_guard_runs_between_publish_and_archive(self):
        publish = self.workflow.index("- name: Publish\n")
        guard_step = self.workflow.index("tools/check_release_natives.py --rid")
        archive = self.workflow.index("- name: Archive (Windows)")
        self.assertLess(publish, guard_step)
        self.assertLess(guard_step, archive)


if __name__ == "__main__":
    unittest.main()
