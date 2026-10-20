"""UI-094 narrow source integration guards; these do not execute C# or prove visual fidelity."""
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


class RideStaticContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.hud = read("source/OpenTPW/Hud/ParkHud.cs")
        cls.ride = read("source/OpenTPW/Hud/ParkHud.RideView.cs")
        cls.timer = read("source/OpenTPW/Hud/EyeModeStaticTransition.cs")

    def test_screen_sync_is_timer_free_and_only_final_mode_is_prepared(self):
        sync = body(self.ride, "private void SyncRideScreen()")
        self.assertNotIn("rideStatic", sync)
        update = body(self.hud, "internal bool Update(")
        self.assertEqual(2, update.count("SyncRideScreen();"))
        self.assertEqual(1, update.count("PrepareRideStatic( context );"))
        self.assertGreater(update.index("PrepareRideStatic( context );"), update.rindex("SyncRideScreen();"))
        self.assertGreater(update.index("PrepareRideStatic( context );"), update.index("UpdateRideExits("))
        self.assertLess(update.index("rideStatic.SkipFrame();"), update.index("var ridingAtStart"))
        self.assertIn("new EyeModeStaticTransition( EyeCameraActive )", self.hud)

    def test_clock_and_current_owner_are_presentation_only(self):
        self.assertIn("ReferenceEquals( Camera.Mode, level.RideView )", self.ride)
        self.assertIn("ReferenceEquals( Camera.Mode, level.FreeWalk )", self.ride)
        prepare = self.ride.split("private void PrepareRideStatic(", 1)[1].split(";", 1)[0]
        self.assertIn("Stopwatch.GetTimestamp()", prepare)
        for forbidden in ("context.Delta", "SimulationTimeScale", "Time.Delta", "CamcorderArmed"):
            self.assertNotIn(forbidden, prepare)
        for forbidden in ("WorldRandom", "GuestSimulation", "ParkSave", "StateHasher", "static bool", "static double"):
            self.assertNotIn(forbidden, self.timer)

    def test_positive_decision_precedes_charge_and_draw_only_reads_it(self):
        prepare = body(self.timer, "public void Prepare(")
        self.assertIn("DurationMilliseconds = 300", self.timer)
        self.assertIn("eyeMode != previousEyeMode", prepare)
        self.assertLess(prepare.index("ShowStatic = RemainingMilliseconds > 0"), prepare.index("RemainingMilliseconds -= now - previousStamp"))
        draw = body(self.ride, "private void DrawRideStatic(")
        self.assertIn("!rideStatic.ShowStatic", draw)
        self.assertIn("DrawStatic( context, rideStatic.NoiseFrame )", draw)
        for forbidden in ("context.Time", "Prepare(", "SkipFrame(", "RideStaticClock", "=", "Random"):
            self.assertNotIn(forbidden, draw)
        self.assertIn("public void SkipFrame() => ShowStatic = false;", self.timer)

    def test_residuals_are_not_retired_or_hidden(self):
        register = read("source/OpenTPW/UI/Original/UiApproximations.cs")
        self.assertIn('("UI-094",', register)
        self.assertNotIn("the static's timer", register)
        proposal = read("docs/proposals/ui-094-verify-ride-view-residual-timing.md")
        for qualification in ("two post-draw", "0x10", "320 x 240", "no `dotnet`", "not retired"):
            self.assertIn(qualification, proposal)
        self.assertIn("that algorithm/sequence is deferred, not untraced", self.ride)


if __name__ == "__main__":
    unittest.main()
