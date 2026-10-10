"""Export identified original table slices and interpreted expectations OUTSIDE Git.

This is a private verification bridge for the standalone C# reader. Neither the
raw slices nor the generated metadata are repository fixtures.
"""
import argparse
import hashlib
import json
from pathlib import Path

from corpus import layout_table
from phase2 import APP_SHA, DIRECT_CALLS, STATUS_CALLS, identified, table_argument


def export(bin_root: Path, destination: Path, labels: dict | None = None):
    destination = destination.resolve()
    if any((parent / '.git').exists() for parent in [destination, *destination.parents]):
        raise ValueError('private table exports must be outside every Git worktree')
    app = identified(bin_root / 'SimThemePark.data', APP_SHA)
    data = bytes(app.data_section.data)
    offsets = set()
    for calls, target in [(DIRECT_CALLS, 0x181aac), (STATUS_CALLS, 0x1471f8)]:
        for load, call in calls:
            offsets.add(table_argument(app, load, call, target))
    destination.mkdir(parents=True, exist_ok=True)
    entries = []
    for offset in sorted(offsets):
        decoded = layout_table(data, offset)
        raw = data[offset:offset + decoded['words_consumed'] * 2]
        path = destination / f'table-{offset:06x}.private'
        path.write_bytes(raw)
        entries.append({'path': str(path), 'sourceDataOffset': offset,
                        'sha256': hashlib.sha256(raw).hexdigest(),
                        'wordsConsumed': decoded['words_consumed'],
                        'controls': decoded['windows'],
                        'externalProperties': decoded['external_parent_properties']})
    manifest = {'schemaVersion': 1, 'executableSha256': APP_SHA, 'tables': entries,
                'labelResources': labels or []}
    path = destination / 'manifest.private.json'
    path.write_text(json.dumps(manifest, sort_keys=True, indent=2) + '\n')
    return {'manifest': str(path), 'tables': len(entries),
            'controls': sum(len(entry['controls']) for entry in entries)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('destination', type=Path)
    parser.add_argument('--mac-uitext', type=Path)
    parser.add_argument('--mac-mbtouni', type=Path)
    parser.add_argument('--pc-uitext', type=Path)
    parser.add_argument('--pc-mbtouni', type=Path)
    args = parser.parse_args()
    labels = []
    for edition, text, characters in [('FeralMacAmerican', args.mac_uitext, args.mac_mbtouni),
                                      ('WindowsBaselineEnglish', args.pc_uitext, args.pc_mbtouni)]:
        if bool(text) != bool(characters):
            parser.error(f'supply both label files for {edition}')
        if text:
            labels.append({'edition': edition, 'strings': str(text.resolve()),
                           'characters': str(characters.resolve())})
    try:
        result = export(args.bin_root, args.destination, labels)
    except (OSError, ValueError) as error:
        parser.exit(1, f'private layout export: {error}\n')
    print(json.dumps(result, sort_keys=True))


if __name__ == '__main__':
    main()
