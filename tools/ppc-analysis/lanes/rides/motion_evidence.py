"""Identity-pinned coaster schema, tick and boarding witnesses; metadata only."""
import argparse
import hashlib
import json
from pathlib import Path
import struct

import evidence
from controller_native import conditional


def scalar_offsets(records):
    """Qualified constructor0x16f4c word accounting, excluding nested arrays."""
    word, array_fields, array_count = 1, 0, 0
    result = {}
    for index, (kind, name, count) in enumerate(records):
        if kind == 12:
            if array_fields:
                raise evidence.pef.PEFError("unfinished descriptor array")
            return result
        if kind == 2:
            if array_fields:
                raise evidence.pef.PEFError("nested descriptor array unsupported")
            end = index + 1
            while end < len(records) and records[end][0] != 3:
                if records[end][0] <= 2:
                    raise evidence.pef.PEFError("non-scalar descriptor array unsupported")
                end += 1
            if end == len(records):
                raise evidence.pef.PEFError("missing descriptor array end")
            array_fields, array_count = end - index - 1, records[end][2]
            if array_fields <= 0 or array_count <= 0:
                raise evidence.pef.PEFError("invalid descriptor array shape")
        elif kind == 3:
            if not array_fields:
                raise evidence.pef.PEFError("orphan descriptor array end")
            word += (array_count - 1) * array_fields + 1
            array_fields = array_count = 0
        elif kind not in (0, 1):
            if not 4 <= kind <= 10:
                raise evidence.pef.PEFError("unsupported descriptor kind")
            result[index] = (name, 8 + word * 4)
            word += 1
    raise evidence.pef.PEFError("missing descriptor table end")


