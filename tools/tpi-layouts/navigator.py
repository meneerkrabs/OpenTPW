"""T4 of the navigator port (docs/reverse/TPI-NAVIGATOR.md, NV2): the person navigator's serializer 0x63ad30 round trip.

A 177-byte record of distinct bytes is read by the load half (mode 0, version 1) into a navigator filled with 0xab, and the save half (mode 1)
writes it out again: the bytes must come back unchanged. Every transfer is listed with its record offset, size, member name (from the field-name
helper 0x441da0) and the navigator offset it fills, which is the layout SaveNavigation and GuestNavigation.ToRecord follow. The unnamed +0x30
must come out zero. Run: python3 navigator.py (needs TPI_GAME_EXE).
"""
import struct
import sys
from unicorn.x86_const import UC_X86_REG_ESP, UC_X86_REG_EAX
from emu import Emu
from models import THIS

SERIALIZER = 0x63AD30
RECORD = bytes((index * 37 + 11) & 0xFF for index in range(177))


def main():
    e = Emu()
    e.hooks[0x441da0] = e.h_name
    written = bytearray()

    def write():
        esp = e.reg(UC_X86_REG_ESP)
        data, size = e.r32(esp + 4), e.r32(esp + 8)
        written.extend(e.uc.mem_read(data, size))
        e.events.append((len(written) - size, size, data, 0, e.lastname))
        e.uc.reg_write(UC_X86_REG_EAX, size)
        e.ret(8)
    e.hooks[0x7f000110] = write
    e.uc.mem_write(THIS, b'\xab' * 0xC0)
    e.buf = RECORD
    loaded = e.call(SERIALIZER, (0x20000000, 0, 1, 0), ecx=THIS)
    reads = list(e.events)
    e.events.clear()
    saved = e.call(SERIALIZER, (0x20000000, 1, 1, 0), ecx=THIS)
    print(f'load half returned {loaded}, save half returned {saved}, failure {e.fail}')
    print('record offset  size  navigator offset  member')
    for (offset, size, dest, _, name) in reads:
        print(f'{offset:13d} {size:5d}  {dest - THIS:#16x}  {name}')
    unnamed = bytes(e.uc.mem_read(THIS + 0x30, 8))
    ok = loaded == 0 and saved == 0 and bytes(written) == RECORD and len(reads) == 33 and e.cur == 177 and unnamed == bytes(8)
    print(f'{len(reads)} reads of {e.cur} bytes; the save half wrote {len(written)} bytes, {"equal" if bytes(written) == RECORD else "DIFFERENT"}; +0x30 = {unnamed.hex()}')
    print('round trip ok' if ok else 'ROUND TRIP FAILED')
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
