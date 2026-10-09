"""Round-3 review witnesses: shared-reader fixes, source fixes and the new formats/scenarios lanes.

Usage: python3 -I round3_evidence.py /path/to/mac-feral/bin

Every check pins the identified Feral Mac containers (via review_evidence.Binary),
decodes instruction fields at hand-reviewed addresses and compares them with values
the reviewer derived independently. Output is metadata only: addresses, decoded
meanings, counts and conclusions. Nothing from the original programs is executed,
emulated, printed or stored.

The pure-arithmetic models are independently written statements of what the
reviewed instructions compute; they are not translations of original code.
"""
from __future__ import annotations

import argparse
import json
import math
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import review_evidence as rv  # noqa: E402
from review_evidence import Binary, ReviewError, decode, require  # noqa: E402

U32 = 0xFFFFFFFF
rv.IDENTITIES.setdefault('engine_shared.data', 'c549123f647dcf33c3e2c3d515bffe2cfcb19d11680f743f02ca42bb09dbd73b')
rv.IDENTITIES.setdefault('ltms_shared.data', '2b0f7ac92c1f8b67761d271dd5832fca1bd19a8dd8d494b7b89b6ffa693e6ee8')


# --- Independent arithmetic models -------------------------------------------------

def s16(value: int) -> int:
    value &= 0xFFFF
    return value - 0x10000 if value & 0x8000 else value


def rand_result(generator_word: int, raw_bound: int) -> int:
    """0xb07f8..0xb0820: labs(x - trunc(x/(b+1))*(b+1)) with x = word>>1 and b = extsh(raw).

    x < 2**31, so both labs calls are identities. For b = -1 the divisor is zero, but the
    remainder is formed by divw/mullw/subf, so whatever divw yields is multiplied by zero and
    the result is x itself. For b <= -2 truncating division keeps the sign of x (>= 0).
    """
    x = (generator_word & U32) >> 1
    d = s16(raw_bound) + 1
    if d == 0:
        return x
    q = abs(x) // abs(d) * (1 if (x >= 0) == (d > 0) else -1)
    return abs(x - q * d)


def research_percentage(researched: int, total: int) -> int:
    """0xf1658..0xf166c: zero items -> 0, else divwu(100*researched, total)."""
    return 0 if total == 0 else ((researched * 100) & U32) // total


def research_opens_next(researched: int, total: int, threshold_word: int) -> bool:
    """0xf1680: cmplw percentage, threshold (unsigned); opening continues when not below."""
    return research_percentage(researched, total) >= (threshold_word & U32)


def research_threshold_element(current_group: int) -> int:
    """Word read at balance+1280+4g is ResearchTech[g+1] when element 0 is at +1276."""
    return (1280 + 4 * current_group - 1276) // 4


def mac_monthly_repayment(amount: int, apr: int, months: int) -> int:
    """Round-1 loan reading: amount*(1+APR/100)**(months/12*0.5)/months, truncated (months > 0)."""
    return math.trunc(amount * math.pow(1 + apr / 100, months / 12 * 0.5) / months)


def opentpw_annuity_repayment(amount: int, apr: int, months: int) -> int:
    """OpenTPW LoanMath approximation (ParkLedger.cs) for comparison only."""
    if apr <= 0:
        return amount // months
    rate = apr / 1200.0
    return math.floor(amount * rate / (1 - math.pow(1 + rate, -months)))


def bf4_channel(dest: int, colour: int, coverage: int) -> int:
    """ltms 0xb574..0xb584 at alpha 255: (D + (mulhwu(0x88888889, n*(C-D)) >> 3)) & 0xFF."""
    product = (coverage * (colour - dest)) & U32
    return (dest + ((product * 0x88888889) >> 32 >> 3)) & 0xFF


def ticks_per_game_day(funny_secs_per_real_sec: int = 15000, ticks_per_real_sec: int = 4) -> float:
    """Calendar 0xe4394: game seconds per tick = mFunnySecsPerRealSec / 4."""
    return 86400 / (funny_secs_per_real_sec / ticks_per_real_sec)


# --- Instruction witnesses -----------------------------------------------------------

def _x31(rt: int, ra: int, rb: int, xo: int, rc: int = 0) -> tuple:
    return ('x31', rt, ra, rb, xo, rc)


def _rlwinm(rs: int, ra: int, sh: int, mb: int, me: int, rc: int = 0) -> tuple:
    return ('rlwinm', rs, ra, sh, mb, me, rc)


def _direct_callers(b: Binary, target: int) -> list[int]:
    out = []
    for at in range(0, len(b.code) - 3, 4):
        word = b.word(at)
        if word >> 26 == 18 and word & 3 == 1 and decode(word, at)[1] == target:
            out.append(at)
    return out


