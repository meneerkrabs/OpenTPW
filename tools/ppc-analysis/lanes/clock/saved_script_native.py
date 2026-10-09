"""Pin saved script framing and ID/list consumers; addresses/fields/hashes only."""
import argparse
import hashlib
import json
from pathlib import Path

import clock_evidence as evidence
import timer_evidence as timer
import pef


def inspect(root: Path) -> dict:
    app = timer.load_identified(root / 'SimThemePark.data')
    records = []
    def d(at, op, fields, meaning):
        timer.require(evidence.d_operand(app.code.data, at, op), fields, meaning)
        records.append({'at': hex(at), 'meaning': meaning, 'fields': list(fields)})
    def call(at, target, meaning):
        timer.require(evidence.branch_target(app.code.data, at, True), target, meaning)
        records.append({'at': hex(at), 'meaning': meaning, 'target': hex(target)})
    def mask(at, fields, meaning):
        word = evidence.word_at(app.code.data, at)
        timer.require(word >> 26, 21, meaning)
        actual = (word >> 21 & 31, word >> 16 & 31, word >> 11 & 31,
                  word >> 6 & 31, word >> 1 & 31)
        timer.require(actual, fields, meaning)
        records.append({'at': hex(at), 'meaning': meaning, 'fields': list(fields)})
    for at, op, fields, meaning in [
        (0xb3fbc, 14, (7, 0, 0), 'fixed writer uses unformatted byte span'),
        (0xb3fb8, 14, (6, 0, 244), 'fixed writer244byte span'),
        (0xb4b48, 32, (20, 31, 0), 'reader captures rebuilt next pointer before bulk read'),
        (0xb4b50, 32, (21, 31, 4), 'reader captures rebuilt previous pointer before bulk read'),
        (0xb4d80, 36, (20, 31, 0), 'reader restores rebuilt next pointer over saved token'),
        (0xb4d90, 36, (21, 31, 4), 'reader restores rebuilt previous pointer over saved token'),
        (0xb4b1c, 32, (3, 27, 0), 'reader new script points at current manager head'),
        (0xb4b20, 36, (3, 31, 0), 'reader inserts new script next'),
        (0xb4b24, 36, (0, 31, 4), 'reader new script previous is zero'),
        (0xb4b34, 36, (31, 3, 4), 'reader former head previous points at new script'),
        (0xb4b38, 36, (31, 27, 0), 'reader publishes new manager head'),
        (0xb4938, 14, (25, 26, 12), 'reader manager stored-count pointer'),
        (0xb4978, 14, (8, 26, 12), 'restored count byte-swap destination'),
        (0xb497c, 32, (9, 26, 12), 'restored manager count word load'),
        (0xb4b0c, 32, (3, 25, 0), 'reader stored-count load before insertion'),
        (0xb4b14, 14, (3, 3, 1), 'reader stored-count word increments per insertion'),
        (0xb4b18, 36, (3, 25, 0), 'reader stored-count word store'),
        (0xb3914, 32, (0, 21, 4), 'writer pass word'),
        (0xb392c, 32, (6, 21, 8), 'writer next-ID word'),
        (0xb3930, 32, (0, 21, 12), 'writer stored count'),
        (0xb4db8, 36, (23, 31, 24), 'reader replaces serialized code pointer'),
        (0xb4e6c, 36, (23, 31, 32), 'reader replaces serialized label pointer'),
        (0xb4f20, 36, (23, 31, 28), 'reader replaces serialized variable pointer'),
        (0xb4fd4, 36, (23, 31, 52), 'reader replaces serialized literal pointer'),
        (0xb506c, 36, (23, 31, 36), 'reader replaces serialized metadata8 pointer'),
        (0xb51e0, 36, (23, 31, 44), 'reader replaces serialized metadata32 pointer'),
        (0xb52a4, 36, (23, 31, 48), 'reader replaces serialized auxiliary pointer'),
        (0xb551c, 14, (6, 0, 4), 'object-binding count word read width'),
        (0xb5548, 14, (6, 0, 4), 'object-binding record-width word read width'),
        (0xb5574, 14, (5, 0, 28), 'reader object-binding record allocation width28'),
        (0xb5594, 36, (4, 3, 0), 'object-binding reader rebuilds next'),
        (0xb55a8, 36, (22, 3, 4), 'object-binding reader rebuilds previous'),
        (0xb55ac, 36, (22, 31, 176), 'object-binding reader head insertion'),
        (0xb5620, 36, (21, 22, 0), 'object-binding reader discards saved next token'),
        (0xb5628, 36, (20, 22, 4), 'object-binding reader discards saved previous token'),
        (0xb5760, 32, (4, 2, -27616), 'ID lookup uses global manager'),
        (0xb577c, 32, (4, 4, 16), 'ID lookup starts at manager list head'),
        (0xb5784, 32, (4, 4, 0), 'ID lookup follows rebuilt next pointer'),
        (0xb5790, 32, (0, 4, 8), 'ID lookup compares stored script ID'),
        (0xb11f8, 36, (3, 31, 12), 'child creation stores returned script ID'),
        (0xb120c, 32, (0, 31, 8), 'child creation reads parent script ID'),
        (0xb1210, 36, (0, 3, 16), 'child receives numeric parent ID'),
        (0xb12a0, 36, (3, 31, 20), 'secondary creation stores returned script ID'),
        (0xb13e0, 32, (3, 31, 16), 'parent-variable consumer reads numeric parent ID'),
        (0xb37e4, 32, (3, 31, 20), 'cleanup reads secondary script ID'),
        (0xb37fc, 32, (3, 31, 12), 'cleanup reads child script ID'),
        (0xb3814, 32, (3, 31, 16), 'cleanup reads parent script ID'),
        (0xb3828, 36, (29, 3, 12), 'cleanup clears parent child-ID field'),
    ]:
        d(at, op, fields, meaning)
    for at, target, meaning in [
        (0xb11f4, 0xb2ba0, 'child creation calls script loader'),
        (0xb1208, 0xb5758, 'child result resolves by script ID'),
        (0xb129c, 0xb2ba0, 'secondary creation calls script loader'),
        (0xb13ec, 0xb5758, 'parent-variable consumer resolves ID'),
        (0xb37f0, 0xb5758, 'secondary cleanup resolves ID'),
        (0xb3808, 0xb5758, 'child cleanup resolves ID'),
        (0xb3824, 0xb5758, 'parent cleanup resolves ID'),
        (0xb4ce0, 0xb4764, 'WAIT deadline restored by word byte swap only'),
        (0xb4ce8, 0xb4764, 'animation deadline restored by word byte swap only'),
        (0xb4d2c, 0xb4764, 'timer deadline restored by word byte swap only'),
    ]:
        call(at, target, meaning)
    for at, source, shift in [(0xb3fd4, 0, 2), (0xb4064, 0, 2), (0xb40f4, 0, 2),
                              (0xb41ec, 0, 3), (0xb4280, 0, 4), (0xb435c, 3, 5),
                              (0xb446c, 0, 2)]:
        mask(at, (0, source, shift, 0, 31 - shift), 'producer element count to bytes')
    for at, shift in [(0xb4e24, 2), (0xb4ed8, 2), (0xb4f8c, 2), (0xb50d8, 3),
                      (0xb5188, 4), (0xb5310, 2)]:
        mask(at, (0, 0, 32 - shift, shift, 31), 'consumer bytes to element count')
    mask(0xb51f0, (0, 22, 5, 0, 26), 'consumer metadata32 count to allocation bytes')
    mask(0xb5244, (0, 6, 5, 0, 26), 'consumer metadata32 count to read bytes')
    word = evidence.word_at(app.code.data, 0xb4980)
    timer.require((word >> 26, word >> 21 & 31, word >> 16 & 31,
                   word >> 11 & 31, word >> 1 & 1023), (31, 9, 0, 8, 662),
                  'restored manager count byte-reversed store')
    # Compare by ID, never by a serialized address. Verify exact register operands.
    word = evidence.word_at(app.code.data, 0xb5794)
    timer.require((word >> 26, word >> 21 & 31, word >> 16 & 31,
                   word >> 11 & 31, word >> 1 & 1023), (31, 0, 0, 3, 0), 'script ID equality comparison')
    hashes = {}
    for start, end, expected in [
        (0xb3868, 0xb471c, '6b921a635d5e3da9a26b1165723f0b57312c5cf66b2296f988ac2122af07b12c'),
        (0xb4818, 0xb56a0, 'fb6098a4c026f94c51fa72ec801049b08d431590d4b74af07414616d7f1de7ed'),
        (0xb5758, 0xb57d4, '2e7fb4e04bbbd3b81976db50a5dab652367a0c3116413b02f4a296433ee13a2b'),
        (0xb11f4, 0xb1214, 'c3e8e283786749d135db7bb96789b9e1e5a4f8aca0902ab7cd967450b4ab2237'),
        (0xb129c, 0xb12a8, '382906480c12aa979818556ace6bdf53a7339f92049e94da516a6b47d6e763ac'),
    ]:
        actual = hashlib.sha256(app.code.data[start:end]).hexdigest()
        timer.require(actual, expected, 'saved-script functional region identity')
        hashes[f'{start:#x}..{end:#x}'] = actual
    return {'identity': timer.IDENTITIES['SimThemePark.data'], 'toc': '0x8000',
            'writer': '0xb3868', 'reader': '0xb4818', 'id_lookup': '0xb5758',
            'witnesses': records, 'region_sha256': hashes,
            'reference_fields': {'12': 'child ID', '16': 'parent ID', '20': 'secondary script ID'},
            'limits': 'No original execution, address dereference or Windows runtime qualification; strict metadata parser guards are tool policy.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.bin_root), indent=2, sort_keys=True))
    except (OSError, pef.PEFError) as error:
        parser.exit(1, f'saved script native: {error}\n')
