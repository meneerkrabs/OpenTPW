"""Structural guardrails for the unintegrated UI-092 VM core; these do not execute the C# tests."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
STAFF = ROOT / "source/OpenTPW/World/Staff"


def body(text, signature):
    start = text.index("{", text.index(signature))
    depth = 1
    end = start + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start + 1:end - 1]


class StaffSpriteVmContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.state = (STAFF / "StaffSpriteVmState.cs").read_text()
        cls.vm = (STAFF / "StaffSpriteVm.cs").read_text()
        cls.programs = (STAFF / "StaffSpritePrograms.cs").read_text()
        cls.tests = (ROOT / "source/OpenTPW.Tests/StaffSpriteVmTests.cs").read_text()

    def test_complete_word_shape_and_deep_copy_owns_all_three_arrays(self):
        for constant in ("RegisterCount = 19", "StackCapacity = 20", "DrawCacheWordCount = 17"):
            self.assertIn(constant, self.state)
        copy = body(self.state, "public StaffSpriteVmState DeepCopy()")
        self.assertIn("MemberwiseClone()", copy)
        for field in ("Stack", "Registers", "DrawCache"):
            self.assertIn(f"copy.{field} = (uint[]){field}.Clone()", copy)
        self.assertIn("Enumerable.Range( 0, 70 )", self.tests)

    def test_partial_refresh_has_exact_register_and_scalar_allowlist(self):
        refresh = body(self.state, "public static void Refresh(")
        writes = re.findall(r"clone\[StaffSpriteRegister\.(\w+)\] =", refresh)
        self.assertEqual(["Kind", "Variant", "Slot", "Intro", "Hold", "Alpha", "SecondaryScale", "Heading", "Frame", "FrameCount", "Flags"], writes)
        scalar_writes = re.findall(r"clone\.(\w+) =", refresh)
        self.assertEqual(["ProgramTable", "CurrentPc", "OutputValid"], scalar_writes)
        self.assertIn("PortraitFlags( clone[StaffSpriteRegister.Flags] )", refresh)
        self.assertNotIn("source[StaffSpriteRegister.Flags]", refresh)

    def test_full_copy_has_only_the_proven_overrides(self):
        create = body(self.state, "public static StaffSpriteVmState Create(")
        self.assertIn("source.DeepCopy()", create)
        self.assertEqual(["Handle"], re.findall(r"clone\.(\w+) =", create))
        self.assertEqual(["X", "Y", "Z", "PrimaryScale", "Flags"], re.findall(r"clone\[StaffSpriteRegister\.(\w+)\] =", create))

    def test_template_replacement_does_not_replace_old_output_or_clock(self):
        apply = body(self.vm, "public static void ApplyTemplate(")
        for forbidden in ("OutputValid", "Due", "Interval", "RunState", "LastPc", "Registers", "ProgramTable", "Array.Clear"):
            self.assertNotIn(forbidden, apply)
        performance = body(self.vm, "public static void ApplyPerformance(")
        self.assertEqual(["Slot", "FrameCount"], re.findall(r"state\[StaffSpriteRegister\.(\w+)\] =", performance))

    def test_clock_and_dispatch_are_bounded_and_do_not_normalize_hybrid_depths(self):
        tick = body(self.vm, "internal static StaffSpriteTickResult Tick(")
        self.assertIn("if ( now <= state.Due )", tick)
        self.assertIn("state.Due = unchecked( now + state.Interval )", tick)
        self.assertIn("count < InstructionLimit", tick)
        self.assertIn("InstructionLimit = 50", self.vm)
        self.assertNotIn("while (", tick)
        self.assertNotIn("IfDepth", tick)
        self.assertNotIn("LoopDepth", tick)
        self.assertNotIn("CallDepth", tick)
        self.assertNotIn("PortraitFlags", self.vm)
        self.assertNotIn("0x50000", self.vm)

    def test_core_is_consumed_only_through_the_staff_sprite_state_and_stays_free_of_rendering(self):
        # round 11, package A: the world's sources (StaffSpriteState, StaffSpriteEvents) are the one consumer of the core
        for path in (ROOT / "source/OpenTPW").rglob("*.cs"):
            if path.name in ("StaffSpriteVm.cs", "StaffSpriteVmState.cs", "StaffSpritePrograms.cs", "StaffSpriteState.cs", "StaffSpriteEvents.cs", "StaffPlacement.cs", "StaffSprites.cs"):
                continue
            text = path.read_text(encoding="utf-8-sig")
            self.assertNotRegex(text, r"\b(?:StaffSpriteVm|StaffSpriteCloneCopy|StaffSpriteVmState)\b", str(path))
        for text in (self.vm, self.state, self.programs):
            for forbidden in ("StaffRenderer", "SpritePlayer.Steps", "Level.Current", "Texture", "DateTime", "Stopwatch", "ReadAllBytes", "DllImport"):
                self.assertNotIn(forbidden, text)

    def test_catalog_keeps_independent_oracles_and_named_cross_program_return_addresses(self):
        self.assertNotIn("StaffScripts.Get(", self.programs)
        self.assertNotIn("StaffScripts.At(", self.programs)
        self.assertNotIn("StaffScripts.PerformanceFrame(", self.programs)
        self.assertIn("StaffScripts.At(", self.tests)
        self.assertIn("StaffScripts.PerformanceFrame(", self.tests)
        self.assertIn("(Word >> 16)", self.state)
        self.assertIn("state.CurrentPc.Word", self.vm)
        self.assertIn("new StaffSpritePc( state.Stack[state.StackPointer] )", self.vm)
        self.assertIn("StaffSpriteProgram.Catch, 7", self.programs)
        self.assertIn("StaffSpriteProgram.Hold )", self.programs)
        self.assertNotIn("StaffSpriteOp.End )", self.programs)

    def test_cpu_tests_and_guard_are_wired_into_existing_ci(self):
        workflow = (ROOT / ".github/workflows/build.yml").read_text()
        self.assertIn('test_staff_sprite_vm_contract.py', workflow)
        self.assertIn("dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj", workflow)
        self.assertGreaterEqual(self.tests.count("[TestMethod]"), 35)
        self.assertIn("UI-092 remains open", (ROOT / "docs/STAFF-SPRITE-VM.md").read_text())


if __name__ == "__main__":
    unittest.main()
