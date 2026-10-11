"""Bounded static coaster geometry, motion and build-money witnesses (COAST-R2); never execute originals.

Usage: python3 -I coast_geometry_evidence.py /path/to/mac-feral/bin

Follow-up to coast_evidence.py (COAST-R). Addresses are section-relative offsets in the identified Mac
`SimThemePark.data`. Instructions are decoded into fields and compared with expected operands; only
digests, decoded fields, constants and derived values are reported. The pure functions restate the
decoded arithmetic (single precision where the binary uses lfs/fadds/fmuls/fmadds) so tests can pin
it without the binary. See docs/reverse/COAST-plan.md sections 12-19 for the narrative and bounds.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import sys

HERE = Path(__file__).resolve().parent
TOOLS = HERE.parents[1]
REPO = TOOLS.parents[1]
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(TOOLS))
import pef  # noqa: E402
import coast_evidence as ce  # noqa: E402
from timer_evidence import call_target, d_fields, load_identified, require  # noqa: E402

f32, fmadds = ce.f32, ce.fmadds

# Coaster module bounds used by the bounded scans (definition defaults .. last coaster helper).
MODULE = (0x2d000, 0x4bd64)

# name -> (code start, code end exclusive, sha256 of that byte range)
BLOCKS = {
    'node_tangents': (0x31450, 0x32014, 'df414363194e20a86376d46145b10bf0e5093bc71b77eac79d1a12c6fcc5cea3'),
    'knot_tangents': (0x32298, 0x32990, 'f336f3f2454d3f5b404e1d91c5da967e0f7f0370f98ed251b3a6347ac190ffd3'),
    'knot_chords': (0x32990, 0x32cf0, 'a9cdb8616dd6b2ae9fecb0192cffa806e908d6ad3951b207f2987e33db491953'),
    'knot_tags': (0x32f80, 0x33398, '3e3b8a5f804a55e934e4b3d174e576da040584d1bc86d3b619a8ab4402eeac6d'),
    'node_layout': (0x33398, 0x337b4, 'f7a4192c0c9523037fc360ac3e649dd2939229261d67f5a50ab865a19c905b05'),
    'speed_estimate': (0x3f36c, 0x3f3f0, 'eaace3dd2aa8bf704f6f726634fa0df4bd67c675c86d5573a3caae19ac7925f9'),
    'auto_bank': (0x3f548, 0x3f618, 'f68120e03ef3438c0ba6d16ec49a547c33da0bc604a109be9c4ee0eba421a184'),
    'bezier': (0x41b00, 0x41c0c, '4f0f038d464f538e63eed69945c8994e8246f14237f0e637b9e0d1889771515b'),
    'sampler': (0x44ff4, 0x45970, 'b50bdf84a574942033792dbeb6033be931c03ab879117bcafcaefa24d4df97e6'),
    'sampler_helpers': (0x44e50, 0x44ff4, 'da0aa1b96f8ab8b70a37a91ef926632c4696c6009b8daab06ecc40ada5d068b7'),
    'controller_ctor': (0x389b4, 0x38bc0, 'cd1bd157663272b55e91c47ac9a3f7885c231e6f2b2b2ff4a534b569fa0552fa'),
    'speed_setting': (0x3fb68, 0x3fc18, 'fec9ec5d6c74a65c8c974f03855e899017718c85378f6e7f2cabe00ac52be3df'),
    'train_tick': (0x40650, 0x41510, '000a90d3433fd6244ebcd276364b46703226777540fbd7da7625b470c12be865'),
    'hop_length': (0x4316c, 0x431c8, '79ffa35bf7fd625b5edc48626724340261e0121195ab7dabd89a15a930f61400'),
    'power_state': (0x38250, 0x3864c, 'f1f05c2796ea107256bb173887093d6f24c78ba381c5d2f2763f953682470976'),
    'place_pylon': (0x375c4, 0x37968, 'b93d3cd084bfe333df2843c0f9064ce664109bd3ceea22734062648a63040bf9'),
    'close_circuit': (0x470c4, 0x47170, '8f509fcd46786c4d08192ec621e58a75fde5c021c7cbf099188f91d260fad5ac'),
    'station_end_nodes': (0x37cc8, 0x37e8c, '56278baf176de06de42261a0add73f422b94752c810c66aee39f4662368e0bbe'),
    'drop_node': (0x4a43c, 0x4a7e4, 'd22810b2cc7e2323ebba7372260465805c38c639141aa582c83efae8f2bf28b4'),
    'cancel_drag': (0x4a7e4, 0x4a954, '8518fc0360efb238e51efd532fe5ddd293295d04d5dd52b6dc45ab75569767b8'),
    'height_edit': (0x45fc4, 0x462f8, '59dddcb6124ec8fd11c3624d5f409382b6013d3507e912386f0d92b3427b18ca'),
    'loop_slider': (0x462f8, 0x46508, 'cd2d1e8394c330369c2a2b6e0c249a36e6652da99ab10fa2a8e759b4533ef67a'),
    'rotate_slider': (0x46508, 0x46718, '98f8aeb130db625d3baf836d3596579d77a677d0b6c00c1a7a77c3c243627c5b'),
    'delete_nodes': (0x47170, 0x47710, 'f95f19ece48f04f1d326d92d49d35a705dcc6e1907af115210693c192a26577d'),
    'remove_node': (0x492d8, 0x4967c, '1ec7aae41f51dd4aa447ce9f9ffe4f3b05d561f0df566b89e976b4bfe200b109'),
    'snapshot_create': (0x4db9c, 0x4dcb8, '8d9c0f262d52319079b94a6e0004abfd21a2d0be05a8b49bfc85848a180402b7'),
    'snapshot_refresh': (0x4dcb8, 0x4dd18, 'bb1d702ae3f1a843252dacb4ca9f03f677461eb203fc4184b67c90550e965283'),
    'height_query_void': (0x504f0, 0x50560, '17d249ec0358d9cdc400d9f041afa75f8a010ed1584633502dc18f0f6019a51d'),
    'sine_table': (0xa3cc8, 0xa3da0, 'aa13eaca11d2ff7eed666e30e33b0263da3a002534144637b33349f5228ec5b4'),
    'money_credit': (0xcbf50, 0xcbfdc, 'ce1abfa448328bbcff7af8a73340a3b25b03f8c8e7590ec8aee42cb0794e304c'),
    'money_debit': (0xcbfdc, 0xcc084, 'c85be4bd002fa2895bca0c6e26b26678a20ad9a4cee8383f718b593163941105'),
    'float_to_unsigned': (0x1c3fbc, 0x1c400c, '242861fcbec29240df00b763eb02bb74ef39137487996682d51ebf8a95bb379e'),
    'build_tick': (0x47e4c, 0x483ac, '530bc8653189f3ab9d641b99affcb5c56ac4ffaac4f860d691ac6f14fdc9ccb6'),
    'tile_paint': (0x99d2c, 0x99fa4, 'b3a300da0d73dee22edd81be51a74cd6966e415278ac282a24886416563aef82'),
}

# (code offset, primary opcode, (rD/rS, rA, signed immediate), meaning). Decoded, never raw words.
FIELDS = (
    # Node layout 0x33398: cell centre and pylon height.
    (0x333ec, 48, (24, 2, -13656), 'half-cell offset 5.0 added to the cell corner'),
    (0x3343c, 36, (3, 24, 76), 'node +76 X from descriptor +28 (10 x cell x)'),
    (0x33484, 48, (0, 26, 196), 'stacked pylon: mesh stack offset pylon +196'),
    # Node tangents 0x31450.
    (0x314bc, 32, (3, 24, 104), 'previous node'),
    (0x314d0, 32, (7, 24, 100), 'next node'),
    (0x314e0, 32, (7, 4, 100), 'closing node: next is the node after the track head'),
    (0x314f0, 14, (27, 24, 76), 'node +76 position'),
    (0x314f4, 14, (29, 24, 64), 'node +64 backward control point'),
    (0x314f8, 14, (28, 24, 88), 'node +88 forward control point'),
    (0x3161c, 48, (0, 26, 176), 'fPylonStraightDotThreshold (pylon +176)'),
    (0x31640, 48, (2, 25, 12), 'fStraightDivisor (definition +12)'),
    (0x31a1c, 52, (0, 24, 148), 'node +148 signed XZ turn angle'),
    (0x31a54, 48, (0, 2, -13700), 'straight threshold without a pylon -0.999'),
    (0x31a60, 48, (0, 2, -13784), 'fold-back threshold 0.999'),
    (0x31a9c, 48, (6, 2, -13680), 'straight tangent factor 0.5'),
    (0x31ccc, 48, (2, 25, 8), 'fCurviness (definition +8), loop pylon'),
    (0x31e58, 48, (2, 25, 8), 'fCurviness (definition +8), curved node'),
    (0x31f2c, 36, (3, 24, 40), 'node +40 backward tangent'),
    (0x31f44, 36, (3, 24, 52), 'node +52 forward tangent'),
    (0x31f58, 48, (2, 2, -13684), 'control point = position + tangent / 3'),
    # Knot tangents 0x32298 and chords 0x32990.
    (0x3233c, 48, (29, 25, 8), 'knot tangent: fCurviness'),
    (0x324e8, 48, (0, 2, -13792), 'loop knot tangent factor 2.1'),
    (0x32650, 48, (0, 2, -13808), 'loop-to-plain transition factor 1.7'),
    (0x32808, 48, (2, 25, 8), 'plain knot: fCurviness x backward chord'),
    (0x32864, 48, (2, 25, 8), 'plain knot: fCurviness x forward chord'),
    (0x328a4, 48, (2, 2, -13684), 'knot control point divisor 3'),
    (0x328b4, 52, (0, 23, 52), 'knot +52 backward control point'),
    (0x32938, 52, (0, 23, 76), 'knot +76 forward control point'),
    (0x32a34, 48, (2, 2, -13812), 'chord height weight 0.7'),
    # Bezier 0x41b00.
    (0x41b00, 48, (2, 3, 76), 'A +76 forward control'),
    (0x41b08, 48, (3, 2, -13324), 'Bernstein 3'),
    (0x41b10, 48, (8, 2, -13336), 'Bernstein -6'),
    (0x41b18, 49, (9, 3, 64), 'A +64 start point (lfsu)'),
    (0x41b1c, 48, (0, 2, -13332), 'Bernstein -1'),
    (0x41b24, 48, (10, 4, 52), 'B +52 backward control'),
    (0x41b2c, 48, (2, 2, -13328), 'Bernstein -3'),
    (0x41b34, 49, (6, 4, 64), 'B +64 end point (lfsu)'),
    # Sampler 0x44ff4 / 0x44ee4 and the spacing constant.
    (0x38ab8, 48, (0, 2, -13684), 'sample spacing 3.0'),
    (0x38abc, 52, (0, 30, 172), 'controller +172 sample spacing'),
    (0x44f5c, 48, (30, 2, -13324), 'capacity estimate 3 x chord / spacing + 1'),
    (0x450ac, 48, (23, 25, 172), 'sampler spacing'),
    (0x45100, 48, (2, 2, -13420), 'capacity growth 1.1'),
    (0x45144, 48, (19, 2, -13424), 'parameter step 0.05'),
    (0x45318, 52, (21, 28, 52), 'sample +52 curve parameter'),
    # Auto-banking 0x3f548, speed estimate 0x3f36c and gates.
    (0x2d2d0, 48, (0, 2, -13640), 'definition +16 default 4.0 (bank divisor)'),
    (0x2d2d4, 52, (0, 31, 16), 'definition +16'),
    (0x3352c, 48, (26, 21, 72), 'bank speed estimate floor fMinSpeed'),
    (0x33634, 52, (1, 24, 216), 'node +216 bank'),
    (0x3f578, 48, (0, 4, 148), 'node turn angle'),
    (0x3f5c8, 48, (1, 2, -13488), 'pi for a non-negative turn angle'),
    (0x3f5d0, 48, (1, 2, -13492), '-pi for a negative turn angle'),
    (0x3f5dc, 48, (0, 3, 16), 'bank divisor definition +16'),
    (0x3f37c, 48, (3, 3, 36), 'estimate: fAccelerationPerHeight'),
    (0x3f380, 48, (4, 3, 40), 'estimate: fUphillAccelModifier'),
    (0x3f3bc, 48, (4, 3, 44), 'estimate: fDownhillAccelModifier'),
    (0x3023c, 24, (0, 0, 8), 'bIsSuspendedCoaster sets definition flag 0x8'),
    # Suspended swing (rating pass tail).
    (0x4030c, 48, (0, 3, 108), 'swing rate fForceMultiplier'),
    # Speed setting 0x3fb68 and default.
    (0x38b2c, 14, (4, 0, 60), 'default speed setting 60'),
    (0x3fbb8, 48, (3, 2, -13512), 'speed setting percent 100'),
    (0x3fbc4, 48, (0, 5, 80), 'fMaxSpeedAtMaxSetting'),
    (0x3fbc8, 48, (2, 5, 76), 'fMaxSpeedAtMinSetting'),
    (0x3fbdc, 52, (0, 30, 52), 'controller +52 speed cap'),
    # Station braking start (rating pass).
    (0x40078, 32, (3, 3, 252), 'last node sample index'),
    (0x40090, 48, (0, 31, 172), 'braking start: speed / spacing samples'),
    (0x400b4, 52, (0, 31, 308), 'controller +308 braking start sample'),
    # Train tick 0x40650.
    (0x406d8, 48, (2, 28, 44), 'controller +44 power factor'),
    (0x406dc, 48, (0, 30, 72), 'fMinSpeed floor'),
    (0x406e0, 48, (1, 30, 68), 'fWinchSpeed floor'),
    (0x40738, 48, (0, 2, -13496), 'milliseconds per second'),
    (0x40868, 48, (23, 2, -13556), 'hop length half a sample'),
    (0x40888, 48, (27, 2, -13548), 'slow-speed decay threshold 2.0'),
    (0x4088c, 48, (28, 2, -13564), 'stop threshold 0.01'),
    (0x40a40, 48, (1, 30, 36), 'tick: fAccelerationPerHeight'),
    (0x40a44, 48, (2, 30, 40), 'tick: fUphillAccelModifier'),
    (0x40a7c, 48, (2, 30, 44), 'tick: fDownhillAccelModifier'),
    (0x40bcc, 48, (5, 28, 308), 'braking start sample'),
    (0x40c70, 48, (0, 30, 84), 'tick: fFrictionMultiplier'),
    (0x40f30, 48, (1, 2, -13560), 'slow-speed decay 0.95 per hop'),
    # Power factor 0x38250.
    (0x38524, 48, (0, 2, -13628), 'power factor 0 on entering mode 64'),
    (0x38528, 52, (0, 29, 44), 'controller +44 = 0'),
    (0x38594, 48, (0, 2, -13648), 'power factor 1 on leaving mode 64'),
    (0x3859c, 52, (0, 29, 44), 'controller +44 = 1'),
    # Blocked cell snapshot (validator) and height query.
    (0x36cd0, 32, (4, 2, -29688), 'validator reads the map snapshot'),
    (0x36cd8, 32, (4, 4, 44), 'snapshot cell array'),
    (0x50538, 48, (1, 2, -13116), 'height query returns FLT_MAX for a bit-0 cell'),
    # Build tool: invalid placements are flagged, not refused.
    (0x48344, 24, (0, 0, 16384), 'failed validation flags the node 0x4000'),
    # Money and edit sliders.
    (0x37864, 14, (4, 0, 1), 'new pylon cost includes fCostPerUnit'),
    (0x4a404, 52, (1, 3, 0), 'pick-up stores the unrounded node cost'),
    (0x46684, 48, (0, 7, 148), 'rotation slider: yaw - fMaxRotnAngleDeviance'),
    (0x466a8, 48, (0, 2, -13288), 'rotation slider step 0.02'),
    (0x46484, 48, (0, 2, -13268), 'loop slider minimum -0.35 (end node)'),
    (0x46498, 48, (0, 2, -13276), 'loop slider minimum 0.15'),
    (0x464a0, 48, (0, 2, -13280), 'loop slider maximum 0.85'),
    (0x464a8, 48, (0, 2, -13284), 'loop slider step 0.01'),
    # Sine table 0xa3cc8.
    (0x4b720, 14, (3, 0, 4096), 'sine table size 4096'),
    (0xa3d18, 48, (31, 2, -11968), 'sine table 2 pi'),
    # Click dispatch in the ride UI.
    (0x73a3c, 14, (3, 0, 129), 'build click: cell action 129 decides complete or add'),
)

CALLS = {
    0x44a98: 0x33398, 0x44aac: 0x44ff4, 0x44ab4: 0x3fc18, 0x33508: 0x31450, 0x33630: 0x3f548,
    0x3364c: 0x3f36c, 0x336f4: 0x32014, 0x3371c: 0x32f80, 0x33770: 0x32cf0, 0x32ea4: 0x32990,
    0x32f14: 0x32298, 0x44e90: 0x41b00, 0x451c8: 0x44e50, 0x451e4: 0x44ea4, 0x4551c: 0x44ce8,
    0x452ac: 0x1c5d54, 0x40ba0: 0x4316c, 0x406c4: 0xa3e08, 0x38b30: 0x3fb68, 0x376cc: 0x470c4,
    0x37888: 0x4b168, 0x378b4: 0x1c3fbc, 0x378c0: 0xcbfdc, 0x4a5f0: 0x41aa4, 0x4a644: 0xcbf50,
    0x4a674: 0xcbfdc, 0x46284: 0x41aa4, 0x462ac: 0xcbf50, 0x462c0: 0xcbfdc, 0x4a3f0: 0x4b168,
    0x37f44: 0x37cc8, 0x47538: 0x37e8c, 0x474dc: 0x492d8, 0x54efc: 0x4dcb8, 0x11b48c: 0x54ee0,
    0x4daac: 0x4db9c, 0x4b72c: 0xa3cc8, 0x73a90: 0x45f5c, 0x73a9c: 0x37ef0, 0x45f80: 0x375c4,
    0x482ec: 0x36ba4,
}

IMPORTS = {0x1c4ddc: 'sin', 0x1c5d84: 'atan2', 0x1c5544: '__fpclassifyf'}

# rlwinm (rS, rA, SH, MB, ME): the masks that select flags.
MASKS = {
    0x33544: (0, 25, 0, 27, 27, 'definition flag 0x10 enables auto-banking'),
    0x30248: (0, 0, 0, 28, 26, 'bIsSuspendedCoaster clears the auto-bank flag 0x10'),
    0x400dc: (0, 0, 0, 28, 28, 'definition flag 0x8 (suspended) runs the swing pass'),
    0x40a18: (0, 0, 0, 20, 20, 'section flag 0x800 selects the winch floor in the tick'),
    0x3769c: (0, 0, 0, 30, 30, 'controller +60 bit 1 (circuit open) allows closing'),
    0x47144: (0, 0, 0, 31, 29, 'closing clears controller +60 bit 1'),
    0x37cec: (0, 0, 0, 26, 26, 'bVisibleEndPylon (0x20): one station end node, else two'),
    0x48308: (0, 0, 0, 18, 16, 'passing validation clears node flag 0x4000'),
    0x50530: (0, 0, 0, 31, 31, 'height query tests cell bit 0'),
    0x36ce8: (3, 3, 0, 31, 31, 'validator tests snapshot cell bit 0'),
    0x31c88: (0, 0, 0, 17, 17, 'pylon flag 0x4000 (loop pylon) selects the bisector tangent'),
    0x31d40: (0, 0, 0, 28, 28, 'definition flag 0x8 marks inverted loop nodes'),
}

TOC_CONSTANTS = {
    -13784: 0.9990000128746033, -13792: 2.0999999046325684, -13808: 1.7000000476837158,
    -13812: 0.699999988079071, -13324: 3.0, -13336: -6.0, -13332: -1.0, -13328: -3.0,
    -13420: 1.100000023841858, -13424: 0.05000000074505806, -13428: 0.0010000000474974513,
    -13488: 3.1415927410125732, -13492: -3.1415927410125732, -13512: 100.0, -13556: 0.5, -13548: 2.0,
    -13564: 0.009999999776482582, -13560: 0.949999988079071, -13116: 3.4028234663852886e+38,
    -13288: 0.019999999552965164, -13268: -0.3499999940395355, -13272: 0.3499999940395355,
    -13276: 0.15000000596046448, -13280: 0.8500000238418579, -13284: 0.009999999776482582,
    -11968: 6.2831854820251465, -13656: 5.0, -13684: 3.0, -13700: -0.9990000128746033, -13680: 0.5,
    -13640: 4.0, -13496: 1000.0, -13628: 0.0, -13648: 1.0,
}

TOC_POINTERS = {-29688: 0x66f78, -30340: 0x1577c0, -29908: 0x66f74}

# Bounded scans over the whole code section (D-form immediates only, stack-relative r1 accesses
# skipped; computed or indexed access is not excluded). Each value is the exact set of offsets found.
SCANS = {
    # The only references to the map snapshot pointer: validator, level load (create), free,
    # game-start refresh, tile restore (two). Nothing else writes the snapshot.
    'snapshot_pointer_loads': {0x36cd0, 0x4daa8, 0x4dd28, 0x54eec, 0x99c98, 0x99d34},
    # stfs to +172 in the coaster module: definition/pylon defaults, the pylon loader, and the
    # controller constructor (the only controller spacing write).
    'module_stfs_172': {0x2d528, 0x2d5d8, 0x2fa68, 0x38abc},
    # Money credit (0xcbf50) calls inside the coaster module: height edit and node drop only.
    'module_credit_calls': {0x462ac, 0x4a644, 0x4a660, 0x4a6c4},
    # Float loads at +112 (pylon fMaxHeightDiffPerGSquareDist) inside the coaster module: none.
    'module_lfs_112': set(),
    # Float loads at +152 inside the coaster module: node records in 0x3d1b8 only.
    'module_lfs_152': {0x3d264, 0x3d31c},
    # Any D-form access at settings offset 15060 (fAutoBankDivisor): none in the whole code.
    'any_15060': set(),
}

NO_MONEY_BLOCKS = ('delete_nodes', 'remove_node', 'cancel_drag', 'close_circuit', 'station_end_nodes')
MONEY_TARGETS = (0xcbf50, 0xcbfdc, 0x7bcb8)


# ---------------------------------------------------------------- pure restatements

SPACING = 3.0          # controller +172, written only by the constructor 0x389b4
PARAMETER_STEP = f32(0.05)
CELL = 10.0            # world units per map cell (descriptor +28/+36 = 10 x cell)
HALF_CELL = 5.0
CHORD_HEIGHT_WEIGHT = f32(0.7)
LOOP_TANGENT = f32(2.1)
LOOP_EXIT_TANGENT = f32(1.7)
BANK_DIVISOR = 4.0     # definition +16; fAutoBankDivisor (settings 15060) is never copied
DEFAULT_SPEED_SETTING = 60
HOP = 0.5              # samples per motion hop
DECAY_BELOW = 2.0
DECAY = f32(0.95)
STOP_BELOW = f32(0.01)
SINE_SIZE = 4096
TWO_PI = f32(2 * math.pi)
HALF_PI = f32(math.pi / 2)


def sine_table(size: int = SINE_SIZE) -> tuple[list[float], int, float]:
    """0xa3cc8: (table, mask, scale); entry k = frsp(sin(f32(f32(k / size) * 2pi)))."""
    table = [f32(math.sin(f32(f32(f32(k) / f32(size)) * TWO_PI))) for k in range(size)]
    return table, size - 1, f32(f32(size) / TWO_PI)


_TABLE = sine_table()


def tsin(angle: float) -> float:
    """Table sine as used by 0x41e24/0x32f80: T[trunc(f32(a * scale)) & mask]."""
    table, mask, scale = _TABLE
    return table[int(f32(angle * scale)) & mask]


def tcos(angle: float) -> float:
    """Table cosine: the sine of (pi/2 + a)."""
    table, mask, scale = _TABLE
    return table[int(f32(f32(HALF_PI + angle) * scale)) & mask]


def bezier_component(p0: float, p1: float, p2: float, p3: float, t: float) -> float:
    """0x41b00 for one axis, in the binary's power-form operation order (single precision)."""
    c1 = f32(3.0 * p1)
    c2 = f32(-6.0 * p1)
    c3 = fmadds(-1.0, p0, c1)
    c2 = fmadds(3.0, p0, c2)
    c3 = fmadds(-3.0, p2, c3)
    c2 = fmadds(3.0, p2, c2)
    c1 = fmadds(-3.0, p0, c1)
    c3 = f32(p3 + c3)
    c2 = f32(t * c2)
    c3 = f32(t * c3)
    c2 = f32(t * c2)
    c3 = f32(t * c3)
    value = fmadds(t, c3, c2)
    value = fmadds(t, c1, value)
    return f32(p0 + value)


