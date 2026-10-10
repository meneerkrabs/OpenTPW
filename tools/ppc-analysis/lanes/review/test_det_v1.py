"""Review DET-V: research 471e4bb (DET-plan, lanes/det) and implementation d1813eb (world seed, state hash).

- With OPENTPW_PPC_BIN_ROOT naming the identified Feral Mac bin directory, four DET-plan claims are decoded
  from the instruction words with this file's own field split, without det_evidence.py:
  the world LCG 0x105328 (1664525/1013904223 at world+0x1da708, wrapping abs) is executed by a small
  interpreter; the RSE RAND call site 0xb0800 projects labs(labs(r >>> 1) % (int16(bound) + 1)); the
  catch-up loop adds 31 ms, masks the substep with 7 for the park turn and skips the turn once the
  per-callback count reaches 3 (unsigned); and setter call 0xe79b8 reseeds the world RNG with the u16
  at thing+0. The two bounded scans the plan relies on (142 direct generator calls, 8 setter callers)
  are recounted. The kart, particle and weather LCG increments, which DET-plan labels pinned but
  det_evidence.py does not pin, are pinned here.
- With the same variable, each of the lane's eight mutation tests is shown to fail at the mutated site
  and not on an unrelated check.
- Always: the RAND reference vector (seed 0, bound 10 -> 6, 8) is recomputed from first principles, and
  the C# source guard of d1813eb (read from this checkout when present) is pinned with its known gaps.

Nothing is executed from the original. Nothing here says anything about the PC build.
"""
from __future__ import annotations

import copy
import hashlib
import os
import re
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef  # noqa: E402

APP_SHA256 = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
BIN_ROOT = os.environ.get('OPENTPW_PPC_BIN_ROOT')
REPO = Path(__file__).resolve().parents[4]
DET_LANE = Path(__file__).resolve().parents[1] / 'det'
MASK32 = 0xFFFFFFFF


def s16(value: int) -> int:
    return value - 0x10000 if value & 0x8000 else value


def s32(value: int) -> int:
    value &= MASK32
    return value - 0x100000000 if value & 0x80000000 else value


def d_form(word: int) -> tuple[int, int, int, int]:
    """(primary opcode, rD/rS/BO/crfD, rA/BI, signed 16-bit immediate)."""
    return word >> 26, (word >> 21) & 31, (word >> 16) & 31, s16(word & 0xFFFF)


def x_form(word: int) -> tuple[int, int, int, int, int]:
    """(primary opcode, rD/rS, rA, rB, extended opcode)."""
    return word >> 26, (word >> 21) & 31, (word >> 16) & 31, (word >> 11) & 31, (word >> 1) & 0x3FF


def m_form(word: int) -> tuple[int, int, int, int, int, int, int]:
    """rlwinm: (opcode, rS, rA, SH, MB, ME, Rc)."""
    return word >> 26, (word >> 21) & 31, (word >> 16) & 31, (word >> 11) & 31, (word >> 6) & 31, (word >> 1) & 31, word & 1


def linked_target(address: int, word: int) -> int | None:
    if word >> 26 != 18 or word & 3 != 1:
        return None
    offset = word & 0x03FFFFFC
    if offset & 0x02000000:
        offset -= 0x04000000
    return address + offset


def conditional(address: int, word: int) -> tuple[int, int, int]:
    """bc: (BO, BI, target); AA and LK must be clear."""
    if word >> 26 != 16 or word & 3:
        raise AssertionError(f'{address:#x} is not a plain bc')
    return (word >> 21) & 31, (word >> 16) & 31, address + s16(word & 0xFFFC)


def lcg(state: int, multiplier: int = 1664525, increment: int = 1013904223) -> int:
    return (state * multiplier + increment) & MASK32


def rand_projection(raw: int, bound: int) -> int:
    """RSE RAND as decoded at 0xb0800: labs(labs(r >>> 1) % (int16(bound) + 1)), C remainder truncates."""
    value = abs(s32(raw >> 1))
    divisor = s16(bound & 0xFFFF) + 1
    quotient = int(value / divisor)
    return abs(s32(value - quotient * divisor))


