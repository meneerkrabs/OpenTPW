"""ANIM-R corpus invariants over a private PC Data tree (read-only; prints counts and verdicts, no asset bytes).

Usage: python3 -I anim_corpus.py /path/to/theme-park-world/Data

Every invariant is a relation between fields of the members (for example "a UV block lists
exactly the corners of its node's mesh"), checked over the whole corpus; the counts are
reported for the record, not pinned, so a different install fails only if a relation breaks.
The WAD/RefPack reader is the formats lane's bounded reader.
"""
from __future__ import annotations

import collections
import json
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / 'formats'))

import anim_model  # noqa: E402
import anim_witness  # noqa: E402
import corpus_check  # noqa: E402

MAGIC = 0x1CD15D46
CURRENT = (221, 203)
# Kinds whose decoding is complete for playback (TRS, quantised vertices, UV, toggles); 0x200/0x400
# (path parameter) are only partly traced, 0x4000 has no traced consumer.
DECODED_BITS = frozenset({0x1, 0x8, 0x80, 0x1000, 0x10000, 0x20000})
PARENT_BIT = {0x10: 0x8, 0x20: 0x8, 0x40: 0x8, 0x8000: 0x8, 0x100: 0x80, 0x800: 0x600, 0x2000: 0x1000}
LOADER_FLAGS = (0, 2, 6)  # the two application call sites (anim_witness loader_gate)


def mask(bits) -> int:
    value = 0
    for bit in bits:
        value |= bit
    return value


KNOWN_MASK = mask(anim_witness.READ_RECORD_BITS | anim_witness.UNREAD_RECORD_BITS)
ADVISOR_ALLOWED_MASK = mask(DECODED_BITS | anim_witness.UNREAD_RECORD_BITS)


class Member:
    """An MD2 member with bounded little-endian reads."""

    def __init__(self, directory: str, name: str, data: bytes):
        self.directory, self.name, self.data = directory, name, data

    def u16(self, offset: int) -> int:
        return corpus_check.u16(self.data, offset)

    def u32(self, offset: int) -> int:
        return corpus_check.u32(self.data, offset)

    def f32(self, offset: int) -> float:
        if offset < 0 or offset + 4 > len(self.data):
            raise corpus_check.CorpusError('f32 outside member')
        return struct.unpack_from('<f', self.data, offset)[0]

    @property
    def version(self) -> tuple[int, int]:
        return self.u32(4), self.u32(8)

    @property
    def stem(self) -> str:
        return self.name.rsplit('.', 1)[0].lower()

    @property
    def is_animation(self) -> bool:
        return self.version == CURRENT and self.u32(0x98) != 0

    def records(self):
        trailer = self.u32(0x98)
        table = self.u32(trailer + 44)
        for index in range(self.u16(trailer + 18)):
            yield table + 64 * index

    def mesh_record(self, node_index: int) -> int | None:
        """The node record of a geometry member if it is a 160-byte mesh record, else None."""
        record = corpus_check.node_records(self.data).get(node_index)
        meshes, table = self.u16(0x44), self.u32(0x70)
        if record is None or not table <= record < table + 160 * meshes or (record - table) % 160:
            return None
        return record


def members(root: Path):
    for path in sorted(root.rglob('*')):
        if not path.is_file():
            continue
        relative = path.relative_to(root).as_posix()
        if path.suffix.lower() == '.md2':
            yield Member(path.parent.relative_to(root).as_posix(), path.name, path.read_bytes())
        elif path.suffix.lower() == '.wad':
            for name, data in corpus_check.wad_members(path):
                if name.lower().endswith('.md2'):
                    yield Member(relative, name, data)


def pair(animation: Member, geometry: list[Member]) -> Member | None:
    """Longest geometry stem that is a proper prefix of the animation stem in the same directory."""
    best = None
    for candidate in geometry:
        if candidate.directory == animation.directory and animation.stem.startswith(candidate.stem) \
                and len(candidate.stem) < len(animation.stem) and (best is None or len(candidate.stem) > len(best.stem)):
            best = candidate
    return best


class Report:
    def __init__(self):
        self.counts = collections.Counter()
        self.violations = collections.defaultdict(list)

    def check(self, invariant: str, holds: bool, where: str):
        self.counts[f'{invariant}.checked'] += 1
        if not holds:
            self.violations[invariant].append(where)


def check_uv(report: Report, clip: Member, record: int, base: Member, where: str):
    mesh = base.mesh_record(clip.u16(record + 20))
    if mesh is None:
        report.counts['uv.unpaired_or_not_mesh'] += 1
        return
    block = clip.u32(record + 44)
    corners, pairs, total, ticks, keys = (clip.u32(block), clip.u32(block + 4), clip.u32(block + 8),
                                          clip.u32(block + 12), clip.u32(block + 16))
    clip_ticks = clip.u32(clip.u32(0x98) + 8)
    report.check('uv.corner_count_equals_mesh_corners', corners == base.u16(mesh + 94), where)
    listed = 0
    for corner in range(corners):
        first, count = clip.u16(pairs + 4 * corner), clip.u16(pairs + 4 * corner + 2)
        listed += count
        track = [clip.u16(ticks + 2 * (first + k)) for k in range(count)]
        report.check('uv.track_has_two_keys', count >= 2, where)
        report.check('uv.track_starts_at_tick_0', bool(track) and track[0] == 0, where)
        report.check('uv.ticks_strictly_increase', all(a < b for a, b in zip(track, track[1:])), where)
        report.check('uv.track_reaches_clip_end', bool(track) and track[-1] >= clip_ticks, where)
        clip.f32(keys + 8 * (first + count) - 4)  # last (u, v) inside the member
        if count >= 2:
            report.check('uv.model_holds_last_key_at_end',
                         anim_model.uv_key(track, float(track[-1])) == (count - 2, 1.0), where)
        report.counts['uv.corner_tracks'] += 1
    report.check('uv.key_total_equals_listed_keys', listed == total, where)
    report.counts['uv.paired_blocks'] += 1