def bezier(p0, p1, p2, p3, t: float) -> tuple[float, float, float]:
    """P0 = knot A +64, P1 = A +76 (forward control), P2 = B +52 (backward control), P3 = B +64."""
    return tuple(bezier_component(a, b, c, d, t) for a, b, c, d in zip(p0, p1, p2, p3))


def sub(a, b):
    return tuple(f32(x - y) for x, y in zip(a, b))


def length(v) -> float:
    return f32(math.sqrt(sum(x * x for x in v)))


def chord(a, b) -> float:
    """0x32990: knot distance with the height difference weighted by 0.7."""
    dx = f32(b[0] - a[0])
    dy = f32(f32(b[1] * CHORD_HEIGHT_WEIGHT) - f32(a[1] * CHORD_HEIGHT_WEIGHT))
    dz = f32(b[2] - a[2])
    return f32(math.sqrt(dx * dx + dy * dy + dz * dz))


def knot_controls(position, direction, back_chord: float, forward_chord: float, curviness: float = 1.25):
    """0x32298 plain knot: T = dir * (C * chord / |dir|); controls = P -/+ T / 3 (knot +52, +76)."""
    norm = length(direction)
    back = f32(f32(curviness * back_chord) / norm)
    forward = f32(f32(curviness * forward_chord) / norm)
    t_back = tuple(f32(-d * back) for d in direction)
    t_forward = tuple(f32(d * forward) for d in direction)
    control_back = tuple(f32(p + f32(t / 3.0)) for p, t in zip(position, t_back))
    control_forward = tuple(f32(p + f32(t / 3.0)) for p, t in zip(position, t_forward))
    return control_back, control_forward


