"""UI-091 source integration guards. These do not run or replace the C# regression suite."""
from pathlib import Path
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


class LocatorVisitorContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.hud = read("source/OpenTPW/Hud/ParkHud.LocatorPanel.cs")
        cls.order = cls.hud.split("internal static class LocatorOrder", 1)[1]

    def test_radio_uses_the_guest_simulations_existing_pool(self):
        self.assertIn("LocatorOrder.NextVisitor( level.Guests?.Guests ?? Array.Empty<Guest>(), level.Guests?.Things, current )", self.hud)
        self.assertNotIn("level.Things", self.hud)
        self.assertIn("Locator.Press( role, NextLocatorActor )", self.hud)

    def test_valid_candidates_follow_pool_links_without_visibility_or_id_chronology(self):
        candidates = body(self.order, "public static IEnumerable<int> VisitorIds(")
        for required in ("things.UsedNewestFirst()", "slot.Model == SaveThingModel.Guest", "!things.IsQueued( slot.Id )",
                         "!things.IsHeld( slot.Id )", "byId.TryGetValue( slot.Id, out var guest )", "guest.State != GuestState.Gone"):
            self.assertIn(required, candidates)
        self.assertNotIn("IsVisible", candidates)
        self.assertNotIn("IsFinite", candidates)
        self.assertNotIn("OrderBy", candidates.split("// Host compatibility only:")[0])
        next_actor = body(self.order, "public static int NextVisitor(")
        self.assertIn("afterCurrent = id == current", next_actor)
        self.assertIn("return 0;", next_actor)
        self.assertNotIn("id >", next_actor)
        self.assertNotIn("id <", next_actor)

    def test_fallback_keeps_first_claim_and_never_reintroduces_rejected_used_slots(self):
        candidates = body(self.order, "public static IEnumerable<int> VisitorIds(")
        self.assertIn("guest.Id > 0", candidates)
        self.assertIn("byId.TryAdd( guest.Id, guest )", candidates)
        fallback = candidates.split("// Host compatibility only:")[1]
        self.assertIn("guest.State != GuestState.Gone", fallback)
        self.assertIn("!(things?.IsUsed( guest.Id ) ?? false)", fallback)
        self.assertIn("OrderBy( guest => guest.Id )", fallback)
        for mutation in (".Allocate(", ".Release(", ".QueueDestroy(", ".Restore(", ".Sort(", "World.", "random"):
            self.assertNotIn(mutation, candidates)

    def test_frame_icon_is_local_and_has_the_same_rect_anchor_and_frame(self):
        self.assertIn('new FrameIcon { Id = "locatorState", Model = "staffstat", Frame = () => LocatorStateFrame, Bounds = strip.Children.Single( node => node.Id == 0x148 ).Rect }, locatorStripElements', self.hud)
        build = body(self.hud, "private void BuildLocatorPanels()")
        self.assertIn("element.Anchor = UiAnchor.BottomLeft", build)
        self.assertIn("element.Visible = false", build)
        draw = read("source/OpenTPW/Hud/ParkHud.EmployeeWindow.cs").split("private sealed class FrameIcon", 1)[1]
        self.assertIn("frame >= 0", draw)
        self.assertIn("frame < model.Frames.Count", draw)
        self.assertIn("context.DrawModel( Model, frame, ScreenRect( context.Canvas ) )", draw)
        mapping = self.hud.split("internal static int StaffStateFrame", 1)[1].split("};", 1)[0]
        self.assertIn("_ => 9", mapping)

    def test_camera_boundary_and_distinct_staff_loss_policy_are_retained(self):
        position = body(self.hud, "private bool TryLocatorActorPosition(")
        for required in ("level.OriginalPark is not", "!float.IsFinite( cell.X )", "!float.IsFinite( cell.Y )", "!float.IsFinite( cell.Z )"):
            self.assertIn(required, position)
        follow = body(self.hud, "private void FollowLocatorActor()")
        self.assertIn("Camera.Mode is not ParkCameraMode camera || !TryLocatorActorPosition", follow)
        update = body(self.hud, "private void UpdateLocatorPanels()")
        self.assertIn("? !TryLocatorActorPosition( out _ ) : !StaffMembers.Any", update)

    def test_declared_regressions_cover_real_radio_and_draw_paths(self):
        visitors = read("source/OpenTPW.Tests/LocatorVisitorOrderTests.cs")
        icons = read("source/OpenTPW.Tests/LocatorStateIconTests.cs")
        for required in ("UiButton", ".Clicked!()", "GuestState.Using", "GuestState.HeldByGuard", "CaptureSnapshot()",
                         "RestoreSnapshot(", "RandomState", "QueueDestroy(", "Allocate(", "float.NaN", "CloseLocator()"):
            self.assertIn(required, visitors)
        for required in ("icon.Draw(", "context.Batch.Draws", "model.GetFrame( 9 )", "StaffState.PickedUp", "OpenVisitorLocator()", "CloseLocator()"):
            self.assertIn(required, icons)


if __name__ == "__main__":
    unittest.main()
