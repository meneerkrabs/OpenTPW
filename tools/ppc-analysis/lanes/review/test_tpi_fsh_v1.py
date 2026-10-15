"""TPI-FSH-V: independent check of the Theme Park Inc SHPI (.fsh) reader of commit 425169c.

This file holds its own minimal decoders, written for this review without reading FshFile's code paths into it:
a DWFB (WAD) directory walker, a RefPack decoder and an SHPI decoder. They are deliberately permissive where
FshFile is strict (any record order, any number of records) so that a disagreement shows up as a count or hash
difference instead of being hidden by the same assumption.

Synthetic tests always run. Corpus tests run only with OPENTPW_TPI_PATH naming the TPI retail ``Data`` folder (or
an install containing it); they re-derive the census in docs/FSH.md. With OPENTPW_FSH_DUMP naming a JSON-lines file
written by FshFile (one ``{"path", "images": [{"w","h","rgba"}]}`` or ``{"path","error"}`` per file), every decoded
RGBA hash is compared with this decoder. Nothing here says anything about how Theme Park Inc draws the textures.
"""
from __future__ import annotations

import collections
import hashlib
import json
import os
import struct
import unittest
from pathlib import Path


# ---------------------------------------------------------------- RefPack

def refpack(src: bytes, pos: int, size: int) -> bytes:
    """Decode RefPack commands starting at ``pos`` until the stop command; result must be exactly ``size`` bytes."""
    out = bytearray()

    def lit(n: int) -> None:
        nonlocal pos
        if pos + n > len(src):
            raise ValueError('literal past end')
        out.extend(src[pos:pos + n])
        pos += n

    def ref(dist: int, n: int) -> None:
        if dist > len(out):
            raise ValueError('reference before start')
        for _ in range(n):
            out.append(out[-dist])

    while True:
        if pos >= len(src):
            raise ValueError('no stop command')
        b0 = src[pos]
        if b0 < 0x80:
            b1 = src[pos + 1]
            pos += 2
            lit(b0 & 3)
            ref(((b0 & 0x60) << 3) + b1 + 1, ((b0 >> 2) & 7) + 3)
        elif b0 < 0xC0:
            b1, b2 = src[pos + 1], src[pos + 2]
            pos += 3
            lit(b1 >> 6)
            ref(((b1 & 0x3F) << 8) + b2 + 1, (b0 & 0x3F) + 4)
        elif b0 < 0xE0:
            b1, b2, b3 = src[pos + 1], src[pos + 2], src[pos + 3]
            pos += 4
            lit(b0 & 3)
            ref(((b0 & 0x10) << 12) + (b1 << 8) + b2 + 1, ((b0 & 0x0C) << 6) + b3 + 5)
        elif b0 < 0xFC:
            pos += 1
            lit(((b0 & 0x1F) + 1) * 4)
        else:
            pos += 1
            lit(b0 & 3)
            break
        if len(out) > size:
            raise ValueError('output past declared size')
    if len(out) != size:
        raise ValueError(f'decoded {len(out)} bytes, declared {size}')
    return bytes(out)


def refpack_with_header(src: bytes) -> bytes:
    flags = src[0]
    if src[1] != 0xFB:
        raise ValueError('no RefPack header')
    wide = 4 if flags & 0x80 else 3
    pos = 2 + (wide if flags & 0x01 else 0)
    size = int.from_bytes(src[pos:pos + wide], 'big')
    return refpack(src, pos + wide, size)


# ---------------------------------------------------------------- DWFB (WAD)

def wad_members(raw: bytes):
    """Yield (member path with '/', bytes) for every directory entry of a DWFB archive."""
    if raw[:4] != b'DWFB':
        raise ValueError('not DWFB')
    count = struct.unpack_from('<I', raw, 72)[0]
    directory = ''
    for index in range(count):
        base = 88 + 40 * index
        _, name_off, name_len, off, length, kind, unpacked = struct.unpack_from('<7I', raw, base)
        name = raw[name_off:name_off + name_len].split(b'\0')[0].decode('latin-1')
        if '\\' in name:
            directory, name = name.rsplit('\\', 1)
        path = f'{directory}/{name}'.replace('\\', '/') if directory else name
        data = raw[off:off + length]
        if kind == 4:
            data = refpack_with_header(data)
            if len(data) != unpacked:
                raise ValueError(f'{path}: unpacked {len(data)} != {unpacked}')
        elif kind != 0:
            raise ValueError(f'{path}: compression kind {kind}')
        yield path, data


# ---------------------------------------------------------------- SHPI

def expand5(v: int) -> int:
    v &= 31
    return (v << 3) | (v >> 2)


