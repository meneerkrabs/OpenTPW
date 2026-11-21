"""Replay the TPI storm timers with synthetic strike coordinates and scripted draws.

The coordinate producer, renderer, audio sink and diagnostic printer are
stubbed. The native timers, strike dispatch, thunder position and RNG call
order run from 0x6422FE up to the debug-only branch at 0x642437.
"""
import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from x86_harness import Image, Sandbox, executable_path  # noqa: E402

CASES = (
    (False, 19, 3, 0), (False, 19, 3, 1), (False, 19, 3, 2),
    (True, 11, 3, 2), (True, 10, 3, 2), (True, 9, 3, 2),
    (True, 9, 3, 0), (True, 9, 10, -1), (True, 9, 11, -1),
)


def rows(image):
    journal = []
    draws = iter(())

    def random(sb):
        journal.append('R')
        return next(draws), 0

    def geometry(sb):
        journal.append('G')
        for arg, point in enumerate(((21, 0, 31), (22, 300, 32))):
            for index, value in enumerate(point):
                sb.write(sb.arg(arg) + index * 4, 'f', value)
        return 0, 12

    def strike(sb):
        journal.append('S')
        return 0, 32

    def thunder(sb):
        journal.append('T')
        assert sb.arg(2) == 0x20
        return 0, 0

    sb = Sandbox(image, [(0x6422FE, 0x642437), (0x86FAB0, 0x86FAD7)], stubs={
        0x4075BD: geometry, 0x401640: strike, 0x40A92F: random,
        0x403E45: lambda sb: (0, 0), 0x402DAB: thunder,
    }, budget=1000)
    for storm, lightning, bolts, countdown in CASES:
        sb.reset()
        journal.clear()
        draws = iter((17, 29))
        weather = sb.object(0x80, {0x4C: ('B', storm), 0x50: ('i', lightning), 0x54: ('i', bolts),
                                   0x58: ('i', countdown), 0x5C: ('f', 11), 0x64: ('f', 12)})
        for address, value in ((0xA0252C, 10), (0xA02530, 500), (0xA02534, 500), (0xA02538, 10), (0xA0253C, 2)):
            sb.write(address, 'i', value)
        sb.call(0x6422FE, regs={'esi': weather, 'ebx': 0}, stop=0x642437)
        values = (str(storm).lower(), lightning, bolts, countdown,
                  *(sb.read(weather + off, 'i') for off in (0x50, 0x54, 0x58)),
                  *(int(sb.read(weather + off, 'f')) for off in (0x5C, 0x64)),
                  '"' + ''.join(journal) + '"')
        yield '[DataRow(' + ', '.join(map(str, values)) + ')]'


def geometry_rows(image):
    draws = iter(())
    sb = Sandbox(image, [(0x642550, 0x6426F9)], stubs={0x40A92F: lambda _: (next(draws), 0)}, budget=500)
    for control in (0x7F, 0x27F):
        for width, height, words in (
            (128, 128, (0, 0, 0, 0)),
            (128, 128, (0xFFFFFFFF, 0x7FFFFFFF, 0xBFFFFFFF, 0x3FFFFFFF)),
            (127, 93, (0x3AB42777, 0x34D52517, 0x01234567, 0x23456789)),
            (128, 128, (0x3FFFFFFF, 1, 0x3FFFFFFF, 1)),
        ):
            sb.reset()
            sb.set_fpcw(control)
            draws = iter(words)
            bottom, top = sb.object(12), sb.object(12)
            sb.write(0xA2ABE4, 'I', width)
            sb.write(0xA2AC44, 'I', height)
            assert sb.call(0x642550, args=(bottom, top, 0)).esp_delta == 0
            assert next(draws, None) is None
            assert (sb.read(bottom + 4, 'f'), sb.read(top + 4, 'f')) == (0, 300)
            points = (sb.read(point + axis, 'I') for point in (bottom, top) for axis in (0, 8))
            prefix = f'{str(control == 0x7F).lower()}, {width}, {height}'
            yield f'[DataRow({prefix}, ' + ', '.join(f'0x{value:08X}u' for value in (*words, *points)) + ')]'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', type=Path)
    args = parser.parse_args()
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('Set OPENTPW_TPI_EXE to the identified Theme Park Inc executable.')
    image = Image.load(path)
    native = [*rows(image), *geometry_rows(image)]
    if args.check is not None:
        fixture = re.findall(r'^\s*(\[DataRow\([^\r\n]+\)\])$', args.check.read_text(encoding='utf-8'), re.MULTILINE)
        if fixture != native:
            raise SystemExit('TPI storm-timer fixtures differ from native output.')
        print(f'{len(native)} storm-timer fixtures match')
    else:
        print('\n'.join(native))


if __name__ == '__main__':
    main()
