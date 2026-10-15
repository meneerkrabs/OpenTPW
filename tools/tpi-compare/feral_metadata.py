"""Reuse the validated PEF reader for a distinctly identified Feral Mac baseline."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'ppc-analysis'))
import pef


def inspect(path):
    if path.stat().st_size > pef.MAX_SECTION_SIZE:
        raise pef.PEFError('PEF input exceeds the existing reader size bound')
    container = pef.load(str(path))
    return {'filename': path.name, 'sha256': hashlib.sha256(container.raw).hexdigest(),
            'formatVersion': container.format_version, 'timestampRaw': container.timestamp,
            'sections': [{'index': s.index, 'kind': s.kind_name, 'bytes': s.total_size} for s in container.sections],
            'imports': len(container.imports), 'exports': len(container.exports),
            'libraries': [{'name': library.name, 'symbols': library.symbol_count} for library in container.libraries],
            'mainEntryVector': container.main,
            'limitation': 'CFM/PowerPC container metadata; no Windows executable or gameplay equivalence claim'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('paths', nargs='+', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.write_text(json.dumps([inspect(path) for path in args.paths], indent=2) + '\n')


if __name__ == '__main__':
    main()
