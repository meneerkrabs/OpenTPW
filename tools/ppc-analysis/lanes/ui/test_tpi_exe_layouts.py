import unittest

import tpi_exe_layouts as tpi
import tpw_exe_layouts as base


def node(kind, node_id, rect, children=(), hash_=None, help_=-1):
    return {'type': kind, 'attributes': 1, 'id': node_id, 'rect': list(rect), 'help': help_, 'help2': -1, 'hash': hash_, 'hash2': None,
            'inner': None, 'implied': 0, 'at': 0, 'children': list(children)}


class TpiLayoutTests(unittest.TestCase):
    def test_tables_name_the_front_end_letter_mission_and_tube_windows(self):
        names = {name: (address, root_id) for name, address, root_id, _, _, _ in tpi.TABLES}
        self.assertEqual((0x9C0FBC, 0xC738), names['PersonnelFile'])
        self.assertEqual((0x9B18BC, 0xEA60), names['QuitConfirm'])
        self.assertEqual((0x9BE808, 0xCEC52), names['MissionWindow'])
        self.assertEqual((0x9B54CC, 0xC11FB), names['ChallengesWindow'])
        self.assertEqual((0x9BBC84, 0xBB8), names['TagTube'])
        self.assertEqual((0x9C9F64, 0xCA181), names['WageDemand'])
        self.assertEqual((0x9BE518, 0xB284), names['WageNegotiation'])
        for name in names:
            self.assertIn(name, tpi.REQUIRED_MODELS)
            self.assertIn(name, tpi.REQUIRED_IDS)

    def test_the_executable_hash_is_the_no_cd_game_exe(self):
        self.assertEqual(64, len(tpi.TPI_SHA))
        with self.assertRaises(ValueError):
            tpi.build(b'not an executable', b'')

    def test_render_writes_derived_values_only(self):
        names = {base.root_name_hash('b_okay'): 'b_okay'}
        child = node(2, 0xEA62, (1259, 680, 1362, 782), hash_=base.root_name_hash('b_okay'), help_=0)
        root = node(1, 0xEA60, (499, 232, 1518, 830), [child])
        text = tpi.render({'QuitConfirm': (0x9B18BC, [root])}, names)
        self.assertIn('public static class ThemeParkIncLayoutTables', text)
        self.assertIn('[BIN:TPI-EXE:0x9b18bc] layout table QuitConfirm.', text)
        self.assertIn('new( 2, 0x1u, 0xEA62u, 1259, 680, 1362, 782, 0, -1, "b_okay", null, null,', text)
        self.assertIn(tpi.TPI_SHA, text)

    def test_command_19_is_a_value_field_node_without_a_stream(self):
        class Image:
            def __init__(self, data):
                self.data = data

            def read(self, address, size):
                return self.data[address:address + size]

        import struct
        words = [0, 12, 1, 0, 0xCA185 & 0xFFFF, 0xCA185 >> 16, 544, 818, 953, 998, 19, 630, 856, 866, 900, 5, 5]
        roots, after = base.parse_layout(Image(struct.pack('<' + 'H' * len(words), *words)), 0)
        self.assertEqual(len(words) * 2, after)
        field = roots[0]['children'][0]
        self.assertEqual((5, 0x101, 3, [630, 856, 866, 900], []), (field['type'], field['attributes'], field['id'], field['rect'], field['children']))

    def test_an_unresolved_model_hash_is_an_error(self):
        with self.assertRaises(ValueError):
            tpi.render({'T': (1, [node(1, 5, (0, 0, 1, 1), hash_=0x12345678)])}, {})


if __name__ == '__main__':
    unittest.main()
