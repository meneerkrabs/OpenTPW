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
                + b'\xC7\x05' + struct.pack('<II', 0x108, 7) + b'\xA3' + struct.pack('<I', 0x10C) + b'\xC3')
        memory = tables.replay_stores(Flat(code), 0)
        self.assertEqual({0x100: 0x59E8C0, 0x104: 0, 0x108: 7, 0x10C: None}, memory)

    def test_costumes_and_help_rows_render(self):
        self.assertIn('{ response = 5, hat = -1, hat2 = 14 }', tables.render_costumes([(5, -1, 14)]))
        text = tables.render_help([{'message': 188, 'first': 407, 'count': 1, 'group': 1, 'duplicates': 1, 'mode': 2}])
        self.assertIn('{ message = 188, response = 407, count = 1, group = 1, duplicates = 1, mode = 2 }', text)


if __name__ == '__main__':
    unittest.main()
