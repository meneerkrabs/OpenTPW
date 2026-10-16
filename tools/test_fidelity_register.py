"""Synthetic annotation fixtures: inventory integrity, not gameplay semantics."""
import subprocess
import sys
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

from fidelity_register import (CLOSED_AREAS, DOCUMENT, REGISTERS, RETIRED_LIST, Inventory, annotations, collect, in_words,
                               prose_retired, render, write_retired)


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

    def test_cli_check_validates_sources_without_a_document_and_write_generates_it(self):
        # The register document is a build artifact: --check needs no committed copy and writes nothing.
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
            check = [sys.executable, str(tool), '--root', str(root), '--check']
            result = subprocess.run(check, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn(f"{len(REGISTERS)} unresolved unique APPROX IDs", result.stdout)
            self.assertFalse((root / DOCUMENT).exists())
            self.assertFalse((root / RETIRED_LIST).exists())
            # A stale local copy is irrelevant to the check.
            (root / DOCUMENT).write_text('stale\n')
            self.assertEqual(subprocess.run(check, capture_output=True, text=True).returncode, 0)
            output = root / 'out/register.md'
            result = subprocess.run([sys.executable, str(tool), '--root', str(root), '--write', '--output', str(output)],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(output.read_text(), render(data))
            self.assertEqual((root / DOCUMENT).read_text(), 'stale\n')
            result = subprocess.run([sys.executable, str(tool), '--root', str(root), '--write'], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual((root / DOCUMENT).read_text(), render(data))
            # A source inconsistency fails the check.
            (root / 'source/extra.cs').write_text('// [APPROX:ECON-002] undeclared\n')
            result = subprocess.run(check, capture_output=True, text=True)
            self.assertEqual(result.returncode, 1)
            self.assertIn('ECON-002: annotation has no declaration', result.stderr)
            # A new APPROX ID in the closed ONLINE area fails with a pointer to EXT labels.
            (root / 'source/extra.cs').write_text('// [APPROX:ONLINE-002] new online uncertainty\n')
            result = subprocess.run(check, capture_output=True, text=True)
            self.assertEqual(result.returncode, 1)
            self.assertIn(f"ONLINE-002: ONLINE is a closed area without APPROX IDs: {CLOSED_AREAS['ONLINE']}", result.stderr)

    def test_cli_check_requires_retired_ids_to_be_recorded(self):
        tool = Path(__file__).with_name('fidelity_register.py')
        with TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'source').mkdir()
            (root / 'docs').mkdir()
            (root / 'tools').mkdir()
            (root / 'source/online.cs').write_text('// [EXT:online-fixture] design choice\n')
            online = '## Online design choices\n| [EXT:online-fixture] | Choice |\n\n## Notes\n| ONLINE-007 | retired |\n'
            (root / 'docs/ONLINE.md').write_text(online)
            for relative in set(REGISTERS.values()):
                file = root / relative
                file.parent.mkdir(parents=True, exist_ok=True)
                file.write_text('// register-empty: fixture\n')
            check = [sys.executable, str(tool), '--root', str(root), '--check']
            result = subprocess.run(check, capture_output=True, text=True)
            self.assertEqual(result.returncode, 1)
            self.assertIn(f'ONLINE-007: retired or reserved (docs/ONLINE.md) but not recorded in {RETIRED_LIST}', result.stderr)
            (root / RETIRED_LIST).write_text('# header kept\n\nZZZ-001 kept\n')
            result = subprocess.run([sys.executable, str(tool), '--root', str(root), '--write', '--output', str(root / 'r.md')],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual((root / RETIRED_LIST).read_text(), '# header kept\n\nONLINE-007 docs/ONLINE.md\nZZZ-001 kept\n')
            self.assertEqual(subprocess.run(check, capture_output=True, text=True).returncode, 0)
            # The entry stays recorded after its doc row is gone; a union merge may leave the list unsorted.
            (root / 'docs/ONLINE.md').write_text(online.split('## Notes')[0])
            (root / RETIRED_LIST).write_text('ZZZ-001 kept\nONLINE-007 docs/ONLINE.md\nONLINE-007 docs/ONLINE.md\n')
            self.assertEqual(subprocess.run(check, capture_output=True, text=True).returncode, 0)
            (root / RETIRED_LIST).write_text('ONLINE-007\n')
            result = subprocess.run(check, capture_output=True, text=True)
            self.assertEqual(result.returncode, 1)
            self.assertIn(f'{RETIRED_LIST}:1: expected `ID where`', result.stderr)

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
            write_retired(root, data.retired)
            # Reusing the retired number fails, even after its doc row is gone.
            (root / 'docs/reverse/TRACE.md').write_text('| TEST-001 | same rule |\n')
            code.write_text('// [APPROX:TEST-001] one\n// [APPROX:TEST-002] another meaning\n')
            register.write_text('("TEST-001", "rule"),\n("TEST-002", "another meaning"),\n')
            errors = collect(root, config).errors
            self.assertTrue(any(e.startswith(f'TEST-002: declared, but {RETIRED_LIST} lists it as retired') for e in errors), errors)
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
            write_retired(root, data.retired)
            code.write_text('// [APPROX:TEST-001] one\n// [APPROX:TEST-004] another meaning\n')
            register.write_text('("TEST-001", "rule"),\n("TEST-004", "another meaning"),\n')
            errors = collect(root, config).errors
            self.assertTrue(any(e.startswith(f'TEST-004: declared, but {RETIRED_LIST} lists it as retired') for e in errors), errors)

    def test_ids_retired_in_plain_prose_are_recorded(self):
        # docs/TRACKS.md retired TRACK-009 and others in plain words ("TRACK-009 retired October 14"), so the list had no
        # TRACK entries and a retired number could have been declared again.
        self.assertEqual(prose_retired('direction (TRACK-009 retired October 14). The path tool'), ['TRACK-009'])
        self.assertEqual(prose_retired('heading 0 (TRACK-012 and TRACK-023 retired: the record is zeroed)'),
                         ['TRACK-012', 'TRACK-023'])
        self.assertEqual(prose_retired('UI-001, 038, 040 and 902 are retired'), ['UI-001', 'UI-038', 'UI-040', 'UI-902'])
        self.assertEqual(prose_retired('COAST-020/021 are retired; `ONLINE-073` is retired'),
                         ['COAST-020', 'COAST-021', 'ONLINE-073'])
        self.assertEqual(prose_retired('ADVISOR-027 (the cut-off slap, TRACK-040 and its 100 frames) was retired'),
                         ['ADVISOR-027'])
        self.assertEqual(prose_retired('Retired with a code change: TRACK-009 (snap), 011 (tick), 012 and 023 (start '
                                       'heading). Narrowed: TRACK-002, 005.'),
                         ['TRACK-009', 'TRACK-011', 'TRACK-012', 'TRACK-023'])
        self.assertEqual(prose_retired('Also retired: RIDES-017 (filler)'), ['RIDES-017'])
        # A hedge word counts only right before the list, not across punctuation.
        self.assertEqual(prose_retired('rolls a forecast at once (AUDIO-027 is retired).'), ['AUDIO-027'])
        # Only a list that "retired" directly follows or precedes counts.
        for line in ('TRACK-001 narrowed, TRACK-031 retired', 'COAST-004 and 029 narrowed, COAST-014 retired'):
            self.assertEqual(len(prose_retired(line)), 1, line)
        for line in ("RIDES-029, 030 and 039 collided with main's retired IDs", 'TRACK-005 is not retired',
                     'TRACK-005 will be retired', 'ECON-046 is a retired ID with another meaning',
                     'Speech ducking (AUDIO-001) is retired', 'Not retired: TRACK-007', 'Retired or not: TRACK-007',
                     'Also not yet retired: TRACK-007', 'TRACK-005 and 2026 retired', 'xTRACK-005 retired',
                     'Retired: the TRACK-007 rule', 'TRACK-005 retiredness', 'Retired: TRACK-009A',
                     'Retired only if tests pass: TRACK-003', 'TRACK-005 (not) retired', 'Is TRACK-009 retired?',
                     'TRACK-009 retired?', 'Neither TRACK-002 nor TRACK-003 retired'):
            self.assertEqual(prose_retired(line), [], line)
        with TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'source').mkdir()
            (root / 'docs').mkdir()
            code = root / 'source/test.cs'
            register = root / 'source/register.cs'
            code.write_text('// [APPROX:TEST-001] one\n')
            register.write_text('("TEST-001", "rule"),\n')
            (root / 'docs/TRACKS.md').write_text('TEST-001 narrowed, TEST-002 and 003 retired (OTHER-004 retired).\n')
            config = {'TEST': 'source/register.cs'}
            data = collect(root, config)
            self.assertEqual(data.errors, [])
            self.assertEqual(data.retired, {'TEST-002': 'docs/TRACKS.md', 'TEST-003': 'docs/TRACKS.md'})
            write_retired(root, data.retired)
            code.write_text('// [APPROX:TEST-001] one\n// [APPROX:TEST-003] another meaning\n')
            register.write_text('("TEST-001", "rule"),\n("TEST-003", "another meaning"),\n')
            errors = collect(root, config).errors
            self.assertTrue(any(e.startswith(f'TEST-003: declared, but {RETIRED_LIST} lists it as retired') for e in errors), errors)

    def test_register_count_sentence_follows_the_configuration(self):
        # Review WV-10: a merge that adds a register to REGISTERS must not leave a stale count in the document.
        self.assertIn(f'read from the {in_words(len(REGISTERS))} configured C# registers', render(Inventory([], {}, [])))
        self.assertIn('read from the three configured C# registers', render(Inventory([], {}, [], 3)))
        self.assertEqual(('eleven', 'twelve', '21'), (in_words(11), in_words(12), in_words(21)))


if __name__ == '__main__':
    unittest.main()
