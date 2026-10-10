"""Summarize every bounded corpus member without emitting assets or text blobs."""
import argparse
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path
import re


def flatten(report):
    records = []
    for file in report['files']:
        if 'sha256' not in file:
            continue
        records.append(file)
        for member in (file.get('readerResult') or {}).get('sampledMembers', []):
            records.append({**member, 'path': file['path'] + '!' + member['path'], 'archiveMember': True})
    return records


def digest(records):
    text = '\n'.join(f"{r['path']}\t{r.get('bytes')}\t{r.get('sha256','not-hashed')}" for r in sorted(records, key=lambda r: r['path']))
    return hashlib.sha256(text.encode()).hexdigest()


def inventory(report):
    records = flatten(report)
    statuses = Counter()
    errors = []
    for record in records:
        result = record.get('readerResult') or record
        family = result.get('reader') or record.get('signature') or record.get('probeFamily') or 'unrecognized'
        status = result.get('status') or 'signature-only'
        statuses[(family, status)] += 1
        if status in ('reader-rejected', 'partial-reader-result', 'unverified-limit', 'preflight-rejected'):
            errors.append({'path': record['path'], 'sha256': record.get('sha256'), 'status': status,
                           'errorType': result.get('errorType'), 'reason': result.get('reason'),
                           'declaredEntries': result.get('declaredEntries'), 'parsedEntries': result.get('parsedEntries'),
                           'skippedEntries': len(result.get('skippedEntries', [])),
                           'skippedDiagnosticsSha256': hashlib.sha256('\n'.join(result.get('skippedEntries', [])).encode()).hexdigest()})
    return {'label': report['baselineLabel'], 'exhaustiveRequested': report.get('exhaustiveRequested'),
            'physicalFiles': report['fileCount'], 'archiveMembers': sum(bool(r.get('archiveMember')) for r in records),
            'hashedRecords': sum('sha256' in r for r in records), 'manifestSha256': digest(records),
            'statusCounts': [{'family': family, 'status': status, 'count': count} for (family, status), count in sorted(statuses.items())],
            'unsupportedOrPartial': errors, 'bounds': report['limits']}


def intersection(before, after):
    left, right = flatten(before), flatten(after)
    a, b = defaultdict(list), defaultdict(list)
    for row in left:
        if row.get('sha256'): a[row['sha256']].append(row['path'])
    for row in right:
        if row.get('sha256'): b[row['sha256']].append(row['path'])
    matches = [{'sha256': key, 'beforeOccurrences': len(a[key]), 'afterOccurrences': len(b[key])} for key in sorted(a.keys() & b.keys())]
    path_a = {r['path'].lower(): r for r in left if r.get('sha256')}
    path_b = {r['path'].lower(): r for r in right if r.get('sha256')}
    common_paths = sorted(path_a.keys() & path_b.keys())
    return {'labels': [before['baselineLabel'], after['baselineLabel']], 'uniqueEqualPayloads': len(matches),
            'equalBeforeOccurrences': sum(m['beforeOccurrences'] for m in matches),
            'equalAfterOccurrences': sum(m['afterOccurrences'] for m in matches),
            'commonLogicalPaths': len(common_paths), 'equalCommonPaths': sum(path_a[p]['sha256'] == path_b[p]['sha256'] for p in common_paths),
            'allEqualPayloadHashes': matches,
            'limitation': 'Exact payload equality across physical files and decompressed WAD members; duplicate occurrences are counted separately'}


def opcode_schema(source_root):
    enum_path = source_root / 'source/OpenTPW/VM/Opcode.cs'
    names = {int(value): name for name, value in re.findall(r'\b(\w+)\s*=\s*(\d+)', enum_path.read_text())}
    statuses = {}
    source_hashes = {str(enum_path.relative_to(source_root)): hashlib.sha256(enum_path.read_bytes()).hexdigest()}
    for path in sorted((source_root / 'source/OpenTPW/VM/Handlers').glob('*.cs')):
        source_hashes[str(path.relative_to(source_root))] = hashlib.sha256(path.read_bytes()).hexdigest()
        for name, status in re.findall(r'OpcodeHandler\(\s*Opcode\.(\w+)\s*,\s*RideOpcodeStatus\.(\w+)', path.read_text()):
            statuses[name] = status
    return names, statuses, source_hashes


def scripts(report, names, statuses):
    counts = Counter()
    files = Counter()
    missing_proofs = []
    for record in flatten(report):
        result = record.get('readerResult') or {}
        if result.get('reader') == 'RideScriptFile' and result.get('status') == 'parsed-existing-reader':
            for op, count in result['opcodeCounts'].items():
                counts[int(op)] += count
                files[int(op)] += 1
                if names.get(int(op)) not in statuses:
                    missing_proofs.append({'path': record['path'], 'sha256': record['sha256'], 'opcodeId': int(op), 'instructions': count})
    return {'label': report['baselineLabel'], 'opcodeCounts': [{'id': op, 'name': names.get(op), 'vmStatus': statuses.get(names.get(op)),
                                                              'instructions': count, 'scripts': files[op]} for op, count in sorted(counts.items())],
            'unknownEnumIds': sorted(op for op in counts if op not in names),
            'noRegisteredHandlerIds': sorted(op for op in counts if names.get(op) not in statuses),
            'missingHandlerProofs': missing_proofs,
            'limitation': 'VM declaration/handler status only; Hooked effects and TPI opcode semantics remain unqualified'}