def loop_knot_factor(curviness: float, side: float, loop_rotate: float) -> float:
    """0x3232c..0x325d0 on a loop pylon knot: K = (C(1-x^2) + 2.1x^2)(1-w) + 2.1w.

    x = clamp(|heading . yaw axis|, 0, 1); w = (1 + cos(2 pi u)) / 2 with u = clamp(2 fmod(r, 0.5), 0, 1)
    for the loop-rotate parameter r (node +248). Table trigonometry.
    """
    x = min(max(abs(side), 0.0), 1.0)
    x2 = f32(x * x)
    k = fmadds(curviness, f32(1.0 - x2), f32(LOOP_TANGENT * x2))
    u = min(max(f32(2.0 * f32(math.fmod(loop_rotate, 0.5))), 0.0), 1.0)
    w = f32(f32(1.0 + tsin(fmadds(TWO_PI, u, HALF_PI))) * 0.5)
    return fmadds(k, f32(1.0 - w), f32(LOOP_TANGENT * w))


def normalize(v):
    n = length(v)
    return tuple(f32(x / n) for x in v)


def cross(a, b):
    return (f32(a[1] * b[2] - a[2] * b[1]), f32(a[2] * b[0] - a[0] * b[2]), f32(a[0] * b[1] - a[1] * b[0]))


