from pathlib import Path
import tempfile
import unittest

import readme_metadata as reader


class ReadmeTests(unittest.TestCase):
    def test_version_metadata_has_no_source_text_or_system_version_guess(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'Readme.txt'
            path.write_text('DirectX 8.0\nTheme Park Inc Version 1.2\nPrivate prose omitted.\n')
            result = reader.inspect(path)
            self.assertEqual(result['explicitProductEngineVersionCandidates'], ['1.2'])
            self.assertNotIn('text', result)
            blocked = Path(directory) / 'serial.txt'
            with self.assertRaises(ValueError):
                reader.inspect(blocked)


if __name__ == '__main__':
    unittest.main()