def inspect(path):
    app = evidence.identified(path)
    checked = set()
    def d(address, operation, fields):
        evidence.require(evidence.d_fields(app, address, operation), fields, f"motion field at code:{address:#x}")
        checked.add(address)
    def call(address, target):
        evidence.require(evidence.call_target(app, address), target, f"motion call at code:{address:#x}")
        checked.add(address)
    def fp(address, fields):
        word = evidence.pef._u32(app.code.data, address)
        evidence.require((word >> 26, word >> 21 & 31, word >> 16 & 31,
                          word >> 11 & 31, word >> 6 & 31, word >> 1 & 31), fields,
                         f"motion FP fields at code:{address:#x}")
        checked.add(address)
    def mask(address, fields):
        word = evidence.pef._u32(app.code.data, address)
        evidence.require(word >> 26, 21, "motion mask operation")
        evidence.require((word >> 21 & 31, word >> 16 & 31, word >> 11 & 31,
                          word >> 6 & 31, word >> 1 & 31), fields, "motion flag mask")
        checked.add(address)

    # Real application callback -> manager -> motion tick and geometry preparation.
    call(0x1c262c, 0x3864c)
    call(0x38710, 0x40650)
    call(0x44ab4, 0x3fc18)
    d(0x386e4, 32, (26, 26, 24))
    d(0x38730, 32, (26, 27, 296))
    d(0x38744, 32, (28, 27, 244))
    mask(0x38704, (0, 0, 0, 28, 30))
    # The motion clock is the cached scaled animation word, selected by literal0.
    d(0x406bc, 14, (3, 0, 0))
    call(0x406c4, 0xa3e08)
    conditional(app, 0xa3e10, (12, 2), 0xa3e1c)
    d(0xa3e14, 32, (3, 3, 16408))
    d(0xa3e1c, 32, (3, 3, 16400))
    d(0x40724, 32, (3, 28, 288))
    d(0x4072c, 50, (2, 2, -13472))
    d(0x40738, 48, (0, 2, -13496))
    fp(0x40758, (59, 1, 1, 2, 0, 20))
    fp(0x4075c, (59, 22, 1, 0, 0, 18))
    d(0x40764, 52, (22, 23, 124))
    evidence.require(struct.unpack_from(">d", app.data_section.data, 0x4b60)[0], 2.0 ** 52, "unsigned timestamp conversion")
    evidence.require(struct.unpack_from(">f", app.data_section.data, 0x4b48)[0], 1000.0, "motion milliseconds divisor")

    # Descriptor lookup and settings-base callbacks are reached through this vtable.
    evidence.require(evidence.pointer(app, 0xc70, 1), 0x3d08c, "settings vtable")
    for slot, descriptor, target in ((0x3d094, 0x6348, 0x3ab54), (0x3d098, 0x6350, 0x3ab64)):
        evidence.require(evidence.pointer(app, slot, 1), descriptor, "settings callback descriptor")
        evidence.require(evidence.pointer(app, descriptor, 0), target, "settings callback code")
    d(0x3ab54, 7, (0, 4, 60))
    d(0x3ab58, 32, (3, 2, -29720))
    evidence.require(evidence.pointer(app, 0xbe8, 1), 0x2ef24, "settings descriptor table")
    d(0x3ab64, 14, (3, 3, 8))
    call(0x302cc, 0x16f4c)
    d(0x16f90, 14, (27, 0, 1))
    d(0x16fc4, 11, (0, 0, 3))
    d(0x16fd0, 11, (0, 0, 1))
    conditional(app, 0x16fd4, (12, 2), 0x17024)
    conditional(app, 0x16fd8, (4, 0), 0x1704c)
    conditional(app, 0x16fc8, (12, 2), 0x17124)
    d(0x17074, 11, (0, 0, 3))
    d(0x16fb4, 32, (12, 12, 8))
    d(0x170c4, 32, (12, 12, 8))
    d(0x170d0, 32, (25, 3, 52))
    d(0x170dc, 32, (12, 12, 12))
    d(0x17128, 14, (0, 25, -1))
    evidence.x_fields(app, 0x17138, (31, 6, 0, 26, 235)); checked.add(0x17138)
    d(0x1715c, 14, (27, 27, 1))
    d(0x171b4, 32, (12, 12, 12))
    d(0x171d8, 14, (27, 27, 1))
    d(0x17194, 14, (27, 27, 1))
    d(0x171ec, 14, (29, 29, 12))
    records = [(evidence.pef._u32(app.data_section.data, 0x2ef24 + i * 60),
                evidence.pef._cstr(app.data_section.data, 0x2ef28 + i * 60),
                evidence.pef._u32(app.data_section.data, 0x2ef24 + i * 60 + 52)) for i in range(384)]
    offsets = scalar_offsets(records)
    mapping = []
    for index, name, source, destination, load, store in (
        (340, "fAccelerationPerHeight", 15064, 36, 0x2ff74, 0x2ff84),
        (341, "fUphillAccelModifier", 15068, 40, 0x2ff88, 0x2ff98),
        (342, "fDownhillAccelModifier", 15072, 44, 0x2ff9c, 0x2ffac),
        (353, "fWinchSpeed", 15116, 68, 0x2ffb8, 0x2ffc8),
        (354, "fMinSpeed", 15120, 72, 0x2ffcc, 0x2ffdc),
        (355, "fMaxSpeedAtMinSetting", 15124, 76, 0x2ffe0, 0x2fff0),
        (356, "fMaxSpeedAtMaxSetting", 15128, 80, 0x2fff4, 0x30004),
        (357, "fFrictionMultiplier", 15132, 84, 0x301b4, 0x301c4),
        (360, "fGravityX", 15144, 96, 0x30008, 0x30018),
        (361, "fGravityY", 15148, 100, 0x3001c, 0x3002c),
        (362, "fGravityZ", 15152, 104, 0x30030, 0x30040),
        (363, "fForceMultiplier", 15156, 108, 0x30044, 0x30054)):
        evidence.require(offsets[index], (name, source), "computed SAM settings field")
        d(load, 48, (1, 31, source)); d(store, 52, (1, 30, destination))
        conditional(app, store - 4, (12, 2), store + 4)
        mapping.append({"name": name, "descriptor_index": index, "settings_field": source,
                        "definition_field": destination, "copy_load": load, "copy_store": store})

    # One sampled position has56-byte stride; XYZ offsets4/8/12.
    d(0x40994, 32, (5, 28, 156))
    d(0x4099c, 7, (0, 0, 56)); d(0x409a0, 7, (3, 3, 56))
    fp(0x40980, (59, 3, 25, 2, 0, 20))
    for a, component, out in ((0x409a8, 4, 84), (0x409c0, 8, 88), (0x409d4, 12, 92)):
        d(a, 48, (0, 4, component))
        # X loads current after multiplication; Y/Z load before multiplication.
        current = a + (12 if component == 4 else 4)
        multiply = a + 8
        fused = a + (16 if component == 4 else 12)
        d(current, 48, (1, 3, component))
        fp(multiply, (59, 0, 0, 0, 2, 25))
        fp(fused, (59, 0, 1, 0, 3, 29))
        d(fused + 4, 52, (0, 22, out))
    d(0x40a08, 14, (0, 3, 44))
    evidence.x_fields(app, 0x40a0c, (31, 3, 4, 0, 23)); checked.add(0x40a0c)
    d(0x40a10, 32, (3, 3, 16))
    d(0x40a14, 32, (0, 3, 8)); mask(0x40a18, (0, 0, 0, 20, 20))
    for base, modifier in ((0x40a3c, 40), (0x40a74, 44), (0x40ae4, 40), (0x40b1c, 44)):
        fp(base, (59, 0, 0, 1, 0, 20))
        d(base + 4, 48, (1, 30, 36)); d(base + 8, 48, (2, 30, modifier))
        fp(base + 12, (59, 0, 1, 0, 0, 25))
        fp(base + 16, (59, 0, 2, 3, 0, 29))
    conditional(app, 0x40a38, (4, 1), 0x40a74)
    d(0x406dc, 48, (0, 30, 72)); d(0x406e0, 48, (1, 30, 68))
    d(0x40c70, 48, (0, 30, 84)); fp(0x40c74, (59, 0, 2, 0, 0, 25))
    mask(0x40c64, (0, 0, 0, 28, 28))
    for a, fields in ((0x43184, (59, 4, 1, 0, 0, 20)), (0x43194, (59, 3, 3, 0, 0, 20)),
                      (0x43198, (59, 0, 4, 0, 4, 25)), (0x4319c, (59, 1, 2, 1, 0, 20)),
                      (0x431a0, (63, 0, 3, 0, 3, 29)), (0x431a4, (59, 1, 1, 0, 1, 25)),
                      (0x431a8, (63, 1, 1, 0, 0, 21)), (0x431b8, (63, 1, 0, 1, 0, 12))): fp(a, fields)
    call(0x431ac, 0x1c552c)

    # FIFO guest removal does not imply sequential car assignment.
    for a, fields in ((0x3e348, (0, 21, 188)), (0x3e354, (4, 21, 196)),
                      (0x3e358, (5, 21, 180)), (0x3e360, (3, 21, 176)),
                      (0x3e368, (28, 4, 0)), (0x3e3a8, (0, 29, 0)),
                      (0x3e3ec, (6, 25, 52)), (0x3e3f0, (3, 25, 68)),
                      (0x3e404, (0, 25, 12)), (0x3e408, (4, 4, 44))): d(a, 32, fields)
    d(0x3e388, 36, (0, 21, 196)); d(0x3e394, 36, (0, 21, 188))
    d(0x3e304, 15, (5, 0, 3)); d(0x3e320, 14, (31, 5, 17405))
    evidence.x_fields(app, 0x3e3ac, (31, 3, 0, 31, 235)); checked.add(0x3e3ac)
    d(0x3e3b0, 15, (3, 3, 39)); d(0x3e3b4, 14, (0, 3, -24893))
    d(0x3e3bc, 36, (0, 29, 0))
    evidence.x_fields(app, 0x3e3c0, (31, 3, 4, 24, 459)); checked.add(0x3e3c0)
    d(0x3e3d8, 7, (3, 3, 96)); d(0x3e450, 14, (25, 25, 96))
    d(0x3e448, 36, (0, 25, 52)); d(0x3e46c, 36, (0, 22, 20))
    # Raw TOUR14 with resolved zero begins reverse unloading, except state52==3.
    d(0x66de4, 11, (0, 4, 0)); conditional(app, 0x66de8, (12, 2), 0x66e00)
    d(0x66df8, 36, (0, 5, 56)); d(0x66e00, 32, (0, 5, 52))
    d(0x66e04, 11, (0, 0, 3)); d(0x66e18, 36, (3, 5, 0))
    d(0x66e38, 32, (0, 4, 220)); d(0x66e3c, 11, (0, 0, 1))
    conditional(app, 0x66e40, (4, 2), 0x66e58)
    d(0x66e44, 32, (0, 4, 232)); d(0x66e48, 11, (0, 0, 0))
    conditional(app, 0x66e4c, (4, 1), 0x66e58)
    evidence.x_fields(app, 0x66e50, (31, 0, 0, 0, 104)); checked.add(0x66e50)
    d(0x66e54, 36, (0, 4, 232)); d(0x66e58, 36, (3, 5, 56))
    # Train sound output identity: call is outside per-car96-byte loop.
    d(0x3ee94, 14, (28, 4, 0)); d(0x3eed4, 32, (4, 28, 40))
    d(0x3eee0, 14, (4, 4, 96)); d(0x3eee8, 32, (3, 28, 12))
    d(0x3efc8, 14, (4, 28, 0)); call(0x3efd0, 0x3bb6c)
    return {"schema": 1, "sha256": evidence.SHA, "checked_instruction_count": len(checked),
            "scope": "static Mac schema-to-motion and FIFO boarding; no full motion/serialized track decoder",
            "motion": {"manager": 0x3864c, "tick": 0x40650, "path_precompute": 0x3fc18,
                       "path_stride": 56, "path_xyz": [4, 8, 12], "car_current_xyz": [72, 76, 80],
                       "car_projected_xyz": [84, 88, 92], "cached_clock": "scaled animation cache+16400",
                       "time_delta": "uint32 elapsed -> single -> /1000 single; no live clock call",
                       "section_winch_flag": 0x800, "sample_friction_flag": 8,
                       "height_target": "max(supplied_floor, fma(modifier, single(acceleration * single(currentY-projectedY)), speed))",
                       "interpolation": "single(next*fraction) then single fused current*(1-fraction)+product",
                       "distance": "single deltas; single y/z squares; double fused x*x+ySquare; double sum/sqrt -> single"},
            "settings": {"descriptor_base": 0x2ef24, "descriptor_stride": 60, "descriptor_count": 384,
                         "descriptor_sha256": hashlib.sha256(app.data_section.data[0x2ef24:0x34924]).hexdigest(),
                         "constructor": 0x16f4c, "loader": 0x2fde8, "mapping": mapping},
            "boarding": {"function": 0x3e2f8, "guest_order": "pending ring FIFO",
                         "car_order": "native random candidate remainder then wrap scan for car+52 < car+68",
                         "candidate_rng": "uint32 state*214013+2531011; upper16 remainder eligibleCarCount; RNG scope is this helper only",
                         "physical_node_source": "carDefinition+44[existing rider count]; caller uses modelhandle car+12",
                         "startup_phase": "not fully qualified; station-range and controller flags gate loading"},
            "tour_unload": {"raw_command": 14, "function": 0x66dc0, "parameter": "resolved zero",
                           "exception_gate": "controller+52==3 returns without transition",
                           "state": "controller+0=1, +56=0; entries type+220==1 negate positive occupancy+232",
                           "departure": "0x66784 increments negative occupancy toward zero then loads slot[-updatedOccupancy]"},
            "sound_output": {"initializer": 0x3bb6c, "caller": 0x3efd0, "record": "train, not car"},
            "unresolved": ["TrackInfo.Direction axis/enum", "COS load/save linkage", "type4 binding",
                           "full station/start/stop transitions", "adaptive motion integration", "seat node ownership callbacks",
                           "brake/collision/reverse train state", "original FPSCR/exceptional float behavior", "PC runtime equivalence"]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    args = parser.parse_args()
    try: print(json.dumps(inspect(args.app), sort_keys=True, indent=2))
    except (OSError, evidence.pef.PEFError) as error: parser.exit(1, f"motion evidence: {error}\n")
