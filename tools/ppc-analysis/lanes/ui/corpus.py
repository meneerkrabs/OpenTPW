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
    records = []
    for index in range(node_count):
        record = mesh_offset + index * 160 if index < mesh_count else dummy_offset + (index - mesh_count) * 88
        span(data, record, 88)
        records.append(record)
        name_offset = struct.unpack('<I', span(data, record + 84, 4))[0]
        tail = span(data, name_offset, min(256, len(data) - name_offset))
        if 0 not in tail:
            raise ValueError('M3D2 node name')
        matrix = struct.unpack('<16f', span(data, record + 16, 64))
        nodes.append({'name': tail.split(b'\0', 1)[0].decode('ascii'),
                      'translation': list(matrix[12:15])})
    root = struct.unpack('<I', span(data, 0x78, 4))[0]
    if root not in records:
        raise ValueError('M3D2 root pointer')
    return {'sha256': hashlib.sha256(data).hexdigest(), 'nodes': nodes,
            'root_node_index': records.index(root),
            'bounds_min': list(struct.unpack('<3f', span(data, 0x80, 12))),
            'bounds_max': list(struct.unpack('<3f', span(data, 0x8c, 12)))}


def name_hash(name: str) -> int:
    """Original registry recurrence: (hash XOR signed byte) times 47, modulo 2^32."""
    value = 0
    for byte in name.encode('ascii'):
        value = ((value ^ byte) * 47) & 0xffffffff
    return value - 2**32 if value & 2**31 else value


def string_labels(strings: bytes, character_table: bytes, identifiers: list[int]) -> dict[int, str]:
    """Read selected BFST labels with their own BFMU; never dump a language table."""
    if len(strings) > LIMIT or span(strings, 0, 4) != b'BFST' or span(character_table, 0, 4) != b'BFMU':
        raise ValueError('BFST/BFMU identity or input limit')
    character_count = struct.unpack('<H', span(character_table, 6, 2))[0]
    if character_count > 255:
        raise ValueError('BFMU count exceeds byte index')
    characters = struct.unpack('<' + 'H' * character_count, span(character_table, 8, character_count * 2))
    count = struct.unpack('<I', span(strings, 8, 4))[0]
    if count > 8192 or len(identifiers) > 64:
        raise ValueError('selected string count limit')
    span(strings, 12, count * 4)
    labels = {}
    for identifier in identifiers:
        if not 0 <= identifier < count:
            raise ValueError('BFST identifier out of range')
        relative = struct.unpack('<I', span(strings, 12 + identifier * 4, 4))[0]
        position = 12 + relative
        require_marker = span(strings, position, 4)
        if require_marker[0] != 1:
            raise ValueError('BFST string marker')
        length = int.from_bytes(require_marker[1:], 'little')
        if length > 512:
            raise ValueError('selected label length limit')
        text = []
        for index in span(strings, position + 4, length):
            if not 1 <= index <= character_count:
                raise ValueError('BFMU character index')
            text.append(chr(characters[index - 1]))
        labels[identifier] = ''.join(text)
    return labels


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
    """Decode the established nineteen-command embedded layout stream.

    Geometry payload meanings remain neutral where only their consumption is
    proved. Implied child controls describe the interpreter's fresh-allocation
    path; original checks reject duplicates or incompatible parent types.
    """
    position, commands, windows, external_properties = offset, 0, [], {}

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

    def rectangle():
        return [signed(read()) for _ in range(4)]

    def node(at, kind, attributes, identifier, rect, parent, command):
        if len(windows) >= 512:
            raise ValueError('layout window budget exceeded')
        value = {'data_offset': at, 'type': kind, 'attributes': attributes,
                 'id': identifier, 'rectangle': rect,
                 'parent': parent['id'] if parent else None, 'origin_command': command}
        windows.append(value)
        return value

    def parent_required(parent):
        if parent is None:
            # The original caller can supply an already-existing parent. Keep
            # outer property commands separate; never invent its type or ID.
            return external_properties
        return parent

    def parse(parent, depth=0):
        if depth > 64:
            raise ValueError('layout nesting budget exceeded')
        while True:
            at = position
            command = read()
            if command == 0:
                kind, attributes, identifier = read(), pair(), pair()
                child = node(at, kind, attributes, identifier, rectangle(), parent, command)
                parse(child, depth + 1)
            elif command in (1, 2, 17, 18):
                parent_required(parent)[f'command_{command}'] = pair()
            elif command == 3:
                parent_required(parent)['command_3'] = rectangle()
            elif command == 4:
                subtype = read()
                if subtype in (1, 2, 3):
                    values = [signed(read()) for _ in range(3 if subtype == 2 else 4)]
                elif subtype == 4:
                    count = signed(read())
                    if not 0 <= count <= 512:
                        raise ValueError('layout polygon count limit')
                    values = [[signed(read()), signed(read())] for _ in range(count)]
                else:
                    raise ValueError('unsupported layout geometry subtype')
                parent_required(parent)['command_4'] = {'subtype': subtype, 'values': values}
            elif command in (6, 7, 8, 9, 12, 14, 15, 16):
                current = parent_required(parent)
                kinds = {6: 3, 7: 3, 8: 3, 9: (4, 7), 12: 7, 14: 12, 15: 12, 16: 13}
                valid = kinds[command]
                if current['type'] not in (valid if isinstance(valid, tuple) else (valid,)):
                    raise ValueError('layout implied child has incompatible parent type')
                if command in (12, 16):
                    number = signed(read())
                    if command == 16 and not 0 <= number < 32:
                        raise ValueError('layout light index limit')
                    identifier = number + 16 if command == 12 else 0x11100000 | number
                else:
                    identifier = {6: 1, 7: 2, 8: 3, 9: 1, 14: 1, 15: 2}[command]
                kind = 3 if command == 9 else 6 if command == 16 else 2
                attributes = 16 if command == 9 else 1 if command in (8, 12, 16) else 33
                child = node(at, kind, attributes, identifier, rectangle(), current, command)
                parse(child, depth + 1)
            elif command == 10:
                if parent_required(parent)['type'] not in (4, 7):
                    raise ValueError('layout command 10 parent type')
                parent['command_10'] = rectangle()
            elif command == 11:
                if parent_required(parent)['type'] != 7:
                    raise ValueError('layout command 11 parent type')
                count = signed(read())
                if not 0 <= count <= 512:
                    raise ValueError('layout value table count limit')
                parent['command_11'] = [[signed(read()), signed(read())] for _ in range(count)]
            elif command == 13:
                if parent_required(parent)['type'] != 11:
                    raise ValueError('layout command 13 parent type')
                parent['command_13'] = [signed(read()), signed(read())]
            elif command == 5:
                return
            else:
                raise ValueError(f'unsupported layout command {command} at data:{at:#x}')

    parse(None)
    return {'data_offset': offset, 'words_consumed': (position - offset) // 2,
            'windows': windows, 'external_parent_properties': external_properties}
