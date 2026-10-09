"""Hash official disc readmes and inspect version labels without dumping text."""
import argparse
import hashlib
import json
from pathlib import Path
import re


def inspect(path):
    if any(word in path.name.lower() for word in ('serial', 'license', 'eula')):
        raise ValueError('serial/license content exclusion')
    if path.stat().st_size > 1024 * 1024:
        raise ValueError('readme exceeds 1 MiB text inspection bound')
    data = path.read_bytes()
    text = data.decode('latin1')
    return {'filename': path.name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
            'explicitProductEngineVersionCandidates': re.findall(
                r'(?:Theme\s+Park[^\r\n<]{0,40}|Engine)\s+(?:Version|v\.)\s*([0-9]+(?:\.[0-9]+)+)', text, re.I),
            'versionWordOccurrences': len(re.findall(r'\b(?:version|versie)\b', text, re.I)),
            'limitation': 'No source text dumped; titles/system requirements do not establish an engine build version'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('paths', nargs='+', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.write_text(json.dumps([inspect(path) for path in args.paths], indent=2) + '\n')


if __name__ == '__main__':
    main()
