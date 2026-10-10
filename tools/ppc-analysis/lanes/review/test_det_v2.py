"""Review DET-V2: the world-seed stack's round-2 fixes (d620fb7) and their merge onto main 8a88379.

Reads C# sources and docs from this checkout only; needs no game data and runs nothing from the original.

- The source guard (DeterminismTests.ForbiddenInSimulation) catches the round-1 F3 forms, and the forms it
  still misses are pinned here as known gaps, so a guard change shows up as a test change.
- The presentation exclusion list hides no simulation code: in the excluded files the guard finds only the
  advisor's mouth-shape Random and camera tuning properties.
- The canonical hash no longer reads the sound seed (F1), the schema is at least 2, and the pinned fixed-run
  hash in DeterminismTests.cs is the one docs/DETERMINISM.md records (later deliberate re-pins stay consistent).
- When docs/M3-GATE.md is present (the merged tree), its determinism row says PASS and DETERMINISM.md no
  longer says the gate is "not on main yet".
"""
from __future__ import annotations

import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[4]
SOURCE = REPO / 'source' / 'OpenTPW'
TEST = REPO / 'source' / 'OpenTPW.Tests' / 'DeterminismTests.cs'


def read(path: Path) -> str:
    return path.read_text(encoding='utf-8-sig')


