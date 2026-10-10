"""Shared clock epoch serialization/re-alignment; metadata, not native execution."""
import argparse
import hashlib
import json
from pathlib import Path
import struct

import clock_evidence as evidence
import clock_edges as edges
import timer_evidence as timer
import pef
import save_phase_evidence as phase
import saved_script_graph as graph


def inspect(root: Path) -> dict:
    app = timer.load_identified(root / 'SimThemePark.data')
    records = []
    def d(at, op, fields, meaning):
        timer.require(evidence.d_operand(app.code.data, at, op), fields, meaning)
        records.append({'at': hex(at), 'meaning': meaning, 'fields': list(fields)})
    def call(at, target, meaning):
        timer.require(evidence.branch_target(app.code.data, at, True), target, meaning)
        records.append({'at': hex(at), 'meaning': meaning, 'target': hex(target)})
    for at, op, fields, meaning in [
        (0x10ea04, 36, (3, 31, 64), 'capture saves adjusted scaled clock word'),
        (0x10ea08, 14, (3, 31, 68), 'capture selects unscaled embedded clock'),
        (0x10ea10, 36, (3, 31, 96), 'capture saves adjusted unscaled clock word'),
        (0x11a4a4, 32, (0, 3, 64), 'scaled writer reads saved epoch word'),
        (0x11a498, 14, (6, 0, 4), 'scaled writer outputs four bytes'),
        (0x11a528, 14, (5, 31, 64), 'scaled reader targets saved epoch word'),
        (0x11a520, 14, (6, 0, 4), 'scaled reader requests four bytes'),
        (0x11a568, 14, (0, 31, 64), 'scaled reader byte-swap destination'),
        (0x11a564, 32, (5, 31, 64), 'scaled reader byte-swap value'),
        (0x11a5d4, 32, (0, 31, 64), 'scaled alignment reads restored saved word'),
        (0x11a5dc, 36, (0, 31, 60), 'scaled alignment writes outer epoch offset'),
        (0x11a5a0, 32, (0, 31, 60), 'scaled public getter reads outer epoch offset'),
        (0x11a344, 32, (0, 3, 28), 'unscaled writer reads saved epoch word'),
        (0x11a338, 14, (6, 0, 4), 'unscaled writer outputs four bytes'),
        (0x11a3c8, 14, (5, 31, 28), 'unscaled reader targets saved epoch word'),
        (0x11a3c0, 14, (6, 0, 4), 'unscaled reader requests four bytes'),
        (0x11a474, 32, (0, 31, 28), 'unscaled alignment reads restored saved word'),
        (0x11a47c, 36, (0, 31, 24), 'unscaled alignment writes outer epoch offset'),
        (0x11a440, 32, (0, 31, 24), 'unscaled public getter reads outer epoch offset'),
        (0x10e974, 14, (3, 30, 68), 'writer pair unscaled member selection'),
        (0x10e9c8, 14, (3, 30, 68), 'reader pair unscaled member selection'),
        (0x10ea40, 14, (3, 31, 68), 'alignment pair unscaled member selection'),
        (0x10ed84, 32, (0, 31, 56), 'selected scaled base includes transition offset56'),
        (0x10ecf4, 36, (3, 31, 56), 'forced-exit stores continuity offset56'),
        (0x11cf20, 15, (3, 0, 19781), 'SSEM writer marker high'),
        (0x11cf28, 14, (0, 3, 21331), 'SSEM writer marker low'),
        (0x11b9cc, 15, (0, 3, -19781), 'SSEM reader marker comparison high'),
        (0x11b9d0, 10, (0, 0, 21331), 'SSEM reader marker comparison low'),
        (0x11d008, 15, (3, 0, 17228), 'following KOLC writer marker high'),
        (0x11d010, 14, (0, 3, 20299), 'following KOLC writer marker low'),
        (0x11d0e8, 15, (3, 0, 22081), 'following TNAV writer marker high'),
        (0x11d0f0, 14, (0, 3, 20052), 'following TNAV writer marker low'),
        (0x11b3cc, 11, (0, 29, 1), 'post-load numeric selector comparison'),
        (0x11b548, 36, (0, 4, 0), 'post-load sets outer scheduler reset request before alignment'),
        (0x1c3540, 36, (3, 4, 0), 'requested reset stores current aligned clock as previous'),
        (0x1c3570, 36, (0, 4, 0), 'requested reset clears outer substep phase'),
    ]:
        d(at, op, fields, meaning)
    for at, target, meaning in [
        (0x10e960, 0x11a494, 'pair writer writes scaled word first'),
        (0x10e97c, 0x11a334, 'pair writer then writes unscaled word'),
        (0x10e9b4, 0x11a514, 'pair reader reads scaled word first'),
        (0x10e9d0, 0x11a3b4, 'pair reader then reads unscaled word'),
        (0x10ea00, 0x11a588, 'capture queries adjusted scaled clock'),
        (0x10ea0c, 0x11a428, 'capture queries adjusted unscaled clock'),
        (0x10ea3c, 0x11a5bc, 'pair alignment aligns scaled offset'),
        (0x10ea44, 0x11a45c, 'pair alignment aligns unscaled offset'),
        (0x11a5d0, 0x10ed54, 'scaled alignment samples base excluding outer epoch offset'),
        (0x11a470, 0x117c00, 'unscaled alignment samples base excluding outer epoch offset'),
        (0x11a59c, 0x10ed54, 'scaled public getter samples selected base'),
        (0x11a43c, 0x117c00, 'unscaled public getter samples pause-aware base'),
        (0x11cfe8, 0x10e944, 'state writer reaches clock pair'),
        (0x11b9f4, 0x10e998, 'state reader reaches clock pair'),
        (0x11d0c8, 0x127ac8, 'KOLC writer serializes separate raw-clock continuity record'),
        (0x11bac0, 0x127b64, 'KOLC reader loads separate raw-clock continuity record'),
        (0x127adc, 0x1c4d64, 'KOLC writer samples raw LbTime clock'),
        (0x127bcc, 0x1c4d64, 'KOLC reader samples current raw LbTime clock for alignment'),
        (0x11d1a4, 0x1c2e00, 'TNAV writer serializes outer cadence state'),
        (0x11bb88, 0x1c31f4, 'TNAV reader restores outer cadence state'),
        (0x11dbac, 0x10e9ec, 'save preparation captures pair'),
        (0x11a800, 0x11db9c, 'save entry invokes capture hook before state writer'),
        (0x11b54c, 0x10ea28, 'post-load hook applies alignment after reset request'),
        (0x11b3d8, 0x11b4f4, 'numeric selector other than1 invokes post-load hook'),
        (0x1c2274, 0x1c3520, 'next active callback consumes reset request'),
        (0x1c3538, 0x10e844, 'reset samples adjusted shared scaled clock'),
    ]:
        call(at, target, meaning)
    for at, fields, meaning in [
        (0x11a5d8, (31, 0, 3, 0, 40), 'scaled epoch offset is saved minus selected base word'),
        (0x11a478, (31, 0, 3, 0, 40), 'unscaled epoch offset is saved minus pause-aware base word'),
        (0x11a5a4, (31, 3, 0, 3, 266), 'scaled getter wraps base plus epoch offset'),
        (0x11a444, (31, 3, 0, 3, 266), 'unscaled getter wraps base plus epoch offset'),
    ]:
        word = evidence.word_at(app.code.data, at)
        actual = (word >> 26, word >> 21 & 31, word >> 16 & 31,
                  word >> 11 & 31, word >> 1 & 1023)
        timer.require(actual, fields, meaning)
        records.append({'at': hex(at), 'meaning': meaning, 'fields': list(fields)})
    imported = timer.glue_import(app, 0x1c4d64, 0x8000)
    timer.require(imported['symbol'], 'LbTime_GetClock__Fv', 'separate raw-clock import identity')
    word = evidence.word_at(app.code.data, 0x11b3d0)
    branch = {'offset': 0x11b3d0, 'bo': word >> 21 & 31, 'bi': word >> 16 & 31,
              'target': evidence.branch_target(app.code.data, 0x11b3d0)}
    timer.require((word >> 26, branch['bo'], branch['bi'], branch['target']),
                  (16, 12, 2, 0x11b3dc), 'selector1 skips epoch alignment hook')
    hashes = {}
    for start, end, expected in [
        (0x10e9ec, 0x10ea28, '44bd977ce99669e281e05a232658f77423fc438c56c5293cacd2ad48ff5740cd'),
        (0x10ea28, 0x10ea5c, '3bb2380eb95de33b0a83265526891ddeb5d9de743a9b4a4718bef761bf0d0fd6'),
        (0x11a494, 0x11a514, '970d971c3564ce467e4904c6e75e6f8668b8f4787521a32710e97c76ee3cce41'),
        (0x11a514, 0x11a588, '726e1a3e12915353323dc7b1fd4a6e6a00722217b83fbf5a904de5ab62ba9ba7'),
        (0x11a588, 0x11a5bc, '15e715f07f8f024c3201c53eda3eef298fa185eb0bcf0ed68ded856a2b232a1e'),
        (0x11a5bc, 0x11a5f4, '6c3c264c6754700fc891748d7f9faacf2c50e32433f5b6ad3cebaf09dc885c58'),
        (0x11a334, 0x11a3b4, '1d4ff6ee06cb80e0f91c5e1b6574f0f2ab926b46647fb475f7962418345f1091'),
        (0x11a3b4, 0x11a428, '1090eb40cc45fa4854cca7fd74f7f6667d8085a879342a4440bb1e7dec0ecbab'),
        (0x11a428, 0x11a45c, '8b49419abf2c13f220d7a6a844a897fe338d9b1eaeb4c778be3d83fc7a766025'),
        (0x11a45c, 0x11a494, 'd183789e59b32ea9336ad0e820fc080a60c34835b6384624df7135d95245bda4'),
    ]:
        actual = hashlib.sha256(app.code.data[start:end]).hexdigest()
        timer.require(actual, expected, 'shared clock functional region identity')
        hashes[f'{start:#x}..{end:#x}'] = actual
    return {'identity_sha256': timer.IDENTITIES['SimThemePark.data'], 'toc': '0x8000',
            'post_load_selector_comparison': 1, 'post_load_skip_branch': branch,
            'skip_target': 0x11b3dc, 'alignment_hook_call': 0x11b3d8,
            'saved_pair_order': ['scaled', 'unscaled'], 'witnesses': records, 'region_sha256': hashes,
            'limits': 'Word arithmetic only; no base/source/scale/pause restore inferred; selector1 complete lifecycle and Windows execution remain unqualified.'}