def node_tangents(prev, position, nxt, curviness: float = 1.25, straight_divisor: float = 1.25,
                  straight_dot: float | None = f32(-0.999), loop_pylon: bool = False, end_flag: bool = False):
    """0x31450: (backward tangent node +40, forward tangent node +52) for one node.

    straight_dot is the node pylon's fPylonStraightDotThreshold (None for a pylon-less node). Only the
    forward direction survives into the track (0x32298 rescales every knot); the magnitudes matter for
    the node +64/+88 control points and the pylon yaw/pitch (0x420f0).
    """
    if prev is None and nxt is None:
        return None
    if prev is None:
        b = sub(nxt, position)
        forward = tuple(f32(x / straight_divisor) for x in b)
        return tuple(f32(-x) for x in forward), forward
    a = sub(prev, position)
    if nxt is None:
        back = tuple(f32(x / straight_divisor) for x in a)
        return back, tuple(f32(-x) for x in back)
    b = sub(nxt, position)
    la, lb = length(a), length(b)
    ua, ub = normalize(a), normalize(b)
    dot = f32(ua[0] * ub[0] + ua[1] * ub[1] + ua[2] * ub[2])
    straight = (dot < straight_dot) if straight_dot is not None else (dot < f32(-0.999))
    if straight or dot > f32(0.999):
        if straight_dot is not None and not loop_pylon:
            half = tuple(f32(f32(f32((x - y) * 0.5)) / straight_divisor) for x, y in zip(ua, ub))
            if end_flag:
                return tuple(f32(-h * la) for h in half), tuple(f32(h * lb) for h in half)
            return tuple(f32(h * la) for h in half), tuple(f32(-h * lb) for h in half)
        return (tuple(f32(x / straight_divisor) for x in a), tuple(f32(x / straight_divisor) for x in b))
    bisector = tuple(f32(x + y) for x, y in zip(ua, ub))
    if loop_pylon:
        return (tuple(f32(s * f32(la * curviness)) for s in bisector),
                tuple(f32(-s * f32(lb * curviness)) for s in bisector))
    tangent = cross(normalize(bisector), normalize(cross(a, b)))
    return (tuple(f32(t * f32(la * curviness)) for t in tangent),
            tuple(f32(-t * f32(lb * curviness)) for t in tangent))


