"""Compare scenario-relevant Feral Mac data files with the Windows baseline and Patch 2.

Inputs are directories, so the original files stay outside the repository. The
Mac directory is the HFS volume's ``Theme Park Data:data`` folder copied out with
its data forks (for example with hfsutils ``hcopy -r``). Output is interpreted
metadata only: relative paths, SHA-256 identities, equality classes, selected
``global.sam`` integers and string-table index alignment. No file contents are
printed.
"""
from __future__ import annotations

import argparse
import difflib
import hashlib
import json
from pathlib import Path
import struct

THEMES = ('jungle', 'hallow', 'fantasy', 'space')
FILES = ['Challenges.sam', 'Advisor/Advisor.sam', 'levels/Standard.sam', 'levels/Online_Standard.sam']
for _theme in THEMES:
    FILES += [f'levels/{_theme}/{name}' for name in ('global.sam', 'Standard.sam', 'Online_Standard.sam')]
FILES += ['levels/jungle/Easy_Standard.sam', 'levels/jungle/Easymode.TPWI']
MAX_FILE = 64 * 1024 * 1024


def find(root: Path, relative: str) -> Path | None:
    """Case-insensitive lookup; also accepts the file directly under ``root``."""
    candidates = [relative, relative.split('/')[-1]]
    for candidate in candidates:
        node = root
        for part in candidate.split('/'):
            if not node.is_dir():
                node = None
                break
            match = next((child for child in node.iterdir() if child.name.lower() == part.lower()), None)
            if match is None:
                node = None
                break
            node = match
        if node is not None and node.is_file():
            return node
    return None


def read(path: Path) -> bytes:
    if path.stat().st_size > MAX_FILE:
        raise ValueError(f'{path.name} exceeds the comparison size limit')
    return path.read_bytes()


def sam_ints(raw: bytes, keys: tuple[str, ...]) -> dict:
    values = {}
    for line in raw.decode('latin-1').splitlines():
        fields = line.split('#', 1)[0].split()
        if len(fields) >= 2 and fields[0] in keys:
            values[fields[0]] = int(fields[1])
    return values


def bfmu(raw: bytes) -> list[str]:
    if raw[:4] != b'BFMU' or len(raw) < 8:
        raise ValueError('character table magic')
    count = struct.unpack_from('<H', raw, 6)[0]
    if 8 + 2 * count > len(raw):
        raise ValueError('character table size')
    return [chr(struct.unpack_from('<H', raw, 8 + 2 * i)[0]) for i in range(count)]


def bfst(raw: bytes, table: list[str]) -> list[str]:
    if raw[:4] != b'BFST' or len(raw) < 12:
        raise ValueError('string table magic')
    count = struct.unpack_from('<i', raw, 8)[0]
    if count < 0 or 12 + 4 * count > len(raw):
        raise ValueError('string table count')
    out = []
    for i in range(count):
        at = 12 + struct.unpack_from('<i', raw, 12 + 4 * i)[0]
        if at < 12 or at + 4 > len(raw) or raw[at] != 1:
            raise ValueError(f'string {i} record')
        length = raw[at + 1] | raw[at + 2] << 8 | raw[at + 3] << 16
        body = raw[at + 4:at + 4 + length]
        if len(body) != length or any(not 1 <= b <= len(table) for b in body):
            raise ValueError(f'string {i} body')
        out.append(''.join(table[b - 1] for b in body))
    return out


def alignment(old: list[str], new: list[str]) -> list[dict]:
    """Index-shifting edits between two string tables (equal-length replacements are wording only)."""
    edits = []
    matcher = difflib.SequenceMatcher(None, old, new, autojunk=False)
    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag != 'equal' and i2 - i1 != j2 - j1:
            edits.append({'edit': tag, 'windows_range': [i1, i2], 'mac_range': [j1, j2]})
    return edits


def compare(mac: Path, windows: Path, patch2: Path | None) -> dict:
    files = {}
    for relative in FILES:
        m, w = find(mac, relative), find(windows, relative)
        p = find(patch2, relative) if patch2 else None
        if m is None or w is None:
            files[relative] = {'status': 'missing', 'mac': m is not None, 'windows': w is not None}
            continue
        mb, wb = read(m), read(w)
        pb = read(p) if p else None
        status = 'same_as_windows' if mb == wb else ('same_as_patch2' if pb is not None and mb == pb else 'different')
        files[relative] = {'status': status, 'mac_sha256': hashlib.sha256(mb).hexdigest()}
        if pb is not None:
            files[relative]['patch2_equals_windows'] = pb == wb
        if relative.endswith('global.sam'):
            files[relative]['values'] = sam_ints(mb, ('Tickets.CanEarnTicketsInTheme', 'Tickets.CanSpendTicketsInTheme',
                                                      'Keys.CostToEnter', 'ParkName.GateObjectId'))
    result = {'files': files}
    mac_lang = find(mac, 'Language/American/UITEXT.str')
    win_lang = find(windows, 'Language/English/UITEXT.str')
    if mac_lang and win_lang:
        mac_table = bfmu(read(mac_lang.parent / 'MBToUni.dat'))
        win_table = bfmu(read(win_lang.parent / 'MBToUni.dat'))
        tables = {}
        for name in ('UITEXT', 'TAG_SYSTEM', 'UIHELPTEXT', 'THEMENAMES', 'STAFFSTATES'):
            ms, ws = find(mac_lang.parent, f'{name}.str'), find(win_lang.parent, f'{name}.str')
            if ms and ws:
                mac_strings, win_strings = bfst(read(ms), mac_table), bfst(read(ws), win_table)
                tables[name] = {'mac_count': len(mac_strings), 'windows_count': len(win_strings),
                                'index_shifts': alignment(win_strings, mac_strings)}
        result['string_tables_mac_american_vs_windows_english'] = tables
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mac_data', type=Path)
    parser.add_argument('windows_data', type=Path)
    parser.add_argument('--patch2-data', type=Path)
    args = parser.parse_args()
    try:
        result = compare(args.mac_data, args.windows_data, args.patch2_data)
    except (OSError, ValueError) as error:
        parser.exit(1, f'mac data compare: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()
