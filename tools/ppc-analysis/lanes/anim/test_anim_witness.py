"""Witness tests: every check passes on an image holding exactly its operands and fails on a changed one.

The real-binary test runs only with OPENTPW_PPC_BIN_ROOT (the Feral `bin` directory); without it
the synthetic tests still prove that each check rejects a wrong key operand.
"""
import os
import struct
import sys
import unittest
from dataclasses import replace
from pathlib import Path
from tempfile import TemporaryDirectory

sys.path.insert(0, str(Path(__file__).resolve().parent))

import anim_witness as aw  # noqa: E402


class SyntheticImage:
    """Code words, switch relocations and TOC floats taken from the checks themselves."""

    def __init__(self, checks):
        self.words, self.pointers, self.floats = {}, {}, {}
        for check in checks:
            if check.kind == 'switch':
                self.pointers[check.offset] = check.fields[1]
                continue
            self.words[check.offset] = check.expected_word()
            if check.kind == 'toc_float':
                self.floats[check.fields[1]] = check.fields[2]

    def w(self, offset):
        return self.words.get(offset, 0x60000000)  # ori r0, r0, 0 (nop) elsewhere

    def code_pointer(self, offset):
        return self.pointers.get(offset)

    def toc_float(self, slot):
        return self.floats[slot]


def images_for(checks):
    return {container: SyntheticImage([c for c in checks if c.container == container])
            for container in (aw.APP, aw.ENGINE)}


def mutated(check):
    """The same check with its key operand changed (immediate, mask, target, value or handler)."""
    k, f = check.kind, check.fields
    if k == 'd':
        return replace(check, fields=(f[0], f[1], f[2], f[3] + 1 if f[3] != 0x7fff else f[3] - 1))
    if k == 'rlwinm':
        return replace(check, fields=(f[0], f[1], f[2], (f[3] + 1) & 31, (f[4] + 1) & 31, f[5]))
    if k == 'x':
        return replace(check, fields=(f[0], f[1], f[2] ^ 1, f[3], *f[4:]))
    if k == 'bl':
        return replace(check, fields=(f[0] + 4,))
    if k == 'toc_float':
        return replace(check, fields=(f[0], f[1], f[2] * 2 + 1))
    if k == 'switch':
        return replace(check, fields=(f[0], f[1] + 4, f[2]))
    raise AssertionError(k)


ALL = aw.CHECKS + aw.SWITCH_CHECKS


