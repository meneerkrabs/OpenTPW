"""Synthetic design checks only; no native data, C# or game execution."""
import importlib.util
import itertools
import json
from pathlib import Path
import sys
import unittest

spec = importlib.util.spec_from_file_location(
    "track_underlay_model", Path(__file__).with_name("track_underlay_model.py"))
model = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = model
spec.loader.exec_module(model)


class UnderlayContractTests(unittest.TestCase):
    def test_legacy_bit_has_fifteen_distinct_nonempty_preimages(self):
        # The current writer ORs the four clear results into one memory bit.
        groups = {False: [], True: []}
        for cells in itertools.product((False, True), repeat=4):
            groups[any(cells)].append(cells)
        self.assertEqual(len(groups[False]), 1)
        self.assertEqual(len(groups[True]), 15)
        self.assertEqual(len(set(groups[True])), 15)
        # Every possible choice reconstructs at most one of those 15 layouts.
        for guessed_layout in groups[True]:
            self.assertEqual(sum(guessed_layout != original
                                 for original in groups[True]), 14)

    def test_both_legacy_values_remain_unknown_not_empty(self):
        for bit in (False, True):
            state = model.migrate_legacy(bit)
            self.assertEqual(state.cells, (model.UNKNOWN,) * 4)
            self.assertIs(state.legacy_over_path, bit)
            self.assertIsNone(model.path_presence(state))
            self.assertEqual(model.decode(model.encode(state)), state)
            with self.assertRaisesRegex(ValueError, "explicit migration policy"):
                model.remove_track(state)
        self.assertIs(model.path_presence(model.Footprint((model.EMPTY,) * 4)), False)

    def test_all_known_layouts_roundtrip_and_both_build_orders(self):
        choices = [model.EMPTY,
                   model.Cell("path", links=5, owner=17, placement_count=3),
                   model.Cell("queue", links=10, owner=29,
                              queue_direction=2, placement_count=7)]
        count = 0
        for layout in itertools.product(choices, repeat=4):
            plain = model.Footprint(layout)
            before = model.put_track(plain)
            after = model.put_track(model.Footprint((model.EMPTY,) * 4))
            for i, cell in enumerate(layout):
                after = model.put_cell(after, i, cell)
            self.assertEqual(before, after)
            self.assertEqual(model.decode(model.encode(before)), before)
            self.assertEqual(model.remove_track(before), plain)
            self.assertIs(model.path_presence(before),
                          any(cell.kind != "empty" for cell in layout))
            # Explicit path/queue removal leaves track and other cells intact.
            for i in range(4):
                removed = model.put_cell(before, i, model.EMPTY)
                expected = list(layout)
                expected[i] = model.EMPTY
                self.assertTrue(removed.track)
                self.assertEqual(removed.cells, tuple(expected))
                self.assertEqual(model.remove_track(removed).cells, tuple(expected))
            count += 1
        self.assertEqual(count, 81)

    def test_distinct_metadata_per_cell_is_not_collapsed(self):
        cells = tuple(model.Cell("queue" if i % 2 else "path", links=i + 1,
                                 owner=100 + i, queue_direction=i,
                                 placement_count=200 + i) for i in range(4))
        state = model.put_track(model.Footprint(cells))
        self.assertEqual(model.decode(model.encode(state)).cells, cells)
        self.assertEqual(model.remove_track(state).cells, cells)

    def test_partial_recovery_never_fabricates_remaining_cells(self):
        state = model.migrate_legacy(True)
        for i in range(4):
            state = model.put_cell(state, i, model.EMPTY)
            self.assertEqual(state.cells.count(model.UNKNOWN), 3 - i)
            self.assertTrue(state.legacy_over_path)  # history is not live presence
            if i < 3:
                with self.assertRaises(ValueError):
                    model.remove_track(state)
        self.assertFalse(model.path_presence(state))
        self.assertEqual(model.remove_track(state).cells, (model.EMPTY,) * 4)

    def test_known_path_presence_does_not_make_unknown_cells_known(self):
        state = model.put_cell(model.migrate_legacy(False), 2,
                               model.Cell("queue", owner=8, links=3))
        self.assertIs(model.path_presence(state), True)
        self.assertEqual(state.cells.count(model.UNKNOWN), 3)
        with self.assertRaises(ValueError):
            model.remove_track(state)

    def test_invalid_records_are_rejected(self):
        good = json.loads(model.encode(model.Footprint((model.EMPTY,) * 4)))
        for version in (0, 2, True, "1"):
            with self.assertRaises(ValueError):
                model.decode(json.dumps(dict(good, version=version)))
        for cells in ([], good["cells"][:3], good["cells"] * 2):
            with self.assertRaises(ValueError):
                model.decode(json.dumps(dict(good, cells=cells)))
        for kind in ("unknown", "empty"):
            with self.assertRaises(ValueError):
                model.Cell(kind, owner=1)
        for value in (-1, True, 1.5):
            with self.assertRaises(ValueError):
                model.Cell("path", links=value)
        with self.assertRaises(ValueError):
            model.decode(json.dumps(dict(good, unexpected=1)))
        with self.assertRaises(ValueError):
            model.put_cell(model.Footprint((model.EMPTY,) * 4), -1, model.EMPTY)


if __name__ == "__main__":
    unittest.main(verbosity=2)
