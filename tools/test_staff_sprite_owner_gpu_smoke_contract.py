"""Bounded staff GPU-smoke structure guards, not C# execution or GPU evidence."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
SMOKE = "source/OpenTPW/FrontEnd/FrontEndSmokeTest.StaffOwners.cs"
PLAN = "source/OpenTPW/FrontEnd/FrontEndSmokeTest.cs"
RESOURCES = "source/OpenTPW/FrontEnd/FrontEndSmokeTest.StaffResources.cs"
GUIDE = "docs/STAFF-SPRITE-OWNER-GPU-SMOKE.md"


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


# Preserve offsets when masking comments and string/character literals. This is a
# deliberately small source guard, not a C# parser; braces in messages must not
# accidentally terminate a method under inspection.
LEXICAL = re.compile(
    r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\''
)


def mask(text, strings=True):
    def replace(match):
        token = match.group()
        if not strings and not token.startswith(("//", "/*")):
            return token
        return "".join("\n" if char == "\n" else " " for char in token)
    return LEXICAL.sub(replace, text)


def body(text, signature):
    masked = mask(text)
    start = masked.index("{", masked.index(signature))
    depth, end = 1, start + 1
    while depth:
        depth += (masked[end] == "{") - (masked[end] == "}")
        end += 1
    return text[start + 1:end - 1]


class StaffSpriteOwnerGpuSmokeContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.smoke = read(SMOKE)
        cls.code = mask(cls.smoke, strings=False)

    def assert_tokens(self, source, *tokens):
        for token in tokens:
            self.assertIn(token, source)

    def test_plan_is_in_existing_frontend_queue_and_cleanup_is_owned(self):
        plan = read(PLAN)
        self.assertEqual(1, body(plan, "private void Plan()").count("PlanStaffOwners();"))
        self.assertIn("RestoreStaffSmoke();", body(plan, "public void Dispose()"))
        staff_plan = body(self.code, "private void PlanStaffOwners()")
        self.assert_tokens(staff_plan, "Do(", "Wait(", "Enum.GetValues<StaffType>()",
                           "HireStaffSmoke( role )", "PlanStaffProgram(", "PlanStaffLoss(",
                           "PlanStaffRename();", "StaffEconomy.Fire( staffSmokeMember!.Id )",
                           "RestoreStaffSmoke();", "RequiredRoles = 5")
        wait = body(self.code, "private void WaitStaffOwner(")
        self.assert_tokens(wait, "steps.Enqueue(", "if ( done() ) return true;",
                           "Require( frame - started < maximumFrames")
        self.assertIn("int maximumFrames = 180", self.code)
        self.assertIn("private const int MaximumFrames = 3000;", plan)
        self.assertIn("private const int MaximumStaffSmokeFrames = 1500;", self.code)
        update = body(plan, "public void Update()")
        self.assert_tokens(update, "if ( staffSmokeActive ) Require( ++staffSmokeFrames <= MaximumStaffSmokeFrames",
                           "frame - speechFrames - loadFrames - staffSmokeFrames < MaximumFrames")

    def test_bounded_mode_reuses_real_start_and_keeps_default_plan(self):
        game = mask(read("source/OpenTPW/Client/Game.cs"), strings=False)
        self.assert_tokens(game, 'args.Contains( "--staff-owner-smoke" )',
                           "if ( staffOwnerSmoke && (!smoke || !startsFrontEnd || GameEditions.IsThemeParkInc) )",
                           'throw new ArgumentException( "--staff-owner-smoke requires --front-end --smoke-test with Theme Park World data." )',
                           "RunAutorun( flow, autorun, smoke, startIntro, staffOwnerSmoke )")
        self.assertEqual(2, game.count("new FrontEndSmokeTest( flow, staffOwnerSmoke )"))
        plan = read(PLAN)
        self.assertIn("public FrontEndSmokeTest( GameFlow flow, bool staffOwnerOnly = false )", plan)
        main = body(plan, "private void Plan()")
        self.assertIn("if ( staffOwnerOnly ) { PlanStaffOwnerOnly(); return; }", main)
        self.assertLess(main.index("PlanStaffOwnerOnly();"), main.index("PlanMaterialRendering();"))
        bounded = body(self.code, "private void PlanStaffOwnerOnly()")
        self.assert_tokens(bounded, 'flow.Menu.Selected.Level == "jungle"', 'Find( "enterPark" )',
                           "flow.InjectedInput = UiInput.Key( UiKeys.Accept )", "PlanFullSimulationStart(",
                           "ParkStartKind.FullSimulation", "ParkGameMode.FullSimulation", "!flow.BuildingPark",
                           "PlanStaffOwners();", "Device.WaitForIdle(); completed = true;", "GameFlow.Quit();",
                           "Full front-end smoke and original raster parity are separate gates.")
        self.assertLess(bounded.index("PlanFullSimulationStart("), bounded.index("PlanStaffOwners();"))
        self.assertLess(bounded.index("PlanStaffOwners();"), bounded.index("completed = true"))
        self.assertNotIn("PlanTexturePackSwitch(", bounded)

    def test_all_roles_have_actual_placement_and_labelled_program_requests(self):
        plan = body(self.code, "private void PlanStaffOwners()")
        for role, program in (("Handyman", "Sweep"), ("Mechanic", "Repair"),
                              ("Researcher", "Research"), ("Guard", "Catch")):
            self.assertIn(f"case StaffType.{role}:", plan)
            self.assertIn(f"StaffSpriteProgram.{program}", plan)
        self.assert_tokens(plan, "case StaffType.Entertainer:", '"performance-request"',
                           "StaffSpriteProgram.Stand", "StaffSpriteProgram.Walk",
                           "StaffSpriteProgram.Hold", "StaffSpriteProgram.Template24")
        hire = body(self.code, "private void HireStaffSmoke(")
        self.assert_tokens(hire, "hud.HireSelectedStaff()", "placement.IsValid(",
                           "placement.Click(", "StaffClickResult.Placed", "SourceTicket != null",
                           "Require( bank.Available && !bank.Multipart", "hud.OpenEmployeeWindow(",
                           "OriginalLayoutTables.EmployeeWindow.All.Select(", "Context.Canvas.Map( node.Rect, UiAnchor.Center )",
                           "StaffFrameEnvelope( bank.Frames )", "GuestRenderer.CellToEngine( level.OriginalPark!.Heightfield, x + 0.5f, y + 0.5f )",
                           "employeeBounds.All( bounds => StaffRectsSeparate( rect, bounds ) )", "StaffUiClear( rect )",
                           "ClearPlacement( rect )",
                           "StaffProjectFrameRect(", "pulled: true", "staffSmokeMember.IdleStamp = StaffEconomy.Turn + 100_000")
        self.assertNotIn("rect.Center", hire)  # Side gaps stay eligible; strict full-quad/UI checks decide.
        self.assertIn("ParkCameraMode.MaximumHeight", body(self.code, "private void PlanStaffOwners()"))
        program = body(self.code, "private void PlanStaffProgram(")
        self.assert_tokens(program, "member.BodySprite!.RequestTemplate(",
                           "Require( bank.TryPerformance(", "member.BodySprite!.RequestPerformance(",
                           'StaffEvidence( "request-fixture"', "RandomDrawAdded = false",
                           "state.OutputValid == 0", "state.CurrentPc.Instruction == 0", "BodySprite.PendingTemplate != 0",
                           "current != (StaffSpriteProgram)(272 + (int)expected - 256)",
                           "flow.Hud!.CloseEmployeeWindow()", "flow.Hud.OpenEmployeeWindow( staffSmokeMember! )",
                           "!StaffRenderer.ResolveRecord( record ).CanDraw",
                           "StabilizeStaffOwner( name )", "portrait: false", "portrait: true")

    def test_fixture_cannot_directly_schedule_publish_or_patch_vm_state(self):
        code = mask(self.smoke + read(RESOURCES))
        for token in (".Schedule(", ".Tick(", ".ApplyPending(", ".PresentStaffSprites(",
                      ".RefreshEmployeePortrait(", ".Publish(", ".AdvanceSimulation("):
            self.assertNotIn(token, code)
        self.assertNotRegex(code, r"\.\s*(?:CurrentPc|RestartPc|LastPc|Due|Interval|OutputValid|Registers|Stack|DrawCache)\s*=(?!=)")
        self.assertNotRegex(code, r"\[\s*StaffSpriteRegister\.\w+\s*\]\s*=(?!=)")
        self.assertNotRegex(code, r"\.\s*(?:Registers|Stack|DrawCache)\s*\[[^]]+\]\s*=(?!=)")
        self.assertNotIn(".NextWord(", code)
        self.assertNotIn("new Random(", code)

    def test_raw_snapshot_covers_all_seventy_words_without_reconstruction(self):
        start = self.code.index("internal static uint[] StaffWords(")
        end = self.code.index("private static uint[] StaffMatrixWords(", start)
        words = self.code[start:end]
        self.assertEqual([
            "OpaqueWord", "Handle", "CurrentPc.Word", "RestartPc.Word", "LastPc.Word",
            "ProgramTable", "RunState", "StackPointer", "Stack", "IfDepth", "CallDepth",
            "LoopDepth", "Due", "Interval", "Registers", "DrawCache", "OutputValid",
        ], re.findall(r"state\.(\w+(?:\.Word)?)", words))
        state = read("source/OpenTPW/World/Staff/StaffSpriteVmState.cs")
        sizes = [int(re.search(rf"const int {name} = (\d+)", state).group(1))
                 for name in ("StackCapacity", "RegisterCount", "DrawCacheWordCount")]
        self.assertEqual(70, 14 + sum(sizes))
        self.assert_tokens(words, ".Concat( state.Stack )", ".Concat( state.Registers )",
                           ".Concat( state.DrawCache )", ".Append( state.OutputValid )")

    def test_observation_includes_identity_clock_gameplay_camera_and_layout(self):
        observation = body(self.code, "private string StaffObservation()")
        self.assert_tokens(observation, "ComputeStateHash()", "CaptureRandomState()", "LastSample",
                           "Schedules = staffSmokeSchedules", "CloneYields = staffSmokeCloneYields", "Registry.Count", "Session",
                           "StaffMatrixWords( Camera.ViewMatrix )", "StaffMatrixWords( Camera.ProjMatrix )",
                           "Screen.PixelSize.X", "Screen.PixelSize.Y", "Context.Canvas.Width", "Context.Canvas.Height",
                           "WorldViewport", "global::Global.Render.MultisampledFramebuffer.Width",
                           "global::Global.Render.MultisampledFramebuffer.Height",
                           "StaffEconomy.Staff.Members.Select", "SourceTicket", "PersonAngle", "PendingTemplate", "PendingInterval",
                           "ReadSourceState()", "CloneTicket", "Epoch", "ReadCloneState()",
                           'screen.Name is "employee" or "employeeRename"', "element.Bounds", "element.Anchor", "label.Text()", "text.Text")
        stabilize = body(self.code, "private void StabilizeStaffOwner(")
        self.assert_tokens(stabilize, "WaitStaffOwner(", "GameSpeed.Paused", "frame == lastFrame",
                           "lastFrame = frame", "current == previous", "consecutive < 3", 'StaffEvidence( "observation"')

    def test_pair_captures_preserve_positive_and_negative_visibility_controls(self):
        pair = body(self.code, "private void PlanStaffPair(")
        self.assert_tokens(pair, "StaffPortraitRect()", "StaffWorldRect( staffSmokeMember! )",
                           "RequireIsolatedStaffRect( roi )", "before = staffResourceObservation ?? StaffObservation()",
                           'CaptureFrame( name + "-visible.png" )', 'CaptureFrame( name + "-hidden.png" )',
                           'CaptureFrame( name + "-restored.png" )', "figure.Visible = false", "renderer.Visible = false",
                           "restoreStaffVisibility = () => figure.Visible = wasVisible",
                           "restoreStaffVisibility = () => renderer.Visible = wasVisible",
                           "var changed = StaffPixelChanges( visible, hidden, roi )",
                           "Require( expectContribution ? changed > 0 : changed == 0",
                           "ExpectedContribution = expectContribution",
                           "portrait ? expectContribution ? StaffPortraitRect() : StaffFigure.ScreenRect( Context.Canvas )",
                           "StaffPixelChanges( visible, restored, roi ) == 0")
        self.assertIn("bool expectContribution = true", self.code)
        self.assertEqual(1, self.code.count("expectContribution: false"))
        self.assertNotIn("expectContribution:", body(self.code, "private void PlanStaffProgram("))
        self.assertGreaterEqual(pair.count("before == StaffObservation()"), 6)
        self.assertGreaterEqual(pair.count("finally { RestoreStaffVisibility(); }"), 2)
        self.assertIn("catch { RestoreStaffVisibility(); throw; }", pair)
        for token in (".Text =", ".Bounds =", ".Anchor =", ".SetPose(", ".Stack.Pop(", ".Stack.Push("):
            self.assertNotIn(token, pair)
        restore = body(self.code, "private void RestoreStaffSmoke()")
        self.assert_tokens(restore, "try { RestoreStaffVisibility(); }", "finally",
                           ".Registry.Scheduled -= StaffSmokeScheduled", "Economy.Speed = staffSmokeSpeed",
                           "camera.SetPose( pose.Target, pose.Height, pose.Yaw )")

    def test_world_roi_excludes_other_staff_ui_and_dangling_sprite(self):
        world = body(self.code, "private UiRect StaffWorldRect(")
        self.assert_tokens(world, "record.Route == StaffSpriteRoute.World", "StaffRenderer.ResolveRecord(",
                           "Require( resolved.CanDraw", "Camera.ViewMatrix", "Camera.ProjMatrix",
                           "resolved.Frame.Extents(", "StaffProjectFrameRect(", "Require( rect != null")
        isolated = body(self.code, "private void RequireIsolatedStaffRect(")
        self.assert_tokens(isolated, "StaffToolKind.None", "member != staffSmokeMember",
                           "var rect = StaffWorldRect( other )", "target.X - 2", "target.Right + 2",
                           "target.Y - 2", "target.Bottom + 2", "Require( StaffUiClear( target )",
                           'StaffEvidence( "isolation-failure", "world", details )',
                           'CaptureFrame( "staff-isolation-failure.png" )', "Screen = screen.Name", "Element = element.Id")
        start = self.code.index("private bool StaffUiClear(")
        end = self.code.index("internal static int StaffPixelChanges(", start)
        ui = self.code[start:end]
        self.assert_tokens(ui, "flow.Hud!.Stack.Screens", ".SelectMany( screen => screen.Elements.Where( element => element.Visible ) )",
                           "StaffRectsSeparate( target, element.ScreenRect( Context.Canvas ) )",
                           "other.Width <= 0 || other.Height <= 0", "other.Right < target.X - 2",
                           "other.X > target.Right + 2", "other.Bottom < target.Y - 2", "other.Y > target.Bottom + 2")
        plan = body(self.code, "private void PlanStaffOwners()")
        self.assert_tokens(plan, "staffSmokeBuyOpen = flow.Hud.BuyWindowOpen; flow.Hud.CloseBuyWindow()",
                           "if ( staffSmokeBuyOpen ) flow.Hud!.OpenBuyWindow()")
        pixels = body(self.code, "internal static int StaffPixelChanges(")
        self.assert_tokens(pixels, "a.Width == b.Width && a.Height == b.Height", "roi.Y", "roi.Bottom",
                           "roi.X", "roi.Right", ".AsSpan(", ".SequenceEqual(", "count++")

    def test_source_loss_uses_real_routes_and_normal_scheduler_outcomes(self):
        loss = body(self.code, "private void PlanStaffLoss(")
        self.assert_tokens(loss, "StaffSpriteProgram.Stand, 3", "StaffSpriteProgram.Performance0Loop",
                           "StaffSpriteProgram.Performance3Loop", "StaffPlacement!.PickUp( staffSmokeMember )",
                           "BodySprite.SourceTicket == null", "StaffPortrait.CloneTicket == clone",
                           "staffSmokeCloneYields == scheduleBefore", "GameSpeed.Normal", "GameSpeed.Paused",
                           "epoch = StaffPortrait.Epoch", "StaffPortrait.Epoch == epoch",
                           "record.Route == StaffSpriteRoute.Screen", "record.Route != StaffSpriteRoute.World",
                           "StaffSpriteDiagnostic.WorldRouteNotImplemented", "StaffFigure.Frame() == null",
                           "StaffPlacement!.Cancel()", "BodySprite.SourceTicket != source",
                           "flow.Hud!.PickUpEmployee()", "!flow.Hud.EmployeeWindowOpen",
                           "flow.Hud.EmployeePortrait == null")
        self.assertIn('else PlanStaffPair( label + "-undrawn", portrait: true, expectContribution: false )', loss)
        self.assertIn('PlanStaffPair( label + "-returned-world", portrait: false )', loss)
        self.assertIn('PlanStaffPair( label + "-returned-portrait", portrait: true )', loss)
        self.assertIn("if ( retainedPerformance ) PlanStaffPair(", loss)
        self.assertGreaterEqual(loss.count("StaffPortrait.Epoch == epoch"), 3)
        scheduled = body(self.code, "private void StaffSmokeScheduled(")
        self.assert_tokens(scheduled, "ticket.Identity.Member != staffSmokeMember",
                           "ticket.Kind != StaffSpriteOwnerKind.EmployeeClone", "StaffSpriteTickStatus.NotDue",
                           "result.Status == StaffSpriteTickStatus.Yielded", "staffSmokeCloneYields++")

    def test_rename_uses_captured_callbacks_and_pause_compares_complete_state(self):
        rename = body(self.code, "private void PlanStaffRename()")
        self.assert_tokens(rename, 'Screen.Find( "speedNormal" )!.Activate()', 'Screen.Find( "speedPaused" )!.Activate()',
                           "Require( StaffEconomy.Speed == GameSpeed.Paused", 'Find( "title" )!.Activate()',  'Find( "cancel" )!.Activate()',
                           'child.Find( "apply" )!.Activate()', "StaffPortrait.Epoch == epoch",
                           "StaffPortrait.Epoch == epoch + 1", '((UiTextField)child!.Find( "name" )!).Text',
                           'StabilizeStaffOwner( "paused-rename-modal" )', "pauseObservation = StaffObservation()",
                           'CaptureFrame( "staff-paused-rename.png" )', "pauseObservation == StaffObservation()",
                           "PausedOuterFrames = 6", "HireStaffSmoke( StaffType.Guard )", "captured = staffSmokeMember",
                           "flow.Hud!.SelectEmployee( other! )", "ReferenceEquals( flow.Hud.Stack.Top, child )",
                           "captured!.Name ==", "other!.Name == otherName", "ReferenceEquals( flow.Hud.Employee, other )",
                           "ReferenceEquals( StaffPortrait.BoundIdentity?.Member, other )", "StaffPortrait.Epoch == epoch + 2",
                           "flow.Hud.SelectEmployee( captured )", "StaffEconomy.Fire( other.Id )")
        self.assertNotRegex(rename, r"staffSmokeMember!?\.Name\s*=(?!=)")

    def test_warm_same_thread_measurements_exclude_readback_and_preserve_state(self):
        measure = body(self.code, "private void MeasureStaffObservers(")
        self.assert_tokens(measure, "before = StaffObservation()", "Environment.CurrentManagedThreadId",
                           "warm < 64", "new double[21]", "new long[21]", "GC.GetAllocatedBytesForCurrentThread()",
                           "Stopwatch.GetTimestamp()", "Stopwatch.Frequency", "before == StaffObservation()",
                           "MedianNanosecondsPerCall", "P95NanosecondsPerCall", "MedianBytesPerCall",
                           '"fresh-DrawRecord"', "body.DrawRecord", '"cached-record-ResolveRecord-including-layout-match"',
                           "StaffRenderer.ResolveRecord( record )", "StaffRenderer.PrepareWorld()",
                           "StaffRenderer.EmployeeFrame( clone )", "GC.KeepAlive(")
        for token in ("CaptureFrame(", ".WaitForIdle(", ".ReadPixels(", "Device.Map(", "PngWriter."):
            self.assertNotIn(token, measure)
        batches = body(measure, "for ( var batch = 0;")
        for token in ("StaffEvidence(", "StaffObservation()", "Log.", "File.", "JsonSerializer."):
            self.assertNotIn(token, batches)
        self.assertLess(measure.index("warm < 64"), measure.index("GC.GetAllocatedBytesForCurrentThread()"))

    def test_cpu_evidence_helpers_have_independent_snapshot_and_roi_checks(self):
        tests = read("source/OpenTPW.Tests/StaffOwnerGpuSmokeEvidenceTests.cs")
        self.assert_tokens(tests, "CompleteSmokeSnapshotIncludesAllSeventyWordsAndOwnsItsArrays",
                           "Enumerable.Range( 0, 70 )", "FrontEndSmokeTest.StaffWords( state )",
                           "state.Stack[19]++", "state.DrawCache[16]++", "state.Registers[18]++",
                           "FrontEndSmokeTest.StaffWords( null ).Length",
                           "PixelEvidenceRequiresAnRgbChangeInsideTheExpectedRoi",
                           "PixelEvidenceClipsFractionalBoundsAndRejectsMismatchedViewports",
                           "FrontEndSmokeTest.StaffPixelChanges(", "Assert.ThrowsException<InvalidOperationException>",
                           "PlacementAndFinalIsolationRejectOcclusionAndKeepTwoPixelClearance",
                           "FrontEndSmokeTest.StaffRectsSeparate(", "new UiRect( 142, 510, 20, 20 )",
                           "new UiRect( 143, 510, 20, 20 )")
        self.assertIn("These do not claim GPU execution.", tests)
        self.assertNotIn("CaptureFrame(", mask(tests))
        self.assertNotIn("Device.", mask(tests))

    def test_evidence_names_and_structural_guard_are_wired(self):
        self.assertIn('native-smoke-{GameLanguage.Current.Name.ToLowerInvariant()}-staff-owners.jsonl', self.smoke)
        evidence = body(self.code, "private void StaffEvidence(")
        self.assert_tokens(evidence, "File.AppendAllText( StaffEvidencePath", "Category = category",
                           "Stage = stage", "OuterFrame = frame", "Details = details",
                           "JsonSerializer.Deserialize<JsonElement>( observation ?? StaffObservation() )")
        self.assertIn('test_staff_sprite_owner_gpu_smoke_contract.py', read(".github/workflows/build.yml"))
        self.assertIn("StaffOwners.cs", read("tools/test_staff_sprite_vm_contract.py"))

    def test_exact_placement_geometry_shares_draw_projection_and_covers_large_frames(self):
        envelope = body(self.code, "internal static (float Left, float Right, float Top, float Bottom) StaffFrameEnvelope(")
        self.assert_tokens(envelope, "value.Extents( false", "value.Extents( true", "extents.Min( value => value.X0 )",
                           "extents.Max( value => value.X1 )", "extents.Max( value => value.Top )", "extents.Min( value => value.Bottom )")
        project = body(self.code, "internal static UiRect? StaffProjectFrameRect(")
        self.assert_tokens(project, "GuestRenderer.PullTowardsEye(", "right * (x * share)", "up * (y * share)",
                           "if ( !(clip.W > 0) ) return null", "float.IsFinite( point.X ) && float.IsFinite( point.Y )",
                           "if ( corners.Any( point => point == null ) ) return null")
        tests = read("source/OpenTPW.Tests/StaffOwnerGpuSmokeEvidenceTests.cs")
        self.assert_tokens(tests, "RealFrameEnvelopeRejectsOverlapThatTheOldPixelHeuristicMissed",
                           "SharedWorldProjectionPreservesPulledPixelBoundsAndRejectsBehindOrNonfinitePoints",
                           "actual.Width > oldGuess.Width && actual.Height > oldGuess.Height",
                           "Assert.IsFalse( FrontEndSmokeTest.StaffRectsSeparate( actual, window )")

    def test_d06_runs_once_on_handyman_stand_and_keeps_one_observation(self):
        resources = read(RESOURCES)
        plan = body(self.code, "private void PlanStaffOwners()")
        self.assertIn("if ( role == StaffType.Handyman ) PlanStaffTextureReload();", plan)
        self.assertLess(plan.index('"stand", StaffSpriteProgram.Stand'), plan.index("PlanStaffTextureReload();"))
        self.assertLess(plan.index("PlanStaffTextureReload();"), plan.index('"walk-request"'))
        self.assertEqual(1, resources.count("staffResourceObservation = StaffObservation();"))
        update = body(read(PLAN), "public void Update()")
        self.assertEqual(2, update.count("RequireStaffResourceObservation();"))
        self.assert_tokens(resources, "staffResourceObservation == StaffObservation()", "restoreStaffResources = lease.Restore;",
                           "TexturePackSwitch.Current == null", "RequireIsolatedStaffRect( world )",
                           "resolved.CanDraw", "clone.Route == StaffSpriteRoute.Screen",
                           "OutputValid != 0", "StaffSpriteDiagnostic.TextureUnavailable", "StaffFigure.Frame() == null")
        self.assertLess(resources.index("restoreStaffResources = lease.Restore;"), resources.index("lease!.MakeUnavailable();"))
        for state in ("available", "unavailable", "recovered"):
            for route in ("world", "portrait"):
                self.assertIn(f'"d06-{state}-{route}"', resources)
        self.assertEqual(2, resources.count("expectContribution: false"))
        self.assertEqual(3, resources.count("fixedRoi: () => world"))
        self.assertEqual(3, resources.count("fixedRoi: () => portrait"))
        self.assert_tokens(resources, "StaffPixelChanges( worldBefore, capture, world ) > 0",
                           "StaffPixelChanges( portraitBefore, capture, portrait ) > 0",
                           "StaffPixelChanges( worldBefore, capture, world ) == 0",
                           "StaffPixelChanges( portraitBefore, capture, portrait ) == 0",
                           'StaffEvidence( "resource-fault-fixture", "d06-unavailable"',
                           'StaffEvidence( "resource-reload", "d06-recovered"',
                           'StaffEvidence( "resource-cleanup", "d06-scratch-retired"')
        self.assertNotIn("StabilizeStaffOwner(", resources)  # Never rebaseline after injecting a fault.

    def test_d06_targets_one_actual_atlas_and_rebinds_all_eight_aliases(self):
        resources = read(RESOURCES)
        renderer = read("source/OpenTPW/World/Staff/StaffRenderer.cs")
        self.assert_tokens(renderer, "AtlasPackKeys => sprites.Atlases.Take( textures.Count )",
                           ".Select( atlas => atlas.PackKey ).ToArray()",
                           "internal void ReloadAtlas( int index )", "(uint)index >= (uint)textures.Count",
                           "ReloadAtlases( new[] { sprites.Atlases[index].PackKey } )")
        unique = body(resources, "internal static string StaffUniqueAtlasKey(")
        self.assert_tokens(unique, "TexturePack.RelativeFileName( keys[selected] )",
                           "keys.Count( value => TexturePack.RelativeFileName( value ) == key ) == 1")
        self.assert_tokens(resources, "StaffUniqueAtlasKey( renderer.AtlasPackKeys, atlas )",
                           "Enumerable.Range( 0, 8 )", "Math.Min( slot, count - 1 )",
                           "Aliases.All( slot => ReferenceEquals( bindings[slot], scratch ) )")
        bind = body(resources, "private void BindCurrent()")
        self.assert_tokens(bind, "renderer.OwnedAssets.OfType<Texture>().ToArray()",
                           "current.All( texture => !texture.NativeTexture.IsDisposed )", 'material.Set( "Color", bindings )',
                           "material.GetResourceSets()", "bindings.Length == 8", "!ReferenceEquals( texture, scratch )")
        for token in ("TexturePackSwitch.Begin(", "PackChanged", "GetField(", "System.Reflection", "File.Write", "Texture.Replace("):
            self.assertNotIn(token, mask(resources))

    def test_d06_cleanup_handles_partial_reload_without_double_disposal(self):
        resources = read(RESOURCES)
        fault = body(resources, "public void MakeUnavailable()")
        self.assert_tokens(fault, "Device.ResourceFactory.CreateTexture(", "Device.ResourceFactory.CreateTextureView( faultNative )",
                           "catch { DisposeFault(); throw; }", "Require( faultNative.IsDisposed", "Device.WaitForIdle()",
                           'material.Set( "Color", bindings )', "quarantineSets = material.GetResourceSets().ToArray()")
        self.assertLess(fault.index('material.Set( "Color", bindings )'), fault.index("original.NativeTexture = faultNative"))
        reload = body(resources, "public void Reload()")
        self.assertLess(reload.index("original.NativeTexture = originalNative"), reload.index("renderer.ReloadAtlas( Atlas )"))
        cleanup = body(resources, "public void Restore()")
        self.assert_tokens(cleanup, "ReferenceEquals( Current, original )", "original.NativeTexture = originalNative",
                           "BindCurrent()", "!ReferenceEquals( Current, original ) && Asset.All.Contains( original )",
                           "original.ReleaseOwned()", "scratchRetirementQueued = true", "scratch.ReleaseOwned()")
        self.assertLess(cleanup.index("BindCurrent()"), cleanup.index("scratch.ReleaseOwned()"))
        self.assertNotIn("reloadSucceeded", resources)
        dispose = body(resources, "internal static void StaffDisposeOnce(")
        self.assertLess(dispose.index("disposed = true"), dispose.index("resource.Dispose()"))
        self.assert_tokens(resources, "StaffDisposeOnce( faultView, ref faultViewDisposed )",
                           "StaffDisposeOnce( faultNative, ref faultNativeDisposed )", "GameFlow.Quit(); throw;")
        verify = body(resources, "public void VerifyReload()")
        self.assert_tokens(verify, "originalNative.IsDisposed", "AtlasViewReleased( original )",
                           "oldSets.Concat( quarantineSets ).All( set => set.IsDisposed )",
                           "otherObjects.SequenceEqual( ResourceObjects( others ) )", "AssetsLive( others )")
        self.assertIn("RestoreStaffResources();", body(self.code, "private void RestoreStaffSmoke()"))

    def test_d06_cpu_helpers_cover_faults_but_do_not_claim_gpu_execution(self):
        tests = read("source/OpenTPW.Tests/StaffOwnerGpuSmokeEvidenceTests.cs")
        self.assert_tokens(tests, "AtlasSlotsCoverEveryPaddedAliasForOneThroughEightLoadedTextures",
                           "TargetedReloadRequiresOneNormalizedLoadedPackKey",
                           "UnavailableEvidenceKeepsThePreviousValidRoiWithoutResolvingAMissingFrame",
                           "FaultHandleCleanupDisposesPartialAndAlreadyDisposedAllocationsOnlyOnce",
                           "count <= 8", "9 - count", "new[] { -1, 0, 9 }", "Throw = true")
        self.assertNotIn("Device.", mask(tests))
        guide = read(GUIDE)
        for token in ("resource-fault-fixture", "resource-reload", "resource-cleanup", "unique", "all eight"):
            self.assertIn(token, guide)

    def test_guide_keeps_source_structure_and_host_evidence_separate(self):
        guide = read(GUIDE)
        for text in (
            "No SDK build, C# execution,", "GPU run, capture inspection, or performance result",
            "passing Python guard establishes only source structure",
            "not a\ncomparison with the original executable",
            "not proof that natural repair", "separate evidence route",
            "Required cases\nmust not be silently skipped",
            "exact tested commit and artifact", "original assets outside the committed evidence",
        ):
            self.assertIn(" ".join(text.split()), " ".join(guide.split()))
        self.assertIn("STAFF-SPRITE-OWNER-GPU-SMOKE.md", read("docs/STAFF-SPRITE-OWNER-ACCEPTANCE.md"))

    def test_guide_documents_full_state_and_pair_isolation_requirements(self):
        guide = read(GUIDE)
        for text in (
            "all 70 raw VM words", "P-to-H idempotence", "clone\nepoch",
            "gameplay hash", "random-stream state", "camera state",
            "viewport/drawable configuration", "other staff cannot contribute pixels",
            "toggle only the selected portrait", "Do not remove/rebuild the window",
            "restored on both success and failure", "SET 0x1200",
            "WorldRouteNotImplemented", "entertainer case is\nseparate",
        ):
            self.assertIn(" ".join(text.split()), " ".join(guide.split()))

    def test_guide_does_not_turn_micro_measurements_into_performance_acceptance(self):
        guide = read(GUIDE)
        for text in (
            "same thread after warmup", "draw-record creation", "layout resolution",
            "StaffRenderer.PrepareWorld", "StaffRenderer.EmployeeFrame",
            "outside timed loops", "not an FPS benchmark", "population-scaling test",
            "GPU texture-reload/disposal", "saved VM/window-history persistence",
        ):
            self.assertIn(" ".join(text.split()), " ".join(guide.split()))


if __name__ == "__main__":
    unittest.main()
