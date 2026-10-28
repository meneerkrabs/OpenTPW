"""WALK-V2: focused re-review of walk-fix at 2079e79 (WALK-F faa64cc and WALK-F2 2079e79 on the merge b10710e).

- Static (git objects of 2079e79, no binary): the merged head-not-ready bound and each head's own limit, the GATE-006
  register entry and its tags, the young-head deferral, the separation port, avoid_walls' first pushing edge, the
  toilet term's phase and byte compare, the schema 7 and gate hash pins, ECON-048, the commit and preview money
  tests and BOUNCE's first free slot. The findings WV2-1 .. WV2-5 were closed at the merge onto main (WALK-M); their
  witnesses read the working tree.
- Binary-gated (OPENTPW_MAC_BIN or OPENTPW_PPC_BIN_ROOT = the Feral bin directory): decoded fields of the neighbour
  query 0x10087c, the separation caller 0xfd778 and its static initialiser 0x1002f4, avoid_walls' edge exits, the
  toilet compare and BOUNCE's slot scan, and the separation callback 0xfd5f4 run in OpenTPW's own PPC interpreter
  against GuestNavigation.Separation's per-neighbour step.

Only decoded fields and model results are pinned; no original bytes or listings. See docs/reverse/REVIEW-WALK-2.md.
"""
from __future__ import annotations

import math
import random
import re
import subprocess
import sys
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[2]
REPO = TOOLS.parents[1]
sys.path.insert(0, str(TOOLS / 'oracle'))

SUBJECT = '2079e79'
# Fork main as fetched for the trial merge (origin/main on October 11, 2026).
MAIN = 'c09c6ba'
MASK32 = 0xFFFFFFFF
ONE = 65536
RADIUS = 13107


def show(path: str, commit: str = SUBJECT) -> str:
    return subprocess.run(['git', '-C', str(REPO), 'show', f'{commit}:{path}'], capture_output=True, text=True,
                          check=True).stdout.lstrip('﻿')


def current(path: str) -> str:
    """The working tree: the findings were closed after SUBJECT, at the merge onto main (WALK-M)."""
    return (REPO / path).read_text(encoding='utf-8').lstrip('\ufeff')


def register_document() -> str:
    """The fidelity register as tools/fidelity_register.py generates it from the working tree (not committed)."""
    import importlib.util
    spec = importlib.util.spec_from_file_location('fidelity_register', REPO / 'tools' / 'fidelity_register.py')
    module = importlib.util.module_from_spec(spec)
    # dataclasses resolve their module through sys.modules (Python < 3.13 fails otherwise)
    sys.modules.setdefault(spec.name, module)
    spec.loader.exec_module(module)
    return module.render(module.collect(REPO))


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
def fixdiv(a: int, b: int) -> int:
    """OpenTPW's Fixed16.Div, the stand-in for the InterfaceLib FixDiv import in the callback run below."""
    if b == 0:
        return 0x7FFFFFFF if a >= 0 else -0x80000000
    return s32(math.floor((a * ONE) / b + 0.5))


def octile(x: int, y: int) -> int:
    """0xfd650..0xfd694: |x| + |y| - min(|x|, |y|) / 2 (srawi 1)."""
    ax, ay = abs(x), abs(y)
    return ax + ay - (min(ax, ay) >> 1)


def separation_push(self_pos: tuple[int, int], other_pos: tuple[int, int]) -> tuple[int, int]:
    """GuestNavigation.Separation's per-neighbour step at 2079e79 (pinned by test_separation_step_text)."""
    ox, oy = s32(self_pos[0] - other_pos[0]), s32(self_pos[1] - other_pos[1])
    d = octile(ox, oy)
    if d < RADIUS + RADIUS + ONE and d != 0:
        return fixdiv(ox, d), fixdiv(oy, d)
    return 0, 0


def head_bound(cells: int) -> int:
    """HeadNotReadyBound: N x C + 2 x w2 + move-up wait trunc(1.2 x 2) + 1 + 2 interludes x (10 + 1) + 1."""
    return max(1, cells) * 9 + 2 * 15 + (int(1.2 * 2) + 1) + 2 * (10 + 1) + 1


