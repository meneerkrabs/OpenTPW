"""Bounded static coaster (track type 3) witnesses (COAST-R); never execute or emit original instructions.

Usage: python3 -I coast_evidence.py /path/to/mac-feral/bin [--pc-data /path/to/Data]

Addresses are section-relative offsets in the identified Mac `SimThemePark.data`. Instructions are
decoded into fields and compared with expected operands; only digests, decoded fields, constants
and derived values are reported. The pure functions restate the decoded arithmetic so tests can pin
it without the binary. See docs/reverse/COAST-plan.md for the narrative and the claim bounds.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import sys

HERE = Path(__file__).resolve().parent
TOOLS = HERE.parents[1]
REPO = TOOLS.parents[1]
sys.path.insert(0, str(TOOLS))
import pef  # noqa: E402
from timer_evidence import call_target, d_fields, load_identified, require  # noqa: E402

TOC = 0x8000
DEG = 0.01745329238474369  # single(pi / 180), TOC -13768

# name -> (code start, code end exclusive, sha256 of that byte range)
BLOCKS = {
    'definition_defaults': (0x2d274, 0x2d600, '1b734468f2c6fddaa007f4ae8f6fa6c2bb7710bcf2e1a6791dfda7a46be6cade'),
    'pylon_loader': (0x2f7dc, 0x2fbfc, '31e236c4141dc86fd49abd40368185a3d434342a53f03cd16824a9e9dcb5a3d2'),
    'coaster_loader': (0x2fde8, 0x30298, '5e44ade7a8f89b2875a5a81eae165777172e0e7406e5e14e698d93fe8c7ceebf'),
    'height_parameter': (0x36990, 0x369d0, '16e7d9337d7b4f8d4c89a9ed82b3b3063829629f0b3e88133e6334b3e1917040'),
    'turn_angle': (0x36a50, 0x36b70, 'd85ebd25a05c59775ca37b2f08685e5b3f8c7d249a6124ad894073a891443221'),
    'place_validator': (0x36ba4, 0x373e4, 'a7452507845deb6552fc4bd8c53598852f0b6b057678d5f1c8d722deb07d128f'),
    'reopen_track': (0x37e8c, 0x37ef0, 'a3d04f4d90d7be00cb832d3379c2f337960a903c35871db35fddedad3eff39ac'),
    'complete_track': (0x37ef0, 0x37fb8, 'e2879426cc1d05205d7458f7a57af18c31c61fb3d5b3b35923397e81dea8aa54'),
    'rating_pass': (0x3fc18, 0x40574, '62e609e90b530265ee68f5a8bd24db89b388ccba31f843abe0119796e5c86501'),
    'rating_reset': (0x41510, 0x415b4, '490003ade5aaf7f8e77e8151a8652eb9ff24b8441159df41aded42b73796e141'),
    'rating_step': (0x415b4, 0x41900, '76c29ce28e5f70175d8fcd9377dc5553b801ffbbd7d677a03e82c9662506b3d4'),
    'rating_final': (0x41900, 0x41a14, '6232f6325cbccf605e4ed4a5721afbc25b853592a4b559415044055be8866fd1'),
    'rating_get': (0x41a14, 0x41a34, 'b6dc5fab36dd5cdb5b4e77e953843416133343f7a76491db8b32d6c1eb0b6553'),
    'round_up': (0x41aa4, 0x41b00, 'f8a7de17f24c9fe398d147422735494dda5463ff378690abfcc10f7fa9692ccd'),
    'runnable_gates': (0x45da4, 0x45f5c, '94158396f570cf08f8ddec19028016e79381ce00f85ca9eda203bec70b7b4843'),
    'move_node': (0x483ac, 0x48cbc, '947bfd58e5ffc4946f2b80f7f0a1a9efdaec88b178409d66648ced51eb5eb322'),
    'pickup_node': (0x4a1fc, 0x4a43c, '297527030e25f5a083538f5f61efd57936340c47cc4f9cb529dd80179701d509'),
    'node_cost': (0x4b168, 0x4b314, '9e126fb005980ba012e772c9d29b904e8692e2a19d9a342ec147fb605db9f11b'),
    'build_cost_accumulator': (0x7bcb8, 0x7bccc, 'c21df79bf5b6ba61fe0348043f6f8393f8e464ceb84b4ab912d3f643abf66ebf'),
    'guest_excitement': (0xdff70, 0xe02ec, '3a20288e0ce123dc33dbe4c02fd4988f477f516bcf6b68705d7c7e31f080b3ee'),
    'is_coaster': (0xe2fe4, 0xe3024, '7aa54e7f966dc54b44bf3c7d206f83efad1ae9c40bc2d63cb68217e01ab66aec'),
}

# (code offset, primary opcode, (rD/rS, rA, signed immediate), meaning). Decoded, never raw words.
FIELDS = (
    # Coaster definition defaults 0x2d274 (r31 = definition; values are read through TOC_CONSTANTS).
    (0x2d280, 14, (5, 0, 15000), 'default uiTrainReleaseRate 15000 ms'),
    (0x2d29c, 14, (28, 0, 1), 'default uiMaxTrains 1'),
    (0x2d2ec, 36, (28, 31, 744), 'store uiMaxTrains at definition +744'),
    (0x2d2f0, 36, (5, 31, 748), 'store uiTrainReleaseRate at definition +748'),
    (0x2d2d8, 48, (0, 2, -13644), 'default fAccelerationPerHeight (1.5)'),
    (0x2d2dc, 52, (0, 31, 36), 'definition +36 fAccelerationPerHeight'),
    (0x2d310, 48, (31, 2, -13656), 'default fWinchSpeed / fMinSpeed (5.0)'),
    (0x2d314, 52, (31, 31, 68), 'definition +68 fWinchSpeed'),
    (0x2d318, 52, (31, 31, 72), 'definition +72 fMinSpeed'),
    (0x2d31c, 48, (0, 2, -13660), 'default fMaxSpeedAtMinSetting (80)'),
    (0x2d324, 48, (0, 2, -13664), 'default fMaxSpeedAtMaxSetting (130)'),
    (0x2d32c, 48, (0, 2, -13668), 'default fFrictionMultiplier (0.8)'),
    (0x2d340, 48, (0, 2, -13672), 'default fGravityY (-0.3)'),
    (0x2d344, 52, (0, 31, 100), 'definition +100 fGravityY'),
    (0x2d34c, 48, (0, 2, -13676), 'default fForceMultiplier (0.2)'),
    (0x2d354, 52, (29, 31, 800), 'definition +800 fInitialSickness = 0'),
    (0x2d358, 52, (2, 31, 804), 'definition +804 fInitialExcitement = 1.0'),
    (0x2d364, 52, (1, 31, 812), 'definition +812 fGForceSicknessModifier = 0.5'),
    (0x2d368, 48, (0, 2, -13684), 'default fUpsideDownSicknessModifier (3.0)'),
    (0x2d36c, 52, (0, 31, 816), 'definition +816 fUpsideDownSicknessModifier'),
    (0x2d370, 48, (0, 2, -13688), 'default fSpeedExcitementModifier (0.1)'),
    (0x2d374, 52, (0, 31, 820), 'definition +820 fSpeedExcitementModifier'),
    (0x2d378, 52, (2, 31, 824), 'definition +824 fGForceExcitementModifier = 1.0'),
    (0x2d37c, 52, (2, 31, 828), 'definition +828 fUpsideDownExcitementModifier = 1.0'),
    (0x2d380, 52, (31, 31, 832), 'definition +832 fBankExcitementModifier = 5.0'),
    (0x2d384, 52, (2, 31, 836), 'definition +836 fXGForceUncomfortableModifier = 1.0'),
    (0x2d38c, 52, (0, 31, 840), 'definition +840 fInvertedUncomfortableModifier = 2.0'),
    (0x2d390, 52, (1, 31, 844), 'definition +844 fNormalScreamStartExciteDelta = 0.5'),
    (0x2d394, 52, (2, 31, 848), 'definition +848 fTerrorScreamStartExciteDelta = 1.0'),
    (0x2d39c, 52, (0, 31, 856), 'definition +856 fTerrorScreamSustainExcitement = 20'),
    (0x2d3a0, 52, (30, 31, 852), 'definition +852 fNormalScreamSustainExcitement = 10'),
    # Standard pylon (definition +168) defaults: place distance squared 9..64, i.e. 3..8 cells.
    (0x2d3d4, 14, (29, 0, 9), 'StdPylon uiMinPlaceDist squared 9'),
    (0x2d3dc, 14, (0, 0, 64), 'StdPylon uiMaxPlaceDist squared 64'),
    (0x2d4c0, 36, (29, 31, 324), 'definition +168+156 min distance squared'),
    (0x2d4c4, 36, (0, 31, 328), 'definition +168+160 max distance squared'),
    (0x2d47c, 52, (31, 31, 256), 'StdPylon +88 fCollisionPossibleRadius = 5'),
    (0x2d484, 52, (30, 31, 272), 'StdPylon +104 fMinHeight = 10'),
    (0x2d488, 52, (30, 31, 276), 'StdPylon +108 fMaxHeight = 10'),
    (0x2d48c, 48, (0, 2, -13704), 'StdPylon fEntranceMinXZAngleAtMinDist (140 deg)'),
    (0x2d49c, 48, (0, 2, -13712), 'StdPylon fEntranceMinXZAngleAtMaxDist (110 deg)'),
    (0x2d4a8, 48, (0, 2, -13716), 'StdPylon fMinXZAngleAtMinDist (80 deg)'),
    (0x2d4b4, 48, (0, 2, -13720), 'StdPylon fMinXZAngleAtMaxDist (50 deg)'),
    (0x2d51c, 48, (0, 2, -13724), 'StdPylon fMaxRotnAngleDeviance (45 deg)'),
    # Loop pylon (definition +456) defaults.
    (0x2d510, 14, (3, 0, 16388), 'LoopPylon flags 0x4004'),
    (0x2d514, 14, (0, 0, 81), 'LoopPylon uiMaxPlaceDist squared 81'),
    (0x2d550, 48, (1, 2, -13732), 'LoopPylon entrance/min XZ angle (120 deg)'),
    (0x2d558, 48, (0, 2, -13736), 'LoopPylon max XZ angle (174 deg)'),
    (0x2d5d4, 48, (0, 2, -13744), 'LoopPylon fMaxLoopHeightDifference (25)'),
    # Pylon loader 0x2f7dc: nonzero SAM values override; angles x DEG; distances squared.
    (0x2f814, 48, (31, 2, -13768), 'degrees -> radians factor'),
    (0x2f82c, 14, (28, 27, 168), 'pylon control 0 at definition +168'),
    (0x2f834, 14, (28, 27, 456), 'pylon control 1 at definition +456'),
    (0x2f8fc, 48, (0, 31, 14536), 'settings fMinHeight'),
    (0x2f908, 52, (0, 28, 104), 'pylon +104 fMinHeight'),
    (0x2f90c, 48, (0, 31, 14540), 'settings fMaxHeight'),
    (0x2f918, 52, (0, 28, 108), 'pylon +108 fMaxHeight'),
    (0x2f9f4, 32, (0, 31, 14588), 'settings uiMinPlaceDist'),
    (0x2fa04, 36, (0, 28, 156), 'pylon +156 = uiMinPlaceDist squared'),
    (0x2fa20, 52, (0, 28, 164), 'pylon +164 = float uiMinPlaceDist'),
    (0x2fa24, 32, (0, 31, 14592), 'settings uiMaxPlaceDist'),
    (0x2fa34, 36, (0, 28, 160), 'pylon +160 = uiMaxPlaceDist squared'),
    (0x2fa50, 52, (0, 28, 168), 'pylon +168 = float uiMaxPlaceDist'),
    (0x2fa6c, 48, (0, 31, 14600), 'settings fCostPerUnit'),
    (0x2fa70, 52, (0, 28, 92), 'pylon +92 fCostPerUnit (copied even when zero)'),
    (0x2fa74, 48, (0, 31, 14604), 'settings fCostPer2DLength'),
    (0x2fa78, 52, (0, 28, 96), 'pylon +96 fCostPer2DLength'),
    (0x2fa7c, 48, (0, 31, 14608), 'settings fCostPerHeight'),
    (0x2fa80, 52, (0, 28, 100), 'pylon +100 fCostPerHeight'),
    (0x2fbc4, 14, (31, 31, 188), 'settings pylon record stride 188'),
    # Coaster loader 0x2fde8: settings -> definition.
    (0x2ff18, 32, (0, 31, 15080), 'settings uiMaxTrains'),
    (0x2ff24, 36, (0, 30, 744), 'definition +744 uiMaxTrains'),
    (0x2ff28, 32, (0, 31, 15084), 'settings uiTrainReleaseRate'),
    (0x2ff34, 36, (0, 30, 748), 'definition +748 uiTrainReleaseRate'),
    (0x30058, 48, (1, 31, 15160), 'settings fInitialSickness'),
    (0x30068, 52, (1, 30, 800), 'definition +800'),
    (0x3006c, 48, (1, 31, 15164), 'settings fInitialExcitement'),
    (0x3007c, 52, (1, 30, 804), 'definition +804'),
    (0x300bc, 48, (1, 31, 15180), 'settings fSpeedExcitementModifier'),
    (0x300cc, 52, (1, 30, 820), 'definition +820'),
    (0x300f8, 48, (1, 31, 15192), 'settings fBankExcitementModifier'),
    (0x30108, 52, (1, 30, 832), 'definition +832'),
    (0x30120, 48, (1, 31, 15200), 'settings fInvertedUncomfortableModifier'),
    (0x30130, 52, (1, 30, 840), 'definition +840'),
    (0x301a0, 24, (0, 0, -32768), 'bSpecialIsTrough: ori 0x8000 (decoded signed) marks pylon 1 a trough'),
    # Height parameter t = (h - h0) / (h1 - h0) over the pylon mesh extents +188/+192.
    (0x36990, 48, (2, 4, 188), 'pylon +188 mesh height at t=0'),
    (0x36994, 48, (0, 4, 192), 'pylon +192 mesh height at t=1'),
    (0x350a0, 48, (0, 30, 104), 'builder clamps height to fMinHeight'),
    (0x350c0, 48, (1, 30, 108), 'builder clamps height to fMaxHeight'),
    # Turn angle 0x36a50: acos(clamp(dot, -1, 1)) of the two unit XZ vectors at the previous pylon.
    (0x36b14, 48, (0, 2, -13788), 'dot clamp lower -1'),
    (0x36b34, 48, (0, 2, -13648), 'dot clamp upper 1'),
    # Placement validator 0x36ba4 (cells x = r29, y = r30).
    (0x36ca0, 32, (3, 3, 108), 'map record'),
    (0x36ca4, 32, (4, 3, 24), 'map width (x must be below)'),
    (0x36cb0, 32, (3, 3, 28), 'map height (y must be below)'),
    (0x36cd8, 32, (4, 4, 44), 'per-cell halfword array (bit 0 blocks a new pylon)'),
    (0x36df0, 14, (3, 3, 456), 'loop pylon control selected after a loop node'),
    (0x36e18, 14, (3, 3, 168), 'standard pylon control otherwise'),
    (0x36e98, 32, (3, 19, 156), 'minimum place distance squared'),
    (0x36eac, 32, (3, 19, 160), 'maximum place distance squared'),
    (0x36ebc, 10, (0, 3, 2), 'previous node ordinal 2 never waives the distance window'),
    (0x36f28, 48, (2, 19, 164), 'minimum place distance (float)'),
    (0x36f2c, 48, (0, 19, 168), 'maximum place distance (float)'),
    (0x36fa8, 48, (3, 19, 116), 'entrance min angle at min distance'),
    (0x36fb0, 48, (1, 19, 120), 'entrance max angle at min distance'),
    (0x36f98, 48, (1, 19, 124), 'entrance min angle at max distance'),
    (0x36f9c, 48, (0, 19, 128), 'entrance max angle at max distance'),
    (0x36fd8, 48, (3, 19, 132), 'min angle at min distance'),
    (0x36fe0, 48, (1, 19, 136), 'max angle at min distance'),
    (0x36fc8, 48, (1, 19, 140), 'min angle at max distance'),
    (0x36fcc, 48, (0, 19, 144), 'max angle at max distance'),
    (0x370e4, 32, (5, 3, 20), 'occupying pylon must belong to this coaster'),
    (0x37104, 32, (0, 3, 24), 'occupying pylon must use the same pylon control'),
    (0x37134, 32, (5, 3, 48), 'stack height of the occupying pylon'),
    (0x37138, 32, (0, 4, 204), 'uiMaxStackedPylons'),
    # Track completion flag (controller +60 bit 0) and the self-intersection count (+320; not riders, docs/COASTERS.md).
    (0x37f1c, 24, (0, 0, 1), 'complete track sets controller +60 bit 0'),
    (0x45da4, 32, (3, 3, 60), 'complete-track flags'),
    (0x45edc, 32, (0, 29, 320), 'self-intersection count'),
    # Rating pass 0x3fc18 over the 56-byte samples.
    (0x3fc88, 48, (0, 24, 68), 'start speed fWinchSpeed'),
    (0x3fcac, 48, (0, 31, 52), 'speed cap controller +52'),
    (0x3fcc8, 48, (23, 2, -13516), 'trough uphill slope threshold'),
    (0x3fccc, 48, (22, 2, -13520), 'force scale divisor 10'),
    (0x3fe44, 48, (1, 24, 36), 'fAccelerationPerHeight'),
    (0x3fe48, 48, (2, 24, 40), 'fUphillAccelModifier when the next sample is higher'),
    (0x3fe80, 48, (2, 24, 44), 'fDownhillAccelModifier otherwise'),
    (0x3fec0, 48, (0, 24, 84), 'fFrictionMultiplier on flag-8 samples'),
    (0x3fefc, 24, (0, 0, 2048), 'section flag 0x800 (winch) when speed equals the winch floor'),
    (0x3ff3c, 52, (0, 23, 28), 'sample +28 speed'),
    (0x3ffa0, 48, (2, 23, 24), 'sample +24 bank angle passed to the rating step'),
    (0x40478, 32, (3, 3, 748), 'uiTrainReleaseRate for the train count'),
    (0x4047c, 48, (0, 2, -13496), 'milliseconds per second 1000'),
    (0x404d0, 32, (3, 3, 744), 'uiMaxTrains cap'),
    # Rating reset / step / final.
    (0x41538, 48, (0, 5, 800), 'reset sickness to fInitialSickness'),
    (0x41550, 48, (0, 7, 804), 'reset excitement to fInitialExcitement'),
    (0x4153c, 14, (5, 0, 12), 'previous force vector cleared (12 bytes)'),
    (0x41600, 48, (3, 2, -13436), 'pi/2: |bank| beyond it counts as upside down'),
    (0x4161c, 48, (7, 5, 812), 'fGForceSicknessModifier'),
    (0x416bc, 48, (9, 5, 820), 'fSpeedExcitementModifier'),
    (0x416d8, 48, (10, 5, 816), 'fUpsideDownSicknessModifier'),
    (0x416e0, 48, (8, 5, 824), 'fGForceExcitementModifier'),
    (0x416e4, 48, (4, 5, 836), 'fXGForceUncomfortableModifier'),
    (0x416ec, 48, (5, 5, 832), 'fBankExcitementModifier'),
    (0x41758, 48, (1, 2, -13444), 'inverted discomfort when vertical force < 0.1'),
    (0x4176c, 48, (0, 5, 840), 'fInvertedUncomfortableModifier (negative force arm)'),
    (0x41770, 48, (1, 2, -13448), 'negative vertical force triples inverted discomfort'),
    (0x417b0, 48, (2, 2, -13452), 'running values decay by 0.5 per sample'),
    (0x41814, 10, (0, 4, 2), 'screams only after sample 2'),
    (0x41834, 48, (0, 5, 844), 'fNormalScreamStartExciteDelta'),
    (0x41850, 24, (0, 0, 4), 'sample flag 4 normal scream'),
    (0x41868, 48, (0, 5, 852), 'fNormalScreamSustainExcitement'),
    (0x4188c, 48, (0, 5, 848), 'fTerrorScreamStartExciteDelta'),
    (0x418a4, 24, (0, 0, 2), 'sample flag 2 terror scream'),
    (0x418c0, 48, (0, 5, 856), 'fTerrorScreamSustainExcitement'),
    (0x41924, 52, (0, 3, 256), 'controller +256 sickness rating = current + peak'),
    (0x4192c, 48, (3, 2, -13456), 'peak excitement weight 1.7'),
    (0x41934, 48, (4, 2, -13452), 'current excitement weight 0.5'),
    (0x41948, 52, (0, 3, 260), 'controller +260 excitement rating'),
    (0x41a08, 52, (1, 3, 272), 'controller +272 = first final argument (max sample height)'),
    (0x41a0c, 52, (2, 3, 276), 'controller +276 = second final argument (max speed)'),
    (0x41a20, 48, (0, 3, 256), 'getter: sickness'),
    (0x41a28, 48, (0, 3, 260), 'getter: excitement'),
    # Ride excitement for guests 0xdff70 and the coaster predicate 0xe2fe4.
    (0xe0070, 48, (1, 2, -11088), 'ride excitement = 0.5 * coaster excitement'),
    (0xe0074, 48, (0, 2, -11128), '... + 50'),
    (0xe3004, 32, (0, 3, 156), 'ride type record +156 (Bumper.WhichTrackType)'),
    (0xe3008, 8, (0, 0, 3), 'track type 3 is a coaster'),
    # Pylon cost 0x4b168 and rounding 0x41aa4.
    (0x4b1a0, 32, (5, 26, 256), 'node descriptor'),
    (0x4b1ac, 32, (31, 5, 24), 'descriptor pylon control'),
    (0x4b214, 48, (1, 26, 232), 'node height parameter t'),
    (0x4b218, 14, (29, 3, 168), 'standard pylon control (fallback rates)'),
    (0x4b220, 48, (30, 2, -13300), 'world units per cost unit 10'),
    (0x4b28c, 48, (0, 31, 92), 'fCostPerUnit when the unit is charged'),
    (0x4b2d8, 48, (1, 31, 96), 'fCostPer2DLength'),
    (0x4b2dc, 48, (0, 31, 100), 'fCostPerHeight'),
    (0x41ae4, 14, (0, 3, 1), 'any fractional part rounds the integer part up by one'),
)

CALLS = {
    0x302cc: 0x16f4c, 0x30300: 0x2d274, 0x30310: 0x2fde8, 0x2fe70: 0x2f7dc, 0x2fe94: 0x2fbfc,
    0x350ec: 0x36990, 0x36ef8: 0x36a50, 0x36f1c: 0x1c552c, 0x36aec: 0x1c552c, 0x36b08: 0x1c552c,
    0x36b44: 0x1c5d3c, 0x37318: 0x36ba4, 0x373b4: 0x36ba4,
    0x3fcc0: 0x41510, 0x3ffa8: 0x415b4, 0x40454: 0x41900, 0x404f8: 0x3e9ec, 0x4050c: 0x3f930,
    0x4155c: 0x1c483c, 0xe0068: 0x41a14, 0xe003c: 0xe2fe4, 0xe004c: 0xb5e24,
    0x4b21c: 0x369bc, 0x4b248: 0x1c552c, 0x4b274: 0x1c552c, 0x41ab4: 0x1c546c,
    0x48c5c: 0x4b168, 0x48c60: 0x41aa4, 0x48c78: 0x7bcb8, 0x48c80: 0x7bcb8, 0x482ec: 0x36ba4,
    0x4a3f0: 0x4b168, 0x4a418: 0x4b168, 0x45f08: 0x45da4,
}

IMPORTS = {  # CFM glue -> imported symbol (resolved through the TOC relocation)
    0x1c552c: 'sqrt', 0x1c5d3c: 'acos', 0x1c5d54: 'fmod', 0x1c483c: 'memset', 0x1c546c: 'modf',
}

# A-form fmadds (opcode 59, xo 29): (frD, frA, frB, frC). llvm prints frD, frA, frC, frB.
FMADDS = {
    0x417bc: (0, 0, 6, 2, 'sickness = 0.5 * sickness + delta'),
    0x417cc: (0, 1, 0, 2, 'excitement = 0.5 * excitement + max(delta, 0)'),
    0x41944: (0, 4, 0, 3, 'excitement rating = 0.5 * current + 1.7 * peak'),
    0xe0078: (0, 2, 0, 1, 'ride excitement = 0.5 * E + 50'),
    0x4b2e0: (29, 1, 29, 2, 'cost += CostPer2DLength * length2d'),
    0x4b2e4: (29, 0, 29, 31, 'cost += CostPerHeight * height'),
    0x36fb4: (2, 3, 2, 5, 'entrance min angle = a(1-t) + b t'),
    0x36fe4: (2, 3, 2, 5, 'min angle = a(1-t) + b t'),
}

# rlwinm (rS, rA, SH, MB, ME): the masks that select flags.
MASKS = {
    0x36f88: (0, 0, 0, 30, 30, 'descriptor flag 0x2 selects the entrance angle limits'),
    0x36de0: (3, 3, 0, 25, 25, 'node flag 0x40 selects the loop pylon control'),
    0x4b298: (0, 0, 0, 16, 17, 'pylon flags 0xc000 (loop/trough) pick fallback rates'),
    0x4a228: (0, 0, 0, 30, 30, 'descriptor flag 0x2: the station node cannot be picked up'),
    0x41820: (0, 0, 0, 0, 28, 'sample scream flags cleared before the step'),
    0x3fd30: (0, 0, 0, 23, 23, 'definition flag 0x100 (trough) enables the uphill sample flag'),
    0x3feb4: (0, 0, 0, 28, 28, 'sample flag 0x8 applies friction'),
    0x37ed0: (0, 0, 0, 0, 30, 'reopen clears controller +60 bit 0'),
}

# TOC offset (relative to r2) -> single value, read from the data section at TOC + offset.
TOC_CONSTANTS = {
    -13628: 0.0, -13636: 1.25, -13640: 4.0, -13644: 1.5, -13648: 1.0, -13652: 10.0, -13656: 5.0,
    -13660: 80.0, -13664: 130.0, -13668: 0.800000011920929, -13672: -0.30000001192092896,
    -13676: 0.20000000298023224, -13680: 0.5, -13684: 3.0, -13688: 0.10000000149011612, -13692: 2.0,
    -13696: 20.0, -13700: -0.9990000128746033, -13704: 2.4434609413146973, -13708: 3.1415927410125732,
    -13712: 1.919862151145935, -13716: 1.3962633609771729, -13720: 0.8726646304130554,
    -13724: 0.7853981852531433, -13728: -0.800000011920929, -13732: 2.094395160675049,
    -13736: 3.0368728637695312, -13740: 1.5707963705062866, -13744: 25.0, -13768: DEG,
    -13788: -1.0, -13436: 1.5707963705062866, -13444: 0.10000000149011612, -13448: 3.0,
    -13452: 0.5, -13456: 1.7000000476837158, -13496: 1000.0, -13516: 0.01744999922811985,
    -13520: 10.0, -13300: 10.0, -11088: 0.5, -11128: 50.0,
}

# TOC slot (relative to r2) -> data-section address it points to (checked through relocations).
TOC_POINTERS = {
    -21744: 0x3d160, -21748: 0x3d164, -21752: 0x3d168, -21756: 0x3d16c,  # rating clamps
    -21732: 0x3d154, -21736: 0x3d158, -21740: 0x3d15c,  # rating step floors
    -21892: 0x3d058, -21896: 0x3d05c,  # validator t clamp
    -21868: 0x3d040, -21872: 0x3d044,  # height t clamp
    -21760: 0x3d144,  # train count floor
    -28028: 0x84ac4,  # build cost accumulator
}

DATA_CONSTANTS = (
    (0x3d160, '>f', 100.0, 'excitement rating upper clamp'),
    (0x3d164, '>f', 0.0, 'excitement rating lower clamp'),
    (0x3d168, '>f', 100.0, 'sickness rating upper clamp'),
    (0x3d16c, '>f', 0.0, 'sickness rating lower clamp'),
    (0x3d154, '>f', 0.0, 'upside-down amount floor'),
    (0x3d158, '>f', 0.0, 'upside-down amount floor (excitement arm)'),
    (0x3d15c, '>f', 0.0, 'excitement delta floor'),
    (0x3d058, '>f', 1.0, 'validator distance fraction upper clamp'),
    (0x3d05c, '>f', 0.0, 'validator distance fraction lower clamp'),
    (0x3d040, '>f', 0.0, 'height parameter lower clamp'),
    (0x3d044, '>f', 1.0, 'height parameter upper clamp'),
    (0x3d144, '>I', 1, 'train count floor'),
)

# Settings descriptor table: index -> (name, settings offset). Walk shared with the rides lane.
DESCRIPTORS = {
    271: ('uiPylonWidthCells', 14524), 273: ('uiMaxStackedPylons', 14532), 274: ('fMinHeight', 14536),
    275: ('fMaxHeight', 14540), 276: ('fMaxHeightDiffPerGSquareDist', 14544),
    277: ('fEntranceMinXZAngleAtMinDist', 14548), 281: ('fMinXZAngleAtMinDist', 14564),
    285: ('fMaxRotnAngleDeviance', 14580), 287: ('uiMinPlaceDist', 14588), 288: ('uiMaxPlaceDist', 14592),
    289: ('fMaxLoopHeightDifference', 14596), 290: ('fCostPerUnit', 14600),
    291: ('fCostPer2DLength', 14604), 292: ('fCostPerHeight', 14608), 344: ('uiMaxTrains', 15080),
    345: ('uiTrainReleaseRate', 15084), 346: ('bSpecialIsTrough', 15088), 353: ('fWinchSpeed', 15116),
    354: ('fMinSpeed', 15120), 364: ('fInitialSickness', 15160), 365: ('fInitialExcitement', 15164),
    369: ('fSpeedExcitementModifier', 15180), 372: ('fBankExcitementModifier', 15192),
    374: ('fInvertedUncomfortableModifier', 15200), 378: ('fTerrorScreamSustainExcitement', 15216),
}


# ---------------------------------------------------------------- pure restatements

def f32(value: float) -> float:
    """Round to IEEE single, as every lfs/fadds/fmuls/fdivs result in these blocks is."""
    return struct.unpack('>f', struct.pack('>f', value))[0]


def fmadds(a: float, c: float, b: float) -> float:
    """Single fused a * c + b (one rounding)."""
    return f32(a * c + b)


DEFAULTS = {
    # Coaster definition (0x2d274), used when the coaster.sam leaves a field zero or absent.
    'fCurviness': 1.25, 'fStraightDivisor': 1.25, 'fAccelerationPerHeight': 1.5,
    'fUphillAccelModifier': 1.0, 'fDownhillAccelModifier': 1.0, 'fWinchSpeed': 5.0, 'fMinSpeed': 5.0,
    'fMaxSpeedAtMinSetting': 80.0, 'fMaxSpeedAtMaxSetting': 130.0, 'fFrictionMultiplier': f32(0.8),
    'fGravityX': 0.0, 'fGravityY': f32(-0.3), 'fGravityZ': 0.0, 'fForceMultiplier': f32(0.2),
    'fInitialPylonLoftHeight': 10.0, 'uiMaxTrains': 1, 'uiTrainReleaseRate': 15000,
    'fInitialSickness': 0.0, 'fInitialExcitement': 1.0, 'fInitialUncomfortableness': 0.0,
    'fGForceSicknessModifier': 0.5, 'fUpsideDownSicknessModifier': 3.0,
    'fSpeedExcitementModifier': f32(0.1), 'fGForceExcitementModifier': 1.0,
    'fUpsideDownExcitementModifier': 1.0, 'fBankExcitementModifier': 5.0,
    'fXGForceUncomfortableModifier': 1.0, 'fInvertedUncomfortableModifier': 2.0,
    'fNormalScreamStartExciteDelta': 0.5, 'fTerrorScreamStartExciteDelta': 1.0,
    'fNormalScreamSustainExcitement': 10.0, 'fTerrorScreamSustainExcitement': 20.0,
}

STD_PYLON = {  # definition +168
    'min_place_dist': 3, 'max_place_dist': 8, 'min_height': 10.0, 'max_height': 10.0,
    'entrance_min_at_min': 140.0, 'entrance_max_at_min': 180.0,
    'entrance_min_at_max': 110.0, 'entrance_max_at_max': 180.0,
    'min_at_min': 80.0, 'max_at_min': 180.0, 'min_at_max': 50.0, 'max_at_max': 180.0,
    'max_rotation_deviance': 45.0, 'max_loop_rotation_deviance': 0.0, 'max_loop_height_difference': 0.0,
    'max_stacked': 1, 'collision_radius': 5.0, 'straight_dot_threshold': f32(-0.999), 'flags': 0x4,
}

LOOP_PYLON = {  # definition +456
    'min_place_dist': 3, 'max_place_dist': 9, 'min_height': 10.0, 'max_height': 10.0,
    'entrance_min_at_min': 120.0, 'entrance_max_at_min': 174.0,
    'entrance_min_at_max': 120.0, 'entrance_max_at_max': 174.0,
    'min_at_min': 0.0, 'max_at_min': 174.0, 'min_at_max': 0.0, 'max_at_max': 174.0,
    'max_rotation_deviance': 90.0, 'max_loop_rotation_deviance': 90.0, 'max_loop_height_difference': 25.0,
    'max_stacked': 1, 'collision_radius': 5.0, 'straight_dot_threshold': f32(-0.8), 'flags': 0x4004,
}


def turn_angle(new: tuple[int, int], previous: tuple[int, int], before: tuple[int, int]) -> float:
    """0x36a50: angle at `previous` between the directions to `new` and to `before` (pi = straight)."""
    ax, ay = float(new[0] - previous[0]), float(new[1] - previous[1])
    bx, by = float(before[0] - previous[0]), float(before[1] - previous[1])
    la = f32(math.sqrt(ax * ax + ay * ay))
    lb = f32(math.sqrt(bx * bx + by * by))
    dot = f32(f32(ax / la) * f32(bx / lb) + f32(ay / la) * f32(by / lb))
    return f32(math.acos(min(max(dot, -1.0), 1.0)))


def angle_window(pylon: dict, distance: float, entrance: bool) -> tuple[float, float]:
    """0x36f28..0x36fe8: limits interpolated by t = clamp((d - min)/(max - min), 0, 1), radians."""
    lo, hi = float(pylon['min_place_dist']), float(pylon['max_place_dist'])
    t = min(max(f32((distance - lo) / (hi - lo)), 0.0), 1.0)
    key = 'entrance_' if entrance else ''
    a0, a1 = pylon[key + 'min_at_min'], pylon[key + 'min_at_max']
    b0, b1 = pylon[key + 'max_at_min'], pylon[key + 'max_at_max']
    return (f32(a0 * DEG * (1 - t) + a1 * DEG * t), f32(b0 * DEG * (1 - t) + b1 * DEG * t))


def placement_ok(pylon: dict, new: tuple[int, int], previous: tuple[int, int] | None,
                 before: tuple[int, int] | None = None, entrance: bool = False,
                 map_size: tuple[int, int] = (128, 128)) -> bool:
    """Bounded restatement of the 0x36ba4 geometric gates for a new pylon.

    Covered: map bounds, same cell, squared distance window, turn-angle window. Not covered (see
    COAST-plan): the blocked-cell bit, stacking/ownership checks, waivers for flagged nodes.
    """
    if not (0 <= new[0] < map_size[0] and 0 <= new[1] < map_size[1]):
        return False
    if previous is None:
        return True
    if new == previous:
        return False
    d2 = (new[0] - previous[0]) ** 2 + (new[1] - previous[1]) ** 2
    if d2 < pylon['min_place_dist'] ** 2 or d2 > pylon['max_place_dist'] ** 2:
        return False
    if before is None:
        return True
    angle = abs(turn_angle(new, previous, before))
    low, high = angle_window(pylon, f32(math.sqrt(d2)), entrance)
    return not (angle < low or angle > high)


def height_parameter(height: float, mesh_low: float, mesh_high: float, min_height: float,
                     max_height: float) -> float:
    """0x350a0..0x35134: clamp h to [fMinHeight, fMaxHeight], then t = clamp((h-h0)/(h1-h0), 0, 1)."""
    h = min(max(height, min_height), max_height)
    t = f32(f32(h - mesh_low) / f32(mesh_high - mesh_low))
    return min(max(t, 0.0), 1.0)


def pylon_cost(cost_per_unit: float, cost_per_2d_length: float, cost_per_height: float,
               length_2d_world: float, height_world: float, charge_unit: bool) -> float:
    """0x4b168: [CostPerUnit] + CostPer2DLength * (len/10) + CostPerHeight * (height/10), single."""
    total = f32(cost_per_unit) if charge_unit else 0.0
    total = fmadds(cost_per_2d_length, f32(length_2d_world / 10.0), total)
    return fmadds(cost_per_height, f32(height_world / 10.0), total)


def round_up(value: float) -> int:
    """0x41aa4: integer part of modf, plus one if the fractional part is nonzero."""
    fraction, whole = math.modf(value)
    return int(whole) + (1 if f32(fraction) != 0.0 else 0)


def train_count(lap_time_s: float, release_rate_ms: int, max_trains: int) -> int:
    """0x40470..0x404f0: min(uiMaxTrains, max(1, trunc(lap / (rate / 1000))))."""
    computed = int(f32(lap_time_s / f32(f32(float(release_rate_ms)) / 1000.0)))
    return min(max_trains, max(1, computed))


def next_speed(speed: float, y_now: float, y_next: float, definition: dict, winch_floor: float,
               cap: float, friction_sample: bool = False) -> tuple[float, bool]:
    """Rating-pass speed step (0x3fe28..0x3ff30): returns (speed, winch section)."""
    uphill = y_next > y_now
    modifier = definition['fUphillAccelModifier' if uphill else 'fDownhillAccelModifier']
    acceleration = f32(definition['fAccelerationPerHeight'] * f32(y_now - y_next))
    value = max(fmadds(modifier, acceleration, speed), winch_floor)
    if friction_sample:
        value = f32(value * definition['fFrictionMultiplier'])
    value = max(value, winch_floor)
    winch = value == winch_floor
    return min(value, cap), winch


class Rating:
    """0x41510/0x415b4/0x41900: excitement and sickness accumulators over the samples.

    The previous-force vector and previous bank are only cleared at reset and never written, so
    the "change" terms equal the current magnitudes (bounded: TOC-slot references only).
    """

    def __init__(self, d: dict = DEFAULTS):
        self.d = d
        self.sickness = f32(d['fInitialSickness'])
        self.excitement = f32(d['fInitialExcitement'])
        self.peak_sickness = 0.0
        self.peak_excitement = 0.0
        self.previous_excitement = self.excitement
        self.normal = self.terror = False

    def step(self, index: int, speed: float, bank: float, force: tuple[float, float, float],
             stale_f0: float = 0.0) -> int:
        """Returns the scream flags for this sample: 4 normal, 2 terror."""
        d = self.d
        magnitude = f32(abs(force[2]) + f32(abs(force[0]) + abs(force[1])))
        inverted = max(f32(abs(bank) - f32(math.pi / 2)), 0.0)
        upside = f32(inverted + (1.0 if inverted != 0.0 else 0.0))
        d_sickness = f32(d['fGForceSicknessModifier'] * magnitude + upside * d['fUpsideDownSicknessModifier'])
        excite = f32(f32(speed * d['fSpeedExcitementModifier']) + d['fGForceExcitementModifier'] * magnitude)
        excite = f32(d['fBankExcitementModifier'] * abs(bank) + excite)
        discomfort = stale_f0
        if upside != 0.0 and force[1] < 0.1:
            discomfort = f32(upside * d['fInvertedUncomfortableModifier'])
            if force[1] < 0.0:
                discomfort = f32(3.0 * discomfort)
        discomfort = f32(d['fXGForceUncomfortableModifier'] * abs(force[0]) + discomfort)
        delta = max(f32(excite - discomfort), 0.0)
        self.sickness = fmadds(self.sickness, 0.5, d_sickness)
        self.excitement = fmadds(self.excitement, 0.5, delta)
        self.peak_sickness = max(self.peak_sickness, self.sickness)
        self.peak_excitement = max(self.peak_excitement, self.excitement)
        flags = 0
        if index > 2:
            rise = f32(self.excitement - self.previous_excitement)
            if rise > d['fNormalScreamStartExciteDelta']:
                self.normal, flags = True, flags | 4
            elif self.normal and self.excitement > d['fNormalScreamSustainExcitement']:
                flags |= 4
            else:
                self.normal = False
            if rise > d['fTerrorScreamStartExciteDelta']:
                self.terror, flags = True, flags | 2
            elif self.terror and self.excitement > d['fTerrorScreamSustainExcitement']:
                flags |= 2
            else:
                self.terror = False
        self.previous_excitement = self.excitement
        return flags

    def final(self) -> tuple[float, float]:
        """(sickness, excitement), each clamped to [0, 100]."""
        sickness = f32(self.sickness + self.peak_sickness)
        excitement = fmadds(0.5, self.excitement, f32(1.7000000476837158 * self.peak_excitement))
        return (max(min(sickness, 100.0), 0.0), max(min(excitement, 100.0), 0.0))


def ride_excitement(coaster_excitement: float) -> int:
    """0xe006c..0xe0084: trunc(0.5 * E + 50); a coaster without a controller handle gives 0."""
    return int(fmadds(coaster_excitement, 0.5, 50.0))


# ---------------------------------------------------------------- binary witness

def a_fields(c, at: int) -> tuple[int, int, int, int, int]:
    word = pef._u32(c.code.data, at)
    require(word >> 26, 59, f'single A-form at code:{at:#x}')
    return (word >> 21 & 31, word >> 16 & 31, word >> 11 & 31, word >> 6 & 31, word >> 1 & 31)


def rlwinm_fields(c, at: int) -> tuple[int, int, int, int, int]:
    word = pef._u32(c.code.data, at)
    require(word >> 26, 21, f'rlwinm at code:{at:#x}')
    return (word >> 21 & 31, word >> 16 & 31, word >> 11 & 31, word >> 6 & 31, word >> 1 & 31)


def toc_pointer(c, offset: int) -> int:
    slot = TOC + offset
    target = c.relocs.get(c.data_section.index, {}).get(slot)
    if target is None:
        raise pef.PEFError(f'missing relocation at data:{slot:#x}')
    require((target.kind, target.target), ('section', c.data_section.index), f'TOC slot {offset}')
    return target.addend


def glue_symbol(c, at: int) -> str:
    """CFM glue `lwz r12, slot(r2)` -> imported transition vector name."""
    rd, ra, slot = d_fields(c, at, 32)
    require((rd, ra), (12, 2), f'glue load at code:{at:#x}')
    target = c.relocs.get(c.data_section.index, {}).get(TOC + slot)
    if target is None or target.kind != 'import':
        raise pef.PEFError(f'glue at code:{at:#x} has no import relocation')
    return c.imports[target.target].name


def descriptor_offsets(data: bytes) -> dict:
    sys.path.insert(0, str(TOOLS / 'lanes' / 'rides'))
    from motion_evidence import scalar_offsets  # noqa: E402  (shared constructor-0x16f4c walk)
    records = [(pef._u32(data, 0x2ef24 + i * 60), pef._cstr(data, 0x2ef28 + i * 60),
                pef._u32(data, 0x2ef24 + i * 60 + 52)) for i in range(384)]
    offsets = scalar_offsets(records)
    for index, expected in DESCRIPTORS.items():
        require(offsets.get(index), expected, f'settings descriptor {index}')
    return {name: offset for name, offset in DESCRIPTORS.values()}


def check_fields(c) -> int:
    for at, op, expected, meaning in FIELDS:
        require(d_fields(c, at, op), expected, f'{meaning} at code:{at:#x}')
    return len(FIELDS)


def check_code(c) -> dict:
    blocks = {}
    for name, (start, end, expected) in BLOCKS.items():
        digest = hashlib.sha256(c.code.data[start:end]).hexdigest()
        require(digest, expected, f'{name} bounded code digest')
        blocks[name] = {'code_start': start, 'code_end_exclusive': end, 'sha256': digest}
    check_fields(c)
    for at, target in CALLS.items():
        require(call_target(c, at), target, f'call at code:{at:#x}')
    for at, symbol in IMPORTS.items():
        require(glue_symbol(c, at), symbol, f'import glue at code:{at:#x}')
    for at, (*fields, meaning) in FMADDS.items():
        require(a_fields(c, at), (*fields, 29), f'{meaning} at code:{at:#x}')
    for at, (*fields, meaning) in MASKS.items():
        require(rlwinm_fields(c, at), tuple(fields), f'{meaning} at code:{at:#x}')
    constants = {}
    for offset, expected in TOC_CONSTANTS.items():
        value = struct.unpack_from('>f', c.data_section.data, TOC + offset)[0]
        require(value, expected, f'TOC constant {offset}')
        constants[str(offset)] = value
    pointers = {}
    for offset, expected in TOC_POINTERS.items():
        pointers[str(offset)] = require(toc_pointer(c, offset), expected, f'TOC pointer {offset}')
    data_constants = {}
    for address, fmt, expected, meaning in DATA_CONSTANTS:
        value = struct.unpack_from(fmt, c.data_section.data, address)[0]
        require(value, expected, f'{meaning} data:{address:#x}')
        data_constants[hex(address)] = value
    return {'blocks': blocks, 'toc_constants': constants, 'toc_pointers': pointers,
            'data_constants': data_constants, 'field_witnesses': len(FIELDS), 'call_witnesses': len(CALLS),
            'fmadds_witnesses': len(FMADDS), 'mask_witnesses': len(MASKS), 'import_witnesses': len(IMPORTS)}


def inspect(bin_root: Path) -> dict:
    c = load_identified(bin_root / 'SimThemePark.data')
    require((c.code.index, c.data_section.index), (0, 1), 'section indices')
    report = check_code(c)
    report['settings_offsets'] = descriptor_offsets(bytes(c.data_section.data))
    report['binary_sha256'] = hashlib.sha256(c.raw).hexdigest()
    report['witness_source'] = Path(__file__).resolve().relative_to(REPO).as_posix()
    report['plan'] = 'docs/reverse/COAST-plan.md'
    report['limitation'] = ('Static, bounded: no original execution; Mac build only; spline/sample '
                            'generation, full motion, camera and charging are not derived.')
    return report


# ---------------------------------------------------------------- PC data (optional)

SAM_LINE = re.compile(r'^\s*([A-Za-z][\w.\[\]]*)\s+("[^"]*"|\S+)', re.M)
THEMES = ('fantasy', 'hallow', 'jungle', 'space')


def sam_values(text: str) -> dict[str, str]:
    return {key: value for key, value in SAM_LINE.findall(text)}


def track_catalog(pc_data: Path) -> dict:
    """Every ride .sam with Bumper.WhichTrackType 1..3, plus coaster.sam settings for type 3."""
    sys.path.insert(0, str(TOOLS / 'lanes' / 'ui'))
    import corpus  # noqa: E402  (ui lane WAD reader, bounded)
    result = {}
    for theme in THEMES:
        rides = pc_data / 'levels' / theme / 'rides'
        for wad in sorted(rides.glob('*.wad')):
            files = corpus.wad(wad)
            members = {name.lower(): name for name in files}
            primary = members.get(f'{wad.stem.lower()}.sam')
            if primary is None:
                continue
            values = sam_values(files[primary].decode('latin-1'))
            track_type = int(values.get('Bumper.WhichTrackType', '0'))
            if track_type == 0:
                continue
            entry = {'theme': theme, 'wad': wad.name, 'track_type': track_type,
                     'id': int(values['Info.Id']), 'name': values['Info.Name'].strip('"'),
                     'price': int(values.get('Upgrades[0].CostOfUpgrade', '0')),
                     'excitement_level': int(values.get('UsageInfo.ExcitementLevel', '0'))}
            if 'coaster.sam' in members:
                settings = sam_values(files[members['coaster.sam']].decode('latin-1'))
                pylons = sorted({int(k.split('[')[1].split(']')[0]) for k in settings
                                 if k.startswith('asPylonControls[')})
                entry['pylons'] = [{
                    'mesh': settings.get(f'asPylonControls[{i}].pcMeshFilename', '').strip('"'),
                    'cost_per_unit': float(settings.get(f'asPylonControls[{i}].fCostPerUnit', '0')),
                    'cost_per_2d_length': float(settings.get(f'asPylonControls[{i}].fCostPer2DLength', '0')),
                    'cost_per_height': float(settings.get(f'asPylonControls[{i}].fCostPerHeight', '0')),
                    'max_stacked': int(settings.get(f'asPylonControls[{i}].uiMaxStackedPylons', '0')),
                    'min_height': float(settings.get(f'asPylonControls[{i}].fMinHeight', '0')),
                    'max_height': float(settings.get(f'asPylonControls[{i}].fMaxHeight', '0')),
                } for i in pylons]
                entry['max_trains'] = int(settings.get('sCoasterType.uiMaxTrains', DEFAULTS['uiMaxTrains']))
                entry['train_release_rate'] = int(settings.get('sCoasterType.uiTrainReleaseRate',
                                                               DEFAULTS['uiTrainReleaseRate']))
                entry['max_cars'] = int(settings.get('sTrainType.uiMaxCars', '0'))
                entry['overrides'] = sorted(k for k in settings if k.startswith('sCoasterType.f'))
            result[entry['id']] = entry
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--pc-data', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
        if args.pc_data:
            result['track_catalog'] = {str(k): v for k, v in sorted(track_catalog(args.pc_data).items())}
    except (OSError, KeyError, ValueError, pef.PEFError) as error:
        parser.exit(1, f'coast evidence: {error}\n')
    print(json.dumps(result, sort_keys=True, indent=2))


if __name__ == '__main__':
    main()
