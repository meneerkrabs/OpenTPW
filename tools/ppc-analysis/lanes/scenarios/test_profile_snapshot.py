"""Profile snapshot reader, writer and envelope. Synthetic bytes only (no gms.dat exists in the
assets), except the witness mutations, which run only with OPENTPW_MAC_BIN set.

Run: python3 -m unittest discover -s tools/ppc-analysis/lanes/scenarios -v
"""
import dataclasses
import json
import os
from pathlib import Path
import struct
import unittest

import player_file_evidence as player_file
import profile_snapshot as snap
import scenario_evidence as evidence
from scenario_evidence import Evidence, pef

GLOBAL, SECRET = 4, 4 + 4              # byte offsets after the version
MODE = 4 + 6 + 8                       # mEasyModeUser is file byte 18
THEMES = MODE + 3 + 4                  # first theme entry
THEME_BYTES = 160


def theme(name: bytes, local=(0,) * 6, seed=0) -> bytes:
    out = bytes(local)
    for i in range(4):
        out += struct.pack('<Bi', (seed + i) & 0xff, -1000 * (i + 1) + seed)
    for i in range(33):
        out += struct.pack('<HH', (seed + 2 * i) & 0xffff, 0xffff - i)
    return struct.pack('<I', len(name)) + name + out + bytes((seed & 1, 1))


SETTINGS = b''.join((bytes((0x00, 0x00, 0x01, i)) + struct.pack('<I', 0x10 + i)) for i in range(4)) + \
    bytes((1, 0, 1, 1, 0, 0, 2))


def gms(version=12, glob=(1, 0, 2, 0), secret=(0, 3), spent=5, extra=1, mode=1, swear=1, first=0,
        themes=((b'jungle', (1, 1, 0, 0, 0, 0)), (b'halloween', (0,) * 6), (b'space', (9,) * 6)),
        theme_count=None, settings=SETTINGS, mystery=(7, 300), mystery_count=None, tail=b'') -> bytes:
    out = struct.pack('<I', version & 0xffffffff) + bytes(glob) + bytes(secret) + struct.pack('<ii', spent, extra)
    out += bytes((mode, swear, first))
    out += struct.pack('<i', len(themes) if theme_count is None else theme_count)
    out += b''.join(theme(name, local, seed) for seed, (name, local) in enumerate(themes))
    out += settings
    out += struct.pack('<i', len(mystery) if mystery_count is None else mystery_count)
    return out + b''.join(struct.pack('<H', r) for r in mystery) + tail


def read(raw, policy='mac-partial'):
    return snap.read_profile_snapshot(raw, policy)


class SnapshotReadTests(unittest.TestCase):
    def test_complete_read_preserves_raw_values_and_file_order(self):
        s = read(gms(mode=2, swear=7))
        self.assertTrue(s.complete)
        self.assertEqual((s.version, s.global_tickets, s.secret_tickets, s.spent_tickets, s.extra_keys),
                         (12, (1, 0, 2, 0), (0, 3), 5, 1))
        self.assertEqual((s.easy_mode_user, s.swear_filter_on, s.first_time_player), (2, 7, 0))
        self.assertEqual([t.name for t in s.themes], [b'jungle', b'halloween', b'space'])  # not sorted
        self.assertEqual(s.themes[1].awards[0], (1, -999))
        self.assertEqual(s.themes[2].sign_names[32], (2 + 64, 0xffff - 32))
        self.assertEqual([n for n, _ in s.settings], list(snap.SETTING_NAMES))
        self.assertEqual(b''.join(raw for _, raw in s.settings), SETTINGS)
        self.assertEqual(snap.setting_words(s.settings[1][1]), (0x00000101, 0x11))
        self.assertEqual(s.mystery, (7, 300))
        self.assertEqual(s.fields_read, snap.PLAYER_FIELDS)
        self.assertEqual(s.issues, ())

    def test_agrees_with_the_existing_reference_reader(self):
        for raw in (gms(), gms()[:MODE], gms()[:THEMES + 30], gms(version=13),
                    gms(themes=((b'a', (1,) * 6), (b'a', (0,) * 6)))):
            old, new = player_file.read_mac_player_file(raw), read(raw)
            with self.subTest(size=len(raw)):
                self.assertEqual(old['ok'], new.complete)
                self.assertEqual(old['record']['mEasyModeUser'], new.easy_mode_user)
                self.assertEqual(old['record']['mExtraKeys'], new.extra_keys)
                self.assertEqual(list(old['themes']), [t.name for t in new.themes])

    def test_snapshot_is_immutable(self):
        s = read(gms())
        with self.assertRaises(dataclasses.FrozenInstanceError):
            s.extra_keys = 9
        with self.assertRaises(dataclasses.FrozenInstanceError):
            s.themes[0].name = b'x'
        self.assertIsInstance(s.themes, tuple)
        self.assertIsInstance(s.mystery_set, frozenset)

    def test_policy_is_required_and_closed(self):
        with self.assertRaises(TypeError):
            snap.read_profile_snapshot(gms())
        with self.assertRaises(ValueError):
            snap.read_profile_snapshot(gms(), 'lenient')


