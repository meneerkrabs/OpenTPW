"""Tests for the bounded path-building witnesses (PATH-R), with optional identified local assets."""
import os
from pathlib import Path
import struct
from types import SimpleNamespace
import unittest

import path_evidence as p


def d_word(op, rd, ra, imm):
    return (op << 26) | (rd << 21) | (ra << 16) | (imm & 0xffff)


def bl_word(at, target):
    return (18 << 26) | ((target - at) & 0x03fffffc) | 1


def synthetic_fields_container():
    code = bytearray(0x150000)
    for at, op, (rd, ra, imm), _ in p.FIELDS:
        struct.pack_into('>I', code, at, d_word(op, rd, ra, imm))
    return SimpleNamespace(code=SimpleNamespace(data=code))


class CellTypeRuleTests(unittest.TestCase):
    def test_path_goes_on_empty_path_or_queue_cells_only(self):
        self.assertTrue(p.can_change_cell_type(p.CELL_EMPTY, p.CELL_PATH, False))
        self.assertTrue(p.can_change_cell_type(p.CELL_PATH, p.CELL_PATH, False))
        self.assertTrue(p.can_change_cell_type(p.CELL_QUEUE, p.CELL_PATH, False))
        for occupied in (2, 4, 5, 7, 9, 10, 21, 30):
            self.assertFalse(p.can_change_cell_type(occupied, p.CELL_PATH, False), occupied)

    def test_queue_over_path_needs_the_last_cell_flag(self):
        self.assertFalse(p.can_change_cell_type(p.CELL_PATH, p.CELL_QUEUE, False))
        self.assertTrue(p.can_change_cell_type(p.CELL_PATH, p.CELL_QUEUE, True))
        self.assertTrue(p.can_change_cell_type(p.CELL_EMPTY, p.CELL_QUEUE, False))

    def test_clearing_and_entrance_rules(self):
        self.assertTrue(p.can_change_cell_type(9, 0, False))
        self.assertFalse(p.can_change_cell_type(4, 4, False))
        self.assertTrue(p.can_change_cell_type(p.CELL_PATH, 4, False))


class ChargeTests(unittest.TestCase):
    def test_one_path_cell_costs_path_cell_and_needs_the_money(self):
        done = p.set_cell_type(p.CELL_EMPTY, p.CELL_PATH, 0, balance=20, path_cost=20, queue_cost=75)
        self.assertEqual((done['ok'], done['type'], done['charged']), (True, p.CELL_PATH, 20))
        refused = p.set_cell_type(p.CELL_EMPTY, p.CELL_PATH, 0, balance=19, path_cost=20, queue_cost=75)
        self.assertEqual((refused['ok'], refused['charged']), (False, 0))

    def test_laying_path_over_path_is_free_and_counts(self):
        again = p.set_cell_type(p.CELL_PATH, p.CELL_PATH, 2, balance=0, path_cost=20, queue_cost=75)
        self.assertEqual((again['ok'], again['counter'], again['charged']), (True, 3, 0))

    def test_queue_cells_cost_queue_cell_and_conversion_is_flagged(self):
        queue = p.set_cell_type(p.CELL_PATH, p.CELL_QUEUE, 0, 1000, 20, 75, last_cell=True)
        self.assertEqual((queue['charged'], queue['path_to_queue']), (75, True))
        back = p.set_cell_type(p.CELL_QUEUE, p.CELL_PATH, 0, 1000, 20, 75)
        self.assertEqual((back['charged'], back['queue_to_path']), (20, True))

    def test_economy_flag_and_free_build_byte(self):
        self.assertTrue(p.set_cell_type(0, 1, 0, -5, 20, 75, economy_off=True)['ok'])
        self.assertEqual(p.set_cell_type(0, 1, 0, -5, 20, 75, free_build=True)['charged'], 0)

    def test_line_money_test_and_queue_refund(self):
        self.assertTrue(p.line_affordable(14, 20, 280))
        self.assertFalse(p.line_affordable(15, 20, 280))
        self.assertEqual(p.queue_cell_refund(75, 80), 60)
        self.assertEqual(p.queue_cell_refund(75, 33), 24)


