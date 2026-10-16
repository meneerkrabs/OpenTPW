import struct
import unittest

import tpw_exe_tables as tables


class Flat(tables.Image):
    """An image whose virtual addresses are offsets into a flat byte string."""

    def __init__(self, data: bytes):
        self.data = data

    def read(self, va: int, length: int) -> bytes:
        return self.data[va:va + length].ljust(length, b'\0')


def mov_reg(register: int, value: int) -> bytes:
    return bytes([0xB8 + register]) + struct.pack('<I', value)


def store_reg(register: int, address: int) -> bytes:
    return bytes([0x89, 0x05 | (register << 3)]) + struct.pack('<I', address)


class ReplayTests(unittest.TestCase):
    def test_replays_register_and_immediate_stores(self):
        code = (mov_reg(1, 0x59E8C0) + store_reg(1, 0x100) + b'\x31\xdb' + store_reg(3, 0x104)
                + b'\xC7\x05' + struct.pack('<II', 0x108, 7) + mov_reg(0, 9) + b'\x5b' + b'\xA3' + struct.pack('<I', 0x10C) + b'\xC3')
        memory = tables.replay_stores(Flat(code), 0)
        self.assertEqual({0x100: 0x59E8C0, 0x104: 0, 0x108: 7, 0x10C: 9}, memory)

    def test_unknown_instructions_and_unloaded_registers_fail(self):
        with self.assertRaises(ValueError):
            tables.replay_stores(Flat(b'\x90\xC3'), 0)
        with self.assertRaises(ValueError):
            tables.replay_stores(Flat(store_reg(2, 0x100) + b'\xC3'), 0)
        with self.assertRaises(ValueError):
            tables.replay_stores(Flat(b'\xC3'), 0, end=4)

    def test_costumes_and_help_rows_render(self):
        self.assertIn('{ response = 5, hat = -1, hat2 = 14 }', tables.render_costumes([(5, -1, 14)]))
        text = tables.render_help([{'message': 188, 'first': 407, 'count': 1, 'group': 1, 'duplicates': 1, 'mode': 2}])
        self.assertIn('{ message = 188, response = 407, count = 1, group = 1, duplicates = 1, mode = 2 }', text)

    def test_descriptor_rows_render(self):
        row = {'first': 407, 'count': 1, 'group': 1, 'duplicates': 1, 'mode': 2, 'callback': 0x59E8C0}
        table = {message: row for message in range(tables.DESCRIPTOR_COUNT)}
        text = tables.render_descriptors(table)
        self.assertIn('{ message = 188, response = 407, count = 1, group = 1, duplicates = 1, mode = 2, callback = 0x59e8c0 },', text)
        self.assertEqual(tables.DESCRIPTOR_COUNT, text.count('{ message = '))


if __name__ == '__main__':
    unittest.main()
