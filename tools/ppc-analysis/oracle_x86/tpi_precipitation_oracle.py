"""Replay the TPI per-turn precipitation branch, stopping before lightning.

The renderer count setter and precipitation stop routine execute natively.
The rain audio sink records the raw requested volume; the later weather-kind
callbacks in the turn are stubbed because they are outside this comparison.
"""
import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from x86_harness import Image, Sandbox, executable_path  # noqa: E402

CASES = (
    (47, -1, 20, 600), (0, 13, 20, 600), (20, 21, 20, 600),
    (0, 20, 20, 600), (20, 0, 20, 600), (19, 0, 20, 600),
    (47, 0, 20, 600), (500, 40, 20, 600), (50, 500, 20, 600),
    (590, 630, 20, 600), (610, 630, 20, 600),
    (-30, 40, 20, 600), (123, 132, 10, 120),
)


def rows(image):
    volumes = []

    def volume(sb):
        value = sb.arg(0)
        volumes.append(value if value < 0x80000000 else value - 0x100000000)
        return 0, 0

    sb = Sandbox(image, [
        (0x642230, 0x6422FE), (0x4080DA, 0x4080DF), (0x6EA910, 0x6EA91D),
        (0x40B2D0, 0x40B2D5), (0x642ED0, 0x642F74),
    ], stubs={0x408C9C: volume, **{a: lambda sb: (0, 0) for a in (0x40243C, 0x4047B4, 0x40378D, 0x402220, 0x4078B5)}}, budget=1000)
    for current, target, speed, maximum in CASES:
        sb.reset()
        volumes.clear()
        weather = sb.object(0x80, {0x44: ('i', current), 0x48: ('i', target), 0x6C: ('i', 0)})
        renderer = sb.object(0x331C, {0x3318: ('i', -999)})
        sb.write(0xBE17E0, 'I', renderer)
        sb.write(0xA024F0, 'i', speed)
        sb.write(0xA02540, 'i', maximum)
        sb.call(0x642230, this=weather, stop=0x6422FE)
        assert len(volumes) <= 1
        count = sb.read(renderer + 0x3318, 'i')
        stopped = sb.read(weather + 0x6C, 'i') == 2
        values = [current, target, speed, maximum, sb.read(weather + 0x44, 'i'),
                  'null' if count == -999 else count, volumes[0] if volumes else 'null', str(stopped).lower()]
        yield '[DataRow(' + ', '.join(map(str, values)) + ')]'


def target_rows(image):
    # Record the mode and fall-speed requests; leave the audio and advice outside the replay.
    modes, speeds = [], []

    def record(sb, sink):
        sink.append(sb.arg(0))
        return 0, 4

    sb = Sandbox(image, [(0x642A80, 0x642C89)], stubs={
        0x4078E7: lambda sb: (0, 4), 0x40795F: lambda sb: (0, 0),
        0x40B2D0: lambda sb: record(sb, modes), 0x409237: lambda sb: record(sb, speeds),
    }, budget=1000)
    for kind, quality, rain_low, rain_high, snow_low, snow_high, maximum in (
        (0, 50, 0, 40, 0, 20, 600), (1, 98, 0, 40, 0, 20, 600),
        (2, 13, 0, 40, 0, 20, 600), (2, 40, 0, 40, 0, 20, 600),
        (3, 14, 0, 40, 0, 20, 600), (3, -1, 0, 40, 0, 20, 600),
        (4, 9, 0, 40, 0, 20, 601), (4, 5, 0, 40, 5, 5, 600),
    ):
        sb.reset()
        modes.clear()
        speeds.clear()
        weather = sb.object(0x80, {0x48: ('i', -999)})
        spell = sb.object(16, {4: ('i', kind), 8: ('i', quality)})
        for address, value in ((0xA02514, snow_low), (0xA02518, snow_high),
                               (0xA0251C, rain_low), (0xA02520, rain_high), (0xA02540, maximum)):
            sb.write(address, 'i', value)
        sb.call(0x642A80, args=(spell,), this=weather, stop=0x642C89)
        assert len(modes) == 1 and len(speeds) <= 1
        values = (kind, quality, rain_low, rain_high, snow_low, snow_high, maximum, sb.read(weather + 0x48, 'i'),
                  modes[0], f'{speeds[0]}u' if speeds else 'null')
        yield '[DataRow(' + ', '.join(map(str, values)) + ')]'


