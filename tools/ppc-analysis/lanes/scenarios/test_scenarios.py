"""Scenario-lane checks. Synthetic fixtures only, except one corpus run skipped when assets are absent.

Run: python3 -m unittest discover -s tools/ppc-analysis/lanes/scenarios -v
Optional: OPENTPW_MAC_BIN=/path/to/mac-feral/bin enables the identified-binary run.
"""
import os
from pathlib import Path
import struct
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest

import followup_evidence as followup
import mac_data_compare as compare
import scenario_evidence as evidence
from scenario_evidence import Evidence, magic, pef


def fixture(words, data=b'', relocs=None):
    """Minimal stand-in for an Evidence object over synthetic code words."""
    e = Evidence.__new__(Evidence)
    e.c = SimpleNamespace(imports=[SimpleNamespace(name='__nw__FUl')], code=SimpleNamespace(index=0))
    e.code = b''.join(struct.pack('>I', w) for w in words)
    e.data = data
    e.relocs = relocs or {}
    e.toc = 0x8000
    e.checked = 0
    e._calls = None
    return e


def d_word(op, rt, ra, imm):
    return op << 26 | rt << 21 | ra << 16 | imm & 0xffff


class DecoderTests(unittest.TestCase):
    def test_d_form_signed_and_unsigned_immediates(self):
        e = fixture([d_word(14, 3, 0, -1), d_word(10, 0, 19, 0xffff), d_word(25, 0, 0, 512)])
        e.d(0, 14, 3, 0, -1)
        e.d(4, 10, 0, 19, 0xffff)
        e.d(8, 25, 0, 0, 512)
        self.assertEqual(e.checked, 3)
        with self.assertRaisesRegex(pef.PEFError, 'code:0x0 D-form'):
            e.d(0, 14, 3, 0, 1)

    def test_x_a_and_rotate_fields(self):
        mulhw = 31 << 26 | 3 << 21 | 0 << 16 | 5 << 11 | 75 << 1
        fdivs = 59 << 26 | 1 << 21 | 0 << 16 | 31 << 11 | 18 << 1
        srwi = 21 << 26 | 3 << 21 | 0 << 16 | 1 << 11 | 31 << 6 | 31 << 1
        e = fixture([mulhw, fdivs, srwi])
        e.x(0, 31, 75, 3, 0, 5)
        e.a(4, 59, 18, 1, 0, 31)
        e.rlwinm(8, 3, 0, 1, 31, 31)
        with self.assertRaises(pef.PEFError):
            e.x(0, 31, 11, 3, 0, 5)

    def test_branches_and_call_index(self):
        e = fixture([18 << 26 | 8 | 1, 0, 16 << 26 | 12 << 21 | 2 << 16 | (-8 & 0xfffc)])
        e.bl(0, 8)
        e.bc(8, 12, 2, 0)
        self.assertEqual(e.calls_to(8), [0])
        with self.assertRaisesRegex(pef.PEFError, 'call target'):
            e.bl(0, 4)
        with self.assertRaisesRegex(pef.PEFError, 'conditional branch'):
            e.bc(8, 4, 2, 0)

    def test_sole_callers_rejects_address_taken_targets(self):
        relocs = {0x10: pef.RelocTarget('section', 0, 8)}
        e = fixture([18 << 26 | 8 | 1, 0, 0], relocs=relocs)
        with self.assertRaisesRegex(pef.PEFError, 'callers'):
            e.sole_callers(8, [0])
        self.assertEqual(fixture([18 << 26 | 8 | 1, 0, 0]).sole_callers(8, [0]), ['0x0'])

    def test_toc_load_and_import_glue(self):
        relocs = {0x8010: pef.RelocTarget('import', 0, 0), 0x8014: pef.RelocTarget('section', 0, 0x40)}
        words = [18 << 26 | 8 | 1, d_word(32, 3, 2, 0x14), d_word(32, 12, 2, 0x10)]
        e = fixture(words, relocs=relocs)
        e.toc_load(4, 3, ('code', 0x40))
        e.import_call(0, '__nw__FUl')
        with self.assertRaisesRegex(pef.PEFError, 'TOC slot target'):
            e.toc_load(4, 3, ('code', 0x44))

    def test_schema_records_and_strings(self):
        record = struct.pack('>I', 4) + b'CostToEnter'.ljust(56, b'\0')
        e = fixture([0x4b657973, 0x0a000000], data=record)
        self.assertEqual(e.schema(0, [(4, 'CostToEnter')]), ['CostToEnter:4'])
        self.assertEqual(e.text(0, 'Keys'), 'Keys')
        with self.assertRaisesRegex(pef.PEFError, 'schema record'):
            e.schema(0, [(5, 'CostToEnter')])

    def test_magic_pairs_used_by_the_evidence(self):
        self.assertEqual(magic(21845, 21846), 0x55555556)
        self.assertEqual(magic(20972, -31457), 0x51eb851f)
        self.assertEqual((6034 << 32) + magic(-1947, -32768), 30 * 864_000_000_000)
        self.assertEqual((201 << 32) + magic(10858, -16384), 86_400 * 10_000_000)

    def test_identity_rejects_unknown_binary(self):
        with TemporaryDirectory() as directory:
            path = Path(directory) / 'SimThemePark.data'
            path.write_bytes(b'synthetic, not a game binary')
            with self.assertRaisesRegex(pef.PEFError, 'identity'):
                evidence.load_identified(path)


