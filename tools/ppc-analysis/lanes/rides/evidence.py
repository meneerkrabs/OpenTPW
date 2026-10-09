"""Pinned, read-only ride/RSE instruction and relocation witnesses.

No game execution, heuristic function discovery, disassembly, or binary output.
The local original file must match its SHA before any instruction is inspected.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef
from timer_evidence import call_target, d_fields, require

SHA = "04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5"


def identified(path: Path):
    raw = path.read_bytes()
    require(hashlib.sha256(raw).hexdigest(), SHA, "SimThemePark identity")
    return pef.PEFContainer(raw, path.name)


def pointer(container, slot, section):
    target = container.relocs.get(container.data_section.index, {}).get(slot)
    if target is None:
        raise pef.PEFError(f"missing relocation at data:{slot:#x}")
    require((target.kind, target.target), ("section", section), "relocation target")
    return target.addend


def table(container, toc_slot, count, expected_base, expected_targets=None):
    base = pointer(container, toc_slot, container.data_section.index)
    require(base, expected_base, "switch table address")
    result = []
    for index in range(count):
        target = pointer(container, base + 4 * index, container.code.index)
        if not (0 <= target < len(container.code.data) and target % 4 == 0):
            raise pef.PEFError("unaligned/out-of-range switch target")
        result.append(target)
    if expected_targets is not None:
        require(result, expected_targets, "switch targets")
    return result


def x_fields(container, offset, expected):
    word = pef._u32(container.code.data, offset)
    fields = (word >> 26, word >> 21 & 31, word >> 16 & 31,
              word >> 11 & 31, word >> 1 & 1023)
    require(fields, expected, f"extended operation at code:{offset:#x}")


def inspect(path: Path):
    app = identified(path)
    checks = []

    def d(address, operation, fields):
        require(d_fields(app, address, operation), fields, f"operand fields at code:{address:#x}")
        checks.append(address)

    def call(address, target):
        require(call_target(app, address), target, f"call at code:{address:#x}")
        checks.append(address)

    d(0xaf5c0, 32, (3, 2, -20648))
    dispatch = table(app, 0x2f58, 106, 0x3f2a4)
    cases = {3: 0xaf5f0, 5: 0xaf650, 15: 0xafa68, 16: 0xafa94,
             17: 0xafbbc, 18: 0xafd00, 20: 0xaff3c, 21: 0xaffc4,
             23: 0xb013c, 27: 0xb05b8, 28: 0xb07e4, 32: 0xb08f4,
             34: 0xb0938, 35: 0xb097c, 47: 0xb0b94, 49: 0xb0c58,
             50: 0xb0cd8, 53: 0xb0e78, 54: 0xb0e84, 55: 0xb0e90}
    for opcode, address in cases.items():
        require(dispatch[opcode], address, f"opcode {opcode} case")
    names = {3: "COPY", 5: "SUB", 15: "FLUSHANIM", 16: "TRIGANIM", 17: "WAITANIM",
             18: "LOOPANIM", 20: "GETANIM", 21: "TRIGANIMSPEED", 23: "TRIGANIM_CH",
             27: "GETANIM_CH", 28: "RAND", 32: "BRANCH_Z", 34: "BRANCH_NV",
             35: "BRANCH_PV", 47: "ADD", 49: "DIV", 50: "MOD", 53: "TOUR", 54: "BUMP", 55: "COAST"}
    for opcode, name in names.items():
        name_address = pointer(app, 0x3ef20 + 8 * opcode, app.code.index)
        require(pef._cstr(app.code.data, name_address), name, "native opcode debug name")
    # The literal path uses signed 16-bit extension; the variable path loads int32.
    x_fields(app, 0xae710, (31, 4, 3, 0, 922))
    d(0xae700, 32, (3, 3, 28))
    d(0xaf638, 36, (0, 31, 72))  # COPY result is the branch accumulator.
    for address in (0xb0ca8, 0xb0d30):
        d(address, 14, (0, 0, 0))
        d(address + 4, 36, (0, 31, 72))
    # Ordinary and explicit-channel calls converge on one animator.
    d(0xafb10, 14, (8, 0, 0))
    d(0xafc68, 14, (8, 0, 0))
    d(0xafd7c, 14, (8, 0, 0))
    d(0xb01c4, 14, (8, 28, 0))
    for address in (0xafb14, 0xafc6c, 0xafd80, 0xb0070, 0xb01cc):
        call(address, 0xa6cc0)
    d(0xb001c, 44, (0, 31, 228))
    require(struct.unpack_from(">f", app.data_section.data, 0x52c0)[0], 1000.0, "speed scale")
    d(0xb0060, 50, (1, 1, 616))
    # GETANIM_CH: query current ID, not a boolean; state bit 4 maps result to -1.
    call(0xb0608, 0xa7dcc)
    d(0xa7dcc, 7, (0, 6, 56))
    d(0xa7de0, 32, (0, 3, 4))
    d(0xa7de4, 36, (0, 4, 0))
    d(0xa7df8, 32, (3, 3, 0))
    d(0xb061c, 14, (0, 0, -1))
    d(0xb0620, 36, (0, 31, 72))
    d(0xb0600, 14, (4, 31, 72))
    d(0xb0604, 14, (5, 0, 0))
    d(0xb05fc, 14, (6, 26, 0))
    # Explicit ID/suffix records, each 8 bytes. The renderer's animation codec is separate.
    letters_base = pointer(app, 0x2c04, app.data_section.index)
    require(letters_base, 0x3d300, "animation binding table")
    letters = []
    for animation, letter in enumerate("CDILSMEUWBRO"):
        offset = letters_base + animation * 8
        require(pef._u32(app.data_section.data, offset), animation, "animation ID")
        require(app.data_section.data[offset + 4], ord(letter), "animation suffix")
        letters.append({"animation": animation, "suffix": letter.lower()})
    string_base = pointer(app, 0x2c08, app.code.index)
    require(pef._cstr(app.code.data, string_base + 467), "%s%s%c%d.md2", "numbered member format")
    require(pef._cstr(app.code.data, string_base + 480), "%s%s%c.md2", "fallback member format")
    d(0x58e8c, 14, (8, 8, 1))
    d(0x5905c, 14, (29, 0, 0))
    # Three distinct controller switches, each with its own script object field.
    for opcode, callsite, target in ((53, 0xb0e7c, 0xb5eb0), (54, 0xb0e88, 0xb6684), (55, 0xb0e94, 0xb6df4)):
        call(callsite, target)
    d(0xb5f20, 10, (0, 4, 18))
    d(0xb671c, 10, (0, 4, 17))
    d(0xb6e74, 10, (0, 4, 8))
    controllers = {}
    for label, slot, count, base in (("TOUR", 0x2f38, 19, 0x3f458),
                                      ("BUMP", 0x2f34, 18, 0x3f4a4),
                                      ("COAST", 0x2f30, 9, 0x3f4ec)):
        controllers[label] = table(app, slot, count, base)
    d(0xb6288, 7, (4, 0, 1000))
    d(0xb6c28, 7, (4, 31, 30))
    call(0xb6c2c, 0x1e6f4)
    call(0xb6cb0, 0x1e6f4)
    d(0x1e778, 36, (4, 7, 4))
    # Original entrance/exit fractional offset rotations and 255 quantization.
    require(struct.unpack_from(">f", app.data_section.data, 0x54ec)[0], 1.0, "rotation complement")
    require(struct.unpack_from(">d", app.data_section.data, 0x54c0)[0], 255.0, "stand/exit quantization")
    d(0xde498, 11, (0, 0, 180))
    d(0xde4a4, 11, (0, 0, 90))
    d(0xde4bc, 11, (0, 0, 270))
    d(0xde524, 38, (3, 27, 1))
    d(0xde528, 38, (0, 28, 1))
    # Vehicle-launch guard and finish-to-loading phase are controller state, not RSE VAR_RUNNING.
    d(0x24364, 32, (3, 29, 92))
    d(0x24368, 32, (0, 29, 100))
    d(0x24374, 11, (0, 3, 64))
    d(0x2437c, 32, (0, 29, 80))
    d(0x25a30, 14, (0, 0, 1))
    d(0x25a34, 36, (0, 28, 80))
    return {"schema": 1, "sha256": SHA, "scope": "static Feral Mac evidence; PC runtime unqualified",
            "interpreter_code": 0xaf534, "checked_instruction_count": len(checks),
            "selected_opcode_cases": cases, "selected_opcode_names": names, "animation_binding": letters,
            "channel_stride": 56, "plain_channel": 0, "getanim_result": "current ID; -1 when state bit 4 is set",
            "controller_case_addresses": controllers, "entrance_offset_scale": 255,
            "known_vm_disagreements": ["COPY branch accumulator", "zero DIV/MOD result", "GETANIM_CH ID/state", "shared channel 0", "TRIGANIMSPEED per-mille speed"]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.app), indent=2, sort_keys=True))
    except (OSError, pef.PEFError) as error:
        parser.exit(1, f"ride evidence: {error}\n")
