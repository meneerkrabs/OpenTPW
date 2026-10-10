"""Key display, door gate and mystery-spending checks. Synthetic values only, except the
corpus mutations, which run only with OPENTPW_MAC_BIN set.

Run: python3 -m unittest discover -s tools/ppc-analysis/lanes/scenarios -v
"""
import os
from pathlib import Path
import struct
import unittest

import key_display_evidence as keys
import scenario_evidence as evidence
from scenario_evidence import Evidence, pef

I32_MIN, I32_MAX = keys.I32_MIN, keys.I32_MAX
NONE6 = [0] * 6


def d_word(op, rt, ra, imm):
    return op << 26 | rt << 21 | ra << 16 | imm & 0xffff


def bc_word(offset, bo, bi, target):
    return 16 << 26 | bo << 21 | bi << 16 | (target - offset) & 0xfffc


class ReferenceKeyTests(unittest.TestCase):
    def test_earned_counts_non_zero_bytes_of_usable_themes_only(self):
        earned = keys.earned_tickets([2, 0, 1, 0], [0, 9], [([1, 1, 1, 0, 0, 0], True), ([1] * 6, False)])
        self.assertEqual(earned, 2 + 1 + 3)
        self.assertEqual(keys.earned_tickets([0] * 4, [0, 0], []), 0)

    def test_keys_truncate_and_wrap_signed(self):
        self.assertEqual(keys.mac_keys(0, 2), 0)
        self.assertEqual(keys.mac_keys(1, 3), 2)
        self.assertEqual(keys.mac_keys(-5, 3), -4)          # a negative saved mExtraKeys is kept
        self.assertEqual(keys.mac_keys(I32_MAX, 3), I32_MIN)  # 32-bit add wraps
        with self.assertRaises(ValueError):
            keys.mac_keys(0, -3)

    def test_available_tickets_subtract_spent_and_wrap(self):
        self.assertEqual(keys.mac_available_tickets(6, 4), 2)
        self.assertEqual(keys.mac_available_tickets(1, 4), -3)
        self.assertEqual(keys.mac_available_tickets(0, I32_MIN), I32_MIN)

    def test_preconditions_reject_wrong_shapes_and_widths(self):
        bad = [
            lambda: keys.earned_tickets([0] * 5, [0, 0], []),
            lambda: keys.earned_tickets([0] * 4, [0, 256], []),
            lambda: keys.earned_tickets([0] * 4, [0, 0], [(NONE6, 1)]),
            lambda: keys.earned_tickets([True, 0, 0, 0], [0, 0], []),
            lambda: keys.mac_keys(2 ** 31, 0),
            lambda: keys.mac_theme_door(3, False, True, True, 1, 1),
            lambda: keys.mac_theme_door(True, False, True, True, 1, 1),
            lambda: keys.mac_mystery_place(0, 1, 0x10000, frozenset(), 3, 0),
            lambda: keys.mac_count_panel(0, 1, 1, (0, 2 ** 31)),
        ]
        for call in bad:
            with self.assertRaises(ValueError):
                call()


