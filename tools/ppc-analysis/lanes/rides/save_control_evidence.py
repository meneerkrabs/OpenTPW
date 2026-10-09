"""Native coaster serialization control-flow boundaries; never a decoder."""
import argparse
import json
from pathlib import Path
import evidence
import save_evidence
from controller_native import conditional


def inspect(path):
    app = evidence.identified(path)
    base = save_evidence.inspect(path)
    checked = set()
    def d(address, operation, fields):
        evidence.require(evidence.d_fields(app, address, operation), fields, f"save control field at code:{address:#x}")
        checked.add(address)
    def call(address, target):
        evidence.require(evidence.call_target(app, address), target, "save control call")
        checked.add(address)
    def x(address, fields):
        evidence.x_fields(app, address, fields); checked.add(address)
    def branch(address, target):
        word = evidence.pef._u32(app.code.data, address)
        evidence.require((word >> 26, word & 1), (18, 0), "unlinked control branch")
        displacement = word & 0x03fffffc
        if displacement & 0x02000000: displacement -= 0x04000000
        evidence.require(displacement if word & 2 else address + displacement, target, "control branch target")
        checked.add(address)
    def condition(address, fields, target):
        conditional(app, address, fields, target); checked.add(address)
    def mask(address, fields):
        word = evidence.pef._u32(app.code.data, address)
        evidence.require(word >> 26, 21, "control mask operation")
        evidence.require((word >> 21 & 31, word >> 16 & 31, word >> 11 & 31,
                          word >> 6 & 31, word >> 1 & 31), fields, "control mask fields")
        checked.add(address)

    # Native+0 is a section ordinal, not a type enum. It is assigned from the
    # current edit counter and link restoration resolves it by walking next.
    d(0x36034, 14, (29, 3, 60)); d(0x3611c, 32, (3, 29, 8)); d(0x36124, 36, (3, 30, 0))
    d(0x36554, 32, (3, 29, 8)); d(0x36558, 14, (0, 3, 1)); d(0x3655c, 36, (0, 29, 8))
    d(0x39988, 32, (0, 31, 0)); d(0x3998c, 10, (0, 0, 2))
    condition(0x39990, (4, 2), 0x39a88)
    # Count/filter predicate is separate from the ordinal2 auxiliary predicate.
    mask(0x39730, (0, 0, 0, 27, 27)); condition(0x39734, (4, 2), 0x39744)
    mask(0x39a90, (0, 0, 0, 27, 27)); condition(0x39a94, (4, 2), 0x39d70)
    d(0x39d70, 32, (31, 31, 100)); condition(0x39d78, (4, 2), 0x39988)

    # Header flag2 captures endpoint pointers but BOTH arms reach the aux read.
    mask(0x3a32c, (0, 0, 0, 30, 30)); condition(0x3a330, (12, 2), 0x3a344)
    d(0x3a334, 32, (3, 28, 84)); d(0x3a338, 32, (13, 28, 88))
    d(0x3a33c, 32, (3, 3, 100)); d(0x3a340, 32, (25, 3, 100))
    d(0x3a344, 32, (5, 28, 84)); d(0x3a354, 32, (5, 5, 100)); d(0x3a35c, 32, (0, 5, 100))
    d(0x3a360, 36, (0, 1, 400)); d(0x3a368, 14, (5, 5, 176)); call(0x3a36c, 0x1c4b24)
    # Existing controller models and initial anchors are built before this read.
    call(0x3a2a4, 0x37968); call(0x3a2d4, 0x366bc)

    # The header's count drives34-byte records; separate counts drive link I/O.
    d(0x3a71c, 40, (0, 1, 350)); x(0x3a720, (31, 0, 29, 0, 32))
    condition(0x3a724, (12, 0), 0x3a430)
    d(0x3a41c, 32, (0, 3, 176)); condition(0x3a424, (12, 0), 0x3a3b0)
    d(0x3a70c, 32, (0, 3, 176)); condition(0x3a714, (12, 0), 0x3a6a0)
    x(0x3a414, (31, 4, 3, 0, 151)); x(0x3a704, (31, 4, 3, 0, 151))
    d(0x3a4f0, 10, (0, 0, 0)); condition(0x3a4f4, (12, 2), 0x3a718)

    # Link IDs become pointers only after topology exists, via ordinal traversal.
    call(0x3a8f0, 0x41dcc); d(0x3a8e8, 14, (3, 28, 60))
    x(0x3a8ec, (31, 4, 4, 29, 23)); x(0x3a900, (31, 3, 4, 29, 151))
    d(0x41dd0, 32, (3, 3, 24)); d(0x41de8, 32, (3, 3, 100)); d(0x41e18, 32, (3, 3, 100))
    # Partial load mutation occurs before error checks; busy flag is cleared later.
    d(0x3a1b8, 14, (0, 0, 1)); d(0x3a1c0, 36, (0, 26, 0))
    d(0x3a728, 14, (0, 0, 0)); d(0x3a72c, 36, (0, 26, 0))
    evidence.require(evidence.pointer(app, 0x8000 - 29628, 1), 355224, "load-in-progress global relocation")

    failures = []
    for operation, stage, io, compare, register, success_branch, success, zero, failure_branch, epilogue in (
        ("write", "ordinal2 auxiliary count", 0x399d0, 0x399e0, 0, 0x399e4, 0x399fc, 0x399f4, 0x399f8, 0x39d98),
        ("write", "ordinal2 auxiliary item", 0x39a48, 0x39a58, 0, 0x39a5c, 0x39a74, 0x39a6c, 0x39a70, 0x39d98),
        ("write", "section34", 0x39c74, 0x39cd0, 29, 0x39cd4, 0x39cec, 0x39ce4, 0x39ce8, 0x39d98),
        ("write", "section link item", 0x39d30, 0x39d40, 0, 0x39d44, 0x39d5c, 0x39d54, 0x39d58, 0x39d98),
        ("read", "controller32", 0x3a1d4, 0x3a244, 28, 0x3a248, 0x3a260, 0x3a258, 0x3a25c, 0x3aaa0),
        ("read", "initial third-node auxiliary count", 0x3a36c, 0x3a37c, 0, 0x3a380, 0x3a398, 0x3a390, 0x3a394, 0x3aaa0),
        ("read", "initial third-node auxiliary item", 0x3a3c4, 0x3a3d4, 0, 0x3a3d8, 0x3a3f0, 0x3a3e8, 0x3a3ec, 0x3aaa0),
        ("read", "section34", 0x3a444, 0x3a4a0, 30, 0x3a4a4, 0x3a4bc, 0x3a4b4, 0x3a4b8, 0x3aaa0),
        ("read", "section link item", 0x3a6b4, 0x3a6c4, 0, 0x3a6c8, 0x3a6e0, 0x3a6d8, 0x3a6dc, 0x3aaa0)):
        call(io, 0x1c4b54 if operation == "write" else 0x1c4b24)
        d(compare, 11, (0, register, 1)); condition(success_branch, (12, 2), success)
        d(zero, 14, (3, 0, 0)); branch(failure_branch, epilogue)
        failures.append({"operation": operation, "stage": stage, "io_call": io,
                         "success_test": compare, "failure_result": 0, "epilogue": epilogue})

    # Nested train helpers return a value that these wrapper callsites do not test.
    call(0x39d84, 0x38da4); d(0x39d88, 32, (27, 27, 24))
    d(0x39d8c, 10, (0, 27, 0)); condition(0x39d90, (4, 2), 0x39718)
    call(0x3aa38, 0x3912c); d(0x3aa3c, 32, (5, 28, 4)); d(0x3aa40, 14, (3, 28, 0))
    d(0x39d94, 14, (3, 0, 1)); d(0x3aa9c, 14, (3, 0, 1))
    return {"schema": 1, "sha256": evidence.SHA, "base_save_checks": base["checked_instruction_count"],
            "checked_instruction_count": len(checked), "scope": "static topology serializer control flow; no decoder/nonempty fixture",
            "ordinal2_auxiliary": {"predicate": "section+0==2, a section ordinal not type enum",
                                  "wire": "u32-le count followed by count u16-le section ordinals",
                                  "load_target": "existing controller+84->next+100->next+100, third node",
                                  "always_read_per_controller": True,
                                  "header_flag2": "captures endpoints; does not gate auxiliary I/O"},
            "filter": {"predicate": "sectionDescriptor+4 flag0x10", "count_excludes": True,
                       "write_skips": "34-byte record and ordinary links; ordinal2 auxiliary already written"},
            "record_gate": {"header_count_offset": 22, "builder_output_gate": 0x3a4f4,
                            "null_output": "increments record counter without reading that record's following link items; invalid-shape behavior unqualified"},
            "link_resolution": {"helper": 0x41dcc, "source": "serialized section ordinals",
                                "destination": "linked section pointers after topology rebuild; no ordinal bounds/null guard qualified"},
            "failure_paths": failures,
            "partial_failure": {"load_mode_global_data": 355224, "set": 0x3a1c0, "clear": 0x3a72c,
                                "qualification": "tracked early failures branch to epilogue before direct mode reset, after prior initialization/mutations; helper side effects and rollback unqualified"},
            "nested_helpers": {"save": 0x38da4, "load": 0x3912c,
                               "qualification": "wrapper callsites do not test returned r3; application/global I/O failure handling remains separate"},
            "remaining": ["valid initial-node/filter invariants", "nonempty PC save fixture", "native link/record count limits",
                          "builder null-output input domain", "nested/global I/O failure propagation", "full topology/train payload",
                          "TPI COS linkage", "full motion/geometry/runtime qualification"]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    args = parser.parse_args()
    try: print(json.dumps(inspect(args.app), sort_keys=True, indent=2))
    except (OSError, evidence.pef.PEFError) as error: parser.exit(1, f"coaster control: {error}\n")
