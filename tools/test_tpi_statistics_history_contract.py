"""TPI historical-import source guards and independent wire arithmetic, not C# execution or a native-save witness."""
from pathlib import Path
import math
import struct
import unittest

ROOT = Path(__file__).resolve().parents[1]
SELECTED = (14, 17, 18, 19, 20)


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def method(text, signature):
    start = text.index("{", text.index(signature))
    depth, end = 1, start + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start + 1:end - 1]


def offsets(shorts, monthly):
    """Independent serialization arithmetic: fixed grid, 4 short rings, 6 totals, 21 word rings."""
    start = 8 + 5 * 4 + 1024 * 61 + sum(11 + count * 2 for count in shorts) + 6 * 5
    found = []
    for count in monthly:
        found.append(start)
        start += 13 + count * 4
    return found, start


def normalize(capacity, head, wrapped, words, floating=False):
    """Small independent model for the selected ring's validation and active-slot rule."""
    if capacity not in (50, 144) or head < -1 or head >= capacity or wrapped not in (0, 1) or (head == -1 and wrapped):
        raise ValueError("invalid ring")
    values = []
    for slot, word in enumerate(words):
        if not wrapped and slot > head:
            values.append(0.0)
            continue
        value = struct.unpack("<f", struct.pack("<I", word & 0xffffffff))[0] if floating else word
        if not math.isfinite(value):
            raise ValueError("nonfinite active sample")
        values.append(float(value))
    return values


class TpiStatisticsHistoryContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.codec = read("source/OpenTPW.Files/Formats/Save/SaveThemeParkIncStatistics.cs")
        cls.importer = read("source/OpenTPW.Files/Formats/Save/OriginalParkImport.cs")
        cls.runtime = read("source/OpenTPW/Economy/ParkEconomyRuntime.cs")

    def test_identity_and_dynamic_body_are_separate_from_tpw(self):
        decoder = method(self.codec, "public static NativeParkStatistics? Read(")
        for expected in ("world.Variables.ParkAnalyser", "analyserId == 0", "return null;", "world.Find( analyserId )",
                         "ThemeParkIncThingModel.ParkAnalyser", "new ThemeParkIncBody( thing.Model, world.BodyOf( thing ) )"):
            self.assertIn(expected, decoder)
        self.assertLess(decoder.index("analyserId == 0"), decoder.index("world.Find"))
        for forbidden in ("SaveParkStatistics", "BodyOffset", "MonthlyOffset", "VisitorsThisMonth", "world.ParkAnalyser"):
            self.assertNotIn(forbidden, decoder)
        for ordinal, name in zip(SELECTED, ("people", "arrivals", "happiness", "stay", "rating")):
            self.assertIn(f'ReadRing( body, "mDayRings[{ordinal}]", MonthlyCapacity, {str(name == "happiness").lower()}, "{name}" )', decoder)
        self.assertIn('ReadRing( body, "ring50", RecentStayCapacity, false, "recent stays" )', decoder)

    def test_raw_flags_capacities_and_active_float_guards(self):
        ring = method(self.codec, "private static NativeStatisticsRing ReadRing(")
        for expected in ('body.U8( path + ".mWrappedAround" )', 'unchecked( (int)body.U32( path + ".mCurrentEntry" ) )',
                         "head < -1", "head >= (long)capacity", "wrapped > 1", "head == -1 && wrapped != 0",
                         "capacity != requiredCapacity", "structurally supported but unsupported by the fixed host history profile",
                         "wrapped == 0 && slot > head", "(double)BitConverter.Int32BitsToSingle( word ) : word", "!double.IsFinite( value )"):
            self.assertIn(expected, ring)
        self.assertNotIn("body.Flag(", ring)
        self.assertNotIn("body.Ring(", ring)
        self.assertNotIn("Math.Clamp", ring)
        self.assertLess(ring.index("continue;"), ring.index("BitConverter.Int32BitsToSingle"))
        self.assertIn("arrivals.Temp < 0", self.codec)

    def test_bundle_and_runtime_apply_are_atomic_without_economy_or_clock_import(self):
        decoder = method(self.codec, "public static NativeParkStatistics? Read(")
        self.assertLess(decoder.index("var recentStays ="), decoder.index("return new NativeParkStatistics"))
        self.assertNotIn("Restore", decoder)
        importer = method(self.importer, "public static OriginalParkImport ImportThemeParkInc(")
        self.assertIn("var statistics = SaveThemeParkIncStatistics.Read( tpiWorld );", importer)
        self.assertIn("Statistics = statistics", importer)
        self.assertLess(importer.index("SaveThemeParkIncStatistics.Read"), importer.index("return new OriginalParkImport"))
        runtime = method(self.runtime, "public static ParkEconomyRuntime ForThemeParkIncLevel(")
        for expected in ("new ParkEconomy(", "EconomyRules.ThemeParkIncMoney", "park.Save?.Statistics", "economy.StatisticsHistory.RestoreOriginal( statistics )",
                         "new ParkEconomyRuntime( economy, import: null )", "ImportShippedSave: false", "StartsOpen = true"):
            self.assertIn(expected, runtime)
        for forbidden in ("OriginalEconomyImport.Apply", "RestoreClock", "RestoreCalendar", "Ledger.CloseMonth", "Calendar.", "GameTick"):
            self.assertNotIn(forbidden, runtime)
        self.assertLess(runtime.index("new ParkEconomy("), runtime.index("RestoreOriginal"))
        common = read("source/OpenTPW.Files/Formats/Save/SaveParkStatistics.cs")
        self.assertIn("Array.AsReadOnly( (int[])words.Clone() )", common)
        self.assertIn("Array.AsReadOnly( (double[])values.Clone() )", common)

    def test_different_wire_counts_move_selected_offsets(self):
        full, recent = offsets([3] * 4, [144] * 21)
        self.assertEqual([70836, 72603, 73192, 73781, 74370], [full[i] for i in SELECTED])
        self.assertEqual(74959, recent)
        counts = [144 if i in SELECTED else i % 7 for i in range(21)]
        shifted, shifted_recent = offsets([0, 1, 2, 3], counts)
        self.assertLess(shifted_recent, recent)
        self.assertTrue(all(shifted[i] != full[i] for i in SELECTED))
        self.assertNotEqual(full[15], full[17])
        # Full and minimal entire body include ring50/ring5, 22 byte rings and the 92-byte scalar tail.
        self.assertEqual(76617, recent + 13 + 50 * 4 + 13 + 5 * 4 + 22 * (10 + 50) + 92)
        _, empty_recent = offsets([0] * 4, [0] * 21)
        self.assertEqual(63177, empty_recent + 13 + 13 + 22 * 10 + 92)

    def test_signed_exactness_finite_values_and_inactive_words(self):
        for integer in (-2147483648, -17, 0, 2147483647):
            words = [integer] * 144
            self.assertEqual(float(integer), normalize(144, 0, 0, words)[0])
        for bits in (0x7fc00001, 0x7f800000, 0xff800000):
            words = [bits] * 144
            self.assertEqual([0.0] * 144, normalize(144, -1, 0, words, True))
            with self.assertRaises(ValueError):
                normalize(144, 0, 0, words, True)
            words[0] = 0x42760000
            self.assertEqual(61.5, normalize(144, 0, 0, words, True)[0])
            with self.assertRaises(ValueError):
                normalize(144, 0, 1, words, True)
        for head, wrapped in ((-2, 0), (144, 0), (-1, 1), (0, 2)):
            with self.assertRaises(ValueError):
                normalize(144, head, wrapped, [0] * 144)

    def test_source_semantics_and_ci_keep_qualification_bounded(self):
        self.assertIn("[BIN:TPI-EXE:0x5AF220 monthly producer]", self.codec)
        self.assertIn("[EXT:tpi-history-fresh-start]", self.runtime)
        self.assertIn("[EXT:tpi-history-five-series]", self.runtime)
        document = read("docs/THEME-PARK-INC.md")
        for expected in ("first fresh host month", "saved game turn, calendar rate", "three series", "144", "50", "UI-914 open"):
            # The contract spells the native three selections explicitly in the mapping section.
            if expected == "three series":
                self.assertIn("selects only arrivals, people and rating", document)
            else:
                self.assertIn(expected, document)
        self.assertIn('("UI-914",', read("source/OpenTPW/UI/Original/UiApproximations.cs"))
        workflow = read(".github/workflows/build.yml")
        self.assertIn('python -m unittest discover -s tools -p "test_tpi_statistics_history_contract.py" -v', workflow)
        self.assertIn("dotnet build source/OpenTPW.sln --configuration Release --nologo", workflow)
        self.assertIn("dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --configuration Release --no-build --nologo", workflow)


if __name__ == "__main__":
    unittest.main()
