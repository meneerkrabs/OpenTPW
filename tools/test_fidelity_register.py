"""Synthetic annotation fixtures: inventory integrity, not gameplay semantics."""
import subprocess
import sys
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

from fidelity_register import CLOSED_AREAS, REGISTERS, Inventory, annotations, collect, in_words, render


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

    def test_empty_register_marker_needs_a_whole_line_comment(self):
        with TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'source').mkdir()
            (root / 'source/test.cs').write_text('// nothing annotated\n')
            register = root / 'source/register.cs'
            config = {'TEST': 'source/register.cs'}
            register.write_text('// no entries yet\n')
            self.assertTrue(any('no declarations found' in e for e in collect(root, config).errors))
            register.write_text('var text = "register-empty: not a comment line";\n')
            self.assertTrue(any('no declarations found' in e for e in collect(root, config).errors))
            register.write_text('\t// register-empty: the last entry was retired\n')
            self.assertEqual(collect(root, config).errors, [])
            # the marker does not hide entries: an annotated site without a declaration is still an error
            (root / 'source/test.cs').write_text('// [APPROX:TEST-001] one\n')
            self.assertTrue(any('TEST-001: annotation has no declaration' in e for e in collect(root, config).errors))

    def test_closed_area_takes_no_approx_ids_and_keeps_its_ids_reserved(self):
        with TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'source').mkdir()
            (root / 'docs').mkdir()
            code = root / 'source/chat.cs'
            code.write_text('// [EXT:online-chat-rule] OpenTPW design choice\n')
            (root / 'docs/ONLINE.md').write_text('| [EXT:online-chat-rule] | `APPROX:ONLINE-001` | Choice | Evidence |\n| ONLINE-002 | old row |\n')
            closed = {'ONLINE': 'online choices take EXT labels'}
            data = collect(root, {}, closed)
            self.assertEqual(data.errors, [])
            self.assertEqual(data.unresolved, [])
            self.assertEqual([(s.kind, s.label) for s in data.sites], [('EXT', 'online-chat-rule')])
            self.assertEqual(data.retired, {'ONLINE-001': 'docs/ONLINE.md', 'ONLINE-002': 'docs/ONLINE.md'})
            code.write_text('// [APPROX:ONLINE-003] new uncertainty\n')
            self.assertIn('ONLINE-003: ONLINE is a closed area without APPROX IDs: online choices take EXT labels', collect(root, {}, closed).errors)
        self.assertIn('ONLINE', CLOSED_AREAS)

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
            data = collect(root)
            self.assertEqual(data.errors, [])
            document = root / 'docs/FIDELITY-REGISTER.md'
            document.write_text(render(data))
            command = [sys.executable, str(tool), '--root', str(root), '--check']
            result = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn(f"{len(REGISTERS)} unresolved unique APPROX IDs", result.stdout)
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
