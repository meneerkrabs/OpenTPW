"""UI-086 source/algorithm guards only; these do not execute the C# Draw or prove rendered parity."""
from fractions import Fraction
from pathlib import Path
import re
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


class MapRepairContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = read("source/OpenTPW/Hud/ParkHud.Map.cs")
        cls.palette_tests = read("source/OpenTPW.Tests/MapRepairPaletteTests.cs")
        cls.draw_tests = read("source/OpenTPW.Tests/MapRepairDrawTests.cs")

    def test_lookup_is_map_local_current_identity_and_read_only(self):
        lookup = body(self.source, "private RgbaByte ObjectFill(")
        for required in ("!item.IsDeleted", "!owner.level.IsReadOnlyVisit", "owner.Status?.EconomySource is ParkEconomy",
                         "ReferenceEquals( economy, owner.level.Park?.Economy )", "owner.level.GetEconomyInstance( item )",
                         "economy.TryGetObject( instance, out var state )", "state.Id == instance",
                         "state.InfoId == item.Entry.InfoId", "RepairFill( state.Repair )", "ParkMapPalette.UnavailableObject"):
            self.assertIn(required, lookup)
        for mutation in (".Repair =", ".Sell(", ".RegisterExisting(", ".Next(", ".Save("):
            self.assertNotIn(mutation, lookup)
        self.assertNotIn("LiveState(", self.source)
        self.assertIn("UnavailableObject = new( 207, 67, 198, 235 )", self.source)

    def test_conversion_uses_stored_float_then_safe_widened_bounds_then_mask(self):
        conversion = body(self.source, "public static RgbaByte? RepairFill(")
        for required in ("var stored = (float)repair", "!float.IsFinite( stored )", "(double)stored < int.MinValue",
                         "(double)stored > int.MaxValue", "return null", "RepairRamp( (int)stored & 255 )"):
            self.assertIn(required, conversion)
        self.assertLess(conversion.index("(double)stored > int.MaxValue"), conversion.index("(int)stored & 255"))
        ramp = body(self.source, "public static RgbaByte RepairRamp(")
        self.assertIn("Math.Clamp( value, 0, 100 )", ramp)
        self.assertIn("<< 16) / 50", ramp)
        self.assertIn("(-200 * fraction) >> 16", ramp)
        self.assertNotIn("float", ramp)
        self.assertNotIn("double", ramp)

    def test_algorithm_vectors_match_separate_rational_floor_reference(self):
        def reference(value):
            v = max(0, min(100, value))
            upper = v >= 50
            f = Fraction(((v - 50 if upper else v) * 65536) // 50, 65536)
            start, end = ((248, 200, 0), (48, 252, 0)) if upper else ((248, 0, 0), (248, 200, 0))
            return tuple(x + ((y - x) * f).__floor__() for x, y in zip(start, end)) + (255,)
        for v in range(-20, 281):
            c = max(0, min(100, v)); q = ((c - 50 if c >= 50 else c) << 16) // 50
            calculated = (248 + ((-200 * q) >> 16), 200 + ((52 * q) >> 16), 0, 255) if c >= 50 else (248, (200 * q) >> 16, 0, 255)
            self.assertEqual(reference(v), calculated)
        vectors = re.findall(r"\[DataRow\( (-?\d+), (\d+), (\d+) \)\]", self.palette_tests)
        self.assertEqual(11, len(vectors))
        for value, red, green in vectors:
            self.assertEqual(reference(int(value)), (int(red), int(green), 0, 255))

    def test_csharp_conversion_vectors_use_the_stored_float_domain(self):
        vectors = re.findall(r"\[DataRow\( (-?[\d.]+), (\d+) \)\]", self.palette_tests)
        self.assertEqual(19, len(vectors))
        for value, byte in vectors:
            stored = struct.unpack("f", struct.pack("f", float(value)))[0]
            self.assertEqual(int(byte), int(stored) & 255, value)
        self.assertIn("decimal.Floor", self.palette_tests)
        self.assertIn("2147483647d", self.palette_tests)
        self.assertIn("double.NaN", self.palette_tests)

    def test_draw_order_filters_and_qualified_residuals_remain(self):
        draw = body(self.source, "public override void Draw(")
        expected = ("ParkMapPalette.CellFill(", "owner.MapShowsCategory(", "var fill = ObjectFill( item )",
                    "if ( owner.mapTracks )", "if ( owner.mapExcitement )", "owner.level.StaffWorld?.Snapshots",
                    "if ( owner.mapVisitors || owner.mapHappiness )", "outcomeCells.Rebuild(")
        positions = [draw.index(token) for token in expected]
        self.assertEqual(positions, sorted(positions))
        self.assertIn("foreach ( var cell in item.Cells )", draw)
        self.assertIn("Math.Max( image.X, rect.X )", draw)
        self.assertIn("Math.Min( image.Right, rect.Right )", draw)
        self.assertIn("[APPROX:UI-086]", self.source)
        register = read("source/OpenTPW/UI/Original/UiApproximations.cs")
        self.assertIn('("UI-086",', register)
        self.assertNotIn("what the value +0x44 of an object record is", register)

    def test_actual_draw_regressions_cover_identity_geometry_and_immutability(self):
        for required in ('"ParkMapView"', ".View.Draw( w.Context", '"staleStatus"', '"wrongInfoId"', '"wrongRecordId"',
                         '"missingRecord"', '"readOnly"', '"nonfinite"', '"overflow"', ".Delete()", "ToggleMapCategory(",
                         "new MapViewState( 2", "vertex.TexCoords", "ParkSaveFile.Capture(", "WorldStateHash.Compute(",
                         "w.Economy.World.State", "Coasters.Random.State", "Script.Variables", "ToggleMapHappiness()",
                         "ToggleMapExcitement()", "ToggleMapSatisfaction()", "ToggleMapTracks()", "StaffWorldSimulation("):
            self.assertIn(required, self.draw_tests)


if __name__ == "__main__":
    unittest.main()
