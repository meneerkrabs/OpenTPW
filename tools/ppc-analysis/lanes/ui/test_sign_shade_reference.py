"""Synthetic reference cases and privately supplied native source witnesses."""
import os
from pathlib import Path
import unittest

from corpus import wad
from sign_evidence import metadata, native_surface
from sign_shade_reference import SignShadeReference as Reference


class ShadeTests(unittest.TestCase):
    def test_gaussian_keeps_even_size_and_normalizes_actual_weights(self):
        kernel = Reference.gaussian_kernel(1.25)
        self.assertEqual(len(kernel), 6)
        self.assertAlmostEqual(sum(sum(row) for row in kernel), 1)
        self.assertGreater(kernel[2][2], kernel[3][3])
        with self.assertRaises(ValueError):
            Reference.gaussian_kernel(0)

    def test_normal_mask_keeps_alpha_separate_and_rejects_morphology(self):
        coverage = bytes((0, 0, 0, 0, 80, 0, 0, 0, 0))
        self.assertEqual(Reference.normal_mask(coverage, 3, 3, 0, 0), bytes((0, 0, 0, 0, 255, 0, 0, 0, 0)))
        blurred = Reference.normal_mask(coverage, 3, 3, 0, 1)
        self.assertEqual(sum(value != 0 for value in blurred), 1)
        self.assertTrue(0 < blurred[4] < 80)
        self.assertEqual(coverage[4], 80, 'normal-shape processing does not overwrite alpha')
        with self.assertRaisesRegex(ValueError, 'morphology'):
            Reference.normal_mask(coverage, 3, 3, 1, 1)

    def test_separable_average_truncates_after_each_axis(self):
        pixels = bytes((1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 3, 3, 3, 3))
        self.assertEqual(Reference.descimate_argb(pixels, 2, 2, 1, 1), bytes(4))
        self.assertEqual(sum(pixels[::4]) // 4, 1, 'single box average would differ')

    def test_fractional_bucket_counts_and_unchanged_channels(self):
        pixels = b''.join(bytes((v, v + 1, v + 2, v + 3)) for v in (10, 20, 30, 40, 50))
        self.assertEqual(Reference.descimate_argb(pixels, 5, 1, 2, 1), bytes((15, 16, 17, 18, 40, 41, 42, 43)))
        self.assertEqual(Reference.descimate_argb(pixels, 5, 1, 5, 1), pixels)
        with self.assertRaises(ValueError):
            Reference.descimate_argb(pixels, 5, 1, 6, 1)

    def test_forward_gradient_and_explicit_material_pipeline(self):
        self.assertEqual(Reference.relief_normal(80, 80, 80, 10), (0, 0, 1))
        normal = Reference.relief_normal(0, 255, 0, 1)
        self.assertAlmostEqual(normal[0], 2 ** -0.5)
        self.assertEqual(normal[1], 0)
        self.assertEqual(Reference.shade_sample((40, 80, 120), 13, 0, 0, 0, (1, 0, 1, 0, 0, 2, 0, 0)), (13, 40, 80, 120))
        self.assertEqual(Reference.shade_sample((40, 80, 120), 0, 0, 0, 0, (1, 0, 0.5, 0.5, 0, 2, 0, 0)), (0, 0, 0, 0))

    def test_integer_blend_is_not_divide_by255(self):
        self.assertEqual(Reference.effect_blend((255, 255, 0, 128), (0, 0, 255, 0)), (255, 254, 0, 127))
        self.assertEqual(Reference.effect_blend((0, 100, 100, 100), (70, 20, 30, 40)), (70, 20, 30, 40))
        self.assertEqual(Reference.paint_blend(255, (255, 0, 128, 255), (0, 0, 0, 0)), (254, 252, 0, 126))

    def test_style_two_combines_masks_with_first_paint_color(self):
        self.assertEqual(Reference.composition_plan((2, 2), False), ['clear', 'max-paint-masks-then-paint-0', 'effect-0', 'effect-1'])
        self.assertEqual(Reference.composition_plan((1, 2), True, True), ['clear', 'extra-image-alpha-blit', 'paint-1', 'effect-1', 'paint-0', 'effect-0'])
        with self.assertRaises(ValueError):
            Reference.composition_plan((3, 2), False)

    def test_split_and_reverse_rows_in_both_pack_modes(self):
        pixel0 = bytes((0x12, 0x34, 0x56, 0x78))
        pixel1 = bytes((0xaa, 0xbb, 0xcc, 0xdd))
        pixel2 = bytes((1, 2, 3, 4))
        pixel3 = bytes((5, 6, 7, 8))
        source = pixel0 + pixel1 + pixel2 + pixel3 + bytes(8)
        self.assertEqual(Reference.split_pack(source, 1, 2, 1), (bytes((4, 3, 2, 1, 0x78, 0x56, 0x34, 0x12)), bytes((8, 7, 6, 5, 0xdd, 0xcc, 0xbb, 0xaa))))
        self.assertEqual(Reference.split_pack(source, 1, 2, 2), (bytes((0, 0, 0x35, 0x71)), bytes((0, 0, 0xbc, 0xda))))
        with self.assertRaises(ValueError):
            Reference.split_pack(source, 1, 2, 3)


@unittest.skipUnless(os.environ.get('UI_EVIDENCE_BIN_ROOT') and os.environ.get('UI_EVIDENCE_PC_DATA'), 'private native/sign paths not supplied')
class NativeShadeTests(unittest.TestCase):
    def test_filter_normal_blend_and_pack_operands(self):
        result = native_surface(Path(os.environ['UI_EVIDENCE_BIN_ROOT']))
        self.assertEqual(result['mode_2'], 'RGBA4444, big-endian native store')
        self.assertIn('reverse destination', result['rows'])
        self.assertIn('forward-gradient boundary validity', result['unproved'])

    def test84_sign_sources_and_materials_fit_reference_domain(self):
        root = Path(os.environ['UI_EVIDENCE_PC_DATA'])
        paths = list(root.glob('levels/*/rides/*.wad')) + list(root.glob('levels/*/features/*.wad')) + [root / 'lobby.wad']
        count, zero_morphology = 0, 0
        for path in paths:
            for name, data in wad(path).items():
                if not name.lower().endswith('.sgn'):
                    continue
                count += 1
                sign = metadata(data)
                for image, effect in zip(sign['images'][:2], sign['effects']):
                    # Private source bytes are consumed, never written or printed.
                    at = image['offset'] + 12
                    source = data[at:at + image['payload_bytes']]
                    filtered = Reference.descimate_argb(source, 16, 128, 16, 64)
                    self.assertEqual(len(filtered), 16 * 64 * 4)
                    color = Reference.shade_sample((40, 80, 120), 73, 80, 90, 100, effect[1:9])
                    self.assertEqual(color[0], 73)
                    self.assertTrue(all(0 <= channel <= 255 for channel in color))
                    if effect[0] == 0:
                        normal = Reference.normal_mask(bytes((0, 0, 0, 0, 255, 0, 0, 0, 0)), 3, 3, 0, effect[2])
                        self.assertEqual(len(normal), 9)
                        zero_morphology += 1
        self.assertEqual(count, 84)
        self.assertEqual(zero_morphology, 78)


if __name__ == '__main__':
    unittest.main()