def turn_angle_xz(prev, position, nxt) -> float:
    """0x31984..0x31a34: node +148 = +-acos(clamp(a_xz . b_xz, -1, 1)), negative when cross < 0."""
    a, b = sub(prev, position), sub(nxt, position)
    la = f32(math.sqrt(a[0] * a[0] + a[2] * a[2]))
    lb = f32(math.sqrt(b[0] * b[0] + b[2] * b[2]))
    ax, az, bx, bz = f32(a[0] / la), f32(a[2] / la), f32(b[0] / lb), f32(b[2] / lb)
    dot = min(max(f32(ax * bx + az * bz), -1.0), 1.0)
    turn = f32(math.acos(dot))
    side = f32(az * bx - ax * bz)
    return f32(-turn) if side < 0.0 else turn


def sample_chain(knots, spacing: float = SPACING):
    """0x44ff4 over an open chain of knots (P, control_back, control_forward).

    The parameter advances in steps of 0.05 per knot pair, the chord lengths between evaluations are
    summed, and a sample is placed on the current chord where the sum reaches the spacing. The step
    then realigns the parameter to the 0.05 grid. Returns [(position, pair index, t)].
    The closing pair of a complete track (last knot to the second node) is not modelled here.
    """
    step = PARAMETER_STEP
    pair = 0
    def point(index, t):
        a, b = knots[index], knots[index + 1]
        return bezier(a[0], a[2], b[1], b[0], t)
    previous = point(0, 0.0)
    t_previous, t, step_now = 0.0, step, step
    distance = distance_before = 0.0
    first = True
    samples = []
    pairs = len(knots) - 1
    while pair < pairs:
        current = point(pair, t)
        d = f32(math.sqrt(sum(f32(c - p) ** 2 for c, p in zip(current, previous))))
        distance = f32(distance + d)
        if first or distance >= spacing:
            a = 0.0 if first else f32(f32(spacing - distance_before) / d)
            first = False
            b = f32(1.0 - a)
            t_previous = fmadds(t_previous, b, f32(t * a))
            previous = tuple(fmadds(p, b, f32(c * a)) for p, c in zip(previous, current))
            samples.append((previous, pair, t_previous))
            distance = 0.0
            step_now = f32(step - f32(math.fmod(t_previous, step)))
            t = f32(t_previous + step_now)
            step_now = step
        else:
            t_previous = t
            t = f32(t + step_now)
            previous = current
        if t > 1.0:
            pair += 1
            t_previous = f32(t_previous - 1.0)
            t = f32(t - 1.0)
        distance_before = distance
    return samples


