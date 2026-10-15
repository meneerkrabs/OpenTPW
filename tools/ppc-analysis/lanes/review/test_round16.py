"""Round 16: independent decode of the MD2 channel clock selector claimed by formats f443475.

The formats witness checks the three channel reads of a scene clock through its own field helpers. This test
decodes the same nine words with a separate, minimal PowerPC field split: channel word +0 is tested with
``rlwinm. rX,rX,0,25,25`` (mask 0x40), ``beq`` skips to the +16400 load (clock A), and the fall-through loads
+16408 (clock B). It says nothing about who sets the bit, about clock rates, or about the PC build.

Runs only with OPENTPW_PPC_BIN_ROOT (the runner's --mac-bin) naming the identified Feral Mac bin directory.
"""
from __future__ import annotations

import hashlib
import os
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef  # noqa: E402

APP_SHA256 = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
CLOCK_A, CLOCK_B = 16400, 16408
# (name, channel load, rlwinm., beq, clock B load, branch over, clock A load)
SITES = (
    ('fresh start 0xa6398', 0xa63e8, 0xa63ec, 0xa63f0, 0xa63f4, 0xa63f8, 0xa63fc),
    ('channel update 0xa7190', 0xa7288, 0xa728c, 0xa7290, 0xa7294, 0xa7298, 0xa729c),
    ('all-ended check 0xa7000', 0xa7070, 0xa7074, 0xa7078, 0xa707c, 0xa7080, 0xa7084),
)


def fields(word: int) -> tuple[int, int, int, int]:
    return word >> 26, (word >> 21) & 31, (word >> 16) & 31, struct.unpack('>h', struct.pack('>H', word & 0xffff))[0]


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'OPENTPW_PPC_BIN_ROOT not set')
class ChannelClockSelector(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = Path(os.environ['OPENTPW_PPC_BIN_ROOT']) / 'SimThemePark.data'
        raw = path.read_bytes()
        if hashlib.sha256(raw).hexdigest() != APP_SHA256:
            raise unittest.SkipTest('not the identified Feral Mac SimThemePark.data')
        cls.code = bytes(pef.load(str(path)).code.data)

    def word(self, offset: int) -> int:
        return struct.unpack_from('>I', self.code, offset)[0]

    def test_bit_0x40_selects_clock_b_at_every_channel_read(self):
        for name, load, test, branch, clock_b, over, clock_a in SITES:
            with self.subTest(name):
                op, rt, ra, imm = fields(self.word(load))
                self.assertEqual((32, 0), (op, imm), 'lwz rX,0(channel)')
                word = self.word(test)
                self.assertEqual((21, rt, rt), fields(word)[:3], 'rlwinm on the loaded word')
                self.assertEqual((0, 25, 25, 1), ((word >> 11) & 31, (word >> 6) & 31, (word >> 1) & 31, word & 1),
                                 'mask 0x40, record form')
                word = self.word(branch)
                self.assertEqual((16, 12, 2), fields(word)[:3], 'beq cr0')
                self.assertEqual(clock_a, branch + (word & 0xfffc), 'bit clear branches to the clock A load')
                self.assertEqual(18, self.word(over) >> 26, 'clock B path branches over the clock A load')
                b_op, _, b_base, b_imm = fields(self.word(clock_b))
                a_op, _, a_base, a_imm = fields(self.word(clock_a))
                self.assertEqual((32, CLOCK_B), (b_op, b_imm))
                self.assertEqual((32, CLOCK_A), (a_op, a_imm))
                self.assertEqual(b_base, a_base, 'both clocks from the same global block')


if __name__ == '__main__':
    unittest.main()
