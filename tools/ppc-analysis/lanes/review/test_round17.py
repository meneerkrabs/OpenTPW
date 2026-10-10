"""Round 17: synthetic models behind scenarios 1dacb25 and the batch-2 3f87c6b line-ending check.

- ``TbStringBase<c>::operator<`` as traced (strncmp on unsigned bytes over the shorter length, shorter string
  less on an equal prefix) orders every NUL-free key exactly like Python ``bytes``, so ``sorted`` is the Mac
  writer's theme order. Exhaustive over a small alphabet that includes 0x01, 0x7f, 0x80 and 0xff.
- A short i32 import keeps the delivered bytes at the member's address (big-endian, so the high-order bytes)
  and the old member value in the rest. Synthetic, and it assumes FSRead stores bytes before EOF.
- With OPENTPW_REVIEW_REPO naming a checkout that holds 3f87c6b, Game.cs is checked for the CRLF -> LF
  conversion (the only line-ending change in batch 2 against origin/main 7bc6c59).

No original file is read and nothing here says anything about the PC build or a real gms.dat.
"""
from __future__ import annotations

import itertools
import os
import struct
import subprocess
import unittest


def mac_less(a: bytes, b: bytes) -> bool:
    n = min(len(a), len(b))
    for x, y in zip(a[:n], b[:n]):
        if x != y:
            return x < y
    return len(a) < len(b)


def short_import(old: int, delivered: bytes) -> int:
    assert 0 <= len(delivered) < 4
    member = bytearray(struct.pack('>i', old))
    member[:len(delivered)] = delivered
    return struct.unpack('>i', bytes(member))[0]


class ThemeKeyOrder(unittest.TestCase):
    def test_mac_comparator_is_python_bytes_order_for_nul_free_keys(self):
        alphabet = (0x01, 0x41, 0x61, 0x7f, 0x80, 0xe9, 0xff)
        keys = [bytes(k) for n in range(4) for k in itertools.product(alphabet, repeat=n)]
        self.assertEqual(400, len(keys))
        for a in keys:
            for b in keys:
                self.assertEqual(mac_less(a, b), a < b, (a, b))

    def test_key_stops_at_first_nul(self):
        # const char* constructor: strlen + strcpy. Two names sharing a prefix before NUL share one key.
        self.assertEqual(b'a\0x'.split(b'\0', 1)[0], b'a\0y'.split(b'\0', 1)[0])


class ShortImport(unittest.TestCase):
    def test_delivered_bytes_take_the_high_order_end(self):
        self.assertEqual(0x7f000000, short_import(0, b'\x7f'))
        self.assertEqual(0x1234ffff, short_import(-1, b'\x12\x34') & 0xffffffff)
        self.assertEqual(5, short_import(5, b''))


@unittest.skipUnless(os.environ.get('OPENTPW_REVIEW_REPO'), 'OPENTPW_REVIEW_REPO not set')
class Batch2LineEndings(unittest.TestCase):
    def show(self, rev: str) -> bytes:
        repo = os.environ['OPENTPW_REVIEW_REPO']
        try:
            return subprocess.run(['git', '-C', repo, 'show', f'{rev}:source/OpenTPW/Client/Game.cs'],
                                  check=True, capture_output=True).stdout
        except subprocess.CalledProcessError:
            raise unittest.SkipTest(f'{rev} not in {repo}')

    def test_3f87c6b_converted_game_cs_from_crlf_to_lf(self):
        parent, commit = self.show('6ec5d28'), self.show('3f87c6b')
        self.assertEqual((419, 419), (parent.count(b'\r\n'), parent.count(b'\n')))
        self.assertEqual((0, 419), (commit.count(b'\r\n'), commit.count(b'\n')))
        # Behaviour change is one line once CR is ignored.
        changed = [p for p, c in zip(parent.replace(b'\r\n', b'\n').split(b'\n'), commit.split(b'\n')) if p != c]
        self.assertEqual(1, len(changed))
        self.assertIn(b'IntroPlaylist.ShouldPlay', [c for c in commit.split(b'\n') if b'var playIntro' in c][0])


if __name__ == '__main__':
    unittest.main()