def speed_estimate(definition: dict, speed: float, floor: float, y_now: float, y_next: float) -> float:
    """0x3f36c: max(mod * (PerHeight * (y_now - y_next)) + v, floor); uphill uses +40, else +44."""
    modifier = definition['fUphillAccelModifier' if y_next > y_now else 'fDownhillAccelModifier']
    value = fmadds(modifier, f32(definition['fAccelerationPerHeight'] * f32(y_now - y_next)), speed)
    return floor if value < floor else value


def auto_bank(speed: float, turn: float, back_length: float, forward_length: float,
              divisor: float = BANK_DIVISOR) -> float:
    """0x3f548: v * (+-pi - theta) / divisor / (|prev - P| + |next - P|); 0 when theta == 0."""
    if turn == 0.0:
        return 0.0
    pi = f32(math.pi) if turn >= 0.0 else f32(-math.pi)
    return f32(f32(f32(speed * f32(pi - turn)) / divisor) / f32(back_length + forward_length))


def speed_cap(setting: int, definition: dict) -> float:
    """0x3fb68: MaxAtMin * (1 - s/100) + MaxAtMax * (s/100); controller +52."""
    s = f32(f32(float(setting)) / 100.0)
    return fmadds(definition['fMaxSpeedAtMinSetting'], f32(1.0 - s),
                  f32(definition['fMaxSpeedAtMaxSetting'] * s))