class SyntheticWitnessTests(unittest.TestCase):
    def test_every_check_passes_on_its_own_operands(self):
        images = images_for(ALL)
        for check in ALL:
            with self.subTest(check=f'{check.container}:{check.offset:#x}'):
                fact = aw.verify(check, images[check.container])
                self.assertEqual(fact['offset'], hex(check.offset))

    def test_every_check_fails_on_a_wrong_key_operand(self):
        images = images_for(ALL)
        for check in ALL:
            with self.subTest(check=f'{check.container}:{check.offset:#x}'):
                with self.assertRaises(aw.WitnessError):
                    aw.verify(mutated(check), images[check.container])

    def test_named_key_operands(self):
        # The image holds the original fields; the expectation is wrong in exactly one operand.
        images = images_for(ALL)

        def find(offset, container=aw.APP):
            return next(c for c in ALL if c.offset == offset and c.container == container)

        wrong = {
            'UV pass mask 0x20000 instead of 0x10000': replace(find(0xa56c0), fields=(0, 0, 0, 14, 14, True)),
            'UV destination +100 (face normals) instead of +104': replace(find(0xa8474), fields=('lwz', 4, 29, 100)),
            'FLUSHANIM calls the trigger instead of the deferred flush': replace(find(0xafa8c), fields=(0xa6cc0,)),
            'TRIGANIM subtracts 299': replace(find(0xafb20), fields=('addi', 0, 3, -299)),
            'LOOPANIM passes flags 0': replace(find(0xafd78), fields=('addi', 6, 0, 0)),
            'advisor AnimID 4': replace(find(0x29418), fields=('addi', 4, 0, 4)),
            'start length divides by 25': replace(find(0xa6c38), fields=(0, 0x516c, 25.0)),
            'loader gate compares with 222': replace(find(0x3fc78, aw.ENGINE), fields=('cmpli', 0, 0, 222)),
            'channel stride 48': replace(find(0xa7a60), fields=('addi', 27, 27, 48)),
            'WAITANIM_CH adds with carry (addc) instead of add': replace(find(0xb035c), fields=(3, 4, 3, 10)),
            'no-object path returns 1000': replace(find(0xafb1c), fields=('addi', 3, 0, 1000)),
            'opcode 15 handled by TRIGANIM': replace(find(aw.SWITCH_TABLE + 4 * 15), fields=(15, 0xafa94, 'FLUSHANIM')),
        }
        for name, check in wrong.items():
            with self.subTest(name):
                with self.assertRaises(aw.WitnessError):
                    aw.verify(check, images[check.container])

    def test_witness_run_on_synthetic_images_reports_scans(self):
        images = images_for(ALL)
        # Give each scan range the record tests it must find, after a record load.
        app, engine = images[aw.APP], images[aw.ENGINE]
        for container, start, end, register, expected in aw.RECORD_SCANS:
            image = images[container]
            # Check words inside the range contribute masks of the expected set only; add the rest
            # in a free slot (plus the scan's follow window) inside the range.
            span = 8 * len(expected) + 4 * 13
            at = next(o for o in range(start, end - span, 4)
                      if not any(p in image.words for p in range(o, o + span, 4)))
            for bit in sorted(expected):
                image.words[at] = aw.d_word('lwz', 9, register, 4)
                image.words[at + 4] = aw.rlwinm_word(9, 0, 0, 31 - (bit.bit_length() - 1),
                                                     31 - (bit.bit_length() - 1), True)
                at += 8
        report = aw.run(images)
        self.assertEqual(report['checks'], len(ALL) + len(aw.RECORD_SCANS) + len(aw.TOKEN_SCANS))
        self.assertTrue(app and engine)
        # A _CH handler that stores the LOOPANIM token breaks the run.
        app.words[0xb0400] = aw.d_word('stw', 0, 31, 168)
        with self.assertRaisesRegex(aw.WitnessError, 'LOOPANIM_CH'):
            aw.run(images)
        del app.words[0xb0400]
        # An unread bit tested in the sampler range breaks the run.
        app.words[0xa4f68 + 0x400] = aw.d_word('lwz', 9, 28, 4)
        app.words[0xa4f68 + 0x404] = aw.rlwinm_word(9, 0, 0, 18, 18, True)  # 0x2000
        with self.assertRaises(aw.WitnessError):
            aw.run(images)

    def test_record_mask_scan_follows_register_until_overwritten(self):
        image = SyntheticImage([])
        image.words.update({
            0x0: aw.d_word('lwz', 4, 28, 4),
            0x4: aw.rlwinm_word(4, 0, 0, 22, 22, True),  # 0x200
            0x8: (16 << 26) | (12 << 21) | (2 << 16) | 0x10,  # bt eq (branches do not stop the scan)
            0xc: aw.rlwinm_word(4, 0, 0, 31, 31, True),  # clrlwi. 0x1
            0x10: aw.d_word('addi', 4, 0, 0),  # r4 overwritten
            0x14: aw.rlwinm_word(4, 0, 0, 17, 17, True),  # not the record word any more
            0x20: aw.d_word('lwz', 3, 27, 4),  # other base register: ignored
            0x24: aw.rlwinm_word(3, 0, 0, 18, 18, True),
        })
        self.assertEqual(aw.record_flag_masks(image, 0, 0x40, 28), {0x200, 0x1})

    def test_identity_rejects_unknown_container_before_parsing(self):
        with TemporaryDirectory() as directory:
            path = Path(directory) / aw.APP
            path.write_bytes(b'synthetic, not the game')
            with self.assertRaisesRegex(aw.WitnessError, 'identity'):
                aw.Image(path)

    def test_encoders(self):
        self.assertEqual(aw.d_word('lwz', 0, 28, 4), 0x801c0004)
        self.assertEqual(aw.rlwinm_word(0, 0, 0, 15, 15, True), 0x540003df)
        self.assertEqual(aw.bl_word(0xa56d8, 0xa83e8), 0x48002d11)
        self.assertEqual(aw.rotate_mask(24, 22), 0xfffffeff)
        self.assertEqual(aw.x_word(0, 3, 0, 32), 0x7c030040)
        with self.assertRaises(aw.WitnessError):
            aw.bl_word(0, 2)


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'OPENTPW_PPC_BIN_ROOT (Feral bin directory) not set')
class OriginalBinaryTests(unittest.TestCase):
    def test_identity_pinned_witness(self):
        images = aw.load_images(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        report = aw.run(images)
        self.assertEqual(report['checks'], len(ALL) + len(aw.RECORD_SCANS) + len(aw.TOKEN_SCANS))
        self.assertEqual(set(report['record_flag_scans'][f'{aw.APP}:0xa4f68']),
                         {hex(bit) for bit in aw.READ_RECORD_BITS})
        # The same mutation must fail against the real words too.
        for check in (ALL[0], ALL[len(ALL) // 2], ALL[-1]):
            with self.assertRaises(aw.WitnessError):
                aw.verify(mutated(check), images[check.container])

    def test_toc_floats_are_single_precision_literals(self):
        images = aw.load_images(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        values = {c.fields[1]: images[c.container].toc_float(c.fields[1]) for c in ALL if c.kind == 'toc_float'}
        self.assertEqual(values[0x516c], 30.0)
        self.assertEqual(values[0x5170], 1000.0)
        self.assertEqual(struct.pack('>f', values[0x5168]), struct.pack('>f', 33.333332))


if __name__ == '__main__':
    unittest.main()
