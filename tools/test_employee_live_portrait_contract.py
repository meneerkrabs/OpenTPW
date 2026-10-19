"""Source integration guards for the CPU source/clone vertical slice; not C# execution or visual proof."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
def source(relative):
    return (ROOT / relative).read_text(encoding="utf-8-sig")
def body(text, signature):
    start = text.index("{", text.index(signature))
    depth, end = 1, start + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start + 1:end - 1]

class EmployeeLivePortraitContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.renderer = source("source/OpenTPW/World/Staff/StaffRenderer.cs")
        cls.hud = source("source/OpenTPW/Hud/ParkHud.EmployeeWindow.cs")
        cls.owner = source("source/OpenTPW/Hud/EmployeePortraitOwner.cs")
        cls.presentation = source("source/OpenTPW/World/Staff/StaffSpritePresentation.cs")

    def test_world_and_hud_consume_same_values_without_any_animation_authority(self):
        prepare = body(self.renderer, "internal int PrepareWorld()")
        for token in ("sourceRecord?.Invoke( member )", "ResolveRecord( record, yaw )", "record.PositionX", "record.Flags & 0x200"):
            self.assertIn(token, prepare)
        for forbidden in ("SpritePlayer", "ClipKey", "MemberView", "PublishedFrame", ".Tick(", "SelectClip(", "StepLength", "WorldPass"):
            self.assertNotIn(forbidden, self.renderer)
        render = body(self.renderer, "protected override void OnRender()")
        self.assertLess(render.index("PrepareWorld()"), render.index("commandList.UpdateBuffer"))

    def test_employee_resources_use_current_texture_bank_layout_and_session_without_world_visibility(self):
        read = body(self.renderer, "internal UI.Original.UiSpriteFrame? EmployeeFrame(")
        self.assertIn("ResolveRecord( record )", read)
        self.assertIn("StaffSpriteRoute.Screen", read)
        self.assertIn("textures[result.Atlas].NativeTexture", read)
        resolve = body(self.renderer, "internal StaffSpriteFrameResolution ResolveRecord(")
        for token in ("Level.Current", "Level.StaffRenderer, this", "Entity.All.Contains( this )", "record.Session != session()", ".Matches( sprites.Atlases[resolved.Atlas] )", "HasAtlas( resolved.Atlas )"):
            self.assertIn(token, resolve)
        for forbidden in ("CanRender", "Visible", "RenderPass", "Published", "member.State", ".Tick("):
            self.assertNotIn(forbidden, read + resolve)
        self.assertIn("IsDisposed: false", self.renderer)

    def test_strict_resolver_has_no_permissive_frame_fallback_and_world_clone_is_explicit(self):
        resolver = source("source/OpenTPW/World/Staff/StaffSpriteDrawRecord.cs")
        for token in ("WorldRouteNotImplemented", "UnsupportedTransform", "UnsupportedResource", "InvalidFrame", "MissingSlot", "record.Route == StaffSpriteRoute.Screen ? 0 : cameraYaw"):
            self.assertIn(token, resolver)
        for forbidden in (".GetFrame(", "AnimationFor(", "%", "Math.Clamp", "StaffMember", "Texture"):
            self.assertNotIn(forbidden, resolver)

    def test_hud_has_no_hire_fallback_and_immediate_full_events_are_explicit(self):
        callback = self.hud.split("private UiSpriteFrame? EmployeePortraitFrame() =>", 1)[1].split(";", 1)[0]
        self.assertIn("employeePortrait?.DrawRecord", callback)
        for token in ("employeePortraitStart", "employeePortraitKey", "PortraitFrame( chosen", "new SpritePlayer"):
            self.assertNotIn(token, self.hud)
        self.assertIn("FullRefreshEmployee();", body(self.hud, "public bool OpenEmployeeWindow("))
        self.assertIn("FullRefreshEmployee();", body(self.hud, "public bool SelectEmployee("))
        rename = source("source/OpenTPW/Hud/ParkHud.EmployeeRename.cs")
        self.assertIn("FullRefreshEmployee();", body(rename, "void Apply()"))
        self.assertIn("screen.Removed = EmployeeScreenRemoved", self.hud)
        hire = source("source/OpenTPW/Hud/ParkHud.Staff.cs")
        self.assertIn("PortraitFrame( chosen.Type, chosen.Variant, Time.Now - portraitStart )", hire)
        self.assertNotIn("EmployeeFrame", hire)

    def test_full_partial_and_scheduler_ownership_are_separate(self):
        full = body(self.owner, "public void FullRefresh(")
        self.assertLess(full.index("RemoveClone()"), full.index("Registry.Allocate"))
        self.assertIn("StaffSpriteCloneCopy.Create", full)
        partial = body(self.owner, "public void PartialRefresh()")
        self.assertIn("StaffSpriteCloneCopy.Refresh", partial)
        for forbidden in ("Allocate", "CreateSource", ".Tick(", "ApplyTemplate"):
            self.assertNotIn(forbidden, partial)
        self.assertNotIn(".Tick(", self.owner)

    def test_outer_phase_is_once_after_simulation_partial_before_heading_even_under_modal(self):
        flow = source("source/OpenTPW/Client/GameFlow.cs")
        phase = "Level.PresentStaffSprites( Hud.RefreshEmployeePortrait, Time.Delta )"
        self.assertEqual(1, flow.count(phase))
        self.assertLess(flow.index("Level.Update();"), flow.index(phase))
        self.assertLess(flow.index(phase), flow.index("UpdateAutosave( Level, Hud )"))
        present = body(self.presentation, "public void Present(")
        self.assertLess(present.index("partialRefresh?.Invoke()"), present.index("body.Publish("))
        refresh = body(self.hud, "internal void RefreshEmployeePortrait()")
        self.assertIn("EmployeeWindowOpen", refresh)
        self.assertNotIn("Stack.Top", refresh)
        simulation = source("source/OpenTPW/World/Level.Simulation.cs")
        self.assertEqual(2, simulation.count("StaffSprites?.Schedule()"))
        self.assertNotIn("PresentStaffSprites", simulation)
        level = source("source/OpenTPW/World/Level.cs")
        self.assertIn("StaffSprites?.Dispose();", body(level, "public void Dispose()"))

if __name__ == "__main__":
    unittest.main()
