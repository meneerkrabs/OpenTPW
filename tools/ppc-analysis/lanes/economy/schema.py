"""Bounded metadata interpretation of the two proven economy SAM tables.

The address calculation follows statically inspected table initialization.
It interprets descriptors, not original instructions, and emits selected
names/offsets only. Nested arrays and unknown descriptor forms are rejected.
"""
from __future__ import annotations

import struct


def fields(data: bytes, base: int, embedded_offset: int = 0) -> list[dict]:
    cursor = 1
    stack = [[]]
    frames = []
    for index in range(400):
        offset = base + index * 60
        if offset < 0 or offset + 60 > len(data):
            raise ValueError('descriptor outside section')
        kind = struct.unpack_from('>I', data, offset)[0]
        name_raw = data[offset + 4:offset + 36]
        if b'\0' not in name_raw:
            raise ValueError('unterminated descriptor name')
        name = name_raw.split(b'\0', 1)[0].decode('ascii')
        count = struct.unpack_from('>I', data, offset + 52)[0]
        if kind in (0, 2):
            if len(frames) >= 16 or (kind == 2 and any(frame[0] == 2 for frame in frames)):
                raise ValueError('unsupported nested/deep structure')
            stack.append([])
            frames.append((kind, cursor, index))
        elif kind in (1, 3):
            if not frames:
                raise ValueError('unmatched descriptor close')
            children = stack.pop()
            opening, first_slot, first_index = frames.pop()
            if (opening, kind) not in ((0, 1), (2, 3)):
                raise ValueError('mismatched descriptor close')
            if kind == 3:
                stride = index - first_index - 1
                if not 1 <= count <= 100 or stride != len(children) or cursor - first_slot != stride:
                    raise ValueError('unsupported array shape/count')
                for row in children:
                    row['array_stride'] = 4 * stride
                    row['array_capacity'] = count
                # The original builder reserves one additional count word.
                cursor += (count - 1) * stride + 1
            for row in children:
                row['path'] = name + '.' + row['path']
            stack[-1].extend(children)
        elif kind == 12:
            if frames:
                raise ValueError('unclosed descriptor scope')
            return stack[0]
        elif 4 <= kind <= 11:
            if cursor > 16384:
                raise ValueError('descriptor destination exceeds lane bound')
            row = {'path': name, 'descriptor_offset': offset, 'descriptor_index': index,
                   'kind': kind, 'runtime_offset': embedded_offset + 8 + 4 * cursor}
            if kind == 6:
                row['minimum'] = struct.unpack_from('>i', data, offset + 36)[0]
                row['maximum'] = struct.unpack_from('>i', data, offset + 40)[0]
            stack[-1].append(row)
            cursor += 1
        else:
            raise ValueError('unsupported descriptor kind')
    raise ValueError('missing bounded table terminator')
