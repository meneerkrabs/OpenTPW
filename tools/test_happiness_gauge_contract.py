"""UI-901 source integration guards and derived math; not C# execution or rendered fidelity proof."""
from pathlib import Path
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


def f32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


class HappinessGaugeContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.hud = read("source/OpenTPW/Hud/ParkHud.cs")
        cls.gauge = read("source/OpenTPW/Hud/ParkHud.Gauge.cs")
        cls.vertical = read("source/OpenTPW/UI/Original/UiVerticalGauge.cs")
        cls.research = read("source/OpenTPW/Hud/ParkHud.ResearchWindow.cs")

    def test_actual_hud_has_one_independently_anchored_fill_and_native_shell(self):
        self.assertEqual(1, self.hud.count('Id = "gaugeFill"'))
        self.assertIn('Id = "gaugeFill", Bounds = new UiRect( 85, 1100, 59, 224 ), Anchor = UiAnchor.BottomLeft', self.hud)
        self.assertIn('Id = "gauge", Model = "gauge", Bounds = new UiRect( 67, 1080, 96, 265 ), Anchor = UiAnchor.BottomLeft, Help = strings.Help( 477 )', self.hud)

    def test_happiness_draw_is_read_only_uses_meter_and_zero_when_absent(self):
        draw = self.gauge.split("internal sealed class HappinessGaugeFill", 1)[1]
        self.assertIn('ArtPath = "/ui/textures/meter.wct"', draw)
        self.assertIn('UiVerticalGauge.Draw( context.Batch, ScreenRect( context.Canvas ), owner.parkHappiness is { } mean ? GaugeValue( mean ) : 0, ArtPath )', draw)
        for forbidden in ("AddRectangle", "GaugeColour", "SampleHappiness", "owner.parkHappiness =", "Focusable", "ResolveTexture", "catch"):
            self.assertNotIn(forbidden, draw)
        self.assertNotIn("GaugeColour", self.gauge)

    def test_helper_crops_same_boundary_with_white_art_and_no_fallback(self):
        self.assertIn('Math.Clamp( value, 0, UiGauge.FullValue ) / (float)UiGauge.FullValue', self.vertical)
        self.assertIn('UiTexture.Image( texturePath )', self.vertical)
        self.assertIn('new NVector2( 0.5f, 0 ), new NVector2( 1, 1 - q ), RgbaByte.White', self.vertical)
        self.assertIn('new NVector2( 0, 1 - q ), new NVector2( 0.5f, 1 ), RgbaByte.White', self.vertical)
        self.assertEqual(2, self.vertical.count('batch.AddQuad('))
        self.assertNotIn('AddRectangle', self.vertical)
        self.assertNotIn('catch', self.vertical)

    def test_sampling_and_research_ownership_remain_separate(self):
        update = body(self.hud, 'internal bool Update(')
        self.assertEqual(1, update.count('SampleHappiness( context.Delta );'))
        sample = body(self.gauge, 'private void SampleHappiness(')
        for expected in ('happinessSampled -= delta;', 'if ( happinessSampled > 0 )', 'happinessSampled = HappinessSampleSeconds;', 'guests.Guests.Where( guests.IsInPark )'):
            self.assertIn(expected, sample)
        self.assertIn('HappinessSampleSeconds = 2f;', self.gauge)
        self.assertIn('UiVerticalGauge.Draw( batch, bounds, value, ArtPath )', self.research)
        self.assertIn('ArtPath = "/ui/textures/effortguage.wct"', self.research)
        self.assertIn('Value = () => workloadKnob.Limit', self.research)
        self.assertIn('so the first projection of raw 0 leaves the ball at 399', self.research)

    def test_resource_failure_keeps_existing_skip_policy(self):
        renderer = read('source/OpenTPW/UI/Original/UiRenderer.cs')
        load = body(renderer, 'private ResourceSet? GetResourceSet(')
        self.assertIn('if ( failedImages.Contains( texture.ImagePath ) )\n\t\t\t\treturn null;', load)
        self.assertIn('failedImages.Add( texture.ImagePath );', load)
        self.assertIn('exception is IOException or InvalidDataException', load)
        self.assertIn('if ( set != null )', renderer)
        self.assertNotIn('meter.wct', renderer)

    def test_all_native_percent_uv_values_equal_direct_fraction(self):
        # Arithmetic oracle only: private static evidence established this listener's formula.
        for raw in range(1025):
            q = raw / 1024
            self.assertEqual(q, f32(f32(f32(raw * f32(1 / 1024)) * 100) * f32(.01)), raw)
        for raw, split in ((0, 1324), (256, 1268), (511, 1212.21875), (512, 1212), (768, 1156), (1023, 1100.21875), (1024, 1100)):
            self.assertEqual(split, 1100 + 224 * (1 - raw / 1024))
        self.assertEqual(511, int(50 * 1024 * f32(.01)))
        self.assertEqual(1023, int(100 * 1024 * f32(.01)))


if __name__ == '__main__':
    unittest.main()