def palette_rgba(code: int, rec: bytes) -> tuple[list[bytes], int]:
    count, rows = struct.unpack_from('<HH', rec, 4)
    body = rec[16:]
    entries = []
    for i in range(count):
        if code == 0x24:
            r, g, b = body[3 * i:3 * i + 3]
            entries.append(bytes((r, g, b, 255)))
        elif code == 0x2A:
            b, g, r, a = body[4 * i:4 * i + 4]
            entries.append(bytes((r, g, b, a)))
        elif code == 0x2D:
            v = struct.unpack_from('<H', body, 2 * i)[0]
            entries.append(bytes((expand5(v >> 10), expand5(v >> 5), expand5(v), 255 if v & 0x8000 else 0)))
        else:
            raise ValueError(f'palette code {code:#x}')
    return entries, rows


def decode_fsh(data: bytes) -> dict:
    """Decode an SHPI file; returns header facts and per-image facts including the RGBA SHA-256."""
    if data[:4] != b'SHPI':
        raise ValueError('not SHPI')
    total, count = struct.unpack_from('<II', data, 4)
    ident = data[12:16].decode('latin-1')
    dirs = [(data[16 + 8 * i:20 + 8 * i].decode('latin-1'), struct.unpack_from('<I', data, 20 + 8 * i)[0])
            for i in range(count)]
    starts = sorted(o for _, o in dirs) + [len(data)]
    images = []
    for tag, off in dirs:
        bound = starts[starts.index(off) + 1]
        records = []  # (code, start, end)
        pos = off
        while True:
            code = data[pos]
            nxt = int.from_bytes(data[pos + 1:pos + 4], 'little')
            end = pos + nxt if nxt else bound
            records.append((code, pos, end))
            if not nxt:
                break
            pos = end
        code, start, end = records[0]
        w, h, cx, cy, px, py = struct.unpack_from('<6H', data, start + 4)
        n = w * h
        body = data[start + 16:end]
        info = {'tag': tag, 'w': w, 'h': h, 'code': code, 'words': (cx, cy, px, py),
                'attached': [c for c, _, _ in records[1:]], 'body_len': len(body)}
        if code == 0xFB:
            info['rp_flags'] = body[0]
            info['rp_size'] = int.from_bytes(body[2:5], 'big')
            unpacked = refpack(body, 5, info['rp_size'])
            info['tail'] = None
            indices = unpacked[:n]
            info['rp_extra'] = info['rp_size'] - n
        elif code == 0x7B:
            indices = body[:n]
            info['raw_extra'] = len(body) - n
        else:
            raise ValueError(f'image code {code:#x}')
        palette = None
        for c, s, e in records[1:]:
            if c in (0x24, 0x2A, 0x2D):
                palette, rows = palette_rgba(c, data[s:e])
                info['pal_code'], info['pal_entries'], info['pal_rows'] = c, len(palette), rows
            elif c == 0x70:
                info['name'] = data[s + 4:e].split(b'\0')[0].decode('latin-1')
        info['max_index'] = max(indices)
        if palette is not None:
            info['rgba'] = hashlib.sha256(b''.join(palette[i] for i in indices)).hexdigest()
        images.append(info)
    return {'total': total, 'length': len(data), 'count': count, 'id': ident, 'images': images}


def build_fsh(w: int, h: int, indices: bytes, pal_code: int, pal: bytes, entries: int,
              name: bytes | None = None, compressed: bytes | None = None) -> bytes:
    """Synthetic single-image SHPI builder (test helper)."""
    body = compressed if compressed is not None else indices
    body += b'\0' * (-len(body) % 16)
    image = struct.pack('<I6H', 0x7B | (0x80 if compressed is not None else 0) | (16 + len(body)) << 8, w, h, 0, 0, 0, 0)
    pal += b'\0' * (-len(pal) % 16)
    tail = b''
    if name is not None:
        tail = struct.pack('<I', 0x70) + name + b'\0'
    palette = struct.pack('<I6H', pal_code | ((16 + len(pal)) << 8 if tail else 0), entries, 1, entries, 0, 0, 0) + pal
    payload = image + body + palette + tail
    header_len = 16 + 8
    return b'SHPI' + struct.pack('<II', header_len + len(payload), 1) + b'G231' + b'test' + \
        struct.pack('<I', header_len) + payload