class ReferenceVectors(unittest.TestCase):
    def test_rand_seed_zero_bound_ten(self):
        state, draws = 0, []
        for _ in range(2):
            state = lcg(state)
            raw = state if s32(state) >= 0 else (-state) & MASK32  # the generator's wrapping abs
            draws.append(rand_projection(raw, 10))
        self.assertEqual(draws, [6, 8], 'DET-plan §4.3 vector')

    def test_wrapping_abs_keeps_int_min(self):
        self.assertEqual((-0x80000000) & MASK32, 0x80000000)


@unittest.skipUnless(BIN_ROOT, 'requires identified Feral Mac PEFs (OPENTPW_PPC_BIN_ROOT)')
class IndependentDecode(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = Path(BIN_ROOT) / 'SimThemePark.data'
        if hashlib.sha256(path.read_bytes()).hexdigest() != APP_SHA256:
            raise unittest.SkipTest('SimThemePark.data is not the identified Feral build')
        cls.app = pef.load(str(path))
        cls.code = bytes(cls.app.code.data)

    def word(self, address: int) -> int:
        return struct.unpack_from('>I', self.code, address)[0]

    def calls_to(self, target: int) -> list[int]:
        return [address for address in range(0, len(self.code), 4)
                if linked_target(address, self.word(address)) == target]

    def run_generator(self, state: int) -> tuple[int, int]:
        """Interprets 0x105328..0x10535c on a world whose RNG field holds state; returns (r3, stored field)."""
        regs, memory, world = [0] * 32, {}, 0x01000000
        regs[3] = world
        pc = 0x105328
        memory[(world + 0x1DA708) & MASK32] = state
        less = False
        for _ in range(32):
            w = self.word(pc)
            op, rd, ra, imm = d_form(w)
            if op == 15:  # addis / lis
                regs[rd] = ((regs[ra] if ra else 0) + (imm << 16)) & MASK32
            elif op == 14:  # addi / li
                regs[rd] = ((regs[ra] if ra else 0) + imm) & MASK32
            elif op == 32:  # lwz
                regs[rd] = memory[(regs[ra] + imm) & MASK32]
            elif op == 36:  # stw
                memory[(regs[ra] + imm) & MASK32] = regs[rd]
            elif op == 11:  # cmpwi
                self.assertEqual((rd, imm), (0, 0), 'cmpwi cr0, rX, 0')
                less = s32(regs[ra]) < 0
            elif op == 19 and w == 0x4C800020:  # bclr 4,0: return when not less
                if not less:
                    return regs[3], memory[(world + 0x1DA708) & MASK32]
            elif op == 31 and x_form(w)[4] == 235:  # mullw
                regs[rd] = (regs[ra] * regs[x_form(w)[3]]) & MASK32
            elif op == 31 and x_form(w)[4] == 104:  # neg
                regs[rd] = (-regs[ra]) & MASK32
            elif w == 0x4E800020:  # blr
                return regs[3], memory[(world + 0x1DA708) & MASK32]
            else:
                self.fail(f'unexpected instruction {w:#010x} at {pc:#x}')
            pc += 4
        self.fail('generator did not return')

    def test_world_lcg_constants_field_and_abs(self):
        for state in (0, 1, 0x7FFFFFFF, 0xDEADBEEF, 0x80000000, 0x2C5AA9F4):
            successor = lcg(state)
            returned, stored = self.run_generator(state)
            self.assertEqual(stored, successor, f'state {state:#x}')
            expected = successor if s32(successor) >= 0 else (-successor) & MASK32
            self.assertEqual(returned, expected, f'state {state:#x}')
        # A successor of exactly 0x80000000 comes back unchanged (wrapping abs).
        preimage = ((0x80000000 - 1013904223) * pow(1664525, -1, 1 << 32)) & MASK32
        self.assertEqual(self.run_generator(preimage), (0x80000000, 0x80000000))
        # Setter 0x105360: addis r3,r3,30; stw r4,-0x58f8(r3); blr — the same field.
        self.assertEqual(d_form(self.word(0x105360)), (15, 3, 3, 30))
        self.assertEqual(d_form(self.word(0x105364)), (36, 4, 3, -0x58F8))
        self.assertEqual(self.word(0x105368), 0x4E800020)

    def test_rand_call_site_projection(self):
        self.assertEqual(linked_target(0xB0800, self.word(0xB0800)), 0x105328)
        self.assertEqual(m_form(self.word(0xB0804)), (21, 3, 3, 31, 1, 31, 0), 'srwi r3,r3,1')
        labs = linked_target(0xB0808, self.word(0xB0808))
        self.assertEqual(linked_target(0xB0820, self.word(0xB0820)), labs, 'second call is the same labs glue')
        self.assertEqual(x_form(self.word(0xB07F8))[:3] + (x_form(self.word(0xB07F8))[4],), (31, 3, 28, 922), 'extsh r28,r3 (bound)')
        self.assertEqual(d_form(self.word(0xB0810)), (14, 4, 28, 1), 'addi r4,r28,1')
        self.assertEqual(x_form(self.word(0xB0814)), (31, 0, 3, 4, 491), 'divw r0,r3,r4')
        self.assertEqual(x_form(self.word(0xB0818)), (31, 0, 0, 4, 235), 'mullw r0,r0,r4')
        self.assertEqual(x_form(self.word(0xB081C)), (31, 3, 0, 3, 40), 'subf r3,r0,r3')
        self.assertEqual(d_form(self.word(0xB082C)), (36, 3, 31, 72), 'result stored')

    def test_substep_31_ms_eighth_mask_and_cap_3(self):
        # previous += 31 and substep += 1 before the world gate.
        self.assertEqual(d_form(self.word(0x1C22DC)), (32, 3, 28, 0))
        self.assertEqual(d_form(self.word(0x1C22E0)), (14, 0, 3, 31))
        self.assertEqual(d_form(self.word(0x1C22E4)), (36, 0, 28, 0))
        self.assertEqual(d_form(self.word(0x1C22EC)), (14, 0, 3, 1))
        self.assertEqual(d_form(self.word(0x1C22F0)), (36, 0, 26, 0))
        # substep & 7: rlwinm. r0,r0,0,29,31 then branch to the eighth-only tail when non-zero.
        self.assertEqual(d_form(self.word(0x1C2338)), (32, 0, 26, 0))
        self.assertEqual(m_form(self.word(0x1C233C)), (21, 0, 0, 0, 29, 31, 1))
        self.assertEqual(conditional(0x1C2340, self.word(0x1C2340)), (4, 2, 0x1C2428))
        # Cap: cmplwi r3,3 (unsigned, cr0) on the park-work count; not-less skips to the 0xb5da4 call.
        self.assertEqual(d_form(self.word(0x1C2350)), (32, 3, 18, 0))
        self.assertEqual(d_form(self.word(0x1C2354)), (10, 0, 3, 3))
        self.assertEqual(conditional(0x1C2358, self.word(0x1C2358)), (4, 0, 0x1C2424))
        self.assertEqual(d_form(self.word(0x1C235C)), (14, 0, 3, 1))
        self.assertEqual(d_form(self.word(0x1C2360)), (36, 0, 18, 0))
        self.assertEqual(linked_target(0x1C2424, self.word(0x1C2424)), 0xB5DA4)
        self.assertEqual(linked_target(0x1C23B8, self.word(0x1C23B8)), 0x10536C)
        # Backlog 2000 and the signed repeat while now > previous.
        self.assertEqual(d_form(self.word(0x1C22C8)), (11, 0, 0, 2000))
        self.assertEqual(d_form(self.word(0x1C22D0)), (14, 0, 3, -2000))
        self.assertEqual(x_form(self.word(0x1C24C8))[:4], (31, 0, 3, 0), 'cmpw cr0,r3,r0')
        self.assertEqual(x_form(self.word(0x1C24C8))[4], 0)
        self.assertEqual(conditional(0x1C24CC, self.word(0x1C24CC)), (12, 1, 0x1C22DC))

    def test_reseed_to_thing_id_at_guest_creation(self):
        # r3 = sp+200, r4 = thing (r29); 0xfa9a4 copies the halfword at thing+0 to sp+200.
        self.assertEqual(d_form(self.word(0xE7964)), (14, 3, 1, 200))
        self.assertEqual(d_form(self.word(0xE7968)), (14, 4, 29, 0))
        for address in range(0xE796C, 0xE79A4, 4):  # nothing in between writes r3 or r4
            op, rd, _, _ = d_form(self.word(address))
            self.assertFalse(op in (14, 15, 32, 40, 34) and rd in (3, 4), f'{address:#x}')
        self.assertEqual(linked_target(0xE79A4, self.word(0xE79A4)), 0xFA9A4)
        self.assertEqual([d_form(self.word(a)) for a in (0xFA9A4, 0xFA9A8)], [(40, 0, 4, 0), (44, 0, 3, 0)])
        self.assertEqual(self.word(0xFA9AC), 0x4E800020)
        # lhz r0,200(sp); sth r0,172(sp); lhz r4,172(sp) (zero-extended u16); r3 = world; bl setter.
        self.assertEqual([d_form(self.word(a)) for a in (0xE79A8, 0xE79AC, 0xE79B0, 0xE79B4)],
                         [(40, 0, 1, 200), (44, 0, 1, 172), (40, 4, 1, 172), (32, 3, 30, 0)])
        self.assertEqual(linked_target(0xE79B8, self.word(0xE79B8)), 0x105360)

    def test_bounded_scans_recounted(self):
        self.assertEqual(len(self.calls_to(0x105328)), 142)
        self.assertEqual(self.calls_to(0x105360), [0xD5B14, 0xD86DC, 0xE79B8, 0xE8FD4, 0xEB160, 0xEB28C, 0xEFA30, 0x1A9574])

    def test_other_lcg_increments_not_pinned_by_the_lane(self):
        # (lis/addi multiplier, addis/addi increment) word pairs for kart, particle and weather.
        for name, mul, inc, multiplier, increment in (
                ('kart', (0x2089C, 0x208A4), (0x208AC, 0x208B0), 214013, 2531011),
                ('particle', (0x9D4A4, 0x9D4AC), (0x9D4B4, 0x9D4B8), 214013, 2531011),
                ('weather', (0x904A8, 0x904B0), (0x904B8, 0x904BC), 1664525, 1013904223)):
            hi, lo = d_form(self.word(mul[0])), d_form(self.word(mul[1]))
            self.assertEqual((hi[0], hi[2], lo[0]), (15, 0, 14), name)
            self.assertEqual(((hi[3] << 16) + lo[3]) & MASK32, multiplier, name)
            hi, lo = d_form(self.word(inc[0])), d_form(self.word(inc[1]))
            self.assertEqual((hi[0], lo[0]), (15, 14), name)
            self.assertEqual(((hi[3] << 16) + lo[3]) & MASK32, increment, name)


@unittest.skipUnless(BIN_ROOT, 'requires identified Feral Mac PEFs (OPENTPW_PPC_BIN_ROOT)')
class LaneMutationsBite(unittest.TestCase):
    """Each lane mutation must fail at the mutated site, reporting the binary's true value."""

    @classmethod
    def setUpClass(cls):
        if not (DET_LANE / 'det_evidence.py').exists():
            raise unittest.SkipTest('lanes/det not in this checkout')
        sys.path.insert(0, str(DET_LANE))
        import det_evidence  # noqa: E402
        cls.ev = det_evidence

    def bite(self, name, mutate, expected_message):
        saved = copy.deepcopy(getattr(self.ev, name))
        try:
            mutate(getattr(self.ev, name))
            with self.assertRaises(self.ev.pef.PEFError) as caught:
                self.ev.inspect(Path(BIN_ROOT))
            self.assertIn(expected_message, str(caught.exception))
        finally:
            setattr(self.ev, name, saved)

    def test_all_eight(self):
        def swap(calls):
            calls[0], calls[1] = (calls[0][0], calls[0][1], calls[1][2], calls[0][3]), calls[1]
        cases = [
            ('SUBSTEP', lambda t: t.__setitem__('slice_ms', (0x1C22E0, 30)), 'code:0x1c22e0: unexpected interpreted value (0, 3, 31)'),
            ('SUBSTEP', lambda t: t.__setitem__('park_cap', (0x1C2354, 4)), 'code:0x1c2354: unexpected interpreted value (0, 3, 3)'),
            ('CADENCE_MASKS', lambda t: t.__setitem__('eighth', (0x1C233C, 30)), 'eighth substep mask'),
            ('INTERPOLATION_DIVISORS', lambda t: t.__setitem__(0x1C25F8, (-0x26B0, 250.0)), 'code:0x1c25f8: unexpected interpreted value 248.0'),
            ('WORLD_RNG', lambda t: t.__setitem__('increment', 1013904224), 'world RNG increment: unexpected interpreted value 1013904223'),
            ('RESEED_FROM_THING_ID', lambda t: t.__setitem__(0, (0xD86E0, None)), 'code:0xd86e0'),
            ('SUBSTEP_CALLS', swap, 'call at code:0x1c230c'),
            ('EXPECTED_SCANS', lambda t: t.__setitem__('world_rng_direct_calls', 141), 'world_rng_direct_calls: unexpected interpreted value 142'),
        ]
        for name, mutate, message in cases:
            with self.subTest(name=name, message=message):
                self.bite(name, mutate, message)


class CSharpSourceGuard(unittest.TestCase):
    """Pins what SimulationCodeUsesNoProcessWideRandomnessOrClock catches (d1813eb, extended for finding F3)."""

    @classmethod
    def setUpClass(cls):
        test = REPO / 'source' / 'OpenTPW.Tests' / 'DeterminismTests.cs'
        if not test.exists():
            raise unittest.SkipTest('DeterminismTests.cs not in this checkout')
        text = test.read_text(encoding='utf-8-sig')
        block = re.search(r'ForbiddenInSimulation = new\((.*?)RegexOptions', text, re.S)
        folders = re.search(r'var folders = new\[\] \{ (.*?) \};', text)
        presentation = re.search(r'PresentationWorldFiles =\s*\{(.*?)\};', text, re.S)
        if not block or not folders or not presentation:
            raise unittest.SkipTest('guard shape changed; re-review it')
        parts = re.findall(r'@"((?:[^"]|"")*)"', block.group(1))
        cls.forbidden = re.compile(''.join(part.replace('""', '"') for part in parts))
        cls.folders = folders.group(1)
        cls.presentation = re.findall(r'"([^"]+)"', presentation.group(1))

    def test_catches_the_reintroduced_sources(self):
        for line in ('private readonly Random random = new Random();', 'var r = new System.Random ( );',
                     'Random.Shared.Next()', 'private static int nextAttractionId = 1;',
                     'var t = Environment.TickCount;', 'DateTime.UtcNow', 'Stopwatch.StartNew()'):
            self.assertTrue(self.forbidden.search(line), line)

    def test_former_gaps_are_caught(self):
        # Review finding F3: these forms were missed by d1813eb's pattern.
        for line in ('private readonly Random random = new();', 'private static long nextId;',
                     'private static int counter;', 'var h = name.GetHashCode();'):
            self.assertTrue(self.forbidden.search(line), line)
        for line in ('private static readonly int Limit = 3;', 'private const int Limit = 3;',
                     'private static bool logged;', 'public override int GetHashCode() => Id;'):
            self.assertIsNone(self.forbidden.search(line), line)

    def test_remaining_gaps(self):
        # Not forbidden: Guid (temp file names in ParkSaveFile) and Parallel (no simulation use found).
        for line in ('Guid.NewGuid()', 'Parallel.For( 0, n, i => { } );'):
            self.assertIsNone(self.forbidden.search(line), line)

    def test_scope(self):
        self.assertEqual(self.folders, '"VM", "Economy", Path.Combine( "World", "Guests" ), Path.Combine( "World", "Objects" )')
        # Top-level World files are scanned unless listed as presentation; Level*.cs partials are scanned.
        for name in ('Level.cs', 'Level.Objects.cs', 'Ride.cs', 'PrototypeRide.cs', 'FixedStepClock.cs'):
            self.assertNotIn(name, self.presentation)


if __name__ == '__main__':
    unittest.main()
