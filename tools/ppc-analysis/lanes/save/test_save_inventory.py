"""Save inventory tests: synthetic checks always run; native and fixture checks need private files."""
import copy
import os
from pathlib import Path
import struct
import unittest

import save_inventory as inv

MAC_APP = os.environ.get('OPENTPW_MAC_APP')
PC_FIXTURE = os.environ.get('OPENTPW_PC_FIXTURE')


def thing(following, model, body):
    return struct.pack('<II', following, model) + body


def attraction(counts):
    body = bytearray(8 + 4 + 2 + 32 + 4 + 2 + 132 + 12 + 24)
    for index, count in enumerate(counts):
        body += struct.pack('<IIBI', 0, count, 0, 0) + bytes(4 * count)
        if index in (1, 2):
            body += bytes(4)
    return bytes(body + bytes(1 + 1 + 4 + 2 + 24 + 12 + 21))


class SyntheticTests(unittest.TestCase):
    def test_delimiters_are_stored_byte_swapped(self):
        self.assertEqual(inv.file_tag(0x57524C44), 'DLRW')
        self.assertEqual(inv.file_tag(0x41445343), 'CSDA')

    def test_body_components_match_the_closure_sizes(self):
        expected = {1: 525, 4: 503, 5: 505, 6: 501, 7: 503, 8: 501, 9: 95, 10: 10, 11: 5838, 12: 8,
                    13: 73536, 14: 4182, 15: 91, 16: 292, 17: 8, 19: 929}
        self.assertEqual(inv.BODY_BYTES, expected)
        self.assertEqual(set(inv.BODY_BYTES) | {3}, set(inv.MODELS))
        self.assertEqual(len(attraction([30] * 6)), 1091)
        self.assertEqual(inv.attraction_body(attraction([0, 1, 2, 3, 4, 5]), 0), 371 + 4 * 15)

    def test_attraction_history_count_is_bounded(self):
        with self.assertRaises(ValueError):
            inv.attraction_body(attraction([1025, 0, 0, 0, 0, 0]), 0)

    def payload(self):
        blocks = [bytes(index + 1) + inv.file_tag(int.from_bytes(tag.encode(), 'big')).encode()
                  for index, (_, tag, *_) in enumerate(inv.SECTIONS)]
        return b''.join(blocks) + bytes(4)

    def test_split_blocks_follows_writer_order(self):
        blocks = inv.split_blocks(self.payload())
        self.assertEqual([end - start for start, end in blocks.values()], list(range(1, 18)) + [4])
        self.assertEqual(list(blocks)[:2] + list(blocks)[-2:], ['World', 'Scripts', 'AdvisorScoring', 'UI'])

    def test_split_blocks_rejects_duplicate_and_reordered_delimiters(self):
        with self.assertRaises(ValueError):
            inv.split_blocks(self.payload() + b'DLRW')
        swapped = list(inv.SECTIONS)
        swapped[0], swapped[1] = swapped[1], swapped[0]
        with self.assertRaises(ValueError):
            inv.split_blocks(self.payload(), swapped)

    def chain(self):
        return struct.pack('<I', 3) + thing(2, 12, bytes(8)) + thing(1, 10, bytes(10)) + thing(0, 17, bytes(8))

    def test_thing_chain_must_close_exactly_at_the_delimiter(self):
        data = self.chain()
        things = inv.walk_things(data, 0, len(data))
        self.assertEqual([(t['id'], t['model'], t['body']) for t in things], [(3, 12, 8), (2, 10, 10), (1, 17, 8)])
        with self.assertRaises(ValueError):
            inv.walk_things(data + bytes(1), 0, len(data) + 1)
        with self.assertRaises(ValueError):
            inv.walk_things(data, 0, len(data) - 1)

    def test_thing_chain_rejects_wrong_size_unknown_model_and_cycles(self):
        data = self.chain()
        wrong = {**inv.BODY_BYTES, 10: 11}
        with self.assertRaises(ValueError):
            inv.walk_things(data, 0, len(data), wrong)
        for model in (0, 2, 18, 20):
            bad = struct.pack('<I', 3) + thing(0, model, bytes(8))
            with self.assertRaises(ValueError):
                inv.walk_things(bad, 0, len(bad))
        cycle = struct.pack('<I', 3) + thing(3, 12, bytes(8)) + thing(0, 12, bytes(8))
        with self.assertRaises(ValueError):
            inv.walk_things(cycle, 0, len(cycle))


