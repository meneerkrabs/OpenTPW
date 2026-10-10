"""Determinism witness tests: synthetic helper checks always run; native checks need the Feral PEFs."""
import copy
import os
import struct
import unittest
from pathlib import Path
from types import SimpleNamespace

import det_evidence as ev

BIN_ROOT = os.environ.get('OPENTPW_PPC_BIN_ROOT')


def container(words):
    return SimpleNamespace(code=SimpleNamespace(data=b''.join(struct.pack('>I', w) for w in words)), name='synthetic')


def d_form(opcode, rd, ra, immediate):
    return opcode << 26 | rd << 21 | ra << 16 | (immediate & 0xffff)


def rlwinm(rs, ra, sh, mb, me, rc):
    return 21 << 26 | rs << 21 | ra << 16 | sh << 11 | mb << 6 | me << 1 | rc


def bl(offset, target):
    return 18 << 26 | ((target - offset) & 0x03fffffc) | 1


class SyntheticHelperTests(unittest.TestCase):
    def test_lcg_multiplier_scan_handles_negative_low_half_and_window(self):
        words = [d_form(15, 3, 0, 0x19), 0, 0, d_form(14, 0, 3, 0x660d),       # lis/addi -> 1664525
                 d_form(15, 4, 0, 0x41c7), d_form(24, 4, 4, 0x4e6d),            # wrong ori pairing
                 d_form(15, 5, 0, 3), d_form(14, 31, 5, 0x43fd)]                # 214013 into another rD
        sample = container(words)
        self.assertEqual(ev.lcg_multiplier_sites(sample, 1664525), [0])
        self.assertEqual(ev.lcg_multiplier_sites(sample, 214013), [24])
        self.assertEqual(ev.lcg_multiplier_sites(sample, 1103515245), [])
        self.assertEqual(ev.lcg_multiplier_sites(container([d_form(15, 3, 0, 0x19)] + [0] * 14 + [d_form(14, 0, 3, 0x660d)]),
                                                 1664525), [])

    def test_displacement_scan_excludes_toc_base(self):
        sample = container([d_form(36, 3, 5, -0x58f8), d_form(36, 3, 2, -0x58f8), d_form(32, 3, 5, -0x58f8)])
        self.assertEqual(ev.displacement_sites(sample, -0x58f8, 36), ([0], [4]))

    def test_direct_call_scan_ignores_unlinked_branches(self):
        sample = container([bl(0, 12), 18 << 26 | 8, 0, 0])
        self.assertEqual(ev.direct_calls(sample), {12: [0]})

    def test_mask_and_branch_decoders_reject_wrong_fields(self):
        sample = container([rlwinm(0, 0, 0, 29, 31, 1), 16 << 26 | 4 << 21 | 2 << 16 | 0x10])
        self.assertEqual(ev.rlwinm(sample, 0), (0, 0, 0, 29, 31, 1))
        self.assertNotEqual(ev.rlwinm(sample, 0), (0, 0, 0, 31, 31, 1))  # even mask is not the eighth mask
        self.assertEqual(ev.branch(sample, 4), (4, 2, 0x14))
        with self.assertRaises(ev.pef.PEFError):
            ev.rlwinm(sample, 4)


@unittest.skipUnless(BIN_ROOT, 'requires identified Feral Mac PEFs (OPENTPW_PPC_BIN_ROOT)')
class NativeWitnessTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.result = ev.inspect(Path(BIN_ROOT))

    def test_scheduler_contract(self):
        scheduler = self.result['scheduler']
        self.assertEqual((scheduler['slice_ms_of_scaled_clock'], scheduler['park_turn_every_substeps'],
                          scheduler['park_turns_per_callback_cap'], scheduler['backlog_limit_ms']), (31, 8, 3, 2000))
        self.assertEqual(scheduler['cadence_masks'], {'even': 1, 'eighth': 3, 'thirty-second': 5})
        self.assertEqual([step['target'] for step in scheduler['substep_order']][:3], [0x9f748, 0x20688, 0xb2838])

    def test_render_is_once_per_callback_after_catch_up(self):
        frame = self.result['frame']
        self.assertEqual(frame['interpolation_divisors_ms'], [31.0, 62.0, 248.0])
        self.assertEqual(frame['render_imports'], ['RenderSystem_FlipScreen__Fv', 'Render__7CMapWhoFv'])
        self.assertIn(0x3864c, [call['target'] for call in frame['post_loop']])
        self.assertEqual(frame['forced_step']['interval_ms'], 31)

    def test_rng_ownership(self):
        world = self.result['world_rng']
        self.assertTrue(world['saved'] and world['tick_saved'])
        self.assertEqual(world['direct_call_sites'], 142)
        self.assertEqual(len(world['reseed_from_thing_id']), 7)
        self.assertEqual(set(self.result['own_state_generators']),
                         {'coaster_boarding', 'kart_object', 'particle_object', 'weather_object'})
        self.assertEqual(self.result['bounded_scans']['seed_label_count'], 1)

    def mutated(self, name, mutate):
        saved = copy.deepcopy(getattr(ev, name))
        try:
            mutate(getattr(ev, name))
            with self.assertRaises(ev.pef.PEFError):
                ev.inspect(Path(BIN_ROOT))
        finally:
            setattr(ev, name, saved)

    def test_mutation_slice_length(self):
        self.mutated('SUBSTEP', lambda table: table.__setitem__('slice_ms', (0x1c22e0, 30)))

    def test_mutation_park_cap(self):
        self.mutated('SUBSTEP', lambda table: table.__setitem__('park_cap', (0x1c2354, 4)))

    def test_mutation_turn_cadence_mask(self):
        self.mutated('CADENCE_MASKS', lambda table: table.__setitem__('eighth', (0x1c233c, 30)))

    def test_mutation_turn_interpolation_divisor(self):
        self.mutated('INTERPOLATION_DIVISORS', lambda table: table.__setitem__(0x1c25f8, (-0x26b0, 250.0)))

    def test_mutation_world_rng_increment(self):
        self.mutated('WORLD_RNG', lambda table: table.__setitem__('increment', 1013904224))

    def test_mutation_reseed_site(self):
        self.mutated('RESEED_FROM_THING_ID', lambda table: table.__setitem__(0, (0xd86e0, None)))

    def test_mutation_substep_order(self):
        def swap(calls):
            calls[0], calls[1] = (calls[0][0], calls[0][1], calls[1][2], calls[0][3]), calls[1]
        self.mutated('SUBSTEP_CALLS', swap)

    def test_mutation_bounded_scan_count(self):
        self.mutated('EXPECTED_SCANS', lambda table: table.__setitem__('world_rng_direct_calls', 141))


if __name__ == '__main__':
    unittest.main()
