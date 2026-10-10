"""Calendar constructor/saved-epoch field separation; no runtime or civil-calendar emulation."""
import argparse
import hashlib
import json
from pathlib import Path
import struct

import clock_evidence as evidence
import timer_evidence as timer
import pef
import save_phase_evidence as phase

MACDOZE_SHA = 'ba11331a70bce77140ae6e7fe73145fe4e13cce5f042707a494cb77654b32f0d'


def calendar_record(payload, offset):
    if offset < 0 or offset + 28 > len(payload):
        raise ValueError('truncated explicit calendar candidate')
    funny, session, month, day, rate = struct.unpack_from('<QQiiI', payload, offset)
    return {'funny_start': funny, 'session_start': session, 'month_cache': month,
            'day_cache': day, 'rate': rate}


def conversion_terms(record, turn):
    """Qualified u32 product/divide input terms; excludes timestamp/civil conversion and overflow after scaling."""
    if not all(isinstance(value, int) for value in (turn, record['rate'], record['funny_start'])):
        raise ValueError('calendar terms require integer register/timestamp values')
    if not 0 <= turn < 1 << 32 or not 0 <= record['rate'] < 1 << 32:
        raise ValueError('turn/rate outside unsigned32 register domain')
    if not 0 <= record['funny_start'] < 1 << 64:
        raise ValueError('funny epoch outside timestamp word domain')
    return {'epoch_units': record['funny_start'], 'added_seconds': turn * record['rate'] // 4}


def inspect(root):
    app = timer.load_identified(root / 'SimThemePark.data')
    bullfrog = timer.load_identified(root / 'libraries/bullfrog_shared.data')
    path = root / 'libraries/macdoze_shared.data'
    raw = path.read_bytes()
    timer.require(hashlib.sha256(raw).hexdigest(), MACDOZE_SHA, 'identified macdoze')
    mac = pef.PEFContainer(raw, path.name)
    rows = []
    def d(obj, at, op, fields, meaning):
        timer.require(evidence.d_operand(obj.code.data, at, op), fields, meaning)
        rows.append({'binary': obj.name, 'at': hex(at), 'meaning': meaning, 'fields': list(fields)})
    def call(obj, at, target, meaning):
        timer.require(evidence.branch_target(obj.code.data, at, True), target, meaning)
        rows.append({'binary': obj.name, 'at': hex(at), 'meaning': meaning, 'target': hex(target)})
    for at, op, fields, meaning in [
        (0x10452c, 14, (3, 31, 672), 'owner constructor selects calendar member+672'),
        (0xe3ca8, 14, (0, 0, 15000), 'calendar default rate literal'),
        (0xe3cc0, 36, (0, 30, 28), 'calendar default rate field+28'),
        (0xe434c, 14, (4, 3, 0), 'fixed epoch receiver is calendar+0'),
        (0xe4358, 14, (5, 0, 2000), 'fixed epoch year2000'),
        (0xe435c, 14, (6, 0, 1), 'fixed epoch month1'),
        (0xe4364, 14, (7, 0, 1), 'fixed epoch day1'),
        (0xe4368, 14, (8, 0, 0), 'fixed epoch hour0'),
        (0xe436c, 14, (9, 0, 0), 'fixed epoch minute0'),
        (0xe4378, 14, (10, 0, 0), 'fixed epoch second0'),
        (0xe4370, 36, (0, 1, 56), 'fixed epoch millisecond0 stack argument'),
        (0xe3cd0, 14, (3, 1, 68), 'host SYSTEMTIME sampled into stack record'),
        (0xe3cec, 14, (4, 30, 8), 'host timestamp receiver is separate session field+8'),
        (0xe3cf4, 40, (5, 1, 68), 'host session year argument'),
        (0xe3cf8, 40, (6, 1, 70), 'host session month argument'),
        (0xe3cfc, 40, (7, 1, 74), 'host session day argument'),
        (0xe3d00, 40, (8, 1, 76), 'host session hour argument'),
        (0xe3d04, 40, (9, 1, 78), 'host session minute argument'),
        (0xe3d08, 40, (10, 1, 80), 'host session second argument'),
        (0xe3ce4, 40, (0, 1, 82), 'host session millisecond field'),
        (0xe3cf0, 36, (0, 1, 56), 'host session millisecond stack argument'),
        (0xe43d8, 32, (7, 22, 28), 'date conversion reads configured rate'),
        (0xe43dc, 32, (4, 3, -22772), 'date conversion reads world turn'),
        (0xe43e8, 14, (6, 0, 4), 'date conversion unsigned64 divisor4'),
        (0xe4430, 14, (4, 22, 0), 'date timestamp addition uses funny field+0'),
        (0xe3d74, 14, (4, 29, 0), 'serializer funny timestamp source+0'),
        (0xe3d98, 14, (4, 29, 8), 'serializer session timestamp source+8'),
        (0xe3e3c, 14, (4, 29, 0), 'reader funny timestamp destination+0'),
        (0xe3e60, 14, (4, 29, 8), 'reader session timestamp destination+8'),
        (0xd6758, 14, (3, 27, 672), 'owner serializer calendar member+672'),
    ]:
        d(app, at, op, fields, meaning)
    for at, target, meaning in [
        (0x104530, 0xe3c90, 'owner constructor reaches calendar constructor'),
        (0xe3cc4, 0xe4348, 'calendar constructor reaches fixed epoch initializer'),
        (0xe3cdc, 0x1c6dd4, 'calendar then samples host local civil time'),
        (0xe3d0c, 0x1c5e2c, 'session receiver calls timestamp setter'),
        (0xe437c, 0x1c5e2c, 'fixed receiver calls timestamp setter'),
        (0xe43d0, 0x10a9a4, 'date conversion queries world getter'),
        (0xe4404, 0x1c4100, 'date conversion divides unsigned64 product by4'),
        (0xe443c, 0x1c5c34, 'date conversion adds time difference to funny epoch'),
        (0xd675c, 0xe3d30, 'owner reaches calendar read/write routine'),
    ]:
        call(app, at, target, meaning)
    for at, symbol, library in [
        (0x1c6dd4, 'GetLocalTime__10NS_MacDozeFPQ210NS_MacDoze11_SYSTEMTIME', 'macdoze shared'),
        (0x1c5e2c, 'SetTime__11TbTimeStampFiiiiiii', 'bullfrog shared'),
        (0x1c5c34, '__pl__11TbTimeStampCFRC10TbTimeDiff', 'bullfrog shared'),
    ]:
        imported = timer.glue_import(app, at, 0x8000)
        timer.require((imported['symbol'], imported['library']), (symbol, library), 'calendar import identity')
    vector = timer.vector(mac, 'GetLocalTime__10NS_MacDozeFPQ210NS_MacDoze11_SYSTEMTIME')
    timer.require((vector['code_offset'], vector['toc_offset']), (0x40f0, 0), 'GetLocalTime implementation')
    for at, target, name in [(0x410c, 0xa950, 'GetDateTime'), (0x4124, 0xa938, 'LongSecondsToDate')]:
        call(mac, at, target, 'local-time wrapper OS call')
        imported = timer.glue_import(mac, target, 0)
        timer.require((imported['symbol'], imported['library']), (name, 'InterfaceLib'), 'local-time OS import')
    d(mac, 0x4164, 44, (31, 30, 14), 'Mac host session milliseconds are zero')
    vector = timer.vector(bullfrog, 'SetTime__11TbTimeStampFiiiiiii')
    timer.require((vector['code_offset'], vector['toc_offset']), (0x1bb50, 0), 'timestamp setter implementation')
    d(bullfrog, 0x1bb58, 14, (31, 4, 0), 'setter receiver is inputr4, not hidden status addressr3')
    d(bullfrog, 0x1bbcc, 36, (4, 31, 4), 'successful setter writes receiver timestamp low word')
    d(bullfrog, 0x1bbd0, 36, (3, 31, 0), 'successful setter writes receiver timestamp high word')
    # Names are interpreted labels tied to field operands, not free text evidence.
    names = app.relocs[1][0x3350]
    timer.require((names.kind, names.target), ('section', 0), 'calendar name-base relocation')
    labels = {}
    for relative, expected in [(0, 'mFunnyTimeStart'), (16, 'mSessionStart'),
                               (30, 'mMonthAtLastUpdate'), (49, 'mDayAtLastUpdate'),
                               (66, 'mFunnySecsPerRealSec')]:
        value = pef._cstr(app.code.data, names.addend + relative)
        timer.require(value, expected, 'calendar serialized field label')
        labels[str(relative)] = value
    hashes = {}
    for obj, start, end, expected in [
        (app, 0xe3c90, 0xe3d30, 'a3af42a6307c264c89946b44398dc2b2e3d26019745e10478693934282679f81'),
        (app, 0xe4348, 0xe4394, '4bad4f1971647d871c1b674c9649e2874ed00e251ae4e9d720fbe53cb0153d36'),
        (app, 0xe4394, 0xe44a8, '7a2dc4d2caa9c7a1f60213a81f9d113cd74ecb1c15b76018b0b49955885c87bc'),
        (app, 0xe3d30, 0xe3f0c, '338e566bcdd21963339caf0696de3993a6317b1570420574ffa7bf3eb6d2758b'),
        (mac, 0x40f0, 0x4180, 'c7079fb03041359e544b87f42f6cd8bc8dc93d9aa5edd3f9e3aabcabaa3d8b57'),
        (bullfrog, 0x1bb50, 0x1bbfc, 'dda999396223691d5e97869437417380aaf6148b7f86980872fc715efdfb926d'),
    ]:
        actual = hashlib.sha256(obj.code.data[start:end]).hexdigest()
        timer.require(actual, expected, 'calendar epoch functional region')
        hashes[f'{obj.name}:{start:#x}..{end:#x}'] = actual
    return {'application_sha256': timer.IDENTITIES['SimThemePark.data'], 'macdoze_sha256': MACDOZE_SHA,
            'bullfrog_sha256': timer.IDENTITIES['bullfrog_shared.data'], 'witnesses': rows,
            'field_labels': labels, 'region_sha256': hashes,
            'constructor_funny_civil_arguments': [2000, 1, 1, 0, 0, 0, 0],
            'host_local_time_receiver_offset': 8, 'virtual_date_receiver_offset': 0,
            'limits': 'Normal successful OS conversion, static identified Mac paths; saved epochs may replace constructor values. No PC execution or universal civil-calendar environment claim.'}


def inspect_save(path):
    import zlib
    phase.inspect(path)
    payload = zlib.decompressobj().decompress(path.read_bytes()[0x629:], phase.PAYLOAD_LENGTH + 1)
    phase.require(hashlib.sha256(payload).hexdigest(), phase.PAYLOAD_SHA, 'identified calendar payload')
    record = calendar_record(payload, 6719)
    phase.require(record, {'funny_start': 125911584000000000, 'session_start': 125850128932900000,
                           'month_cache': 1, 'day_cache': 2, 'rate': 15000}, 'identified separate calendar timestamps')
    return {'save_sha256': phase.SAVE_SHA, 'payload_sha256': phase.PAYLOAD_SHA,
            'calendar_candidate_offset': 6719, 'calendar_candidate_bytes': 28, 'fields': record,
            'conversion_terms_at_world_turn755': conversion_terms(record, 755),
            'limits': 'Explicit calendar candidate; preceding full-player framing remains separate. Native Mac field correspondence, not PC runtime equivalence.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--save', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
        if args.save:
            result['pc_save_correspondence'] = inspect_save(args.save)
        print(json.dumps(result, indent=2, sort_keys=True))
    except (OSError, ValueError, pef.PEFError) as error:
        parser.exit(1, f'calendar epoch evidence: {error}\n')
