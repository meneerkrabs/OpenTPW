"""UI-914 source integration guards and synthetic wire arithmetic; not C# execution or native corpus qualification."""
from pathlib import Path
import math
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


class NativeStatisticsHistoryContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.codec = read("source/OpenTPW.Files/Formats/Save/SaveParkStatistics.cs")
        cls.importer = read("source/OpenTPW.Files/Formats/Save/OriginalParkImport.cs")
        cls.history = read("source/OpenTPW/Economy/ParkStatisticsHistory.cs")
        cls.save = read("source/OpenTPW/Economy/ParkSaveFile.cs")
        cls.graph = read("source/OpenTPW/Hud/ParkHud.InfoGraph.cs")
        cls.economy = read("source/OpenTPW/Economy/ParkEconomy.cs")

    def test_fixed_profile_checks_body_and_every_preceding_capacity(self):
        for expected in ("MonthlyCapacity = 144", "MonthlyCount = 16", "MonthlyBytes = 13 + MonthlyCapacity * 4",
                         "SaveRecentResearch.BodyOffset + SaveRecentResearch.Bytes + 6 * 5", "RecentStayCapacity = 50",
                         "thing.Model != SaveThingModel.ParkAnalyser", "thing.BodyOffset < 0", "thing.BodyLength < BodyBytes",
                         "thing.BodyOffset > payload.Length", "thing.BodyLength > payload.Length - thing.BodyOffset",
                         "SaveRecentResearch.Read( payload, thing )", "index < MonthlyCount", "!= MonthlyCapacity"):
            self.assertIn(expected, self.codec)
        for ordinal, name in ((9, "people"), (12, "arrivals"), (13, "happiness"), (14, "stay"), (15, "rating")):
            self.assertIn(f'MonthlyOffset + {ordinal} * MonthlyBytes, MonthlyCapacity, {str(name == "happiness").lower()}, "{name}"', self.codec)
        self.assertIn('RecentStaysOffset, RecentStayCapacity, false, "recent stays"', self.codec)
        self.assertNotIn("new int[BinaryPrimitives", self.codec)

    def test_signed_values_float_bits_inactive_slots_and_read_only_copies_are_separate(self):
        ring = body(self.codec, "private static NativeStatisticsRing ReadRing(")
        for expected in ("length > body.Length - offset", "head < -1", "head >= capacity", "wrapped > 1",
                         "head == -1 && wrapped != 0", "wrapped == 0 && slot > head", "continue;",
                         "(double)BitConverter.Int32BitsToSingle( word ) : word", "!double.IsFinite( value )"):
            self.assertIn(expected, ring)
        self.assertLess(ring.index("continue;"), ring.index("BitConverter.Int32BitsToSingle"))
        self.assertIn("Array.AsReadOnly( (int[])words.Clone() )", self.codec)
        self.assertIn("Array.AsReadOnly( (double[])values.Clone() )", self.codec)
        self.assertIn("arrivals.Temp < 0", self.codec)
        self.assertNotIn("Math.Clamp", self.codec)

    def test_only_the_world_referenced_tpw_analyser_joins_the_bundle(self):
        tpw = body(self.importer, "public static OriginalParkImport Import(")
        for expected in ("world.Variables.ParkAnalyser", "analyserId == 0", "world.Find( analyserId )",
                         "SaveParkStatistics.Read( span, statisticsThing )", "Statistics = statistics"):
            self.assertIn(expected, tpw)
        self.assertLess(tpw.index("SaveParkStatistics.Read"), tpw.index("return new OriginalParkImport"))
        tpi = body(self.importer, "public static OriginalParkImport ImportThemeParkInc(")
        self.assertNotIn("SaveParkStatistics", tpi)
        self.assertIn("SaveThemeParkIncStatistics.Read( tpiWorld )", tpi)
        self.assertIn("Statistics = statistics", tpi)
        economy_import = read("source/OpenTPW/Economy/OriginalEconomyImport.cs")
        self.assertIn("economy.StatisticsHistory.RestoreOriginal( statistics )", economy_import)
        self.assertIn("economy.StatisticsHistory.Restore( new ParkStatisticsHistory().Capture() )", economy_import)

    def test_restore_builds_all_rings_before_assignment_and_migrates_only_absent_rating(self):
        restore = body(self.history, "public void Restore( ParkStatisticsData")
        self.assertIn("data == null || !data.IsValid", restore)
        self.assertIn("data.Rating?.ToRing() ?? LegacyRating( legacyRatings )", restore)
        self.assertLess(restore.index("var recentStays ="), restore.index("ArrivalsThisMonth ="))
        self.assertIn(".TakeLast( MonthsKept )", self.history)
        self.assertIn("Rating.Count == 0", self.history)
        self.assertIn("Rating is null || Rating.IsValid( ParkStatisticsHistory.MonthsKept )", self.history)
        self.assertIn("park.StatisticsHistory.IsEmpty && park.Ledger.History.Count == 0 ? null", self.save)
        self.assertIn("park.StatisticsHistory.Restore( history, park.Ledger.History.Select( month => month.ParkRating ) )", self.save)
        self.assertIn("park.StatisticsHistory.RestoreLegacyRating( park.Ledger.History.Select( month => month.ParkRating ) )", self.save)
        self.assertLess(self.save.index("park.Ledger.Restore("), self.save.index("park.StatisticsHistory.Restore("))

    def test_month_close_captures_rating_once_without_changing_financial_history(self):
        sample = self.economy.split("var census = CachedGuestCensus( 0 );", 1)[1].split("Raise( ParkEventKind.MonthEnded", 1)[0]
        self.assertEqual(1, sample.count("ParkRating"))
        self.assertIn("var rating = ParkRating;", sample)
        self.assertIn("StatisticsHistory.CloseMonth( guestStatistics.CensusHappiness, census, rating );", sample)
        self.assertIn("Ledger.CloseMonth( nextMonthIndex, rating, NetWorth( 0 ) );", sample)
        close = body(self.history, "public void CloseMonth(")
        self.assertEqual(1, close.count("Rating.Add( rating )"))
        self.assertIn("ArrivalsThisMonth = 0", close)
        self.assertIn("RecentStays.SumNewest( StayWindow )", close)
        self.assertNotIn("LegacyRating", close)
        state_hash = read("source/OpenTPW/World/WorldStateHash.cs")
        self.assertIn("root.Remove( nameof( ParkSaveFile.Data.StatisticsHistory ) );", state_hash)

    def test_actual_draw_consumer_reads_independent_rating_without_ledger_reconstruction(self):
        self.assertIn("_ => history.Rating", self.graph)
        draw = body(self.graph, "public override void Draw(")
        self.assertIn("ParkInfoGraph.Ring( series, economy.StatisticsHistory )", draw)
        self.assertNotIn("Ledger", draw)
        self.assertNotIn("RestoreLegacyRating", self.graph)
        self.assertNotIn("HistoryRing.Of", self.graph)

    def test_wire_arithmetic_and_logical_order_cover_partial_full_and_wrapped_states(self):
        # Independent arithmetic oracle; the executable C# fixtures exercise the production reader.
        prefix = 8 + 5 * 4 + 1024 * 61 + 4 * 17 + 6 * 5
        stride = 13 + 144 * 4
        self.assertEqual((62590, 589), (prefix, stride))
        self.assertEqual([67891, 69658, 70247, 70836, 71425], [prefix + i * stride for i in (9, 12, 13, 14, 15)])
        self.assertEqual(73536, prefix + 16 * stride + 213 + 33 + 20 * 60 + 16 + 48 + 12)
        for capacity in (50, 144):
            for count in (0, 1, 12, 36, capacity - 1, capacity, capacity + 1, 2 * capacity + 1):
                with self.subTest(capacity=capacity, count=count):
                    words = [0x7fc00000] * capacity
                    head, wrapped = -1, False
                    for value in range(count):
                        head += 1
                        if head == capacity:
                            head, wrapped = 0, True
                        words[head] = value
                    held = capacity if wrapped else head + 1
                    ordered = [words[(head - age + capacity) % capacity] for age in range(held - 1, -1, -1)]
                    self.assertEqual(list(range(max(0, count - capacity), count)), ordered)
        self.assertEqual(61.5, struct.unpack("<f", struct.pack("<I", 0x42760000))[0])
        for value in (-2147483648, -1, 0, 2147483647):
            self.assertEqual(value, int(float(struct.unpack("<i", struct.pack("<i", value))[0])))
        for word in (0x7fc00000, 0x7f800000, 0xff800000):
            self.assertFalse(math.isfinite(struct.unpack("<f", struct.pack("<I", word))[0]))

    def test_standard_ci_keeps_full_csharp_checks_and_residual_open(self):
        workflow = read(".github/workflows/build.yml")
        self.assertIn('python -m unittest discover -s tools -p "test_native_statistics_history_contract.py" -v', workflow)
        self.assertIn("dotnet build source/OpenTPW.sln --configuration Release --nologo", workflow)
        self.assertIn("dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --configuration Release --no-build --nologo", workflow)
        self.assertIn('("UI-914",', read("source/OpenTPW/UI/Original/UiApproximations.cs"))
        proposal = read("docs/proposals/ui-914-scope-information-history-residuals.md")
        for qualifier in ("UI-914 remains open", "Theme Park Inc", "wrapped", "no .NET SDK", "template preservation"):
            self.assertIn(qualifier, proposal)


if __name__ == "__main__":
    unittest.main()
