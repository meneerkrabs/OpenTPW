"""Corpus-constrained COS/SHPI/SDT metadata only; never decode game/image/audio payloads."""
import argparse
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path
import struct


def sha(data):
    return hashlib.sha256(data).hexdigest()


def cos_profile(paths):
    rows, groups = [], defaultdict(list)
    for path in sorted(paths):
        if path.stat().st_size > 1024 * 1024:
            raise ValueError('COS metadata input exceeds 1 MiB bound')
        data = path.read_bytes()
        if len(data) < 140 or struct.unpack_from('<I', data)[0] != 2:
            raise ValueError('COS file violates the observed bounded magic/header contract')
        row = {'path': path.parent.name + '/' + path.name, 'bytes': len(data), 'sha256': sha(data),
               'headerRegion4To132Sha256': sha(data[4:132]), 'bodyFrom132Sha256': sha(data[132:]),
               'zeroWordsAt128And132': data[128:136] == bytes(8)}
        rows.append(row)
        groups[row['bodyFrom132Sha256']].append(row['path'])
    return {'fileCount': len(rows), 'languageIndependentBodyGroups': len(groups),
            'groupSizes': dict(Counter(len(paths) for paths in groups.values())),
            'groups': [{'bodySha256': h, 'paths': p} for h, p in sorted(groups.items())], 'files': rows,
            'limitation': 'Fixed header-region/body equality only; text encoding, record strides, track/cart meanings and thumbnail layout unresolved'}


def header_candidate(data, offset):
    if offset < 0 or offset + 40 > len(data):
        return None
    header, length = struct.unpack_from('<II', data, offset)
    name = data[offset+8:offset+24].split(b'\0', 1)[0]
    if header != 40 or length == 0 or offset + header + length > len(data):
        return None
    if not 5 <= len(name) <= 16 or not all(32 <= byte < 127 for byte in name):
        return None
    return {'offset': offset, 'headerBytes': header, 'dataBytes': length, 'end': offset + header + length,
            'nameSha256': sha(name), 'headerSha256': sha(data[offset:offset+40])}


def sdt_profile(path):
    if path.stat().st_size > 64 * 1024 * 1024:
        raise ValueError('SDT metadata input exceeds 64 MiB bound')
    data = path.read_bytes()
    if len(data) < 4:
        raise ValueError('SDT count missing')
    count = struct.unpack_from('<I', data)[0]
    if count > 65536 or 4 + count * 4 > len(data):
        raise ValueError('SDT directory count exceeds bounded input')
    offsets = struct.unpack_from('<' + 'I' * count, data, 4)
    indexed = [header_candidate(data, offset) for offset in offsets]
    first_bad = next((i for i, header in enumerate(indexed) if header is None), count)
    prefix = indexed[:first_bad]
    prefix_links = sum(h['end'] == offsets[i+1] for i, h in enumerate(prefix) if i+1 < count)
    candidates, at = [], offsets[first_bad] if first_bad < count else len(data)
    while True:
        at = data.find(b'\x28\0\0\0', at)
        if at < 0:
            break
        header = header_candidate(data, at)
        if header:
            candidates.append(header)
        at += 1
    candidate_offsets = {h['offset'] for h in candidates}
    chain_links = sum(h['end'] in candidate_offsets for h in candidates)
    return {'filename': path.name, 'world': path.parent.parent.name, 'bytes': len(data), 'sha256': sha(data),
            'declaredEntries': count, 'validIndexedPrefix': first_bad, 'prefixEndLinksMatchNextOffset': prefix_links,
            'firstBadDeclaredOffset': offsets[first_bad] if first_bad < count else None,
            'tailCandidate40ByteHeaders': len(candidates), 'candidateChainLinks': chain_links,
            'tailCandidates': candidates,
            'limitation': 'Candidate boundaries/names are hashed metadata, not a repaired directory or alternate audio decoder'}


