import struct
import unittest

import tpw_exe_layouts as layouts


class Flat(layouts.Image):
    """An image whose virtual addresses are offsets into a flat byte string."""

    def __init__(self, data: bytes):
        self.data = data

    def read(self, va: int, length: int) -> bytes:
        return self.data[va:va + length].ljust(length, b'\0')


def words(*values: int) -> bytes:
    return b''.join(struct.pack('<H', value & 0xFFFF) for value in values)


def window(kind: int, attributes: int, identifier: int, rect: tuple[int, int, int, int]) -> bytes:
    return words(0, kind, attributes & 0xFFFF, attributes >> 16, identifier & 0xFFFF, identifier >> 16, *rect)


class LayoutTests(unittest.TestCase):
    def test_root_name_hash_matches_the_shipped_model_key(self):
        self.assertEqual(0xAAEE5929, layouts.root_name_hash('b_ride it!'))
        self.assertEqual(0x14A7E48B, layouts.root_name_hash('b_okay'))

    def test_the_all_staff_window_table_is_generated(self):
        # tp.exe 0x496620 applies the table at 0x750e10 (window procedure 0x495da0): the window4 frame, the list_allstaff list with its five column headers and role tabs
        entry = next(table for table in layouts.TABLES if table[0] == 'AllStaffWindow')
        self.assertEqual((0x750E10, 0x320, (186, 30, 2018, 1007), 0x751182, 27), entry[1:])
        for model in ('list_allstaff', 'b_shandy', 'b_smech', 'b_senter', 'b_sguard', 'b_sresrcher', 'b_pkinfo', 'b_kids', 'b_allthings'):
            self.assertIn(model, layouts.REQUIRED['AllStaffWindow'])

    def test_parses_children_properties_and_trailing_properties(self):
        stream = (window(1, 1, 0x3E14, (248, 30, 1800, 1007)) + words(1, 0xAAAA, 0xBBBB)
                  + window(2, 1, 0x3E37, (956, 800, 1109, 953)) + words(17, 16, 0, 18, 0xFFFF, 0xFFFF, 1, 0x5929, 0xAAEE, 5)
                  + words(3, 1, 2, 3, 4) + words(5, 5))
        roots, end = layouts.parse_layout(Flat(stream), 0)
        self.assertEqual(1, len(roots))
        root = roots[0]
        self.assertEqual(0xBBBBAAAA, root['hash'])
        child = root['children'][0]
        self.assertEqual((16, -1, 0xAAEE5929, [956, 800, 1109, 953]), (child['help'], child['help2'], child['hash'], child['rect']))
        self.assertEqual(len(stream), end)

    def test_implied_children_and_unknown_commands(self):
        stream = window(3, 0x61, 7, (0, 0, 10, 10)) + words(8, 1, 2, 3, 4, 5, 5, 5)
        roots, _ = layouts.parse_layout(Flat(stream), 0)
        self.assertEqual((2, 1, 3), (roots[0]['children'][0]['type'], roots[0]['children'][0]['attributes'], roots[0]['children'][0]['id']))
        with self.assertRaises(ValueError):
            layouts.parse_layout(Flat(words(99)), 0)

    def test_tool_block_decodes_overrides_and_default(self):
        # at 0x20: mov eax,[0x816d60]; test; je +4; push 5; jmp call; push 3 (falls into the call at 0x40)
        code = bytearray(0x60)
        block = b'\xa1' + struct.pack('<I', 0x816D60) + b'\x85\xc0\x74\x04\x6a\x05\xeb\x00'
        block += b'\x6a\x03'
        code[0x20:0x20 + len(block)] = block
        call = 0x20 + len(block)
        code[0x20 + 5 + 2 + 2 + 3] = (call - (0x20 + 5 + 2 + 2 + 2 + 2)) & 0xFF
        code[call] = 0xE8
        overrides, default = layouts.tool_block(Flat(bytes(code)), 0x20, call)
        self.assertEqual(([('Link', 5)], 3), (overrides, default))
        with self.assertRaises(ValueError):
            layouts.tool_block(Flat(b'\x90' * 16), 0, 8)

    @staticmethod
    def md2(name: str) -> bytes:
        # a minimal model: the offset table at 0x50 names the root node record (+84: pointer to its name)
        data = bytearray(0x200)
        root = 0x100
        struct.pack_into('<12I', data, 0x50, *([0] * 10 + [root, 0]))
        struct.pack_into('<I', data, root + 84, 0x180)
        data[0x180:0x180 + len(name) + 1] = name.encode('latin1') + b'\0'
        return bytes(data)

    def test_language_banner_meshes_join_the_names(self):
        self.assertEqual('s_congrats', layouts.md2_root_name(self.md2('s_congrats')))
        self.assertIsNone(layouts.md2_root_name(b'\0' * 16))
        names = layouts.language_root_names([self.md2('s_congrats')], {layouts.root_name_hash('b_okay'): 'b_okay'})
        self.assertEqual('s_congrats', names[0x706DE86B])
        self.assertEqual('b_okay', names[0x14A7E48B])
        with self.assertRaises(ValueError):
            layouts.add_root_name({0x706DE86B: 'other'}, 's_congrats')

    def test_unresolved_hash_fails(self):
        with self.assertRaises(ValueError):
            layouts.resolve({'hash': 1, 'id': 2}, {}, 'T')


if __name__ == '__main__':
    unittest.main()
