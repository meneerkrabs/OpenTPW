"""Pin the full reproducible audit while retaining bounded review aggregates."""
import argparse
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path


def fingerprint(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def compact(full, raw, artifact_path, regeneration_command):
    report = {key: value for key, value in full.items()
              if key not in ('inventories', 'intersections', 'standardSam', 'tpiTrackAndTrainingKeys', 'cosStructures')}
    report['schema'] = 2
    report['fullAudit'] = {'bytes': len(raw), 'sha256': hashlib.sha256(raw).hexdigest(),
                         'artifactPath': str(artifact_path), 'regenerationCommand': regeneration_command,
                         'coverage': 'Every bounded physical file and decompressed WAD member; original assets remain external'}
    report['inventories'] = []
    for inventory in full['inventories']:
        row = {key: value for key, value in inventory.items() if key != 'unsupportedOrPartial'}
        diagnostics = inventory['unsupportedOrPartial']
        groups = defaultdict(list)
        for diagnostic in diagnostics:
            groups[(diagnostic['status'], diagnostic.get('errorType'))].append(diagnostic)
        row['diagnostics'] = {'count': len(diagnostics), 'recordsSha256': fingerprint(diagnostics),
                              'groups': [{'status': status, 'errorType': error_type, 'count': len(records),
                                          'examples': records[:3]} for (status, error_type), records in
                                         sorted(groups.items(), key=lambda item: str(item[0]))],
                              'allPartialReaderResults': [d for d in diagnostics if d['status'] == 'partial-reader-result']}
        report['inventories'].append(row)
    report['intersections'] = []
    for intersection in full['intersections']:
        records = intersection['allEqualPayloadHashes']
        report['intersections'].append({**{k: v for k, v in intersection.items() if k != 'allEqualPayloadHashes'},
                                        'equalPayloadRecordsSha256': fingerprint(records), 'equalPayloadRecordCount': len(records)})
    sam = full['standardSam']
    changes = sam['changes']
    report['standardSam'] = {**{k: v for k, v in sam.items() if k != 'changes'},
                             'changeCount': len(changes), 'changeRecordsSha256': fingerprint(changes),
                             'stateCounts': dict(sorted(Counter(r['state'] for r in changes).items()))}
    keys = full['tpiTrackAndTrainingKeys']
    rows = keys['keyOccurrences']
    report['tpiTrackAndTrainingKeys'] = {**{k: v for k, v in keys.items() if k != 'keyOccurrences'},
                                        'occurrenceCount': len(rows), 'occurrenceRecordsSha256': fingerprint(rows),
                                        'keyCounts': dict(sorted(Counter(r['Key'] for r in rows).items()))}
    if 'cosStructures' in full:
        cos = full['cosStructures']
        hashes = cos['allHashes']
        report['cosStructures'] = {**{k: v for k, v in cos.items() if k != 'allHashes'},
                                   'hashRecordCount': len(hashes), 'hashRecordsSha256': fingerprint(hashes)}
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--full', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--regeneration-command', required=True)
    args = parser.parse_args()
    raw = args.full.read_bytes()
    result = compact(json.loads(raw), raw, args.full.resolve(), args.regeneration_command)
    args.output.write_text(json.dumps(result, sort_keys=True, indent=2) + '\n')


if __name__ == '__main__':
    main()
