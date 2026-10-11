"""ORACLE-V round 2: re-review of the oracle fixes 8a72a2c (ORACLE-F) on 447b4c7.

- Static (git objects of 8a72a2c, no binary): the register ID ECON-046, the path writer's money test, the
  queue preview against the traced validator, C# test coverage of the new branches, the BOUNCE slot order, the M3
  mutation harness against the bridge's slot shape, the head-not-ready bound arithmetic and the witness edits of
  test_queue_v1/test_path_v1. Open findings are expected failures (OV2-1 .. OV2-5).
- Binary-gated (OPENTPW_MAC_BIN or OPENTPW_PPC_BIN_ROOT = the Feral bin directory): decoded fields of the
  validator's money compare 0x84550, the pending-cost add 0x7bcc0 and SetCellType's subtraction 0x82bc8.

Only decoded fields and model results are pinned; no original bytes or listings. See
docs/reverse/REVIEW-ORACLE-2.md.
"""
from __future__ import annotations

import math
import re
import subprocess
import sys
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[2]
REPO = TOOLS.parents[1]
sys.path.insert(0, str(TOOLS / 'oracle'))

SUBJECT = '8a72a2c'
BASE = '447b4c7'
MASK32 = 0xFFFFFFFF


def show(path: str, commit: str = SUBJECT) -> str:
    return subprocess.run(['git', '-C', str(REPO), 'show', f'{commit}:{path}'], capture_output=True, text=True,
                          check=True).stdout


def has_commit(commit: str) -> bool:
    return subprocess.run(['git', '-C', str(REPO), 'cat-file', '-e', f'{commit}^{{commit}}'],
                          capture_output=True).returncode == 0


def s32(value: int) -> int:
    value &= MASK32
    return value - (1 << 32) if value & 0x80000000 else value


def method_body(source: str, signature: str) -> str:
    start = source.index(signature)
    open_at = source.index('{', start)
    depth = 0
    for index in range(open_at, len(source)):
        depth += {'{': 1, '}': -1}.get(source[index], 0)
        if depth == 0:
            return source[open_at:index + 1]
    raise ValueError(signature)


# ------------------------------------------------------------------ models (written from the disassembly)
def setcelltype_affordable(balance: int, cost: int) -> bool:
    """0x82bc8..0x82bdc: subf r5 = balance - cost (32 bits), affordable = sign bit clear."""
    return s32(balance - cost) >= 0


def validator_refuses(balance: int, cost: int, pending_cells: int) -> bool:
    """0x8451c..0x84554: the pending line cost (a 32-bit add per cell, this cell included) is skipped when 0,
    else refused when pending > balance as a signed compare (cmpw, no subtraction)."""
    pending = s32((pending_cells + 1) * cost)
    return pending != 0 and pending > s32(balance)


def csharp_can_spend_cell(balance: int, cost: int, pending_cells: int) -> bool:
    """ParkEconomy.CanSpendCell at 8a72a2c (pinned by test_can_spend_cell_text_is_the_modelled_one)."""
    return s32(s32(balance) - s32((pending_cells + 1) * cost)) >= 0


