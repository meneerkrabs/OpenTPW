"""Pin numeric load selector control flow; no user-facing enum names or native execution."""
import argparse
import hashlib
import json
from pathlib import Path

import clock_evidence as evidence
import timer_evidence as timer
import pef


def conditional(code, at):
    word = evidence.word_at(code, at)
    timer.require(word >> 26, 16, 'selector conditional branch')
    fields = {'at': at, 'bo': word >> 21 & 31, 'bi': word >> 16 & 31,
              'target': evidence.branch_target(code, at)}
    if fields['bo'] not in (4, 12) or fields['bi'] != 2:
        raise pef.PEFError('selector witness supports only CR0 EQ predicates')
    return fields


def branch_taken(branch, equal):
    if branch['bi'] != 2 or branch['bo'] not in (4, 12):
        raise ValueError('unqualified selector branch predicate')
    return equal if branch['bo'] == 12 else not equal


def successful_route(selector, rules):
    """Only after initial header/prefix operations succeed; no callee state effects are modelled."""
    if not -(1 << 31) <= selector < (1 << 31):
        raise ValueError('selector must be a signed32-bit register value')
    if not branch_taken(rules['selector_zero_continue'], selector == 0):
        return {'route': 'header_return1', 'allows_clock_pair_read': False,
                'allows_saved_script_read': False, 'calls_pre_hook': False, 'calls_post_hook': False}
    cleanup = not branch_taken(rules['selector_one_skip_pre_hook'], selector == 1)
    full_body = branch_taken(rules['selector_one_full_body'], selector == 1)
    return {'route': 'full_body' if full_body else 'prefix_return1',
            'allows_clock_pair_read': full_body, 'allows_saved_script_read': full_body,
            'calls_pre_hook': cleanup,
            'calls_post_hook': not branch_taken(rules['selector_one_skip_post_hook'], selector == 1)}


