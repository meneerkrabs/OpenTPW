"""Recover command contracts by exact native switch index, not guessed enum names.

Results are evidence metadata. Preconditions and descriptive controller roles
remain conditional until the surrounding host/vehicle schemas are recovered.
"""
from pathlib import Path
import argparse
import json

import evidence

# command: (parameter contract, accumulator policy, direct non-diagnostic callees)
# "resolved input" means signed16 literal or int32 variable, not arbitrary tags.
CONTRACTS = {
    "COAST": {
        1: ("resolved input", "preserve", [0x3dbc0]),
        2: ("optional variable output", "result", [0x3dd14]),
        3: ("optional variable output", "result", [0x3dd5c]),
        4: ("resolved input", "preserve", [0x3ddfc]),
        5: ("resolved input", "preserve", [0x3dec8]),
        6: ("resolved input", "preserve", [0x3df24]),
        7: ("consumed and ignored", "preserve", []),
        8: ("consumed and ignored", "preserve", [0x3dafc, 0x3de68]),
    },
    "BUMP": {
        1: ("required variable input", "result", [0x251a8]),
        2: ("required variable output", "result", [0x25294]),
        3: ("consumed and ignored", "preserve", [0x1e54c]),
        4: ("consumed and ignored", "result", [0x242d8]),
        5: ("consumed and ignored", "host field +40", []),
        6: ("consumed and ignored", "preserve", [0x1de20]),
        7: ("consumed and ignored", "preserve", [0x1dc20]),
        8: ("resolved input", "preserve", [0x1e150, 0x1e388]),
        9: ("resolved input", "preserve", [0x1e0ac, 0x1e388]),
        10: ("consumed and ignored", "preserve", [0x1dfac]),
        11: ("optional variable output", "result", [0x1e9bc]),
        12: ("consumed and ignored", "result", [0x23fe8]),
        13: ("resolved input multiplied by 30", "original input", [0x1e6f4]),
        14: ("resolved input negated", "original input", [0x1e6f4]),
        16: ("consumed and ignored", "result", [0x25638]),
        17: ("resolved input", "preserve", [0x776b0, 0x77954, 0x776b0, 0x77954]),
    },
    "TOUR": {
        1: ("resolved input", "preserve", [0x19b298, 0x63b60]),
        2: ("consumed and ignored", "preserve", [0x66550]),
        3: ("required variable input", "result", [0x66a64]),
        4: ("required variable output", "result", [0x66784]),
        5: ("resolved input", "preserve", [0x6729c]),
        8: ("resolved input multiplied by 1000", "preserve", [0x66bcc]),
        9: ("resolved input", "preserve", [0x66f58]),
        10: ("consumed and ignored", "result", [0x672e0]),
        11: ("consumed and ignored", "result", [0x67360]),
        12: ("resolved input", "preserve", [0x673d8]),
        14: ("resolved input", "preserve", [0x66dc0]),
        15: ("consumed and ignored", "result", [0x67314]),
        16: ("required variable output", "result", [0x6745c]),
        17: ("resolved input", "preserve", [0x67404]),
        18: ("resolved input", "preserve", [0x67430]),
    },
}

# Variable tag checks precede required operations, but follow optional-query
# accumulator writes. Each gate subtracts the variable tag then compares zero.
VARIABLE_GATES = {
    ("BUMP", 1): (0xb6784, 3), ("BUMP", 2): (0xb67fc, 3),
    ("TOUR", 3): (0xb60ec, 3), ("TOUR", 4): (0xb6164, 3),
    ("TOUR", 16): (0xb6624, 3), ("COAST", 2): (0xb6fc4, 4),
    ("COAST", 3): (0xb7040, 4), ("BUMP", 11): (0xb6b48, 4),
}


def inspect(path):
    base = evidence.inspect(path)
    app = evidence.identified(path)
    result = []
    for family, contracts in CONTRACTS.items():
        targets = base["controller_case_addresses"][family]
        boundaries = sorted(set(targets) | { {"TOUR": 0xb6684, "BUMP": 0xb6df4, "COAST": 0xb7238}[family] })
        script_register = {"TOUR": 31, "BUMP": 29, "COAST": 30}[family]
        for command, (parameter, accumulator, expected_calls) in contracts.items():
            start = targets[command]
            end = next(boundary for boundary in boundaries if boundary > start)
            calls = []
            accumulator_stores = []
            for address in range(start, end, 4):
                word = evidence.pef._u32(app.code.data, address)
                if word >> 26 == 18 and word & 1:
                    target = evidence.call_target(app, address)
                    if target != 0xb678:  # Native diagnostic/assert helper, not controller operation.
                        calls.append(target)
                if word >> 26 == 36:
                    _, register, displacement = evidence.d_fields(app, address, 36)
                    if register == script_register and displacement == 72:
                        accumulator_stores.append(address)
            evidence.require(calls, expected_calls, f"{family} command {command} direct calls")
            evidence.require(bool(accumulator_stores), accumulator != "preserve", f"{family} command {command} branch accumulator policy")
            gate = VARIABLE_GATES.get((family, command))
            if gate:
                address, register = gate
                evidence.require(evidence.d_fields(app, address, 15), (0, register, -16384), "variable tag subtraction")
                evidence.require(evidence.d_fields(app, address + 4, 10), (0, 0, 0), "variable tag comparison")
                branch = evidence.pef._u32(app.code.data, address + 8)
                evidence.require((branch >> 26, branch >> 21 & 31, branch >> 16 & 31), (16, 4, 2), "skip non-variable operation")
                if parameter.startswith("required"):
                    evidence.require(min(accumulator_stores) > address, True, "required variable precedes result")
                else:
                    evidence.require(max(accumulator_stores) < address, True, "optional variable follows query result")
            result.append({"family": family, "raw_command": command, "native_case": start,
                           "parameter": parameter, "accumulator": accumulator,
                           "variable_gate_address": gate[0] if gate else None,
                           "direct_controller_calls": calls, "accumulator_store_addresses": accumulator_stores})
    return {"schema": 1, "sha256": evidence.SHA, "scope": "static native bridge contracts; valid host/controller precondition",
            "commands": result, "unimplemented_or_invalid_commands": {"COAST": [0], "BUMP": [0, 15], "TOUR": [0, 6, 7, 13]},
            "coast_noop_command": 7}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.app), sort_keys=True, indent=2))
    except (OSError, evidence.pef.PEFError) as error:
        parser.exit(1, f"controller contracts: {error}\n")
