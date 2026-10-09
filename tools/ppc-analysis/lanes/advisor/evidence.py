"""Identity-pinned advisor/audio witnesses. Never execute original instructions.

The selected code was manually reviewed with local llvm-mc. This helper checks
relocations, interpreted operands, range identities and supplied asset metadata;
it is not a general decompiler or proof of original runtime presentation.
"""
from __future__ import annotations

import argparse
from collections import Counter
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef
from timer_evidence import call_target, d_fields, glue_import, require, vector

IDENTITIES = {
    'SimThemePark.data': '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5',
    'sound_shared.data': '7132c2f1d772de25b458b9c6e0303e130e6c536d7650a9d2cde5c8cacd6bb94f',
}
ASSET_IDENTITIES = {
    'global/Speech/lips.wad': 'f86d74c4b4356aeaa0e6ed2feb00e80450a9a643a37c7d108e0c2d65c19cc9e1',
    'global/Speech/speechHD.SDT': '61e2d6a34c7eecb4569bcd8495c4515227f4404a5c787bd0181e2928f2b79fe8',
    'global/sound/UIHD.sdt': '565fa2aa98935268b03db3dbc3f903d2f65d48005241691b0172414f60372cb7',
    'global/sound/MusicHD.sdt': '36c035524e8c5b5c45ee7f4740ee4f4e0aaee38531ade4126e17078cc054b354',
    'Advisor/Advisor.sam': 'e905df3d0a12ea5d7580ce87a7dd3a6c7e690e2f072b9eafef8cbfdc88f00df3',
    'levels/fantasy/Speech/lips/sp_001.LIP': '5b9010fbf818327d5de682626787786a869b54206ea8bedd2d188c5bc2cdd1b7',
    'levels/hallow/Speech/lips/sp_001.LIP': 'd6be53d0ed0e1f6d2de96983dcc24e62d1980ee72b6042b51e3198551898bec2',
    'levels/jungle/Speech/lips/sp_001.LIP': 'f3455533f1c2c3d214484a40c3a0b712cbbb97feb06ee877dd0d860e20c2ce2b',
    'levels/space/Speech/lips/sp_001.LIP': 'd7414e592f1b7f5a8e701018375851ed2b8a169e6de9764c041e9592b2702e6a',
}


def identified(path: Path, expected: str) -> bytes:
    raw = path.read_bytes()
    require(hashlib.sha256(raw).hexdigest(), expected, f'identity {path.name}')
    return raw


def data_pointer(c, slot: int, section: int) -> int:
    target = c.relocs[c.data_section.index].get(slot)
    if target is None or target.kind != 'section' or target.target != section:
        raise pef.PEFError(f'section pointer missing at data:{slot:#x}')
    return target.addend


def toc_pointer(c, at: int) -> int:
    _, base, offset = d_fields(c, at, 32)
    require(base, 2, f'TOC base at {at:#x}')
    return data_pointer(c, 0x8000 + offset, c.data_section.index)


def x_fields(c, at: int, operation: int) -> tuple[int, int, int]:
    word = pef._u32(c.code.data, at)
    require((word >> 26, word >> 1 & 1023), (31, operation), f'X operation {at:#x}')
    return word >> 21 & 31, word >> 16 & 31, word >> 11 & 31


def rotate_fields(c, at: int) -> tuple[int, int, int, int, int]:
    word = pef._u32(c.code.data, at)
    require(word >> 26, 21, f'rotate operation {at:#x}')
    return word >> 21 & 31, word >> 16 & 31, word >> 11 & 31, word >> 6 & 31, word >> 1 & 31


def conditional_branch(c, at: int) -> tuple[int, int, int]:
    word = pef._u32(c.code.data, at)
    require(word >> 26, 16, f'conditional branch {at:#x}')
    displacement = word & 0xFFFC
    if displacement & 0x8000:
        displacement -= 0x10000
    return word >> 21 & 31, word >> 16 & 31, at + displacement


def imported_toc(c, at: int) -> str:
    _, base, offset = d_fields(c, at, 32)
    require(base, 2, f'import TOC base {at:#x}')
    target = c.relocs[c.data_section.index].get(0x8000 + offset)
    if not target or target.kind != 'import':
        raise pef.PEFError('selected command is not imported')
    return c.imports[target.target].name


def priority_slot(scores: tuple[int | None, ...], highest: bool) -> int:
    """Derived queue scan model: ignore invalid slots, retain first equal score."""
    chosen = -1
    for index, value in enumerate(scores):
        if value is None:
            continue
        if chosen == -1 or (value > scores[chosen] if highest else value < scores[chosen]):
            chosen = index
    return chosen


