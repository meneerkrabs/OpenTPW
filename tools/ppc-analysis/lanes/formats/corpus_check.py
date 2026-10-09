"""Cross-check original-code-derived format models against a private PC Data tree.

Reads WAD members and the Easymode TPWI with a bounded DWFB/RefPack/zlib
reader and prints counts and invariants only (no asset bytes). The models are
the statically read Mac PowerPC decoder paths in models.py; agreement on the PC
corpus is evidence, not proof, that the PC build behaves the same.

Usage: python3 -I corpus_check.py /path/to/theme-park-world/Data
"""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
import struct
import sys
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import models  # noqa: E402

MAX_MEMBER = 32 * 1024 * 1024
MAX_WAD = 256 * 1024 * 1024
EASYMODE_SHA256 = '6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a'


class CorpusError(Exception):
    pass


def refpack(source: bytes, limit: int = MAX_MEMBER) -> bytes:
    if len(source) < 5:
        raise CorpusError('truncated RefPack header')
    flags = source[0]
    position = 2
    if flags & 0x80:
        size = int.from_bytes(source[position:position + 4], 'big')
        position += 8 if flags & 1 else 4
    else:
        size = int.from_bytes(source[position:position + 3], 'big')
        position += 6 if flags & 1 else 3
    if size > limit:
        raise CorpusError('RefPack output exceeds limit')
    out = bytearray()

    def take(count: int) -> bytes:
        nonlocal position
        if position + count > len(source):
            raise CorpusError('truncated RefPack stream')
        chunk = source[position:position + count]
        position += count
        return chunk

    while position < len(source):
        b0 = take(1)[0]
        if b0 < 0x80:
            b1 = take(1)[0]
            literal, length, distance = b0 & 3, ((b0 >> 2) & 7) + 3, ((b0 & 0x60) << 3) + b1 + 1
        elif b0 < 0xC0:
            b1, b2 = take(2)
            literal, length, distance = b1 >> 6, (b0 & 0x3F) + 4, ((b1 & 0x3F) << 8) + b2 + 1
        elif b0 < 0xE0:
            b1, b2, b3 = take(3)
            literal = b0 & 3
            length = ((b0 & 0x0C) << 6) + b3 + 5
            distance = ((b0 & 0x10) << 12) + (b1 << 8) + b2 + 1
        elif b0 < 0xFC:
            literal, length, distance = ((b0 & 0x1F) << 2) + 4, 0, 0
        else:
            literal, length, distance = b0 & 3, 0, 0
        out += take(literal)
        if length:
            if distance > len(out) or len(out) + length > size:
                raise CorpusError('invalid RefPack copy')
            for _ in range(length):
                out.append(out[-distance])
        if len(out) > size:
            raise CorpusError('RefPack output exceeds declared size')
        if b0 >= 0xFC:
            break
    if len(out) != size:
        raise CorpusError('RefPack output size mismatch')
    return bytes(out)


def wad_members(path: Path):
    raw = path.read_bytes()
    if len(raw) > MAX_WAD or raw[:4] != b'DWFB' or len(raw) < 88:
        raise CorpusError(f'not a bounded DWFB archive: {path.name}')
    count = struct.unpack_from('<I', raw, 72)[0]
    if 88 + 40 * count > len(raw):
        raise CorpusError('truncated WAD directory')
    for index in range(count):
        _, name_offset, name_length, data_offset, data_length, compression, _ = \
            struct.unpack_from('<7I', raw, 88 + 40 * index)
        if name_offset + name_length > len(raw) or data_offset + data_length > len(raw):
            raise CorpusError('WAD entry outside archive')
        name = raw[name_offset:name_offset + name_length].split(b'\0')[0].decode('latin-1')
        data = raw[data_offset:data_offset + data_length]
        yield name, refpack(data) if compression == 4 else data


def u16(data: bytes, offset: int) -> int:
    if offset < 0 or offset + 2 > len(data):
        raise CorpusError('u16 outside member')
    return struct.unpack_from('<H', data, offset)[0]


def u32(data: bytes, offset: int) -> int:
    if offset < 0 or offset + 4 > len(data):
        raise CorpusError('u32 outside member')
    return struct.unpack_from('<I', data, offset)[0]