def check_block12(report: Report, clip: Member, record: int, flags: int, base: Member, where: str):
    report.check('block12.has_vertex_flag_0x1000', bool(flags & 0x1000), where)
    mesh = base.mesh_record(clip.u16(record + 20))
    if mesh is None:
        report.counts['block12.unpaired_or_not_mesh'] += 1
        return
    block = clip.u32(record + 40)
    indices, count, floats = clip.u32(block), clip.u32(block + 4), clip.u32(block + 8)
    positions = base.u16(mesh + 88)
    report.check('block12.count_equals_mesh_positions', count == positions, where)
    report.check('block12.indices_in_range', all(clip.u32(indices + 4 * i) < positions for i in range(count)), where)
    clip.f32(floats + 12 * count - 4)  # count x 3 floats inside the member
    report.counts['block12.paired_records'] += 1


def run(root: Path) -> dict:
    report = Report()
    every = list(members(root))
    geometry = [m for m in every if m.version == CURRENT and m.u32(0x98) == 0]
    quickload = {}
    for path in root.rglob('qickload.txt'):
        quickload[path.parent.relative_to(root).as_posix()] = {
            line.strip().lower() for line in path.read_text(encoding='latin-1').splitlines() if line.strip()}

    for member in every:
        where = f'{member.directory}/{member.name}'
        report.check('member.magic', member.u32(0) == MAGIC, where)
        major, minor = member.version
        if (major, minor) == CURRENT:
            report.counts['member.current'] += 1
            continue
        report.counts['member.other_version'] += 1
        report.check('other_version.below_221', major < 221, where)
        report.check('other_version.refused_by_every_call_site',
                     not any(anim_model.loads(major, flags) for flags in LOADER_FLAGS), where)
        if not member.directory.lower().endswith('.wad'):
            report.check('other_version.loose_member_is_quickloaded',
                         member.name.lower() in quickload.get(member.directory, set()), where)
            report.counts['other_version.loose'] += 1

    for mesh_member in geometry:
        table = mesh_member.u32(0x70)
        for index in range(mesh_member.u16(0x44)):
            word = mesh_member.u32(table + 160 * index)
            report.check('geometry.stored_node_bits_0x4000_0x800_0x100_0x10_clear', not word & 0x4910,
                         f'{mesh_member.directory}/{mesh_member.name}#{index}')

    kinds = collections.Counter()
    for clip in (m for m in every if m.is_animation):
        base = pair(clip, geometry)
        bits_in_clip = 0
        for record in clip.records():
            flags = clip.u32(record + 4)
            where = f'{clip.directory}/{clip.name}@{record:#x}'
            report.counts['records'] += 1
            bits_in_clip |= flags
            report.check('record.bits_known', not flags & ~KNOWN_MASK, where)
            for bit, parent in PARENT_BIT.items():
                if flags & bit:
                    report.check('record.unread_bit_implies_read_parent', bool(flags & parent), where)
            for bit in anim_witness.READ_RECORD_BITS | anim_witness.UNREAD_RECORD_BITS:
                if flags & bit:
                    kinds[hex(bit)] += 1
            if base is None:
                continue
            if flags & 0x10000:
                check_uv(report, clip, record, base, where)
            if flags & 0x4000:
                check_block12(report, clip, record, flags, base, where)
        if clip.stem.startswith('advisor'):
            report.counts['advisor.clips'] += 1
            report.check('advisor.no_12_byte_records', not bits_in_clip & 0x4000, clip.name)
            if clip.directory.endswith('global/advisor.wad'):
                report.check('advisor.global_clips_use_decoded_kinds_only',
                             not bits_in_clip & ~ADVISOR_ALLOWED_MASK, clip.name)
            elif bits_in_clip & 0x600:
                report.counts['advisor.level_clips_with_path_records'] += 1
    for bit, count in kinds.items():
        report.counts[f'records.with_bit_{bit}'] = count

    return {
        'counts': dict(sorted(report.counts.items())),
        'violations': {key: value[:5] + ([f'... {len(value) - 5} more'] if len(value) > 5 else [])
                       for key, value in sorted(report.violations.items())},
        'ok': not report.violations,
    }


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__.splitlines()[2], file=sys.stderr)
        return 2
    result = run(Path(argv[1]))
    print(json.dumps(result, indent=2))
    return 0 if result['ok'] else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))