class Synthetic(unittest.TestCase):
    def test_refpack_literal_and_back_reference(self):
        # 4 literals "ABCD", then a 2-byte command copying 4 bytes from distance 4, then stop with 1 literal "Z".
        stream = bytes([0xE0]) + b'ABCD' + bytes([((4 - 3) << 2), 3]) + bytes([0xFD]) + b'Z'
        self.assertEqual(b'ABCDABCDZ', refpack(stream, 0, 9))
        with self.assertRaises(ValueError):
            refpack(stream, 0, 10)
        with self.assertRaises(ValueError):
            refpack(bytes([0x00, 0x00, 0xFC]), 0, 3)  # reference before any output

    def test_palette_orders(self):
        bgra = bytes([1, 2, 3, 4])
        self.assertEqual([bytes([3, 2, 1, 4])], palette_rgba(0x2A, struct.pack('<I6H', 0x2A, 1, 1, 1, 0, 0, 0) + bgra)[0])
        self.assertEqual([bytes([1, 2, 3, 255])],
                         palette_rgba(0x24, struct.pack('<I6H', 0x24, 1, 1, 1, 0, 0, 0) + bytes([1, 2, 3]))[0])
        self.assertEqual([bytes([255, 0, 0, 255])],
                         palette_rgba(0x2D, struct.pack('<I6H', 0x2D, 1, 1, 1, 0, 0, 0) + struct.pack('<H', 0xFC00))[0])

    def test_decode_raw_and_compressed_with_padding(self):
        w = h = 3  # 9 pixels, padded RefPack size 16
        indices = bytes([0, 1, 0, 1, 0, 1, 0, 1, 0])
        pal = bytes([10, 20, 30, 40, 50, 60])
        raw = decode_fsh(build_fsh(w, h, indices, 0x24, pal, 2, name=b'nm'))
        stream = bytes([0x10, 0xFB, 0, 0, 16, 0xE3]) + indices + bytes(7) + bytes([0xFC])
        packed = decode_fsh(build_fsh(w, h, indices, 0x24, pal, 2, name=b'nm', compressed=stream))
        self.assertEqual(raw['images'][0]['rgba'], packed['images'][0]['rgba'])
        self.assertEqual(7, packed['images'][0]['rp_extra'])
        self.assertEqual('nm', raw['images'][0]['name'])
        expected = hashlib.sha256(b''.join(bytes(pal[3 * i:3 * i + 3]) + b'\xff' for i in indices)).hexdigest()
        self.assertEqual(expected, raw['images'][0]['rgba'])


def tpi_data() -> Path | None:
    root = os.environ.get('OPENTPW_TPI_PATH')
    if not root:
        return None
    root = Path(root)
    for candidate in (root, root / 'Data', root / 'retail' / 'Data'):
        if (candidate / 'ui.wad').is_file():
            return candidate
    return None


def corpus(root: Path):
    """Yield (posix key, bytes) for every loose .fsh and every WAD member that is .fsh or starts with SHPI."""
    for path in sorted(root.rglob('*')):
        if not path.is_file():
            continue
        rel = path.relative_to(root).as_posix()
        if path.suffix.lower() == '.fsh':
            yield rel, path.read_bytes(), True
        elif path.suffix.lower() == '.wad':
            for member, data in wad_members(path.read_bytes()):
                named = member.lower().endswith('.fsh')
                if named or data[:4] == b'SHPI':
                    yield f'{rel}!{member}', data, named


