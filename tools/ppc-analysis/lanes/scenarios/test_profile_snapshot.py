"""Profile snapshot reader, writer and envelope. Synthetic bytes only (no gms.dat exists in the
assets), except the witness mutations, which run only with OPENTPW_MAC_BIN set.

Run: python3 -m unittest discover -s tools/ppc-analysis/lanes/scenarios -v
"""
import dataclasses
import json
import os
from pathlib import Path
import struct
import time
import unittest
from unittest import mock

import native_io_evidence as native_io
import player_file_evidence as player_file
import profile_snapshot as snap
import scenario_evidence as evidence
from scenario_evidence import Evidence, pef

GLOBAL, SECRET = 4, 4 + 4              # byte offsets after the version
SPENT, EXTRA = SECRET + 2, SECRET + 2 + 4
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
        s = read(gms(spent=4, extra=3)[:EXTRA + 2])   # cut inside mExtraKeys
        self.assertEqual((s.global_tickets, s.secret_tickets, s.spent_tickets), ((1, 0, 2, 0), (0, 3), 4))
        # 03 00 delivered into the big-endian member over the reset 0, swap skipped.
        self.assertEqual((s.extra_keys, s.swear_filter_on, s.first_time_player), (0x03000000, 1, 1))
        self.assertEqual(s.fields_read, snap.PLAYER_FIELDS[:3])
        self.assertIn('2 of 4 bytes delivered', s.issues[-1])

    def test_short_i32_member_holds_the_delivered_bytes_unswapped(self):
        raw = gms(spent=0x04030201)                   # 01 02 03 04 on disk
        for k, expected in ((0, 0), (1, 0x01000000), (2, 0x01020000), (3, 0x01020300), (4, 0x04030201)):
            with self.subTest(k=k):
                s = read(raw[:SPENT + k])
                self.assertEqual(s.spent_tickets, expected)
                self.assertEqual(player_file.read_mac_player_file(raw[:SPENT + k])['record']['mSpentTickets'],
                                 expected)
                self.assertEqual(bool(s.issues) and 'delivered' in s.issues[-1], 0 < k < 4)
                with self.assertRaises(snap.StrictReject):
                    read(raw[:SPENT + k], 'strict-host')

    def test_one_byte_into_extra_keys_corrupts_the_key_count(self):
        # A file cut one byte into mExtraKeys: the low byte lands in the high-order byte.
        for extra, held in ((0x7f, 0x7f000000), (-1, -0x01000000), (0x100, 0)):
            with self.subTest(extra=extra):
                s = read(gms(extra=extra)[:EXTRA + 1])
                self.assertEqual(s.extra_keys, held)
                self.assertEqual(snap.key_counters(s, lambda key: True)['keys'], held + 3 // 3)

    def test_short_byte_member_keeps_its_value(self):
        s = read(gms(mode=1)[:MODE])
        self.assertEqual((s.easy_mode_user, s.failed_at, s.issues), (0, 'mEasyModeUser', ()))

    def test_short_import_bounds(self):
        self.assertEqual(player_file.short_import(0x11223344, b'\xaa'), struct.unpack('>i', b'\xaa\x22\x33\x44')[0])
        with self.assertRaises(ValueError):
            player_file.short_import(0, b'1234')

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
        self.assertIn('3 of 8 bytes delivered into the game-wide settings member', s.issues[-1])
        self.assertEqual(read(raw[:settings_at + 8 * 2]).issues, ())

    def test_oversized_name_length_is_flagged_as_undefined_on_the_mac(self):
        raw = bytearray(gms())
        struct.pack_into('<I', raw, THEMES, 0xffffffff)
        s = read(bytes(raw))
        self.assertEqual((s.complete, s.failed_at, s.themes), (False, 'theme name', ()))
        self.assertIn('not defined by the trace', s.issues[-1])
        with self.assertRaises(snap.StrictReject):
            read(bytes(raw), 'strict-host')
        struct.pack_into('<I', raw, THEMES, 0x10000)
        s = read(bytes(raw))
        self.assertEqual((s.failed_at, s.themes), ('theme name', ()))
        self.assertIn('fails without inserting the theme', s.issues[-1])


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
        for key, value in (('schema', 'other'), ('envelope_version', 1), ('envelope_version', 3),
                           ('envelope_version', '2'), ('envelope_version', 2.0), ('envelope_version', True)):
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
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


class EnvelopeValidationTests(unittest.TestCase):
    """Every bad envelope is a ValueError raised before a snapshot exists (no KeyError/TypeError)."""

    def setUp(self):
        self.env = json.loads(json.dumps(snap.to_envelope(read(gms()))))

    def refused(self, env, msg=None):
        with self.assertRaises(ValueError) as cm:
            snap.from_envelope(env)
        self.assertNotIsInstance(cm.exception, (KeyError, TypeError))
        if msg:
            self.assertIn(msg, str(cm.exception))

    def edited(self, edit):
        env = json.loads(json.dumps(self.env))
        edit(env)
        return env

    def test_missing_fields_are_value_errors(self):
        for key in self.env:
            if key in ('schema', 'envelope_version'):
                continue
            with self.subTest(key=key):
                self.refused({k: v for k, v in self.env.items() if k != key}, 'lacks')
        for key in self.env['player']:
            with self.subTest(player=key):
                self.refused(self.edited(lambda e: e['player'].pop(key)), 'lacks')
        for key in self.env['themes'][0]:
            with self.subTest(theme=key):
                self.refused(self.edited(lambda e: e['themes'][0].pop(key)), 'lacks')

    def test_unknown_fields_are_refused(self):
        self.refused({**self.env, 'extra': 1}, 'unknown fields')
        self.refused(self.edited(lambda e: e['player'].update(mKeys=3)), 'unknown fields')
        self.refused(self.edited(lambda e: e['themes'][0].update(keys=3)), 'unknown fields')

    def test_wrong_types_and_ranges_are_refused(self):
        for path, value in ((('version',), '12'), (('version',), 12.0), (('version',), 2 ** 32), (('version',), -1),
                            (('complete',), 'true'), (('complete',), 1), (('policy',), None), (('policy',), 'mac'),
                            (('player', 'mEasyModeUser'), 256), (('player', 'mEasyModeUser'), True),
                            (('player', 'mSpentTickets'), 2 ** 31), (('player', 'mEarnedGlobalTicket'), [1, 0, 2]),
                            (('player', 'mEarnedSecretTicket'), 'ab'), (('theme_count',), '3'),
                            (('mystery',), [7, '300']), (('mystery',), [7, -1]), (('mystery',), [7, 65536]),
                            (('trailing_hex',), 'ABCD'), (('trailing_hex',), 'abc'), (('issues',), [1]),
                            (('settings',), {}), (('themes',), None), (('fields_read',), 'mExtraKeys'),
                            (('failed_offset',), 3)):
            def edit(e, path=path, value=value):
                target = e
                for key in path[:-1]:
                    target = target[key]
                target[path[-1]] = value
            with self.subTest(path=path, value=value):
                self.refused(self.edited(edit))
        self.refused(self.edited(lambda e: e['themes'][0].update(name_hex='JUNGLE')))
        self.refused(self.edited(lambda e: e['themes'][0]['mAward_mAwardScore'][0].append(1)))
        self.refused(self.edited(lambda e: e['themes'][0]['mSignNameA_mSignNameB'][0].__setitem__(1, 65536)))
        self.refused(self.edited(lambda e: e['settings'][0].__setitem__(1, '00')), 'width')
        self.refused(['not', 'an', 'object'])
        self.refused({**self.env, 'envelope_version': None})

    def test_records_no_read_could_produce_are_refused(self):
        self.refused(self.edited(lambda e: e['mystery'].append(9)), 'mystery_count')
        self.refused(self.edited(lambda e: e.update(theme_count=4)), 'theme_count')
        self.refused(self.edited(lambda e: e['issues'].append('made up')), 'own bytes')
        flipped = snap.from_envelope(self.edited(lambda e: e['themes'].reverse()))   # any file order is a file
        self.assertEqual([t.name for t in flipped.themes], [b'space', b'halloween', b'jungle'])
        self.refused(self.edited(lambda e: e['themes'].__setitem__(1, e['themes'][0])), 'repeats')
        self.refused(self.edited(lambda e: e.update(policy='strict-host', trailing_hex='00')), 'strict-host')
        partial = snap.to_envelope(read(gms()[:THEMES + 50]))
        self.assertEqual(snap.from_envelope(partial).failed_at, 'theme record')
        self.refused({**partial, 'policy': 'strict-host'}, 'partial')
        self.refused({**partial, 'trailing_hex': '00'})
        self.refused({**partial, 'failed_at': 'rideId'})
        self.refused({**partial, 'settings': [['MusicVolume', '0000000000000000']]})
        cut = snap.to_envelope(read(gms()[:SPENT + 2]))
        self.assertEqual(cut['failed_at'], 'mSpentTickets')
        self.assertEqual(snap.from_envelope(cut), read(gms()[:SPENT + 2]))   # short member keeps its bytes
        self.refused({**cut, 'player': {**cut['player'], 'mExtraKeys': 1}}, 'reset')

    def test_every_partial_read_cycles_through_json(self):
        for raw in CycleTests.CASES + (gms(themes=((b'a', (0,) * 6), (b'a', (0,) * 6))),):
            for n in range(len(raw) + 1):
                s = read(raw[:n])
                with self.subTest(size=len(raw), cut=n):
                    self.assertEqual(snap.loads_envelope(json.dumps(snap.to_envelope(s))), s)

    def test_strict_host_envelope_cycles_and_is_rechecked(self):
        s = read(gms(version=12), 'strict-host')
        env = json.loads(json.dumps(snap.to_envelope(s)))
        self.assertEqual(snap.from_envelope(env), s)
        self.refused({**env, 'version': 13}, 'strict-host')          # strict refuses unknown versions
        mac = json.loads(json.dumps(snap.to_envelope(read(gms(version=13)))))
        self.assertEqual(snap.from_envelope(mac).version, 13)                # the Mac's unsigned >= 12 gate
        self.refused({**mac, 'policy': 'strict-host'}, 'unknown-version')
        self.refused({**mac, 'issues': []}, 'own bytes')

    def test_layout_source_names_the_layout_not_the_bytes(self):
        self.assertNotIn('source', self.env)
        self.assertEqual(self.env['envelope_version'], 2)
        self.assertTrue(self.env['layout_source'].startswith('layout-reference/1: '))
        self.assertIn('snapshot bytes unauthenticated', self.env['layout_source'])
        self.refused({**self.env, 'layout_source': 'Feral Mac gms.dat captured from a real install'}, 'layout_source')
        legacy = {k: v for k, v in self.env.items() if k != 'layout_source'}
        self.refused({**legacy, 'envelope_version': 1, 'source': snap.LAYOUT_SOURCE}, 'envelope_version')
        self.refused({**legacy, 'source': snap.LAYOUT_SOURCE})

    def test_json_text_with_repeated_members_or_constants_is_refused(self):
        text = json.dumps(self.env)
        self.assertEqual(snap.loads_envelope(text), read(gms()))
        self.assertEqual(snap.loads_envelope(text.encode()), read(gms()))
        for bad in (text[:-1] + ', "policy": "strict-host"}', text.replace('"version": 12', '"version": NaN'),
                    text[:-1], '[]'):
            with self.subTest(bad=bad[-40:]), self.assertRaises(ValueError):
                snap.loads_envelope(bad)


class LargeMysteryTests(unittest.TestCase):
    """Repeat detection is linear: the quadratic list scan took ~1.1 s for 20k rideIds."""

    def test_large_repeated_set_keeps_order_and_policy_split(self):
        rides = tuple(i % 20000 for i in range(60000))
        raw = gms(mystery=rides)
        start = time.perf_counter()
        s = read(raw)
        self.assertLess(time.perf_counter() - start, 1.0)
        self.assertEqual((s.complete, s.mystery, len(s.mystery_set), len(s.issues)), (True, rides, 20000, 40000))
        self.assertEqual(snap.serialize_profile_snapshot(s), raw)
        self.assertEqual(s.issues[0], 'rideId 0 repeats; the Mac set insert ignores it (result unchecked)')
        with self.assertRaises(snap.StrictReject) as cm:
            read(raw, 'strict-host')
        self.assertEqual(cm.exception.reason, 'duplicate-ride-id')
        self.assertIn('rideId 0 repeats', str(cm.exception))

    def test_unique_large_set_passes_strict(self):
        rides = tuple(range(65535, 5535, -1))
        start = time.perf_counter()
        s = read(gms(mystery=rides), 'strict-host')
        self.assertLess(time.perf_counter() - start, 1.0)
        self.assertEqual((s.mystery, s.issues), (rides, ()))


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


class WriterOrderTests(unittest.TestCase):
    def test_themes_ascend_by_key_and_mystery_ascends_unsigned(self):
        order = snap.mac_writer_order(read(gms(mystery=(300, 7, 0x8000, 7, 0xffff, 0))))
        self.assertEqual(order, {'themes': (b'halloween', b'jungle', b'space'), 'theme_count': 3,
                                 'mystery': (0, 7, 300, 0x8000, 0xffff), 'mystery_count': 5})

    def test_key_order_is_unsigned_bytes_with_the_shorter_prefix_first(self):
        names = (b'zoo', b'\xe9te', b'abc', b'ab', b'Ab', b'')
        s = read(gms(themes=tuple((n, (0,) * 6) for n in names)))
        self.assertTrue(s.complete)
        self.assertEqual(snap.mac_writer_order(s)['themes'], (b'', b'Ab', b'ab', b'abc', b'zoo', b'\xe9te'))
        self.assertEqual([t.name for t in s.themes], list(names))   # the snapshot keeps file order

    def test_embedded_nul_name_is_written_back_as_its_key(self):
        s = read(gms(themes=((b'b', (0,) * 6), (b'a\0zz', (0,) * 6))))
        self.assertEqual(snap.mac_writer_order(s)['themes'], (b'a', b'b'))
        self.assertEqual(snap.serialize_profile_snapshot(s), gms(themes=((b'b', (0,) * 6), (b'a\0zz', (0,) * 6))))

    def test_partial_read_orders_only_the_inserted_containers(self):
        raw = gms(themes=((b'space', (0,) * 6), (b'jungle', (0,) * 6), (b'space', (0,) * 6)))
        order = snap.mac_writer_order(read(raw))
        self.assertEqual((order['themes'], order['mystery']), ((b'jungle', b'space'), ()))
        negative = snap.mac_writer_order(read(gms(theme_count=-4, themes=(), mystery_count=-1, mystery=())))
        self.assertEqual((negative['theme_count'], negative['mystery_count']), (0, 0))


@unittest.skipUnless(os.environ.get('OPENTPW_MAC_BIN'), 'OPENTPW_MAC_BIN not set')
class NativeIoCorpusTests(unittest.TestCase):
    def _patched(self, ev, offset, word):
        if offset is not None:
            code = bytearray(ev.code)
            struct.pack_into('>I', code, offset, word)  # in memory only
            ev.code = bytes(code)
            ev._calls = None
        return ev

    def _all(self, where=None, offset=None, word=None):
        root = Path(os.environ['OPENTPW_MAC_BIN'])
        e = Evidence(evidence.load_identified(root / 'SimThemePark.data'))
        bf, libc, md = (native_io.load_library(root, n) for n in native_io.LIBRARIES)
        target = {'main': e, 'bullfrog': bf, 'libc': libc, 'macdoze': md}.get(where)
        if target is not None:
            self._patched(target, offset, word)
        return e, bf, libc, md

    def run_all(self, e, bf, libc, md):
        return (native_io.theme_key(bf, libc), native_io.containers(e), native_io.short_reads(e, bf, md),
                native_io.settings_apply(e))

    def test_identified_binary_and_libraries(self):
        key, containers, short, settings = self.run_all(*self._all())
        self.assertIn('unsigned', key['comparator'])
        self.assertIn('ascending', containers['writer_order'])
        self.assertIn('FSRead', short['chain'])
        self.assertEqual(settings['order'][0], 'MusicVolume')

    def test_library_identity_is_pinned(self):
        with mock.patch.dict(native_io.LIBRARIES, {'bullfrog_shared.data': '0' * 64}):
            with self.assertRaisesRegex(pef.PEFError, 'identity of bullfrog_shared.data'):
                native_io.load_library(Path(os.environ['OPENTPW_MAC_BIN']), 'bullfrog_shared.data')

    def test_mutations_are_rejected(self):
        cases = [
            ('bullfrog', 0xcde4, 31 << 26 | 3 << 16, 'code:0xcde4'),                       # cmpw lengths
            ('libc', 0x20efc, 31 << 26 | 5 << 11, 'code:0x20efc'),                         # signed bytes
            ('bullfrog', 0x9c0, 14 << 26 | 4 << 21 | 1 << 16 | 0x38, 'code:0x9c0'),         # buffer replaced
            ('macdoze', 0x2b20, 14 << 26 | 5 << 21 | 1 << 16 | 0x40, 'code:0x2b20'),        # FSRead into a temp
            ('main', 0x12b804, 36 << 26 | 29 << 21 | 24 << 16 | 0x10, 'code:0x12b804'),     # leftmost moved
            ('main', 0x12b5e4, 31 << 26 | 3 << 16, 'code:0x12b5e4'),                       # rideIds signed
            ('main', 0xc398, 10 << 26, 'code:0xc398'),                                    # success on 0 bytes
            ('main', 0x126480, 36 << 26 | 3 << 21 | 31 << 16 | 0x18, 'settings stored'),   # apply writes back
        ]
        for where, offset, word, message in cases:
            with self.subTest(where=where, offset=hex(offset)):
                with self.assertRaisesRegex(pef.PEFError, message):
                    self.run_all(*self._all(where, offset, word))


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
