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
             max_trains: int = 0, release_rate: int = 0, trains: bool = False, nodes: int = 0, train_spec: dict | None = None):
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
        car_bases = []
        if train_spec is not None:
            # trains for the placement after the pass: every train has the same cars; the type records hold the extents (+0x24 front, +0x28 rear)
            cars = train_spec['cars']
            types = {}
            for t_index in range(max(train_spec['count'], max_trains)):
                base = train_table + 0x80 * t_index
                car_base = sb.alloc(0x60 * len(cars) + 0x60)
                car_bases.append(car_base)
                sb.write(base + 8, 'I', 0)
                sb.write(base + 0xC, 'I', len(cars))
                sb.write(base + 0x28, 'I', car_base)
                sb.write(base + 0x2C, 'I', train_spec['reference'])
                for c_index, car in enumerate(cars):
                    key = (car['front'], car['rear'])
                    if key not in types:
                        types[key] = sb.alloc(0x80)
                        sb.write(types[key] + 0x24, 'f', car['front'])
                        sb.write(types[key] + 0x28, 'f', car['rear'])
                    cbase = car_base + 0x60 * c_index
                    sb.write(cbase + 4, 'I', types[key])
                    sb.write(cbase + 0x38, 'f', car['offset'])
                    if t_index == 0 and c_index == len(cars) - 1:
                        sb.write(cbase + 0x40, 'f', train_spec['lead'])

        def train_count(box):
            captured['count'] = box.arg(1)
            if train_spec is not None:
                box.write(controller + 0xF4, 'I', train_spec['count'])
            return 0, 0

        def train_place(box):
            captured['lap'] = from_bits(box.arg(1))
            return 0, 0

        sb.stubs[TRAIN_COUNT] = train_count
        if train_spec is None:
            sb.stubs[TRAIN_PLACE] = train_place
        else:
            sb.stubs.pop(TRAIN_PLACE, None)
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
        self.last_trains = []
        if train_spec is not None:
            for t_index in range(train_spec['count']):
                base = train_table + 0x80 * t_index
                self.last_trains.append({'coordinate': sb.read(base + 0x30, 'f'), 'position': sb.read(base + 0x74, 'f'), 'speed': sb.read(base + 0x78, 'f'),
                                         'front': sb.read(base + 0x20, 'f'), 'rear': sb.read(base + 0x24, 'f'),
                                         'cars': [sb.read(car_bases[t_index] + 0x60 * c + 0x40, 'f') for c in range(len(train_spec['cars']))]})
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

    # ------------------------------------------------------------------------------ the ride motion tick
    TICK = 0x43CE20
    TIMER = 0x4709E0
    TICK_STUBS = (0x474050, 0x438800, 0x439780, 0x439840, 0x43D8F0, 0x43C0A0)

    def tick(self, definition: dict, controller_fields: dict, samples: list[dict], trains: list[dict], steps_ms: list[int], spacing: float, total: float, tick_stub_counts=None, watch=None):
        """Runs the motion tick 0x43CE20 once per entry of `steps_ms` (the timer 0x4709E0 returns the running sum, the first call sets the controller's clock) on a synthetic
        controller: `samples` carry position, speed, friction flag and `winch` (the flag 0x800 of the sample's section node); each train has flags, position, speed and its cars (type extents,
        offset, flags). The presentation callees (clip speed, bend, sound, crash, car matrix, curve point) are stubbed and only counted. Returns the trains' state after every tick."""
        oracle = self.oracle
        sb = oracle.prepare()
        count = len(samples)
        type_record = sb.alloc(0x400)
        for name, offset in DEF_FIELDS.items():
            sb.write(type_record + offset, 'f', definition[name])
        sb.write(type_record + 0x48, 'f', definition['min_speed'])
        sb.write(type_record + 0x2EC, 'I', definition.get('release_rate', 15000))
        controller = sb.alloc(0x400)
        sb.write(type_record, 'I', 0)
        sb.write(controller + 4, 'I', type_record)
        sb.write(controller, 'I', 0)
        for name, (offset, fmt) in {'power': (0x2C, 'f'), 'cap': (0x34, 'f'), 'braking_start': (0x134, 'f'), 'platform': (0xFC, 'f')}.items():
            sb.write(controller + offset, fmt, controller_fields[name])
        sb.write(controller + 0xA4, 'I', count)
        sb.write(controller + 0xA8, 'f', total)
        sb.write(controller + 0xAC, 'f', spacing)
        sb.write(controller + 0xF4, 'I', len(trains))
        array = sb.alloc(0x38 * (count + 2) + 0x40)
        for index, sample in enumerate(samples + [samples[-1]]):
            base = array + 0x38 * index
            sb.write(base, 'I', 8 if sample.get('friction') else 0)
            for k in range(3):
                sb.write(base + 4 + 4 * k, 'f', sample['position'][k])
            sb.write(base + 0x1C, 'f', sample['speed'])
            node = sb.alloc(0x100)
            sb.write(node + 8, 'I', 0x800 if sample.get('winch') else 0)
            knot = sb.alloc(0x40)
            sb.write(knot + 0x10, 'I', node)
            sb.write(base + 0x2C, 'I', knot)
        sb.write(controller + 0x9C, 'I', array)
        train_base = sb.alloc(0x80 * len(trains) + 0x80)
        sb.write(controller + 0x128, 'I', train_base)
        sb.write(controller + 0x12C, 'I', sb.alloc(4 * len(trains) + 16))
        car_types = {}
        car_bases = []
        train_type = sb.alloc(0x40)
        sb.write(train_type + 4, 'I', 4)   # the train definition's car count: the sound pass divides by it
        for t_index, train in enumerate(trains):
            base = train_base + 0x80 * t_index
            cars = train['cars']
            car_base = sb.alloc(0x60 * len(cars) + 0x60)
            car_bases.append(car_base)
            sb.write(base, 'I', train['flags'])
            sb.write(base + 4, 'I', train_type)
            sb.write(base + 8, 'I', 0)
            sb.write(base + 0xC, 'I', len(cars))
            sb.write(base + 0x28, 'I', car_base)
            sb.write(base + 0x2C, 'I', train['reference'])
            sb.write(base + 0x74, 'f', train['position'])
            sb.write(base + 0x78, 'f', train['speed'])
            sb.write(base + 0x20, 'f', train.get('front', 0.0))
            sb.write(base + 0x24, 'f', train.get('rear', 0.0))
            for c_index, car in enumerate(cars):
                key = (car['front'], car['rear'])
                if key not in car_types:
                    address = sb.alloc(0x80)
                    sb.write(address + 0x24, 'f', car['front'])
                    sb.write(address + 0x28, 'f', car['rear'])
                    car_types[key] = address
                cbase = car_base + 0x60 * c_index
                sb.write(cbase, 'I', car.get('flags', 1))
                sb.write(cbase + 4, 'I', car_types[key])
                sb.write(cbase + 0x38, 'f', car['offset'])
                for k in range(3):
                    sb.write(cbase + 0x48 + 4 * k, 'f', car.get('start', (0.0, 0.0, 0.0))[k])
        if watch is not None:
            import unicorn as unicorn_module
            from unicorn import x86_const as x86_const_module

            def on_write(uc, access, address, size, value, user):
                for t_index2, base2 in enumerate(car_bases):
                    if base2 <= address < base2 + 0x60 * len(trains[t_index2]['cars']):
                        watch(uc.reg_read(x86_const_module.UC_X86_REG_EIP), t_index2, (address - base2) // 0x60, (address - base2) % 0x60, size, value)
            sb.uc.hook_add(unicorn_module.UC_HOOK_MEM_WRITE, on_write)
        clock = [0]
        sb.stubs[self.TIMER] = lambda box: (clock[0], 0)
        counts = {address: 0 for address in self.TICK_STUBS}

        def counter(address):
            def stub(box):
                counts[address] += 1
                return 0, 0
            return stub

        for address in self.TICK_STUBS:
            sb.stubs[address] = counter(address)
        # the curve point 0x43C0A0 fills a vector the car's pose reads; stub it to write the car coordinate's sample point (the pose itself is not compared)
        results = []
        total_ms = 0
        for step in steps_ms:
            total_ms += step
            clock[0] = total_ms
            if not results:
                clock[0] = 0
            sb.call(self.TICK, (controller,))
            state = []
            for t_index, train in enumerate(trains):
                base = train_base + 0x80 * t_index
                car_state = []
                for c_index in range(len(train['cars'])):
                    cbase = car_bases[t_index] + 0x60 * c_index
                    car_state.append({'flags': sb.read(cbase, 'I'), 'coordinate': sb.read(cbase + 0x40, 'f'),
                                      'start': tuple(sb.read(cbase + 0x48 + 4 * k, 'f') for k in range(3)), 'half': tuple(sb.read(cbase + 0x54 + 4 * k, 'f') for k in range(3))})
                state.append({'flags': sb.read(base, 'I'), 'position': sb.read(base + 0x74, 'f'), 'speed': sb.read(base + 0x78, 'f'), 'budget': sb.read(base + 0x7C, 'f'),
                              'front': sb.read(base + 0x20, 'f'), 'rear': sb.read(base + 0x24, 'f'), 'cars': car_state})
            results.append({'trains': state, 'lead': (sb.read(controller + 0x124, 'I') - train_base) // 0x80 if sb.read(controller + 0x124, 'I') else -1})
        if tick_stub_counts is not None:
            tick_stub_counts.update(counts)
        return results