class VersionBoundaryTests(unittest.TestCase):
    def test_below_twelve_is_rejected_by_both(self):
        for version in (0, 11):
            s = read(gms(version=version))
            self.assertFalse(s.complete)
            self.assertEqual((s.failed_at, s.easy_mode_user, s.extra_keys), ('version', 0, 0))
            with self.assertRaises(snap.StrictReject) as cm:
                read(gms(version=version), 'strict-host')
            self.assertEqual(cm.exception.reason, 'rejected-version')

    def test_unsigned_gate_accepts_high_versions_with_one_layout_but_strict_does_not(self):
        for version in (13, 0x7fffffff, 0x80000000, 0xffffffff):
            with self.subTest(version=hex(version)):
                s = read(gms(version=version))
                self.assertTrue(s.complete)
                self.assertEqual(s.version, version)
                self.assertEqual(s.themes, read(gms()).themes)
                self.assertIn('unsigned Mac gate', s.issues[0])
                with self.assertRaises(snap.StrictReject) as cm:
                    read(gms(version=version), 'strict-host')
                self.assertEqual((cm.exception.reason, cm.exception.offset), ('unknown-version', 0))

    def test_twelve_passes_strict(self):
        self.assertTrue(read(gms(), 'strict-host').complete)


class TruncationTests(unittest.TestCase):
    def test_every_cut_is_partial_under_mac_and_rejected_under_strict(self):
        raw = gms()
        for n in range(len(raw)):
            s = read(raw[:n])
            self.assertFalse(s.complete, n)
            self.assertLessEqual(s.failed_offset, n)
            with self.assertRaises(snap.StrictReject) as cm:
                read(raw[:n], 'strict-host')
            self.assertEqual(cm.exception.reason, 'truncated')

    def test_mode_survives_only_past_byte_eighteen(self):
        raw = gms(mode=1)
        self.assertEqual(read(raw[:MODE]).easy_mode_user, 0)
        self.assertEqual(snap.selection_game_type(read(raw[:MODE]), 0), 0)
        cut = read(raw[:MODE + 1])
        self.assertEqual((cut.easy_mode_user, cut.failed_at), (1, 'mSwearFilterOn'))
        self.assertEqual(snap.selection_game_type(cut, 0), 2)

    def test_partial_overlay_keeps_earlier_members_and_resets_later_ones(self):
        s = read(gms(spent=4, extra=3)[:SECRET + 2 + 4 + 2])   # cut inside mExtraKeys
        self.assertEqual((s.global_tickets, s.secret_tickets, s.spent_tickets), ((1, 0, 2, 0), (0, 3), 4))
        self.assertEqual((s.extra_keys, s.swear_filter_on, s.first_time_player), (0, 1, 1))
        self.assertEqual(s.fields_read, snap.PLAYER_FIELDS[:3])
        self.assertIn('not traced', s.issues[-1])

    def test_cut_theme_is_not_inserted_and_earlier_themes_stay(self):
        raw = gms()
        second = THEMES + 4 + 6 + THEME_BYTES
        s = read(raw[:second + 4 + 9 + 100])
        self.assertEqual([t.name for t in s.themes], [b'jungle'])
        self.assertEqual(s.failed_at, 'theme record')
        self.assertEqual(s.settings, ())

    def test_settings_read_before_a_cut_are_held_but_not_complete(self):
        raw = gms()
        settings_at = len(raw) - len(SETTINGS) - 4 - 4
        s = read(raw[:settings_at + 8 * 2 + 3])
        self.assertEqual([n for n, _ in s.settings], ['SFXVolume', 'MusicVolume'])
        self.assertFalse(s.settings_complete)
        self.assertEqual(s.failed_at, 'SpeechVolume')

    def test_oversized_name_length_is_flagged_as_undefined_on_the_mac(self):
        raw = bytearray(gms())
        struct.pack_into('<I', raw, THEMES, 0xffffffff)
        s = read(bytes(raw))
        self.assertEqual((s.complete, s.failed_at, s.themes), (False, 'theme name', ()))
        self.assertIn('not defined by the trace', s.issues[-1])
        with self.assertRaises(snap.StrictReject):
            read(bytes(raw), 'strict-host')


