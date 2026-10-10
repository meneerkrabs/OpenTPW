"""QUEUE-V round 2: the QUEUE-FIX stack (025d410 merge, bb45896 fixes, 952ab0f DET-V2 witnesses) on main 5ec2622.

- Binary (OPENTPW_MAC_BIN = the Feral bin directory, SimThemePark.data identified by SHA-256): ride +408 is the
  object state of CObject::SetState 0xe0c3c; the ride update 0xe0a8c runs 0xe1864 (admission 0xe1404 first) only
  in state 0, and 0xe1864 then reads VAR_BROKEN (var 7) and moves the ride to BROKEN_DOWN (1) or CONDEMNED (4).
  This is the decode behind APPROX:QUEUE-019. Uses test_queue_v1's PEF reader and field decoder.
- Source (this checkout): every guest queue field is in the canonical hash, the vacuous Stalled signal is gone,
  the breakdown guards carry their register IDs, and the merge resolution keeps both parents' statements.
- Git (OPENTPW_REVIEW_REPO, defaults to this repository; skipped when a commit is missing): 025d410 leaves
  M3Gate.cs exactly as on main, and the stack fast-forwards from 5ec2622.

Bounded: static reading only, nothing original is executed. The behaviour runs (invariants, breakdown,
removal, stuck head, mutations) are scratch harnesses described in docs/reverse/REVIEW-QUEUE.md, Round 2.
"""
from __future__ import annotations

import hashlib
import os
import re
import unittest
from pathlib import Path

from test_queue_v1 import DATA_SHA256, Code, git, load_sections, show

REPO = Path(__file__).resolve().parents[4]
GUESTS = REPO / 'source' / 'OpenTPW' / 'World' / 'Guests'
MAIN, MERGE, FIX = '5ec2622', '025d410', '952ab0f'
STRING_BASE = 0x1ceb50  # code-section address the TOC slot -19640 holds in 0xe0a8c, 0xe0c3c and 0xe1864
SET_STATE = 0xe0c3c
# Every bl to CObject::SetState in the code section, with the state its caller loads into r4.
SET_STATE_CALLS = {0xdad24: 0, 0xdad34: 3, 0xde8e4: 0, 0xdf4d4: 0, 0xdf90c: 0, 0xdf990: 2, 0xdfb08: 0,
                   0xe1908: 1, 0xe1954: 4}


