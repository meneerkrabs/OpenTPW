"""Synthetic PEF fixtures only: no original bytes required or stored."""
import struct
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

import pef
from inventory import inventory


def container(body=b"abcd", kind=0, total=4, unpacked=4):
    header = b"Joy!peffpwpc" + struct.pack(">IIIIIHHI", 1, 0, 0, 0, 0, 1, 1, 0)
    section = struct.pack(">iIIIIIBBBB", -1, 0, total, unpacked, len(body), 68, kind, 1, 2, 0)
    return header + section + body


class ReaderTests(unittest.TestCase):
    def test_code_section(self):
        c = pef.PEFContainer(container())
        self.assertEqual(c.code.data, b"abcd")
        self.assertEqual(c.imports, [])

    def test_pidata_container_with_encoding_overhead(self):
        raw = container(body=b"\x21a", kind=2, total=1, unpacked=1)
        self.assertEqual(pef.PEFContainer(raw).data_section.data, b"a")

    def test_zero_fill(self):
        self.assertEqual(pef.PEFContainer(container(total=8)).code.data, b"abcd\0\0\0\0")

    def test_pidata_forms(self):
        cases = [(b"\x03", b"\0" * 3), (b"\x23abc", b"abc"),
                 (b"\x42\x02ab", b"ababab"),
                 (b"\x61\x01\x02axy", b"axaya"),
                 (b"\x81\x01\x02xy", b"\0x\0y\0"),
                 (b"\x00\x81\x00", b"\0" * 128)]
        for packed, expected in cases:
            with self.subTest(packed=packed):
                self.assertEqual(pef.unpack_pidata(packed, len(expected)), expected)

    def test_invalid_pidata(self):
        for packed, size in [(b"\x00\x80", 1), (b"\x23a", 3), (b"\x41", 1),
                             (b"\x03", 2), (b"\xa1", 1), (b"\x01", 2)]:
            with self.subTest(packed=packed):
                with self.assertRaises(pef.PEFError):
                    pef.unpack_pidata(packed, size)

    def test_truncated_headers_and_sections(self):
        raw = container()
        for length in range(len(raw)):
            with self.subTest(length=length):
                with self.assertRaises(pef.PEFError):
                    pef.PEFContainer(raw[:length])

    def test_invalid_sizes(self):
        for raw in [container(total=3), container(unpacked=3),
                    container(total=pef.MAX_SECTION_SIZE + 1), container(kind=4)]:
            with self.subTest(raw=raw[:12]):
                with self.assertRaises(pef.PEFError):
                    pef.PEFContainer(raw)

    def test_invalid_strings(self):
        for data, offset in [(b"a", 0), (b"a\0", -1), (b"a\0", 2)]:
            with self.assertRaises(pef.PEFError):
                pef._cstr(data, offset)

    def test_loader_symbols(self):
        c = pef.PEFContainer(container())
        header = struct.pack(">iIiIiIIIIIIIII", -1, 0, -1, 0, -1, 0,
                             1, 1, 0, 84, 102, 84, 0, 1)
        library = struct.pack(">IIIIIB3x", 0, 0, 0, 1, 0, 0)
        imported = struct.pack(">I", (2 << 24) | 4)
        export_tables = struct.pack(">III Ih", 0, 3 << 16, 8, 0, 0)
        loader = header + library + imported + export_tables + b"lib\0imp\0exp"
        c._parse_loader(loader)
        self.assertEqual(c.imports[0].name, "imp")
        self.assertEqual(c.imports[0].library, "lib")
        self.assertEqual(c.exports[0].name, "exp")
        self.assertEqual(c.exports[0].section, 0)
        broken = bytearray(loader)
        struct.pack_into(">I", broken, 68, 2)
        with self.assertRaises(pef.PEFError):
            pef.PEFContainer(container())._parse_loader(bytes(broken))
        with self.assertRaises(pef.PEFError):
            pef.PEFContainer(container())._parse_loader(loader[:100])

    def test_section_relocation(self):
        c = pef.PEFContainer(container(body=struct.pack(">I", 3)))
        result = c._run_relocs(0, [0x4000])
        self.assertEqual(result[0], pef.RelocTarget("section", 0, 3))

    def test_relocation_errors(self):
        c = pef.PEFContainer(container())
        for instructions in [[0xa000], [0x9000], [0x4a00], [0x6601], [0x4001], [0xffff]]:
            with self.subTest(instructions=instructions):
                with self.assertRaises(pef.PEFError):
                    c._run_relocs(0, instructions)
        with self.assertRaises(pef.PEFError):
            c._run_relocs(1, [])

    def test_inventory_is_deterministic_metadata(self):
        with TemporaryDirectory() as directory:
            path = Path(directory) / "synthetic.data"
            path.write_bytes(container())
            result = inventory(path)
            self.assertEqual(result, inventory(path))
            self.assertEqual(result["file"], "synthetic.data")
            self.assertEqual(len(result["sha256"]), 64)
            self.assertNotIn("data", result["sections"][0])


if __name__ == "__main__":
    unittest.main()
