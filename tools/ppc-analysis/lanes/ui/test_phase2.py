"""Tests for full layout grammar, root bindings and identified Mac UI consumers."""
import os
from pathlib import Path
import struct
import unittest

from corpus import layout_table, name_hash, string_labels
from phase2 import inspect


def words(values):
    return struct.pack('>' + 'H' * len(values), *(value & 65535 for value in values))


class GrammarTests(unittest.TestCase):
    def test_registry_uses_root_name_and_case(self):
        self.assertEqual(name_hash('b_buy'), 557179197)
        self.assertEqual(name_hash('base'), 468387477)
        self.assertNotEqual(name_hash('mainpanel'), name_hash('base'))
        self.assertNotEqual(name_hash('B_BUY'), name_hash('b_buy'))

    def test_implied_scroll_child_and_parent_id(self):
        data = words([0, 3, 1, 0, 123, 0, 0, 0, 100, 20, 6, 1, 2, 4, 6, 5, 5, 5])
        result = layout_table(data, 0)
        self.assertEqual(len(result['windows']), 2)
        child = result['windows'][1]
        self.assertEqual((child['id'], child['parent'], child['type']), (1, 123, 2))
        self.assertEqual(child['rectangle'], [1, 2, 4, 6])

    def test_implied_child_requires_correct_parent_type(self):
        data = words([0, 1, 1, 0, 123, 0, 0, 0, 100, 20, 6])
        with self.assertRaisesRegex(ValueError, 'incompatible parent'):
            layout_table(data, 0)

    def test_geometry_counts_and_unknown_subtypes_are_rejected(self):
        header = [0, 1, 1, 0, 123, 0, 0, 0, 100, 20]
        for payload in [[4, 4, 513], [4, 5], [4, 4, -1]]:
            with self.assertRaises(ValueError):
                layout_table(words(header + payload), 0)

    def test_external_parent_properties_are_distinguished(self):
        result = layout_table(words([3, -2, -1, 4, 5, 5]), 0)
        self.assertEqual(result['windows'], [])
        self.assertEqual(result['external_parent_properties'], {'command_3': [-2, -1, 4, 5]})

    def test_selected_language_labels_use_own_character_table(self):
        mu = b'BFMU' + bytes(2) + struct.pack('<H2H', 2, ord('A'), ord('B'))
        st = b'BFST' + bytes(4) + struct.pack('<II', 1, 4) + bytes([1, 2, 0, 0, 1, 2])
        self.assertEqual(string_labels(st, mu, [0]), {0: 'AB'})
        with self.assertRaisesRegex(ValueError, 'identifier'):
            string_labels(st, mu, [1])
        with self.assertRaisesRegex(ValueError, 'character index'):
            string_labels(st[:-1] + bytes([0]), mu, [0])


@unittest.skipUnless(os.environ.get('UI_EVIDENCE_BIN_ROOT'), 'private corpus paths not supplied')
class CorpusPhase2Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        language = {key: Path(os.environ[variable]) for key, variable in [
            ('uitext', 'UI_EVIDENCE_MAC_UITEXT'), ('uihelp', 'UI_EVIDENCE_MAC_UIHELP'),
            ('mbtouni', 'UI_EVIDENCE_MAC_MBTOUNI')]}
        cls.report = inspect(Path(os.environ['UI_EVIDENCE_BIN_ROOT']),
                             Path(os.environ['UI_EVIDENCE_MAC_UI']), language)

    def test_complete_table_inventory(self):
        self.assertEqual(len(self.report['consumer_calls']), 60)
        self.assertEqual(len(self.report['catalog']), 55)
        self.assertEqual(sum(table['window_count'] for table in self.report['catalog']), 934)

    def test_main_hud_root_binding_and_distinct_button_rectangles(self):
        windows = {window['id']: window for window in self.report['selected']['main_hud']['windows']}
        self.assertEqual(windows[29]['drawing_1']['model_candidates'][0]['member'], 'mainpanel.MD2')
        self.assertEqual(windows[29]['drawing_1']['model_candidates'][0]['root'], 'base')
        self.assertEqual(windows[38]['rectangle'], [170, 1122, 288, 1240])
        self.assertEqual(windows[40]['rectangle'], [287, 1133, 405, 1252])
        self.assertEqual(windows[42]['rectangle'], [156, 1241, 274, 1360])

    def test_theme_lobby_is_distinct_from_online_world_lobby(self):
        theme = self.report['selected']['theme_lobby']['windows'][0]
        world = self.report['selected']['world_lobby']['windows'][0]
        self.assertEqual(theme['drawing_1']['model_candidates'][0]['member'], 'islandlobby.MD2')
        self.assertEqual(world['drawing_1']['model_candidates'][0]['member'], 'worldlob.MD2')

    def test_original_hud_typography_and_mac_text_index_boundary(self):
        self.assertEqual(self.report['hud_text']['date'], {'id': 32, 'font_slot': 3, 'rgba': [0, 0, 0, 255]})
        self.assertEqual(self.report['mac_language']['selected_uitext'][315], 'Game Options')
        self.assertEqual(self.report['mac_language']['selected_uihelp'][12], 'Click to open the ride')
        self.assertEqual(self.report['door_help_selector']['ordinary_help_id'], 13)

    def test_sign_alpha_is_preserved_through_channel_swizzle(self):
        effects = self.report['sign_effects']
        self.assertEqual(effects['swizzled_order'], 'blue, green, red, alpha')
        self.assertFalse(effects['swizzle_resizes'])
        self.assertEqual(effects['caller_destination_dimensions'], [128, 128])

    def test_speed_is_registered_on_release(self):
        events = self.report['input_events']
        self.assertEqual(events['keyboard_group_count'], 15)
        self.assertEqual(events['release_callback_offset'], 12)
        self.assertEqual([record['encoded_key'] for record in events['speed_records']], [0x6d00, 0x6b00])
        self.assertEqual(events['button_repeat_milliseconds'], [500, 125])


if __name__ == '__main__':
    unittest.main()
