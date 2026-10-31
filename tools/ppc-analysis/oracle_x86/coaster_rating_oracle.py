"""tp.exe's coaster rating pass run in the x86 oracle sandbox at the thread's x87 control word (docs/reverse/ORACLE.md, finding X44; COAST-032, COAST-014).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified tp.exe (OPENTPW_TPW_EXE, SHA-256 pinned) are needed, nothing here is shipped. The rating pass 0x43C450 runs
on synthetic records the oracle lays out in the sandbox heap (a controller, a type record and a list of 56-byte samples), with the control word 0x7F the shipped D3DIM device leaves
the thread at (24-bit significand, X87Precision) and the PC sine table in place. No original data and no game object enter; the train routines the pass calls after its final are
stubbed and only record their arguments.
"""
from __future__ import annotations

import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from coaster_x87_oracle import CoasterOracle, bits, f32, from_bits  # noqa: E402

PASS = 0x43C450
TRAIN_COUNT = 0x43BA20      # (controller, count, 0): allocates the trains
TRAIN_PLACE = 0x43C1F0      # (controller, lap seconds): spaces the trains along the track

# the type record's offsets (the same as the Mac's): flags +0 (0x100 trough, 0x8 suspended), then the named floats
DEF_FIELDS = {
    'acceleration_per_height': 0x24, 'uphill': 0x28, 'downhill': 0x2C, 'winch': 0x44, 'friction': 0x54,
    'gravity_x': 0x60, 'gravity_y': 0x64, 'gravity_z': 0x68, 'force_multiplier': 0x6C,
    'initial_sickness': 0x320, 'initial_excitement': 0x324,
    'gforce_sick': 0x32C, 'upside_sick': 0x330, 'speed': 0x334, 'gforce_excite': 0x338, 'bank_excite': 0x340,
    'xg_uncomf': 0x344, 'inverted_uncomf': 0x348, 'normal_start': 0x34C, 'terror_start': 0x350, 'normal_sustain': 0x354, 'terror_sustain': 0x358,
}


