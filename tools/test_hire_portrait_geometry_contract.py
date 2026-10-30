"""UI-082 source integration guards; not a substitute for C# execution or raster validation."""
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


class HirePortraitGeometryContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.hud = read("source/OpenTPW/Hud/ParkHud.Staff.cs")
        cls.geometry = read("source/OpenTPW/UI/Original/HirePortraitGeometry.cs")
        cls.figure = read("source/OpenTPW/UI/Original/UiSpriteFigure.cs")

    def test_hire_geometry_uses_inner_endpoints_signed_shifts_and_nominal_scale(self):
        self.assertIn("Left = 513, Top = 202, Right = 723, Bottom = 415", self.geometry)
        self.assertIn("UiRect Inner = new( Left, Top, Right - Left, Bottom - Top )", self.geometry)
        self.assertIn("FeetInCanvas = Feet( Left, Top, Right, Bottom )", self.geometry)
        self.assertIn("float CanvasUnitsPerPixel = 2.4f", self.geometry)
        self.assertIn("Feet( int left, int top, int right, int bottom )", self.geometry)
        self.assertIn("new( left + ((right - left) >> 1) + 8, bottom - ((bottom - top) >> 2) )", self.geometry)
        formula = self.geometry.split("Feet( int left", 1)[1]
        for forbidden in ("MathF", "CanvasUnitsPerPixel", "- 3", "/ 4f", "0.25"):
            self.assertNotIn(forbidden, formula)

    def test_real_hire_widget_opts_in_without_shrinking_its_outer_picture(self):
        self.assertIn('Model( "staffPicture", "f_staffpic", Rect( 484, 179, 748, 443 ) )', self.hud)
        widget = self.hud.split('Id = "portraitFigure"', 1)[1].split("} );", 1)[0]
        for token in ("Frame = PortraitFrame", "Bounds = HirePortraitGeometry.Inner", "FeetInCanvas = HirePortraitGeometry.FeetInCanvas",
                      "CanvasUnitsPerPixel = HirePortraitGeometry.CanvasUnitsPerPixel", "Anchor = UiAnchor.Center"):
            self.assertIn(token, widget)
        self.assertIn("[EXT:staff-employee-view]", self.hud)
        self.assertNotIn("EmployeeFrame", self.hud)

    def test_callback_follows_the_native_hire_phases_and_source_policy(self):
        callback = body(self.hud, "private UiSpriteFrame? PortraitFrame()")
        ordered = ["showingEmployedStaff", "SelectedStaff", "SelectedCandidate", "key != portraitKey", "portraitKey = key",
                   "new SpritePlayer( nowMs )", "player.Tick( nowMs )", "level.StaffRenderer?.PortraitFrameAtStep( chosen.Type, chosen.Variant, player.Steps - 1 )"]
        offsets = [callback.index(token) for token in ordered]
        self.assertEqual(sorted(offsets), offsets)
        # no frame before the script's first due pass, and a hire view's invalid applicant keeps the figure
        self.assertIn("player.Steps == 0 ? null", callback)
        self.assertIn(": portraitOfEmployee ? null : portraitKey", callback)
        for token in ("PrepareWorld", "Random", "Restart(", "HirePortraitGeometry"):
            self.assertNotIn(token, callback)
        # the close ends the figure (0x49B7B4)
        self.assertIn("EndPortrait()", body(self.hud, "public void CloseStaffWindow()"))
        renderer = body(read("source/OpenTPW/World/Staff/StaffRenderer.cs"), "internal UI.Original.UiSpriteFrame? PortraitFrameAtStep(")
        for token in ("sprites.AtlasFor( type, variantKey )", "deleted || atlasIndex < 0 || atlasIndex >= textures.Count",
                      "StaffSpriteRules.PortraitAtStep( steps, directions )", "frame.ScaleX, frame.ScaleY"):
            self.assertIn(token, renderer)

    def test_shared_layout_still_scales_both_offsets_and_dimensions_and_mirrors_uv(self):
        layout = body(self.figure, "public static (UiRect Rect, NVector2 Uv0, NVector2 Uv1) Layout(")
        for token in ("-(frame.OriginX + frame.Width)", "pixel * frame.ScaleX", "pixel * frame.ScaleY", "left * pixelX",
                      "frame.OriginY * pixelY", "frame.Width * pixelX", "frame.Height * pixelY", "(u0, u1) = (u1, u0)"):
            self.assertIn(token, layout)
        self.assertIn("context.Canvas.Map( authored, Anchor ) : Feet( rect )", self.figure)
        self.assertIn("CanvasUnitsPerPixel * rect.Width / Bounds.Width", self.figure)

    def test_generic_employee_and_visitor_are_independent_regression_controls(self):
        self.assertIn("CanvasUnitsPerPixel { get; init; } = 3.2f", self.figure)
        self.assertIn("rect.X + rect.Width / 2, rect.Bottom - rect.Height / 4", self.figure)
        employee = read("source/OpenTPW/Hud/ParkHud.EmployeeWindow.cs")
        self.assertNotIn("HirePortraitGeometry", employee)
        widget = employee.split('Id = "portraitFigure"', 1)[1].split("} );", 1)[0]
        self.assertIn("CanvasUnitsPerPixel = EmployeePortraitGeometry.CanvasUnitsPerPixel", widget)
        self.assertIn("FeetInCanvas = EmployeePortraitGeometry.Feet( inner )", widget)
        self.assertIn("var inner = portrait.Inner ?? portrait.Rect", employee)
        self.assertNotIn("VisitorInfoRules.PortraitFeet", employee)
        self.assertNotIn("EmployeePortraitGeometry", self.hud)
        visitor = read("source/OpenTPW/Hud/ParkHud.VisitorInfoWindow.cs")
        self.assertIn("FeetInCanvas = VisitorInfoRules.PortraitFeet( inner ), CanvasUnitsPerPixel = 3.6f", visitor)
        self.assertNotIn("HirePortraitGeometry", visitor)
        self.assertNotIn("EmployeePortraitGeometry", visitor)

    def test_residuals_tests_and_full_ci_stay_explicit(self):
        self.assertIn('("UI-082",', read("source/OpenTPW/UI/Original/UiApproximations.cs"))
        for token in ("no frame before the first due pass", "global rotation/depth", "viewport/raster"):
            self.assertIn(token, self.hud)
        tests = read("source/OpenTPW.Tests/HirePortraitGeometryTests.cs")
        for token in ("figure.Draw(", "figure.Frame()", "rig.Hud.Stack.Top", "ParkSaveFile.Serialize", "rig.Economy.World.State",
                      "hud.ShowEmployedStaff( true )", "hud.CloseStaffWindow()", "hud.SortStaff( true )", "StaffSpriteSet.Empty"):
            self.assertIn(token, tests)
        workflow = read(".github/workflows/build.yml")
        self.assertIn('python -m unittest discover -s tools -p "test_hire_portrait_geometry_contract.py" -v', workflow)
        self.assertIn("dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --configuration Release --no-build --nologo", workflow)


if __name__ == "__main__":
    unittest.main()
