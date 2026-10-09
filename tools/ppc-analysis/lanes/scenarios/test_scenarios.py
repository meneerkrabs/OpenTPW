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
import park_entry_evidence as park_entry
import player_file_evidence as player_file
import profile_evidence as profile
import progression_evidence as progression
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


class ProgressionHelperTests(unittest.TestCase):
    def test_unconditional_branch_rejects_link_and_wrong_target(self):
        e = fixture([18 << 26 | 8, 18 << 26 | 8 | 1])
        progression.branch(e, 0, 8)
        with self.assertRaisesRegex(pef.PEFError, 'unconditional branch'):
            progression.branch(e, 4, 12)
        with self.assertRaisesRegex(pef.PEFError, 'unconditional branch'):
            progression.branch(e, 0, 12)

    def test_vtable_slot_follows_descriptor_to_code(self):
        relocs = {0x100 + 16: SimpleNamespace(kind='section', target=1, addend=0x200),
                  0x200: SimpleNamespace(kind='section', target=0, addend=0x40)}
        e = fixture([0], relocs=relocs)
        progression.vtable_slot(e, 0x100, 16, 0x40)
        with self.assertRaisesRegex(pef.PEFError, 'vtable entry'):
            progression.vtable_slot(e, 0x100, 16, 0x44)

    def test_no_base_stores_flags_any_store_width(self):
        loads = [d_word(32, 3, 26, 0), d_word(36, 3, 27, 0), d_word(34, 0, 26, 4)]
        progression.no_base_stores(fixture(loads), 0, 12, 26)
        for op in (36, 37, 38, 39, 44, 45):
            with self.assertRaisesRegex(pef.PEFError, 'store through r26'):
                progression.no_base_stores(fixture(loads + [d_word(op, 0, 26, 8)]), 0, 16, 26)


class ParkEntryHelperTests(unittest.TestCase):
    def test_wide_string_bounded_and_compared(self):
        e = fixture([0])
        e.code = ':'.encode('utf-16-be') + b'\0\0' + 'autosave'.encode('utf-16-be') + b'\0\0'
        self.assertEqual(park_entry.wide(e, 0, ':'), ':')
        self.assertEqual(park_entry.wide(e, 4, 'autosave'), 'autosave')
        with self.assertRaisesRegex(pef.PEFError, 'wide identifier string'):
            park_entry.wide(e, 4, 'restart')
        e.code = 'x'.encode('utf-16-be') * 40
        with self.assertRaisesRegex(pef.PEFError, 'no bounded wide string'):
            park_entry.wide(e, 0, 'x')

    def test_stores_at_matches_every_store_width_and_only_the_displacement(self):
        words = [d_word(32, 0, 3, 1028)] + [d_word(op, 0, 3, 1028) for op in (36, 37, 38, 39, 44, 45)]
        words.append(d_word(36, 0, 3, 1032))
        self.assertEqual(park_entry.stores_at(fixture(words), 0, 4 * len(words), 1028), [4, 8, 12, 16, 20, 24])


def park_header(version=400, language=0, banner='LEGAL', block=bytes(256), tag=0x01221985, flag=0):
    text = banner.encode('utf-16-le').ljust(1280, b'\0')
    return struct.pack('<i', version) + bytes([language]) + text + block + struct.pack('>I', tag) + \
        struct.pack('<i', flag) + b'payload'


class ParkHeaderTests(unittest.TestCase):
    def test_traced_header_checks_accept_a_matching_file(self):
        result = profile.qualify_tpwi_header(park_header(), 'LEGAL', 0x01221985)
        self.assertTrue(result['qualified'])
        self.assertEqual((result['version'], result['header_flag']), (400, 0))
        self.assertTrue(profile.qualify_tpwi_header(park_header(version=500), 'LEGAL', 0x01221985)['qualified'])

    def test_corrupt_headers_are_rejected(self):
        for raw, failed in ((park_header(version=501), 'version_le_limit'),
                            (park_header(banner='LEGAl'), 'banner_equal'),
                            (park_header(tag=0x85192201), 'tag_equal')):
            result = profile.qualify_tpwi_header(raw, 'LEGAL', 0x01221985)
            self.assertFalse(result['qualified'])
            self.assertIs(result[failed], False)
        self.assertEqual(profile.qualify_tpwi_header(park_header()[:1548], 'LEGAL', 0x01221985),
                         {'qualified': False, 'reason': 'shorter than the traced header'})

    def test_untraced_language_entry_is_not_a_pass(self):
        result = profile.qualify_tpwi_header(park_header(language=1), 'LEGAL', 0x01221985)
        self.assertIsNone(result['banner_equal'])
        self.assertFalse(result['qualified'])
        self.assertFalse(profile.qualify_tpwi_header(park_header(block=b'\1' + bytes(255)), 'LEGAL', 0x01221985)
                         ['object_block_all_zero'])