@unittest.skipUnless(tpi_data(), 'OPENTPW_TPI_PATH not set to TPI retail Data')
class Corpus(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.files = {}
        cls.mismatch_named = []
        for key, data, named in corpus(tpi_data()):
            if not named or data[:4] != b'SHPI':
                cls.mismatch_named.append(key)
            if named:
                cls.files[key] = decode_fsh(data)
        cls.images = [(k, i) for k, f in cls.files.items() for i in f['images']]

    def test_counts(self):
        loose = sum('!' not in k for k in self.files)
        self.assertEqual((7283, 162, 7121), (len(self.files), loose, len(self.files) - loose))
        self.assertEqual([], self.mismatch_named)
        self.assertEqual({('G231', 1)}, {(f['id'], f['count']) for f in self.files.values()})
        self.assertTrue(all(f['total'] == f['length'] for f in self.files.values()))
        codes = collections.Counter(i['code'] for _, i in self.images)
        self.assertEqual({0x7B: 547, 0xFB: 6736}, dict(codes))
        pals = collections.Counter(i['pal_code'] for _, i in self.images)
        self.assertEqual({0x24: 4308, 0x2A: 2974, 0x2D: 1}, dict(pals))
        self.assertEqual({256}, {i['pal_entries'] for _, i in self.images if i['pal_code'] == 0x24})
        bgra = collections.Counter(i['pal_entries'] == 256 for _, i in self.images if i['pal_code'] == 0x2A)
        self.assertEqual((414, 2560), (bgra[True], bgra[False]))
        self.assertTrue(all(1 <= i['pal_entries'] <= 256 for _, i in self.images if i['pal_code'] == 0x2A))
        self.assertEqual({1}, {i['pal_rows'] for _, i in self.images})
        self.assertEqual(['levels/water/rides/snowtrac.wad!stexture/icewall1.fsh'],
                         [k for k, i in self.images if i['pal_code'] == 0x2D])
        attached = collections.Counter(tuple(sorted(i['attached'])) for _, i in self.images)
        self.assertEqual({0x24, 0x2A, 0x2D, 0x70}, {c for t in attached for c in t})
        self.assertTrue(all(sum(c in (0x24, 0x2A, 0x2D) for c in t) == 1 for t in attached))
        self.assertEqual(6649, sum(n for t, n in attached.items() if 0x70 in t))
        self.assertTrue(all(i['words'] == (0, 0, 0, 0) for _, i in self.images))
        self.assertTrue(all(i['max_index'] < i['pal_entries'] for _, i in self.images))

    def test_padding_rule_only_where_data_shows_it(self):
        packed = [i for _, i in self.images if i['code'] == 0xFB]
        self.assertEqual({0x10}, {i['rp_flags'] for i in packed})
        self.assertTrue(all(i['rp_size'] == (i['w'] * i['h'] + 15) // 16 * 16 for i in packed))
        padded = [i for i in packed if i['rp_extra']]
        self.assertEqual(135, len(padded))
        self.assertEqual({12}, {i['rp_extra'] for i in padded})
        self.assertTrue(all(i['rp_extra'] < i['w'] for i in padded))  # less than one row: rows are not padded
        raw = [i for _, i in self.images if i['code'] == 0x7B]
        self.assertTrue(all(i['raw_extra'] == 0 and i['w'] * i['h'] % 16 == 0 for i in raw))

    def test_layout_facts_in_docs(self):
        sizes = collections.Counter((i['w'], i['h']) for _, i in self.images)
        self.assertEqual(26, len(sizes))
        self.assertEqual([((32, 32), 3001), ((128, 128), 2009), ((64, 64), 1840)], sizes.most_common(3))
        self.assertEqual([(250, 250), (384, 344)], sorted(sizes, key=lambda s: s[0] * s[1])[-2:])
        tags = sum(i['tag'].lower() == k.split('!')[-1].split('/')[-1][:4].lower() for k, i in self.images)
        self.assertEqual(7101, tags)
        # Palette always directly follows the image; the name, when present, follows the palette.
        orders = collections.Counter(tuple(i['attached']) for _, i in self.images)
        self.assertEqual({(0x24, 0x70): 3824, (0x2A, 0x70): 2824, (0x24,): 484, (0x2A,): 150, (0x2D, 0x70): 1},
                         dict(orders))

    def test_fixture_hashes_from_docs(self):
        expected = {
            'generic/shadow/alphkid.fsh': 'd9adb81b8d57c9a4f34c4ea0592e6f47b04e9eae4736a6eabacccddfbe907bd9',
            'levels/water/rides/snowtrac.wad!stexture/icewall1.fsh':
                'aefb6b56c12487f023746a8b057edda235ac5ce59189af41c50bea2b8eb57b4e',
            'ui.wad!stexture/tb_camera.fsh': '75d874e1d3fc656e115c7626e796ac8b94a948963f5d4244be277434f5c3d9e7',
            'levels/arabian/Sharetex.wad!an_g07.fsh': '35175b6940013fd3c99f1494e5741d1f839c6d7735f0637a99919fc4ca409baa',
            'global/Advisor/textures/Mutant_Eye.fsh': '1f0bd89426b4cf12b72316f1b1c3967821355c70d6e4ecb806219111dfaf3b1a',
        }
        lower = {k.lower(): f for k, f in self.files.items()}
        for key, digest in expected.items():
            with self.subTest(key):
                self.assertEqual(digest, lower[key.lower()]['images'][0]['rgba'])

    @unittest.skipUnless(os.environ.get('OPENTPW_FSH_DUMP'), 'OPENTPW_FSH_DUMP not set')
    def test_every_hash_matches_fshfile_dump(self):
        dump = {}
        with open(os.environ['OPENTPW_FSH_DUMP'], encoding='utf-8') as handle:
            for line in handle:
                row = json.loads(line)
                dump[row['path']] = row
        self.assertEqual(set(self.files), set(dump))
        mismatches = [k for k, f in self.files.items()
                      if 'error' in dump[k] or [(i['w'], i['h'], i['rgba']) for i in f['images']]
                      != [(i['w'], i['h'], i['rgba']) for i in dump[k]['images']]]
        self.assertEqual([], mismatches)


if __name__ == '__main__':
    unittest.main()