@unittest.skipUnless(os.environ.get('OPENTPW_MAC_BIN'), 'OPENTPW_MAC_BIN not set to the Feral bin directory')
class ObjectState(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        raw = (Path(os.environ['OPENTPW_MAC_BIN']) / 'SimThemePark.data').read_bytes()
        if hashlib.sha256(raw).hexdigest() != DATA_SHA256:
            raise unittest.SkipTest('SimThemePark.data is not the identified Feral build')
        cls.c = Code(*load_sections(raw))

    def string(self, offset: int) -> str:
        at = STRING_BASE + offset
        return self.c.code[at:self.c.code.index(b'\0', at)].decode('ascii')

    def test_ride_update_admits_only_in_state_0(self):
        c = self.c
        self.assertEqual((32, 31, 2, -19640), c.d_form(0xe0a94))  # r31 = string base
        self.assertEqual(0x7c7d1b78, c.word(0xe0aa4))  # mr r29, r3 (the ride)
        self.assertEqual((32, 0, 3, 408), c.d_form(0xe0ab4))  # lwz r0, +408
        self.assertEqual((11, 0, 0, 3), c.d_form(0xe0ab8))
        self.assertEqual((12, 2, 0xe0c1c), c.cond_branch(0xe0abc))  # 3: return
        self.assertEqual((4, 0, 0xe0ad4), c.cond_branch(0xe0ac0))  # >= 3 (4, 5+)
        self.assertEqual((11, 0, 0, 0), c.d_form(0xe0ac4))
        self.assertEqual((12, 2, 0xe0ae0), c.cond_branch(0xe0ac8))  # 0: admission
        self.assertEqual((4, 0, 0xe0aec), c.cond_branch(0xe0acc))  # 1, 2: the state-14 guest walk
        self.assertEqual((11, 0, 0, 5), c.d_form(0xe0ad4))
        self.assertEqual((4, 0, 0xe0c10), c.cond_branch(0xe0ad8))  # >= 5: assertion
        self.assertEqual(0xe1864, c.call(0xe0ae4))
        self.assertEqual((14, 4, 31, 2917), c.d_form(0xe0c14))
        self.assertEqual('Unknown state in CObject::ModelState\n', self.string(2917))

    def test_set_state_stores_408(self):
        c = self.c
        self.assertEqual(0x7c9c2378, c.word(0xe0c44))  # mr r28, r4 (new state)
        self.assertEqual(0x7c7f1b78, c.word(0xe0c58))  # mr r31, r3 (object)
        self.assertEqual((32, 30, 2, -19640), c.d_form(0xe0c5c))
        self.assertEqual((36, 28, 31, 408), c.d_form(0xe1308))  # stw r28, +408
        self.assertEqual('Unknown state in CObject::SetState\n', self.string(2985))
        found = {}
        for at in range(0, len(c.code), 4):
            w = c.word(at)
            if w >> 26 == 18 and w & 3 == 1 and c.call(at) == SET_STATE:
                li = c.d_form(at - 4)
                self.assertEqual((14, 4, 0), li[:3], f'{at:#x}')
                found[at] = li[3]
        self.assertEqual(SET_STATE_CALLS, found)

    def test_breakdown_sets_a_nonzero_state_after_admission(self):
        c = self.c
        self.assertEqual(0xe1404, c.call(0xe1890))  # admission runs first, on every state-0 turn
        self.assertEqual(0xb5758, c.call(0xe1898))
        self.assertEqual((14, 4, 0, 7), c.d_form(0xe189c))  # VAR_BROKEN
        self.assertEqual(0xb5be0, c.call(0xe18a0))
        self.assertEqual((12, 2, 0xe195c), c.cond_branch(0xe18a8))  # VAR_BROKEN == 0: no state change
        self.assertEqual((48, 0, 29, 68), c.d_form(0xe18ac))  # byte(+68) picks the state
        self.assertEqual((14, 4, 31, 3234), c.d_form(0xe18d4))
        self.assertEqual('Object %d: Setting state BROKEN_DOWN\n', self.string(3234))
        self.assertEqual(((14, 4, 0, 1), SET_STATE), (c.d_form(0xe1904), c.call(0xe1908)))
        self.assertEqual((14, 4, 31, 3272), c.d_form(0xe1920))
        self.assertEqual('Object %d: Setting state CONDEMNED\n', self.string(3272))
        self.assertEqual(((14, 4, 0, 4), SET_STATE), (c.d_form(0xe1950), c.call(0xe1954)))


def read(path: Path) -> str:
    return path.read_text(encoding='utf-8')


class Source(unittest.TestCase):
    def test_every_guest_queue_field_is_hashed(self):
        guest = read(GUESTS / 'Guest.cs')
        simulation = read(GUESTS / 'GuestSimulation.cs')
        canonical = simulation[simulation.index('internal void AddCanonicalState'):]
        canonical = canonical[:canonical.index('\n\t}\n')]
        fields = re.findall(r'public \w+ (\w*(?:Queue|Interlude)\w*) \{ get; internal set; \}', guest)
        fields += re.findall(r'^\tinternal \w+ (\w*Queue\w*)\s*[=;]', guest, re.M)
        self.assertEqual(14, len(fields), fields)
        for field in fields:
            self.assertIn(f'hash.Add( guest.{field} );', canonical)
        self.assertIn('hash.Add( seenGridVersion );', canonical)
        self.assertIn('seenQueueEdits.TryGetValue( attraction.AttractionId', canonical)

    def test_progress_counters_replace_stalled(self):
        bridge = read(GUESTS / 'RideVisitorBridge.cs')
        visitor = read(GUESTS / 'IRideVisitorBridge.cs')
        self.assertNotRegex(bridge + visitor, r'\bStalled\b')
        self.assertIn('public bool HeadNotReady => ConditionsHold && HeadGuest != 0 && !HeadAtFront;', visitor)
        canonical = bridge[bridge.index('internal void AddCanonicalState'):bridge.index('// ---- Queue geometry')]
        for name in ('calledTurn', 'HeadNotReadyStreak', 'MaximumHeadNotReadyStreak', 'CalledAgeTurns', 'MaximumCalledAgeTurns'):
            self.assertIn(f'hash.Add( {name} );', canonical)

    def test_breakdown_guards_are_registered(self):
        sources = read(GUESTS / 'GuestSimulation.cs') + read(GUESTS / 'RideVisitorBridge.cs')
        register = read(GUESTS / 'QueueApproximations.cs')
        for approx in ('QUEUE-017', 'QUEUE-018', 'QUEUE-019'):
            self.assertIn(f'[APPROX:{approx}]', sources)
            self.assertIn(f'("{approx}",', register)
        host_step = sources[sources.index('lastAdmissionTurn = turn;'):]
        self.assertLess(host_step.index('if ( IsBroken )'), host_step.index('CheckAdmission( turn );'))

    def test_merge_keeps_both_parents_statements(self):
        level = read(REPO / 'source' / 'OpenTPW' / 'World' / 'Level.Objects.cs')
        self.assertIn('new ParkObjects( ObjectCatalog.Load( theme, easy ), grid, Seed ) { IsReserved = ( x, y ) => '
                      'IsReservedByPrototype( x, y ) || Guests?.Grid.IsQueue( x, y ) == true };', level)
        resolve = level[level.index('ResolveVisitorCells( IEnumerable<ObjectAccessPoint>'):]
        resolve = resolve[:resolve.index('\n\t}\n')]
        self.assertIn('WalkableNear( grid, entrance.OutsideX, entrance.OutsideY )', resolve)  # main's static form
        self.assertIn('if ( entranceCell == null || exitCell == null )\n\t\t\treturn null;', resolve)
        self.assertIn('(entrance.OutsideX, entrance.OutsideY)', resolve)  # c213e81's unsnapped queue front
        tool = read(REPO / 'tools' / 'fidelity_register.py')
        for key in ('DET', 'GATE', 'QUEUE'):
            self.assertIn(f'    "{key}": "source/', tool)


class Git(unittest.TestCase):
    def setUp(self):
        for rev in (MAIN, MERGE, FIX):
            git('cat-file', '-e', rev + '^{commit}')

    def test_merge_leaves_the_gate_as_on_main(self):
        self.assertEqual(show(MAIN, 'source/OpenTPW/Client/M3Gate.cs'), show(MERGE, 'source/OpenTPW/Client/M3Gate.cs'))
        parents = git('rev-list', '--parents', '-n', '1', MERGE).stdout.decode().split()
        self.assertEqual(3, len(parents))

    def test_fix_stack_fast_forwards_main(self):
        self.assertEqual(0, git('merge-base', '--is-ancestor', MAIN, FIX, check=False).returncode)


if __name__ == '__main__':
    unittest.main()