@unittest.skipUnless(has_commit(SUBJECT), f'{SUBJECT} not in this repository')
class Gate(unittest.TestCase):
    gate = None

    @classmethod
    def setUpClass(cls):
        cls.gate = show('source/OpenTPW/Client/M3Gate.cs')

    def test_merged_head_bound_is_110_65_65(self):
        self.assertEqual((110, 65, 65), (head_bound(6), head_bound(1), head_bound(1)))
        self.assertIn('internal const long QueueCellWalkTurns = 9;', self.gate)
        self.assertIn('internal const int HeadInterludes = 2;', self.gate)
        self.assertIn('var turns = walkTurns + HeadWaitTurns;', self.gate)
        # The wait part alone, which a head standing at position 0 gets: 3 + 22 + 1.
        self.assertEqual(26, head_bound(1) - (9 + 30))

    def test_each_head_is_held_to_the_smaller_of_the_queue_bound_and_its_own_walk(self):
        limit = method_body(self.gate, 'internal static long HeadNotReadyLimit(')
        self.assertIn('Math.Min( queueBound, walk + HeadWaitTurns )', limit)
        # The limit is set once, at the first evaluation that judges the head, and a streak beyond it is a violation.
        self.assertIn('state.HeadLimit = HeadNotReadyLimit( guests.Find( check.HeadGuest ), bridge, state.HeadBound );', self.gate)
        self.assertIn('if ( streak > state.HeadLimit )', self.gate)

    def test_a_young_head_is_deferred_not_exempt(self):
        """WV-2: a head under 15 turns old is judged from the turn it reaches that age; no permanent exemption."""
        self.assertNotIn('HeadYoung', self.gate)
        self.assertIn('state.HeadDeferred = true;', self.gate)
        self.assertIn('is { } headTrack && guests.ParkTurn - headTrack.FirstSeenTurn < MinimumWalkAgeTurns', self.gate)

    def test_the_remaining_walk_counts_walks_not_the_cell_index(self):
        """WV-12: HeadRemainingWalkTurns charges the walk as set up (WalkCrossings) and never QueueCellIndex."""
        body = method_body(self.gate, 'internal static long HeadRemainingWalkTurns(')
        self.assertIn('QueueWalkTurns( navigation.WalkCrossings )', body)
        self.assertNotIn('QueueCellIndex', body)
        self.assertIn('return cell == entrance ? 0 : cells.Count;', body)

    def test_gate_006_is_registered_and_tagged_where_c_is_used(self):
        register = show('source/OpenTPW/Client/M3GateApproximations.cs')
        self.assertIn('("GATE-006", "the queue walk terms (C = 9', register)
        self.assertIn("enumerating OpenTPW's own port", register)
        self.assertIn('accepted by the owner on 2026-10-10', register)
        tags = [m.start() for m in re.finditer(r'\[APPROX:GATE-006\]', self.gate)]
        for anchor in ('HeadNotReadyBound( int queueCells )', 'internal const long QueueCellWalkTurns = 9;',
                       'internal static long HeadRemainingWalkTurns('):
            at = self.gate.index(anchor)
            self.assertTrue(any(0 < at - tag < 1200 for tag in tags), anchor)
        self.assertIn("$\"[APPROX:GATE-006] two queue walks", self.gate)

    def test_c_is_labelled_as_an_enumeration_of_the_port(self):
        doc = show('docs/M3-GATE.md')
        self.assertIn("the bounded enumeration of OpenTPW's port of the traced step (WALK-plan §15), `[APPROX:GATE-006]`", doc)
        self.assertIn("the bounds hold for\nOpenTPW's walk and match the original's to the extent those entries do", doc)

    def test_gate_hash_and_schema_pins(self):
        self.assertIn('public const int SchemaVersion = 7;', show('source/OpenTPW/World/WorldStateHash.cs'))
        self.assertIn('0x082000C95B7EB3FE', show('source/OpenTPW.Tests/DeterminismTests.cs'))
        self.assertIn('0x082000C95B7EB3FE', show('docs/DETERMINISM.md'))
        self.assertIn('A07A2871552E22E8', show('docs/M3-GATE.md'))