class ReferenceDoorTests(unittest.TestCase):
    def test_failure_order(self):
        self.assertEqual(keys.mac_theme_door(2, True, True, False, 9, 0), 'ignored: already entering')
        self.assertEqual(keys.mac_theme_door(2, False, False, False, 9, 0), 'ignored: no target theme')
        # Instant Action passes before the usability and key tests.
        self.assertEqual(keys.mac_theme_door(2, False, True, False, 9, 0), 'enter')
        self.assertEqual(keys.mac_theme_door(0, False, True, False, 0, 9), 'refused: theme record unusable')
        self.assertEqual(keys.mac_theme_door(1, False, True, True, 5, 4), 'refused: cost above keys')

    def test_boundary_and_signed_compare(self):
        self.assertEqual(keys.mac_theme_door(0, False, True, True, 3, 3), 'enter')
        self.assertEqual(keys.mac_theme_door(0, False, True, True, 4, 3), 'refused: cost above keys')
        self.assertEqual(keys.mac_theme_door(0, False, True, True, -1, -1), 'enter')
        self.assertEqual(keys.mac_theme_door(0, False, True, True, 0, I32_MIN), 'refused: cost above keys')

    def test_lobby_display_uses_the_door_predicate(self):
        for cost, have in ((1, 0), (1, 1), (3, 2), (5, 5), (0, I32_MIN)):
            door = keys.mac_theme_door(0, False, True, True, cost, have)
            shown = keys.mac_lobby_door_display(0, True, cost, have)
            self.assertEqual(door == 'enter', shown['state'] == 0)
        self.assertEqual(keys.mac_lobby_door_display(0, True, 3, 2)['value'], 7)
        self.assertEqual(keys.mac_lobby_door_display(0, True, 3, 3)['value'], 2)
        self.assertEqual(keys.mac_lobby_door_display(0, True, I32_MAX, 0)['value'], I32_MIN + 3)
        self.assertEqual(keys.mac_lobby_door_display(2, True, 9, 0), {'touched': True, 'checked_bit': 0})
        self.assertEqual(keys.mac_lobby_door_display(0, False, 9, 0), {'touched': False})


class ReferencePanelTests(unittest.TestCase):
    def test_instant_action_never_updates_keys_but_updates_tickets(self):
        out = keys.mac_count_panel(2, 4, 0, (-1, -1))
        self.assertIsNone(out['keys_group'])
        self.assertEqual(out['tickets_group'], {'checked_bit': 0})
        self.assertEqual(out['cache'], (-1, 0))

    def test_cache_and_zero(self):
        out = keys.mac_count_panel(0, 0, 3, (-1, -1))
        self.assertEqual(out['keys_group'], {'checked_bit': 0})
        self.assertEqual(out['tickets_group'], {'checked_bit': 1, 'text_value': 3})
        self.assertEqual(keys.mac_count_panel(0, 0, 3, out['cache'])['keys_group'], None)
        # Negative available tickets are shown, not cleared (bne on zero only).
        self.assertEqual(keys.mac_count_panel(0, 1, -2, (1, 0))['tickets_group'], {'checked_bit': 1, 'text_value': -2})


class ReferenceMysteryTests(unittest.TestCase):
    def test_spending_tickets_never_changes_keys(self):
        earned = 6
        before = keys.mac_keys(1, earned)
        out = keys.mac_mystery_place(0, 4, 7, frozenset(), earned, 0)
        self.assertEqual((out['purchase'], out['spent'], out['owned']), (1, 4, frozenset({7})))
        self.assertEqual(keys.mac_keys(1, earned), before)
        self.assertEqual(keys.mac_available_tickets(earned, out['spent']), 2)

    def test_order_owned_online_unaffordable(self):
        self.assertEqual(keys.mac_mystery_place(0, 4, 7, frozenset({7}), 9, 0)['path'], 'money')
        self.assertEqual(keys.mac_mystery_place(0, 0, 7, frozenset(), 9, 0)['path'], 'money')
        online = keys.mac_mystery_place(1, 99, 7, frozenset(), 0, 0)
        self.assertEqual((online['purchase'], online['owned'], online['spent']), (1, frozenset(), 0))
        poor = keys.mac_mystery_place(0, 5, 7, frozenset(), 4, 0)
        self.assertEqual((poor['path'], poor['purchase'], poor['spent']), ('tickets', 0, 0))
        self.assertEqual(keys.mac_mystery_place(0, 4, 7, frozenset(), 4, 0)['purchase'], 1)  # cost == available

    def test_spend_wraps_and_menu_gate_differs_for_negative_cost(self):
        self.assertEqual(keys.mac_mystery_place(0, 1, 7, frozenset(), 0, -1)['spent'], 0)
        self.assertEqual(keys.mac_mystery_place(0, I32_MAX, 7, frozenset(), I32_MAX, -1)['purchase'], 0)
        self.assertEqual(keys.mac_mystery_place(0, 2, 7, frozenset(), 0, I32_MAX - 1)['purchase'], 0)
        self.assertTrue(keys.mac_menu_affordable(-3, 7, frozenset(), 0, 0))
        self.assertEqual(keys.mac_mystery_place(0, -3, 7, frozenset(), 0, 0)['path'], 'money')
        self.assertIsNone(keys.mac_menu_affordable(0, 7, frozenset(), 0, 0))
        self.assertFalse(keys.mac_menu_affordable(1, 7, frozenset(), 0, 0))


