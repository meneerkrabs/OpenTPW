import struct
import unittest

import pe_metadata as pe


def fixture():
    data = bytearray(1024)
    data[:2] = b'MZ'
    struct.pack_into('<I', data, 0x3c, 0x80)
    data[0x80:0x84] = b'PE\0\0'
    struct.pack_into('<HHI', data, 0x84, 0x14c, 1, 123)
    struct.pack_into('<H', data, 0x94, 224)
    opt = 0x98
    struct.pack_into('<H', data, opt, 0x10b)
    struct.pack_into('<I', data, opt + 60, 512)
    struct.pack_into('<I', data, opt + 92, 16)
    struct.pack_into('<II', data, opt + 104, 0x1000, 40)
    sec = opt + 224
    data[sec:sec+8] = b'.rdata\0\0'
    struct.pack_into('<IIII', data, sec + 8, 512, 0x1000, 512, 512)
    struct.pack_into('<IIIII', data, 512, 0x1040, 0, 0, 0x1080, 0x1060)
    struct.pack_into('<II', data, 0x240, 0x10a0, 0)
    data[0x280:0x28d] = b'KERNEL32.dll\0'
    data[0x2a0:0x2ae] = b'\0\0ExitProcess\0'
    data[0x2c0:0x2ce] = b'.?AVCVisitor@@'
    return data


class MetadataTests(unittest.TestCase):
    def test_file_backed_imports_and_type_name_candidate(self):
        report = pe.inspect(fixture())
        self.assertEqual(report['imports_status'], 'readable-standard-imports')
        self.assertEqual(report['imports'][0], {'library': 'KERNEL32.dll', 'entries': [{'name': 'ExitProcess'}]})
        self.assertEqual(report['msvc_type_name_candidates'], ['CVisitor'])

    def test_mz_is_not_enough(self):
        with self.assertRaises(pe.MetadataError):
            pe.inspect(b'MZ' + bytes(80))

    def test_truncated_optional_header(self):
        data = fixture()
        struct.pack_into('<H', data, 0x94, 20)
        with self.assertRaises(pe.MetadataError):
            pe.inspect(data)

    def test_unbacked_import_rva_is_reported_without_unwrapping(self):
        data = fixture()
        struct.pack_into('<I', data, 0x98 + 104, 0x4000)
        self.assertEqual(pe.inspect(data)['imports_status'], 'unreadable-metadata')

    def test_import_descriptor_bound_requires_terminator(self):
        data = fixture()
        struct.pack_into('<I', data, 0x98 + 108, 20)
        self.assertEqual(pe.inspect(data)['imports_status'], 'unreadable-metadata')

    def test_non_ascii_metadata_names_rejected(self):
        data = fixture()
        data[0x280] = 255
        self.assertEqual(pe.inspect(data)['imports_status'], 'unreadable-metadata')


if __name__ == '__main__':
    unittest.main()
