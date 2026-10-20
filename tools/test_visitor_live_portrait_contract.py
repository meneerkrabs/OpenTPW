"""UI-910 source integration guards; synthetic C# and visual validation remain separate."""
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


class VisitorLivePortraitContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.renderer = read("source/OpenTPW/World/Guests/GuestRenderer.cs")
        cls.hud = read("source/OpenTPW/Hud/ParkHud.VisitorInfoWindow.cs")
        cls.figure = read("source/OpenTPW/UI/Original/UiSpriteFigure.cs")

    def test_real_world_path_publishes_only_after_successful_guest_quad(self):
        prepare = body(self.renderer, "internal int PrepareWorld()")
        ordered = ["rendererPass++", "publishedBodies.Clear()", "if ( !CanRender", "Figures?.Invoke()",
                   "bounceRiders.Clear()", "BounceRiders?.Collect(", "foreach ( var guest", "AcceptsBodyConsumer(",
                   "SelectSpriteAndSlot(", "AddQuad(", "rider == null && canPublish", "publishedBodies[guest] =",
                   "DrawnGuests++", "foreach ( var figure", "DrawnFigures++", "return count"]
        offsets = [prepare.index(token) for token in ordered]
        self.assertEqual(sorted(offsets), offsets)
        self.assertEqual(1, self.renderer.count("publishedBodies[guest] ="))
        render = body(self.renderer, "protected override void OnRender()")
        self.assertLess(render.index("PrepareWorld()"), render.index("commandList.UpdateBuffer"))
        for signature in ("public (int Atlas, int Frame, bool Mirror) SelectSprite(", "internal bool TryMount("):
            self.assertNotIn("publishedBodies", body(self.renderer, signature))

    def test_observer_never_calls_selection_poses_animation_or_rng(self):
        observer = body(self.renderer, "internal UI.Original.UiSpriteFrame? VisitorFrame(")
        for forbidden in (".Invoke(", "SelectSprite", "TryMount(", ".Collect(", ".Tick(", ".Restart(", "RequestBody", "Random", "KidSet(", "publishedBodies[", "GetValue("):
            self.assertNotIn(forbidden, observer.replace("TryGetValue(", "Lookup("))
        self.assertIn("output.HeadingX, output.HeadingY, 0f", observer)
        for required in ("output.Owner, Level", "output.Simulation, simulation", "output.Renderer, this", "output.Guest, guest",
                         "output.Id != guest.Id", "output.Costume != guest.InCostume", "output.RideSource, BounceRiders",
                         "output.WorldPass != Level.RenderPass", "output.RendererPass != rendererPass", "output.Assets != assetGeneration",
                         "output.Atlas, atlases[output.AtlasIndex]", "resourceIdentities.TryGetValue", "resource, output.Resource",
                         "ReferenceEquals( item, guest )", "!ValidFrame("):
            self.assertIn(required, observer)
        snapshot = self.renderer.split("private readonly record struct PublishedBody(", 1)[1].split(";", 1)[0]
        self.assertNotIn("Texture", snapshot)
        self.assertNotIn("UiSpriteFrame", snapshot)
        self.assertIn("ConditionalWeakTable<Veldrid.Texture, object>", self.renderer)

    def test_owned_source_checks_exclude_extension_routes_without_callbacks(self):
        owned = self.renderer.split("private bool OrdinaryRenderer =>", 1)[1].split("private static bool ValidFrame", 1)[0]
        for required in ("Level.Current", "Level.Guests, simulation", "Entity.All.Contains( this )", "Level.GuestRenderer, this",
                         "Level.CostumeRenderer, this", "!guest.InSwimSession", "guest.InCostume", "guest.IsVisible",
                         "SlotChooser == null", "FeetChooser == null", "DepthPullChooser == null", "DepthPulledSlots == null",
                         "RotatedSlots == null", "BottomAnchoredSlots == null", "!ExactDirections", "!SwimSlotFallback"):
            self.assertIn(required, owned)
        self.assertNotIn(".Invoke(", owned)
        self.assertNotIn("AcceptsBodyConsumer(", owned)
        self.assertIn("IsDisposed: false", self.renderer)

    def test_reload_delete_and_omitted_render_passes_cannot_reuse_output(self):
        replace = body(self.renderer, "private Texture ReplaceAtlas(")
        self.assertLess(replace.index("assetGeneration++"), replace.index("textures[index] = replacement"))
        self.assertLess(replace.index("publishedBodies.Clear()"), replace.index("textures[index] = replacement"))
        self.assertIn("ReplaceAtlas( index, LoadAtlas(", body(self.renderer, "private void ReloadAtlases( IReadOnlyList<string>? paths )"))
        delete = body(self.renderer, "protected override void OnDelete()")
        for token in ("deleted = true", "assetGeneration++", "publishedBodies.Clear()", "textures.Clear()"):
            self.assertLess(delete.index(token), delete.index("ScheduleDelete"))
        level = body(read("source/OpenTPW/World/Level.cs"), "public void Render()")
        self.assertLess(level.index("RenderPass++"), level.index("Entity.All.ToArray()"))

    def test_hud_sessions_clear_on_any_close_without_changing_statistics_cadence(self):
        bind = body(self.hud, "private void BindVisitorPortrait(")
        for required in ("++visitorPortraitSession", "var simulation = level.Guests", "session == visitorPortraitSession",
                         "ReferenceEquals( guest, visitorPortraitGuest )", "ReferenceEquals( simulation, level.Guests )",
                         "ReferenceEquals( guest, InfoVisitor )", "Level.Current", "VisitorFrame( guest )"):
            self.assertIn(required, bind)
        self.assertIn("Removed = ClearVisitorPortrait", self.hud)
        clear = body(self.hud, "private void ClearVisitorPortrait()")
        self.assertIn("visitorPortraitSession++", clear)
        self.assertIn("visitorPortraitGuest = null", clear)
        cycle = body(self.hud, "public bool CycleVisitorInfo(")
        self.assertLess(cycle.index("next == guest.Id"), cycle.index("BindVisitorPortrait("))
        refresh = body(self.hud, "private void RefreshVisitorInfo()")
        self.assertNotIn("Portrait", refresh)
        self.assertIn("public const float VisitorInfoRefreshSeconds = 4f", self.hud)
        flow = read("source/OpenTPW/Client/GameFlow.cs")
        self.assertLess(flow.index("Level.Render()"), flow.index("Hud.Draw( Context )"))

    def test_inner_integer_geometry_is_an_opt_in_before_viewport_scaling(self):
        self.assertIn("var inner = portrait.Inner!.Value", self.hud)
        self.assertIn("Bounds = inner", self.hud)
        self.assertIn("FeetInCanvas = VisitorInfoRules.PortraitFeet( inner ), CanvasUnitsPerPixel = 3.6f", self.hud)
        helper = self.hud.split("PortraitFeet( UiRect inner ) =>", 1)[1].split(";", 1)[0]
        self.assertIn(">> 1", helper)
        self.assertIn(">> 2", helper)
        self.assertEqual((598, 450), (477 + ((704 - 477) >> 1) + 8, 510 - ((510 - 281) >> 2) - 3))
        self.assertIn("CanvasUnitsPerPixel { get; init; } = 3.2f", self.figure)
        self.assertIn("context.Canvas.Map( authored, Anchor ) : Feet( rect )", self.figure)
        self.assertIn("rect.X + rect.Width / 2, rect.Bottom - rect.Height / 4", self.figure)

    def test_residuals_and_full_ci_remain_explicit(self):
        self.assertIn('("UI-910",', read("source/OpenTPW/UI/Original/UiApproximations.cs"))
        for required in ("clone VM timing", "source-loss epochs", "rotation/depth", "viewport/raster"):
            self.assertIn(required, self.hud)
        workflow = read(".github/workflows/build.yml")
        self.assertIn('python -m unittest discover -s tools -p "test_visitor_live_portrait_contract.py" -v', workflow)
        self.assertIn("dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --configuration Release --no-build --nologo", workflow)


if __name__ == "__main__":
    unittest.main()