def wind_rows(image):
    # The original fmod helper runs; only the RNG draw and renderer call are intercepted.
    handoffs = []
    word = 0

    def renderer(sb):
        handoffs.append((sb.arg(0), sb.arg(1)))
        return 0, 8

    sb = Sandbox(image, [(0x642C89, 0x642D4A), (0x87066A, 0x870692), (0x87A5F0, 0x87A960)],
                 stubs={0x40A92F: lambda _: (word, 0), 0x40A830: renderer}, budget=1000)
    for direction, quality, word in ((90, 21, 89), (350, 21, 89), (10, 120, 0), (0, -20, 0),
                                     (359, 0, 46), (90, 50, 45), (0, 100, 0xFFFFFFFF)):
        results = []
        for control in (0x7F, 0x27F, 0x37F):
            sb.reset()
            sb.set_fpcw(control)
            handoffs.clear()
            weather = sb.object(0x80, {0x3C: ('i', quality), 0x74: ('f', direction)})
            sb.call(0x642C89, regs={'esi': weather, 'ebx': 0}, stop=0x642D4A)
            result = sb.read(weather + 0x74, 'I'), sb.read(weather + 0x78, 'I')
            assert handoffs == [result]
            results.append(result)
        assert len(set(results)) == 1
        angle, force = results[0]
        yield f'[DataRow({direction}, {quality}, {word}u, {angle}u, {force}u)]'


def fall_vector_rows(image):
    sb = Sandbox(image, [(0x6EA9A0, 0x6EAA73)], budget=200)
    for angle, force_pct in ((0, 0), (0, 100), (90, 50), (134, 79), (210, 100), (359, 25)):
        for control in (0x7F, 0x27F):
            sb.reset()
            sb.set_fpcw(control)
            renderer = sb.object(0x3400)
            sb.write(renderer, 'f', angle)
            sb.write(renderer + 4, 'f', force_pct / 100)
            sb.call(0x6EA9A0, args=(sb.read(renderer, 'I'), sb.read(renderer + 4, 'I')),
                    this=renderer, stop=0x6EAA73)
            x, y, z = (sb.read(renderer + offset, 'I') for offset in (0x331C, 0x3320, 0x3324))
            yield f'[DataRow({angle}, {force_pct}, {str(control == 0x7F).lower()}, {x}u, {y}u, {z}u)]'


def scroll_rows(image):
    offsets = []

    def submit(sb):
        offsets.append(tuple(sb.arg(i) for i in range(3)))
        return 1, 12

    sb = Sandbox(image, [(0x6ED800, 0x6EDB6B), (0x86FAB0, 0x86FAD7)], stubs={
        0x40519B: lambda _: (500, 0), 0x40646F: lambda _: (1, 0),
        0x409D54: submit, 0x4032F6: submit,
    }, budget=500)
    for mode, eye, elapsed, direction in (
        (1, (0, 0, 0), 500, (0.0, -1.0, 0.0)),
        (1, (10, 20, 30), 250, (0.5, -0.5, 0.5)),
        (2, (0, 0, 0), 500, (0.0, -1.0, 0.0)),
    ):
        sb.reset()
        sb.set_fpcw(0x7F)
        offsets.clear()
        camera = sb.object(0x40, {0x30 + 4 * i: ('f', value) for i, value in enumerate(eye)})
        sb.write(0xB910D0, 'I', camera)
        sb.write(0xB9123C, 'I', sb.object(64))
        renderer = sb.object(0xA200, {
            0: ('I', mode), 0x3318: ('I', 1), 0x3340: ('f', 0.1), 0x3344: ('I', 500 - elapsed),
            0x3338: ('f', 4.5), 0x333C: ('f', 0.8),
            **{0x331C + 4 * i: ('f', value) for i, value in enumerate(direction)},
        })
        sb.call(0x6ED800, this=renderer)
        assert len(offsets) == 1
        # The original draw routines subtract the supplied X and Z, and add Y.
        x, y, z = offsets[0]
        x ^= 0x80000000
        z ^= 0x80000000
        values = [str(v) for v in (mode, *eye, elapsed)] + [f'{v}f' for v in direction] + [f'{v}u' for v in (x, y, z)]
        yield '[DataRow(' + ', '.join(values) + ')]'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', type=Path)
    args = parser.parse_args()
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('Set OPENTPW_TPI_EXE to the identified Theme Park Inc executable.')
    image = Image.load(path)
    native = [*rows(image), *target_rows(image), *wind_rows(image), *fall_vector_rows(image), *scroll_rows(image)]
    if args.check:
        fixture = re.findall(r'^\s*(\[DataRow\([^\r\n]+\)\])$', args.check.read_text(encoding='utf-8'), re.MULTILINE)
        if fixture != native:
            raise SystemExit('TPI precipitation fixtures differ from native output.')
        print(f'{len(native)} precipitation fixtures match')
    else:
        print('\n'.join(native))


if __name__ == '__main__':
    main()
