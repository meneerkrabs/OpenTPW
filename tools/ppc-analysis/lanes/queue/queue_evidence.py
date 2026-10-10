"""Bounded static queue witnesses (QUEUE-R); never execute or emit original instructions.

Usage: python3 -I queue_evidence.py /path/to/mac-feral/bin [--pc-data /path/to/Data]

Addresses are section-relative. Instructions are decoded into fields and compared with
expected operands; only digests, decoded fields and derived values are reported. The pure
functions below restate the decoded arithmetic so that tests can pin it without the binary.
See docs/reverse/QUEUE-plan.md for the narrative and the claim bounds.
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
sys.path.insert(0, str(TOOLS / 'lanes' / 'economy'))
import pef  # noqa: E402
from timer_evidence import call_target, d_fields, load_identified, require  # noqa: E402
from schema import fields as schema_fields  # noqa: E402

TOC = 0x8000
GAME_TURN = -22772  # *(data:0x11ef04) + 0x1e0000 - 22772 = game + 0x1da70c, the park-turn counter
NOMINAL_TURN_MS = 248  # PPC-clock: 31 ms substep x 8, scaled scheduler clock

BLOCKS = {
    'standing_queue': (0xed244, 0xed754, 'ab165d0f08b0db025f41b6e3fe024074f8543f9c59c3b61ab6ae522e92f65522'),
    'leave_queue': (0xed754, 0xed7e0, 'f564da452f008458e44ab6071761c4a092e3964e14679e2d6969f4bb0b28a5a4'),
    'join_at_back': (0xeccb0, 0xed0c4, 'e2799b96fb5d675780bc4e9a63ce4f0a338aeb5dfbcd8f37503846058ecc8e48'),
    'state_entry': (0xef700, 0xefd84, '1a878ae687d7f5ed5d9025988a63ee7b2d41e4bd0b8b1d9fc2829873eb16626a'),
    'state_dispatch': (0xef240, 0xef700, '327b61c052e3a8e2ce19feb98975d9faf7296dfa1122d54d5e0082f0896bff97'),
    'idle_interlude': (0xe8b74, 0xe8c34, 'd5d17583289a11feccfab8f77632fb470a1d113742c751ca56f85875537defd1'),
    'idle_restore': (0xe8c34, 0xe8c58, 'c589444c1d823376e6e86f7564a5e2a8ff2b6bf5878e7218462155a5b223a207'),
    'physical_room': (0xdc988, 0xdc9d4, 'c35c00c3846be86c9fdd688864f1655cff41d0e1540fd64e89d2a9cb2fbddce9'),
    'queue_limit_value': (0xdc9d4, 0xdcb74, '53b465ea138ccc6d7a566847b6c5c49579263dc4f3c626e5df494c6ea6689914'),
    'queue_full': (0xdcb74, 0xdcd34, '43e0b767478495cac9edea4102c5e2c0e07d2ced1e2f38131578c5511782c492'),
    'queue_append': (0xdcd34, 0xdcebc, '65f8db9240ad782207bc5e6374d0746fe7114dff9237c4261bb43eb697d92f83'),
    'queue_remove': (0xdcebc, 0xdd118, 'd8515dd4411edc5f9a40899b7469798c93b7b3b1a0caf3bf8c631b2de1196056'),
    'queue_count': (0xdd118, 0xdd144, 'd4ab68e86f3540ebd58f3d116e50e4b231effbded342614ad955d706e3e989c8'),
    'queue_position': (0xdd144, 0xdd2b4, '8d4f54fbe522a99e81c4f56aff6d74e4e30b4b0eb63fc308026b83181c1228d7'),
    'front_cell': (0xdd2b4, 0xdd3f4, 'abb699443fe54b7b1550d7067a59874d94b09f74c518ca723e6ca55775dcbfb1'),
    'back_of_queue': (0xdd3f4, 0xdd57c, '11c81cd33d1df53b9b4f80a77516f37b2d4961569fadbbd7ac8035f7957ed986'),
    'queue_edited': (0xdd57c, 0xdd744, '327561e6280f22cc9a8cbb299970deb50058f602bd6b0c442e5c951ec9d2bfe2'),
    'next_queue_cell': (0xdda18, 0xddc2c, '3df1e0fbc97f611af11bb310c3d30024850673de879700a43435f5da6f8dd45b'),
    'queue_slot_dispatch': (0xddc2c, 0xddcc4, '24a0248ddadba0bf686fbad6f151c0f294835beadd33bda9b8c85f544999b240'),
    'queue_slot': (0xddcc4, 0xde02c, '887248bd61929abb31c31989b7693603c8c5a5df3759faf55e71d5bc18a9a2cd'),
    'find_queue_destination': (0xee604, 0xee76c, '3ffad58fb9f35d2e07f52f59bed87fe1d84223bcad6bb01ab22f81619c183ca7'),
    'standing_at_front': (0xee76c, 0xee794, 'c28f465a2ccf063dd816e5ee2d0ac407aceae8cef07bc51435b84a0606bf09f0'),
    'call_forward': (0xee794, 0xee804, '0f2505a261478fc3205bfc51757d19ac813c27dfbd684c39a41cddd45e5ad2cb'),
    'pending_visitor': (0xe0620, 0xe063c, '1d72cf33aefdc7a4f228c065fdb42af7e7f0dcd5c09d7bdafa381dc015013b45'),
    'admission_step': (0xe1404, 0xe1864, 'c25cdc71d6cb6fa17548b524d750ab155ac72623fd5539e736e7b52931d9023d'),
    'ride_update': (0xe0a8c, 0xe0b00, '7850ffe642ce69a0f3d885e1a1f4e2c393c77af2fedb87545905cff926834879'),
    'ride_parameter_setters': (0xdc5c4, 0xdc834, 'ac6aefff1ed0528b1e557fa14130d3554115c3317ec38200fc648398cb6874eb'),
    'cell_type_predicates': (0x851ac, 0x851ec, '147b0eaf726798c2e095b64ba069390a31338885ba3abd0abf8abba5ea4d14ac'),
    'queue_link_accessors': (0x6e1a8, 0x6e230, '269d79ca419ebcfdb93e4c8ec14822c2f3b5edb9a1737c0005b55fc79c59fbf0'),
    'bounce': (0xadf40, 0xae020, '153a79424a1aba3daf9bac46582cd2c4faf689fba711becfcded10e64d91df6f'),
    'unbounce': (0xae020, 0xae114, 'e7788848c05e1d05235935340da2c0ee7346bf64a0c8fcce3b94412e97579d32'),
    'rng': (0x105328, 0x105360, '769e529537a1604011476554bdc2f3c84ec91ed2156dac08be48fbd70cd8597e'),
    'uint_conversion': (0x1c3fbc, 0x1c4010, '1b81ce4f120459932643cbc133150a21a853e4a72d861e9b64872af4d1667ba6'),
}

# (code offset, primary opcode, (rD/rS, rA, signed immediate), meaning). Decoded, never raw words.
FIELDS = (
    # Standing in queue (state 11, 0xed244): needs window, boredom timeout, delay counter.
    (0xed590, 32, (0, 28, 520), 'load guest +520 (time of last idle interlude)'),
    (0xed598, 32, (5, 4, GAME_TURN), 'load park-turn counter'),
    (0xed5a0, 10, (0, 0, 30), 'cmplwi turns since interlude, 30'),
    (0xed5a8, 48, (0, 28, 412), 'lfs guest +412 (happiness-like)'),
    (0xed5bc, 10, (0, 0, 80), 'happiness > 80 -> idle animation 5'),
    (0xed5e0, 10, (0, 0, 20), 'happiness < 20 band'),
    (0xed5f4, 10, (0, 0, 10), 'happiness < 10 -> thought 11, leave queue'),
    (0xed628, 48, (0, 28, 428), 'lfs guest +428 (toilet)'),
    (0xed63c, 10, (0, 0, 80), 'toilet > 80 -> thought 4'),
    (0xed654, 40, (0, 29, 46), 'ride flags +46 (bit 0 = ProvidesRelief keeps the guest)'),
    (0xed66c, 32, (4, 28, 508), 'load guest +508 (state-11 entry turn)'),
    (0xed670, 14, (0, 4, 100), 'boredom timeout: entry turn + 100'),
    (0xed4e4, 14, (0, 4, -1), 'move delay +500 decrements by one'),
    (0xed4a4, 10, (0, 0, 2), 'delay applies only when the gap to the recorded position is <= 2'),
    # State entry (0xef700): state 11 resets the boredom origin and the move delay.
    (0xef880, 32, (3, 3, GAME_TURN), 'state 11 entry reads the park-turn counter'),
    (0xef884, 36, (3, 31, 508), 'state 11 entry: +508 = counter'),
    (0xef8ac, 36, (3, 31, 500), 'state 11 entry: +500 = uint(1.2 * +497)'),
    (0xefd2c, 32, (0, 3, GAME_TURN), 'state 8 entry reads the park-turn counter'),
    (0xefd30, 36, (0, 31, 520), 'state 8 entry: +520 = counter'),
    # Idle interlude 0xe8b74 and its restore in the state-8 handler.
    (0xe8bfc, 14, (4, 0, 8), 'interlude enters state 8'),
    (0xe8c0c, 36, (0, 30, 520), 'interlude: +520 = counter'),
    (0xe8c14, 36, (0, 30, 548), 'interlude saves the current state in +548'),
    (0xef6cc, 32, (3, 27, 520), 'state 8 handler reads +520'),
    (0xef6d8, 14, (0, 3, 10), 'state 8 lasts until counter > +520 + 10'),
    # Queue limit 0xdcb74 / 0xdc9d4 and physical room 0xdc988.
    (0xdcba4, 40, (0, 3, 46), 'ride flags +46'),
    (0xdcbb0, 14, (28, 0, 100), 'HasQueue flag (bit 3) -> limit 100'),
    (0xdcbb8, 32, (0, 29, 84), 'SPEED (+84)'),
    (0xdcbd8, 34, (0, 29, 76), 'upgrade level byte +76'),
    (0xdcbf4, 32, (0, 3, 424), 'Upgrades[level].InitSpeed'),
    (0xdcc58, 48, (31, 3, 436), 'Upgrades[level].QueueWaitTimeConstant'),
    (0xdcc80, 34, (0, 29, 88), 'DUR (+88)'),
    (0xdcc9c, 34, (4, 29, 89), 'CAPACITY (+89)'),
    (0xdc9a4, 32, (0, 30, 60), 'queue size in cells (+60), compared with count after x4'),
    # Guest linked list on the ride.
    (0xdd16c, 40, (0, 3, 56), 'queue head guest id (+56)'),
    (0xdd1c8, 40, (0, 26, 552), 'next guest id (guest +552)'),
    (0xdcda0, 44, (0, 27, 56), 'append to empty queue sets head'),
    (0xdce4c, 44, (0, 3, 552), 'append links old tail -> new guest'),
    (0xdce80, 44, (3, 5, 554), 'new guest +554 = previous guest'),
    (0xdcea4, 44, (0, 3, 552), 'new guest +552 = 0 (tail)'),
    # Queue cells: front cell, back of queue, size in cells, next cell and per-cell positions.
    (0xdd2e0, 40, (0, 4, 50), 'ride entrance cell id (+50)'),
    (0xdd2f4, 7, (0, 0, 68), '68-byte map cell record'),
    (0xdd458, 14, (29, 0, 1000), 'GetBackOfQueue iteration guard 1000'),
    (0xdd46c, 40, (4, 4, 54), 'cached back-of-queue cell (+54)'),
    (0xdd4c0, 36, (0, 28, 60), 'size in cells reset to 0'),
    (0xdd4e0, 14, (0, 3, 1), 'size in cells +1 per walked cell'),
    (0xdd4e4, 36, (0, 28, 60), 'size in cells store'),
    (0x851b0, 11, (0, 0, 3), 'cell type 3 accepted by the queue walk'),
    (0x851b8, 11, (0, 0, 9), 'cell type 9 accepted by 0x851ac'),
    (0x851d4, 11, (0, 0, 9), 'cell type 9 rejected by 0x851d0 in the walk'),
    (0x6e228, 34, (3, 3, 13), 'queue link direction byte +13 (read)'),
    (0x6e1a8, 38, (4, 3, 13), 'queue link direction byte +13 (write)'),
    (0xddab0, 10, (0, 0, 16), 'neighbour link must be 16'),
    (0xddb1c, 10, (0, 0, 1), 'neighbour link must be 1'),
    (0xddb88, 10, (0, 0, 64), 'neighbour link must be 64'),
    (0xddbf4, 10, (0, 0, 4), 'neighbour link must be 4'),
    (0xddd30, 14, (21, 21, -4), 'four positions per queue cell'),
    (0xddd3c, 10, (0, 21, 4), 'walk while remaining position >= 4'),
    (0xdddc8, 7, (0, 0, 28), 'lateral jitter rand mod 28'),
    (0xdddd0, 14, (25, 3, 114), 'lateral jitter + 114'),
    # Joining at the back (state 10, 0xeccb0).
    (0xecda8, 11, (0, 0, 45), 'join-time price/score gate 45'),
    (0xee688, 38, (28, 30, 497), 'recorded queue position +497 = list position'),
    (0xee72c, 14, (4, 0, 12), 'walking to the queue position is state 12'),
    # Admission (ride update 0xe1404) and the front guest.
    (0xe1468, 14, (4, 0, 0), 'script variable 0 (LETMEON)'),
    (0xe143c, 14, (4, 0, 2), 'script variable 2 (CAPACITY)'),
    (0xe148c, 14, (4, 0, 5), 'script variable 5 (ONRIDE)'),
    (0xe14d0, 14, (4, 0, 9), 'script variable 9 (RUNNING)'),
    (0xe1504, 40, (4, 29, 56), 'queue head'),
    (0xe1564, 44, (0, 29, 104), 'pending visitor +104 = head'),
    (0xee774, 11, (0, 0, 11), 'head must be in state 11'),
    (0xee77c, 34, (0, 3, 497), 'head must have recorded position 0'),
    (0xee7e8, 36, (0, 30, 504), 'called-forward flag +504 = 1'),
    (0xe0628, 40, (0, 3, 104), 'admitted iff pending visitor == guest'),
    # Ride parameters: setters and build-time initialization from Upgrades[0].
    (0xdc604, 36, (31, 30, 84), 'SPEED setter stores +84'),
    (0xdc658, 32, (0, 3, 300), 'DUR lower clamp UsageInfo.MinDuration'),
    (0xdc698, 32, (0, 3, 304), 'DUR upper clamp UsageInfo.MaxDuration'),
    (0xdc6cc, 14, (4, 0, 3), 'DUR written to script variable 3'),
    (0xdc6e8, 38, (30, 29, 88), 'DUR stored at +88'),
    (0xdc75c, 32, (3, 3, 292), 'CAPACITY clamp UsageInfo.MinCapacity'),
    (0xdc760, 32, (0, 30, 296), 'CAPACITY clamp UsageInfo.MaxCapacity'),
    (0xdc7f4, 14, (4, 0, 2), 'CAPACITY written to script variable 2'),
    (0xdc810, 38, (28, 31, 89), 'CAPACITY stored at +89'),
    (0xdad4c, 32, (4, 28, 424), 'build: Upgrades[0].InitSpeed'),
    (0xdad60, 32, (0, 28, 408), 'build: Upgrades[0].InitCapacity'),
    (0xdad78, 32, (0, 28, 416), 'build: Upgrades[0].InitDuration'),
    (0xdab6c, 32, (0, 28, 244), 'build: UsageInfo.ProvidesRelief -> flag 0x1'),
    (0xdab80, 24, (0, 0, 1), 'flag 0x1'),
    (0xdac0c, 32, (0, 28, 64), 'build: Info.HasQueue -> flag 0x8'),
    (0xdac1c, 24, (0, 0, 8), 'flag 0x8'),
    (0xdaca4, 32, (0, 28, 72), 'build: Info.RunsContinuously -> flag 0x100'),
    (0xdacb4, 24, (0, 0, 256), 'flag 0x100'),
    # BOUNCE: hold deadline = scheduler ms + duration * 1000.
    (0xadfb4, 7, (5, 28, 1000), 'BOUNCE duration operand x 1000 ms'),
    (0xae0a4, 7, (0, 0, 1000), 'UNBOUNCE elapsed mod 1000'),
    # Park-turn counter increment.
    (0x10539c, 14, (0, 3, 1), 'park turn +1'),
    (0x1053a0, 36, (0, 4, GAME_TURN), 'park turn store'),
)

CALLS = {
    0xef4ac: 0xed244, 0xef4b8: 0xe6454, 0xef6e8: 0xe8c34, 0xe8c18: 0xef700, 0xed5cc: 0xe8b74,
    0xed620: 0xe8b74, 0xed6c4: 0xed754, 0xed2c8: 0xe0620, 0xed460: 0xdd144, 0xed4c0: 0xee604,
    0xed4f4: 0xdc9d4, 0xed424: 0xdfe34, 0xef8a8: 0x1c3fbc, 0xecd48: 0xdd3f4, 0xecd60: 0xdc988,
    0xecdb4: 0xdcb74, 0xece68: 0xdcd34, 0xece70: 0xee604, 0xee66c: 0xdd144, 0xee6c8: 0xddc2c,
    0xddc70: 0xddcc4, 0xddd38: 0xdda18, 0xddd98: 0x105328, 0xdd4d8: 0xdda18, 0xdd130: 0xdd144,
    0xdccfc: 0xdd118, 0xdc9a0: 0xdd118, 0xdcbd4: 0x118018, 0xdcc40: 0x118018, 0xe1544: 0xee76c,
    0xe1554: 0xee794, 0xe1890: 0xe1404, 0xe0ae4: 0xe1864, 0xfaa64: 0xef240, 0xfaa78: 0xe0a8c,
    0xdad5c: 0xdc5c4, 0xdad74: 0xdc708, 0xdad8c: 0xdc620, 0xdc6d0: 0xb57d4, 0xdc7f8: 0xb57d4,
    0xb1504: 0xadf40, 0xb1520: 0xae020, 0xadfb0: 0x10e844, 0xae074: 0x10e844,
}

DATA_CONSTANTS = (
    (0x55a0, '>f', 1.2000000476837158, 'move-delay factor (state 11 entry)'),
    (0x54d8, '>f', 0.25, 'intra-cell depth step'),
    (0x54dc, '>f', 255.0, 'intra-cell depth scale'),
    (0x54e8, '>f', 4.0, 'queue-limit floor'),
    (0x54ec, '>f', 1.0, 'speed factor when SPEED is 0'),
    (0x54fc, '>f', 0.0, 'QueueWaitTimeConstant > 0 diagnostic'),
)

SCHEMA_EXPECTED = {
    'Info.IsChoosable': 60, 'Info.HasQueue': 64, 'Info.RunsContinuously': 72, 'Info.DurationUnit': 112,
    'UsageInfo.ProvidesRelief': 244, 'UsageInfo.MinCapacity': 292, 'UsageInfo.MaxCapacity': 296,
    'UsageInfo.MinDuration': 300, 'UsageInfo.MaxDuration': 304, 'UsageInfo.MinSpeed': 308,
    'UsageInfo.MaxSpeed': 312, 'Upgrades.InitCapacity': 408, 'Upgrades.InitDuration': 416,
    'Upgrades.InitSpeed': 424, 'Upgrades.QueueWaitTimeConstant': 436,
}

DIAGNOSTICS = {  # string base + offset -> leading text, checked as text, not as code
    (0x1d0cac, 1120): b"Person %d: OK, I've been standing here too long, I'm bored and leaving the queue",
    (0x1d0cac, 1076): b"Couldn't get to my intended queue position",
    (0x1d0cac, 984): b"Problem with a queue - shouldn't be fatal",
    (0x1ceb50, 780): b'No queue constant entered in SAM file',
    (0x1ceb50, 860): b'Operating duration set to zero',
    (0x1ceb50, 743): b'SPEED = %d',
    (0x1ceb50, 755): b'DUR = %d',
    (0x1ceb50, 765): b'CAPACITY = %d',
    (0x1ceb50, 1249): b'*** GetBackOfQueue() crashed! ***',
    (0x1ceb50, 1284): b"Object's queue is now %d cells long",
    (0x1ceb50, 1422): b'Virtual queue problem!',
    (0x1ceb50, 3047): b'Object %d::LetMeOn - person being loaded is %d',
}


# ---------------------------------------------------------------- pure restatements

def f32(value: float) -> float:
    """Round to IEEE single, as every fsubs/fmuls/fdivs/lfs result in these blocks is."""
    return struct.unpack('>f', struct.pack('>f', value))[0]


def uint_convert(value: float) -> int:
    """0x1c3fbc for the finite non-negative range used here: truncate toward zero."""
    if value != value or value <= 0.0:
        return 0
    if value >= 4294967296.0:
        return 0xffffffff
    return int(value)


def speed_factor(speed: int, init_speed: int) -> float:
    """0xdcbb8..0xdcc2c: SPEED / Upgrades[level].InitSpeed in single precision, 1.0 when SPEED is 0."""
    if speed == 0:
        return 1.0
    return f32(f32(float(speed & 0xffffffff)) / f32(float(init_speed)))


def queue_limit(has_queue: bool, queue_wait_time_constant: float, speed: int, init_speed: int,
                capacity: int, duration: int) -> int:
    """0xdcb74/0xdc9d4: the admission limit compared with the queued count (full when count >= limit).

    HasQueue objects (flag 0x8) get 100. Others get trunc(max(QWTC * (G * CAP) / DUR, 4.0)) with the
    products and the quotient rounded to single; a NaN quotient fails the > 4.0 test and becomes 4.
    """
    if has_queue:
        return 100
    g = speed_factor(speed, init_speed)
    product = f32(f32(queue_wait_time_constant) * f32(g * float(capacity)))
    quotient = f32(product / float(duration)) if duration else (math.inf if product > 0 else math.nan)
    return uint_convert(quotient if quotient > 4.0 else 4.0)


def physical_room(count: int, cells: int) -> bool:
    """0xdc988: room while count < 4 * size-in-cells (unsigned)."""
    return (count & 0xffffffff) < ((4 * cells) & 0xffffffff)


def may_join(count: int, cells: int, limit: int) -> bool:
    """State 10 at the back of the queue: physical room and not full (0xecd60, 0xecdb4)."""
    return physical_room(count, cells) and count < limit


def max_queue_length(cells: int, limit: int) -> int:
    """Largest count reachable by joins: min(4 * cells, limit)."""
    return min(4 * cells, limit)


def move_delay(recorded_position: int) -> int:
    """State 11 entry (0xef878..0xef8ac): +500 = uint(single(1.2f * position))."""
    return uint_convert(f32(f32(1.2000000476837158) * f32(float(recorded_position))))


def delays_move(recorded_position: int, actual_position: int, delay: int) -> bool:
    """0xed488..0xed4e8: wait (and decrement) only if delay != 0 and 0 <= recorded - actual <= 2."""
    if recorded_position == actual_position:
        return False
    gap = (recorded_position - actual_position) & 0xffffffff
    return delay != 0 and gap <= 2


def queue_slot(position: int, cells_available: int) -> tuple[int, int]:
    """0xddcc4: (cells walked from the front, depth byte inside the cell) for a position.

    Four positions per cell: while remaining >= 4 and the current cell is not the terminator,
    subtract 4 and follow the link (0xddd2c..0xddd50). walked == cells_available means the walk
    ended on the terminator; physical_room keeps joins below that. Depth is
    trunc(255 * (0.25 * remaining)) in single precision: 0, 63, 127, 191 for remaining 0..3.
    """
    walked = 0
    remaining = position
    while remaining >= 4 and walked < cells_available:
        remaining -= 4
        walked += 1
    depth = int(f32(255.0 * f32(0.25 * float(remaining))))
    return walked, depth & 0xff


def lateral_offset(random_value: int) -> int:
    """0xddd9c..0xdddd0: rand mod 28 + 114 (sub-cell byte across the queue)."""
    return (random_value & 0xffffffff) % 28 + 114


def bored(counter: int, entry_turn: int, interlude_turn: int) -> bool:
    """0xed58c..0xed678: the boredom exit needs both conditions (unsigned arithmetic)."""
    window = ((counter - interlude_turn) & 0xffffffff) <= 30
    timeout = (counter & 0xffffffff) > ((entry_turn + 100) & 0xffffffff)
    return window and timeout


def interlude_due(counter: int, interlude_turn: int) -> bool:
    """The happiness/toilet checks run only when more than 30 turns passed since the last interlude."""
    return ((counter - interlude_turn) & 0xffffffff) > 30


def bounce_deadline_ms(now_ms: int, duration: int) -> int:
    """0xadfb4: BOUNCE stores now + duration * 1000 (scheduler milliseconds)."""
    return now_ms + duration * 1000


def unbounce_releases(now_ms: int, start_ms: int, deadline_ms: int) -> bool:
    """0xae088..0xae0bc: release once deadline < now and (now - start) mod 1000 < 200."""
    elapsed = now_ms - start_ms
    remainder = int(math.fmod(elapsed, 1000))
    return deadline_ms < now_ms and int(remainder / 200) == 0


def bounce_wait_bound(position: int, capacity: int, duration_s: int, boarding_s: float = 0.0) -> float:
    """Upper bound on the wait of the guest at 0-based queue position `position` for a BOUNCE ride.

    Every slot frees after DUR s plus < 1 s release granularity; the (position+1)-th free slot comes
    within floor(position / CAP) + 1 slot turnovers. `boarding_s` (call -> walk -> admission -> BOUNCE)
    is not derived and must be supplied as a labelled approximation.
    """
    if capacity <= 0:
        raise ValueError('capacity must be positive')
    return (position // capacity + 1) * (duration_s + 1 + boarding_s)


def clamp_parameter(value: int, minimum: int, maximum: int) -> int:
    """0xdc620/0xdc708: below minimum -> minimum, else above maximum -> maximum (signed)."""
    if value < minimum:
        return minimum
    if value > maximum:
        return maximum
    return value


# ---------------------------------------------------------------- binary witness

def rlwinm_fields(c, at: int) -> tuple[int, int, int, int, int]:
    word = pef._u32(c.code.data, at)
    require(word >> 26, 21, f'rlwinm at code:{at:#x}')
    return (word >> 21 & 31, word >> 16 & 31, word >> 11 & 31, word >> 6 & 31, word >> 1 & 31)


def x_compare(c, at: int) -> tuple[str, int, int, int]:
    word = pef._u32(c.code.data, at)
    require(word >> 26, 31, f'X-form compare at code:{at:#x}')
    kind = {0: 'cmpw', 32: 'cmplw'}.get(word >> 1 & 0x3ff)
    require(kind is not None, True, f'compare kind at code:{at:#x}')
    return (kind, word >> 23 & 7, word >> 16 & 31, word >> 11 & 31)


def branch_conditional(c, at: int) -> tuple[int, int, int]:
    """(BO, BI, absolute target) of a relative bc."""
    word = pef._u32(c.code.data, at)
    require(word >> 26, 16, f'conditional branch at code:{at:#x}')
    displacement = word & 0xfffc
    if displacement & 0x8000:
        displacement -= 0x10000
    return (word >> 21 & 31, word >> 16 & 31, at + displacement)


def schema_offsets(data: bytes) -> dict:
    rows = schema_fields(data, 0x39b64, embedded_offset=4)
    selected = {}
    for row in rows:
        if row['path'] in SCHEMA_EXPECTED:
            selected[row['path']] = row['runtime_offset']
    require(selected, SCHEMA_EXPECTED, 'ride SAM schema runtime offsets')
    kinds = {row['path']: row['kind'] for row in rows if row['path'] in SCHEMA_EXPECTED}
    require(kinds['Upgrades.QueueWaitTimeConstant'], 7, 'QueueWaitTimeConstant descriptor kind (float)')
    stride = {row['path']: row.get('array_stride') for row in rows if row['path'].startswith('Upgrades.')}
    require(set(stride.values()), {64}, 'Upgrades record stride')
    return selected


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
    # Branch shape of the boredom gate: window (gt false -> timeout test), timeout (gt false -> skip).
    require(branch_conditional(c, 0xed5a4), (4, 1, 0xed66c), 'window branch (<= 30 turns -> timeout test)')
    require(x_compare(c, 0xed674), ('cmplw', 0, 5, 0), 'timeout compares counter with +508 + 100')
    require(branch_conditional(c, 0xed678), (4, 1, 0xed6cc), 'timeout branch (not later -> stay)')
    require(x_compare(c, 0xef6dc), ('cmplw', 0, 4, 0), 'state 8 compares counter with +520 + 10')
    # Flag tests: HasQueue bit 3 in the limit; RunsContinuously bit 8 in admission.
    require(rlwinm_fields(c, 0xdcba8), (0, 0, 0, 28, 28), 'HasQueue mask 0x8')
    require(rlwinm_fields(c, 0xe14e4), (0, 0, 0, 23, 23), 'RunsContinuously mask 0x100')
    require(rlwinm_fields(c, 0xdcbe4), (0, 0, 6, 0, 25), 'upgrade level x 64')
    require(x_compare(c, 0xae088), ('cmpw', 0, 0, 31), 'UNBOUNCE deadline vs now')
    constants = {}
    for offset, fmt, expected, meaning in DATA_CONSTANTS:
        value = struct.unpack_from(fmt, c.data_section.data, offset)[0]
        require(value, expected, f'{meaning} data:{offset:#x}')
        constants[hex(offset)] = value
    diagnostics = {}
    for (base, offset), text in DIAGNOSTICS.items():
        at = base + offset
        require(bytes(c.code.data[at:at + len(text)]), text, f'diagnostic text at code:{at:#x}')
        diagnostics[f'{at:#x}'] = text.decode('ascii')
    return {'blocks': blocks, 'constants': constants, 'diagnostics': diagnostics,
            'field_witnesses': len(FIELDS), 'call_witnesses': len(CALLS)}


def inspect(bin_root: Path) -> dict:
    c = load_identified(bin_root / 'SimThemePark.data')
    require((c.code.index, c.data_section.index), (0, 1), 'section indices')
    report = check_code(c)
    report['schema_offsets'] = schema_offsets(bytes(c.data_section.data))
    report['binary_sha256'] = hashlib.sha256(c.raw).hexdigest()
    report['witness_source'] = Path(__file__).resolve().relative_to(REPO).as_posix()
    report['plan'] = 'docs/reverse/QUEUE-plan.md'
    report['limitation'] = ('Static, bounded: no original execution; build-tool placement rules, '
                            'boarding latency and non-BOUNCE ride cycles are not derived.')
    return report


# ---------------------------------------------------------------- PC data (optional)

SAM_LINE = re.compile(r'^\s*([A-Za-z][\w.\[\]]*)\s+("[^"]*"|\S+)', re.M)


def sam_values(text: str) -> dict[str, str]:
    return {key: value for key, value in SAM_LINE.findall(text)}


def ride_parameters(pc_data: Path, theme: str, wad_name: str, sam_name: str) -> dict:
    """Layer <theme>/rides/Rides.sam defaults under the object's .sam (as OpenTPW's catalog does)."""
    sys.path.insert(0, str(TOOLS / 'lanes' / 'ui'))
    import corpus  # noqa: E402  (ui lane WAD reader, bounded)
    rides = pc_data / 'levels' / theme / 'rides'
    values = sam_values((rides / 'Rides.sam').read_text(encoding='latin-1'))
    files = corpus.wad(rides / wad_name)
    values.update(sam_values(files[sam_name].decode('latin-1')))

    def number(key):
        return int(values[key])
    capacity = clamp_parameter(number('Upgrades[0].InitCapacity'), number('UsageInfo.MinCapacity'),
                               number('UsageInfo.MaxCapacity'))
    duration = clamp_parameter(number('Upgrades[0].InitDuration'), number('UsageInfo.MinDuration'),
                               number('UsageInfo.MaxDuration'))
    return {'id': number('Info.Id'), 'has_queue': number('Info.HasQueue') != 0,
            'duration_unit': number('Info.DurationUnit'), 'capacity': capacity, 'duration': duration,
            'init_speed': number('Upgrades[0].InitSpeed'),
            'queue_wait_time_constant': float(values['Upgrades[0].QueueWaitTimeConstant'])}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--pc-data', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
        if args.pc_data:
            bouncy = ride_parameters(args.pc_data, 'jungle', 'bouncy.wad', 'Bouncy.sam')
            result['belly_bounce'] = bouncy
    except (OSError, KeyError, ValueError, pef.PEFError) as error:
        parser.exit(1, f'queue evidence: {error}\n')
    print(json.dumps(result, sort_keys=True, indent=2))


if __name__ == '__main__':
    main()