@unittest.skipUnless(has_commit(SUBJECT), f'{SUBJECT} not in this repository')
class Walk(unittest.TestCase):
    nav = sim = None

    @classmethod
    def setUpClass(cls):
        cls.nav = show('source/OpenTPW/World/Guests/GuestNavigation.cs')
        cls.sim = show('source/OpenTPW/World/Guests/GuestSimulation.cs')

    def test_separation_step_text(self):
        body = method_body(self.nav, 'public static (int X, int Y) Separation(')
        self.assertIn('const int Range = 2 * Radius;', body)
        self.assertIn('const int Limit = (Range >> 8) * (Range >> 8);', body)
        self.assertIn('if ( square == 0 || square >= Limit )', body)
        self.assertIn('var distance = Fixed16.Octile( offsetX, offsetY );', body)
        self.assertIn('if ( distance < Radius + Radius + Fixed16.One && distance != 0 )', body)
        self.assertIn('if ( ++visited >= 5 )', body)
        self.assertIn('public const int Radius = 13107;', self.nav)
        self.assertEqual(102 * 102, ((2 * RADIUS) >> 8) ** 2)

    def test_the_neighbour_scan_is_3_by_3_rows_from_the_top(self):
        # ceil(2 x radius / ONE) = 1 cell each way (0x1008a8..0x1008ac).
        self.assertEqual(1, (2 * RADIUS + ONE - 1) >> 16)
        body = method_body(self.sim, 'private (int X, int Y) SeparationForStep(')
        self.assertLess(body.index('for ( var y = cellY - 1; y <= cellY + 1; y++ )'), body.index('for ( var x = cellX - 1; x <= cellX + 1; x++ )'))
        self.assertIn('other.State is not (GuestState.Using or GuestState.Gone)', body)

    def test_neighbour_lists_are_rebuilt_before_any_step_of_a_turn(self):
        tick = method_body(self.sim, 'public void Tick( float deltaSeconds )')
        self.assertLess(tick.index('RebuildOccupants();'), tick.index('UpdateGuest( guests[index], deltaSeconds );'))
        self.assertIn('if ( !turnStarted )', self.sim)

    def test_avoid_walls_stops_at_the_first_pushing_edge(self):
        self.assertIn('if ( wallX == 0 && VelocityY >= 0 )', self.nav)
        self.assertIn('else if ( wallX == 0 )', self.nav)

    def test_toilet_term_runs_on_the_needs_phase_with_an_unsigned_low_byte(self):
        self.assertIn('if ( turnStarted && (ParkTurn & 3) == (guest.Id & 3) )', self.sim)
        self.assertIn('((int)guest.Toilet & 0xff) > GuestSpeed.ToiletBonusLevel', self.sim)

    def test_a_redirect_from_the_join_cell_starts_behind_the_back_cell(self):
        self.assertIn('if ( from < 0 && attraction.JoinCell == current )\n\t\t\tfrom = cells.Count;', self.sim)


