"""Framing/graph negative cases; all record bodies are invented, never executed."""
import os
from pathlib import Path
import struct
import unittest

import saved_script_graph as graph
import saved_script_native as native


def fixture(ids=(3, 2, 1), references=None, header_count=None):
    refs = references or {}
    tokens = [100 + index * 100 for index in range(len(ids))]
    header = b'RSSE' + struct.pack('<6I', 20, 1, 7, 42,
                                   len(ids) if header_count is None else header_count,
                                   tokens[0] if tokens else 0)
    data = bytearray(header + b'PAD_' * 5 + struct.pack('<II', len(ids), 244))
    positions = []
    for index, identifier in enumerate(ids):
        positions.append(len(data))
        fixed = bytearray(244)
        struct.pack_into('<6I', fixed, 0, tokens[index + 1] if index + 1 < len(tokens) else 0,
                         tokens[index - 1] if index else 0, identifier, *refs.get(identifier, (0, 0, 0)))
        struct.pack_into('<i', fixed, 60, -10000)
        struct.pack_into('<i', fixed, 148, 17)
        struct.pack_into('<II', fixed, 160, 0xffffffff, 0x80000001)
        struct.pack_into('<h', fixed, 192, -50)
        struct.pack_into('<I', fixed, 196, 12345)
        data += fixed
        data += struct.pack('<9I', *([0] * 9))
        data += b'OBJ ' + struct.pack('<II', 0, 28)
    return data, positions