def player_file_bytes(version=12, keys=1, easy=0, themes=((b'jungle', 1),), mystery=(7,), settings=True):
    """Synthetic player file in the traced layout (invented values)."""
    out = struct.pack('<i', version) + bytes([1, 0, 0, 0]) + bytes([0, 1]) + struct.pack('<ii', 2, keys)
    out += bytes([easy, 1, 0]) + struct.pack('<i', len(themes))
    for name, local in themes:
        out += struct.pack('<i', len(name)) + name + bytes([local, 0, 0, 0, 0, 0])
        out += b''.join(bytes([i]) + struct.pack('<i', 100 + i) for i in range(4))
        out += b''.join(struct.pack('<HH', 65 + i, 97 + i) for i in range(33)) + bytes([0, 1])
    if settings:
        out += b''.join(b'\x00\x00\x00\x01' + struct.pack('<I', 50 + i) for i in range(4)) + bytes(range(7))
        out += struct.pack('<i', len(mystery)) + b''.join(struct.pack('<H', m) for m in mystery)
    return out


class PlayerFileReferenceTests(unittest.TestCase):
    """Traced Mac read order on synthetic bytes; no PC player file exists to test against."""

    def test_complete_file_reads_every_section(self):
        raw = player_file_bytes()
        out = player_file.read_mac_player_file(raw)
        self.assertTrue(out['ok'])
        self.assertEqual(out['consumed'], len(raw))
        self.assertEqual(out['record']['mExtraKeys'], 1)
        self.assertEqual(out['record']['mEarnedSecretTicket'], [0, 1])
        theme = out['themes'][b'jungle']
        self.assertEqual(theme['mAwardScore[i]'], [100, 101, 102, 103])
        self.assertEqual((theme['mSignNameA[i]'][0], theme['mSignNameB[i]'][0]), (65, 97))
        self.assertEqual(theme['mAllResearchCompleted'], 1)
        self.assertEqual(out['settings']['SFXVolume'], (b'\x00\x00\x00\x01', 50))
        self.assertEqual(out['mystery'], {7})

    def test_version_gate_is_unsigned_and_has_no_layout_branch(self):
        self.assertEqual(player_file.read_mac_player_file(player_file_bytes(version=11))['failed_at'], 'version')
        self.assertEqual(player_file.read_mac_player_file(player_file_bytes(version=0))['failed_at'], 'version')
        for version in (13, -1):  # -1 is 0xFFFFFFFF unsigned
            self.assertTrue(player_file.read_mac_player_file(player_file_bytes(version=version))['ok'])

    def test_truncation_keeps_members_read_before_the_failure(self):
        raw = player_file_bytes(keys=5, easy=1)
        # Cut inside mEasyModeUser: keys read, mode falls back to the reset value (Full Simulation).
        out = player_file.read_mac_player_file(raw[:4 + 6 + 8])
        self.assertFalse(out['ok'])
        self.assertEqual(out['failed_at'], 'mEasyModeUser')
        self.assertEqual((out['record']['mExtraKeys'], out['record']['mEasyModeUser']), (5, 0))
        self.assertEqual(out['record']['mFirstTimePlayer'], 1)
        # Cut after the mode byte: the Instant Action flag survives.
        out = player_file.read_mac_player_file(raw[:4 + 6 + 8 + 1])
        self.assertEqual((out['failed_at'], out['record']['mEasyModeUser']), ('mSwearFilterOn', 1))
        # Cut inside the settings block: the theme is in, part of the settings already overwritten.
        settings = len(raw) - 39 - 4 - 2  # settings, mystery count, one rideId
        out = player_file.read_mac_player_file(raw[:settings + 3 * 8 + 4])
        self.assertIn(b'jungle', out['themes'])
        self.assertEqual(sorted(out['settings']), ['MusicVolume', 'SFXVolume', 'SpeechVolume'])
        self.assertEqual(out['failed_at'], 'MovieVolume')

    def test_duplicate_theme_and_negative_counts(self):
        out = player_file.read_mac_player_file(player_file_bytes(themes=((b'space', 1), (b'space', 0))))
        self.assertEqual(out['failed_at'], 'duplicate theme')
        self.assertEqual(out['themes'][b'space']['mEarnedLocalTicket[i]'][0], 1)  # the first stays
        raw = bytearray(player_file_bytes(themes=()))
        struct.pack_into('<i', raw, 4 + 6 + 8 + 3, -2)  # signed loop: no themes
        self.assertTrue(player_file.read_mac_player_file(bytes(raw))['ok'])

    def test_register_source_scan(self):
        e = fixture([d_word(14, 6, 0, 1), d_word(14, 29, 6, 0), d_word(36, 6, 1, 8), d_word(47, 22, 1, -40)])
        self.assertEqual(player_file.source_reads(e, 0, 16, 6), [4, 8])


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
        self.assertEqual(result['player_window']['window_class'], 'InterfaceWindow')
        self.assertEqual(result['cell_types']['guest_stat_cells'], [0, 1, 3, 9, 10])
        self.assertEqual(result['guest_stats']['staff_class_bytes']['researcher'], 8)
        self.assertEqual(result['instant_action_ui']['loan_button_control_ids'],
                         {'0x14eeec': 324524, '0x15161c': 74367, '0x1698e8': 733})
        self.assertTrue(result['mystery_items']['purchase_result_ignored_by_placement'])
        self.assertEqual(result['park_entry']['easymode_readers'], ['0x137608 (creation-time copy, easy flag only)'])

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


    def _mutated(self, offset, word):
        container = evidence.load_identified(Path(os.environ['OPENTPW_MAC_BIN']) / 'SimThemePark.data')
        e = Evidence(container)
        code = bytearray(e.code)
        struct.pack_into('>I', code, offset, word)  # in memory only
        e.code = bytes(code)
        return e

    def test_progression_witnesses_reject_mutations(self):
        # Award no longer closes the player window with message 4.
        with self.assertRaisesRegex(pef.PEFError, 'code:0x15cd7c'):
            progression.player_window(self._mutated(0x15cd7c, d_word(14, 4, 0, 5)))
        # A hidden store through the award's player-window register.
        with self.assertRaisesRegex(pef.PEFError, 'store through r26'):
            progression.player_window(self._mutated(0x15ce88, d_word(36, 0, 26, 0)))
        # A second writer of the "own all land" secret byte.
        with self.assertRaisesRegex(pef.PEFError, 'secret ticket byte stores'):
            progression.secrets_and_research(self._mutated(0x128d94, d_word(38, 0, 3, 39)))
        # Guest-cell predicate value changed (entrance 9 -> 8).
        with self.assertRaisesRegex(pef.PEFError, 'code:0x851d4'):
            progression.cell_types(self._mutated(0x851d4, d_word(11, 0, 0, 8)))
        # Mystery placement debits money unconditionally.
        with self.assertRaisesRegex(pef.PEFError, 'code:0xdacd0'):
            progression.mystery_items(self._mutated(0xdacd0, 18 << 26 | 0x30))

    def test_park_entry_witnesses_reject_mutations(self):
        container = evidence.load_identified(Path(os.environ['OPENTPW_MAC_BIN']) / 'SimThemePark.data')
        result = park_entry.park_entry(Evidence(container))
        self.assertEqual(result['resume_callers'], ['0x1c2024'])
        self.assertEqual(result['names']['autosave'], 'autosave')
        # Resume no longer requires the startup-save flag to be clear.
        with self.assertRaisesRegex(pef.PEFError, 'code:0x1c1f60'):
            park_entry.park_entry(self._mutated(0x1c1f60, 18 << 26 | 0xc8))
        # A second writer of the startup-save +1028 flag inside the main loop.
        with self.assertRaisesRegex(pef.PEFError, r'\+1028 stores'):
            park_entry.park_entry(self._mutated(0x1c1f44, d_word(36, 0, 3, 1028)))
        # The newest-save search loads even when nothing was found.
        with self.assertRaisesRegex(pef.PEFError, 'code:0x1990b0'):
            park_entry.park_entry(self._mutated(0x1990b0, 0x60000000))
        # The easymode copy runs without the easy flag.
        with self.assertRaisesRegex(pef.PEFError, 'code:0x1376b4'):
            park_entry.park_entry(self._mutated(0x1376b4, 0x60000000))


    def test_profile_witnesses_reject_mutations(self):
        container = evidence.load_identified(Path(os.environ['OPENTPW_MAC_BIN']) / 'SimThemePark.data')
        result = profile.inspect_profiles(Evidence(container))
        self.assertEqual(result['profile_paths']['player_info_file'], '<player directory>:gms.dat')
        self.assertEqual(result['profile_info_file']['version_written'], 12)
        self.assertFalse(result['profile_scan']['load_result_used'])
        self.assertEqual(result['profile_select']['delete_callers'], ['0x15bedc'])
        # The scan tests the gms.dat load result.
        with self.assertRaisesRegex(pef.PEFError, 'code:0x136d28'):
            profile.scan(self._mutated(0x136d28, d_word(11, 0, 3, 0)))
        # A slot digit of 5 is accepted.
        with self.assertRaisesRegex(pef.PEFError, 'code:0x136c04'):
            profile.scan(self._mutated(0x136c04, d_word(11, 0, 25, 5)))
        # gms.dat version 11 written.
        with self.assertRaisesRegex(pef.PEFError, 'code:0x128fc0'):
            profile.info_file(self._mutated(0x128fc0, d_word(14, 0, 0, 11)))
        # An empty player name is accepted.
        with self.assertRaisesRegex(pef.PEFError, 'code:0x15cf8c'):
            profile.create(self._mutated(0x15cf8c, 0x60000000))
        # The key-gate refusal shows something (a call in the epilogue path).
        with self.assertRaisesRegex(pef.PEFError, 'call in refusal path'):
            profile.key_gate(self._mutated(0x96610, 18 << 26 | 0x101))
        # Cost equal to keys refused (bgt -> bge).
        with self.assertRaisesRegex(pef.PEFError, 'code:0x965e0'):
            profile.key_gate(self._mutated(0x965e0, 16 << 26 | 4 << 21 | 0 << 16 | 0x30))
        # The resume accepts a newer save version.
        with self.assertRaisesRegex(pef.PEFError, 'code:0x11ae64'):
            profile.header_gate(self._mutated(0x11ae64, d_word(11, 0, 0, 600)))
        # A second current-slot writer in the load routine.
        with self.assertRaisesRegex(pef.PEFError, 'current-slot stores'):
            profile.select_and_unload(self._mutated(0x137a44, d_word(36, 0, 27, 96)))

    def test_player_file_witnesses_reject_mutations(self):
        container = evidence.load_identified(Path(os.environ['OPENTPW_MAC_BIN']) / 'SimThemePark.data')
        result = player_file.inspect_player_file(Evidence(container))
        self.assertEqual(result['player_file_schema']['theme_record_bytes'], 160)
        self.assertEqual(result['player_file_schema']['settings_bytes'], 39)
        self.assertEqual(result['saved_profile_flags']['mSwearFilterOn']['writers'], ['0x1292ec', '0x1af8c8', '0x1af944'])
        # Signed version compare (cmplwi -> cmpwi).
        with self.assertRaisesRegex(pef.PEFError, 'code:0x129108'):
            player_file.version_gate(self._mutated(0x129108, d_word(11, 0, 6, 12)))
        # A sub-serializer starts reading the version argument.
        with self.assertRaisesRegex(pef.PEFError, 'version argument users'):
            player_file.version_gate(self._mutated(0x12a0e0, d_word(11, 0, 6, 13)))
        # The int reader swaps even after a failed read.
        with self.assertRaisesRegex(pef.PEFError, 'code:0xc3b0'):
            player_file.io_helpers(self._mutated(0xc3b0, 18 << 26 | 0x24))
        # mExtraKeys moved to the other side of mEasyModeUser (record offset 36).
        with self.assertRaisesRegex(pef.PEFError, 'code:0x1293cc'):
            player_file.player_record(self._mutated(0x1293cc, d_word(14, 4, 26, 36)))
        # The award saves and then uses the write result.
        with self.assertRaisesRegex(pef.PEFError, 'code:0x15ceb8'):
            player_file.key_award(self._mutated(0x15ceb8, d_word(11, 0, 3, 0)))
        # Keys() divides unsigned (mulhw -> mulhwu).
        with self.assertRaisesRegex(pef.PEFError, 'code:0x128c7c'):
            player_file.keys_source(self._mutated(0x128c7c, 31 << 26 | 3 << 21 | 0 << 16 | 5 << 11 | 11 << 1))
        # The first-time clear writes the player file.
        with self.assertRaisesRegex(pef.PEFError, 'player file written'):
            player_file.saved_flags(self._mutated(0x1c20e8, 18 << 26 | ((0x137dbc - 0x1c20e8) & 0x3fffffc) | 1))

if __name__ == '__main__':
    unittest.main()
