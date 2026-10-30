"""Source integration guards for the UI-092 CPU tests; not a substitute for running them."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def source(relative):
    return (ROOT / relative).read_text(encoding="utf-8-sig")


def body(text, signature):
    start = text.index("{", text.index(signature))
    depth = 1
    end = start + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start + 1:end - 1]


class EmployeeLivePortraitContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.renderer = source("source/OpenTPW/World/Staff/StaffRenderer.cs")
        cls.hud = source("source/OpenTPW/Hud/ParkHud.EmployeeWindow.cs")

    def test_real_world_path_uses_cpu_preparation_before_gpu_submission(self):
        render = body(self.renderer, "protected override void OnRender()")
        self.assertLess(render.index("PrepareWorld()"), render.index("if ( count == 0 )"))
        self.assertLess(render.index("PrepareWorld()"), render.index("commandList.UpdateBuffer"))
        prepare = body(self.renderer, "internal int PrepareWorld()")
        ordered = ["rendererPass++", "if ( !CanRender )", "new MemberView", "if ( !CanPlaceMember",
                   "DrawPoint( member )", "Select( member, memberView",
                   "AddSprite( ref count, chosen", "memberView.Published =", "DrawnStaff++"]
        offsets = [prepare.index(token) for token in ordered]
        self.assertEqual(sorted(offsets), offsets)
        self.assertEqual(1, self.renderer.count("memberView.Published ="))

    def test_selection_retains_existing_mutation_order_and_describe_cannot_publish(self):
        select = body(self.renderer, "private Selection? Select(")
        ordered = ["StaffSpriteRules.HasBodySprite", "member.Sprite.Output", "FrameFor(", "return new Selection"]
        offsets = [select.index(token) for token in ordered]
        self.assertEqual(sorted(offsets), offsets)
        for mutation in ("Tick(", "Queue", "Restart(", "Create(", "Delete(", "Apply"):
            self.assertNotIn(mutation, select)
        describe = body(self.renderer, "internal (int Atlas, int Frame, bool Mirror, int Slot, int Step)? Describe(")
        self.assertNotIn("Published", describe)
        self.assertNotIn("PrepareWorld", describe)

    def test_accessor_is_read_only_and_uses_literal_yaw_zero(self):
        read = body(self.renderer, "internal UI.Original.UiSpriteFrame? EmployeeFrame(")
        for token in ["Describe(", "Select(", "DrawPoint(", ".Tick(", ".Restart(", ".SetInterval(", "PendingInterval", "new MemberView", "Random"]:
            self.assertNotIn(token, read)
        self.assertIn("output.HeadingX, output.HeadingY, 0f, atlas.DirectionsOf", read)
        for token in ["!CanRender", "Level.Current", "Level.StaffRenderer, this", "Entity.All.Contains( this )",
                      "!CanPlaceMember", "StaffSpriteRules.HasBodySprite", "output.Owner, Level", "output.Member, member",
                      "output.ThingId != member.ThingId", "output.WorldPass != Level.RenderPass",
                      "output.RendererPass != rendererPass", "output.Assets != assetGeneration", "!HasAtlas( output.Atlas )",
                      "ReferenceEquals( list[index], member )"]:
            self.assertIn(token, read)
        snapshot = self.renderer.split("private readonly record struct PublishedFrame(", 1)[1].split(";", 1)[0]
        self.assertNotIn("Texture", snapshot)

    def test_reload_and_level_pass_invalidate_before_replacement_and_entity_traversal(self):
        replace = body(self.renderer, "private Texture ReplaceAtlas(")
        self.assertLess(replace.index("assetGeneration++"), replace.index("textures[index] = replacement"))
        reload = body(self.renderer, "private void ReloadAtlases( IReadOnlyList<string>? paths )")
        self.assertIn("ReplaceAtlas( index, GuestRenderer.LoadAtlas", reload)
        delete = body(self.renderer, "protected override void OnDelete()")
        self.assertLess(delete.index("deleted = true"), delete.index("textures.Clear()"))
        level = body(source("source/OpenTPW/World/Level.cs"), "public void Render()")
        self.assertLess(level.index("RenderPass++"), level.index("Entity.All.ToArray()"))

    def test_hud_retains_fallback_updates_before_trying_live_output(self):
        read = body(self.hud, "private UiSpriteFrame? EmployeePortraitFrame()")
        ordered = ["employeePortraitKey = key", "employeePortraitStart = Time.Now", "EmployeeFrame( selected )",
                   "return live", "PortraitFrame( chosen.Type, chosen.Variant, Time.Now - employeePortraitStart )"]
        offsets = [read.index(token) for token in ordered]
        self.assertEqual(sorted(offsets), offsets)
        self.assertIn("Frame = EmployeePortraitFrame", self.hud)
        hire = source("source/OpenTPW/Hud/ParkHud.Staff.cs")
        self.assertNotIn("EmployeeFrame", hire)
        self.assertIn("PortraitFrameAtStep( chosen.Type, chosen.Variant, player.Steps - 1 )", hire)
        flow = source("source/OpenTPW/Client/GameFlow.cs")
        self.assertLess(flow.index("Level.Render()"), flow.index("Hud.Draw( Context )"))

    def test_body_exclusions_are_shared_without_selecting_again_in_ui(self):
        rules = source("source/OpenTPW/World/Staff/StaffSprites.cs")
        self.assertIn("internal static bool HasBodySprite( StaffMember member )", rules)
        self.assertIn("member.State is not (StaffState.Resting or StaffState.PickedUp)", rules)
        self.assertIn("member.Job is not (StaffJob.Resting or StaffJob.PickedUp)", rules)


if __name__ == "__main__":
    unittest.main()
