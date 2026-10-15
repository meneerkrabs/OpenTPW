"""Original TPW park save (TPWS/TPWI) inventory: section and thing-model witnesses, read-only.

Static Mac PEF checks (no game execution, no disassembly output):

* the top-level writer 0x11cb60 calls each subsystem writer, then builds its 4-byte delimiter
  (lis/addi) and writes it; the loader 0x11b5ac calls the matching reader, then compares the
  delimiter it reads (addis/cmplwi). Delimiters trail their block.
* the World writer 0x105d3c / reader 0x106708 dispatch every used thing through two 20-entry
  jump tables (writer data 0x44924, reader data 0x44974). Each model case calls one shared
  bidirectional serializer, with mode r5 = 1 (write) or 0 (read) and r6 = 1 on both sides.
* fixed loop bounds and record strides inside selected serializers.

PC fixture checks (Easymode.TPWI, identity-pinned): the 17 delimiters split the payload in
writer order, and the World thing chain from the map end walks to exactly the WRLD delimiter
using the model body sizes below. Nothing is written; proprietary bytes are not printed.

Usage: python3 -I save_inventory.py SimThemePark.data [--fixture Easymode.TPWI] [--mac-image hfs.img]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import mmap
from pathlib import Path
import struct
import sys
import zlib

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef  # noqa: E402
from timer_evidence import call_target, d_fields, require  # noqa: E402

APP_SHA = "04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5"
FIXTURE_SHA = "6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a"
PAYLOAD_SHA = "a3c9a28252c37ad49a8eb78e4a0c5e1d5229d01548fa35801db67015d2589173"
PAYLOAD_OFFSET = 0x629
FIXTURE_LIMIT = 8 * 1024 * 1024
TOC_BASE = 0x8000  # data offset of r2; toc[slot] = data[TOC_BASE + displacement]

ADDI, ADDIS, CMPLWI, CMPWI, MULLI, LWZ = 14, 15, 10, 11, 7, 32

# name, file tag, writer call site, writer, hi site, hi register, lo site, reader call site, reader,
# reader addis site, reader cmplwi site. The RSSE delimiter reuses r28 = 0x5253 set for RSYS.
SECTIONS = (
    ("World", "WRLD", 0x11cc28, 0x105d3c, 0x11cc58, 3, 0x11cc60, 0x11b680, 0x106708, 0x11b72c, 0x11b730),
    ("Scripts", "SPSC", 0x11cd1c, 0xb7f54, 0x11cd20, 3, 0x11cd28, 0x11b750, 0xb83e4, 0x11b7f4, 0x11b7f8),
    ("Particles", "PART", 0x11cde0, 0x9c2c0, 0x11ce00, 3, 0x11ce08, 0x11b818, 0x9cc30, 0x11b8c0, 0x11b8c4),
    ("MessageCentre", "MESS", 0x11cf00, 0x116a20, 0x11cf20, 3, 0x11cf28, 0x11b928, 0x116ad8, 0x11b9cc, 0x11b9d0),
    ("Clock", "CLOK", 0x11cfe8, 0x10e944, 0x11d008, 3, 0x11d010, 0x11b9f4, 0x10e998, 0x11ba98, 0x11ba9c),
    ("VanillaTime", "VANT", 0x11d0c8, 0x127ac8, 0x11d0e8, 3, 0x11d0f0, 0x11bac0, 0x127b64, 0x11bb64, 0x11bb68),
    ("GameSystem", "GSYS", 0x11d1a4, 0x1c2e00, 0x11d1c4, 3, 0x11d1cc, 0x11bb88, 0x1c31f4, 0x11bc2c, 0x11bc30),
    ("RideSystem", "RSYS", 0x11d280, 0x5ae70, 0x11d2a0, 28, 0x11d2a8, 0x11bc50, 0x5b898, 0x11bcf4, 0x11bcf8),
    ("TrackRides", "TRAK", 0x11d35c, 0x1a424, 0x11d37c, 3, 0x11d384, 0x11bd18, 0x1b74c, 0x11bdbc, 0x11bdc0),
    ("FlyingRides", "FLYR", 0x11d438, 0x674bc, 0x11d458, 3, 0x11d460, 0x11bde0, 0x67c1c, 0x11be84, 0x11be88),
    ("RSSE", "RSSE", 0x11d514, 0xb3868, 0x11d2a0, 28, 0x11d534, 0x11bea8, 0xb4818, 0x11bf4c, 0x11bf50),
    ("Camera", "KAME", 0x11d5ec, 0x2cb90, 0x11d60c, 3, 0x11d614, 0x11bf70, 0x2ceac, 0x11c014, 0x11c018),
    ("Coasters", "COAS", 0x11d6c8, 0x394b8, 0x11d6e8, 3, 0x11d6f0, 0x11c038, 0x39f98, 0x11c0dc, 0x11c0e0),
    ("Advisor", "ADVS", 0x11d7a4, 0x78c8, 0x11d7a8, 3, 0x11d7b0, 0x11c100, 0x7c7c, 0x11c188, 0x11c18c),
    ("Sound", "SOUN", 0x11d864, 0xbbad0, 0x11d868, 3, 0x11d870, 0x11c1ac, 0xbbd1c, 0x11c234, 0x11c238),
    ("Cheat", "CHTS", 0x11d944, 0x10e788, 0x11d964, 3, 0x11d96c, 0x11c278, 0x10e6d4, 0x11c31c, 0x11c320),
    ("AdvisorScoring", "ADSC", 0x11da58, 0xe834, 0x11da78, 3, 0x11da80, 0x11c370, 0xe7c0, 0x11c414, 0x11c418),
)
# Unframed blocks: the action recording before World and the UI block after ADSC.
UNFRAMED = {"ActionRecording": (0x11cbc4, 0x10bc1c, 0x11b610, 0x10bc1c), "UI": (0x11db40, 0x18f738, 0x11c43c, 0x18f704)}

# Thing model -> (name, serializer, writer call site, reader call site, reader allocation bytes).
MODELS = {
    1: ("Guest", 0xe7f34, 0x1064d4, 0x107044, 556),
    3: ("Attraction", 0xdaf04, 0x1064fc, 0x1070e4, 1096),
    4: ("Mechanic", 0xd9674, 0x10640c, 0x106d88, 536),
    5: ("Handyman", 0xd57ac, 0x106434, 0x106e04, 536),
    6: ("Entertainer", 0xd28e0, 0x10645c, 0x106e80, 532),
    7: ("Guard", 0xd483c, 0x106484, 0x106efc, 532),
    8: ("Researcher", 0xf00f0, 0x1064ac, 0x106f78, 532),
    9: ("StaffHQ", 0xf7c34, 0x106524, 0x107164, 104),
    10: ("MechanicHQ", 0xda068, 0x10654c, 0x1071bc, 14),
    11: ("AdvisorController", 0x82e0, 0x106574, 0x107258, 5872),
    12: ("TagSystem", 0xf9108, 0x10659c, 0x1072b0, 2062),
    13: ("ParkAnalyser", 0xc0230, 0x1065c4, 0x1074fc, None),
    14: ("ResearchLab", 0xf0930, 0x1065ec, 0x107590, 5228),
    15: ("Weather", 0x102b1c, 0x106614, 0x107604, 104),
    16: ("Bank", 0xcba54, 0x10663c, 0x10765c, 296),
    17: ("UIMsgReceiver", 0xfc7dc, 0x106664, 0x1076b4, 12),
    19: ("ChallengeSystem", 0xcf42c, 0x10669c, 0x107744, 996),
}
# Model numbers without a serializer: writer/reader case targets (0 and 2 unknown, 18 online person).
REJECTED_MODELS = {0: (0x1066b0, 0x107760), 2: (0x1066b0, 0x107760), 18: (0x106678, 0x1076d0)}
WRITER_TABLE = (0x1063dc, 0x1063e4, 0x3700, 0x44924)  # cmplwi 19, lwz r3 toc, toc slot, table
READER_TABLE = (0x106d0c, 0x106d14, 0x36fc, 0x44974)
# Case entry points; each reader case starts with `li r3, allocation` (model 13 computes its size).
WRITER_CASE = {1: 0x1064c0, 3: 0x1064e8, 4: 0x1063f8, 5: 0x106420, 6: 0x106448, 7: 0x106470, 8: 0x106498,
               9: 0x106510, 10: 0x106538, 11: 0x106560, 12: 0x106588, 13: 0x1065b0, 14: 0x1065d8,
               15: 0x106600, 16: 0x106628, 17: 0x106650, 19: 0x106688}
READER_CASE = {1: 0x106f94, 3: 0x107060, 4: 0x106d28, 5: 0x106da4, 6: 0x106e20, 7: 0x106e9c, 8: 0x106f18,
               9: 0x107100, 10: 0x107180, 11: 0x1071d8, 12: 0x107274, 13: 0x1072cc, 14: 0x107518,
               15: 0x1075ac, 16: 0x107620, 17: 0x107678, 19: 0x1076e0}

# (address, primary operation, (rD, rA, immediate), meaning)
LOOP_WITNESSES = (
    (0xcf478, MULLI, (0, 29, 48), "challenge slot runtime stride 48"),
    (0xcf4a0, CMPWI, (0, 29, 20), "challenge system writes 20 challenge slots"),
    (0xf098c, MULLI, (0, 29, 20), "research project runtime stride 20"),
    (0xf09c4, CMPWI, (0, 29, 256), "research lab writes 256 project records"),
    (0xf0a08, CMPWI, (0, 29, 5), "research lab writes 5 group records"),
    (0x8388, MULLI, (0, 29, 24), "advisor pending record runtime stride 24"),
    (0x83a0, ADDI, (3, 30, 20), "advisor pending records start at controller +20"),
    (0x83c0, CMPWI, (0, 29, 8), "advisor controller writes 8 pending records"),
    (0x8444, ADDI, (4, 30, 224), "advisor history starts at controller +224"),
    (0x846c, CMPWI, (0, 29, 351), "advisor controller writes 351 history entries"),
    (0x8484, ADDI, (3, 24, 5840), "advisor current record at controller +5840"),
    (0xcccc, ADDI, (6, 0, 16), "advisor history entry is one 16-byte write"),
    (0xf7cd8, MULLI, (0, 29, 12), "staff HQ strike level runtime stride 12"),
    (0xf7d0c, CMPWI, (0, 29, 5), "staff HQ writes 5 strike levels"),
    (0xfc108, CMPWI, (0, 29, 5), "staff HQ writes 5 training budgets"),
    (0xcd240, ADDI, (6, 0, 4), "Used Thing Head/Next helper writes 4 bytes"),
    (0x10a648, ADDI, (6, 0, 4), "thingmodel helper writes 4 bytes"),
)
THING_HEADER_CALLS = ((0x10634c, 0xcd1b0), (0x106394, 0xcd1b0), (0x1063c4, 0x10a5b8))

# Saved body bytes after the 8-byte (next ID, model) header. Components are documented in
# docs/reverse/SAVE-plan.md; 3 is variable (history buffers), 13 is closure-derived only.
PERSON, STAFF = 390, 105
BODY_BYTES = {
    1: PERSON + 135,
    4: PERSON + STAFF + 4 + 2 + 2,
    5: PERSON + STAFF + 2 + 4 + 2 + 2,
    6: PERSON + STAFF + 4 + 2,
    7: PERSON + STAFF + 2 + 4 + 2,
    8: PERSON + STAFF + 4 + 2,
    9: 8 + 4 + 2 + 5 * 12 + 5 * 4 + 1,
    10: 8 + 2,
    11: 8 + 4 + 4 + 8 * 21 + 4 + 4 + 4 + 351 * 16 + 21 + 1 + 4,
    12: 8,
    13: 73536,
    14: 8 + 256 * 16 + 5 * 13 + 4 + 4 + 4 + 1,
    15: 8 + 4 + 8 + 8 + 13 + 4 + 4 + 1 + 4 + 4 + 4 + 12 + 4 + 4 + 4 + 4 + 1,
    16: 8 + 7 * 4 + 8 * 8 * 4,
    17: 8,
    19: 8 + 20 * 45 + 4 + 8 + 1 + 1 + 1 + 4 + 2,
}
CLOSURE_ONLY = {13}
ATTRACTION_HISTORIES = 6
# Easymode facts used as invariants.
EASYMODE_MAP_END = 1385521  # formats lane corpus_check: 16,134 x 84 + 250 x 94 cells from 6,765
EASYMODE_BLOCKS = {"World": 1495462, "Scripts": 5452, "Particles": 76252, "MessageCentre": 266, "Clock": 8,
                   "VanillaTime": 4, "GameSystem": 36, "RideSystem": 17526, "TrackRides": 44,
                   "FlyingRides": 452, "RSSE": 10856, "Camera": 40, "Coasters": 16, "Advisor": 372,
                   "Sound": 1445, "Cheat": 2, "AdvisorScoring": 4, "UI": 4}
WORLD_VAR_OWNERS = {"mStaffHQ": 9, "mMechanicHQ": 10, "mTagSystem": 12, "mParkAnalyser": 13, "mResearchLab": 14,
                    "mWeather": 15, "mBankAccount": 16, "mUIMsgReceiver": 17, "mFirstHandyman": 5,
                    "mFirstMechanic": 4, "mFirstEntertainer": 6, "mFirstGuard": 7, "mFirstResearcher": 8,
                    "mFirstObject": 3, "mParkGates": 3, "mTrafficLights": 3}
# Offsets of the labelled 2-byte thing IDs in the world variables (payload offset, Easymode).
WORLD_VAR_IDS = {"mBankAccount": 1189, "mMechanicHQ": 1197, "mParkAnalyser": 1199, "mParkGates": 1209,
                 "mTrafficLights": 1211, "mResearchLab": 1217, "mStaffHQ": 1219, "mTagSystem": 1221,
                 "mUIMsgReceiver": 1223, "mWeather": 1225, "mFirstHandyman": 1231, "mFirstMechanic": 1233,
                 "mFirstEntertainer": 1235, "mFirstGuard": 1237, "mFirstResearcher": 1239, "mFirstObject": 1241}


def identified(path: Path) -> pef.PEFContainer:
    raw = path.read_bytes()
    require(hashlib.sha256(raw).hexdigest(), APP_SHA, "SimThemePark identity")
    return pef.PEFContainer(raw, path.name)


def file_tag(value: int) -> str:
    """The writer byte-swaps the constant before LbFile_Write, so the file holds it little-endian."""
    return struct.pack("<I", value).decode("ascii")


def data_pointer(app, slot: int, section: int) -> int:
    target = app.relocs.get(app.data_section.index, {}).get(slot)
    if target is None:
        raise pef.PEFError(f"missing relocation at data:{slot:#x}")
    require((target.kind, target.target), ("section", section), f"relocation at data:{slot:#x}")
    return target.addend


def check_sections(app, sections=SECTIONS) -> list[dict]:
    result = []
    for name, tag, wsite, writer, hi_site, hi_reg, lo_site, rsite, reader, addis_site, cmp_site in sections:
        require(call_target(app, wsite), writer, f"{name} writer call")
        require(call_target(app, rsite), reader, f"{name} reader call")
        _, _, hi = d_fields(app, hi_site, ADDIS)
        require(d_fields(app, hi_site, ADDIS)[:2], (hi_reg, 0), f"{name} delimiter high half register")
        rd, ra, lo = d_fields(app, lo_site, ADDI)
        require((rd, ra), (0, hi_reg), f"{name} delimiter low half registers")
        value = (hi & 0xffff) << 16 | (lo & 0xffff)
        require(file_tag(value)[::-1], tag, f"{name} delimiter constant")
        # Loader: r0 = r3 + (-hi << 16); cmplwi r0, lo  <=>  r3 == value.
        require(0 < hi < 0x8000, True, f"{name} delimiter high half range")
        require(d_fields(app, addis_site, ADDIS), (0, 3, -hi), f"{name} loader delimiter high half")
        require(d_fields(app, cmp_site, CMPLWI), (0, 0, lo), f"{name} loader delimiter low half")
        result.append({"name": name, "tag": tag, "file_bytes": file_tag(value), "writer": writer, "reader": reader})
    for name, (wsite, writer, rsite, reader) in UNFRAMED.items():
        require(call_target(app, wsite), writer, f"{name} writer call")
        require(call_target(app, rsite), reader, f"{name} reader call")
    return result


def check_tables(app, models=MODELS) -> dict:
    code = app.code.index
    tables = {}
    for side, (cmp_site, load_site, slot, base), mode in (("writer", WRITER_TABLE, 1), ("reader", READER_TABLE, 0)):
        require(d_fields(app, cmp_site, CMPLWI), (0, 0, 19), f"{side} thing model bound")
        require(d_fields(app, load_site, LWZ), (3, 2, slot - TOC_BASE), f"{side} jump table TOC load")
        require(data_pointer(app, slot, app.data_section.index), base, f"{side} jump table address")
        targets = [data_pointer(app, base + 4 * model, code) for model in range(20)]
        for model, (name, serializer, wsite, rsite, _) in models.items():
            site = wsite if side == "writer" else rsite
            require(call_target(app, site), serializer, f"{side} model {model} ({name}) serializer")
            require(d_fields(app, site - 8, ADDI), (5, 0, mode), f"{side} model {model} direction argument")
            require(d_fields(app, site - 4, ADDI), (6, 0, 1), f"{side} model {model} version argument")
            cases = WRITER_CASE if side == "writer" else READER_CASE
            require(targets[model], cases[model], f"{side} model {model} ({name}) case")
        for model, (writer_case, reader_case) in REJECTED_MODELS.items():
            require(targets[model], writer_case if side == "writer" else reader_case, f"{side} rejected model {model}")
        tables[side] = {"base": base, "targets": targets}
    for model, (name, _, _, _, size) in models.items():
        if size is not None:
            require(d_fields(app, READER_CASE[model], ADDI), (3, 0, size), f"reader model {model} ({name}) allocation")
    for site, target in THING_HEADER_CALLS:
        require(call_target(app, site), target, "thing header helper call")
    return tables


def check_loops(app, witnesses=LOOP_WITNESSES) -> list[str]:
    for address, operation, fields, meaning in witnesses:
        require(d_fields(app, address, operation), fields, meaning)
    return [meaning for *_, meaning in witnesses]


def inspect(path: Path) -> dict:
    app = identified(path)
    sections = check_sections(app)
    tables = check_tables(app)
    loops = check_loops(app)
    return {"schema": 1, "sha256": APP_SHA, "sections": sections, "unframed": sorted(UNFRAMED),
            "models": {str(m): {"name": n, "serializer": s, "reader_allocation": a}
                       for m, (n, s, _, _, a) in MODELS.items()},
            "rejected_models": sorted(REJECTED_MODELS), "jump_tables": {k: v["base"] for k, v in tables.items()},
            "loop_witnesses": loops}


# ------------------------------------------------------------------ fixture
def decode_fixture(raw: bytes) -> bytes:
    if len(raw) > FIXTURE_LIMIT:
        raise ValueError("fixture larger than the evidence bound")
    require(hashlib.sha256(raw).hexdigest(), FIXTURE_SHA, "Easymode container identity")
    payload = zlib.decompress(raw[PAYLOAD_OFFSET:])
    require(hashlib.sha256(payload).hexdigest(), PAYLOAD_SHA, "Easymode payload identity")
    return payload


def split_blocks(payload: bytes, sections=SECTIONS) -> dict:
    """Blocks in writer order; each delimiter must occur exactly once and in order."""
    blocks, position = {}, 0
    for name, tag, *_ in sections:
        stored = tag[::-1].encode("ascii")
        if payload.count(stored) != 1:
            raise ValueError(f"{name} delimiter {stored!r} is not unique")
        found = payload.index(stored)
        if found < position:
            raise ValueError(f"{name} delimiter out of writer order")
        blocks[name] = (position, found)
        position = found + 4
    blocks["UI"] = (position, len(payload))
    return blocks


def attraction_body(payload: bytes, body: int) -> int:
    """Class 3 body length: fixed fields plus six history buffers of u32 entries (see TPWS-PAYLOAD.md)."""
    offset = body + 8 + 4 + 2 + 8 * 4 + 4 + 2 + 33 * 4 + 3 * 4 + 24
    for index in range(ATTRACTION_HISTORIES):
        count = struct.unpack_from("<I", payload, offset + 4)[0]
        if count > 1024:
            raise ValueError("implausible attraction history count")
        offset += 4 + 4 + 1 + 4 + 4 * count
        if index in (1, 2):
            offset += 4  # mNumCustomers after the second buffer, mNumWalkAways after the third
    offset += 1 + 1 + 4 + 2 + 6 * 4 + 3 * 4 + 5 * 4 + 1
    return offset - body


def walk_things(payload: bytes, start: int, end: int, body_bytes=BODY_BYTES) -> list[dict]:
    """Used Thing Head, then per thing (next ID, model, body); must end with next 0 exactly at `end`."""
    if not 0 <= start <= end - 4 <= len(payload) - 4:
        raise ValueError("thing list bounds")
    current = struct.unpack_from("<I", payload, start)[0]
    offset, things, seen = start + 4, [], set()
    while True:
        if current in seen or not 0 < current < 0x10000:
            raise ValueError(f"bad thing ID {current}")
        seen.add(current)
        if offset + 8 > end:
            raise ValueError("thing header past the World delimiter")
        following, model = struct.unpack_from("<II", payload, offset)
        if model == 3:
            size = attraction_body(payload, offset + 8)
        elif model in body_bytes:
            size = body_bytes[model]
        else:
            raise ValueError(f"thing {current} has unsupported model {model}")
        if offset + 8 + size > end:
            raise ValueError(f"thing {current} body past the World delimiter")
        things.append({"id": current, "model": model, "offset": offset, "body": size})
        offset += 8 + size
        if following == 0:
            break
        current = following
    if offset != end:
        raise ValueError(f"thing chain ends at {offset}, delimiter at {end}")
    return things


def fixture_facts(path: Path) -> dict:
    payload = decode_fixture(path.read_bytes())
    blocks = split_blocks(payload)
    sizes = {name: end - start for name, (start, end) in blocks.items()}
    require(sizes, EASYMODE_BLOCKS, "Easymode block sizes in writer order")
    things = walk_things(payload, EASYMODE_MAP_END, blocks["World"][1])
    by_id = {thing["id"]: thing["model"] for thing in things}
    owners = {}
    for name, offset in WORLD_VAR_IDS.items():
        thing_id = struct.unpack_from("<H", payload, offset)[0]
        require(by_id.get(thing_id), WORLD_VAR_OWNERS[name], f"world variable {name} -> thing {thing_id} model")
        owners[name] = thing_id
    counts = {}
    for thing in things:
        counts[str(thing["model"])] = counts.get(str(thing["model"]), 0) + 1
    advisor = next(thing for thing in things if thing["model"] == 11)
    first = advisor["offset"] + 8 + 16
    records = [struct.unpack_from("<HIIiBIBB", payload, first + 21 * index) for index in range(8)]
    return {"container_sha256": FIXTURE_SHA, "payload_sha256": PAYLOAD_SHA, "block_bytes": sizes,
            "thing_count": len(things), "model_counts": counts, "chain_ids": [t["id"] for t in things],
            "world_variable_ids": owners,
            "advisor_pending_valid_flags": [record[4] for record in records],
            "advisor_pending_messages": [record[2] for record in records],
            "uninitialised_cd_bytes": payload.count(b"\xcd")}


def mac_image_copy(image: Path, fixture: Path) -> dict:
    """Bounded byte search: the Mac disc image holds one contiguous copy of the PC fixture."""
    raw = fixture.read_bytes()
    require(hashlib.sha256(raw).hexdigest(), FIXTURE_SHA, "Easymode container identity")
    with image.open("rb") as handle, mmap.mmap(handle.fileno(), 0, access=mmap.ACCESS_READ) as view:
        offset = view.find(raw[:4096])
        if offset < 0 or view[offset:offset + len(raw)] != raw:
            raise ValueError("no contiguous Easymode copy in the Mac image")
        again = view.find(raw[:4096], offset + 1)
    return {"image": image.name, "offset": offset, "second_copy": again >= 0}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("app", type=Path)
    parser.add_argument("--fixture", type=Path)
    parser.add_argument("--mac-image", type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.app)
        result["app"] = args.app.name
        if args.fixture:
            result["pc_fixture"] = fixture_facts(args.fixture)
            if args.mac_image:
                result["mac_image"] = mac_image_copy(args.mac_image, args.fixture)
        print(json.dumps(result, sort_keys=True, indent=2))
    except (OSError, ValueError, pef.PEFError, zlib.error, struct.error) as error:
        parser.exit(1, f"save inventory: {error}\n")


if __name__ == "__main__":
    main()
