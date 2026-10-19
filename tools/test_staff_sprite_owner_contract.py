"""UI-092 integration structure guards. These do not execute C#, draw a GPU frame or prove native raster fidelity."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def body(text, signature):
    start = text.index("{", text.index(signature))
    depth, end = 1, start + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start + 1:end - 1]


class StaffSpriteOwnerContract(unittest.TestCase):
    def test_cpu_metadata_precedes_renderer_and_final_restore_precedes_reconstruction(self):
        staff = read("source/OpenTPW/World/Level.Staff.cs")
        setup = body(staff, "private void SetupStaff()")
        self.assertLess(setup.index("StaffSpriteSet.Load"), setup.index("if ( OriginalPark != null )"))
        self.assertLess(setup.index("StaffSpritePresentation"), setup.index("new StaffWorldSimulation"))
        self.assertIn("ready: false", setup)
        level = read("source/OpenTPW/World/Level.cs")
        self.assertLess(level.index("RestoreResumeSimulation( resume )"), level.index("InitializeStaffSprites();"))
        final = read("source/OpenTPW/World/Level.StaffSprites.cs")
        self.assertLess(final.index("StaffPlacement?.Reconcile()"), final.index("StaffSprites?.Reconstruct("))
        self.assertLess(final.index("StaffWorld?.Synchronize()"), final.index("StaffSprites?.Reconstruct("))

    def test_load_validation_is_before_live_economy_and_random_stream_commit(self):
        runtime = body(read("source/OpenTPW/Economy/ParkEconomyRuntime.cs"), "internal WorldRandomState? Load(")
        self.assertLess(runtime.index("validate?.Invoke( loaded )"), runtime.index("Economy.EventRaised -= LogEvent"))
        self.assertLess(runtime.index("validate?.Invoke( loaded )"), runtime.index("Economy = loaded"))
        streams = body(read("source/OpenTPW/World/ParkWorldStreams.cs"), "internal void LoadPark(")
        self.assertLess(streams.index("Park.Load( path, validate )"), streams.index("Guests?.AttachWorld"))
        level = body(read("source/OpenTPW/World/Level.cs"), "public void LoadPark(")
        self.assertIn("detached.RestoreSnapshot( Things.CaptureSnapshot() )", level)
        self.assertIn("candidate.Staff.AttachThings( detached )", level)
        self.assertLess(level.index("candidate.Staff.AttachThings( detached )"), level.index("StaffPlacement?.Abandon()"))
        tests = read("source/OpenTPW.Tests/StaffSpriteLevelIntegrationTests.cs")
        for token in ("ActualLevelLoadThingConflictPreservesEconomyStreamsPoolSourcesAndClone", "scene.Level.LoadPark( file )", "guestsWorld", "scriptsWorld", "CaptureRandomState", "CaptureSnapshot", "cloneWords"):
            self.assertIn(token, tests)

    def test_pending_requests_clear_before_absent_return_and_moving_queries_restart(self):
        source = read("source/OpenTPW/World/Staff/StaffBodySprite.cs")
        apply = body(source, "public void ApplyPending()")
        self.assertLess(apply.index("PendingTemplate = PendingInterval = 0"), apply.index("if ( !TryResolve"))
        self.assertLess(apply.index("if ( request != 0 )"), apply.index("StaffSpriteVm.ApplyInterval"))
        moving = body(source, "public void Moving(")
        self.assertIn("!TryResolve( out var state ) || state.RestartPc", moving)
        self.assertNotIn("PendingTemplate ==", moving)
        self.assertIn("owner.Detach( this )", source)
        for token in ("Time.", "new Random", ".NextWord(", "System.Random", "SpritePlayer"):
            self.assertNotIn(token, source)

    def test_smoke_harness_presents_once_after_its_outer_stage_not_each_fixed_step(self):
        smoke = read("source/OpenTPW/Client/ParkResumeSmokeTest.cs")
        fixture = read("source/OpenTPW/Client/ParkResumeSmokeTest.Fixture.cs")
        self.assertEqual(1, smoke.count(".PresentStaffSprites("))
        self.assertNotIn(".PresentStaffSprites(", fixture)
        update = body(smoke, "private void Update()")
        self.assertLess(update.index("case Stage.Done:"), update.index(".PresentStaffSprites("))
        self.assertIn("flow.Hud?.RefreshEmployeePortrait()", update)

    def test_runtime_owners_are_not_serialized_or_added_to_gameplay_hash(self):
        staff = read("source/OpenTPW/Economy/ParkStaff.cs")
        self.assertRegex(staff, r"\[System\.Text\.Json\.Serialization\.JsonIgnore\]\s+public StaffBodySprite\? BodySprite")
        for path in ("source/OpenTPW/Economy/ParkSaveFile.cs", "source/OpenTPW/World/WorldStateHash.cs"):
            for token in ("StaffSpritePresentation", "StaffSpriteVmState", "SourceTicket", "CloneTicket", "BodySprite.Pending"):
                self.assertNotIn(token, read(path))
        presentation = read("source/OpenTPW/World/Staff/StaffSpritePresentation.cs")
        self.assertIn("EpochMilliseconds + GuestSimulation.BodySpriteSampleAtTick( tick ) - GuestSimulation.BodySpriteSampleAtTick( EpochHostTick )", presentation)
        self.assertIn("body.EnsureInitialized( reconstructed: true )", presentation)

    def test_actual_route_and_event_tape_tests_remain_substantive(self):
        level = read("source/OpenTPW.Tests/StaffSpriteLevelIntegrationTests.cs")
        for token in ("scene.Level.AdvanceSimulation(", "scene.Level.PresentStaffSprites(", "scene.Level.LoadPark(", "scene.Level.Dispose()", "ComputeStateHash()", "[DataRow( false )]", "[DataRow( true )]"):
            self.assertIn(token, level)
        edge = read("source/OpenTPW.Tests/StaffSpriteAcceptanceEdgeTests.cs")
        for token in ("SourceBefore", "SourceAfter", "CloneBefore", "CloneAfter", "StaffSpriteTicket?", '"RenameConfirm"', '"Reopen"', '"Delete"', "new[] { 126, 252, 252 }", "rig.Figure.Draw(", "renderer.PrepareWorld()", "uint.MaxValue"):
            self.assertIn(token, edge)

    def test_all_65_vectors_map_to_existing_test_methods_and_keep_execution_qualified(self):
        mapping = read("docs/STAFF-SPRITE-OWNER-ACCEPTANCE.md")
        expected = {f"{prefix}{index:02}" for prefix, count in (("O", 12), ("R", 3), ("L", 6), ("Q", 4), ("E", 10), ("N", 6), ("P", 8), ("D", 8), ("M", 4), ("S", 2), ("G", 2)) for index in range(1, count + 1)}
        actual = re.findall(r"^\|\s*([ORLQENPDMSG]\d{2})\s*\|", mapping, re.M)
        self.assertEqual(65, len(actual))
        self.assertEqual(expected, set(actual))
        tests = "\n".join(path.read_text(encoding="utf-8-sig") for path in (ROOT / "source/OpenTPW.Tests").glob("*.cs"))
        declared = set(re.findall(r"public\s+(?:async\s+)?(?:void|Task)\s+(\w+)\s*\(", tests))
        for row in mapping.splitlines():
            if not re.match(r"\|\s*[ORLQENPDMSG]\d{2}\s*\|", row):
                continue
            methods = re.findall(r"`\w+\.(\w+)`", row.split("|")[2])
            self.assertTrue(methods, row)
            for method in methods:
                self.assertIn(method, declared, f"Missing method {method}: {row}")
        for token in ("pending", "synthetic", "visual", "SS01"):
            self.assertIn(token.lower(), mapping.lower())


if __name__ == "__main__":
    unittest.main()
