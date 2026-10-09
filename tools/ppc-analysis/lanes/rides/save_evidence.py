"""Original TPW coaster save/load field witnesses; no serialized track decoder."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import zlib

import evidence
from controller_native import conditional

FIXTURE_SHA = "6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a"
PAYLOAD_SHA = "a3c9a28252c37ad49a8eb78e4a0c5e1d5229d01548fa35801db67015d2589173"


def fixture_metadata(path):
    raw = path.read_bytes()
    evidence.require(hashlib.sha256(raw).hexdigest(), FIXTURE_SHA, "Easymode container identity")
    # Identity is checked before inflation; this is the one bounded known fixture.
    payload = zlib.decompress(raw[0x629:])
    evidence.require(hashlib.sha256(payload).hexdigest(), PAYLOAD_SHA, "Easymode payload identity")
    for marker in (b"EMAK", b"SAOC"):
        evidence.require(payload.count(marker), 1, "coaster boundary marker count")
    start, end = payload.index(b"EMAK") + 4, payload.index(b"SAOC")
    evidence.require((start, end), (1606446, 1606462), "coaster body boundaries")
    body = payload[start:end]
    evidence.require(len(body), 16, "empty coaster prefix length")
    fields = struct.unpack("<4I", body)
    evidence.require(fields, (0, 0, 0, 1), "empty coaster prefix fields")
    return {"container_sha256": FIXTURE_SHA, "payload_sha256": PAYLOAD_SHA, "body_start": start,
            "body_end": end, "body_bytes": 16, "body_sha256": hashlib.sha256(body).hexdigest(),
            "coaster_count": fields[0], "unqualified_global_values": fields[1:],
            "qualification": "empty prefix only; no nonempty track/train fixture or TPI COS linkage"}


def inspect(path):
    app = evidence.identified(path)
    checked = set()
    def d(address, operation, fields):
        evidence.require(evidence.d_fields(app, address, operation), fields, f"save field at code:{address:#x}")
        checked.add(address)
    def call(address, target):
        evidence.require(evidence.call_target(app, address), target, f"save call at code:{address:#x}")
        checked.add(address)
    def x(address, fields):
        evidence.x_fields(app, address, fields); checked.add(address)
    def mask(address, fields):
        word = evidence.pef._u32(app.code.data, address)
        evidence.require(word >> 26, 21, "save mask operation")
        evidence.require((word >> 21 & 31, word >> 16 & 31, word >> 11 & 31,
                          word >> 6 & 31, word >> 1 & 31), fields, "save mask fields")
        checked.add(address)

    # Previous EMAK boundary, saver/loader, then trailing SAOC boundary.
    call(0x11d6c8, 0x394b8); call(0x11c038, 0x39f98)
    d(0x11c014, 15, (0, 3, -19265)); d(0x11c018, 10, (0, 0, 19781))
    conditional(app, 0x11c01c, (12, 2), 0x11c034)
    d(0x11d6e8, 15, (3, 0, 17231)); d(0x11d6f0, 14, (0, 3, 16723))
    call(0x11d728, 0x1c4b54)
    d(0x11c0dc, 15, (0, 3, -17231)); d(0x11c0e0, 10, (0, 0, 16723))
    conditional(app, 0x11c0e4, (12, 2), 0x11c0fc)
    imports = {}
    for glue, slot, index, name in ((0x1c4b24, 0x554, 380, "LbFile_Read__FPvPvUlPUl"),
                                   (0x1c4b54, 0x4e8, 349, "LbFile_Write__FPvPCvUlPUl")):
        d(glue, 32, (12, 2, slot - 0x8000))
        relocation = app.relocs.get(app.data_section.index, {}).get(slot)
        if relocation is None: raise evidence.pef.PEFError("missing save I/O import relocation")
        evidence.require((relocation.kind, relocation.target, relocation.addend), ("import", index, 0), "save I/O import")
        symbol = app.imports[index]
        evidence.require((symbol.name, symbol.library), (name, "bullfrog shared"), "save I/O imported symbol")
        imports[name] = {"glue": glue, "toc_slot": slot, "import_index": index, "library": symbol.library}
    # Four32-bit prefix values, first the controller linked-list count.
    d(0x394ec, 32, (4, 4, 24)); d(0x39510, 14, (6, 0, 4))
    for address in (0x39534, 0x395a0, 0x3960c, 0x39678): call(address, 0x1c4b54)
    for address in (0x39fc8, 0x3a020, 0x3a080, 0x3a0e0): call(address, 0x1c4b24)
    # Endian helpers read and byte-reverse the supplied scalar in place.
    d(0x39108, 40, (4, 3, 0)); x(0x3910c, (31, 4, 0, 3, 918))
    d(0x39120, 32, (4, 3, 0)); x(0x39124, (31, 4, 0, 3, 662))
    d(0x19e54, 32, (4, 3, 0)); x(0x19e58, (31, 4, 0, 3, 662))

    header = [{"wire_offset": 0, "wire_type": "u32-le", "source": "packed controller flags; remapping not decoded"}]
    for load, operation, source, store, destination, store_op, wire_type in (
        (0x39830, 32, 8, 0x39838, 1116, 36, "u32-le"),
        (0x3983c, 32, 12, 0x39840, 1120, 44, "u16-le"),
        (0x39844, 32, 16, 0x39848, 1122, 44, "u16-le"),
        (0x3984c, 32, 20, 0x39850, 1124, 44, "u16-le"),
        (0x39854, 32, 28, 0x39858, 1126, 44, "u16-le"),
        (0x3985c, 32, 32, 0x39860, 1128, 44, "u16-le"),
        (0x39864, 48, 44, 0x39868, 1130, 52, "f32-le"),
        (0x3986c, 32, 244, 0x39870, 1136, 44, "u16-le"),
        (0x39874, 32, 240, 0x39878, 1138, 44, "u16-le"),
        (0x3987c, 32, 320, 0x39880, 1140, 36, "u32-le")):
        d(load, operation, (0, 27, source)); d(store, store_op, (0, 1, destination))
        header.append({"wire_offset": destination - 1112, "wire_type": wire_type, "controller_field": source})
    d(0x39740, 44, (0, 1, 1134))
    d(0x39728, 32, (4, 3, 256)); d(0x3972c, 32, (0, 4, 4))
    mask(0x39730, (0, 0, 0, 27, 27))
    conditional(app, 0x39734, (4, 2), 0x39744)
    header.append({"wire_offset": 22, "wire_type": "u16-le", "source": "count sections excluding descriptor+4 flag0x10"})
    d(0x398e8, 14, (5, 1, 1112)); d(0x398f0, 14, (6, 0, 32)); call(0x398f4, 0x1c4b54)
    d(0x3a1c8, 14, (5, 1, 328)); d(0x3a1d0, 14, (6, 0, 32)); call(0x3a1d4, 0x1c4b24)
    call(0x39884, 0x39120); call(0x39894, 0x39108); call(0x398bc, 0x19e54)

    track = [{"wire_offset": 0, "wire_type": "u32-le", "source": "packed section flags; full remapping unqualified"}]
    for load, operation, register, source, store, destination, store_op, wire_type, meaning in (
        (0x39bc0, 32, 31, 112, 0x39bc8, 1080, 36, "u32-le", "section field"),
        (0x39bd0, 32, 4, 12, 0x39bd4, 1084, 44, "u16-le", "section descriptor grid X"),
        (0x39bdc, 32, 4, 16, 0x39be0, 1086, 44, "u16-le", "section descriptor grid Y"),
        (0x39be4, 32, 31, 4, 0x39be8, 1088, 38, "u8", "cell stack insertion ordinal, not Direction"),
        (0x39bec, 48, 31, 232, 0x39bf0, 1089, 52, "f32-le", "section field"),
        (0x39bf4, 48, 31, 216, 0x39bf8, 1093, 52, "f32-le", "section field"),
        (0x39bfc, 48, 31, 220, 0x39c00, 1097, 52, "f32-le", "section field"),
        (0x39c04, 48, 31, 224, 0x39c08, 1101, 52, "f32-le", "section field"),
        (0x39c0c, 48, 31, 248, 0x39c10, 1105, 52, "f32-le", "section field"),
        (0x39c14, 32, 31, 176, 0x39c18, 1109, 38, "u8", "number of following u16 link values")):
        d(load, operation, (0, register, source)); d(store, store_op, (0, 1, destination))
        track.append({"wire_offset": destination - 1076, "wire_type": wire_type, "native_field": source, "meaning": meaning})
    d(0x39c68, 14, (5, 1, 1076)); d(0x39c70, 14, (6, 0, 34)); call(0x39c74, 0x1c4b54)
    d(0x3a438, 14, (5, 1, 360)); d(0x3a440, 14, (6, 0, 34)); call(0x3a444, 0x1c4b24)
    call(0x39c1c, 0x39120); call(0x39c2c, 0x39108); call(0x39c3c, 0x19e54)
    # Lists use individually narrowed16-bit values, not56-byte sampled-path records.
    d(0x39cf8, 32, (6, 29, 180)); d(0x39d08, 32, (0, 6, 0))
    d(0x39d10, 14, (6, 0, 2)); call(0x39d30, 0x1c4b54)
    d(0x3a688, 34, (0, 1, 393)); d(0x3a698, 36, (0, 3, 176))
    d(0x3a6b0, 14, (6, 0, 2)); call(0x3a6b4, 0x1c4b24)
    d(0x39988, 32, (0, 31, 0)); d(0x3998c, 10, (0, 0, 2))
    conditional(app, 0x39990, (4, 2), 0x39a88)
    d(0x39994, 32, (0, 31, 176)); d(0x399ac, 14, (6, 0, 4)); call(0x399d0, 0x1c4b54)
    d(0x39a20, 14, (6, 0, 2)); call(0x39a48, 0x1c4b54)
    d(0x39a88, 32, (3, 31, 256)); d(0x39a8c, 32, (0, 3, 4))
    mask(0x39a90, (0, 0, 0, 27, 27))
    conditional(app, 0x39a94, (4, 2), 0x39d70)
    call(0x39d84, 0x38da4)  # Train/car payload follows the topology records.

    # Grid coordinates flatten as y*mapwidth+x. Byte selector255 retries computed value.
    d(0x3a4cc, 40, (3, 1, 368)); d(0x3a4d4, 40, (4, 1, 370)); d(0x3a4dc, 34, (8, 1, 372))
    call(0x3a4e8, 0x375c4)
    d(0x37638, 32, (0, 6, 24))
    x(0x3763c, (31, 0, 4, 0, 235)); x(0x37640, (31, 6, 3, 0, 266))
    d(0x377bc, 10, (0, 0, 255))
    conditional(app, 0x377c0, (4, 2), 0x377cc)
    d(0x377c4, 32, (0, 1, 80)); call(0x37804, 0x35fdc)
    # Follow the byte through builder arguments to its actual cell-list assignment.
    d(0x36020, 14, (28, 10, 0)); d(0x36514, 14, (6, 28, 0)); call(0x36528, 0x34d90)
    d(0x34da0, 14, (26, 6, 0)); d(0x34da8, 14, (25, 5, 0)); d(0x34db8, 14, (24, 4, 0))
    d(0x34f60, 36, (24, 3, 52)); d(0x34f74, 36, (26, 24, 4))
    d(0x34f7c, 36, (25, 24, 256)); d(0x34f80, 32, (3, 25, 48))
    d(0x34f84, 14, (0, 3, 1)); d(0x34f88, 36, (0, 25, 48))
    d(0x34f48, 36, (3, 5, 4)); d(0x34f4c, 14, (3, 3, -1))
    return {"schema": 1, "sha256": evidence.SHA, "checked_instruction_count": len(checked),
            "scope": "original TPW native save/load field contracts, no full codec/TPI COS layout or motion",
            "imports": imports,
            "boundary": {"save_call": 0x11d6c8, "save": 0x394b8, "load_call": 0x11c038, "load": 0x39f98,
                         "previous_marker": "EMAK", "trailing_marker": "SAOC", "markers_are_prefixes": False},
            "prefix": {"bytes": 16, "fields": ["coaster count", "unqualified global1", "unqualified global2", "unqualified global3"]},
            "controller_header": {"bytes": 32, "fields": sorted(header, key=lambda f: f["wire_offset"])},
            "section_record": {"bytes": 34, "fields": track, "followed_by": "count*u16-le link values",
                               "link_count_domain": "native list loop uses full+176; byte33 is narrowed; values above255 unqualified",
                               "byte_selector": "cell stack insertion ordinal;255 selects computed fallback; not Direction",
                               "ordinal_consumer": {"function": 0x34d90, "section_field": 4, "cell_count": 48,
                                                    "cell_section_array_field": 52, "cell_pointer_in_section": 256}},
            "additional_payload": ["type2 auxiliary u32 count/u16 list", "filtered linked sections", "train/car/passenger payload via0x38da4"],
            "unresolved": ["nonempty PC save fixture", "full section flags/float meanings", "complete topology/train codec",
                           "TrackInfo.Direction axis/enum", "type4 binding", "TPI COS format linkage", "full motion/runtime qualification"]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    parser.add_argument("--fixture", type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.app)
        if args.fixture: result["pc_fixture"] = fixture_metadata(args.fixture)
        print(json.dumps(result, sort_keys=True, indent=2))
    except (OSError, evidence.pef.PEFError, zlib.error) as error: parser.exit(1, f"coaster save: {error}\n")
