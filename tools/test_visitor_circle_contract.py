"""UI-930 source and independent arithmetic guards, not a replacement for C# runtime/visual tests."""
import math
from pathlib import Path
import struct
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


def f32(x):
    return struct.unpack("<f", struct.pack("<f", x))[0]


class VisitorCircleContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.renderer = read("source/OpenTPW/World/Guests/GuestRenderer.cs")
        cls.circle = read("source/OpenTPW/Hud/VisitorCirclePicker.cs")
        cls.rectangle = read("source/OpenTPW/Hud/GuestWorldPicker.cs")

    def test_eligibility_is_separate_from_hit_and_a_circle_miss_cannot_retry(self):
        query = body(self.renderer, "internal Guest? PickVisitor(SVector2 mouse, SVector2 viewport, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 projection, out float depth)")
        circle_branch = body(query, "if (canonicalView")
        self.assertIn("if (!circle.Contains(pointer.X, pointer.Y))", circle_branch)
        self.assertIn("continue;", circle_branch)
        self.assertNotIn("TryHit", circle_branch)
        self.assertIn("else if (!Hud.GuestWorldPicker.TryHit", query)
        self.assertEqual(1, query.count("Draws?.Invoke(guest)"))
        for forbidden in (".Tick(", ".ApplyPending(", "RequestBody", ".Collect(", "PrepareWorld(", "publishedBodies["):
            self.assertNotIn(forbidden, query)
        self.assertLess(query.index("SelectSpriteAndSlot("), query.index("OrdinaryCircleSource("))
        self.assertIn("hit == best", query)
        self.assertIn("guest.Id < picked.Id", query)

    def test_scale_history_and_resources_are_observed_without_new_callbacks(self):
        source = self.renderer.split("private bool OrdinaryCircleSource(", 1)[1].split("/// <summary>Picks", 1)[0]
        for token in ("OrdinaryRenderer", "Level.OriginalPark != null", "OrdinaryGuest", "simulation.Settings.Types.Count",
                      "Pipeline is { IsDisposed: false }", "HasAtlas", "ValidFrame", "!guest.BodySpriteHistoryReconstructed",
                      "guest.BodySpriteInCostume == guest.InCostume", "Output: { } output", "player.ActiveTemplate", "player.CurrentProgram",
                      "IsValidOutput( output )", "guest.BodySprite.PendingTemplate"):
            self.assertIn(token, source)
        for forbidden in ("Invoke(", ".Tick(", "Apply", "RequestBody", "CaptureSnapshot("):
            self.assertNotIn(forbidden, source)
        self.assertIn("internal static bool IsValidOutput", read("source/OpenTPW/World/Guests/VisitorSpriteState.cs"))

    def test_matrix_gate_is_exact_except_for_explicit_rigid_basis_tolerance(self):
        canonical = body(self.circle, "internal static bool IsCanonicalView(")
        for token in ("Camera.Mode is not ParkCameraMode", "!Camera.Mode.UsesOriginalProjection", "Camera.Mode.FieldOfView != 90f",
                      "view != Camera.ViewMatrix", "projection != Camera.ProjMatrix", "projection != expected",
                      "OriginalProjection.Create(aspect, OriginalProjection.FieldOfView)", "view.M14 != 0", "view.M24 != 0",
                      "view.M34 != 0", "view.M44 != 1", "determinant > 0", "double.IsFinite(determinant)"):
            self.assertIn(token, canonical)
        self.assertIn("internal const float BasisTolerance = 1e-5f", self.circle)
        self.assertIn("Math.Abs(determinant - 1) <= BasisTolerance", canonical)
        self.assertEqual(6, canonical.count("Math.Abs(Dot("))

    def test_shared_clipping_preserves_legacy_depth_without_borrowing_circle_dimensions(self):
        create = body(self.circle, "internal static bool TryCreate(")
        self.assertIn("GuestWorldPicker.TryBounds", create)
        self.assertIn("out depth, out _, out _", create)
        for token in ("frame.PixelsPerUnitX == 0", "frame.PixelsPerUnitY == 0", "clip.W <= 0", "double.IsFinite(radiusSquared)",
                      "Math.Sin(Math.PI / 4) * clip.W / OriginalParkPlacement.ModelScale", "mirror ? -(double)frame.OriginX",
                      "(double)0.0002f * (width * width + height * height)"):
            self.assertIn(token, create)
        self.assertNotIn(".Contains(", create)
        self.assertNotIn("SpriteScale", create)
        self.assertNotIn("Extents", create.replace("legacy", ""))
        bounds = body(self.rectangle, "internal static bool TryBounds(")
        self.assertIn("depth = z", bounds)
        self.assertIn("z < 0 || z > 1", bounds)
        self.assertIn("for (var corner = 0; corner < 4; corner++)", bounds)

    def test_integer_fractional_and_large_drawable_pointer_arithmetic(self):
        pointer = body(self.circle, "internal static bool TryPointer(")
        self.assertLess(pointer.index("!ValidViewport"), pointer.index("Math.Truncate"))
        self.assertIn("Math.Truncate((double)mouse.X * 2048 / viewport.X)", pointer)
        self.assertIn("Math.Truncate((double)mouse.Y * 1536 / viewport.Y)", pointer)
        self.assertIn("(float)(y * (double)(1f / 767.5f) - 1)", pointer)
        for width, height, x, y in ((1024, 768, 512, 384), (1920, 1080, 960, 540), (2048, 1536, 1024, 768)):
            px = f32(math.trunc(x * 2048 / width) * f32(1 / 1024) - 1)
            py = f32(math.trunc(y * 1536 / height) * f32(1 / 767.5) - 1)
            self.assertEqual(0, px)
            self.assertEqual(0.0006514489650726318, py)
        self.assertEqual(1025, math.trunc(512.5 * 2048 / 1024))
        self.assertEqual(1024, math.trunc(512.49 * 2048 / 1024))

    def test_independent_literal_geometry_has_no_depth_factor_in_radius(self):
        aspect, width, height, a, b, ox = .75, 30, 43, 128, 128, -15
        radius = f32(.0002) * ((width / a * 20 * aspect) ** 2 + (height / b * 20) ** 2)
        self.assertAlmostEqual(.011500243850104397, radius, places=15)
        native_offset = ox / a * 20 * aspect / (math.sin(math.pi / 4) * 100)
        host_offset = ox / a * 20 / math.cos(math.pi / 4) * .2 * (2 * aspect) / 20
        self.assertAlmostEqual(2 * native_offset, host_offset, places=15)
        self.assertFalse(radius < radius)
        self.assertTrue(math.nextafter(radius, 0) < radius)
        self.assertFalse(math.nextafter(radius, math.inf) < radius)
        self.assertNotEqual(-ox, -(ox + width))

    def test_level_remains_post_winner_without_second_best_retry(self):
        level = body(read("source/OpenTPW/World/Level.cs"), "internal Guest? PickVisitor(")
        self.assertLess(level.index("SwimmerRenderer?.PickVisitor"), level.index("IsInHoveredNeighbourhood"))
        self.assertEqual(1, level.count("IsInHoveredNeighbourhood"))
        self.assertIn("swimDepth < Math.Min( kidDepth, costumeDepth )", level)
        self.assertIn("costumeDepth == kidDepth && costumed.Id < kid.Id", level)
        self.assertIn("? picked : null", level)

    def test_tests_and_residuals_are_required_without_retirement_or_lag_shortcut(self):
        for file in ("VisitorCircleGeometryTests.cs", "VisitorCircleSelectionTests.cs"):
            tests = read("source/OpenTPW.Tests/" + file)
            self.assertIn("[TestMethod]", tests)
            self.assertIn("GuestWorldPicker.TryHit", tests)
        selection = read("source/OpenTPW.Tests/VisitorCircleSelectionTests.cs")
        for token in ("ActualHudShiftPress", "RectangleOnly", "CaptureSnapshot", "RandomState", "Swimmer"):
            self.assertIn(token, selection)
        self.assertIn('("UI-930",', read("source/OpenTPW/UI/Original/UiApproximations.cs"))
        proposal = read("docs/proposals/ui-930-match-visitor-sprite-pick-geometry.md")
        for residual in ("Two separately retained", "lowest-ID ties", "reconstructed histories", "x87", "cross-kind", "rendered-view", "not run"):
            self.assertIn(residual, proposal)
        self.assertIn('test_visitor_circle_contract.py', read(".github/workflows/build.yml"))
        self.assertIn("dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --configuration Release --no-build --nologo", read(".github/workflows/build.yml"))


if __name__ == "__main__":
    unittest.main()
