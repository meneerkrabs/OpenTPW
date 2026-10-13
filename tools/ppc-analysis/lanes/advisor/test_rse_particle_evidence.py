"""Supplied original-binary classification check; no original bytes in Git."""
import os
from pathlib import Path
import unittest

import rse_particle_evidence as particles


class ParticleEvidenceTests(unittest.TestCase):
    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'supplied Mac binary not selected')
    def test_event_particle_routes_are_identity_and_operand_pinned(self):
        report = particles.native(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual(report['classification'], 'particles')
        self.assertEqual([row['type'] for row in report['routes']], [1, 2])
        self.assertEqual([row['direction_vector'] for row in report['routes']], [False, True])
        self.assertEqual(report['position_scale'], 1024)


if __name__ == '__main__':
    unittest.main()
