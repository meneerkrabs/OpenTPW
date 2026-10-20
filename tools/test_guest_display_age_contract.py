"""GUEST-920 synthetic arithmetic/source guards, not a C# or original-game runtime oracle."""
from pathlib import Path
import struct
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8")


def turn(tick):
    return tick * 1000 // 14880


def boundary(tick, phase):
    previous = (tick - 1) % 372 + phase
    return turn(previous + 1) - turn(previous)


class GuestDisplayAgeContract(unittest.TestCase):
    def test_all_phases_match_integer_turn_subtraction(self):
        for phase in range(372):
            for tick in range(1, 1117):
                self.assertEqual(turn(tick + phase) - turn(tick - 1 + phase), boundary(tick, phase))

    def test_observed_source_formulas_expose_both_synthetic_mismatches(self):
        dt = struct.unpack("f", struct.pack("f", 1 / 60))[0]
        self.assertEqual(4, turn(134) - turn(75))
        self.assertEqual(3, int(59 * dt * 1000 / 248))
        self.assertEqual(14955, (1005 * 14880 + 999) // 1000)
        self.assertEqual(104, turn(14955 + 59) - 905)
        self.assertEqual(103, int(((100 * 248 + .5) / 1000 + 59 * dt) * 1000 / 248))

    def test_clock_and_display_do_not_replace_seconds_or_gameplay_consumers(self):
        clock = read("source/OpenTPW/World/Guests/GuestSimulation.DisplayAge.cs")
        self.assertIn("unchecked((uint)turns + 1u)", clock)
        self.assertIn("ParkCalendar.Turn( previous + 1 ) == ParkCalendar.Turn( previous )", clock)
        self.assertNotIn("random.", clock)
        simulation = read("source/OpenTPW/World/Guests/GuestSimulation.cs")
        self.assertIn("guest.TimeInParkSeconds = elapsed + dt;", simulation)
        self.assertIn("StayDays( (long)Math.Floor( elapsed * 1000 / ParkCalendar.TurnMilliseconds ) )", simulation)
        self.assertNotIn("DisplayTimeInParkTurns", simulation[simulation.index("internal void AddCanonicalState"):])
        umbrella = read("source/OpenTPW/World/Guests/GuestSimulation.Umbrellas.cs")
        self.assertNotIn("DisplayTimeInParkTurns", umbrella)

    def test_both_existing_hud_consumers_use_the_same_projection(self):
        roster = read("source/OpenTPW/Hud/OverviewRosters.cs")
        self.assertIn("guest.DisplayTimeInParkTurns ?? (long)Math.Floor", roster)
        self.assertIn("guest.TimeInParkSeconds is { } elapsed", roster)
        for source in ("ParkHud.VisitorWindow.cs", "ParkHud.VisitorInfoWindow.cs"):
            self.assertIn("ParkVisitorRoster.ValuesOf( guest, refreshed: true )", read("source/OpenTPW/Hud/" + source))
        self.assertIn("ParkVisitorRoster.ValuesOf( guest, refreshed: false )", read("source/OpenTPW/Hud/ParkHud.VisitorWindow.cs"))

    def test_restore_keeps_display_phase_separate_and_validators_share_rules(self):
        simulation = read("source/OpenTPW/World/Guests/GuestSimulation.cs")
        restore = simulation[simulation.index("public void RestoreSnapshot("):simulation.index("private void RestoreThings(")]
        self.assertIn("displayTurnPhaseTicks = snapshot.DisplayTurnPhaseTicks", restore)
        self.assertNotIn("turnPhase =", restore)
        self.assertIn("DisplayAgeProblems( snapshot )", simulation)
        self.assertIn("GuestSimulation.DisplayAgeProblems( guests )", read("source/OpenTPW/World/ParkResumeSaveFile.cs"))


if __name__ == "__main__":
    unittest.main()
