"""Synthetic annotation fixtures: inventory integrity, not gameplay semantics."""
import subprocess
import sys
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

from fidelity_register import EXTENSION_SCOPE, REGISTERS, Inventory, annotations, collect, in_words, markdown_declarations, render


class FidelityRegisterTests(unittest.TestCase):
    def test_repeated_ids_and_distinct_provenance_kinds(self):
        sites, errors = annotations('''// [APPROX:TEST-001] first
/// [APPROX:TEST-001] repeated reference
// [DATA:object.sam:Upgrades[0].InitDuration] [EXT:display] intentional
var text = "[APPROX:TEST-002] runtime string";
/// <c>[APPROX:TEST-NNN]</c> documentation template
''', 'fixture.cs')
        self.assertEqual(errors, [])
        self.assertEqual([s.kind for s in sites], ['APPROX', 'APPROX', 'DATA', 'EXT'])
        self.assertEqual(sites[2].label, 'object.sam:Upgrades[0].InitDuration')
        self.assertEqual([s.line for s in sites], [1, 2, 3, 3])

    def test_binary_evidence_labels(self):
        sites, errors = annotations('''// [BIN:STP-PPC:0x1010474C world setup] traced
// [BIN:TPI-EXE:0x00401000] traced
/// <c>[BIN:STP-PPC:<address>]</c> documentation template
''', 'fixture.cs')
        self.assertEqual(errors, [])
        self.assertEqual([(s.kind, s.label) for s in sites],
                         [('BIN', 'STP-PPC:0x1010474C world setup'), ('BIN', 'TPI-EXE:0x00401000')])
        for tag in ['[BIN:0x1010474C]', '[BIN:STP-PPC:]', '[BIN:manual:p12]', '[BIN:]']:
            with self.subTest(tag=tag):
                sites, errors = annotations('// ' + tag, 'fixture.cs')
                self.assertEqual(sites, [])
                self.assertEqual(len(errors), 1)

    def test_multiline_data_comment(self):
        sites, errors = annotations('''/// Original values ([DATA:settings:0 Point,
/// 1 Bilinear, 2 Trilinear]); inferred sampler setup.
// [APPROX:TEST-001] conversion remains unresolved
''', 'fixture.cs')
        self.assertEqual(errors, [])
        self.assertEqual(sites[0].label, 'settings:0 Point, 1 Bilinear, 2 Trilinear')
        self.assertEqual(sites[0].line, 1)
        self.assertEqual(sites[1].line, 3)

    def test_missing_or_malformed_ids(self):
        for tag in ['[APPROX]', '[APPROX:]', '[APPROX:TEST-12]', '[APPROX:TEST-001',
                    '[APPROX:test-001]', '[EXT:]']:
            with self.subTest(tag=tag):
                sites, errors = annotations('// ' + tag, 'fixture.cs')
                self.assertEqual(sites, [])
                self.assertEqual(len(errors), 1)

    def test_completeness_and_duplicate_declarations(self):
        with TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'source').mkdir()
            code = root / 'source/test.cs'
            register = root / 'source/register.cs'
            code.write_text('// [APPROX:TEST-001] one\n// [APPROX:TEST-001] repeated\n')
            register.write_text('("TEST-001", "rule"),\n')
            config = {'TEST': 'source/register.cs'}
            result = collect(root, config)
            self.assertEqual(result.errors, [])
            self.assertEqual(result.unresolved, ['TEST-001'])
            self.assertEqual(len(result.sites), 2)
            register.write_text('("TEST-001", "rule"),\n("TEST-001", "again"),\n')
            self.assertTrue(any('duplicate declaration TEST-001' in e for e in collect(root, config).errors))
            register.write_text('("TEST-002", "other"),\n')
            errors = collect(root, config).errors
            self.assertTrue(any('TEST-001: annotation has no declaration' in e for e in errors))
            self.assertTrue(any('TEST-002: declaration has no source annotation' in e for e in errors))
            register.write_text('("TEST-2", "bad ID"),\n')
            self.assertTrue(any('malformed declaration ID' in e for e in collect(root, config).errors))

    def test_markdown_extension_evidence_and_consistency(self):
        with TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'source').mkdir()
            (root / 'docs').mkdir()
            code = root / 'source/chat.cs'
            document = root / 'docs/ONLINE.md'
            code.write_text('// [APPROX:ONLINE-001] inferred input\n// [EXT:ONLINE-001] package choice\n')
            text = '## Approximation register\n| ID | Assumption | Evidence needed |\n| --- | --- | --- |\n| ONLINE-001 | Inferred rule | Original trace |\n'
            document.write_text(text)
            config = {'ONLINE': 'docs/ONLINE.md'}
            result = collect(root, {}, config)
            self.assertEqual(result.errors, [])
            self.assertEqual(result.unresolved, ['ONLINE-001'])
            self.assertEqual(result.declarations['ONLINE-001'].scope, EXTENSION_SCOPE)
            self.assertEqual(result.declarations['ONLINE-001'].evidence, 'Original trace')
            self.assertEqual([s.kind for s in result.sites], ['APPROX', 'EXT'])
            self.assertIn('1 ONLINE IDs record uncertainty', render(result))
            document.write_text(text + '| ONLINE-001 | Duplicate | Original trace |\n')
            self.assertTrue(any('duplicate declaration ONLINE-001' in e for e in collect(root, {}, config).errors))
            document.write_text(text.replace('ONLINE-001', 'ONLINE-002'))
            self.assertEqual(len(collect(root, {}, config).errors), 2)

    def test_markdown_requires_valid_id_assumption_and_evidence(self):
        header = '## Approximation register\n'
        for row in ['| ONLINE-001 | Rule | |', '| ONLINE-001 | | Trace |',
                    '| ONLINE-01 | Rule | Trace |', '| OTHER-001 | Rule | Trace |',
                    '| ONLINE-001 | Rule | Trace | extra |']:
            with self.subTest(row=row):
                entries, errors = markdown_declarations(header + row, 'docs/ONLINE.md', 'ONLINE')
                self.assertEqual(entries, [])
                self.assertTrue(errors)
        text = '| ONLINE-999 | Outside section | Trace |\n' + header + '| ONLINE-001 | Rule with escaped \\| delimiter | Trace |\n## Other section\n| ONLINE-002 | Outside section | Trace |'
        entries, errors = markdown_declarations(text, 'docs/ONLINE.md', 'ONLINE')
        self.assertEqual(errors, [])
        self.assertEqual([label for label, _ in entries], ['ONLINE-001'])
        self.assertEqual(entries[0][1].description, 'Rule with escaped | delimiter')

    def test_cli_check_detects_stale_and_incomplete_document(self):
        tool = Path(__file__).with_name('fidelity_register.py')
        with TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'docs').mkdir()
            # Several prefixes may share one register file (RIDES and MD2 both read RidesApproximations.cs).
            grouped = {}
            for prefix, relative in REGISTERS.items():
                grouped[relative] = grouped.get(relative, '') + f'// [APPROX:{prefix}-001] fixture\n("{prefix}-001", "rule"),\n'
            for relative, text in grouped.items():
                file = root / relative
                file.parent.mkdir(parents=True, exist_ok=True)
                file.write_text(text)
            (root / 'source/online.cs').write_text('// [APPROX:ONLINE-001] unresolved fixture\n')
            (root / 'docs/ONLINE.md').write_text('## Approximation register\n| ONLINE-001 | Rule | Original behavior trace |\n')
            data = collect(root)
            self.assertEqual(data.errors, [])
            document = root / 'docs/FIDELITY-REGISTER.md'
            document.write_text(render(data))
            command = [sys.executable, str(tool), '--root', str(root), '--check']
            result = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn(f"{len(REGISTERS) + 1} unresolved unique APPROX IDs", result.stdout)
            document.write_text(render(data).replace('| ECON-001 |', '| ECON-999 |'))
            result = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(result.returncode, 1)
            self.assertIn('missing or stale', result.stderr)

    def test_ids_used_only_in_docs_cannot_be_declared_again(self):
        with TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'source').mkdir()
            (root / 'docs/reverse').mkdir(parents=True)
            code = root / 'source/test.cs'
            register = root / 'source/register.cs'
            code.write_text('// [APPROX:TEST-001] one\n')
            register.write_text('("TEST-001", "rule"),\n')
            (root / 'docs/reverse/TRACE.md').write_text('| ID | Verdict |\n| --- | --- |\n| TEST-001 | same rule |\n| TEST-002 | retired rule |\n| OTHER-003 | not an area |\n')
            config = {'TEST': 'source/register.cs'}
            data = collect(root, config)
            self.assertEqual(data.errors, [])
            self.assertEqual(data.retired, {'TEST-002': 'docs/reverse/TRACE.md'})
            (root / 'docs/FIDELITY-REGISTER.md').write_text(render(data))
            # Reusing the retired number fails, even after its doc row is gone.
            (root / 'docs/reverse/TRACE.md').write_text('| TEST-001 | same rule |\n')
            code.write_text('// [APPROX:TEST-001] one\n// [APPROX:TEST-002] another meaning\n')
            register.write_text('("TEST-001", "rule"),\n("TEST-002", "another meaning"),\n')
            errors = collect(root, config).errors
            self.assertTrue(any(e.startswith('TEST-002: declared, but docs/FIDELITY-REGISTER.md lists it as retired') for e in errors), errors)
            self.assertIn('TEST-002', collect(root, config).retired)

    def test_ids_retired_in_docs_prose_cannot_be_declared_again(self):
        # Review WV2-2: GATE-002..GATE-005 were retired in prose ("`[APPROX:GATE-004]` ... now retired"), not in a table.
        with TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'source').mkdir()
            (root / 'docs').mkdir()
            code = root / 'source/test.cs'
            register = root / 'source/register.cs'
            code.write_text('// [APPROX:TEST-001] one\n')
            register.write_text('("TEST-001", "rule"),\n')
            (root / 'docs/GATE.md').write_text('`[APPROX:TEST-001]` stays; `[APPROX:TEST-004]` is now retired (APPROX:OTHER-005).\n')
            config = {'TEST': 'source/register.cs'}
            data = collect(root, config)
            self.assertEqual(data.errors, [])
            self.assertEqual(data.retired, {'TEST-004': 'docs/GATE.md'})
            (root / 'docs/FIDELITY-REGISTER.md').write_text(render(data))
            code.write_text('// [APPROX:TEST-001] one\n// [APPROX:TEST-004] another meaning\n')
            register.write_text('("TEST-001", "rule"),\n("TEST-004", "another meaning"),\n')
            errors = collect(root, config).errors
            self.assertTrue(any(e.startswith('TEST-004: declared, but docs/FIDELITY-REGISTER.md lists it as retired') for e in errors), errors)

    def test_register_count_sentence_follows_the_configuration(self):
        # Review WV-10: a merge that adds a register to REGISTERS must not leave a stale count in the document.
        self.assertIn(f'read from the {in_words(len(REGISTERS))} configured C# registers', render(Inventory([], {}, [])))
        self.assertIn('read from the three configured C# registers', render(Inventory([], {}, [], 3)))
        self.assertEqual(('eleven', 'twelve', '21'), (in_words(11), in_words(12), in_words(21)))


if __name__ == '__main__':
    unittest.main()