def shpi_profile(report):
    formats, attachments, prefix = Counter(), Counter(), Counter()
    rows = []
    for file in report['files']:
        candidates = [(file, file.get('readerResult') or {})]
        candidates += [(member, member.get('readerResult') or {}) for member in (file.get('readerResult') or {}).get('sampledMembers', [])]
        for record, result in candidates:
            directory = (result.get('structural') or {}).get('shpiDirectoryCandidate') or {}
            for entry in directory.get('entryHeaders', []):
                formats[entry['formatByte']] += 1
                prefix[(entry['formatByte'], entry['refpackPrefix'])] += 1
                attachment = entry.get('attachmentHeader')
                if attachment:
                    attachments[(attachment['formatByte'], attachment['width16'], attachment['height16'])] += 1
                rows.append({'path': file['path'] + ('!' + record['path'] if record is not file else ''),
                             'sha256': record['sha256'], 'entry': entry})
    return {'entryCount': len(rows), 'formatBytes': dict(formats),
            'refpackPrefixCounts': [{'formatByte': k[0], 'matches': k[1], 'entries': n} for k, n in sorted(prefix.items())],
            'attachmentCandidates': [{'formatByte': k[0], 'width16': k[1], 'height16': k[2], 'entries': n} for k, n in sorted(attachments.items())],
            'examples': rows[:5],
            'primaryImplementation': 'https://github.com/bartlomiejduda/EA-Graphics-Manager/blob/dce358bc1d34102ea2c72b74210b6ca627a46cc4/src/EA_Image/dir_entry.py',
            'limitation': 'Source/corpus-consistent header labels only; palette channel order, mipmaps, pixel output and rendering remain unverified'}


def speech_overlap(music_path, speech_path, candidates):
    if max(music_path.stat().st_size, speech_path.stat().st_size) > 64 * 1024 * 1024:
        raise ValueError('speech/music overlap input exceeds 64 MiB bound')
    speech = speech_path.read_bytes()
    music = music_path.read_bytes()
    count = struct.unpack_from('<I', speech)[0]
    if count > 65536 or 4 + count * 4 > len(speech):
        raise ValueError('speech directory exceeds bounded input')
    headers, blocks = {}, {}
    for index, offset in enumerate(struct.unpack_from('<' + 'I'*count, speech, 4)):
        header = header_candidate(speech, offset)
        if header:
            headers[header['headerSha256']] = index
            blocks[sha(speech[offset:header['end']])] = (index, offset)
    matches = []
    for candidate in candidates:
        block_hash = sha(music[candidate['offset']:candidate['end']])
        if block_hash in blocks:
            index, offset = blocks[block_hash]
            matches.append({'musicOffset': candidate['offset'], 'musicEnd': candidate['end'],
                            'speechIndex': index, 'speechOffset': offset, 'completeBlockSha256': block_hash})
    return {'musicSha256': sha(music), 'speechSha256': sha(speech),
            'exactHeaderMatches': sum(c['headerSha256'] in headers for c in candidates),
            'completeBlockMatches': len(matches), 'matches': matches,
            'limitation': 'Exact indexed speech block equality inside music bytes; extraction/source cause is not established and no audio is decoded'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cos-root', type=Path, required=True)
    parser.add_argument('--sdt', type=Path, nargs='+', required=True)
    parser.add_argument('--shpi-report', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--speech', type=Path)
    args = parser.parse_args()
    report = {'cos': cos_profile(args.cos_root.rglob('*.cos')), 'sdt': [sdt_profile(path) for path in args.sdt],
              'shpi': shpi_profile(json.loads(args.shpi_report.read_text()))}
    if args.speech:
        report['speechInsideMusic'] = [speech_overlap(path, args.speech, entry['tailCandidates']) for path, entry in zip(args.sdt, report['sdt'])]
    args.output.write_text(json.dumps(report, sort_keys=True, indent=2) + '\n')


if __name__ == '__main__':
    main()