@unittest.skipUnless(MAC_APP, 'OPENTPW_MAC_APP (SimThemePark.data) not set')
class NativeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = inv.identified(Path(MAC_APP))

    def test_inventory(self):
        result = inv.inspect(Path(MAC_APP))
        self.assertEqual([s['file_bytes'] for s in result['sections']],
                         ['DLRW', 'CSPS', 'TRAP', 'SSEM', 'KOLC', 'TNAV', 'SYSG', 'SYSR', 'KART',
                          'RYLF', 'ESSR', 'EMAK', 'SAOC', 'SVDA', 'NUOS', 'STHC', 'CSDA'])
        self.assertEqual(result['jump_tables'], {'writer': 0x44924, 'reader': 0x44974})
        self.assertEqual(result['rejected_models'], [0, 2, 18])

    def mutated(self, check, table, index, field, value):
        rows = copy.deepcopy(list(table))
        row = list(rows[index])
        row[field] = value
        rows[index] = tuple(row)
        with self.assertRaises(inv.pef.PEFError):
            check(self.app, rows)

    def test_wrong_delimiter_writer_or_reader_fails(self):
        self.mutated(inv.check_sections, inv.SECTIONS, 0, 1, 'WRLE')
        self.mutated(inv.check_sections, inv.SECTIONS, 10, 1, 'RSYS')
        self.mutated(inv.check_sections, inv.SECTIONS, 4, 3, 0x10e998)
        self.mutated(inv.check_sections, inv.SECTIONS, 4, 8, 0x10e944)
        self.mutated(inv.check_sections, inv.SECTIONS, 10, 5, 3)

    def test_wrong_loop_bound_or_stride_fails(self):
        self.mutated(inv.check_loops, inv.LOOP_WITNESSES, 1, 2, (0, 29, 21))
        self.mutated(inv.check_loops, inv.LOOP_WITNESSES, 9, 2, (0, 29, 350))
        self.mutated(inv.check_loops, inv.LOOP_WITNESSES, 6, 2, (3, 30, 24))
        self.mutated(inv.check_loops, inv.LOOP_WITNESSES, 11, 2, (6, 0, 12))

    def test_wrong_model_serializer_or_allocation_fails(self):
        for model, field, value in ((4, 1, 0xd57ac), (16, 4, 292), (11, 2, 0x10654c), (1, 3, 0x107040)):
            models = dict(inv.MODELS)
            row = list(models[model])
            row[field] = value
            models[model] = tuple(row)
            with self.subTest(model=model, field=field), self.assertRaises(inv.pef.PEFError):
                inv.check_tables(self.app, models)


@unittest.skipUnless(PC_FIXTURE, 'OPENTPW_PC_FIXTURE (Easymode.TPWI) not set')
class FixtureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.path = Path(PC_FIXTURE)
        cls.payload = inv.decode_fixture(cls.path.read_bytes())
        cls.world_end = inv.split_blocks(cls.payload)['World'][1]

    def test_chain_closes_at_the_world_delimiter(self):
        facts = inv.fixture_facts(self.path)
        self.assertEqual(facts['thing_count'], 42)
        self.assertEqual(facts['model_counts'], {'1': 13, '3': 14, '4': 1, '5': 1, '6': 1, '7': 1, '8': 1, '9': 1,
                                                 '10': 1, '11': 1, '12': 1, '13': 1, '14': 1, '15': 1, '16': 1,
                                                 '17': 1, '19': 1})
        self.assertEqual(sorted(facts['chain_ids']), list(range(1, 43)))
        self.assertEqual(facts['advisor_pending_valid_flags'], [0, 0, 1, 0, 0, 0, 0, 0])
        self.assertEqual(self.world_end, 1495462)

    def test_any_wrong_body_size_breaks_closure(self):
        for model, delta in ((15, -1), (13, 1), (11, 16), (19, -45), (1, 1)):
            sizes = {**inv.BODY_BYTES, model: inv.BODY_BYTES[model] + delta}
            with self.subTest(model=model), self.assertRaises(ValueError):
                inv.walk_things(self.payload, inv.EASYMODE_MAP_END, self.world_end, sizes)

    def test_wrong_start_breaks_closure(self):
        for start in (inv.EASYMODE_MAP_END - 1, inv.EASYMODE_MAP_END + 1):
            with self.subTest(start=start), self.assertRaises((ValueError, struct.error)):
                inv.walk_things(self.payload, start, self.world_end)


if __name__ == '__main__':
    unittest.main()
