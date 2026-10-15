"""Pinned boarding-socket producer/consumer; no model codec or game execution."""
import argparse
import json
from pathlib import Path
import evidence
from controller_native import conditional


def inspect(path):
    app = evidence.identified(path)
    checked = set()
    def d(address, operation, fields):
        evidence.require(evidence.d_fields(app, address, operation), fields, f"boarding socket at code:{address:#x}")
        checked.add(address)
    def call(address, target):
        evidence.require(evidence.call_target(app, address), target, "boarding socket call")
        checked.add(address)
    def x(address, fields):
        evidence.x_fields(app, address, fields); checked.add(address)

    # The producer receives a52-byte car definition and loads its modelhandle+12.
    d(0x303cc, 14, (29, 3, 0)); d(0x303f0, 32, (4, 29, 8))
    d(0x303fc, 36, (3, 29, 12))
    d(0x30430, 14, (4, 0, 128)); call(0x304e4, 0x19b354)
    d(0x304e8, 36, (3, 29, 48)); d(0x304ec, 32, (0, 29, 48))
    conditional(app, 0x304f4, (12, 2), 0x30540)
    d(0x30504, 36, (3, 29, 44)); d(0x30514, 14, (5, 28, 1))
    d(0x30518, 14, (4, 0, 128)); call(0x3051c, 0x19b300)
    d(0x30520, 32, (4, 29, 44)); x(0x30528, (31, 3, 4, 30, 151))
    d(0x3052c, 14, (30, 30, 4)); d(0x30540, 36, (30, 29, 44))
    # Selected car roles contribute their filtered socket counts to per-train capacity.
    call(0x30920, 0x303a8); call(0x30934, 0x303a8); call(0x30948, 0x303a8)
    d(0x30900, 14, (31, 3, 752))
    d(0x30910, 32, (29, 3, 768)); d(0x30914, 32, (30, 3, 764)); d(0x30918, 32, (28, 3, 772))
    d(0x30998, 32, (0, 30, 48)); d(0x309b8, 32, (0, 28, 48)); d(0x309cc, 32, (0, 29, 48))
    d(0x309a0, 36, (0, 31, 8)); d(0x309c0, 36, (0, 31, 8)); d(0x309d4, 36, (0, 31, 8))
    d(0x309dc, 32, (3, 31, 4))
    d(0x31290, 32, (3, 29, 760)); d(0x31294, 32, (0, 29, 744))
    x(0x31298, (31, 0, 3, 0, 235)); d(0x3129c, 36, (0, 29, 792))
    # Count: firstword & mask, records20 bytes, runtime count is uint16.
    d(0x19b354, 32, (5, 3, 8)); d(0x19b360, 32, (7, 5, 4))
    d(0x19b364, 40, (0, 7, 72)); d(0x19b374, 32, (5, 7, 124))
    x(0x19b378, (31, 0, 5, 6, 23)); x(0x19b37c, (31, 4, 0, 0, 28))
    conditional(app, 0x19b380, (12, 2), 0x19b388)
    d(0x19b384, 14, (3, 3, 1)); d(0x19b388, 14, (6, 6, 20))
    # Ordinal: increments matchcount, returns current raw attribute index, -1 if absent.
    d(0x19b314, 40, (0, 8, 72)); d(0x19b324, 32, (6, 8, 124))
    x(0x19b328, (31, 0, 6, 7, 23)); x(0x19b32c, (31, 4, 0, 0, 28))
    conditional(app, 0x19b330, (12, 2), 0x19b340)
    d(0x19b334, 14, (9, 9, 1)); x(0x19b338, (31, 0, 9, 5, 32))
    d(0x19b340, 14, (7, 7, 20)); d(0x19b344, 14, (3, 3, 1))
    d(0x19b34c, 14, (3, 0, -1))
    # Each car receives two separate passengerID arrays; no paired-seat conclusion.
    d(0x379c8, 36, (3, 21, 304)); d(0x379e4, 36, (3, 21, 328))
    d(0x37a6c, 36, (21, 24, 4)); d(0x37a80, 36, (25, 24, 32))
    d(0x37a84, 32, (3, 24, 4)); d(0x37a88, 32, (0, 3, 48))
    d(0x37a94, 36, (25, 24, 36)); d(0x37aa0, 32, (3, 3, 48))
    d(0x37aa4, 36, (0, 24, 40)); d(0x37ab4, 36, (0, 24, 44))
    d(0x37b50, 14, (24, 24, 96))
    # Boarding indexes the selected attribute array by existing logical occupancy.
    d(0x3e3ec, 32, (6, 25, 52)); d(0x3e3f0, 32, (3, 25, 68))
    d(0x3e3fc, 32, (4, 25, 4)); d(0x3e404, 32, (0, 25, 12))
    d(0x3e408, 32, (4, 4, 44)); d(0x3e418, 32, (4, 3, 0))
    d(0x3e41c, 32, (3, 6, 8)); call(0x3e420, 0x19b56c)
    call(0x3e298, 0x19b680)
    # Attachment changes a dynamic20-byte record, not the static hierarchy node.
    d(0x19b574, 7, (4, 4, 20)); d(0x19b594, 32, (7, 3, 40))
    d(0x19b59c, 32, (6, 7, 4)); d(0x19b5b0, 32, (3, 4, 8))
    d(0x19b5b8, 36, (0, 4, 8)); d(0x19b5c4, 36, (0, 29, 0))
    d(0x19b63c, 36, (3, 29, 12)); d(0x19b660, 36, (3, 29, 12))
    d(0x19b688, 7, (5, 4, 20)); d(0x19b6a4, 36, (0, 7, 8))
    d(0x19b6d4, 32, (3, 31, 12)); d(0x19b6dc, 14, (0, 0, -1))
    d(0x19b6e0, 36, (0, 31, 12))
    return {"schema": 1, "sha256": evidence.SHA, "checked_instruction_count": len(checked),
            "scope": "static model-attribute boarding selection and separate ID buffers; no asset codec or admission",
            "producer": {"function": 0x303a8, "attribute_mask": 128, "count_helper": 0x19b354,
                         "ordinal_helper": 0x19b300, "runtime_attribute_stride": 20,
                         "raw_index_array_field": 44, "count_field": 48,
                         "selection": "mask intersection, positive one-based match ordinal returns zero-based raw attribute index"},
            "passenger_buffers": {"allocation": 0x37968, "original_fields": [32, 36], "active_fields": [40, 44],
                                  "count_source": "carDefinition+48 for each buffer; two distinct ID arrays"},
            "capacity": {"role_setup": 0x308f8, "front_role": 764, "centre_role": 768, "rear_role": 772,
                         "max_cars": 756, "max_trains": 744, "per_train_socket_count": 760, "fleet_socket_count": 792,
                         "per_train": "first car uses front role; last uses rear unless also first; others centre; sum role+48",
                         "fleet": "definition+760 * definition+744, int32 multiplication; overflow unqualified"},
            "binding": {"board": 0x3e420, "attach": 0x19b56c, "unload": 0x3e298, "detach": 0x19b680,
                        "model_instance_field": 40, "dynamic_attribute_stride": 20,
                        "dynamic_child_handle_field": 12},
            "unresolved": ["static PC dummy-attribute-to-native-runtime loader linkage", "attribute hierarchy/transform association",
                           "paired-seat mapping", "host guest visual ownership/lifetime", "full boarding/release state machine",
                           "original asset/runtime and Windows qualification"]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    args = parser.parse_args()
    try: print(json.dumps(inspect(args.app), sort_keys=True, indent=2))
    except (OSError, evidence.pef.PEFError) as error: parser.exit(1, f"boarding socket: {error}\n")
