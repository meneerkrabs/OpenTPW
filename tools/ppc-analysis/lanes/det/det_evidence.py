"""Pinned Mac witnesses for simulation determinism: main-loop order, cadence and RNG ownership.

Read-only static evidence on the identified Feral Mac PEFs. No original instruction is executed
and no original byte or disassembly listing is emitted: the result is interpreted metadata only
(addresses, operands, call targets, constants and counts).

Two kinds of claim are produced and labelled separately:

* operand witnesses: a specific instruction at a pinned address has the expected fields or call
  target. A wrong expectation raises PEFError.
* bounded scans ("negative witnesses"): whole-section scans over one instruction form (direct
  `bl` targets, D-form displacements, LCG multiplier immediates, string literals). They do not
  follow branches, CTR/pointer-glue calls, tail branches or register flow, so "only" in their
  results means "only among sites of that form", never "only route at runtime".
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef  # noqa: E402
from timer_evidence import call_target, d_fields, glue_import, require  # noqa: E402

APP_NAME = 'SimThemePark.data'
APP_SHA = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
CLIB_NAME = 'c_c++_shared.data'
CLIB_SHA = '5e04f9c00c922dc78a787d1b93067c75d37a3e65b0a0202e50c2f449f131b27f'
TOC = 0x8000

# World context fields reached through `addis rX, world, 0x1e` and a negative displacement.
WORLD_HIGH = 0x1e
RNG_DISPLACEMENT = -0x58f8   # world + 0x1da708, label mRandomSeed
TICK_DISPLACEMENT = -0x58f4  # world + 0x1da70c, label mGameTick
WORLD_STATE_DISPLACEMENT = -0x58c8

# Scheduler catch-up loop inside game-callback helper 0x1c1208. Each entry is pinned below.
SUBSTEP = {
    'clock_call': (0x1c22b4, 0x10e844),
    'backlog_limit': (0x1c22c8, 2000),
    'slice_ms': (0x1c22e0, 31),
    'substep_increment': (0x1c22ec, 1),
    'park_cap': (0x1c2354, 3),
}
# (cadence, call site, target, role). Order is the instruction order inside one substep.
SUBSTEP_CALLS = [
    ('every', 0x1c230c, 0x9f748, 'particle system (PTCL strings), own 214013 LCGs'),
    ('every', 0x1c2310, 0x20688, 'vehicle/kart subsystem (GoKart strings), own 214013 LCG + world RNG'),
    ('every', 0x1c2314, 0xb2838, 'RSE script manager pass'),
    ('even', 0x1c2330, 0x63dd0, 'flying-car/ambient pass (FLY strings), consumes world RNG'),
    ('even', 0x1c2334, 0xb7ac0, 'reads scheduler clock; role unqualified'),
    ('turn', 0x1c23b8, 0x10536c, 'park turn (mode 0/2 direct)'),
    ('turn', 0x1c23ec, 0x10565c, 'park turn wrapper (mode 1)'),
    ('eighth', 0x1c2424, 0xb5da4, 'runs on every eighth substep even when the park cap is spent; role unqualified'),
    ('thirty-second', 0x1c246c, 0xbc1d4, 'audio control from a clamped count (<=89); role unqualified'),
    ('thirty-second', 0x1c24b8, 0xbc210, 'audio control from a clamped value (<=100); role unqualified'),
    ('thirty-second', 0x1c24bc, 0x63d20, 'role unqualified'),
]
# rlwinm. masks testing the substep counter: (address, mb) with sh=0, me=31, Rc=1.
CADENCE_MASKS = {'even': (0x1c231c, 31), 'eighth': (0x1c233c, 29), 'thirty-second': (0x1c242c, 27)}
# Conditional branches of the loop: (address, BO, BI, target).
LOOP_BRANCHES = {
    'backlog_ok': (0x1c22cc, 4, 1, 0x1c24c0),
    'world_flag8': (0x1c22fc, 4, 2, 0x1c230c),
    'world_flag1': (0x1c2308, 4, 2, 0x1c24c0),
    'odd_skips_even_block': (0x1c2320, 4, 2, 0x1c2338),
    'not_eighth_skips_turn': (0x1c2340, 4, 2, 0x1c2428),
    'cap_spent_skips_turn': (0x1c2358, 4, 0, 0x1c2424),
    'not_thirty_second': (0x1c2430, 4, 2, 0x1c24c0),
    'repeat_while_now_gt_previous': (0x1c24cc, 12, 1, 0x1c22dc),
}
# Once per callback, before and after the catch-up loop.
PRE_LOOP_CALLS = [(0x1c22ac, 0xa6f70, 'animation clock cache (+16400/+16404/+16408)')]
POST_LOOP_CALLS = [
    (0x1c2578, 0xbb18c, 'sound service (Tick then Process)'),
    (0x1c257c, 0x7434, 'advisor per-frame update (unscaled clock, C-library rand)'),
    (0x1c2588, 0xb2aac, 'per-frame walk over all scripts; role unqualified'),
    (0x1c25c0, 0x107b0c, 'thing interpolation, alpha=(now-previous)/31'),
    (0x1c25f0, 0x65e80, 'even-phase interpolation, alpha=(now-even_time)/62'),
    (0x1c2624, 0x107958, 'turn interpolation, alpha=(now-turn_time)/248'),
    (0x1c2628, 0x483ac, 'role unqualified'),
    (0x1c262c, 0x3864c, 'coaster motion manager (variable elapsed ms)'),
    (0x1c2788, 0x4d4c4, 'CMapWho::Render wrapper'),
    (0x1c27ac, 0x4d4f0, 'RenderSystem_FlipScreen wrapper'),
]
# lfs f0, displacement(r2) -> float constant in data; divisor for the three interpolation alphas.
INTERPOLATION_DIVISORS = {0x1c25a0: (-0x26a8, 31.0), 0x1c25d0: (-0x26ac, 62.0), 0x1c25f8: (-0x26b0, 248.0)}
RENDER_GLUE = {0x4d4d8: (0x1c6ce4, 'Render__7CMapWhoFv'), 0x4d4fc: (0x1c6cfc, 'RenderSystem_FlipScreen__Fv')}

# Park-turn body 0x10536c: tick increment, actor pass, 30-turn block, then fixed tail calls.
TURN_CALLS = [
    (0x10541c, 0xfa9b0, 'per-thing update dispatch on thing type byte +2 (ThingArray walk)'),
    (0x10562c, 0x1092bc, 'every-30-turn block tail'),
    (0x105634, 0x1078cc, 'deferred thing-list removal'),
    (0x10563c, 0xd67f0, 'world/economy update (calendar 0xe3f0c)'),
    (0x105644, 0x10ce68, 'action recorder/replayer, counted in park turns'),
]
TURN_THIRTY = {'magic_high': (0x105488, -0x7777), 'multiplier': (0x10549c, 30)}

# World-context RNG: generator, setter, seed, save/load.
WORLD_RNG = {
    'generator': 0x105328, 'setter': 0x105360,
    'multiplier': 1664525, 'increment': 1013904223,
    'seed_call': (0x104908, 0x1c6474), 'seed_store': 0x104910, 'tick_reset': 0x104900,
    'save': (0x106018, 0xc6e4, 0x106014), 'load': (0x1069a8, 0xc200, 0x1069a4),
    'tick_save': (0x105f00, 0xc90c, 0x105efc), 'tick_load': (0x106890, 0xc3f8, 0x10688c),
    'labels': {0x486: 'mGameTick', 0x4ef: 'mRandomSeed'}, 'label_base_slot': 0x372c,
}
# Reseed sites: setter calls whose r4 is the thing ID halfword copied by 0xfa9a4 (or read from +0).
RESEED_FROM_THING_ID = [(0xd86dc, None), (0xe79b8, 0xe79a4), (0xe8fd4, 0xe8fbc), (0xeb160, 0xeb14c),
                        (0xeb28c, 0xeb278), (0xefa30, 0xefa1c), (0x1a9574, 0x1a955c)]
RESEED_OTHER = [(0xd5b14, 'u16 object field +0x214 (helper 0xc3304 output)')]
GUEST_CREATION_DRAWS = {'before_reseed': [0xe7774, 0xe77a0, 0xe77d4, 0xe7828, 0xe7864, 0xe7890, 0xe78d4, 0xe78dc],
                        'reseed': 0xe79b8, 'after_reseed': [0xe7a24, 0xe7a44]}
# Generators with their own state, distinct from the world context.
OWN_STATE_GENERATORS = {
    'coaster_boarding': {'site': 0x3e3ac, 'state': 'module global data 622280 (TOC slot 0xcc0)', 'multiplier': 214013,
                         'increment': 2531011, 'projection': 'srwi 16 (logical high half)'},
    'kart_object': {'site': 0x208a8, 'state': 'object +0x38', 'multiplier': 214013, 'increment': 2531011,
                    'projection': 'srawi 16 (arithmetic high half)'},
    'particle_object': {'site': 0x9d4b0, 'state': 'object +0x10', 'multiplier': 214013, 'increment': 2531011,
                        'projection': 'srawi 16 (arithmetic high half)'},
    'weather_object': {'site': 0x904b4, 'state': 'object +0x34', 'multiplier': 1664525, 'increment': 1013904223,
                       'projection': 'full new state, no absolute value'},
}
CLIB_RAND = {'rand_glue': 0x1c5424, 'srand_glue': 0x1c7944, 'srand_site': 0x1c0cc0, 'coast_seed_site': 0x54bf4,
             'advisor_draw_site': 0x7780, 'advisor_anim_draw_site': 0x6420,
             'code': 0x1e78c, 'state': 0x544c, 'initial': 1}
FORCED_STEP = {'request': (0x1c229c, 0x10ec60, 32), 'release': (0x1c22a8, 0x10ecc0), 'capture': (0x1c27a0, 0x1c2d20),
               'gate_slot': -0x5e24, 'interval': (0x10ec98, 1000)}
RECORDER = {'countdown_decrement': (0x10d0e0, 0x10d0e4, 0x10d0e8, 0x30),
            'strings': ('Replaying a timed event at time %d\n', 'Layout load in progress!  Next action in %d game turns\n',
                        'Start Action Recording\n')}

# Bounded-scan expectations (whole code section, one instruction form each).
EXPECTED_SCANS = {
    'world_rng_direct_calls': 142,
    'world_rng_setter_calls': [0xd5b14, 0xd86dc, 0xe79b8, 0xe8fd4, 0xeb160, 0xeb28c, 0xefa30, 0x1a9574],
    'nr_multiplier_sites': [0x904a8, 0x90b78, 0x95134, 0x95344, 0x10532c, 0x1056ec, 0x105868, 0x1058f4, 0x1093d0],
    'ms_multiplier_site_count': 31,
    'ansi_multiplier_sites': [],
    'rng_field_stores': [0x104910, 0x105344, 0x105348, 0x105364, 0x105760, 0x105764, 0x1058a0, 0x1058a4,
                         0x105934, 0x105938, 0x10598c, 0x105990, 0x109404, 0x109408],
    'rng_field_address_takers': [0x106014, 0x1069a4],
    'tick_field_stores': [0x104900, 0x1053a0],
    'tick_field_address_takers': [0x105efc, 0x10688c],
    'rand_calls': 17, 'srand_calls': [0x1c0cc0],
    'coaster_motion_calls': [0x1c262c],
    'seed_label_count': 1,
}


def identified(path: Path, sha: str) -> pef.PEFContainer:
    raw = path.read_bytes()
    require(hashlib.sha256(raw).hexdigest(), sha, f'{path.name} identity')
    return pef.PEFContainer(raw, path.name)


def word(container, address):
    return pef._u32(container.code.data, address)


def pointer(container, slot):
    target = container.relocs.get(container.data_section.index, {}).get(slot)
    if target is None or target.kind != 'section':
        raise pef.PEFError(f'missing section relocation at data:{slot:#x}')
    return target.target, target.addend


def rlwinm(container, address):
    value = word(container, address)
    require(value >> 26, 21, f'rlwinm at code:{address:#x}')
    return (value >> 21 & 31, value >> 16 & 31, value >> 11 & 31, value >> 6 & 31, value >> 1 & 31, value & 1)


def branch(container, address):
    value = word(container, address)
    require(value >> 26, 16, f'conditional branch at code:{address:#x}')
    displacement = value & 0xfffc
    if displacement & 0x8000:
        displacement -= 0x10000
    return (value >> 21 & 31, value >> 16 & 31, displacement if value & 2 else address + displacement)


def x_form(container, address):
    value = word(container, address)
    return (value >> 26, value >> 21 & 31, value >> 16 & 31, value >> 11 & 31, value >> 1 & 0x3ff)


def data_float(container, toc_displacement):
    return struct.unpack_from('>f', container.data_section.data, TOC + toc_displacement)[0]


def direct_calls(container):
    calls = {}
    code = container.code.data
    for offset in range(0, len(code) - 3, 4):
        value = pef._u32(code, offset)
        if value >> 26 == 18 and value & 3 == 1:
            displacement = value & 0x03fffffc
            if displacement & 0x02000000:
                displacement -= 0x04000000
            calls.setdefault(offset + displacement, []).append(offset)
    return calls


def lcg_multiplier_sites(container, multiplier, window=13):
    """lis rD,hi followed within `window` instructions by addi/ori rX,rD,lo forming `multiplier`."""
    code = container.code.data
    sites = []
    for offset in range(0, len(code) - 3, 4):
        value = pef._u32(code, offset)
        if value >> 26 != 15 or value >> 16 & 31:
            continue
        rd, high = value >> 21 & 31, value & 0xffff
        for step in range(1, window + 1):
            if offset + 4 * step + 4 > len(code):
                break
            follow = pef._u32(code, offset + 4 * step)
            low = follow & 0xffff
            if follow >> 26 == 14 and follow >> 16 & 31 == rd:
                total = ((high << 16) + (low - 0x10000 if low & 0x8000 else low)) & 0xffffffff
            elif follow >> 26 == 24 and follow >> 21 & 31 == rd:
                total = (high << 16) | low
            else:
                continue
            if total == multiplier:
                sites.append(offset)
            break
    return sites


def displacement_sites(container, displacement, opcode, exclude_base=2):
    """D-form sites with this displacement; base r2 (TOC) is excluded, other bases are not followed."""
    code = container.code.data
    found, excluded = [], []
    for offset in range(0, len(code) - 3, 4):
        value = pef._u32(code, offset)
        if value >> 26 != opcode:
            continue
        immediate = value & 0xffff
        if (immediate - 0x10000 if immediate & 0x8000 else immediate) != displacement:
            continue
        (excluded if value >> 16 & 31 == exclude_base else found).append(offset)
    return found, excluded


def inspect(bin_root: Path) -> dict:
    app = identified(bin_root / APP_NAME, APP_SHA)
    clib = identified(bin_root / 'libraries' / CLIB_NAME, CLIB_SHA)
    checks = set()

    def d(address, opcode, fields, container=app):
        require(d_fields(container, address, opcode), fields, f'operand fields at code:{address:#x}')
        checks.add((container.name, address))

    def call(address, target):
        require(call_target(app, address), target, f'call at code:{address:#x}')
        checks.add((app.name, address))

    # --- Scheduler substep loop -------------------------------------------------------------
    call(*SUBSTEP['clock_call'])
    d(SUBSTEP['backlog_limit'][0], 11, (0, 0, SUBSTEP['backlog_limit'][1]))
    d(0x1c22d0, 14, (0, 3, -SUBSTEP['backlog_limit'][1]))
    d(SUBSTEP['slice_ms'][0], 14, (0, 3, SUBSTEP['slice_ms'][1]))
    d(SUBSTEP['substep_increment'][0], 14, (0, 3, SUBSTEP['substep_increment'][1]))
    d(SUBSTEP['park_cap'][0], 10, (0, 3, SUBSTEP['park_cap'][1]))
    d(0x1c235c, 14, (0, 3, 1))
    require(x_form(app, 0x1c24c8), (31, 0, 3, 0, 0), 'signed now/previous compare')
    checks.add((app.name, 0x1c24c8))
    for name, (address, mb) in CADENCE_MASKS.items():
        require(rlwinm(app, address), (0, 0, 0, mb, 31, 1), f'{name} substep mask')
        checks.add((app.name, address))
    for name, (address, bo, bi, target) in LOOP_BRANCHES.items():
        require(branch(app, address), (bo, bi, target), f'loop branch {name}')
        checks.add((app.name, address))
    for _, address, target, _ in SUBSTEP_CALLS:
        call(address, target)
    d(0x1c2384, 11, (0, 0, 0)); d(0x1c23ac, 11, (0, 0, 2)); d(0x1c23e0, 11, (0, 0, 1))
    # Phase timestamps consumed by the interpolation alphas: even block and turn block store `previous`.
    d(0x1c2328, 32, (3, 2, -0x5e40)); d(0x1c232c, 36, (0, 3, 0))
    d(0x1c2348, 32, (3, 2, -0x65e4)); d(0x1c234c, 36, (0, 3, 0))
    # Park-work counter is cleared once per callback at the tail, after rendering.
    d(0x1c27a4, 14, (0, 0, 0)); d(0x1c27a8, 36, (0, 18, 0))

    # --- Once-per-callback work and render -----------------------------------------------------
    for address, target, _ in PRE_LOOP_CALLS + POST_LOOP_CALLS:
        call(address, target)
    divisors = {}
    for address, (displacement, value) in INTERPOLATION_DIVISORS.items():
        d(address, 48, (0, 2, displacement))
        require(data_float(app, displacement), value, f'interpolation divisor at code:{address:#x}')
        divisors[address] = value
    render = {}
    for address, (glue, symbol) in RENDER_GLUE.items():
        call(address, glue)
        imported = glue_import(app, glue, TOC)
        require(imported['symbol'], symbol, f'render import at code:{glue:#x}')
        render[address] = imported['symbol']
    # Forced stepping: the capture gate requests a 32 Hz forced clock (1000/32 = 31 ms per frame).
    call(*FORCED_STEP['request'][:2]); d(0x1c2298, 14, (4, 0, FORCED_STEP['request'][2]))
    call(*FORCED_STEP['release'])
    call(*FORCED_STEP['capture'])
    d(0x1c2284, 32, (3, 2, FORCED_STEP['gate_slot'])); d(0x1c2790, 32, (3, 2, FORCED_STEP['gate_slot']))
    d(0x10ec90, 14, (0, 0, FORCED_STEP['interval'][1]))
    require(x_form(app, FORCED_STEP['interval'][0]), (31, 3, 0, 31, 459), 'forced interval divwu')
    checks.add((app.name, FORCED_STEP['interval'][0]))
    capture_import = glue_import(app, 0x1c5964, TOC)  # first import call of capture routine
    require(bytes(app.code.data).count(b'Scr%05ld.tga\0'), 1, 'per-frame capture file pattern')

    # --- Park turn ---------------------------------------------------------------------------
    d(0x10537c, 15, (4, 26, WORLD_HIGH))
    d(0x105398, 32, (3, 4, TICK_DISPLACEMENT)); d(0x10539c, 14, (0, 3, 1)); d(0x1053a0, 36, (0, 4, TICK_DISPLACEMENT))
    d(0x1053a4, 32, (0, 4, WORLD_STATE_DISPLACEMENT)); d(0x1053a8, 11, (0, 0, 4))
    require(branch(app, 0x1053ac), (12, 2, 0x105470), 'world state 4 skips actor pass')
    checks.add((app.name, 0x1053ac))
    d(TURN_THIRTY['magic_high'][0], 15, (3, 0, TURN_THIRTY['magic_high'][1]))
    d(0x10548c, 32, (4, 5, TICK_DISPLACEMENT))
    d(TURN_THIRTY['multiplier'][0], 7, (0, 0, TURN_THIRTY['multiplier'][1]))
    for address, target, _ in TURN_CALLS:
        call(address, target)
    d(0x1056a0, 11, (0, 0, 1)); call(0x1056ac, 0x10536c)
    # Recorder countdown (+0x30) decrements once per park turn in its replay modes.
    a, b, c, field = RECORDER['countdown_decrement']
    d(a, 32, (3, 28, field)); d(b, 14, (0, 3, -1)); d(c, 36, (0, 28, field))
    code_bytes = bytes(app.code.data)
    recorder_strings = {text: code_bytes.count(text.encode('ascii')) for text in RECORDER['strings']}
    for text, count in recorder_strings.items():
        require(count, 1, f'recorder string {text!r}')

    # --- World RNG ----------------------------------------------------------------------------
    generator = WORLD_RNG['generator']
    d(generator, 15, (5, 3, WORLD_HIGH)); d(generator + 4, 15, (3, 0, 25))
    d(generator + 8, 32, (4, 5, RNG_DISPLACEMENT)); d(generator + 12, 14, (0, 3, 0x660d))
    require(x_form(app, generator + 16), (31, 3, 4, 0, 235), 'world RNG mullw'); checks.add((app.name, generator + 16))
    d(generator + 20, 15, (3, 3, 0x3c6f)); d(generator + 24, 14, (3, 3, -0xca1))
    d(generator + 28, 36, (3, 5, RNG_DISPLACEMENT)); d(generator + 32, 36, (3, 5, RNG_DISPLACEMENT))
    d(generator + 36, 32, (3, 5, RNG_DISPLACEMENT))
    d(generator + 40, 11, (0, 3, 0))  # cmpwi: conditional negation follows (wrapping absolute value)
    require((25 << 16) + 0x660d, WORLD_RNG['multiplier'], 'world RNG multiplier')
    require(((0x3c6f << 16) - 0xca1) & 0xffffffff, WORLD_RNG['increment'], 'world RNG increment')
    setter = WORLD_RNG['setter']
    d(setter, 15, (3, 3, WORLD_HIGH)); d(setter + 4, 36, (4, 3, RNG_DISPLACEMENT))
    call(*WORLD_RNG['seed_call']); d(WORLD_RNG['seed_store'], 36, (3, 20, RNG_DISPLACEMENT))
    d(0x1048fc, 14, (21, 0, 0)); d(WORLD_RNG['tick_reset'], 36, (21, 20, TICK_DISPLACEMENT))
    seed_import = glue_import(app, WORLD_RNG['seed_call'][1], TOC)
    require((seed_import['symbol'], seed_import['library']), ('time', 'c/c++ shared'), 'world seed import')
    for key, displacement in (('save', RNG_DISPLACEMENT), ('load', RNG_DISPLACEMENT),
                              ('tick_save', TICK_DISPLACEMENT), ('tick_load', TICK_DISPLACEMENT)):
        site, target, address_taker = WORLD_RNG[key]
        call(site, target); d(address_taker, 14, (4, 4, displacement))
    section, base = pointer(app, WORLD_RNG['label_base_slot'])
    require(section, app.code.index, 'serializer label pool section')
    labels = {}
    for offset, label in WORLD_RNG['labels'].items():
        text = code_bytes[base + offset:base + offset + 32].split(b'\0')[0].decode('ascii').strip()
        require(text, label, f'serializer label +{offset:#x}')
        labels[offset] = text
    d(0x105d54, 32, (29, 2, WORLD_RNG['label_base_slot'] - TOC))
    d(0x106010 - 4, 14, (5, 29, 0x4ef)); d(0x105ef4, 14, (5, 29, 0x486))
    # Thing-ID reseeds: 0xfa9a4 copies the halfword at thing+0 (the guest-phase ID, 0xeed18/0xeed34).
    d(0xfa9a4, 40, (0, 4, 0)); d(0xfa9a8, 44, (0, 3, 0))
    call(0xeed18, 0xfa9a4); require(rlwinm(app, 0xeed34), (0, 0, 0, 30, 31, 0), 'guest ID phase mask')
    checks.add((app.name, 0xeed34))
    for site, copy in RESEED_FROM_THING_ID:
        call(site, setter)
        if copy is not None:
            call(copy, 0xfa9a4)
    d(0xd86c4, 40, (0, 26, 0))  # the remaining site reads thing+0 directly
    call(RESEED_OTHER[0][0], setter); d(0xd5af4, 40, (4, 29, 0x214))
    for site in GUEST_CREATION_DRAWS['before_reseed'] + GUEST_CREATION_DRAWS['after_reseed']:
        call(site, generator)
    call(GUEST_CREATION_DRAWS['reseed'], setter)
    # Inline advances of the same field (no generator call): addis base,world,0x1e then -0x58f8.
    d(0x1056f0, 15, (25, 27, WORLD_HIGH)); d(0x105750, 32, (0, 25, RNG_DISPLACEMENT))
    d(0x105758, 15, (3, 3, 0x3c6f)); d(0x105760, 36, (3, 25, RNG_DISPLACEMENT))

    # --- Generators with their own state ----------------------------------------------------
    d(0x3e30c, 32, (29, 2, 0xcc0 - TOC)); d(0x3e3a8, 32, (0, 29, 0)); d(0x3e3bc, 36, (0, 29, 0))
    require(rlwinm(app, 0x3e3b8), (0, 4, 16, 16, 31, 0), 'coaster logical high half')
    d(0x3e3b0, 15, (3, 3, 0x27)); d(0x3e3b4, 14, (0, 3, -0x613d))
    require(pointer(app, 0xcc0), (app.data_section.index, 622280), 'coaster RNG global')
    d(0x2089c, 15, (3, 0, 3)); d(0x208a0, 32, (4, 31, 0x38)); d(0x208a4, 14, (0, 3, 0x43fd))
    d(0x208b8, 36, (3, 31, 0x38)); require(x_form(app, 0x208b4)[:4], (31, 3, 0, 16), 'kart srawi 16')
    d(0x9d4a8, 32, (5, 4, 0x10)); d(0x9d4c0, 36, (3, 4, 0x10))
    require(x_form(app, 0x9d4bc), (31, 3, 5, 16, 824), 'particle srawi 16')
    d(0x904ac, 32, (5, 3, 0x34)); d(0x904b0, 14, (0, 4, 0x660d)); d(0x904c0, 36, (4, 3, 0x34))
    d(0x904c8, 32, (3, 3, 0x34))
    for address in (0x208b4, 0x9d4bc, 0x3e3b8, 0x9d4c0):
        checks.add((app.name, address))
    rain = [symbol.name for symbol in app.imports if symbol.name == 'gei_Rain']
    require(rain, ['gei_Rain'], 'weather import used by the weather-object caller')

    # --- C library rand -----------------------------------------------------------------------
    rand_import = glue_import(app, CLIB_RAND['rand_glue'], TOC)
    srand_import = glue_import(app, CLIB_RAND['srand_glue'], TOC)
    require((rand_import['symbol'], srand_import['symbol']), ('rand', 'srand'), 'C library imports')
    for site in (CLIB_RAND['coast_seed_site'], CLIB_RAND['advisor_draw_site'], CLIB_RAND['advisor_anim_draw_site']):
        call(site, CLIB_RAND['rand_glue'])
    call(CLIB_RAND['srand_site'], CLIB_RAND['srand_glue']); call(0x1c0ca0, 0x1c5844)
    call(0x7454, 0x10e864)  # the advisor update reads the unscaled clock before drawing
    d(0x1e78c, 14, (4, 2, -11188), clib); d(0x1e790, 15, (3, 0, 16838), clib)
    d(0x1e794, 14, (0, 3, 20077), clib); d(0x1e7a0, 14, (0, 3, 12345), clib)
    require(rlwinm(clib, 0x1e7ac), (0, 3, 16, 17, 31, 0), 'C library 15-bit projection')
    checks.add((clib.name, 0x1e7ac))
    require(pef._u32(clib.data_section.data, CLIB_RAND['state']), CLIB_RAND['initial'], 'C library initial state')

    # --- Bounded scans --------------------------------------------------------------------------
    calls = direct_calls(app)
    rng_stores, rng_store_toc = displacement_sites(app, RNG_DISPLACEMENT, 36)
    rng_takers, _ = displacement_sites(app, RNG_DISPLACEMENT, 14)
    tick_stores, _ = displacement_sites(app, TICK_DISPLACEMENT, 36)
    tick_takers, _ = displacement_sites(app, TICK_DISPLACEMENT, 14)
    seed_labels = len(re.findall(rb'[A-Za-z_]*(?:Seed|Random)[A-Za-z_]*\0', code_bytes))
    scans = {
        'world_rng_direct_calls': len(calls.get(generator, [])),
        'world_rng_setter_calls': sorted(calls.get(setter, [])),
        'nr_multiplier_sites': lcg_multiplier_sites(app, 1664525),
        'ms_multiplier_site_count': len(lcg_multiplier_sites(app, 214013)),
        'ansi_multiplier_sites': lcg_multiplier_sites(app, 1103515245),
        'rng_field_stores': rng_stores, 'rng_field_address_takers': rng_takers,
        'tick_field_stores': tick_stores, 'tick_field_address_takers': tick_takers,
        'rand_calls': len(calls.get(CLIB_RAND['rand_glue'], [])),
        'srand_calls': sorted(calls.get(CLIB_RAND['srand_glue'], [])),
        'coaster_motion_calls': sorted(calls.get(0x3864c, [])),
        'seed_label_count': seed_labels,
    }
    for key, expected in EXPECTED_SCANS.items():
        require(scans[key], expected, f'bounded scan {key}')
    loop = range(0x1c22dc, 0x1c24d0)
    in_loop = {name: [site for site in calls.get(target, []) if site in loop]
               for name, target in (('world_rng', generator), ('world_rng_setter', setter), ('rand', CLIB_RAND['rand_glue']),
                                    ('coaster_motion', 0x3864c), ('animation_cache', 0xa6f70))}
    require(in_loop, {'world_rng': [], 'world_rng_setter': [], 'rand': [], 'coaster_motion': [], 'animation_cache': []},
            'no direct draws, reseeds, coaster motion or clock-cache refresh inside the substep loop')
    require(lcg_multiplier_sites(clib, 1103515245), [0x1e790], 'C library is the only ANSI LCG site')

    return {
        'schema': 1, 'sha256': APP_SHA, 'clib_sha256': CLIB_SHA, 'checked_instruction_count': len(checks),
        'scope': 'identified Feral Mac PEFs, static operands and bounded scans; no execution, no Windows claim',
        'scheduler': {
            'slice_ms_of_scaled_clock': 31, 'backlog_limit_ms': 2000, 'park_turn_every_substeps': 8,
            'park_turns_per_callback_cap': 3, 'compare': 'signed now > previous',
            'substep_order': [{'cadence': cadence, 'site': site, 'target': target, 'role': role}
                              for cadence, site, target, role in SUBSTEP_CALLS],
            'cadence_masks': {name: 32 - mb for name, (_, mb) in CADENCE_MASKS.items()},
            'gate': 'work runs when application flag 8 is set or gameplay flag 1 is clear; time/substep still advance',
        },
        'frame': {
            'pre_loop': [{'site': s, 'target': t, 'role': r} for s, t, r in PRE_LOOP_CALLS],
            'post_loop': [{'site': s, 'target': t, 'role': r} for s, t, r in POST_LOOP_CALLS],
            'interpolation_divisors_ms': sorted(divisors.values()), 'render_imports': sorted(render.values()),
            'forced_step': {'rate_hz': 32, 'interval_ms': 1000 // 32, 'gate_toc_displacement': FORCED_STEP['gate_slot'],
                            'same_gate_calls_capture': True, 'capture_first_import': capture_import['symbol']},
        },
        'park_turn': {
            'tick_field': 'world+0x1da70c (mGameTick), incremented first', 'world_state_4': 'skips actor pass',
            'every_30_turns': 'extra block (0x1091ac/0x1091b8/0xc3684 guarded)',
            'tail_order': [{'site': s, 'target': t, 'role': r} for s, t, r in TURN_CALLS],
            'recorder_strings': sorted(recorder_strings),
        },
        'world_rng': {
            'state': 'uint32 world+0x1da708 (mRandomSeed), adjacent to mGameTick',
            'equation': 'state = state*1664525 + 1013904223 (mod 2^32); returns wrapping signed absolute of new state',
            'seed': 'time(NULL) at world setup 0x10474c (tick reset to 0 just before)',
            'reseed_from_thing_id': [site for site, _ in RESEED_FROM_THING_ID], 'reseed_other': RESEED_OTHER[0][0],
            'guest_creation': GUEST_CREATION_DRAWS, 'saved': True, 'tick_saved': True,
            'direct_call_sites': scans['world_rng_direct_calls'],
        },
        'own_state_generators': OWN_STATE_GENERATORS,
        'clib_rand': {'equation': 'state = state*1103515245 + 12345; returns (state>>16)&32767', 'initial_state': 1,
                      'srand': 'low32(UTimer::GetAbsolute()/1000) at 0x1c0cc0', 'direct_rand_sites': scans['rand_calls'],
                      'consumers_include': 'coaster seed 0x54bf4, advisor per-frame 0x7780, advisor anim 0x6420'},
        'bounded_scans': {**scans, 'excluded_toc_based_rng_displacement_stores': rng_store_toc},
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('bin_root', type=Path, help='Feral Mac bin directory (SimThemePark.data + libraries/)')
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.bin_root), indent=2, sort_keys=True, default=str))
    except (OSError, pef.PEFError) as error:
        parser.exit(1, f'determinism witness: {error}\n')


if __name__ == '__main__':
    main()
