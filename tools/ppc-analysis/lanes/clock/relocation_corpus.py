"""Compare complete PEF relocation metadata with the pre-fix corpus baseline.

The checked-in baseline contains binary identities, counts and metadata digests
only. No binary contents, original instructions or raw disassembly are emitted.
"""
import argparse
import hashlib
import json
from pathlib import Path

import clock_evidence
import pef


def inspect(root: Path) -> dict:
    baseline = json.loads(Path(__file__).with_name('relocation_baseline.json').read_text())
    actual = []
    for entry in baseline:
        path = root / entry['file']
        raw = path.read_bytes()
        clock_evidence.timer.require(hashlib.sha256(raw).hexdigest(), entry['sha256'],
                                     f'identity of {entry["file"]}')
        container = pef.PEFContainer(raw, path.name)
        rows = [(section, offset, target.kind, target.target, target.addend)
                for section, relocations in sorted(container.relocs.items())
                for offset, target in sorted(relocations.items())]
        digest = hashlib.sha256(json.dumps(rows, separators=(',', ':')).encode()).hexdigest()
        clock_evidence.timer.require((len(rows), digest),
                                     (entry['relocations'], entry['relocation_metadata_sha256']),
                                     f'complete relocation metadata of {entry["file"]}')
        actual.append({'file': entry['file'], 'relocations': len(rows),
                       'relocation_metadata_sha256': digest})
    return {'containers': len(actual), 'relocations': sum(row['relocations'] for row in actual),
            'pre_fix_metadata_matches': True, 'files': actual}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.bin_root), indent=2, sort_keys=True))
    except (OSError, pef.PEFError) as error:
        parser.exit(1, f'relocation corpus: {error}\n')


if __name__ == '__main__':
    main()