class CompareTests(unittest.TestCase):
    def test_bfst_round_trip_and_alignment(self):
        table = b'BFMU' + struct.pack('<HH', 0, 3) + ''.join('abc').encode('utf-16-le')
        strings = [b'\x01\x02', b'\x03']
        offsets, body = [], b''
        for s in strings:
            offsets.append(len(body))
            body += b'\x01' + struct.pack('<I', len(s))[:3] + s
        raw = b'BFST' + struct.pack('<ii', 0, 2) + b''.join(struct.pack('<i', 8 + o) for o in offsets) + body
        decoded = compare.bfst(raw, compare.bfmu(table))
        self.assertEqual(decoded, ['ab', 'c'])
        self.assertEqual(compare.alignment(['a', 'b'], ['a', 'x', 'b']),
                         [{'edit': 'insert', 'windows_range': [1, 1], 'mac_range': [1, 2]}])
        with self.assertRaises(ValueError):
            compare.bfst(raw[:-1] + b'\x09', compare.bfmu(table))

    def test_compare_classifies_and_reads_global_values(self):
        with TemporaryDirectory() as directory:
            root = Path(directory)
            for side, cost in (('mac', 3), ('win', 3), ('p2', 3)):
                theme = root / side / 'levels' / 'fantasy'
                theme.mkdir(parents=True)
                (theme / 'global.sam').write_text(f'Keys.CostToEnter\t\t{cost}\r\n')
                (theme / 'Standard.sam').write_text('A 1\r\n' if side != 'win' else 'A 2\r\n')
            result = compare.compare(root / 'mac', root / 'win', root / 'p2')['files']
            self.assertEqual(result['levels/fantasy/global.sam']['values'], {'Keys.CostToEnter': 3})
            self.assertEqual(result['levels/fantasy/Standard.sam']['status'], 'same_as_patch2')
            self.assertFalse(result['levels/fantasy/Standard.sam']['patch2_equals_windows'])
            self.assertEqual(result['Challenges.sam']['status'], 'missing')


def schema_bytes(records):
    out = b''
    for kind, name, count in records:
        out += struct.pack('>I', kind) + name.encode().ljust(48, b'\0') + struct.pack('>II', count, 0)
    return out