def inspect_save(path: Path) -> dict:
    import zlib
    scripts = graph.inspect(path)
    raw = path.read_bytes()
    decoder = zlib.decompressobj()
    payload = decoder.decompress(raw[0x629:], phase.PAYLOAD_LENGTH + 1)
    phase.require(hashlib.sha256(payload).hexdigest(), phase.PAYLOAD_SHA, 'epoch fixture decoded identity')
    phase.require(payload.find(b'SSEM'), 1577444, 'identified epoch-pair marker offset')
    phase.require(payload.find(b'SSEM', 1577445), -1, 'unique epoch-pair marker')
    phase.require(payload[1577456:1577460], b'KOLC', 'following clock-state marker')
    phase.require(payload[1577464:1577468], b'TNAV', 'following outer cadence marker')
    phase.require(struct.unpack_from('<I', payload, 1577460)[0], 114938044, 'identified separate raw-clock word')
    phase.require(struct.unpack_from('<4I', payload, 1577468),
                  (114374806, 114374775, 114374589, 6055), 'identified outer cadence prefix')
    scaled, unscaled = struct.unpack_from('<II', payload, 1577448)
    phase.require((scaled, unscaled), (114374804, 114876286), 'identified adjusted clock pair')
    deadlines = [{'script_id': record['script_id'], 'field': field, 'word': record[field],
                  'signed_modular_distance_from_saved_scaled': edges.signed32(record[field] - scaled)}
                 for record in scripts['records'] for field in ('wait_deadline', 'animation_wait_deadline', 'timer_deadline')
                 if record[field]]
    return {'save_sha256': phase.SAVE_SHA, 'payload_sha256': phase.PAYLOAD_SHA,
            'ssem_offset': 1577444, 'scaled_saved_word_offset': 1577448,
            'unscaled_saved_word_offset': 1577452, 'kolc_offset': 1577456,
            'saved_adjusted_scaled': scaled, 'saved_adjusted_unscaled': unscaled,
            'kolc_raw_clock_word': struct.unpack_from('<I', payload, 1577460)[0],
            'tnav_offset': 1577464,
            'tnav_prefix_three_stamps_and_phase': list(struct.unpack_from('<4I', payload, 1577468)),
            'nonzero_deadlines': deadlines,
            'limits': 'Distances are diagnostic modular arithmetic, not a change to WAIT unsigned comparison or proof of PC execution.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--save', type=Path)
    parser.add_argument('--contract-rules', action='store_true')
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
        if args.save:
            result['pc_save_correspondence'] = inspect_save(args.save)
        if args.contract_rules:
            result = {key: result[key] for key in ('identity_sha256', 'post_load_selector_comparison',
                      'post_load_skip_branch', 'skip_target', 'alignment_hook_call')}
        print(json.dumps(result, indent=2, sort_keys=True))
    except (OSError, ValueError, pef.PEFError) as error:
        parser.exit(1, f'clock epoch evidence: {error}\n')