class StructureTests(unittest.TestCase):
    def test_duplicate_theme_stops_the_read_and_keeps_the_first(self):
        raw = gms(themes=((b'space', (1,) * 6), (b'jungle', (0,) * 6), (b'space', (0,) * 6),
                          (b'arctic', (0,) * 6)))
        s = read(raw)
        self.assertEqual((s.complete, s.failed_at), (False, 'duplicate theme'))
        self.assertEqual([t.name for t in s.themes], [b'space', b'jungle'])
        self.assertEqual(s.themes[0].local_tickets, (1,) * 6)
        self.assertEqual(s.settings, ())
        with self.assertRaises(snap.StrictReject) as cm:
            read(raw, 'strict-host')
        self.assertEqual(cm.exception.reason, 'duplicate-theme')

    def test_embedded_nul_collides_on_the_inferred_map_key(self):
        raw = gms(themes=((b'space', (0,) * 6), (b'space\0x', (0,) * 6)))
        s = read(raw)
        self.assertEqual((s.failed_at, len(s.themes)), ('duplicate theme', 1))
        lone = read(gms(themes=((b'sp\0ace', (0,) * 6),)))
        self.assertTrue(lone.complete)
        self.assertEqual((lone.themes[0].name, lone.themes[0].map_key), (b'sp\0ace', b'sp'))
        self.assertIn('embedded NUL', lone.issues[0])
        with self.assertRaises(snap.StrictReject) as cm:
            read(gms(themes=((b'sp\0ace', (0,) * 6),)), 'strict-host')
        self.assertEqual(cm.exception.reason, 'nul-in-theme-name')

    def test_empty_theme_name_is_read(self):
        s = read(gms(themes=((b'', (1,) * 6),)), 'strict-host')
        self.assertEqual(s.themes[0].name, b'')

    def test_negative_counts_read_nothing_and_shift_nothing(self):
        s = read(gms(themes=(), theme_count=-2, mystery=(), mystery_count=-1))
        self.assertTrue(s.complete)
        self.assertEqual((s.theme_count, s.themes, s.mystery_count, s.mystery), (-2, (), -1, ()))
        self.assertEqual(len(s.issues), 2)
        with self.assertRaises(snap.StrictReject) as cm:
            read(gms(themes=(), theme_count=-2), 'strict-host')
        self.assertEqual(cm.exception.reason, 'negative-count')

    def test_repeated_ride_id_is_kept_in_order_but_collapses_in_the_set(self):
        s = read(gms(mystery=(300, 7, 300)))
        self.assertTrue(s.complete)
        self.assertEqual((s.mystery, s.mystery_set), ((300, 7, 300), frozenset({7, 300})))
        with self.assertRaises(snap.StrictReject) as cm:
            read(gms(mystery=(300, 7, 300)), 'strict-host')
        self.assertEqual(cm.exception.reason, 'duplicate-ride-id')

    def test_trailing_bytes_are_ignored_by_the_mac_and_rejected_by_strict(self):
        s = read(gms(tail=b'\x01\x02'))
        self.assertEqual((s.complete, s.trailing), (True, b'\x01\x02'))
        with self.assertRaises(snap.StrictReject) as cm:
            read(gms(tail=b'\x01'), 'strict-host')
        self.assertEqual((cm.exception.reason, cm.exception.offset), ('trailing-bytes', len(gms())))


class CycleTests(unittest.TestCase):
    CASES = (gms(), gms(version=0xffffffff, tail=b'xyz'), gms(themes=(), theme_count=-5, mystery_count=-1,
                                                                    mystery=()),
             gms(mystery=(1, 1, 2)), gms(themes=((b'sp\0ace', (2,) * 6),)), gms(extra=-7, spent=-2 ** 31))

    def test_bytes_cycle_identically(self):
        for raw in self.CASES:
            with self.subTest(size=len(raw)):
                s = read(raw)
                self.assertEqual(snap.serialize_profile_snapshot(s), raw)
                self.assertEqual(read(snap.serialize_profile_snapshot(s)), s)

    def test_envelope_cycles_through_json(self):
        for raw in self.CASES + (gms()[:THEMES + 50], gms(version=3)):
            s = read(raw)
            env = json.loads(json.dumps(snap.to_envelope(s)))
            self.assertEqual(snap.from_envelope(env), s)
            self.assertNotIn('keys', env)            # derived values are not stored

    def test_envelope_refuses_unknown_versions(self):
        env = snap.to_envelope(read(gms()))
        for key, value in (('schema', 'other'), ('envelope_version', 2)):
            with self.assertRaises(ValueError):
                snap.from_envelope({**env, key: value})

    def test_partial_and_malformed_snapshots_are_not_serialized(self):
        with self.assertRaises(ValueError):
            snap.serialize_profile_snapshot(read(gms()[:40]))
        s = read(gms())
        for change in ({'theme_count': 2}, {'mystery_count': 5}, {'easy_mode_user': 256},
                       {'settings': s.settings[::-1]}, {'themes': (s.themes[0], s.themes[0])},
                       {'extra_keys': 2 ** 31}):
            with self.subTest(change=list(change)):
                with self.assertRaises(ValueError):
                    snap.serialize_profile_snapshot(dataclasses.replace(s, **change))