def standard(report):
    return next(row for row in report['files'] if row['path'].lower() == 'data/levels/standard.sam')


def sam_difference(before, after):
    a, b = standard(before), standard(after)
    group = lambda row: {key: [v['valueHash'] for v in row['readerResult']['valueHashes'] if v['Key'] == key] for key in row['readerResult']['keys']}
    left, right = group(a), group(b)
    changes, families = [], Counter()
    for key in sorted(left.keys() | right.keys()):
        if left.get(key) == right.get(key): continue
        state = 'added' if key not in left else 'removed' if key not in right else 'value-hash-changed'
        families[(key.split('.')[0], state)] += 1
        changes.append({'key': key, 'state': state, 'beforeValueHashes': left.get(key), 'afterValueHashes': right.get(key)})
    return {'beforeSha256': a['sha256'], 'afterSha256': b['sha256'], 'equalKeysAndValueHashes': sum(left[k] == right[k] for k in left.keys() & right.keys()),
            'familyChanges': [{'family': family, 'state': state, 'keys': count} for (family, state), count in sorted(families.items())], 'changes': changes,
            'limitation': 'Normalized existing SAMParser first-value token; trailing/multi-value schema and runtime semantics are not asserted'}


def relevant_keys(report):
    rows = []
    for record in flatten(report):
        result = record.get('readerResult') or {}
        for entry in result.get('valueHashes') or []:
            if re.search(r'TrackInfo|Direction|Training|Experience', entry['Key'], re.I):
                rows.append({'path': record['path'], 'fileSha256': record['sha256'], **entry})
    return {'keyOccurrences': rows, 'trackInfoKeys': sorted(set(r['Key'] for r in rows if 'trackinfo' in r['Key'].lower())),
            'directionKeys': sorted(set(r['Key'] for r in rows if 'direction' in r['Key'].lower()))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['tpw', 'patch2', 'tpi', 'source-root', 'output']:
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--cos', type=Path)
    parser.add_argument('--readme', type=Path)
    args = parser.parse_args()
    tpw, patch, tpi = [json.loads(getattr(args, name).read_text()) for name in ['tpw', 'patch2', 'tpi']]
    names, statuses, hashes = opcode_schema(args.source_root)
    report = {'schema': 1, 'inventories': [inventory(r) for r in [tpw, patch, tpi]],
              'intersections': [intersection(tpw, patch), intersection(tpw, tpi), intersection(patch, tpi)],
              'opcodeSourceHashes': hashes, 'opcodes': [scripts(r, names, statuses) for r in [tpw, patch, tpi]],
              'standardSam': sam_difference(tpw, tpi), 'tpiTrackAndTrainingKeys': relevant_keys(tpi)}
    structural = Counter()
    examples = []
    for record in flatten(tpi):
        result = record.get('readerResult') or {}
        candidate = (result.get('structural') or {}).get('shpiDirectoryCandidate')
        if candidate:
            structural[(candidate['declaredLengthAt4'] == record['bytes'], candidate['tableFits'], candidate['offsetsInsideFile'])] += 1
            if len(examples) < 3:
                examples.append({'path': record['path'], 'sha256': record['sha256'], 'candidate': candidate})
    report['shpiStructures'] = {'observed': sum(structural.values()), 'consistencyCounts': [
        {'declaredLengthMatches': key[0], 'tableFits': key[1], 'offsetsInsideFile': key[2], 'files': count} for key, count in sorted(structural.items())],
        'examples': examples, 'limitation': 'Bounded candidate table consistency, not pixel decoding or proven entry semantics'}
    if args.cos:
        cos = json.loads(args.cos.read_text())
        records = flatten(cos)
        report['cosStructures'] = {'fileCount': len(records), 'manifestSha256': digest(records),
            'firstU32Counts': dict(Counter(str(((r.get('readerResult') or {}).get('structural') or {}).get('firstU32')) for r in records)),
            'tagOffsetPatterns': dict(Counter(json.dumps(((r.get('readerResult') or {}).get('structural') or {}).get('tagOffsets'), sort_keys=True) for r in records)),
            'readerStatusCounts': dict(Counter((r.get('readerResult') or {}).get('status') for r in records)),
            'allHashes': [{'path': r['path'], 'sha256': r['sha256'], 'bytes': r['bytes']} for r in records],
            'limitation': 'All coaster headers classified; no invented payload/track schema'}
    if args.readme:
        report['retailReadmeMetadata'] = json.loads(args.readme.read_text())
    args.output.write_text(json.dumps(report, sort_keys=True, indent=2) + '\n')


if __name__ == '__main__':
    main()
