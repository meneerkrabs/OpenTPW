"""Pinned native arithmetic/layout witnesses for the controller math helpers."""
import argparse
import json
from pathlib import Path

import contracts
import evidence


def conditional(container, address, condition, expected_target):
    word = evidence.pef._u32(container.code.data, address)
    evidence.require(word >> 26, 16, "conditional branch operation")
    evidence.require((word >> 21 & 31, word >> 16 & 31), condition, "branch condition")
    displacement = word & 0xfffc
    if displacement & 0x8000:
        displacement -= 0x10000
    target = displacement if word & 2 else address + displacement
    evidence.require(target, expected_target, "conditional branch target")
    return target


def inspect(path):
    app = evidence.identified(path)
    contract = contracts.inspect(path)
    checks = []
    def d(address, operation, expected):
        evidence.require(evidence.d_fields(app, address, operation), expected, f"controller field at code:{address:#x}")
        checks.append(address)
    def x(address, expected):
        evidence.x_fields(app, address, expected)
        checks.append(address)

    # Zero-divisor branches actually target the zero arms, not merely nearby constants.
    conditional(app, 0xb0c94, (12, 2), 0xb0ca8)
    conditional(app, 0xb0d14, (12, 2), 0xb0d30)
    x(0xb0c9c, (31, 0, 0, 3, 491))
    x(0xb0d1c, (31, 0, 4, 3, 491))
    # Coaster passenger rings have separate read and write cursors and held counts.
    for address, fields in ((0x3dbcc, (6, 3, 176)), (0x3dbd0, (5, 3, 188)),
                            (0x3dbd4, (3, 3, 192)),
                            (0x3dbf4, (5, 6, 4)), (0x3dc24, (3, 6, 12)),
                            (0x3dd68, (0, 3, 220)), (0x3dd7c, (0, 7, 28)),
                            (0x3dd9c, (0, 7, 12)), (0x3dda8, (6, 7, 4))):
        if address == 0x3dbcc:
            d(address, 14, fields)
        else:
            d(address, 32, fields)
    d(0x3dbfc, 32, (3, 6, 0))
    x(0x3dc0c, (31, 0, 4, 3, 459))
    d(0x3dc20, 36, (0, 6, 24))
    d(0x3dde4, 36, (0, 7, 12))
    # Capacity is clamped by a global, definition+792, and first train definition+8.
    d(0x3df6c, 32, (0, 5, 792))
    d(0x3df94, 32, (5, 7, 296))
    d(0x3dfa0, 32, (0, 5, 8))
    # Train records128 bytes, car records96 bytes; capacity per eligible car+68.
    d(0x3eb3c, 32, (0, 3, 12))
    d(0x3eb50, 32, (0, 3, 20))
    x(0x3eb8c, (31, 0, 8, 0, 459))
    d(0x3eb90, 36, (0, 6, 68))
    d(0x3eba0, 14, (6, 6, 96))
    d(0x3ebb4, 14, (30, 30, 128))
    # Centered car spacing accumulates and normalizes with single-precision ops.
    d(0x3ebf4, 32, (5, 4, 40))
    d(0x3ec40, 48, (0, 10, 28))
    d(0x3ec58, 48, (0, 10, 36))
    d(0x3ec60, 48, (0, 10, 32))
    x(0x3ec64, (59, 1, 1, 0, 21))
    d(0x3ec6c, 52, (1, 8, 56))
    d(0x3ec70, 48, (0, 3, 172))
    x(0x3ec74, (59, 0, 1, 0, 18))
    d(0x3ec78, 52, (0, 8, 60))
    x(0x3ecc8, (59, 1, 1, 0, 20))
    x(0x3ecdc, (59, 0, 1, 0, 18))
    # Tour allocations = header92 + 20 records*228. Guest state/counts are signed.
    d(0x63bc0, 14, (3, 0, 4652))
    d(0x634cc, 14, (0, 0, 20))
    d(0x634e4, 14, (31, 4, 92))
    d(0x66acc, 7, (5, 5, 228))
    d(0x66aec, 36, (29, 3, 236))
    d(0x66b00, 36, (0, 4, 232))
    d(0x66b0c, 32, (5, 3, 312))
    x(0x66b2c, (31, 0, 3, 5, 491))
    d(0x66b3c, 14, (5, 3, 1))
    conditional(app, 0x66b18, (12, 2), 0x66b7c)
    d(0x66b8c, 36, (0, 5, 72))
    d(0x6687c, 36, (0, 4, 232))
    d(0x6688c, 36, (0, 4, 72))
    # Tour input heading conversion through signed reciprocal /360, then complement.
    d(0xb6018, 32, (0, 4, 16))
    d(0xb6020, 15, (3, 0, -18933))
    d(0xb6030, 14, (3, 3, 24759))
    x(0xb6034, (31, 4, 3, 0, 75))
    x(0xb604c, (31, 0, 4, 0, 266))
    x(0xb6058, (31, 0, 0, 8, 824))
    d(0x63bf0, 8, (3, 26, 1024))
    # BUMP passenger links have20-byte stride; distinct pending and departure heads.
    d(0x2525c, 32, (6, 8, 196))
    d(0x25264, 36, (7, 8, 196))
    d(0x2530c, 32, (4, 7, 200))
    d(0x2531c, 36, (0, 7, 200))
    d(0x240b4, 32, (5, 7, 196))
    d(0x240c4, 36, (5, 30, 48))
    d(0x240c8, 36, (0, 7, 196))
    d(0x25518, 36, (28, 3, 200))
    d(0x25528, 36, (0, 4, 96))
    d(0x2430c, 7, (0, 0, 208))
    d(0x25b4c, 14, (5, 0, 172))
    d(0x253c0, 32, (5, 7, 52))
    return {"schema": 1, "sha256": evidence.SHA, "scope": "static layout/arithmetic witnesses; conditional schema, no original execution",
            "checked_instruction_count": len(checks), "controller_contract_count": len(contract["commands"]),
            "coast": {"train_stride": 128, "car_stride": 96, "capacity_per_car_field": 68,
                      "car_offset_field": 56, "normalized_offset_field": 60, "track_length_field": 172},
            "tour": {"allocation_bytes": 4652, "header_bytes": 92, "record_count": 20, "record_stride": 228,
                     "state_field_in_record": 128, "signed_occupancy_field": 140, "passenger_array_field": 144,
                     "passenger_array_observed_space": 48, "grouping_field": 220},
            "bump": {"vehicle_stride": 172, "controller_stride": 208, "passenger_link_minimum_bytes": 20,
                     "pending_board_head": 196, "departure_head": 200, "vehicle_passenger_head": 48},
            "zero_division_boundary": "RSE opcodes49/50 only; controller/geometric division invalid inputs remain unqualified"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.app), sort_keys=True, indent=2))
    except (OSError, evidence.pef.PEFError) as error:
        parser.exit(1, f"controller native: {error}\n")