@unittest.skipUnless(has_commit(SUBJECT) and has_commit(BASE), f'{SUBJECT} or {BASE} not in this repository')
class Static(unittest.TestCase):
    @unittest.expectedFailure
    def test_econ_046_is_not_a_retired_id(self):
        """OV2-1: ECON-046 already names "upgrades need a mechanic" in APPROX-TRACE.md and PPC-scenarios.md."""
        trace = show('docs/reverse/APPROX-TRACE.md') + show('docs/reverse/PPC-scenarios.md')
        self.assertNotRegex(trace, r'\| ECON-046 \|')

    @unittest.expectedFailure
    def test_path_writer_uses_the_32_bit_money_test(self):
        """OV2-2: WritePath refuses on a 64-bit balance - cost before TrySpendCell and ignores TrySpendCell's result."""
        body = method_body(show('source/OpenTPW/World/ParkPathBuilder.cs'), 'private bool WritePath(')
        self.assertNotIn('park.Balance - park.CellCost( CellPurchase.Path ) < 0', body)

    def test_path_writer_divergence_cases(self):
        """The two cases OV2-2 changes: the 64-bit test and the original disagree at the 32-bit edges."""
        for balance, cost in ((-0x80000000, 1), (0x7FFFFFFF, -1)):
            self.assertNotEqual(balance - cost >= 0, setcelltype_affordable(balance, cost), (balance, cost))

    def test_can_spend_cell_text_is_the_modelled_one(self):
        source = show('source/OpenTPW/Economy/ParkEconomy.cs')
        self.assertIn('public bool CanSpendCell( CellPurchase kind, int pending ) => '
                      'unchecked( (int)Balance - (int)((pending + 1) * CellCost( kind )) ) >= 0;', source)

    @unittest.expectedFailure
    def test_queue_preview_follows_the_traced_validator(self):
        """OV2-3: the preview extension uses SetCellType's wrapping subtraction; the validator compares pending > balance."""
        for balance, cost, pending in ((-0x80000000, 1, 0), (0x7FFFFFFF, -1, 0), (-5, 0, 3), (100, 75, 1)):
            self.assertEqual(not validator_refuses(balance, cost, pending), csharp_can_spend_cell(balance, cost, pending),
                             (balance, cost, pending))

    def test_preview_and_validator_agree_off_the_edges(self):
        for balance in (0, 74, 75, 149, 150, 5000):
            for pending in range(4):
                self.assertEqual(not validator_refuses(balance, 75, pending), csharp_can_spend_cell(balance, 75, pending))

    @unittest.expectedFailure
    def test_negative_cell_cost_has_a_csharp_test(self):
        """OV2-4: reverting ECON-046 (Post(OtherCosts, cost) with cost < 0) fails no C# test and no oracle input."""
        tests = '\n'.join(show(f'source/OpenTPW.Tests/{name}') for name in ('ParkEconomyTests.cs', 'QueueTests.cs', 'PathBuilderTests.cs'))
        self.assertRegex(tests, r'Costs\.(QueueCell|PathCell) -\d')

    @unittest.expectedFailure
    def test_unbounce_window_has_a_csharp_test(self):
        """OV2-4: the strict deadline and the 200 ms window are checked only by the oracle (private binary)."""
        tests = '\n'.join(show(f'source/OpenTPW.Tests/{name}') for name in ('QueueTests.cs', 'GuestAssetTests.cs'))
        self.assertIn('TakeUnbounced', tests)

    @unittest.expectedFailure
    def test_bounce_takes_the_first_free_slot(self):
        """OV2-6: BOARD-plan §5 (0xadf40) fills the first free slot; the bridge appends, so release order after a hole
        differs from the original's index order."""
        source = show('source/OpenTPW/World/Guests/RideVisitorBridge.cs')
        self.assertNotIn('bouncing.Add( (guest, now + seconds * 1000.0, now) )', source)

    def test_bridge_slot_shape(self):
        source = show('source/OpenTPW/World/Guests/RideVisitorBridge.cs')
        self.assertIn('List<(int Guest, double Until, double Start)> bouncing', source)

    @unittest.expectedFailure
    def test_m3_mutation_harness_of_the_subject_matches_the_slot_shape(self):
        """OV2-5: v3/v4 hooks cast the slot list to (int, double); every v4 run and three v3 runs crash at 8a72a2c."""
        for name in ('test_m3_gate_v3.py', 'test_m3_gate_v4.py'):
            self.assertNotIn('List<(int Guest, double Until)>', show(f'tools/ppc-analysis/lanes/review/{name}'))

    def test_m3_mutation_harness_in_this_tree_matches_the_slot_shape(self):
        here = Path(__file__).resolve().parent
        for name in ('test_m3_gate_v3.py', 'test_m3_gate_v4.py'):
            text = (here / name).read_text(encoding='utf-8')
            self.assertNotIn('List<(int Guest, double Until)>', text)
            self.assertIn('List<(int Guest, double Until, double Start)>', text)

    def test_head_not_ready_bound_follows_from_six_cells(self):
        """M3-GATE: N = ceil(21 / 4) = 6 cells; walk N + 4 cells at 1 x 0.7 cells/s in 248 ms turns, + 3 + 22 + 1."""
        cells = math.ceil(math.floor(130 * 5 / 30) / 4)
        self.assertEqual(6, cells)
        walk = math.ceil((cells + 4) / 0.7 * 1000 / 248)
        self.assertEqual((58, 84), (walk, walk + 3 + 22 + 1))
        old = math.ceil((25 + 4) / 0.7 * 1000 / 248)
        self.assertEqual(194, old + 3 + 22 + 1)
        self.assertIn('**84 turns**', show('docs/M3-GATE.md'))

    def test_witness_edits_only_flip_the_has_queue_polarity(self):
        """test_queue_v1/test_path_v1: every expected value survives; only the HasQueue / last-cell argument flips."""
        def calls(text: str) -> list[tuple[str, str]]:
            return re.findall(r'assertEqual\((0x[0-9A-F]+|\d+), queue_limit\((True|False),', text)
        before = calls(show('tools/ppc-analysis/lanes/review/test_queue_v1.py', BASE))
        after = calls(show('tools/ppc-analysis/lanes/review/test_queue_v1.py'))
        self.assertEqual(len(before), len(after))
        flipped = {(value, 'True' if flag == 'False' else 'False') for value, flag in before}
        self.assertEqual(flipped, set(after))
        path = show('tools/ppc-analysis/lanes/review/test_path_v1.py')
        self.assertIn('return new == 3 and old == 1 and not last_cell', path)
        self.assertIn('self.assertFalse(can_change_cell_type(1, 3, True)', path)


def _root():
    from harness import bin_root
    return bin_root()


@unittest.skipUnless(_root(), 'requires the identified Feral bin directory (OPENTPW_MAC_BIN or OPENTPW_PPC_BIN_ROOT)')
class Disassembly(unittest.TestCase):
    """Decoded fields of the three money instructions (code-section offsets)."""

    @classmethod
    def setUpClass(cls):
        import struct
        from harness import Image
        code = Image.load(_root()).container.code.data
        cls.word = staticmethod(lambda offset: struct.unpack_from('>I', code, offset)[0])

    def fields(self, offset):
        w = self.word(offset)
        return w >> 26, (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31, (w >> 1) & 0x3FF, w & 1

    def test_validator_compares_pending_with_balance(self):
        # cmpw cr0, r3 (pending from 0x7bccc), r18 (balance from 0xcbf48): opcode 31, xo 0, L 0.
        self.assertEqual((31, 0, 3, 18, 0, 0), self.fields(0x84550))
        # bc BO 4 (branch if false), BI 1 (cr0 GT) to the accept path: refuse only when pending > balance.
        w = self.word(0x84554)
        self.assertEqual((16, 4, 1), (w >> 26, (w >> 21) & 31, (w >> 16) & 31))

    def test_pending_cost_is_a_plain_32_bit_add(self):
        self.assertEqual((31, 0, 0, 3, 266, 0), self.fields(0x7BCC0))  # add r0, r0, r3 (OE 0: no trap, wraps)

    def test_setcelltype_subtracts(self):
        self.assertEqual((31, 5, 27, 3, 40, 0), self.fields(0x82BC8))  # subf r5, r27, r3 = balance - cost


if __name__ == '__main__':
    unittest.main()