def cstring(c, address: int) -> str:
    raw = c.code.data
    if not 0 <= address < len(raw):
        raise pef.PEFError('string outside code section')
    end = raw.find(b'\0', address, min(len(raw), address + 256))
    if end < 0:
        raise pef.PEFError('unterminated selected string')
    return raw[address:end].decode('mac_roman')


def signed_mark_ms(word: int) -> int:
    """Algebraic model of the reviewed multiply-high / shift / sign correction."""
    value = word if word < 0x80000000 else word - 0x100000000
    quotient = ((value * 0x10624DD3) >> 32) >> 6
    return quotient + (1 if quotient < 0 else 0)


@dataclass
class LipCursor:
    """Small derived witness model; does not execute any original instructions."""
    words: tuple[int, ...]
    start_ms: int = 0
    index: int = 1
    talking: bool = True
    active: bool = True

    def __post_init__(self):
        if not self.words or self.words[-1] != 0xFFFFFFFF:
            raise ValueError('model requires terminated marks')
        self.deadline = self.start_ms + signed_mark_ms(self.words[0])

    def update(self, now: int):
        if self.active and now > self.deadline:
            self.talking = not self.talking
            word = self.words[self.index]
            self.index += 1
            if word == 0xFFFFFFFF:
                self.active = self.talking = False
                self.deadline = -1
            else:
                self.deadline = self.start_ms + signed_mark_ms(word)
        return self.talking