def node_records(data: bytes) -> dict[int, int]:
    nodes, stack, seen = {}, [u32(data, 0x78)], set()
    while stack:
        record = stack.pop()
        if not record or record in seen:
            continue
        seen.add(record)
        nodes[u32(data, record + 80)] = record
        stack.extend((u32(data, record + 12), u32(data, record + 8)))
    return nodes


def check_md2(root: Path) -> dict:
    members = []
    for wad in sorted(root.rglob('*')):
        if wad.suffix.lower() == '.wad' and wad.is_file():
            for name, data in wad_members(wad):
                if name.lower().endswith('.md2'):
                    members.append((str(wad.relative_to(root)), name, data))
    versions = collections.Counter()
    statuses = collections.Counter()
    geometry = collections.defaultdict(list)
    for wad, name, data in members:
        major, minor = u32(data, 4), u32(data, 8)
        trailer = u32(data, 0x98) if major == 221 else 0
        versions[f'{major}.{minor}'] += 1
        flags = models.LOAD_ANIMATION if trailer else 0
        if major != 221 and name.lower().endswith('m.md2'):
            flags = models.LOAD_ANIMATION
        statuses[models.load_status(major, minor, bool(trailer) or flags == models.LOAD_ANIMATION, flags,
                                    u32(data, 0))[0]] += 1
        if major == 221 and not trailer:
            geometry[wad].append((name, data))
    checks = collections.Counter()
    for wad, name, data in members:
        if u32(data, 4) != 221 or not u32(data, 0x98):
            continue
        trailer = u32(data, 0x98)
        base = None
        for geometry_name, geometry_data in geometry.get(wad, []):
            stem = geometry_name.lower()[:-4]
            if name.lower().startswith(stem) and name.lower() != geometry_name.lower():
                if base is None or len(stem) > len(base[0]):
                    base = (stem, geometry_data)
        frames_count, frames = u16(data, trailer + 24), u32(data, trailer + 48)
        checks['trailer_flag2_matches_frame_tracks' if bool(u32(data, trailer) & 2) == bool(frames_count)
               else 'trailer_flag2_mismatch'] += 1
        if frames_count:
            checks['texture_frame_tracks'] += frames_count
            if base:
                slots = u16(base[1], 0x36)
                slot_table = u32(base[1], 0x54)
                for k in range(frames_count):
                    slot, keys, pairs = u16(data, frames + 8 * k), u16(data, frames + 8 * k + 2), \
                        u32(data, frames + 8 * k + 4)
                    if slot < slots:
                        limit = u16(base[1], slot_table + 16 * slot + 10)
                        ok = all(u16(data, pairs + 4 * j + 2) < limit for j in range(keys))
                        checks['texture_frame_tracks_frames_in_range' if ok else 'texture_frame_tracks_out_of_range'] += 1
        records, table = u16(data, trailer + 18), u32(data, trailer + 44)
        nodes = node_records(base[1]) if base else {}
        for r in range(records):
            record = table + 64 * r
            flags, duration, block = u32(data, record + 4), u32(data, record + 12), u32(data, record + 40)
            if flags & 0x20000:
                checks['visibility_lists'] += 1
                count, values = u16(data, record + 22), u32(data, record + 48)
                ticks = [abs(struct.unpack_from('<h', data, values + 2 * i)[0]) for i in range(count)]
                checks['visibility_ticks_within_duration' if all(t <= duration for t in ticks)
                       else 'visibility_ticks_beyond_duration'] += 1
            if not block:
                continue
            if flags & 0x4000:
                checks['vertex_blocks_12_byte'] += 1
                continue
            checks['vertex_blocks_quantized'] += 1
            header = [u16(data, block + 2 * i) for i in range(5)]
            groups, group_table = header[1], u32(data, block + 12)
            checks[f'quantized_header_flags_{header[0]}'] += 1
            if header[2:] == [0, 0, 0] and u32(data, block + 16) == 0:
                checks['quantized_unused_fields_zero'] += 1
            last_ticks = [u16(data, u32(data, group_table + 20 * g + 8) + 2 * (u16(data, group_table + 20 * g) - 1))
                          for g in range(groups)]
            if max(last_ticks) == duration:
                checks['quantized_last_tick_equals_duration'] += 1
            node = u16(data, record + 20)
            if base and node in nodes:
                positions = u16(base[1], nodes[node] + 88)
                first = [u16(data, u32(data, group_table + 4) + 2 * i) for i in range(u16(data, group_table + 2))]
                if first == [positions, positions + 1]:
                    checks['quantized_group0_virtual_vertices'] += 1
                if sum(u16(data, group_table + 20 * g + 2) for g in range(1, groups)) == positions:
                    checks['quantized_groups_cover_mesh_vertices'] += 1
    return {'members': len(members), 'versions': dict(versions), 'load_status_by_model': dict(statuses),
            'animation_checks': dict(sorted(checks.items()))}


