"""Bounded, read-only UI corpus witnesses. Never emits original payload bytes."""
from __future__ import annotations

import hashlib
from pathlib import Path
import struct

LIMIT = 32 * 1024 * 1024


def span(data: bytes, offset: int, size: int) -> bytes:
    if offset < 0 or size < 0 or offset + size > len(data):
        raise ValueError('out-of-range corpus span')
    return data[offset:offset + size]


def unpack(data: bytes) -> bytes:
    if span(data, 0, 2) != b'\x10\xfb':
        raise ValueError('unsupported RefPack header')
    expected = int.from_bytes(span(data, 2, 3), 'big')
    if expected > LIMIT:
        raise ValueError('RefPack output limit')
    pos, out = 5, bytearray()
    while True:
        command = span(data, pos, 1)[0]
        pos += 1
        stop, distance, count = False, 0, 0
        if command < 0x80:
            second = span(data, pos, 1)[0]
            pos += 1
            literals = command & 3
            count = ((command & 0x1c) >> 2) + 3
            distance = ((command & 0x60) << 3) + second + 1
        elif command < 0xc0:
            second, third = span(data, pos, 2)
            pos += 2
            literals, count = second >> 6, (command & 0x3f) + 4
            distance = ((second & 0x3f) << 8) + third + 1
        elif command < 0xe0:
            second, third, fourth = span(data, pos, 3)
            pos += 3
            literals, count = command & 3, ((command & 12) << 6) + fourth + 5
            distance = ((command & 16) << 12) + (second << 8) + third + 1
        elif command < 0xfc:
            literals = ((command & 31) + 1) * 4
        else:
            literals, stop = command & 3, True
        if len(out) + literals + count > expected:
            raise ValueError('RefPack expansion exceeds declared length')
        out.extend(span(data, pos, literals))
        pos += literals
        if count and not 0 < distance <= len(out):
            raise ValueError('RefPack invalid back reference')
        for _ in range(count):
            out.append(out[-distance])
        if stop:
            if len(out) != expected or pos != len(data):
                raise ValueError('RefPack terminal size mismatch')
            return bytes(out)


def wad(path: Path) -> dict[str, bytes]:
    data = path.read_bytes()
    if len(data) > LIMIT or span(data, 0, 4) != b'DWFB':
        raise ValueError('WAD identity or input limit')
    count = struct.unpack('<I', span(data, 72, 4))[0]
    if count > 8192:
        raise ValueError('WAD member limit')
    span(data, 88, count * 40)
    members, directory, total = {}, '', 0
    for i in range(count):
        _, name_offset, name_size, offset, size, compression, decoded = struct.unpack(
            '<7I', span(data, 88 + i * 40, 28))
        name = span(data, name_offset, name_size)
        if not name or name[-1] != 0:
            raise ValueError('WAD unterminated name')
        name = name[:-1].decode('ascii').replace('\\', '/')
        if '/' in name:
            directory, name = name.rsplit('/', 1)
        name = f'{directory}/{name}' if directory else name
        if name in members:
            raise ValueError('WAD duplicate member')
        payload = span(data, offset, size)
        if compression == 4:
            payload = unpack(payload)
            if len(payload) != decoded:
                raise ValueError('WAD decoded length mismatch')
        elif compression != 0:
            raise ValueError('WAD unsupported compression')
        total += len(payload)
        if total > LIMIT:
            raise ValueError('WAD aggregate output limit')
        members[name] = payload
    return members


def model_summary(data: bytes) -> dict:
    if struct.unpack('<3I', span(data, 0, 12)) != (0x1cd15d46, 221, 203):
        raise ValueError('unsupported M3D2 identity')
    mesh_count = struct.unpack('<H', span(data, 0x44, 2))[0]
    node_count = struct.unpack('<H', span(data, 0x42, 2))[0]
    mesh_offset, dummy_offset = struct.unpack('<2I', span(data, 0x70, 8))
    if not 0 < mesh_count <= node_count <= 512:
        raise ValueError('M3D2 node count')
    nodes = []
    for index in range(node_count):
        record = mesh_offset + index * 160 if index < mesh_count else dummy_offset + (index - mesh_count) * 88
        span(data, record, 88)
        name_offset = struct.unpack('<I', span(data, record + 84, 4))[0]
        tail = span(data, name_offset, min(256, len(data) - name_offset))
        if 0 not in tail:
            raise ValueError('M3D2 node name')
        matrix = struct.unpack('<16f', span(data, record + 16, 64))
        nodes.append({'name': tail.split(b'\0', 1)[0].decode('ascii'),
                      'translation': list(matrix[12:15])})
    return {'sha256': hashlib.sha256(data).hexdigest(), 'nodes': nodes,
            'bounds_min': list(struct.unpack('<3f', span(data, 0x80, 12))),
            'bounds_max': list(struct.unpack('<3f', span(data, 0x8c, 12)))}


def resource_index(data: bytes) -> dict[int, str]:
    if span(data, 0, 4) != b'BFRI':
        raise ValueError('resource index identity')
    count = struct.unpack('<I', span(data, 4, 4))[0]
    if count > 4096:
        raise ValueError('resource index count')
    span(data, 8, count * 12)
    result = {}
    for i in range(count):
        identifier, _, relative, _ = struct.unpack('<IHHI', span(data, 8 + i * 12, 12))
        offset = relative + 8
        name = span(data, offset, min(256, len(data) - offset))
        if 0 not in name or identifier in result:
            raise ValueError('resource index name or duplicate')
        result[identifier] = name.split(b'\0', 1)[0].decode('ascii')
    return result


def layout_table(data: bytes, offset: int) -> dict:
    """Decode only established commands of the original embedded short stream.

    Unsupported commands fail rather than guess their length. This is not a
    general UI layout decoder: the identified park-information table uses this
    bounded subset of the original nineteen-command interpreter.
    """
    position, commands, windows = offset, 0, []

    def read():
        nonlocal position, commands
        commands += 1
        if commands > 4096:
            raise ValueError('layout word budget exceeded')
        value = struct.unpack('>H', span(data, position, 2))[0]
        position += 2
        return value

    def signed(value):
        return value - 65536 if value & 32768 else value

    def pair():
        low, high = read(), read()
        value = low | high << 16
        return value - 2**32 if value & 2**31 else value

    def parse(parent, depth=0):
        if depth > 64:
            raise ValueError('layout nesting budget exceeded')
        while True:
            at = position
            command = read()
            if command == 0:
                kind, attributes, identifier = read(), pair(), pair()
                rectangle = [signed(read()) for _ in range(4)]
                if len(windows) >= 512:
                    raise ValueError('layout window budget exceeded')
                windows.append({'data_offset': at, 'type': kind,
                                'attributes': attributes, 'id': identifier,
                                'rectangle': rectangle, 'parent': parent})
                parse(identifier, depth + 1)
            elif command in (1, 2, 17, 18):
                pair()
            elif command == 3:
                for _ in range(4):
                    read()
            elif command == 5:
                return
            else:
                raise ValueError(f'unsupported layout command {command} at data:{at:#x}')

    parse(None)
    return {'data_offset': offset, 'words_consumed': (position - offset) // 2,
            'windows': windows}
