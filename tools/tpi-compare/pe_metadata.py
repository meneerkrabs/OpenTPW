"""Bounded PE header/import/readable-name metadata; no code execution or unwrapping.

Microsoft PE specification: https://learn.microsoft.com/en-us/windows/win32/debug/pe-format
This reads standard disk metadata only and does not identify an engine version
from filenames, linker versions, import overlap, or protected executable stubs.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct


class MetadataError(ValueError):
    pass


def span(data, offset, size):
    if offset < 0 or size < 0 or offset + size > len(data):
        raise MetadataError('metadata span lies outside the file')
    return data[offset:offset + size]


def number(data, offset, fmt):
    span(data, offset, struct.calcsize(fmt))
    return struct.unpack_from(fmt, data, offset)[0]


def cstring(data, offset, limit=256):
    end = data.find(b'\0', offset, min(len(data), offset + limit))
    if offset < 0 or end < offset:
        raise MetadataError('unterminated bounded metadata string')
    try:
        return data[offset:end].decode('ascii')
    except UnicodeDecodeError as error:
        raise MetadataError('non-ASCII metadata name') from error


def inspect(data):
    if len(data) > 64 * 1024 * 1024:
        raise MetadataError('64 MiB readable PE metadata bound exceeded')
    if span(data, 0, 2) != b'MZ':
        raise MetadataError('DOS signature missing')
    pe = number(data, 0x3c, '<I')
    if span(data, pe, 4) != b'PE\0\0':
        raise MetadataError('PE signature missing; MZ alone is not a PE image')
    coff = pe + 4
    machine = number(data, coff, '<H')
    count = number(data, coff + 2, '<H')
    if count > 96:
        raise MetadataError('section count exceeds standard image bound')
    optional = coff + 20
    optional_size = number(data, coff + 16, '<H')
    span(data, optional, optional_size)
    magic = number(data, optional, '<H')
    if magic not in (0x10b, 0x20b):
        raise MetadataError('unsupported optional-header magic')
    is64 = magic == 0x20b
    directory_offset, directory_count_offset = (112, 108) if is64 else (96, 92)
    if optional_size < directory_offset:
        raise MetadataError('truncated optional header')
    directory_count = number(data, optional + directory_count_offset, '<I')
    if min(directory_count, 16) * 8 > optional_size - directory_offset:
        raise MetadataError('data directory array exceeds optional header')
    sections = []
    start = optional + optional_size
    for i in range(count):
        at = start + i * 40
        row = span(data, at, 40)
        sections.append({'name': row[:8].split(b'\0', 1)[0].decode('ascii', errors='replace'),
                         'virtual_size': number(data, at + 8, '<I'), 'rva': number(data, at + 12, '<I'),
                         'raw_size': number(data, at + 16, '<I'), 'raw_offset': number(data, at + 20, '<I')})
    header_size = number(data, optional + 60, '<I')

    def rva_offset(rva, width):
        matches = []
        if rva < header_size and rva + width <= min(header_size, len(data)):
            matches.append(rva)
        for section in sections:
            delta = rva - section['rva']
            if 0 <= delta and delta + width <= section['raw_size']:
                offset = section['raw_offset'] + delta
                span(data, offset, width)
                matches.append(offset)
        if len(matches) != 1:
            raise MetadataError('RVA lacks one unambiguous file-backed span')
        return matches[0]

    imports = []
    import_status = 'absent'
    import_error = None
    if directory_count > 1:
        directory = optional + directory_offset + 8
        import_rva = number(data, directory, '<I')
        import_size = number(data, directory + 4, '<I')
        if import_rva:
            try:
                terminated = False
                for i in range(min(import_size // 20, 512)):
                    at = rva_offset(import_rva + i * 20, 20)
                    words = struct.unpack('<IIIII', span(data, at, 20))
                    if not any(words):
                        terminated = True
                        break
                    lookup, _, _, name, first_thunk = words
                    library = cstring(data, rva_offset(name, 1))
                    thunk = lookup or first_thunk
                    width, fmt = (8, '<Q') if is64 else (4, '<I')
                    entries = []
                    for j in range(16384):
                        value = number(data, rva_offset(thunk + j * width, width), fmt)
                        if value == 0:
                            break
                        if value & (1 << (width * 8 - 1)):
                            entries.append({'ordinal': value & 0xffff})
                        else:
                            entries.append({'name': cstring(data, rva_offset(value, 3) + 2)})
                    else:
                        raise MetadataError('import thunk bound exceeded')
                    imports.append({'library': library, 'entries': entries})
                if not terminated:
                    raise MetadataError('import descriptors lack a bounded terminator')
                import_status = 'readable-standard-imports'
            except MetadataError as error:
                import_status, import_error = 'unreadable-metadata', str(error)
    type_names = sorted(set(m.group(1).decode('ascii') for m in re.finditer(rb'\.\?AV([A-Za-z_][A-Za-z0-9_]{1,127})@@', data)))
    plain_types = sorted(set(m.group(1).decode('ascii') for m in re.finditer(rb'(?:\0|^)(C[A-Z][A-Za-z0-9_]{2,100})\0', data)))
    source_names = sorted(set(m.group(1).decode('ascii') for m in re.finditer(rb'([A-Za-z_][A-Za-z0-9_]{1,80}\.(?:cpp|c|h))\0', data)))
    markers = {label: [m.start() for m in re.finditer(pattern, data, re.IGNORECASE)][:32]
               for label, pattern in {'SafeDisc-text': rb'SafeDisc', 'SecuROM-text': rb'SecuROM',
                                      'RSSEQ-text': rb'RSSEQ', 'TP2M-text': rb'TP2M', 'COS-text': rb'\bCOS\b'}.items()}
    return {'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data), 'machine': machine,
            'optional_magic': magic, 'coff_timestamp_raw': number(data, coff + 4, '<I'),
            'linker_version': [data[optional + 2], data[optional + 3]],
            'image_version': [number(data, optional + 44, '<H'), number(data, optional + 46, '<H')],
            'entry_rva': number(data, optional + 16, '<I'), 'sections': sections,
            'imports_status': import_status, 'imports_error': import_error, 'imports': imports,
            'msvc_type_name_candidates': type_names[:300], 'type_name_candidate_count': len(type_names),
            'plain_c_prefixed_name_candidates': plain_types[:300], 'plain_name_candidate_count': len(plain_types),
            'source_filename_candidates': source_names[:300], 'source_filename_candidate_count': len(source_names),
            'readable_markers': markers,
            'limitation': 'Headers/imports/type-name text do not establish engine version, executable equivalence, runtime behavior, or protection removal.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('paths', nargs='+', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    report = []
    for path in args.paths:
        try:
            if path.stat().st_size > 64 * 1024 * 1024:
                raise MetadataError('64 MiB readable PE metadata bound exceeded')
            if any(word in path.name.lower() for word in ('serial', 'license', 'eula')):
                raise MetadataError('user-requested serial/license content exclusion')
            report.append({'filename': path.name, 'metadata': inspect(path.read_bytes())})
        except (OSError, MetadataError) as error:
            report.append({'filename': path.name, 'status': 'unverified', 'reason': str(error)})
    result = json.dumps(report, indent=2, sort_keys=True)
    if args.output:
        args.output.write_text(result + '\n')
    else:
        print(result)


if __name__ == '__main__':
    main()
