"""TPI arrival-history source guards and independent wire arithmetic, not C# execution or a native-save witness."""
from pathlib import Path
import re
import struct
import unittest

ROOT = Path(__file__).resolve().parents[1]
CAPACITY = 5


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def method(text, signature):
    start = text.index("{", text.index(signature))
    depth, end = 1, start + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start + 1:end - 1]


def compact(text):
    return re.sub(r"\s+", " ", text).strip()


def wire(head, wrapped, words, temp=193847):
    """Independent synthetic serialization: signed head, count, raw byte, scratch, words."""
    return struct.pack("<iIBi", head, len(words), wrapped, temp) + b"".join(
        struct.pack("<I", value & 0xffffffff) for value in words)


def decode(data):
    """Independent bounded ring model; exact bodies and world references are guarded in source below."""
    if len(data) < 13:
        raise ValueError("truncated ring")
    head, capacity, wrapped, _ = struct.unpack_from("<iIBi", data)
    if capacity > CAPACITY or len(data) != 13 + 4 * capacity:
        raise ValueError("invalid native extent")
    if head < -1 or head >= capacity or wrapped not in (0, 1) or (head == -1 and wrapped):
        raise ValueError("invalid metadata")
    if capacity != CAPACITY:
        raise ValueError("unsupported fixed host profile")
    words = list(struct.unpack_from(f"<{capacity}i", data, 13))
    return words[head + 1:] + words[:head + 1] if wrapped else words[:head + 1]


def arrival_offset(shorts, monthly, recent):
    """Independent body arithmetic: fixed grid, short rings, totals, monthly rings, recent stays."""
    return (8 + 5 * 4 + 1024 * 61 + sum(11 + 2 * count for count in shorts) + 6 * 5
            + sum(13 + 4 * count for count in monthly) + 13 + 4 * recent)


class TpiArrivalRingContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.codec = read("source/OpenTPW.Files/Formats/Save/SaveThemeParkIncArrivalRing.cs")
        cls.importer = read("source/OpenTPW.Files/Formats/Save/OriginalParkImport.cs")
        cls.advisor = read("source/OpenTPW/Client/AutomaticAdvisor.cs")

    def test_codec_uses_referenced_identity_and_exact_dynamic_body(self):
        decoder = method(self.codec, "public static IReadOnlyList<int>? Read(")
        for expected in ("ArgumentNullException.ThrowIfNull( world )", "world.Variables.ParkAnalyser",
                         "analyserId == 0", "return null;", "world.Find( analyserId )",
                         "ThemeParkIncThingModel.ParkAnalyser",
                         "new ThemeParkIncBody( thing.Model, world.BodyOf( thing ) )"):
            self.assertIn(expected, decoder)
        self.assertLess(decoder.index("analyserId == 0"), decoder.index("world.Find"))
        for forbidden in ("world.ParkAnalyser", "SaveArrivalRing.", "BodyOffset", "75172", "75_172",
                          "VisitorsThisMonth", "LargestBusLoad", "Admissions", "mDayRings", "ring50"):
            self.assertNotIn(forbidden, decoder)
        self.assertIn("ring 4 5 ring5", read("source/OpenTPW.Files/Formats/Save/ThemeParkIncBodyLayout.Data.cs"))

    def test_codec_preserves_raw_metadata_and_signed_oldest_first_values(self):
        decoder = method(self.codec, "public static IReadOnlyList<int>? Read(")
        for expected in ('body.U32( "ring5.mNumEntries" )',
                         'unchecked( (int)body.U32( "ring5.mCurrentEntry" ) )',
                         'body.U8( "ring5.mWrappedAround" )', "head < -1", "head >= (long)capacity",
                         "wrapped > 1", "head == -1 && wrapped != 0", "capacity != Capacity",
                         "structurally supported but unsupported by the fixed host history profile",
                         'unchecked( (int)body.U32( $"ring5.mData[{slot}]" ) )',
                         "wrapped != 0", "slot = head + 1", "slot < Capacity", "slot = 0", "slot <= head",
                         "new List<int>( Capacity )", "return sizes.AsReadOnly();"):
            self.assertIn(expected, decoder)
        self.assertLess(decoder.index("slot = head + 1"), decoder.index("slot = 0"))
        for forbidden in ('body.U32( "ring5.mTemp" )', "body.Flag(", "body.Ring(", "Math.Clamp",
                          "Math.Max", "Int32BitsToSingle", "(float)", "Restore("):
            self.assertNotIn(forbidden, decoder)
        self.assertIn("public const int Capacity = 5;", self.codec)

    def test_import_exposes_arrivals_and_statistics_only_after_full_decode(self):
        importer = method(self.importer, "public static OriginalParkImport ImportThemeParkInc(")
        for expected in ("SaveThemeParkIncStatistics.Read( tpiWorld )", "SaveThemeParkIncArrivalRing.Read( tpiWorld )",
                         "Statistics = statistics", "ArrivalGroups = arrivalGroups", "ThemeParkIncWorld = tpiWorld"):
            self.assertIn(expected, importer)
        returned = importer.index("return new OriginalParkImport")
        self.assertLess(importer.index("SaveThemeParkIncStatistics.Read"), returned)
        self.assertLess(importer.index("SaveThemeParkIncArrivalRing.Read"), returned)
        self.assertLess(importer.index("foreach ( var attraction in unrecorded )"), returned)
        for forbidden in ("RestoreOriginal", "RestoreSnapshot", "RestoreMilliseconds", "OriginalEconomyImport.Apply"):
            self.assertNotIn(forbidden, importer)

    def test_attachment_is_narrow_and_keeps_tpw_controller_clock_guards(self):
        attach = method(self.advisor, "public void AttachLevel(")
        self.assertIn("started.OriginalPark?.Save is { ArrivalGroups: { } savedGroups } source", attach)
        self.assertIn("(started.Park?.Import is not null || started.Park?.Economy.Rules == EconomyRules.ThemeParkIncMoney "
                      "&& source.ThemeParkIncWorld is not null)", compact(attach))
        self.assertIn("arrivalRing.Restore( savedGroups );", attach)
        self.assertLess(attach.index("ChallengesAttached( guests )"), attach.index("arrivalRing.Restore( savedGroups )"))
        self.assertNotIn("AssumeEstablishedArrivalRing", attach[attach.index("arrivalRing.Restore( savedGroups )"):])
        self.assertIn("started.Park?.Import is { } import && started.OriginalPark?.Save?.Advisor is { } saved", attach)
        self.assertIn("started.Park?.Import is not null && started.OriginalPark?.Save?.SavedAdvisorClockMilliseconds is { } savedClock", attach)
        self.assertIn("Controller.ImportOriginal( saved )", attach)
        self.assertIn("clock.RestoreMilliseconds( savedClock )", attach)
        self.assertNotIn("new OriginalEconomyImport", attach)

    def test_snapshot_null_and_empty_admissions_policy_is_unchanged(self):
        restore = method(self.advisor, "public void RestoreSnapshot(")
        self.assertIn("arrivalRing.Restore( snapshot.ArrivalWaves ?? Array.Empty<int>() );", restore)
        self.assertIn("if ( snapshot.ArrivalWaves is null or { Count: 0 } ) AssumeEstablishedArrivalRing();", compact(restore))
        fallback = method(read("source/OpenTPW/Client/AutomaticAdvisor.Challenges.cs"), "private void AssumeEstablishedArrivalRing(")
        self.assertEqual("if ( CurrentEconomy?.Counters[ParkCounters.Admissions] > 0 ) arrivalRing.AssumeFilled();", compact(fallback))

    def test_real_tpi_factory_keeps_fresh_economy_and_null_import(self):
        runtime = method(read("source/OpenTPW/Economy/ParkEconomyRuntime.cs"), "public static ParkEconomyRuntime ForThemeParkIncLevel(")
        for expected in ("new ParkEconomy(", "EconomyRules.ThemeParkIncMoney", "park.Save?.Statistics",
                         "economy.StatisticsHistory.RestoreOriginal( statistics )", "new ParkEconomyRuntime( economy, import: null )",
                         "ImportShippedSave: false", "StartsOpen = true"):
            self.assertIn(expected, runtime)
        for forbidden in ("OriginalEconomyImport.Apply", "RestoreClock", "RestoreCalendar", "RestoreSnapshot",
                          "SavedAdvisorClock", "Ledger.CloseMonth", "Calendar.", "GameTick"):
            self.assertNotIn(forbidden, runtime)
        self.assertLess(runtime.index("new ParkEconomy("), runtime.index("RestoreOriginal"))

    def test_actual_gameflow_returns_before_advisor_and_capabilities_stay_disabled(self):
        flow = method(read("source/OpenTPW/Client/GameFlow.cs"), "private Level EnterLevel(")
        inc = method(flow, "if ( level.IsThemeParkIncSlice )")
        self.assertIn("IncHud = CreateThemeParkIncHud( level );", inc)
        self.assertTrue(inc.rstrip().endswith("return level;"))
        self.assertNotIn("AutomaticAdvisor", inc)
        self.assertNotIn("AttachLevel", inc)
        self.assertLess(flow.index("if ( level.IsThemeParkIncSlice )"), flow.index("AutomaticAdvisor.TryCreate()"))
        self.assertLess(flow.index("return level;"), flow.index("Advisor?.AttachLevel( level )"))
        # Also forbid moving advisor creation above the entire guarded branch.
        self.assertNotIn("AutomaticAdvisor", flow[:flow.index("if ( level.IsThemeParkIncSlice )")])
        self.assertNotIn("AttachLevel", flow[:flow.index("if ( level.IsThemeParkIncSlice )")])
        capabilities = method(read("source/OpenTPW/Client/Setup/GameEdition.cs"), "public static EditionCapabilities ForThemeParkIncSlice()")
        for capability in ("Advisor", "ParkSaves", "Staff", "Research", "Challenges", "Sharing", "OriginalHud"):
            self.assertIn(f"{capability} = false", capabilities)
        self.assertIn("ExperimentalHud = true", capabilities)
        refusal = 'if ( GameEditions.IsThemeParkInc ) throw new NotSupportedException( "Theme Park Inc park saves are not supported." );'
        resume_source = read("source/OpenTPW/Client/GameFlow.Resume.cs")
        # [EXT:extensions-apply-at-load] the entry refuses first, before a confirmed extension switch or a waiting extension choice is applied
        entry = method(resume_source, "internal Level ResumePark(")
        self.assertIn(refusal, entry.split("Level Resume()")[0])
        # and the strict resume it calls still refuses before the economy is restored
        resume = method(resume_source, "private Level ResumeStrict(")
        self.assertIn(refusal, resume)
        self.assertLess(resume.index("GameEditions.IsThemeParkInc"), resume.index("ParkSaveFile.Restore"))
        # the overload that does the capture (the shorter overloads forward to it) hands it, as the co-op join capture does, to the parts that refuse first
        level_resume = read("source/OpenTPW/World/Level.Resume.cs")
        capture = method(level_resume, "internal ParkResumeSnapshot CaptureResumeSnapshotUnchecked( AutomaticAdvisor? advisor, string displayName, out Action validate, out IEnumerable<bool> validationSteps,")
        self.assertIn("CaptureResumeSnapshotParts(", capture)
        parts = method(level_resume, "private ParkResumeSnapshot CaptureResumeSnapshotParts(")
        self.assertTrue(parts.lstrip().startswith("if ( IsReadOnlyVisit || GameEditions.IsThemeParkInc || OriginalPark == null || Park == null || Guests == null )"))

    def test_synthetic_empty_partial_full_and_every_wrapped_head(self):
        words = [-2147483648, -17, 0, 2147483647, -1]
        for head in range(-1, CAPACITY):
            with self.subTest(head=head, wrapped=False):
                payload = wire(head, 0, words)
                before = bytes(payload)
                self.assertEqual(words[:head + 1], decode(payload))
                self.assertEqual(before, payload)
        for head in range(CAPACITY):
            with self.subTest(head=head, wrapped=True):
                expected = words[head + 1:] + words[:head + 1]
                self.assertEqual(expected, decode(wire(head, 1, words)))
                self.assertEqual(expected[1:] + [81], (decode(wire(head, 1, words)) + [81])[-CAPACITY:])
        self.assertEqual(b"\xff\xff\xff\xff", wire(-1, 0, words)[:4])
        self.assertEqual([], decode(wire(-1, 0, words, temp=2147483647)))
        self.assertEqual([0], decode(wire(0, 0, [0, -1, -17, -2147483648, 2147483647], temp=-193847)))

    def test_synthetic_unsupported_capacity_raw_flags_heads_and_truncation(self):
        for capacity in range(CAPACITY):
            with self.subTest(capacity=capacity), self.assertRaisesRegex(ValueError, "unsupported fixed host profile"):
                decode(wire(-1, 0, [0] * capacity))
        for capacity in (6, 0x80000000, 0xffffffff):
            with self.subTest(capacity=capacity), self.assertRaisesRegex(ValueError, "invalid native extent"):
                decode(struct.pack("<iIBi", -1, capacity, 0, 0))
        for head, wrapped in ((-2, 0), (5, 0), (-1, 1), (0, 2), (0, 255)):
            with self.subTest(head=head, wrapped=wrapped), self.assertRaisesRegex(ValueError, "invalid metadata"):
                decode(wire(head, wrapped, [1, 2, 3, 4, 5]))
        payload = wire(2, 1, [11, 22, 33, 44, 55])
        for length in range(len(payload)):
            with self.subTest(length=length), self.assertRaises(ValueError):
                decode(payload[:length])
        with self.assertRaises(ValueError):
            decode(payload + b"following record")

    def test_preceding_variable_counts_move_ring_without_changing_its_values(self):
        words = [-19, 53, 7, -2147483648, 2147483647]
        payload = wire(1, 1, words)
        full = arrival_offset([3] * 4, [144] * 21, 50)
        self.assertEqual(75172, full)
        for shorts, monthly, recent in (([0, 1, 2, 3], [i % 7 for i in range(21)], 17),
                                       ([3] * 4, [144] * 21, 0),
                                       ([0] * 4, [0] * 21, 50)):
            offset = arrival_offset(shorts, monthly, recent)
            self.assertNotEqual(full, offset)
            synthetic = b"\xa5" * offset + payload + b"following record"
            self.assertEqual(words[2:] + words[:2], decode(synthetic[offset:offset + len(payload)]))

    def test_scope_documentation_keeps_residual_and_full_ci(self):
        register = next(line for line in read("source/OpenTPW/World/Advisor.cs").splitlines() if '("ADVISOR-028",' in line)
        self.assertIn("group count or sizes", register)
        self.assertIn("null or explicitly empty", register)
        self.assertIn("public TPI slice still creates no advisor", register)
        self.assertNotIn("one to four", register)
        self.assertNotIn("only the first-group", register)
        document = read("docs/reverse/ADVISOR-SCAN.md")
        for expected in ("ADVISOR-028 remains open", "data readiness only", "not a native-format restriction",
                         "populated/wrapped native-save witness", "RestoreSnapshot", "AttachLevel"):
            self.assertIn(expected, document)
        workflow = read(".github/workflows/build.yml")
        self.assertIn('python -m unittest discover -s tools -p "test_tpi_arrival_ring_contract.py" -v', workflow)
        self.assertIn("dotnet build source/OpenTPW.sln --configuration Release --nologo", workflow)
        self.assertIn("dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --configuration Release --no-build --nologo", workflow)


if __name__ == "__main__":
    unittest.main()