class GuardRound2(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not TEST.exists():
            raise unittest.SkipTest('DeterminismTests.cs not in this checkout')
        text = read(TEST)
        block = re.search(r'ForbiddenInSimulation = new\((.*?)RegexOptions', text, re.S)
        presentation = re.search(r'PresentationWorldFiles =\s*\{(.*?)\};', text, re.S)
        if not block or not presentation:
            raise unittest.SkipTest('guard shape changed; re-review it')
        parts = re.findall(r'@"((?:[^"]|"")*)"', block.group(1))
        cls.forbidden = re.compile(''.join(part.replace('""', '"') for part in parts))
        cls.presentation = re.findall(r'"([^"]+)"', presentation.group(1))

    def test_round1_f3_forms_are_caught(self):
        for line in ('Random r = new();', 'System.Random r = new();', 'private Random? random = new();',
                     'private static long counter;', 'internal static volatile uint ticks;',
                     'public static int NextId { get; set; }', 'var h = name.GetHashCode();',
                     'var h = string.GetHashCode( name );', 'var k = HashCode.Combine( a, b );',
                     'var t = Environment.TickCount64;'):
            self.assertTrue(self.forbidden.search(line), line)

    def test_known_gaps_after_round2(self):
        # Not caught by d620fb7's pattern. None of these forms occurs in the scanned files today (checked by
        # hand at d620fb7: the only Guid is ParkSaveFile's temporary file name); a guard fix flips this test.
        for line in ('private static readonly Random shared = new( 1 );',  # process-wide seeded stream
                     'private static Random shared = new( 1 );',
                     'private static List<int> ids = new();',                # mutable static collection
                     'private static int[] counts = new int[4];',
                     'private static GameSpeed speed;',                      # static enum/struct field
                     'private static int counter; // not readonly',         # "readonly" anywhere hides the line
                     'var x = RandomNumberGenerator.GetInt32( 10 );',
                     'var d = DateTime.Today;',
                     'var id = Guid.NewGuid();'):
            self.assertIsNone(self.forbidden.search(line), line)

    def test_excluded_files_hold_only_presentation_hits(self):
        world = SOURCE / 'World'
        for name in self.presentation:
            self.assertTrue((world / name).exists(), name)
        hits = sorted(f'{name}: {line.strip()}' for name in self.presentation
                      for line in read(world / name).splitlines() if self.forbidden.search(line))
        self.assertEqual(hits, [
            'Advisor.cs: private readonly AdvisorMouth mouth = new( new Random() );',
            'LobbyCameraMode.cs: public static float Radius { get; set; } = 70f;',
            'LobbyCameraMode.cs: public static float SpinSpeed { get; set; } = 0.02f;',
            'LobbyCameraMode.cs: public static float VerticalOffset { get; set; } = 20f;',
            'ParkCameraMode.cs: public static float TargetExtent { get; set; } = SandboxTargetExtent;',
        ])
        # The mouth Random drives only the advisor's mouth mesh (BIN 0x10007434: rand() % 5 + 1 per 100 ms).
        advisor = read(world / 'Advisor.cs')
        self.assertEqual(re.findall(r'\bmouth\.\w+', advisor), ['mouth.Current', 'mouth.Update'])
        for name in self.presentation:
            text = read(world / name)
            for simulation in ('RideScriptWorld', 'WorldSeed', 'GuestSimulation', 'ParkWorldStreams'):
                self.assertNotIn(simulation, text, f'{name} names {simulation}')


class HashRound2(unittest.TestCase):
    def setUp(self):
        hash_file = SOURCE / 'World' / 'WorldStateHash.cs'
        if not hash_file.exists() or not TEST.exists():
            self.skipTest('world-seed stack not in this checkout')
        self.hash_text = read(hash_file)
        self.test_text = read(TEST)

    def test_sound_seed_is_not_hashed(self):
        schema = re.search(r'public const int SchemaVersion = (\d+);', self.hash_text)
        self.assertIsNotNone(schema)
        self.assertGreaterEqual(int(schema.group(1)), 2)
        self.assertNotIn('SoundSeed', self.hash_text)
        streams = read(SOURCE / 'World' / 'ParkWorldStreams.cs')
        compute = re.search(r'ComputeStateHash\(\) => WorldStateHash\.Compute\((.*?)\);', streams, re.S)
        self.assertIsNotNone(compute)
        self.assertNotIn('Sound', compute.group(1))

    def test_pinned_hash_is_documented(self):
        # Reproduced in this review: 89f2db9 without the SeedResearcherStandIn key in the economy digest gives
        # 0x86B5046D497B900C (round 1's pin); d620fb7 with schema 1 and the two empty sound fields restored
        # gives 0xB55E94284EBFE91B (89f2db9's pin); d620fb7's own pin was 0x0D8B481BB19391DC (schema 2).
        # Later schema bumps re-pin deliberately; the test pin and the documented current pin must agree.
        pin = re.search(r'Assert\.AreEqual\( 0x([0-9A-F]{16})UL, park\.Hash', self.test_text)
        self.assertIsNotNone(pin)
        schema = re.search(r'public const int SchemaVersion = (\d+);', self.hash_text).group(1)
        doc = read(REPO / 'docs' / 'DETERMINISM.md')
        self.assertRegex(doc, rf'`0x{pin.group(1)}`, schema {schema}')


class MergedGateDocs(unittest.TestCase):
    def setUp(self):
        self.gate = REPO / 'docs' / 'M3-GATE.md'
        if not self.gate.exists():
            self.skipTest('the M3 gate is not in this checkout')

    def test_determinism_row_passes(self):
        gate = read(self.gate)
        row = re.search(r'^\| determinism\.same-seed \| (.*?) \|', gate, re.M)
        self.assertIsNotNone(row)
        self.assertEqual(row.group(1), 'PASS')
        # The totals line must agree with the baseline table (it changes as other rows are fixed).
        verdicts = re.findall(r'^\| [a-z.\-]+ \| \**(PASS|FAIL|UNRESOLVED)\** \|', gate, re.M)
        totals = re.search(r'Totals: (\d+) pass, (\d+) fail, (\d+) unresolved', gate)
        self.assertIsNotNone(totals)
        self.assertEqual([int(n) for n in totals.groups()],
                         [verdicts.count('PASS'), verdicts.count('FAIL'), verdicts.count('UNRESOLVED')])
        self.assertNotIn('not on main yet', read(REPO / 'docs' / 'DETERMINISM.md'))
        self.assertNotIn('process-wide counter', read(SOURCE / 'Client' / 'M3Gate.cs'))

    def test_register_count_matches_the_sentence(self):
        # Round 2 merged DET and GATE ("eight"); later registers raise the count, which must stay in words.
        tool = read(REPO / 'tools' / 'fidelity_register.py')
        self.assertIn('"DET": "source/OpenTPW/World/DeterminismApproximations.cs",', tool)
        self.assertIn('"GATE": "source/OpenTPW/Client/M3GateApproximations.cs",', tool)
        registers = re.search(r'^REGISTERS = \{(.*?)^\}', tool, re.S | re.M)
        self.assertIsNotNone(registers)
        count = len(re.findall(r'^\s+"[A-Z]+": "source/', registers.group(1), re.M))
        words = ['zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'eleven', 'twelve']
        self.assertIn(f'the {words[count]} configured C# registers', tool)


if __name__ == '__main__':
    unittest.main()
