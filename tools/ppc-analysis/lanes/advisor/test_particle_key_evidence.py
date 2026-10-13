import os
from pathlib import Path
import unittest

import particle_key_evidence as keys


class ParticleKeyEvidenceTests(unittest.TestCase):
    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'Supplied original binary not selected')
    def test_key_mapping_and_direction_operands(self):
        report = keys.native(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual(report['first_node_header_offset'], 70)
        self.assertEqual(report['matrix_position_offsets'], [48, 52, 56])
        self.assertEqual(report['constants']['-11580'], 1024.0)


if __name__ == '__main__':
    unittest.main()