class SavedGraphTests(unittest.TestCase):
    def test_ids_and_phase_are_restored_without_renumbering(self):
        data, _ = fixture()
        result = graph.read_graph(data, 0)
        self.assertEqual([3, 2, 1], result['serialized_order'])
        self.assertEqual([1, 2, 3], result['restored_order'])
        self.assertEqual(7, result['manager_header']['pass_counter'])
        self.assertEqual(42, result['manager_header']['next_script_id'])
        self.assertEqual(len(data), result['manager_end_offset'])

    def test_unsigned_deadlines_and_signed_fields_remain_distinct(self):
        data, _ = fixture(ids=(9,))
        record = graph.read_graph(data, 0)['records'][0]
        self.assertEqual((-10000, -50, 17), (record['program_word_index'], record['speed_bias'], record['slice_budget']))
        self.assertEqual((0xffffffff, 0x80000001, 12345),
                         (record['wait_deadline'], record['animation_wait_deadline'], record['timer_deadline']))

    def test_global_id_edges_resolve_across_serialized_order(self):
        data, _ = fixture(ids=(3, 2, 1), references={1: (2, 0, 3), 2: (0, 1, 0)})
        self.assertEqual([{'from': 2, 'role': 'parent_id', 'to': 1},
                          {'from': 1, 'role': 'child_id', 'to': 2},
                          {'from': 1, 'role': 'secondary_script_id', 'to': 3}],
                         graph.read_graph(data, 0)['id_reference_edges'])

    def test_stored_count_and_physical_record_count_are_not_collapsed(self):
        data, _ = fixture(ids=(2, 1), header_count=7)
        result = graph.read_graph(data, 0)
        self.assertEqual(7, result['manager_header']['list_count'])
        self.assertEqual(2, len(result['records']))
        self.assertEqual(9, result['source_reader_counter_after_insertions'])

    def test_empty_initialized_list_keeps_phase_and_allocator(self):
        data, _ = fixture(ids=())
        result = graph.read_graph(data, 0)
        self.assertEqual([], result['restored_order'])
        self.assertEqual(7, result['manager_header']['pass_counter'])
        self.assertEqual(42, result['manager_header']['next_script_id'])

    def test_duplicate_and_null_ids_are_rejected(self):
        for ids in [(3, 3), (0,)]:
            with self.subTest(ids=ids), self.assertRaises(ValueError):
                graph.read_graph(fixture(ids=ids)[0], 0)

    def test_dangling_references_are_rejected(self):
        for refs in [(999, 0, 0), (0, 999, 0), (0, 0, 999)]:
            with self.subTest(refs=refs), self.assertRaises(ValueError):
                graph.read_graph(fixture(ids=(1,), references={1: refs})[0], 0)

    def test_saved_list_cycles_and_broken_previous_tokens_are_rejected(self):
        data, positions = fixture()
        for position, relative, value in [(positions[1], 4, 999), (positions[-1], 0, 100),
                                           (positions[0], 0, 100)]:
            changed = bytearray(data)
            struct.pack_into('<I', changed, position + relative, value)
            with self.subTest(position=position, relative=relative), self.assertRaises(ValueError):
                graph.read_graph(changed, 0)

    def test_truncated_fixed_blob_and_object_record_spans_are_rejected(self):
        data, positions = fixture(ids=(1,))
        for size in [0, 27, 48, 55, positions[0] + 243, len(data) - 1]:
            with self.subTest(size=size), self.assertRaises(ValueError):
                graph.read_graph(data[:size], 0)
        changed = bytearray(data)
        struct.pack_into('<I', changed, positions[0] + 244, 4)
        with self.assertRaises(ValueError):
            graph.read_graph(changed, 0)

    def test_blob_size_count_limit_and_stride_mismatch_are_rejected(self):
        data, positions = fixture(ids=(1,))
        for position, value in [(48, 1025), (52, 243), (positions[0] + 244, 0xffffffff),
                                (len(data) - 4, 27)]:
            changed = bytearray(data)
            struct.pack_into('<I', changed, position, value)
            with self.subTest(position=position, value=value), self.assertRaises(ValueError):
                graph.read_graph(changed, 0)

    def test_nonzero_sized_auxiliary_blocks_follow_source_widths(self):
        data, positions = fixture(ids=(1,))
        fixed = bytearray(data[positions[0]:positions[0] + 244])
        blocks = bytearray()
        for name, field, width, count_wire in graph.BLOCKS:
            elements = 2 if field is not None else 3
            if field is not None:
                struct.pack_into('<I', fixed, field, elements)
            size = elements * width
            blocks += struct.pack('<I', elements if count_wire else size) + bytes(size)
        framed = data[:positions[0]] + fixed + blocks + b'OBJ ' + struct.pack('<II', 2, 28) + bytes(56)
        result = graph.read_graph(framed, 0)
        self.assertEqual(len(framed), result['manager_end_offset'])
        self.assertEqual(8, result['records'][0]['blocks']['code_words']['bytes'])
        self.assertEqual(64, result['records'][0]['blocks']['metadata32']['bytes'])
        self.assertEqual(2, result['records'][0]['object_bindings']['count'])

    def test_coherent_oversized_blob_is_rejected_before_read(self):
        data, positions = fixture(ids=(1,))
        words = graph.MAX_BLOB // 4 + 1
        struct.pack_into('<I', data, positions[0] + 80, words)
        struct.pack_into('<I', data, positions[0] + 244, words * 4)
        with self.assertRaisesRegex(ValueError, 'oversized'):
            graph.read_graph(data, 0)

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires identified original PEF')
    def test_identified_native_graph_producer_consumer(self):
        result = native.inspect(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual('0xb4818', result['reader'])
        self.assertEqual('0xb3868', result['writer'])
        self.assertEqual({'12': 'child ID', '16': 'parent ID', '20': 'secondary script ID'}, result['reference_fields'])
        self.assertEqual(5, len(result['region_sha256']))

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_SAVE_PATH'), 'requires identified PC save')
    def test_identified_original_records_and_endpoint(self):
        result = graph.inspect(Path(os.environ['OPENTPW_PPC_SAVE_PATH']))
        self.assertEqual(14, len(result['records']))
        self.assertEqual(1606398, result['manager_end_offset'])
        self.assertEqual([1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15], result['restored_order'])
        self.assertEqual([], result['id_reference_edges'])


if __name__ == '__main__':
    unittest.main()
