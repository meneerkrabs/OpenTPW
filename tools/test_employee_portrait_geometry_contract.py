"""UI-092 geometry integration guards; not C# execution or original raster validation."""
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


class EmployeePortraitGeometryContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.hud = read("source/OpenTPW/Hud/ParkHud.EmployeeWindow.cs")
        cls.geometry = read("source/OpenTPW/UI/Original/EmployeePortraitGeometry.cs")
        cls.tests = read("source/OpenTPW.Tests/EmployeePortraitGeometryTests.cs")

    def test_geometry_uses_signed_endpoints_and_employee_scale_offset(self):
        self.assertIn("float CanvasUnitsPerPixel = 2.4f", self.geometry)
        self.assertIn("Feet( (int)inner.X, (int)inner.Y, (int)inner.Right, (int)inner.Bottom )", self.geometry)
        formula = self.geometry.split("Feet( int left, int top, int right, int bottom ) =>", 1)[1]
        self.assertIn("new( left + ((right - left) >> 1) + 8, bottom - ((bottom - top) >> 2) - 2 )", formula)
        for token in ("HirePortraitGeometry", "VisitorInfoRules", "Time.", "Random", "MathF", "/ 4f"):
            self.assertNotIn(token, self.geometry)

    def test_real_employee_widget_inherits_inner_and_keeps_outer_frame(self):
        self.assertIn("var inner = portrait.Inner ?? portrait.Rect", self.hud)
        widget = self.hud.split('Id = "portraitFigure"', 1)[1].split("} );", 1)[0]
        for token in ("Frame = EmployeePortraitFrame", "Bounds = inner", "Anchor = UiAnchor.Center",
                      "FeetInCanvas = EmployeePortraitGeometry.Feet( inner )",
                      "CanvasUnitsPerPixel = EmployeePortraitGeometry.CanvasUnitsPerPixel"):
            self.assertIn(token, widget)
        self.assertIn('Id = "portraitFrame", Model = portrait.Model!, Bounds = portrait.Rect, Anchor = UiAnchor.Center', self.hud)
        self.assertNotIn("HirePortraitGeometry", self.hud)
        self.assertNotIn("VisitorInfoRules.PortraitFeet", self.hud)

    def test_source_policy_clock_and_shared_draw_remain_independent(self):
        callback = body(self.hud, "private UiSpriteFrame? EmployeePortraitFrame()")
        ordered = ["employeePortraitKey = key", "employeePortraitStart = Time.Now", "EmployeeFrame( selected )",
                   "return live", "PortraitFrame( chosen.Type, chosen.Variant, Time.Now - employeePortraitStart )"]
        offsets = [callback.index(token) for token in ordered]
        self.assertEqual(sorted(offsets), offsets)
        for token in ("Geometry", "PrepareWorld", ".Tick(", ".Restart(", "Random"):
            self.assertNotIn(token, callback)
        for path in ("source/OpenTPW/Hud/ParkHud.Staff.cs", "source/OpenTPW/Hud/ParkHud.VisitorInfoWindow.cs",
                     "source/OpenTPW/UI/Original/UiSpriteFigure.cs", "source/OpenTPW/World/Staff/StaffRenderer.cs"):
            self.assertNotIn("EmployeePortraitGeometry", read(path))
        figure = read("source/OpenTPW/UI/Original/UiSpriteFigure.cs")
        self.assertIn("CanvasUnitsPerPixel { get; init; } = 3.2f", figure)
        self.assertIn("rect.X + rect.Width / 2, rect.Bottom - rect.Height / 4", figure)

    def test_actual_draw_oracle_covers_all_vertices_without_shared_geometry_helpers(self):
        oracle = body(self.tests, "private static void AssertDraw(")
        for token in ("figure.Frame()", "figure.Draw(", "MathF.Max( 0.05f, MathF.Min(", "width / 2f + (x - 1024) * s",
                      "height / 2f + (y - 768) * s", "pixel * s * 128 / a", "pixel * s * 128 / b",
                      "draw.Vertices[index].Position", "draw.Vertices[index].TexCoords", "Assert.AreSame( frame.Texture",
                      "Assert.AreEqual( 1, context.Batch.Draws.Count )", "Assert.AreEqual( 6, draw.Vertices.Count )"):
            self.assertIn(token, oracle)
        for token in ("EmployeePortraitGeometry.Feet(", "Canvas.Map(", "UiSpriteFigure.Layout(", "UiSpriteFigure.Feet("):
            self.assertNotIn(token, oracle)
        for row in ("2048, 1536", "640, 480", "1280, 720", "1920, 1080", "1000, 1000", "801, 601", "64, 48"):
            self.assertIn(f"[DataRow( {row} )]", self.tests)
        for token in ("new[] { 5, 8 }", "new[] { -1, 1 }", "(128, 128), (256, 64)", "20, 40", "-8, -38"):
            self.assertIn(token, self.tests)

    def test_three_widgets_default_and_observational_source_phase_controls_are_present(self):
        for token in ("ActualThreeWidgetsAndDefaultDrawKeepIndependentBoundsFeetAndScale", "Hud.OpenEmployeeWindow( Member )",
                      "Hud.OpenStaffWindow()", "visitorHud.OpenVisitorInfoWindow( guest )", "new UiSpriteFigure",
                      "(employee, 526, 476, 2.4f)", "(hire, 626, 362, 2.4f)", "(visitor, 598, 450, 3.6f)", "(generic, 518, 478, 3.2f)",
                      "Assert.IsNull( portrait.Inner )", "20, 30, 7, 15, 21, 17", "385, 279, 650, 544, 525, 476",
                      "StaffSpriteRules.Sweep", "step > 0", "StaffState.Resting", '"employeePortraitStart"',
                      "ParkSaveFile.Serialize", "observed.Economy.World.State", 'QueueInterval( 148 )',
                      "control.State()", "Get<Vertex[]>( control.Renderer", "StaffSpriteSet.Empty"):
            self.assertIn(token, self.tests)

    def test_residual_stays_registered_and_ci_runs_contract_and_full_cpu_suite(self):
        register = read("source/OpenTPW/UI/Original/UiApproximations.cs")
        self.assertIn('("UI-092",', register)
        for token in ("independent clone", "source-loss", "depth/occlusion", "viewport/raster"):
            self.assertIn(token, self.hud)
        workflow = read(".github/workflows/build.yml")
        self.assertIn('python -m unittest discover -s tools -p "test_employee_portrait_geometry_contract.py" -v', workflow)
        self.assertIn("dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --configuration Release --no-build --nologo", workflow)


if __name__ == "__main__":
    unittest.main()