@unittest.skipUnless(has_commit(SUBJECT), f'{SUBJECT} not in this repository')
class OracleFindings(unittest.TestCase):
    def test_econ_048_replaces_the_retired_046(self):
        source = show('source/OpenTPW/Economy/ParkEconomy.cs')
        self.assertIn('[APPROX:ECON-048]', source)
        self.assertNotIn('ECON-046', source)
        self.assertIn('- `ECON-046` (', show('docs/FIDELITY-REGISTER.md'))

    def test_commit_and_preview_money_tests(self):
        economy = show('source/OpenTPW/Economy/ParkEconomy.cs')
        self.assertIn('public bool CanSpendCell( CellPurchase kind ) => unchecked( (int)Balance - (int)CellCost( kind ) ) >= 0;', economy)
        preview = method_body(economy, 'public bool CanPreviewLine(')
        self.assertIn('return pending == 0 || pending <= unchecked( (int)Balance );', preview)
        builder = show('source/OpenTPW/World/ParkPathBuilder.cs')
        self.assertIn('if ( park != null && !park.CanSpendCell( CellPurchase.Path ) )', method_body(builder, 'private bool WritePath('))
        self.assertIn('CheckQueueCell( grid, null, ride, x, y, ', show('source/OpenTPW/World/Level.Objects.cs'))

    def test_bounce_takes_the_first_free_slot_of_the_header_table(self):
        bridge = show('source/OpenTPW/World/Guests/RideVisitorBridge.cs')
        body = method_body(bridge, 'internal static bool TakeBounceSlot(')
        self.assertIn('for ( var index = 0; index < capacity; index++ )', body)
        self.assertIn('if ( slots[index].Guest == 0 )', body)
        self.assertIn('TakeBounceSlot( bouncing, call.VM.Script.BounceSize, ', bridge)

    def test_bounce_start_reads_the_clock_again(self):
        """WV2-4 (closed): 0xadf40 calls the clock 0x10e844 twice, the deadline from the first call and the start from
        the second; the port takes one `now` for both. The BIN label says so and `[EXT:bounce-single-clock-read]` records it (QUEUE-020 closed)."""
        bridge = current('source/OpenTPW/World/Guests/RideVisitorBridge.cs')
        label = bridge[bridge.index('[BIN:STP-PPC:0x100ADF40 BOUNCE]'):]
        self.assertRegex(label[:label.index('\n')], r'(second|two|twice).*clock|clock.*(second|twice)')
        self.assertIn('[EXT:bounce-single-clock-read]', label[:label.index('TakeBounceSlot')])
        self.assertNotIn('("QUEUE-020", ', current('source/OpenTPW/World/Guests/QueueApproximations.cs'))


@unittest.skipUnless(has_commit(SUBJECT), f'{SUBJECT} not in this repository')
class RegisterFindings(unittest.TestCase):
    def test_prose_retired_gate_ids_are_protected(self):
        """WV2-2 (closed): the retired-ID guard read docs table rows only; it now also reads `APPROX:ID` mentions in
        docs prose, so GATE-002..GATE-005 cannot be declared again with another meaning."""
        retired = register_document()
        retired = retired[retired.index('## Retired and reserved IDs'):]
        for label in ('GATE-002', 'GATE-003', 'GATE-004', 'GATE-005'):
            self.assertIn(f'`{label}`', retired)

    def test_walk_003_is_retired_with_the_staff_on_the_chains(self):
        """WV2-3 (closed, then retired in round 10): the staff are on the cells' thing chains, so the register no longer
        declares WALK-003."""
        approx = current('source/OpenTPW/World/Guests/WalkApproximations.cs')
        self.assertNotIn('("WALK-003"', approx)
        self.assertIn('0x5102F0', approx)

    def test_oracle_seams_name_both_bounce_helpers(self):
        """WV2-5 (closed): ORACLE.md's seam list called TakeUnbounced private and left out TakeBounceSlot."""
        oracle = current('docs/reverse/ORACLE.md')
        seams = oracle[oracle.index('The C# comparisons use these seams'):]
        seams = seams[:seams.index('\n\n', seams.index('- '))]
        self.assertIn('TakeBounceSlot', seams)
        self.assertNotIn('private', seams)


@unittest.skipUnless(has_commit(SUBJECT) and has_commit(MAIN), f'{SUBJECT} or {MAIN} not in this repository')
class MergeOntoMain(unittest.TestCase):
    def test_rides_029_is_free_on_main(self):
        """WV2-1 (closed at the merge, WALK-M): fork main declared RIDES-029 for the Theme Park Inc '@' shape symbol
        (6820470), an ID APPROX-TRACE.md already uses for the retired Totem prototype; the merge renumbers it."""
        self.assertIn('("RIDES-029"', show('source/OpenTPW/World/Objects/RidesApproximations.cs', MAIN))
        self.assertIn('- `RIDES-029` (docs/reverse/APPROX-TRACE.md)', register_document())
        self.assertNotIn('("RIDES-029"', current('source/OpenTPW/World/Objects/RidesApproximations.cs'))