def vm_audit(app: Binary) -> dict:
    # Dispatcher requires top byte 0x80 (opcode tag) and index <= 105.
    app.expect(0xaf5a4, *_rlwinm(3, 4, 0, 0, 7))
    app.expect(0xaf5a8, 'addis', 0, 4, -0x8000)
    app.expect(0xaf5b0, 'bc', 4, 2, 0xb2340)
    app.expect(0xaf5b8, 'cmpli', 0, 0, 105)
    # COPY destination must carry tag 0x40; otherwise exit with no fetch of the source word.
    app.expect(0xaf5f8, *_rlwinm(3, 4, 0, 0, 7))
    app.expect(0xaf5fc, 'addis', 0, 4, -0x4000)
    app.expect(0xaf604, 'bc', 4, 2, 0xb2354)
    # Operand fetch past either end of the code also stops with PC -10000.
    app.expect(0xaf410, 'addi', 0, 0, -10000)
    app.expect(0xaf418, 'stw', 0, 31, 60)
    # RAND: x = generator >> 1 (logical); remainder via divw/mullw/subf.
    app.expect(0xb07f8, *_x31(3, 28, 0, 922))
    app.expect(0xb0804, *_rlwinm(3, 3, 31, 1, 31))
    require(app.glue(app.call(0xb0808)), 'labs', 'RAND first labs')
    app.expect(0xb0810, 'addi', 4, 28, 1)
    app.expect(0xb0814, *_x31(0, 3, 4, 491))
    app.expect(0xb0818, *_x31(0, 0, 4, 235))
    app.expect(0xb081c, *_x31(3, 0, 3, 40))
    require(app.glue(app.call(0xb0820)), 'labs', 'RAND second labs')
    app.expect(0xb082c, 'stw', 3, 31, 72)
    return {'copy_literal_destination': 'exit before source fetch; RSE operand tags are 0x00/0x10/0x20/0x40, '
                                        'so the next dispatch always fails the 0x80 test -> PC -10000',
            'rand': 'labs(x - trunc(x/(b+1))*(b+1)), x = gen>>1, b = extsh(raw); '
                    'b = -1 -> x (0..2^31-1); b <= -2 -> 0..(-b-2); result sets the accumulator',
            'opentpw_b3d14f9_negative_bound': 'throws; the Mac result is defined (corpus has none: 56 RAND, all literal >= 0)'}


def research_audit(app: Binary) -> dict:
    rec = 0x34d10 + 60 * 165
    require(struct.unpack_from('>I', app.data, rec)[0], 6, 'descriptor 165 kind')
    require(app.data[rec + 4:rec + 36].split(b'\0')[0], b'PercentageForThisTech', 'descriptor 165 name')
    require(app.data[rec + 64:rec + 96].split(b'\0')[0], b'ResearchTech', 'descriptor 166 array name')
    balance = app.slot(0xf15c4, 31)
    require((balance.kind, balance.target, balance.addend), ('section', app.c.data_section.index, 0x54860),
            'balance object pointer')
    app.expect(0xf1668, 'mulli', 0, 24, 100)
    app.expect(0xf166c, *_x31(4, 0, 25, 459))           # divwu (unsigned percentage)
    app.expect(0xf1670, 'lbz', 5, 29, 5144)             # current group g of category c
    app.expect(0xf1674, *_rlwinm(5, 3, 2, 0, 29))
    app.expect(0xf1678, 'addi', 0, 3, 1280)
    app.expect(0xf167c, *_x31(0, 31, 0, 23))            # lwzx threshold
    app.expect(0xf1680, *_x31(0, 4, 0, 32))             # cmplw (unsigned)
    app.expect(0xf1688, 'cmpli', 0, 5, 7)
    return {'threshold': 'balance+1280+4g = ResearchTech[g+1].PercentageForThisTech '
                         '(element 0 at +1276 per the economy lane descriptor walk)',
            'comparison': 'unsigned divwu percentage, unsigned compare, g < 7',
            'shipped_consequence': 'group 1 opens at 0 %, group 2 at 80 %, group 3 at 85 %, group 4 at 85 %'}


def game_tick_audit(app: Binary) -> dict:
    app.expect(0x10537c, 'addis', 4, 26, 30)
    app.expect(0x105398, 'lwz', 3, 4, -22772)
    app.expect(0x1053a0, 'stw', 0, 4, -22772)
    app.expect(0x1053a4, 'lwz', 0, 4, -22728)
    app.expect(0x1053a8, 'cmpi', 0, 0, 4)
    return {'counter': 'world + 0x1E0000 - 22772 is mGameTick (scenarios ticket counter = same field)',
            'restored_by_save': 'formats lane: world var mGameTick = 755 in Easymode',
            'state_4': 'world + 0x1E0000 - 22728 == 4 is a world-state gate, not GameType (data 0x53d98)',
            'ticks_per_game_day': ticks_per_game_day()}


