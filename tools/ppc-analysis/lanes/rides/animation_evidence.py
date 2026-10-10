"""Pin the animation helper's scalar facts; emit metadata, never instruction bytes."""
import argparse
import json
from pathlib import Path
import struct

import evidence
from controller_native import conditional


def inspect(path):
    app = evidence.identified(path)
    evidence.inspect(path)  # Already pins category letters, suffix formats, channel0 and ID query fields.
    checked = []
    def d(address, operation, expected):
        evidence.require(evidence.d_fields(app, address, operation), expected, f"animation field at code:{address:#x}")
        checked.append(address)
    def fp(address, expected):
        word = evidence.pef._u32(app.code.data, address)
        actual = (word >> 26, word >> 21 & 31, word >> 16 & 31,
                  word >> 11 & 31, word >> 6 & 31, word >> 1 & 31)
        evidence.require(actual, expected, f"animation FP operation at code:{address:#x}")
        checked.append(address)
    def mask(address, expected):
        word = evidence.pef._u32(app.code.data, address)
        evidence.require(word >> 26, 21, "rotate/mask operation")
        evidence.require((word >> 21 & 31, word >> 16 & 31, word >> 11 & 31,
                          word >> 6 & 31, word >> 1 & 31), expected, "flag mask")
        checked.append(address)

    constants = {}
    for address, expected, size in ((0x51a8, 2.0 ** 52, 8), (0x5290, 2.0 ** 52 + 2.0 ** 31, 8),
                                    (0x516c, 30.0, 4), (0x5170, 1000.0, 4),
                                    (0x52b0, 100.0, 4), (0x52c0, 1000.0, 4), (0x52d0, .5, 4)):
        value = struct.unpack_from(">d" if size == 8 else ">f", app.data_section.data, address)[0]
        evidence.require(value, expected, "animation conversion constant")
        constants[hex(address)] = value
    mask(0xa7074, (4, 4, 0, 25, 25))  # Exactly flag0x40.
    d(0xa707c, 32, (4, 6, 16408))
    d(0xa7084, 32, (4, 6, 16400))
    d(0xa7098, 32, (5, 7, 16))
    d(0xa70a0, 48, (1, 7, 12))
    d(0xa70cc, 48, (0, 7, 28))
    fp(0xa70b4, (59, 0, 0, 3, 0, 20))  # fsubs: uint/double difference -> single.
    fp(0xa70b8, (59, 0, 4, 0, 0, 25))
    fp(0xa70bc, (59, 0, 0, 2, 0, 18))
    fp(0xa70c0, (59, 0, 1, 0, 0, 25))
    conditional(app, 0xa70d4, (4, 1), 0xa70dc)  # Native completion test is strictly greater, not a pose rule.
    mask(0xb0614, (3, 0, 0, 29, 29))  # Exactly query state flag4.
    fp(0xaf588, (59, 0, 0, 29, 0, 20))
    fp(0xaf58c, (59, 0, 0, 30, 0, 18))
    fp(0xaf590, (59, 31, 1, 0, 0, 21))
    fp(0xb0064, (59, 1, 1, 29, 0, 20))
    fp(0xb0068, (59, 1, 31, 0, 1, 25))
    fp(0xb006c, (59, 1, 1, 0, 0, 18))
    fp(0xafb98, (59, 0, 0, 1, 0, 20))
    fp(0xafb9c, (59, 0, 0, 31, 0, 18))
    # fctiwz has the X-form conversion opcode; source float arithmetic is already rounded.
    evidence.x_fields(app, 0xafba0, (63, 0, 0, 0, 15))
    d(0xb001c, 44, (0, 31, 228))
    d(0xb00dc, 7, (4, 5, 1000))
    evidence.x_fields(app, 0xb00e0, (31, 4, 4, 28, 491))
    # Signed adjustments are separately pinned for plain/channel trigger/wait/speed cases.
    clamps = {}
    for opcode, subtract, compare, branch, assign, store in (
        (16, 0xafb20, 0xafb2c, 0xafb30, 0xafb34, 0xafb38),
        (19, 0xafe8c, 0xafe98, 0xafe9c, 0xafea0, 0xafea4),
        (21, 0xb007c, 0xb0088, 0xb008c, 0xb0090, 0xb0094),
        (23, 0xb01d8, 0xb01e4, 0xb01e8, 0xb01ec, 0xb01f0)):
        d(subtract, 14, (0, 3, -300))
        d(compare, 11, (0, 0, 300))
        conditional(app, branch, (4, 0), assign + 8)
        d(assign, 14, (0, 0, 300))
        d(store, 36, (0, 31, 72))
        clamps[str(opcode)] = {"subtract": subtract, "signed_compare": compare, "minimum": 300}
    # WAITANIM differs: unsigned int-to-double conversion and unsigned post-division comparison.
    d(0xafc88, 50, (1, 2, -11640))
    evidence.require(struct.unpack_from(">d", app.data_section.data, 0x5288)[0], 2.0 ** 52,
                     "WAITANIM unsigned conversion constant")
    d(0xafcb4, 10, (0, 0, 300))
    # Unnumbered loader retry is gated by having no numbered variants yet.
    d(0x59050, 32, (0, 31, 4))
    d(0x59054, 10, (0, 0, 0))
    conditional(app, 0x59058, (4, 2), 0x59064)
    return {"schema": 1, "sha256": evidence.SHA, "scope": "static Mac animation scalar paths; no playback state machine",
            "checked_instruction_count": len(checked), "constants": constants,
            "signed_trigger_clamps": clamps, "waitanim_supported": False,
            "waitanim_reason": "unsigned conversion and unsigned clamp after speed division need separate boundary qualification",
            "frame_operation_precision": "single conversion, multiply30 single, divide1000 single, speed multiply single",
            "end_threshold": "native all-channel completion counts strictly greater; final pose/loop/mixing unqualified"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.app), indent=2, sort_keys=True))
    except (OSError, evidence.pef.PEFError) as error:
        parser.exit(1, f"animation evidence: {error}\n")