@unittest.skipUnless(os.environ.get('OPENTPW_MAC_BIN'), 'OPENTPW_MAC_BIN not set')
class KeyDisplayCorpusTests(unittest.TestCase):
    def _e(self, offset=None, word=None):
        e = Evidence(evidence.load_identified(Path(os.environ['OPENTPW_MAC_BIN']) / 'SimThemePark.data'))
        if offset is not None:
            code = bytearray(e.code)
            struct.pack_into('>I', code, offset, word)  # in memory only
            e.code = bytes(code)
        return e

    def test_identified_binary(self):
        result = keys.inspect_key_display(self._e())
        self.assertEqual(result['key_door_and_getters']['front_end_key_readers'],
                         [hex(o) for o in keys.FRONT_END_KEY_READERS])
        self.assertEqual(result['lobby_key_display']['format'], '%d x')
        self.assertEqual(result['key_count_panel']['format'], '%d  x')

    def test_mutations_are_rejected(self):
        cases = [
            # Lobby shows a door with cost == keys as locked (blt -> ble).
            (keys.lobby_display, 0x18419c, bc_word(0x18419c, 4, 1, 0x184250), 'code:0x18419c'),
            # Cached compare tests unsigned-less (cmpw -> cmplw).
            (keys.lobby_display, 0x183edc, 31 << 26 | 3 << 11 | 32 << 1, 'code:0x183edc'),
            # A fourth user of the cached key count.
            (keys.lobby_display, 0x183a88, d_word(32, 4, 2, keys.KEY_CACHE_SLOT - 0x8000), 'key cache users'),
            # Count panel no longer skips keys in Instant Action.
            (keys.count_panel, 0x155c40, 0x60000000, 'code:0x155c40'),
            # Ticket count shows earned instead of available.
            (keys.count_panel, 0x155de8, 18 << 26 | (0x128b60 - 0x155de8) & 0x3fffffc | 1, 'code:0x155de8'),
            # The purchase writes mExtraKeys.
            (keys.mystery_spending, 0x128cf8, d_word(36, 0, 30, 32), 'code:0x128cf8'),
            # Affordability compared unsigned.
            (keys.mystery_spending, 0xd31a0, 31 << 26 | 30 << 16 | 3 << 11 | 32 << 1, 'code:0xd31a0'),
            # Placement also debits money after the ticket purchase.
            (keys.mystery_spending, 0xdacfc, 18 << 26 | (0xdad00 - 0xdacfc), 'code:0xdacfc'),
            # Keys() reads mSpentTickets somewhere in its body.
            (keys.door_and_getters, 0x128b98, d_word(32, 0, 29, 28), 'Keys\\(\\) reads mSpentTickets'),
            # Door tests cost >= keys (bgt -> bge).
            (keys.door_and_getters, 0x965e0, bc_word(0x965e0, 4, 0, 0x96610), 'code:0x965e0'),
            # Keys() adds unsigned-with-carry instead of a plain add.
            (keys.door_and_getters, 0x128c88, 31 << 26 | 3 << 21 | 4 << 16 | 10 << 1, 'code:0x128c88'),
        ]
        for check, offset, word, message in cases:
            with self.subTest(offset=hex(offset)):
                with self.assertRaisesRegex(pef.PEFError, message):
                    check(self._e(offset, word))


if __name__ == '__main__':
    unittest.main()