def _root():
    from harness import bin_root
    return bin_root()


@unittest.skipUnless(_root(), 'requires the identified Feral bin directory (OPENTPW_MAC_BIN or OPENTPW_PPC_BIN_ROOT)')
class Disassembly(unittest.TestCase):
    """Decoded fields (code-section offsets)."""

    @classmethod
    def setUpClass(cls):
        import struct
        from harness import Image
        code = Image.load(_root()).container.code.data
        cls.word = staticmethod(lambda offset: struct.unpack_from('>I', code, offset)[0])

    def d_form(self, offset):
        w = self.word(offset)
        imm = w & 0xFFFF
        return w >> 26, (w >> 21) & 31, (w >> 16) & 31, imm - 0x10000 if imm & 0x8000 else imm

    def rlwinm(self, offset):
        w = self.word(offset)
        return w >> 26, (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31, (w >> 6) & 31, (w >> 1) & 31

    def branch_target(self, offset):
        w = self.word(offset)
        self.assertEqual(18, w >> 26)
        li = w & 0x03FFFFFC
        return offset + (li - 0x04000000 if li & 0x02000000 else li)

    def test_neighbour_query_keeps_types_1_4_to_8_and_18(self):
        compares = {0x10097C: 1, 0x100984: 4, 0x10099C: 5, 0x1009B0: 6, 0x1009C4: 8, 0x1009D8: 7, 0x1009F8: 18}
        for offset, value in compares.items():
            self.assertEqual((11, 0, 0, value), self.d_form(offset), hex(offset))  # cmpwi cr0, r0, value
        self.assertEqual((34, 0, 22, 2), self.d_form(0x100978))  # lbz r0, 2(r22): the thing type

    def test_neighbour_query_stops_after_five_callbacks(self):
        self.assertEqual((11, 0, 21, 5), self.d_form(0x100A54))  # cmpwi r21, 5 after addi r21, r21, 1
        self.assertEqual((14, 21, 21, 1), self.d_form(0x100A50))

    def test_neighbour_query_distance_is_strictly_below_the_range(self):
        w = self.word(0x100A3C)  # bc 4, 0 (branch if not less): skip when d^2 >= (range >> 8)^2
        self.assertEqual((16, 4, 0), (w >> 26, (w >> 21) & 31, (w >> 16) & 31))

    def test_separation_range_is_twice_the_radius(self):
        self.assertEqual((32, 0, 30, 4), self.d_form(0xFD86C))  # lwz r0, 4(r30): the radius
        self.assertEqual((21, 0, 4, 1, 0, 30), self.rlwinm(0xFD870))  # slwi r4, r0, 1

    def test_static_initialiser_sets_one_and_zero(self):
        self.assertEqual((15, 0, 0, 1), self.d_form(0x10031C))  # lis r0, 1 = ONE
        self.assertEqual((14, 29, 0, 0), self.d_form(0x100300))  # li r29, 0
        self.assertEqual((36, 29, 3, 0), self.d_form(0x100324))  # stw r29, 0(r3 = &0xecd88)
        self.assertEqual((36, 0, 4, 0), self.d_form(0x10032C))  # stw r0, 0(r4 = &0xecd84)

    def test_avoid_walls_edges_end_the_routine(self):
        for offset in (0xFDE78, 0xFDEE4, 0xFDF64, 0xFDFE0):
            self.assertEqual(0xFE5F0, self.branch_target(offset), hex(offset))

    def test_toilet_term_compares_the_low_byte_unsigned(self):
        self.assertEqual((21, 0, 0, 0, 24, 31), self.rlwinm(0xEF0CC))  # clrlwi r0, r0, 24
        self.assertEqual((10, 0, 0, 80), self.d_form(0xEF0D0))  # cmplwi r0, 80

    def test_needs_phase_compares_the_low_two_bits(self):
        self.assertEqual((21, 0, 0, 0, 30, 31), self.rlwinm(0xEED34))  # clrlwi r0, r0, 30 (thing id)
        self.assertEqual((21, 3, 3, 0, 30, 31), self.rlwinm(0xEED38))  # clrlwi r3, r3, 30 (counter)

    def test_bounce_scans_for_the_first_free_slot(self):
        self.assertEqual((32, 0, 3, 100), self.d_form(0xADF6C))  # lwz r0, 100(r3): slot count into ctr
        self.assertEqual((11, 0, 0, 0), self.d_form(0xADF88))  # cmpwi r0, 0: visitor word
        w = self.word(0xADF8C)  # bc 4, 2: next slot when the visitor is not 0
        self.assertEqual((16, 4, 2), (w >> 26, (w >> 21) & 31, (w >> 16) & 31))
        # The deadline and the start each come from their own call of the clock 0x10e844 (WV2-4).
        self.assertEqual(0x10E844, 0xADFB0 + (self.word(0xADFB0) & 0x03FFFFFC))
        self.assertEqual(0x10E844, 0xADFCC + (self.word(0xADFCC) & 0x03FFFFFC))


@unittest.skipUnless(_root(), 'requires the identified Feral bin directory (OPENTPW_MAC_BIN or OPENTPW_PPC_BIN_ROOT)')
class SeparationCallbackOracle(unittest.TestCase):
    """0xfd5f4 (+0xfcb8c, 0xfd704) in OpenTPW's PPC interpreter against separation_push, FixDiv stubbed."""

    def test_callback_matches_the_port(self):
        from harness import Image, Sandbox
        image = Image.load(_root())

        def fixdiv_stub(m):
            m.gpr[3] = fixdiv(s32(m.gpr[3]), s32(m.gpr[4])) & MASK32
        sb = Sandbox(image, [(0xFD5F4, 0xFD778), (0xFCB8C, 0xFCBB0), (0x1C73D4, 0x1C73EC)], {'FixDiv': fixdiv_stub})
        rng = random.Random('walk-v2-separation')
        base = (5 * ONE, 5 * ONE)
        cases = [(base, base), (base, (base[0] + 1, base[1])), (base, (base[0] + 2 * RADIUS + ONE, base[1])),
                 (base, (base[0] + 2 * RADIUS + ONE - 1, base[1])), (base, (base[0] + 26213, base[1] + 26213))]
        for _ in range(300):
            sx, sy = rng.randrange(0, 128 * ONE), rng.randrange(0, 128 * ONE)
            span = rng.choice([30000, 100000, 3 * ONE])
            cases.append(((sx, sy), (sx + rng.randrange(-span, span), sy + rng.randrange(-span, span))))
        mismatches, pushes = [], 0
        for self_pos, other_pos in cases:
            sb.reset()
            sb.write(sb.data(0xECD84), 'i', ONE)  # the static initialiser's values (0x1002f4)
            sb.write(sb.data(0xECD88), 'i', 0)
            sb.write(sb.data(0xECD58), 'i', 0)
            sb.write(sb.data(0xECD5C), 'i', 0)
            me = sb.struct(64, {4: ('i', RADIUS), 8: ('i', self_pos[0]), 12: ('i', self_pos[1])})
            other = sb.struct(64, {4: ('i', RADIUS), 8: ('i', other_pos[0]), 12: ('i', other_pos[1])})
            sb.call(0xFD5F4, {3: me, 4: other})
            got = (sb.read(sb.data(0xECD58), 'i'), sb.read(sb.data(0xECD5C), 'i'))
            want = separation_push(self_pos, other_pos)
            pushes += want != (0, 0)
            if got != want:
                mismatches.append((self_pos, other_pos, got, want))
        self.assertEqual([], mismatches[:3])
        self.assertGreater(pushes, 100)


if __name__ == '__main__':
    unittest.main()