class LineTests(unittest.TestCase):
    def test_drag_end_keeps_the_dominant_axis(self):
        self.assertEqual(p.snap_end((10, 10), (15, 12)), (15, 10))
        self.assertEqual(p.snap_end((10, 10), (12, 4)), (10, 4))
        self.assertEqual(p.snap_end((10, 10), (13, 13)), (13, 10))  # a tie keeps X
        self.assertEqual(p.snap_end((10, 10), (10, 10)), (10, 10))

    def test_lay_line_walks_one_axis_and_flags_the_end(self):
        self.assertEqual(p.lay_line((3, 5), (6, 9)), [(3, 5, 0, 1, False), (3, 6, 0, 1, False), (3, 7, 0, 1, False),
                                                       (3, 8, 0, 1, False), (3, 9, 0, 1, True)])
        self.assertEqual(p.lay_line((6, 5), (3, 5)), [(6, 5, -1, 0, False), (5, 5, -1, 0, False),
                                                       (4, 5, -1, 0, False), (3, 5, -1, 0, True)])
        self.assertEqual(p.lay_line((4, 4), (4, 4)), [(4, 4, 0, 1, True)])
        # Unsnapped input: the minor axis is ignored, never walked diagonally.
        self.assertEqual([c[:2] for c in p.lay_line((0, 0), (3, 1))], [(0, 0), (1, 0), (2, 0), (3, 0)])

    def test_cell_ids(self):
        self.assertEqual(p.cell_id(0, 0), 1)
        self.assertEqual(p.cell_id(47, 21), 1 + 47 + 21 * 128)


class RemovalTests(unittest.TestCase):
    def test_removal_never_refunds(self):
        for counter in (0, 1, 3):
            self.assertEqual(p.remove_path(counter, False, True)['refund'], 0)

    def test_forced_and_counted_removal(self):
        self.assertTrue(p.remove_path(3, False, True, 0, 0)['removed'])          # a == b == 0 forces
        self.assertEqual(p.remove_path(1, False, True, 1, 0), {'removed': False, 'counter': 0, 'refund': 0})
        self.assertTrue(p.remove_path(0, False, True, 1, 0)['removed'])

    def test_nomodify_initial_path(self):
        self.assertFalse(p.remove_path(0, True, True)['removed'])
        self.assertTrue(p.remove_path(0, True, False)['removed'])                 # lonely cell: flag cleared
        self.assertTrue(p.remove_path(0, True, True, converting=True)['removed'])

    def test_cardinal_links_and_corners(self):
        link, seen = 1, []
        for _ in range(4):
            seen.append(link)
            link = p.next_link(link)
        self.assertEqual((seen, link), ([1, 4, 16, 64], 1))
        self.assertEqual(p.corner(1 | 4), 5)
        self.assertEqual(p.corner(16 | 64), 80)
        self.assertEqual(p.corner(1 | 16), 0)    # straight
        self.assertEqual(p.corner(1 | 4 | 16), 0)


class MapAndValidatorTests(unittest.TestCase):
    def test_initial_path_cells_are_nomodify_paths(self):
        self.assertEqual(p.initial_cell(0x08), (p.CELL_PATH, p.FLAG_NOMODIFY))
        self.assertEqual(p.initial_cell(0x00), (p.CELL_EMPTY, p.FLAG_UNOWNED))
        self.assertEqual(p.initial_cell(0x18), (p.CELL_PATH, p.FLAG_NOMODIFY | 0x80))

    def test_validator_for_path(self):
        self.assertEqual(p.validate_path_cell(p.CELL_EMPTY, 0), 0)
        self.assertEqual(p.validate_path_cell(p.CELL_PATH, 0), 0)
        self.assertEqual(p.validate_path_cell(p.CELL_EMPTY, p.FLAG_UNOWNED), 1)
        self.assertEqual(p.validate_path_cell(4, 0), 1)
        self.assertEqual(p.validate_path_cell(9, 0), 1)
        self.assertEqual(p.validate_path_cell(p.CELL_QUEUE, 0), 8)


