"""Produce compact comparison evidence from existing-reader metadata reports."""
import argparse
from collections import Counter
import json
from pathlib import Path


def outcomes(report):
    physical, members = Counter(), Counter()
    for file in report['files']:
        result = file.get('readerResult') or {}
        if result:
            physical[(file['probeFamily'], result['status'])] += 1
        for member in result.get('sampledMembers', []):
            parsed = member.get('readerResult') or member
            members[(member.get('signature', 'not-read'), parsed['status'])] += 1
    rows = lambda counts: [{'family': key[0], 'status': key[1], 'count': value} for key, value in sorted(counts.items())]
    return {'label': report['baselineLabel'], 'fileCount': report['fileCount'], 'signatureCounts': report['signatureCounts'],
            'physicalReaderOutcomes': rows(physical), 'memberReaderOutcomes': rows(members), 'probeLimits': report['limits']}


def same_paths(before, after):
    left = {f['path'].lower(): f for f in before['files'] if 'sha256' in f}
    right = {f['path'].lower(): f for f in after['files'] if 'sha256' in f}
    common = sorted(left.keys() & right.keys())
    equal = [name for name in common if left[name]['sha256'] == right[name]['sha256']]
    return {'comparison': [before['baselineLabel'], after['baselineLabel']], 'commonPaths': len(common),
            'equalHashes': len(equal), 'changedHashes': len(common) - len(equal),
            'equalExamples': [{'path': name, 'sha256': left[name]['sha256']} for name in equal[:12]],
            'limitation': 'Common physical paths only; does not match renamed assets or prove runtime behavior'}


def selected(report):
    rows = []
    for file in report['files']:
        result = file.get('readerResult') or {}
        if file['path'].lower().endswith(('levels/standard.sam', 'global/speech/speechhd.sdt', 'dynamic/garrow.md2')):
            compact = {key: value for key, value in result.items() if key != 'keys'}
            if 'keys' in result:
                compact['distinctKeyCount'] = len(result['keys'])
            rows.append({**{k: file[k] for k in ['path', 'bytes', 'sha256', 'signature']}, 'readerResult': compact})
        for member in result.get('sampledMembers', []):
            parsed = member.get('readerResult') or {}
            if member.get('signature') == 'TP2M' or member.get('path', '').lower().endswith(('animctrl.rse', 'bus.rse')):
                rows.append({'path': file['path'] + '!' + member['path'], **{k: member[k] for k in ['bytes', 'sha256', 'signature'] if k in member}, 'readerResult': parsed})
    return {'label': report['baselineLabel'], 'proofs': rows}


def schema_difference(before, after):
    path = 'data/levels/standard.sam'
    find = lambda r: next(f for f in r['files'] if f['path'].lower() == path)
    left, right = find(before), find(after)
    a, b = set(left['readerResult']['keys']), set(right['readerResult']['keys'])
    return {'path': path, 'beforeSha256': left['sha256'], 'afterSha256': right['sha256'],
            'beforeEntryCount': left['readerResult']['entryCount'], 'afterEntryCount': right['readerResult']['entryCount'],
            'sharedKeys': len(a & b), 'beforeOnlyKeys': len(a - b), 'afterOnlyKeys': len(b - a),
            'afterOnlyExamples': sorted(b - a)[:24],
            'limitation': 'Existing SAMParser keeps one value token; key coverage is not complete multi-value schema or gameplay compatibility'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['tpw', 'patch2', 'tpi', 'cos', 'pe', 'feral', 'output']:
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    load = lambda name: json.loads(getattr(args, name).read_text())
    tpw, patch, tpi, cos = [load(name) for name in ['tpw', 'patch2', 'tpi', 'cos']]
    report = {'schema': 1, 'scope': 'Container metadata and bounded reader outcomes; engine/runtime equivalence unverified',
              'baselines': [outcomes(r) for r in [tpw, patch, tpi, cos]],
              'physicalComparisons': [same_paths(tpw, patch), same_paths(tpw, tpi)],
              'standardSamSchema': schema_difference(tpw, tpi),
              'selectedProofs': [selected(r) for r in [tpw, patch, tpi]],
              'cosProofs': cos['files'][:3], 'executableMetadata': load('pe'), 'feralMetadata': load('feral')}
    args.output.write_text(json.dumps(report, sort_keys=True, indent=2) + '\n')


if __name__ == '__main__':
    main()