class BalanceLayoutTests(unittest.TestCase):
    RECORDS = [(0, '', 0), (5, 'ExitLevel', 0), (1, 'PeepInfo', 0),
               (2, '', 0), (5, 'BaseWage', 0), (7, 'RecuperationRate', 0), (3, 'PerGrade', 3),
               (6, 'StartingWorkLoad', 0), (1, 'Research', 0), (4, 'Loose', 0)]

    def test_parser_rules_counter_start_arrays_and_groups(self):
        layout = followup.balance_layout(self.RECORDS)
        # counter starts at word 1 behind an 8-byte value base
        self.assertEqual(layout['PeepInfo.ExitLevel'], 12)
        # array element k of a two-field struct: word 2 + 2k (+1 for the second field)
        self.assertEqual([layout[f'PerGrade[{k}].BaseWage'] for k in range(3)], [16, 24, 32])
        self.assertEqual(layout['PerGrade[2].RecuperationRate'], 36)
        # the closing record adds one word after 3*2 element words
        self.assertEqual(layout['Research.StartingWorkLoad'], 8 + 4 * (2 + 6 + 1))
        self.assertEqual(layout['Loose'], layout['Research.StartingWorkLoad'] + 4)
        self.assertNotIn('PeepInfo', layout)

    def test_unclosed_array_and_schema_reader(self):
        with self.assertRaisesRegex(pef.PEFError, 'no closing record'):
            followup.balance_layout([(2, '', 0), (5, 'A', 0), (1, 'G', 0)])
        raw = schema_bytes(self.RECORDS + [(12, '', 0)])
        self.assertEqual(followup.schema_records(raw, 0), self.RECORDS)
        with self.assertRaisesRegex(pef.PEFError, 'TABLE_END'):
            followup.schema_records(schema_bytes(self.RECORDS), 0)
        with self.assertRaisesRegex(pef.PEFError, 'record type'):
            followup.schema_records(schema_bytes([(13, 'X', 0)]), 0)

    def test_toc_users_matches_r2_displacements_only(self):
        words = [d_word(32, 3, 2, 0x10), d_word(32, 3, 4, 0x10), d_word(36, 0, 2, 0x10), d_word(32, 3, 2, 0x14)]
        e = fixture(words)
        self.assertEqual(followup.toc_users(e, 0x8010), [0, 8])
        self.assertEqual(followup.exact_callers(fixture([18 << 26 | 8 | 1, 0, 0]), 8, [0]), ['0x0'])
        with self.assertRaisesRegex(pef.PEFError, 'callers'):
            followup.exact_callers(fixture([0, 0, 0]), 8, [0])


@unittest.skipUnless(os.environ.get('OPENTPW_MAC_BIN'), 'set OPENTPW_MAC_BIN to the Feral bin directory')
class CorpusTests(unittest.TestCase):
    def test_identified_executable_facts(self):
        result = evidence.inspect(Path(os.environ['OPENTPW_MAC_BIN']))
        self.assertEqual(result['progression']['serialized_members']['mExtraKeys'], 32)
        self.assertEqual(result['theme_entry']['key_consumption_on_entry'], False)
        self.assertEqual(result['golden_ticket_checks']['game_type_required'], 0)
        self.assertEqual(result['strikes']['minimum_park_age_months'], 24)
        self.assertGreater(result['instruction_checks'], 600)
        balance = result['main_balance']
        self.assertEqual(balance['records'], 283)
        self.assertEqual(balance['derived']['GoldenTicketGlobal.MinCellsOwned'], 1912)
        self.assertEqual(balance['anchors_verified']['ResearchTech[1].PercentageForThisTech'], 1280)
        self.assertEqual(result['advisor']['rules']['167']['responses'], [380, 381])
        self.assertEqual(result['finance']['bankrupt_after_months_in_red'], 6)
        self.assertEqual(result['staff_economy']['debit_callers'], 16)

    def test_layout_rejects_a_changed_counter_start_or_anchor(self):
        container = evidence.load_identified(Path(os.environ['OPENTPW_MAC_BIN']) / 'SimThemePark.data')
        e = Evidence(container)
        code = bytearray(e.code)
        struct.pack_into('>I', code, 0x16f90, d_word(14, 27, 0, 0))  # in memory only
        e.code = bytes(code)
        with self.assertRaisesRegex(pef.PEFError, 'code:0x16f90'):
            followup.main_balance(e)
        e = Evidence(container)
        data = bytearray(e.data)
        struct.pack_into('>I', data, followup.MAIN_TABLE + 60 * 44 + 52, 99)  # widen the first array
        e.data = bytes(data)
        with self.assertRaisesRegex(pef.PEFError, 'balance layout'):
            followup.main_balance(e)


if __name__ == '__main__':
    unittest.main()
