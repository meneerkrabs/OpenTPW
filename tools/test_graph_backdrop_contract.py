"""UI-060 source integration and independent arithmetic guards, not C# execution or rendered parity."""
from pathlib import Path
import struct
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def f32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def columns(width, samples):
    skip = {12: 1, 36: 3, 144: 12}.get(samples)
    if skip is None:
        return []
    delta = f32((width - 24) / samples)
    position = f32(4 + delta)
    result = []
    for sample in range(1, samples + 1):
        if sample % skip == 0:
            result.append(int(position))
        position = f32(position + delta)
    return result


def rows(height):
    delta = f32((height - 24) / 10)
    position = f32(height - 20 - delta + 1)
    result = []
    for _ in range(10):
        result.append(int(position))
        position = f32(position - delta)
    return result


def zero_axis(height, minimum, maximum):
    bottom = height - 20
    if minimum < 0 <= maximum:
        # Exact signed truncation, not floor division or a floating-point quotient.
        return bottom - ((height - 24) * -minimum // (maximum - minimum))
    return bottom + 1


class GraphBackdropContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.helper = read("source/OpenTPW/UI/Original/GraphBackdrop.cs")
        cls.finance = read("source/OpenTPW/Hud/ParkHud.FinanceWindow.cs")
        cls.info = read("source/OpenTPW/Hud/ParkHud.InfoGraph.cs")
        cls.tests = read("source/OpenTPW.Tests/GraphBackdropTests.cs")

    def test_shared_geometry_is_immutable_and_has_no_park_dependencies(self):
        self.assertIn("internal readonly record struct GraphBackdrop", self.helper)
        for colour in ("0, 0, 0, 255", "0, 100, 0, 255", "0, 200, 0, 255"):
            self.assertIn(colour, self.helper)
        for dependency in ("ParkEconomy", "FinanceValue", "WorldRng", "ParkSaveFile", "Random", "Time."):
            self.assertNotIn(dependency, self.helper)
        self.assertIn("=> (4, 4, width - 20, height - 20)", self.helper)
        self.assertIn("=> GraphBackdrop.PlotBounds( widthPixels, heightPixels )", self.info)
        self.assertIn("=> GraphBackdrop.GridSteps( samples )", self.info)

    def test_grid_advances_every_float_step_without_endpoint_snapping(self):
        for expression in ("12 => 1, 36 => 3, 144 => 12, _ => 0", "step <= Samples; step++",
                           "x = (float)(x + dx)", "var column = (int)x",
                           "var y = (float)(plot.Bottom - (double)dy + 1d)", "y = (float)(y - dy)"):
            self.assertIn(expression, self.helper)
        self.assertNotIn("step * dx", self.helper)
        self.assertNotIn("division * dy", self.helper)
        self.assertNotIn("Math.Round", self.helper)
        self.assertNotIn("MathF.Round", self.helper)

    def test_counts_and_labels_are_not_endpoint_lengths(self):
        for expression in ("new UiRect( column, plot.Top, 1, plot.Bottom )",
                           "new UiRect( plot.Left, (int)y, plot.Right, 1 )",
                           "new( Plot.Left - 1, ZeroAxisRow, Plot.Right + 1, 1 )",
                           "new( Plot.Left - 1, Plot.Top - 1, 1, Plot.Bottom + 1 )",
                           "Math.Max( 2, column )", "Math.Max( 2, plot.Bottom + 4 )"):
            self.assertIn(expression, self.helper)

    def test_axis_math_widens_both_product_and_range(self):
        self.assertIn("Minimum < 0 && Maximum >= 0", self.helper)
        self.assertIn("(Int128)(plot.Bottom - plot.Top) * Minimum / ((Int128)Maximum - Minimum)", self.helper)
        self.assertIn(": plot.Bottom + 1", self.helper)
        cases = [(0, 100, 249), (0, 0, 249), (-50, 50, 126), (-1, 2, 167), (-100, 0, 4),
                 (-5, -5, 249), (-(2**63), 2**63 - 1, 126), (-(2**63), 0, 4), (-1, 2**63 - 1, 248)]
        for minimum, maximum, expected in cases:
            self.assertEqual(expected, zero_axis(268, minimum, maximum))
        self.assertEqual(4, zero_axis(2**31 - 1, -(2**63), 0))

    def test_synthetic_columns_pin_accumulation_differences(self):
        expected = {
            12: [39, 74, 110, 145, 180, 215, 251, 286, 322, 357, 392, 428],
            36: [39, 74, 110, 145, 180, 215, 251, 286, 321, 357, 392, 427],
            144: [39, 74, 109, 145, 180, 215, 251, 286, 322, 357, 392, 428],
        }
        for samples, positions in expected.items():
            self.assertEqual(positions, columns(448, samples))
            self.assertIn(", ".join(map(str, positions)), self.tests)
        self.assertEqual(428, columns(449, 12)[-1])
        for unsupported in (0, -12, 24, 145):
            self.assertEqual([], columns(448, unsupported))

    def test_synthetic_rows_pin_float_store_and_truncation(self):
        for height, expected in ((268, [224, 200, 175, 151, 127, 102, 78, 53, 29, 5]),
                                 (50, [28, 25, 23, 20, 17, 15, 12, 10, 7, 4])):
            self.assertEqual(expected, rows(height))
            self.assertIn(", ".join(map(str, expected)), self.tests)

    def test_draw_adapter_orders_clear_plot_labeled_columns_rows_axes(self):
        body = self.helper.split("internal static bool Draw(", 1)[1]
        tokens = ["context.Batch.AddRectangle( frame", "Fill( backdrop.PlotFill", "backdrop.VerticalLines()",
                  "Fill( span, GraphBackdrop.Grid )", "context.DrawText(", "backdrop.HorizontalLines()",
                  "Fill( backdrop.HorizontalAxis", "Fill( backdrop.VerticalAxis"]
        indices = [body.index(token) for token in tokens]
        self.assertEqual(sorted(indices), indices)
        self.assertIn("Width > 24 && Height > 24 && GridInterval( Samples ) != 0", self.helper)
        self.assertEqual(2, self.helper.count("!CanPlot || Divisions <= 0"))
        self.assertIn("!float.IsFinite( frame.Width )", body)
        self.assertIn("!float.IsFinite( frame.Right )", body)
        self.assertIn("!float.IsFinite( frame.Bottom )", body)
        self.assertIn("Math.Max( 1f, canvas.Scale * 2f )", self.helper)

    def test_finance_backdrop_precedes_empty_guard_and_keeps_fractional_series(self):
        body = self.finance.split("private sealed class FinanceGraph", 1)[1]
        self.assertLess(body.index("GraphBackdropRenderer.Draw("), body.index("if ( !hasSeries"))
        self.assertIn("if ( !hasSeries || !canDrawSeries )", body)
        self.assertLess(body.index("GraphBackdropRenderer.Draw("), body.index("if ( minimum == maximum )"))
        self.assertIn("owner.financeYears * ParkCalendar.MonthsPerYear, minimum, maximum", body)
        for expression in ("owner.FinanceMonths()", "owner.financeVisible.OrderBy( series => series )",
                           "Math.Min( 0L, values.Min( series => series.Min() ) )", "Math.Max( 0L, values.Max( series => series.Max() ) )",
                           "index / (float)(months.Count - 1)", "values[s][index] - (double)minimum",
                           "maximum - (double)minimum", "owner.financeYears < 12"):
            self.assertIn(expression, body)
        self.assertLess(body.index("HollowMark("), body.index("context.Batch.AddTriangle("))
        for forbidden in ("75, 86, 125, 130", "plot.Height * line / 4", "ParkInfoGraph.Row", "ZeroAxisRow"):
            self.assertNotIn(forbidden, body)

    def test_info_backdrop_does_not_take_over_series_sampling(self):
        body = self.info.split("private sealed class ParkInfoGraphElement", 1)[1]
        self.assertLess(body.index("GraphBackdropRenderer.Draw("), body.index("if ( economy == null"))
        self.assertIn("if ( economy == null || !canDrawSeries )", body)
        for expression in ("ParkInfoGraph.ScaleMaximum(", "ParkInfoGraph.DrawOrder", "ParkStatisticsHistory.SeriesPoints(",
                           "ParkInfoGraph.Column( index, samples, plot )", "ParkInfoGraph.Row( points[index], maximum, plot )",
                           "points[^1].ToString", "samples < 0x90"):
            self.assertIn(expression, body)
        self.assertIn("(int)(plot.Left + (float)(plot.Right - plot.Left) / samples * index)", self.info)
        self.assertIn("plot.Bottom - (int)((long)value * height / maximum)", self.info)

    def test_ci_and_register_keep_full_execution_and_qualification(self):
        workflow = read(".github/workflows/build.yml")
        self.assertIn('python -m unittest discover -s tools -p "test_graph_backdrop_contract.py" -v', workflow)
        self.assertIn("dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --configuration Release --no-build --nologo", workflow)
        register = next(line for line in read("docs/UI.md").splitlines() if line.startswith("| UI-060 |"))
        for token in ("ten horizontal", "twelve", "pixel", "month", "raster"):
            self.assertIn(token, register)
        self.assertNotIn("sets three interior", register)
        canonical = next(line for line in read("source/OpenTPW/UI/Original/UiApproximations.cs").splitlines() if '("UI-060",' in line)
        for token in ("ten horizontal", "twelve", "ring's months", "raster", "Signed 128-bit"):
            self.assertIn(token, canonical)
        self.assertNotIn("three interior", canonical)
        self.assertIn("[APPROX:UI-060]", self.helper)
        draw_tests = read("source/OpenTPW.Tests/GraphBackdropDrawTests.cs")
        self.assertIn("PopulatedControlsRejectNonfiniteCoordinatesAndNegativeSizesBeforeDrawingTheirSeries", draw_tests)
        self.assertIn("PopulatedControlsWithAnUnsupportedStoredPeriodDrawOnlyTheFullBackground", draw_tests)
        proposal = read("docs/proposals/ui-060-match-graph-pixel-metrics.md")
        for token in ("ten horizontal", "twelve labeled", "run in CI", "sample-to-month", "not retired"):
            self.assertIn(token, proposal)


if __name__ == "__main__":
    unittest.main()