def braking_start(sample_count: int, speed_before_end: float, spacing: float = SPACING) -> float:
    """0x40070..0x400d4: controller +308 = max((n - 1) - v / spacing, 0) in samples."""
    return max(f32(f32(float(sample_count - 1)) - f32(speed_before_end / spacing)), 0.0)


def brake_blend(speed: float, floor: float, front: float, start: float, total: float) -> float:
    """0x40bb8..0x40c24: x = (front - start)/(n - start), 1 outside [0, 1]; v(1 - x^4) + floor x^4."""
    x = f32(f32(front - start) / f32(total - start))
    if x < 0.0 or x > 1.0:
        x = 1.0
    x4 = f32(f32(x * x) * f32(x * x))
    return fmadds(speed, f32(1.0 - x4), f32(floor * x4))


def hop_speed(definition: dict, speed: float, y_now: float, y_half: float, floor: float, cap: float,
              friction: bool = False) -> float:
    """One car, one half-sample hop of 0x40650: height step, friction, floor, cap (no braking)."""
    modifier = definition['fUphillAccelModifier' if y_half > y_now else 'fDownhillAccelModifier']
    value = fmadds(modifier, f32(definition['fAccelerationPerHeight'] * f32(y_now - y_half)), speed)
    value = max(value, floor)
    if friction:
        value = f32(value * definition['fFrictionMultiplier'])
    value = max(value, floor)
    return min(value, cap)


def advance(definition: dict, heights, position: float, speed: float, dt: float, floor: float,
            cap: float, hop_length: float = SPACING * HOP):
    """One train with one car over a uniform track, one frame of 0x40650.

    `heights(s)` gives the car height at sample position s. Full hops of half a sample consume
    hop_length / ((v_old + v_new) / 2) seconds; the last hop is partial and interpolates the speed.
    After every hop: v *= 0.95 below 2.0, and v = 0 below 0.01. Returns (position, speed).
    """
    remaining = dt
    while True:
        new = hop_speed(definition, speed, heights(position), heights(position + HOP), floor, cap)
        hop_time = f32(hop_length / f32(f32(speed + new) * 0.5)) if speed + new > 0.0 else math.inf
        if hop_time < remaining:
            remaining = f32(remaining - hop_time)
            speed = new
            position = f32(position + HOP)
            done = False
        else:
            fraction = f32(remaining / hop_time) if math.isfinite(hop_time) else 0.0
            end = fmadds(f32(1.0 - fraction), speed, f32(fraction * new))
            travelled = f32(f32(f32(f32(speed + end) * 0.5) * remaining) / hop_length)
            position = fmadds(HOP, travelled, position)
            speed = min(end, cap)
            done = True
        speed = f32(speed * (DECAY if speed < DECAY_BELOW else 1.0))
        if speed < STOP_BELOW:
            speed = 0.0
        if done:
            return position, speed


def ceil_cost(value: float) -> int:
    return ce.round_up(value)


def trunc_cost(value: float) -> int:
    """0x1c3fbc: float to unsigned with truncation; negative gives 0."""
    return 0 if value < 0.0 else int(value)


def place_charge(cost: float) -> int:
    """0x37888..0x378c0: a newly placed pylon (unit cost included) is charged trunc(cost) at once."""
    return trunc_cost(cost)