def inspect(root: Path) -> dict:
    app = timer.load_identified(root / 'SimThemePark.data')
    rows = []
    def d(at, op, expected, meaning):
        timer.require(evidence.d_operand(app.code.data, at, op), expected, meaning)
        rows.append({'at': hex(at), 'meaning': meaning, 'fields': list(expected)})
    def call(at, target, meaning):
        timer.require(evidence.branch_target(app.code.data, at, True), target, meaning)
        rows.append({'at': hex(at), 'meaning': meaning, 'target': hex(target)})
    def transfer(at, source, destination, records_condition=False):
        word = evidence.word_at(app.code.data, at)
        fields = (word >> 26, word >> 21 & 31, word >> 16 & 31,
                  word >> 11 & 31, word >> 1 & 1023, word & 1)
        timer.require(fields, (31, source, destination, source, 444, int(records_condition)),
                      'selector register-copy relationship')
        rows.append({'at': hex(at), 'meaning': 'selector register transfer',
                     'source_register': source, 'destination_register': destination})
    transfer(0x11ad04, 6, 29, True)
    d(0x11b3a4, 14, (5, 29, 0), 'entry forwards selector as reader argument5')
    transfer(0x11b5b8, 5, 23)
    for at, op, fields, meaning in [
        (0x11aefc, 11, (0, 29, 0), 'selector0 comparison after header stage'),
        (0x11af14, 14, (3, 0, 1), 'selector0 returns literal success1'),
        (0x11b390, 11, (0, 29, 1), 'selector1 pre-hook comparison'),
        (0x11b3cc, 11, (0, 29, 1), 'selector1 post-hook comparison'),
        (0x11b3dc, 11, (0, 25, 0), 'state-reader result checked after conditional post-hook'),
        (0x11b614, 11, (0, 3, 0), 'initial prefix reader success requires return0'),
        (0x11b628, 14, (3, 0, 0), 'initial prefix failure returns0 from state reader'),
        (0x11b658, 11, (0, 23, 1), 'selector1 reader full-body comparison'),
        (0x11b66c, 14, (3, 0, 1), 'selector1 reader returns literal success1'),
        (0x11db8c, 14, (3, 0, 1), 'selector1 prefix hook is literal success stub'),
        (0x11b600, 14, (5, 0, 0), 'initial generic prefix operation argument5'),
        (0x11b608, 14, (6, 0, 1), 'initial generic prefix operation argument6'),
        (0x11b60c, 14, (7, 0, 0), 'initial generic prefix operation argument7'),
    ]:
        d(at, op, fields, meaning)
    for at, target, meaning in [
        (0x11aeb4, 0x11c880, 'initial header stage receives numeric selector'),
        (0x11b39c, 0x11b478, 'non1 pre-hook invokes cleanup'),
        (0x11b3ac, 0x11b5ac, 'entry invokes state reader with selector'),
        (0x11b3d8, 0x11b4f4, 'non1 post-hook invokes clock/reset alignment lifecycle'),
        (0x11b610, 0x10bc1c, 'initial generic prefix reader before selector1 return'),
        (0x11b668, 0x11db8c, 'selector1 calls literal success stub'),
        (0x11b4cc, 0xb2b18, 'pre-hook shuts down existing script manager'),
        (0x11b54c, 0x10ea28, 'post-hook aligns saved epochs'),
        (0x11b9f4, 0x10e998, 'later full body reads SSEM clock pair'),
        (0x11bac0, 0x127b64, 'later full body reads KOLC raw clock'),
        (0x11bb88, 0x1c31f4, 'later full body reads TNAV cadence state'),
        (0x11bea8, 0xb4818, 'later full body reads saved script manager graph'),
    ]:
        call(at, target, meaning)
    for at, expected in [(0x11af18, 0x11b464), (0x11b670, 0x11c46c)]:
        timer.require(evidence.branch_target(app.code.data, at), expected, 'early return jumps common epilogue')
    rules = {}
    for name, at, expected in [
        ('selector_zero_continue', 0x11af00, (4, 2, 0x11af1c)),
        ('selector_one_skip_pre_hook', 0x11b394, (12, 2, 0x11b3a0)),
        ('selector_one_full_body', 0x11b660, (4, 2, 0x11b674)),
        ('selector_one_skip_post_hook', 0x11b3d0, (12, 2, 0x11b3dc)),
    ]:
        fields = conditional(app.code.data, at)
        timer.require((fields['bo'], fields['bi'], fields['target']), expected, 'exact selector branch')
        rules[name] = fields
    prefix = conditional(app.code.data, 0x11b618)
    timer.require((prefix['bo'], prefix['bi'], prefix['target']), (12, 2, 0x11b630),
                  'initial prefix return0 continues reader')
    hashes = {}
    for start, end, expected in [
        (0x11b390, 0x11b3dc, '41888215c650e11db9d46826bd8b3f9ccad60d2a656294d5e4b35f47a0cd784e'),
        (0x11b5ac, 0x11b674, '0bb9074566effd664742bc2375f6f01cb979f41d40f85d96572ce3cb91f31c84'),
        (0x11db84, 0x11db9c, '7a539b0014bddf821985d6121b7961fcadfa88281304e1843fdf86b69c9fdb7d'),
        (0x11ae40, 0x11af1c, 'ae49ca56447e2f9b6a3dfe9396d5534eb64bd0679e14aafd2afc902e10170d23'),
    ]:
        actual = hashlib.sha256(app.code.data[start:end]).hexdigest()
        timer.require(actual, expected, 'selector functional region hash')
        hashes[f'{start:#x}..{end:#x}'] = actual
    return {'identity_sha256': timer.IDENTITIES['SimThemePark.data'], 'toc': '0x8000',
            'witnesses': rows, 'branch_rules': rules, 'region_sha256': hashes,
            'successful_control_routes': {str(selector): successful_route(selector, rules) for selector in (0, 1, 2)},
            'limits': 'Successful initial header/prefix only; numeric selectors have no inferred names; generic prefix virtual calls/callee mutations and target PC execution remain unqualified.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.bin_root), indent=2, sort_keys=True))
    except (OSError, ValueError, pef.PEFError) as error:
        parser.exit(1, f'selector lifecycle: {error}\n')