def save_writer_audit(app: Binary) -> dict:
    require(app.call(0x11cc28), 0x105d3c, 'World writer call')
    app.expect(0x11cc58, 'addis', 3, 0, 0x5752)
    app.expect(0x11cc60, 'addi', 0, 3, 0x4C44)
    require(app.glue(app.call(0x11cc98)), 'LbFile_Write__FPvPCvUlPUl', 'tag write')
    return {'framing': 'World data is written first, then its tag WRLD (stored DLRW): tags trail their data'}


def md2_gate_audit(app: Binary, engine: Binary) -> dict:
    engine.expect(0x3f9f0, *_rlwinm(4, 27, 0, 31, 31))     # r27 = flags & 1
    engine.expect(0x3fc74, 'lwz', 0, 29, 4)
    engine.expect(0x3fc78, 'cmpli', 0, 0, 221)
    engine.expect(0x3fcec, *_rlwinm(26, 0, 0, 30, 30, 1))  # flags & 2
    stub = app.call(0x58f04)
    require(app.glue(stub), 'LoadM3D2Header__FPcUl', 'MD2 loader import')
    require(app.call(0x99ad8), stub, 'second loader call')
    require(_direct_callers(app, stub), [0x58f04, 0x99ad8], 'loader call sites')
    app.expect(0x58f00, 'addi', 4, 0, 0)
    app.expect(0x99acc, 'addi', 4, 0, 6)
    app.expect(0x99ad4, 'addi', 4, 0, 2)
    return {'flags': 'callers pass 0, 6 or 2; mask 0x1 (the formats doc\'s "bit 1") is never set',
            'consequence': 'major < 221 meshes (207.201) return DeadMesh in this build'}


def bf4_audit(ltms: Binary) -> dict:
    ltms.expect(0xb4dc, 'addis', 5, 0, -30583)
    ltms.expect(0xb4f4, 'addi', 27, 5, -30583)
    ltms.expect(0xb574, *_x31(3, 27, 8, 11))               # mulhwu
    ltms.expect(0xb57c, *_rlwinm(3, 3, 29, 3, 31))
    ltms.expect(0xb580, *_x31(3, 5, 3, 266))               # add
    ltms.expect(0xb584, *_rlwinm(3, 3, 0, 24, 31))
    return {'magic': hex(((-30583 & 0xFFFF) << 16) + -30583 & U32),
            'blend': '(D + (mulhwu(0x88888889, n*(C-D)) >> 3)) & 0xFF',
            'full_coverage_black_on_white': bf4_channel(255, 0, 15)}


def glue_population(root: Path) -> dict:
    counts = {}
    for path in sorted(root.rglob('*.data')):
        raw = path.read_bytes()
        if raw[:8] != b'Joy!peff':
            continue
        c = rv.pef.PEFContainer(raw, path.name)
        code = bytes(c.code.data)
        counts[str(path.relative_to(root))] = sum(
            1 for at in range(0, len(code) - 23, 4)
            if struct.unpack_from('>I', code, at)[0] >> 16 == 0x8182
            and struct.unpack_from('>5I', code, at + 4) == rv.GLUE_TAIL)
    return {'containers': len(counts), 'standard_glue_stubs': sum(counts.values())}


def inspect(root: Path) -> dict:
    app = Binary(root, 'SimThemePark.data', 'SimThemePark.data', rv.APP_TOC)
    engine = Binary(root, 'engine_shared.data', 'libraries/engine_shared.data', 0x8000)
    ltms = Binary(root, 'ltms_shared.data', 'libraries/ltms_shared.data', 0x8000)
    return {'vm': vm_audit(app), 'research': research_audit(app), 'game_tick': game_tick_audit(app),
            'save_writer': save_writer_audit(app), 'md2_gate': md2_gate_audit(app, engine),
            'bf4': bf4_audit(ltms), 'glue': glue_population(root),
            'loan_import_example': {'amount': 10000, 'apr': 10, 'months': 24,
                                    'mac_reading': mac_monthly_repayment(10000, 10, 24),
                                    'opentpw_annuity': opentpw_annuity_repayment(10000, 10, 24)}}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
    except (OSError, rv.pef.PEFError, ReviewError) as error:
        parser.exit(1, f'round3 evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()