def move_settlement(old_costs, new_costs, moved: bool) -> tuple[int, int]:
    """0x4a43c / 0x45fc4: (refund, charge). Refund trunc(old float), charge ceil(new); nothing if the
    node was dropped on its own cell. old/new list the node and, on a complete track, the next node."""
    if not moved:
        return 0, 0
    return sum(trunc_cost(c) for c in old_costs), sum(ceil_cost(c) for c in new_costs)


def preview_delta(old_cost: float, new_cost: float) -> int:
    """0x483ac: build-cost display accumulator change ceil(new) - ceil(old)."""
    return ceil_cost(new_cost) - ceil_cost(old_cost)


def node_world(cell: tuple[int, int], ground: float, pylon_height: float,
               below: float | None = None, stack_offset: float = 0.0) -> tuple[float, float, float]:
    """0x3343c..0x334cc: cell centre; Y = ground + h(t), or below + stack offset + h(t) when stacked."""
    y = f32(ground + pylon_height) if below is None else f32(f32(pylon_height + stack_offset) + below)
    return (f32(cell[0] * CELL + HALF_CELL), y, f32(cell[1] * CELL + HALF_CELL))


# ---------------------------------------------------------------- binary witness

def scan(c, opcode: int | None, immediate: int, start: int = 0, end: int | None = None,
         ra: int | None = None) -> set[int]:
    data = c.code.data
    end = len(data) if end is None else end
    found = set()
    for at in range(start, end - 3, 4):
        word = pef._u32(data, at)
        op = word >> 26
        if opcode is not None and op != opcode:
            continue
        if opcode is None and not 32 <= op <= 55:
            continue
        value = word & 0xffff
        value = value - 0x10000 if value & 0x8000 else value
        base = word >> 16 & 31
        if value != immediate or (ra is not None and base != ra) or (ra is None and base == 1):
            continue
        found.add(at)
    return found


def calls_in(c, start: int, end: int) -> set[int]:
    targets = set()
    for at in range(start, end, 4):
        word = pef._u32(c.code.data, at)
        if word >> 26 == 18 and word & 1:
            targets.add(call_target(c, at))
    return targets


def check_scans(c) -> dict:
    found = {
        'snapshot_pointer_loads': scan(c, 32, -29688, ra=2),
        'module_stfs_172': scan(c, 52, 172, *MODULE),
        'module_credit_calls': {at for at in range(*MODULE, 4)
                                if pef._u32(c.code.data, at) >> 26 == 18 and pef._u32(c.code.data, at) & 1
                                and call_target(c, at) == 0xcbf50},
        'module_lfs_112': scan(c, 48, 112, *MODULE),
        'module_lfs_152': scan(c, 48, 152, *MODULE),
        'any_15060': scan(c, None, 15060),
    }
    for name, expected in SCANS.items():
        require(found[name], expected, f'bounded scan {name}')
    for name in NO_MONEY_BLOCKS:
        start, end, _ = BLOCKS[name]
        require(calls_in(c, start, end) & set(MONEY_TARGETS), set(), f'{name} makes no money call')
    return {name: sorted(hex(at) for at in value) for name, value in found.items()}


def check_code(c) -> dict:
    blocks = {}
    for name, (start, end, expected) in BLOCKS.items():
        digest = hashlib.sha256(c.code.data[start:end]).hexdigest()
        require(digest, expected, f'{name} bounded code digest')
        blocks[name] = {'code_start': start, 'code_end_exclusive': end, 'sha256': digest}
    for at, op, expected, meaning in FIELDS:
        require(d_fields(c, at, op), expected, f'{meaning} at code:{at:#x}')
    for at, target in CALLS.items():
        require(call_target(c, at), target, f'call at code:{at:#x}')
    for at, symbol in IMPORTS.items():
        require(ce.glue_symbol(c, at), symbol, f'import glue at code:{at:#x}')
    for at, (*fields, meaning) in MASKS.items():
        require(ce.rlwinm_fields(c, at), tuple(fields), f'{meaning} at code:{at:#x}')
    constants = {}
    for offset, expected in TOC_CONSTANTS.items():
        value = struct.unpack_from('>f', c.data_section.data, ce.TOC + offset)[0]
        require(value, expected, f'TOC constant {offset}')
        constants[str(offset)] = value
    pointers = {str(offset): require(ce.toc_pointer(c, offset), expected, f'TOC pointer {offset}')
                for offset, expected in TOC_POINTERS.items()}
    return {'blocks': blocks, 'toc_constants': constants, 'toc_pointers': pointers,
            'field_witnesses': len(FIELDS), 'call_witnesses': len(CALLS), 'mask_witnesses': len(MASKS),
            'import_witnesses': len(IMPORTS), 'scans': check_scans(c)}


def inspect(bin_root: Path) -> dict:
    c = load_identified(bin_root / 'SimThemePark.data')
    require((c.code.index, c.data_section.index), (0, 1), 'section indices')
    report = check_code(c)
    report['binary_sha256'] = hashlib.sha256(c.raw).hexdigest()
    report['witness_source'] = Path(__file__).resolve().relative_to(REPO).as_posix()
    report['plan'] = 'docs/reverse/COAST-plan.md'
    report['limitation'] = ('Static, bounded: no original execution; Mac build only; pylon mesh tag '
                            'positions, the ride camera and cell action 129 are not derived.')
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
    except (OSError, KeyError, ValueError, pef.PEFError) as error:
        parser.exit(1, f'coast geometry evidence: {error}\n')
    print(json.dumps(result, sort_keys=True, indent=2))


if __name__ == '__main__':
    main()