def inspect_binary(root: Path) -> dict:
    app = pef.PEFContainer(identified(root / 'SimThemePark.data', IDENTITIES['SimThemePark.data']))
    sound = pef.PEFContainer(identified(root / 'libraries/sound_shared.data', IDENTITIES['sound_shared.data']))
    require(toc_pointer(app, 0x6ba0), 0x53844, 'advisor presentation state')
    response_table = toc_pointer(app, 0x6b94)
    require(response_table, 0x18ff4, 'response table')
    records = []
    for index in range(1000):
        record = struct.unpack_from('>8i', app.data_section.data, response_table + index * 32)
        if record[0] == 9999:
            break
        records.append(record)
    else:
        raise pef.PEFError('response table sentinel missing')
    require(len(records), 610, 'response record count')
    locals_ = [r for r in records if r[4] >> 16]
    require([r[0] for r in locals_], [1, 399, 400, 401, 402], 'local response IDs')
    require(d_fields(app, 0x6c50, 32), (19, 5, 8), 'LIP stem selector from response record')
    require(d_fields(app, 0x6c3c, 32), (0, 5, 4), 'audio sample ID from response record')
    require(cstring(app, 0x1c8182), ':Speech:lips:sp_%03d.lip', 'LIP path format')
    require(cstring(app, 0x1c819b), 'Data:Global', 'global speech root')
    require(call_target(app, 0x6d30), 0x115f38, 'level speech root selector')
    require((d_fields(app, 0x6e8c, 32), d_fields(app, 0x6e94, 32)),
            ((4, 14, 20), (4, 14, 32)), 'global/local bank selectors')
    require(call_target(app, 0x6eac), 0xbb4fc, 'speech event wrapper')
    require(call_target(app, 0x6ebc), 0xbbfc0, 'sample duration wrapper')
    require(call_target(app, 0x6e4c), 0x1c5d0c, 'LIP endian conversion glue')
    require(glue_import(app, 0x1c5d0c, 0x8000)['symbol'],
            '_USwapBlock32__13SamsUtilitiesFPUll', 'LIP endian conversion import')
    for address in (0x6fe4, 0x770c):
        require(d_fields(app, address, 15), (3, 0, 4194), 'LIP division high literal')
        require(d_fields(app, address + 4, 14), (0, 3, 19923), 'LIP division low literal')
        require(x_fields(app, address + 8, 75), (0, 0, 4), 'LIP multiply-high operands')
        # srawi has immediate shift in the third X field.
        require(x_fields(app, address + 12, 824), (0, 0, 6), 'LIP quotient shift')
        require(rotate_fields(app, address + 16), (0, 3, 1, 31, 31), 'LIP sign correction')
        require(x_fields(app, address + 20, 266), (4 if address == 0x6fe4 else 0, 0, 3),
                'LIP corrected quotient result')
    require(d_fields(app, 0x6e74, 36), (0, 27, 0), 'initial talking flag set')
    require(d_fields(app, 0x6e68, 14), (0, 0, 1), 'initial talking literal')
    require(x_fields(app, 0x76d0, 26), (0, 0, 0), 'talking boolean inversion')
    require(rotate_fields(app, 0x76d4), (0, 0, 27, 24, 31), 'talking inversion normalization')
    require(conditional_branch(app, 0x76c4), (4, 1, 0x7758), 'skip LIP toggle unless now is greater')
    require(conditional_branch(app, 0x777c), (4, 1, 0x77b4), 'skip mouth change unless now is greater')
    require(d_fields(app, 0x76d8, 36), (0, 31, 16), 'talking state store')
    require(d_fields(app, 0x7734, 11), (0, 3, -1), 'LIP end sentinel compare')
    require(d_fields(app, 0x7740, 36), (0, 9, 0), 'clear LIP active on terminator')
    require(d_fields(app, 0x7744, 36), (0, 5, 0), 'clear talking on terminator')
    require(call_target(app, 0x7454), 0x10e864, 'advisor clock consumer')
    require(call_target(app, 0x10e874), 0x11a428, 'advisor clock wrapper')
    require(call_target(app, 0x11a43c), 0x117c00, 'elapsed clock wrapper')
    require(call_target(app, 0x117c24), 0x10edb4, 'elapsed clock source')
    require(glue_import(app, call_target(app, 0x10edc8), 0x8000)['symbol'],
            'LbTime_GetClock__Fv', 'actual LIP clock source import')
    require(d_fields(app, 0x6cf0, 14), (0, 3, -200), 'initial clock bias')
    require(d_fields(app, 0x6d7c, 14), (0, 6, 1000), 'speech pending deadline offset')
    require(d_fields(app, 0x779c, 14), (3, 3, 1), 'mouth selector one-based')
    require(d_fields(app, 0x778c, 14), (0, 29, 100), 'next mouth selection interval')
    require(x_fields(app, 0x7790, 491), (4, 3, 5), 'random mouth quotient')
    require(d_fields(app, 0x7790 - 8, 32), (5, 31, 272), 'mouth-count input')
    require(d_fields(app, 0x6294, 36), (0, 28, 272), 'mouth count store')
    require(d_fields(app, 0x628c, 14), (0, 0, 5), 'mouth count literal')
    mouth_names = ['mouth - normal', 'mouth - aah', 'mouth - eee', 'mouth - ooh', 'mouth - sss']
    for address, name in zip((0x1db98b, 0x1db99a, 0x1db9a6, 0x1db9b2, 0x1db9be), mouth_names):
        require(cstring(app, address), name, 'mouth identity')
    require(call_target(app, 0x62d4), 0x19b394, 'mouth lookup consumer')
    require(d_fields(app, 0x7810, 24), (0, 0, 16), 'hide former mouth mesh flag')
    require(rotate_fields(app, 0x785c), (0, 0, 0, 28, 26), 'clear selected mouth hidden flag')
    require(call_target(app, 0x6350), 0xa7e74, 'animation duration lookup')
    require(call_target(app, 0x29424), 0xa6cc0, 'advisor animation start')
    require(call_target(app, 0x7664), 0xa6cc0, 'advisor animation continuation')
    subscriptions = [(0x8090, 5), (0x80e0, 17), (0x8130, 19), (0x8180, 23),
                     (0x81d0, 14), (0x8220, 8), (0x8270, 25)]
    for address, type_id in subscriptions:
        require(d_fields(app, address, 14), (5, 0, type_id), 'advisor subscribed message ID')
    identities = [(5, 'CMsgAdvisor', 0xd4434), (17, 'CMsgResearchCompleted', 0xf26b8),
                  (19, 'CMsgEvent', 0xcd0ac), (23, 'CMsgStaffInfo', 0xf8e40),
                  (14, 'CMsgPrankery', 0xefdb8), (8, 'CMsgRideCondemned', 0xe30e4),
                  (25, 'CMsgChallenge', 0xd1218)]
    rtti_tables = {5: 0x40ca8, 17: 0x4141c, 19: 0x408a4, 23: 0x41500,
                   14: 0x413e0, 8: 0x40f58, 25: 0x40bd8}
    for type_id, name, address in identities:
        table = rtti_tables[type_id]
        info = data_pointer(app, table, app.data_section.index)
        require(cstring(app, data_pointer(app, info, app.code.index)), name, 'message RTTI identity')
        transition = data_pointer(app, table + 8, app.data_section.index)
        require(data_pointer(app, transition, app.code.index), address, 'message RTTI getter slot')
        require(d_fields(app, address, 14), (3, 0, type_id), 'RTTI-bound message type getter')
    dispatch = toc_pointer(app, 0xa278)
    require(dispatch, 0x1e120, 'ReceiveMessage dispatch table')
    dispatches = {type_id: data_pointer(app, dispatch + (type_id - 5) * 4, app.code.index)
                  for type_id, _, _ in identities}
    require(d_fields(app, 0x93f8, 11), (0, 27, 8), 'lowest-score scan queue capacity')
    require(d_fields(app, 0x94bc, 11), (0, 27, 8), 'highest-score scan queue capacity')
    require(conditional_branch(app, 0x93d0), (4, 0, 0x93f0), 'lowest-score ties retain first slot')
    require(conditional_branch(app, 0x9494), (4, 1, 0x94b4), 'highest-score ties retain first slot')
    require(conditional_branch(app, 0x8cd8), (4, 0, 0x8d10), 'replace weakest only for higher incoming score')
    commands = {address: imported_toc(app, address) for address in
                (0xbb1dc, 0xbb4c8, 0xbb19c, 0xbb3f0, 0xbb424, 0xbb458, 0xbb48c, 0xbc008)}
    require(commands[0xbb1dc], '__vt__Q212TbSysCommand4Tick', 'audio update Tick command')
    require(commands[0xbb4c8], '__vt__Q212TbSysCommand7Process', 'audio update Process command')
    require(commands[0xbc008], '__vt__Q212TbSysCommand15GetSampleLength', 'sample length command')
    sound_symbols = [
        'Decode__9CMpegBaseFPs', 'Decode_Layer1__9CMpegBaseFPs', 'Decode_Layer2__9CMpegBaseFPs',
        'SoundCallback__20CPlaceHolderSentenceFPvPv',
        'Execute__Q212TbSysCommand15GetSampleLengthCFPC11TbAudioBody',
        'Poll__13CServiceQueueFv', 'Execute__Q212TbSysCommand4TickCFPC11TbAudioBody',
    ]
    exported = [vector(sound, name) for name in sound_symbols]
    require(call_target(sound, 0x1c64), 0x2d0c, 'Layer I decode dispatch')
    require(call_target(sound, 0x1c78), 0x3ee4, 'Layer II decode dispatch')
    ranges = [(0x6264, 0x6388), (0x6b7c, 0x7068), (0x7434, 0x78c8),
              (0x8028, 0x82a4), (0x9350, 0x94dc), (0xa228, 0xb678),
              (0x19b394, 0x19b504), (0x29084, 0x2925c)]
    witnesses = {f'{lo:#x}-{hi:#x}': hashlib.sha256(app.code.data[lo:hi]).hexdigest()
                 for lo, hi in ranges}
    return {'identities': IDENTITIES, 'section_addresses': 'code=0, data=1; application TOC=0x8000',
            'selected_code_witnesses_sha256': witnesses,
            'responses': {'table': response_table, 'count': len(records), 'stride': 32,
                          'local_response_ids': [r[0] for r in locals_], 'local_lip_stem': 'sp_001',
                          'sentinel': 9999},
            'message_subscription': [{'id': id_, 'identity': name, 'getter': getter,
                                      'dispatch': dispatches[id_]} for id_, name, getter in identities],
            'pending_slots': 8, 'priority_ties': 'earliest valid slot',
            'audio_command_imports': {hex(k): v for k, v in commands.items()},
            'lip_conversion_divisor': 1000,
            'lip_clock_import': 'LbTime_GetClock__Fv', 'mouth_names': mouth_names,
            'mouth_selection_interval_clock_units': 100, 'sound_export_vectors': exported,
            'limitations': ['Static Mac evidence; no original execution or capture.',
                            'Microsecond unit remains conditional on the LbTime/UTimer route.',
                            'Selected operand checks plus manual control-flow review; not formal proof.']}