def check_map(root: Path) -> dict:
    headers = collections.Counter()
    for theme in ('fantasy', 'hallow', 'jungle', 'space'):
        for name, data in wad_members(root / 'levels' / theme / 'terrain.wad'):
            if name.lower().endswith('.map') and data[:4] == b'TP2M':
                headers[tuple(u32(data, 0x34 + 4 * i) for i in range(5))] += 1
    return {'tp2m_header_value_tuples': {str(k): v for k, v in headers.items()}}


def check_tpws(root: Path) -> dict:
    path = root / 'levels' / 'jungle' / 'Easymode.TPWI'
    raw = path.read_bytes()
    if hashlib.sha256(raw).hexdigest() != EASYMODE_SHA256:
        raise CorpusError('unexpected Easymode.TPWI identity')
    if raw[0x60d:0x611] != b'BILZ':
        raise CorpusError('missing BILZ chunk')
    declared = u32(raw, 0x611)
    inflater = zlib.decompressobj()
    payload = inflater.decompress(raw[0x629:], declared + 1)
    if len(payload) != declared or not inflater.eof:
        raise CorpusError('payload size mismatch')
    blocks, tail = models.split_sections(payload)
    sizes = {name: end - start for name, _, start, end in blocks}
    head, world_offset, world, world_end = models.parse_world_vars(payload)
    # The map writer emits 16,384 cell records; find the unique start where all parse with known status.
    grid_start = None
    for start in range(world_end, min(len(payload), world_end + 64 * 1024)):
        offset, ok = start, True
        for _ in range(models.GRID_CELLS):
            if payload[offset] not in (3, 7):
                ok = False
                break
            cell, offset = models.parse_cell(payload, offset)
            if cell['track']['mSegmentNumber'] != 0xFFFF:
                ok = False
                break
        if ok:
            if grid_start is not None:
                raise CorpusError('ambiguous cell grid start')
            grid_start, grid_end = start, offset
    if grid_start is None:
        raise CorpusError('cell grid not found')
    offset = grid_start
    status = collections.Counter()
    map_types = collections.Counter()
    parents = 0
    path_type, path_tile, path_both = 0, 0, 0
    for _ in range(models.GRID_CELLS):
        cell, offset = models.parse_cell(payload, offset)
        status[cell['status']] += 1
        map_types[cell['map']['mType']] += 1
        parents += cell['map']['mParentID'] != 0
        is_type, is_tile = cell['map']['mType'] == 1, bool(cell['map']['mTileData'][0] & 1)
        path_type, path_tile, path_both = path_type + is_type, path_tile + is_tile, path_both + (is_type and is_tile)
    return {'payload_bytes': len(payload), 'section_bytes': sizes, 'untagged_tail_bytes': tail[1] - tail[0],
            'action_record': head, 'world_vars_offset': world_offset, 'world_vars_end': world_end,
            'world_vars': world, 'cell_grid': [grid_start, grid_end], 'cell_status_counts': dict(status),
            'map_cell_type_counts': dict(map_types.most_common(8)), 'cells_with_parent': parents,
            'map_type_1_cells': path_type, 'tile_data_bit0_cells': path_tile, 'both': path_both}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('data_root', type=Path)
    args = parser.parse_args()
    try:
        result = {'md2': check_md2(args.data_root), 'map': check_map(args.data_root),
                  'tpws': check_tpws(args.data_root)}
    except (OSError, ValueError, CorpusError, zlib.error) as error:
        parser.exit(1, f'corpus check: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True, default=str))


if __name__ == '__main__':
    main()