class DecoderTests(unittest.TestCase):
    def test_synthetic_fields_pass_and_each_mutation_fails(self):
        c = synthetic_fields_container()
        self.assertEqual(p.check_fields(c), len(p.FIELDS))
        for index in (0, len(p.FIELDS) // 3, len(p.FIELDS) // 2, len(p.FIELDS) - 1):
            at = p.FIELDS[index][0]
            mutated = synthetic_fields_container()
            word = struct.unpack_from('>I', mutated.code.data, at)[0]
            struct.pack_into('>I', mutated.code.data, at, word ^ 1)
            with self.assertRaises(p.pef.PEFError):
                p.check_fields(mutated)

    def test_key_operands_are_pinned(self):
        pinned = {(at, op): fields for at, op, fields, _ in p.FIELDS}
        self.assertEqual(pinned[(0x10f3cc, 32)], (3, 31, 1472))    # Costs.PathCell
        self.assertEqual(pinned[(0x10f3d4, 32)], (3, 31, 1468))    # Costs.QueueCell
        self.assertEqual(pinned[(0x7bc58, 32)], (3, 2, -28060))    # path cost global slot
        self.assertEqual(pinned[(0x82b24, 11)], (0, 26, 1))        # path = type 1
        self.assertEqual(pinned[(0x8432c, 14)], (4, 0, 64))        # unowned-land flag
        self.assertEqual(pinned[(0x7bd00, 11)], (0, 0, 1024))      # vertex stack cap
        self.assertEqual(pinned[(0x13a6ec, 14)], (4, 0, 441))      # hover help
        self.assertEqual(p.SCHEMA_EXPECTED['Costs.PathCell'], 1472)
        self.assertEqual(p.TOC_SLOTS[0x1264], 0x84ad4)

    def test_negative_witness_scanner(self):
        code = bytearray(0x90000)
        c = SimpleNamespace(code=SimpleNamespace(data=code))
        self.assertEqual(p.check_negatives(c)[0]['absent_call'], 0xcbf50)
        struct.pack_into('>I', code, 0x85b00, bl_word(0x85b00, 0xcbf50))
        with self.assertRaises(p.pef.PEFError):
            p.check_negatives(c)

    def test_string_table_readers(self):
        bfmu = b'BFMU\0\0' + struct.pack('<H', 3) + 'abc'.encode('utf-16-le')
        chars = p.read_bfmu(bfmu)
        body = bytes([1, 2, 0, 0, 3, 1])
        bfst = b'BFST' + b'\0' * 4 + struct.pack('<i', 1) + struct.pack('<i', 4) + body
        self.assertEqual(p.read_bfst(bfst, chars), ['ca'])
        self.assertEqual(p.sam_values('Costs.PathCell\t\t20\n#x\n')['Costs.PathCell'], '20')


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires local identified PEF')
class IdentifiedBinaryTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(os.environ['OPENTPW_PPC_BIN_ROOT'])

    def test_identified_original_witness(self):
        report = p.inspect(self.root)
        self.assertEqual(report['field_witnesses'], len(p.FIELDS))
        self.assertEqual(report['schema_offsets']['Costs.PathCell'], 1472)
        self.assertEqual(report['toc_slots']['0x1264'], '0x84ad4')
        self.assertEqual(report['witness_source'], 'tools/ppc-analysis/lanes/path/path_evidence.py')

    def _mutated(self, at, word):
        c = p.load_identified(self.root / 'SimThemePark.data')
        data = bytearray(c.code.data)
        struct.pack_into('>I', data, at, word)
        c.code.data = data
        return c

    def test_wrong_path_cost_offset_is_rejected(self):
        with self.assertRaises(p.pef.PEFError):
            p.check_fields(self._mutated(0x10f3cc, d_word(32, 3, 31, 1476)))

    def test_wrong_path_type_is_rejected(self):
        with self.assertRaises(p.pef.PEFError):
            p.check_fields(self._mutated(0x82b24, d_word(11, 0, 26, 2)))

    def test_wrong_hover_help_is_rejected(self):
        with self.assertRaises(p.pef.PEFError):
            p.check_fields(self._mutated(0x13a6ec, d_word(14, 4, 0, 443)))

    def test_refund_call_in_path_removal_is_rejected(self):
        with self.assertRaises(p.pef.PEFError):
            p.check_negatives(self._mutated(0x85b00, bl_word(0x85b00, 0xcbf50)))


@unittest.skipUnless(os.environ.get('OPENTPW_PC_DATA'), 'requires local PC Data directory')
class PcDataTests(unittest.TestCase):
    def test_costs_and_tool_help(self):
        facts = p.pc_facts(Path(os.environ['OPENTPW_PC_DATA']))
        self.assertEqual(facts['costs'], {'Costs.PathCell': 20, 'Costs.QueueCell': 75, 'Costs.MapCell': 100})
        self.assertEqual(facts['help'], p.PC_HELP)
        self.assertEqual(p.lay_line((47, 21), (47, 34))[-1], (47, 34, 0, 1, True))
        self.assertTrue(p.line_affordable(len(p.lay_line((47, 22), (47, 35))), facts['costs']['Costs.PathCell'], 280))


if __name__ == '__main__':
    unittest.main()