def lip_words(raw: bytes) -> tuple[int, ...]:
    if not 8 <= len(raw) <= 65536 or len(raw) % 8:
        raise pef.PEFError('invalid selected LIP length')
    words = struct.unpack('<' + 'I' * (len(raw) // 4), raw)
    if words[-1] != 0xFFFFFFFF or any(x == 0xFFFFFFFF for x in words[:-1]):
        raise pef.PEFError('invalid selected LIP terminator')
    if any(a >= b for a, b in zip(words[:-2], words[1:-1])):
        raise pef.PEFError('non-increasing selected LIP marks')
    return words


def wad_members(raw: bytes) -> dict[str, bytes]:
    if len(raw) < 88 or raw[:4] != b'DWFB':
        raise pef.PEFError('invalid selected WAD header')
    count = struct.unpack_from('<I', raw, 72)[0]
    if count > 65536 or 88 + count * 40 > len(raw):
        raise pef.PEFError('selected WAD directory exceeds file')
    result = {}
    for i in range(count):
        _, noff, nlen, off, size, compression, expanded = struct.unpack_from('<7I', raw, 88 + 40 * i)
        if not nlen or noff + nlen > len(raw) or off + size > len(raw):
            raise pef.PEFError('selected WAD member exceeds file')
        if compression != 0 or expanded not in (0, size) or raw[noff + nlen - 1] != 0:
            raise pef.PEFError('selected WAD requires uncompressed terminated names')
        name = raw[noff:noff + nlen - 1].decode('ascii')
        if name in result:
            raise pef.PEFError('duplicate selected WAD member')
        result[name] = raw[off:off + size]
    return result


def sdt_entries(raw: bytes) -> list[dict]:
    if len(raw) < 4:
        raise pef.PEFError('short SDT')
    count = struct.unpack_from('<I', raw)[0]
    if count > 65536 or 4 + count * 4 > len(raw):
        raise pef.PEFError('SDT directory exceeds file')
    result = []
    for index in range(count):
        off = struct.unpack_from('<I', raw, 4 + index * 4)[0]
        if off < 4 + count * 4 or off + 40 > len(raw):
            raise pef.PEFError('selected SDT offset exceeds file')
        header, size = struct.unpack_from('<II', raw, off)
        if header < 40 or size < 4 or off + header + size > len(raw):
            raise pef.PEFError('selected SDT payload exceeds file')
        name = raw[off + 8:off + 24].split(b'\0')[0].decode('ascii')
        word = struct.unpack_from('>I', raw, off + header)[0]
        if word >> 21 != 0x7FF:
            raise pef.PEFError('selected SDT MPEG first frame has no sync')
        result.append({'number': index + 1, 'name': name, 'offset': off,
                       'header_size': header, 'data_size': size,
                       'mpeg_version_id': word >> 19 & 3, 'layer': 4 - (word >> 17 & 3),
                       'channel_mode': word >> 6 & 3})
    return result


def inspect_assets(root: Path) -> dict:
    raw = {name: identified(root / name, digest) for name, digest in ASSET_IDENTITIES.items()}
    members = wad_members(raw['global/Speech/lips.wad'])
    require(len(members), 639, 'global LIP members')
    parsed = {name: lip_words(data) for name, data in members.items()}
    require(parsed['sp_001.LIP'], (2226893, 2812380, 4058820, 0xFFFFFFFF), 'selected actual LIP marks')
    entries = sdt_entries(raw['global/Speech/speechHD.SDT'])
    require(len(entries), 641, 'global SDT count')
    counts = Counter(e['layer'] for e in entries)
    require(dict(counts), {2: 640, 1: 1}, 'global first-frame layer inventory')
    require(entries[637]['name'], 'z_error.mp2', 'Layer I speech identity')
    audio_banks = {}
    for name in ('global/sound/UIHD.sdt', 'global/sound/MusicHD.sdt'):
        bank = sdt_entries(raw[name])
        audio_banks[name] = {'entries': len(bank), 'first_frame_layers': dict(Counter(e['layer'] for e in bank))}
    require(audio_banks['global/sound/UIHD.sdt']['first_frame_layers'], {1: 32}, 'UI Layer I inventory')
    require(audio_banks['global/sound/MusicHD.sdt']['first_frame_layers'], {2: 2}, 'music Layer II inventory')
    levels = {}
    for name, data in raw.items():
        if name.endswith('.LIP'):
            words = lip_words(data)
            matches = [stem for stem, candidate in members.items() if candidate == data]
            levels[name.split('/')[1]] = {'marks': len(words) - 1,
                'first_mark_ms': signed_mark_ms(words[0]), 'same_as_global': matches}
    return {'identities': ASSET_IDENTITIES, 'global_lip_count': len(parsed),
            'global_speech_count': len(entries), 'first_frame_layers': dict(counts),
            'selected_entries': [entries[i] for i in (0, 637, 638, 639, 640)],
            'global_sp_001_deadlines_ms_from_start': [signed_mark_ms(w) for w in parsed['sp_001.LIP'][:-1]],
            'levels': levels, 'audio_banks': audio_banks, 'limitation': 'Windows baseline assets identify supplied data, not Mac or Patch2 runtime equivalence.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--asset-root', type=Path)
    args = parser.parse_args()
    try:
        result = {'binary': inspect_binary(args.bin_root)}
        if args.asset_root:
            result['assets'] = inspect_assets(args.asset_root)
    except (OSError, ValueError, struct.error, pef.PEFError) as error:
        parser.exit(1, f'advisor evidence: {error}\n')
    print(json.dumps(result, sort_keys=True, indent=2))


if __name__ == '__main__':
    main()