class RatingOracle:
    def __init__(self, oracle: CoasterOracle | None = None):
        self.oracle = oracle or CoasterOracle()

    def rate(self, definition: dict, samples: list[dict], spacing: float, cap: float, end_coordinate: float, tail_sample: int, trough=False, suspended=False,
             max_trains: int = 0, release_rate: int = 0, trains: bool = False, nodes: int = 0):
        """Runs 0x43C450 on a controller with `samples`; each sample is a dict with position, yaw, pitch, bank, flags. Returns the controller's results, every sample's outputs and the
        nodes' flags (the winch mark 0x800 each sample's section node receives, `node` index per sample); with `trains` the pass also reaches its train count and the placing call."""
        oracle = self.oracle
        sb = oracle.prepare()
        count = len(samples)
        type_record = sb.alloc(0x400)
        flags = (0x100 if trough else 0) | (0x8 if suspended else 0)
        sb.write(type_record, 'I', flags)
        for name, offset in DEF_FIELDS.items():
            sb.write(type_record + offset, 'f', definition[name])
        sb.write(type_record + 0x2E8, 'I', max_trains)
        sb.write(type_record + 0x2EC, 'I', release_rate)
        controller = sb.alloc(0x400)
        sb.write(controller, 'I', 0x1000 if trains else 0)
        sb.write(controller + 4, 'I', type_record)
        sb.write(controller + 0x34, 'f', cap)
        tail = sb.alloc(0x200)
        sb.write(tail + 0xFC, 'I', tail_sample)
        sb.write(controller + 0x58, 'I', tail)
        array = sb.alloc(0x38 * count + 0x40)
        node_addresses = [sb.alloc(0x200) for _ in range(max(nodes, 1))]
        knots = []
        for address in node_addresses:
            knot = sb.alloc(0x40)
            sb.write(knot + 0x10, 'I', address)
            knots.append(knot)
        for index, sample in enumerate(samples):
            base = array + 0x38 * index
            sb.write(base, 'I', sample.get('flags', 0))
            for k in range(3):
                sb.write(base + 4 + 4 * k, 'f', sample['position'][k])
            sb.write(base + 0x10, 'f', sample['yaw'])
            sb.write(base + 0x14, 'f', sample['pitch'])
            sb.write(base + 0x18, 'f', sample['bank'])
            sb.write(base + 0x2C, 'I', knots[sample.get('node', 0)])
        sb.write(controller + 0x9C, 'I', array)
        sb.write(controller + 0xA4, 'I', count)
        sb.write(controller + 0xA8, 'f', end_coordinate)
        sb.write(controller + 0xAC, 'f', spacing)
        train_table = sb.alloc(0x80 * 16 + 0x80)
        sb.write(controller + 0x128, 'I', train_table)
        captured = {}

        def train_count(box):
            captured['count'] = box.arg(1)
            return 0, 0

        def train_place(box):
            captured['lap'] = from_bits(box.arg(1))
            return 0, 0

        sb.stubs[TRAIN_COUNT] = train_count
        sb.stubs[TRAIN_PLACE] = train_place
        sb.call(PASS, (controller,))
        outputs = []
        for index in range(count):
            base = array + 0x38 * index
            outputs.append({'flags': sb.read(base, 'I'), 'speed': sb.read(base + 0x1C, 'f'), 'force': tuple(sb.read(base + 0x20 + 4 * k, 'f') for k in range(3)),
                            'bank': sb.read(base + 0x18, 'f')})
        result = {name: sb.read(controller + offset, 'f') for name, offset in (('sickness', 0x100), ('excitement', 0x104), ('length', 0x10C), ('max_height', 0x110), ('max_speed', 0x114),
                                                                              ('braking_start', 0x134))}
        result['sections'] = sb.read(controller + 0x108, 'I')
        winch = [sb.read(address + 8, 'I') & 0x800 != 0 for address in node_addresses]
        return result, outputs, winch, captured

    # ------------------------------------------------------------------------------ the step and the final on their own
    STATE = {'sickness': 0x7923B4, 'peak_sickness': 0x7923B0, 'excitement': 0x7923D8, 'peak_excitement': 0x7923BC, 'previous_excitement': 0x7923C0}
    FLAGS = {'normal': 0x7923AC, 'terror': 0x7923C4}

    def step(self, definition: dict, state: dict, index: int, speed: float, bank: float, force, flags: int):
        """0x43DC50(controller, index, section flags, speed, bank, &force, lap) with the running state written into the routine's static block; the previous force and bank stay 0 (the reset
        0x43DBF0 zeroes them and nothing writes them)."""
        sb = self.oracle.prepare()
        type_record = sb.alloc(0x400)
        for name, offset in DEF_FIELDS.items():
            sb.write(type_record + offset, 'f', definition[name])
        controller = sb.alloc(0x400)
        sb.write(controller + 4, 'I', type_record)
        array = sb.alloc(0x38 * (index + 2))
        sb.write(array + 0x38 * index, 'I', flags)
        force_address = sb.alloc(16)
        for k in range(3):
            sb.write(force_address + 4 * k, 'f', force[k])
        sb.write(controller + 0x9C, 'I', array)
        for name, address in self.STATE.items():
            sb.write(address, 'f', state[name])
        for name, address in self.FLAGS.items():
            sb.write(address, 'I', state[name])
        sb.call(0x43DC50, (controller, index, 0, bits(speed), bits(bank), force_address, 0))
        out = {name: sb.read(address, 'f') for name, address in self.STATE.items()}
        out.update({name: sb.read(address, 'I') for name, address in self.FLAGS.items()})
        out['flags'] = sb.read(array + 0x38 * index, 'I')
        return out

    def final(self, state: dict, max_height: float, max_speed: float, spacing: float, end_coordinate: float, section_flags):
        """0x43DF90(controller, max height, max speed, lap) on a controller whose +0x54 list holds `section_flags` (flags at +8, next at +0x64)."""
        sb = self.oracle.prepare()
        controller = sb.alloc(0x400)
        sb.write(controller + 0xA8, 'f', end_coordinate)
        sb.write(controller + 0xAC, 'f', spacing)
        previous = 0
        addresses = []
        for flags in section_flags:
            address = sb.alloc(0x100)
            sb.write(address + 8, 'I', flags)
            addresses.append(address)
        for first, second in zip(addresses, addresses[1:]):
            sb.write(first + 0x64, 'I', second)
        if addresses:
            sb.write(controller + 0x54, 'I', addresses[0])
        for name, address in self.STATE.items():
            sb.write(address, 'f', state[name])
        sb.call(0x43DF90, (controller, bits(max_height), bits(max_speed), 0))
        out = {name: sb.read(controller + offset, 'f') for name, offset in (('sickness', 0x100), ('excitement', 0x104), ('length', 0x10C), ('max_height', 0x110), ('max_speed', 0x114))}
        out['sections'] = sb.read(controller + 0x108, 'I')
        return out