class KeyCounterTests(unittest.TestCase):
    def test_keys_are_extra_plus_earned_over_three_and_ignore_spending(self):
        s = read(gms(glob=(1, 0, 2, 0), secret=(0, 3), extra=1, spent=5))
        everything = snap.key_counters(s, lambda key: True)
        # globals 2 + secrets 1 + jungle 2 + space 6 = 11 non-zero bytes
        self.assertEqual(everything, {'earned': 11, 'keys': 1 + 3, 'spent': 5, 'available': 6})
        unspent = snap.key_counters(dataclasses.replace(s, spent_tickets=0), lambda key: True)
        self.assertEqual(unspent['keys'], everything['keys'])

    def test_locals_count_only_for_usable_themes(self):
        s = read(gms())
        only_jungle = snap.key_counters(s, lambda key: key == b'jungle')
        self.assertEqual((only_jungle['earned'], only_jungle['keys']), (5, 2))
        self.assertEqual(snap.key_counters(s, lambda key: False)['earned'], 3)

    def test_usable_predicate_is_required_and_must_be_bool(self):
        s = read(gms())
        with self.assertRaises(TypeError):
            snap.key_counters(s)
        with self.assertRaises(ValueError):
            snap.key_counters(s, lambda key: 1)

    def test_negative_and_wrapping_counters(self):
        s = read(gms(glob=(0,) * 4, secret=(0, 0), themes=(), extra=-5, spent=-2 ** 31))
        self.assertEqual(snap.key_counters(s, lambda key: True),
                         {'earned': 0, 'keys': -5, 'spent': -2 ** 31, 'available': -2 ** 31})
        s = read(gms(glob=(1, 1, 1, 0), secret=(0, 0), themes=(), extra=2 ** 31 - 1))
        self.assertEqual(snap.key_counters(s, lambda key: True)['keys'], -2 ** 31)

    def test_partial_read_counts_what_the_mac_record_holds(self):
        cut = read(gms(extra=4)[:SECRET + 2 + 4])   # extra keys not read
        self.assertEqual(snap.key_counters(cut, lambda key: True)['keys'], (3 // 3) + 0)

    def test_selection_game_type(self):
        for mode, current, expected in ((0, 0, 0), (1, 0, 2), (0x80, 2, 2), (0, 2, 0), (1, 1, 1), (0, 1, 1)):
            self.assertEqual(snap.selection_game_type(read(gms(mode=mode)), current), expected)
        with self.assertRaises(ValueError):
            snap.selection_game_type(read(gms()), 3)


@unittest.skipUnless(os.environ.get('OPENTPW_MAC_BIN'), 'OPENTPW_MAC_BIN not set')
class SnapshotWitnessCorpusTests(unittest.TestCase):
    def _e(self, offset=None, word=None):
        e = Evidence(evidence.load_identified(Path(os.environ['OPENTPW_MAC_BIN']) / 'SimThemePark.data'))
        if offset is not None:
            code = bytearray(e.code)
            struct.pack_into('>I', code, offset, word)  # in memory only
            e.code = bytes(code)
        return e

    def test_identified_binary(self):
        self.assertIn('unchecked', snap.snapshot_witnesses(self._e())['theme_name'])

    def test_mutations_are_rejected(self):
        cases = [
            (0x1297d8, 31 << 26 | 22 << 16 | 3 << 11, 'code:0x1297d8'),       # cmplw -> cmpw
            (0x1297a0, 11 << 26 | 3 << 16, 'allocation checked'),            # cmpwi r3, 0 after new[]
            (0x129964, 14 << 26 | 24 << 21 | 3 << 16 | 1, 'code:0x129964'),   # count from the insert result
            (0x137990, 10 << 26 | 3 << 16, 'code:0x137990'),                  # cmplwi instead of clrlwi.
        ]
        for offset, word, message in cases:
            with self.subTest(offset=hex(offset)):
                with self.assertRaisesRegex(pef.PEFError, message):
                    snap.snapshot_witnesses(self._e(offset, word))


if __name__ == '__main__':
    unittest.main()
