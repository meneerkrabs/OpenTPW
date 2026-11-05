"""X90: ThemeEngine loader bindings (docs/reverse/ORACLE.md, section 7.42).

Dev-only: OPENTPW_TPI_EXE and Unicorn are required. No original game process runs.
The real constructor (0x418F60), schema accessors, initializer (0x419830), key
resolver (0x4192C0), integer conversion and C-locale character classification run
in the bounded x86 harness. Only allocation is synthetic. The parser inputs below
are controlled scalar settings, not a test of the full SAM grammar.
No executable bytes, schema contents or game data are emitted.

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_themeengine_oracle.py
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

OBJECT = 0xA01A60
VTABLE = 0x93A048
SCHEMA = 0x9966C0
CONSTRUCTOR = 0x418F60
RESOLVER = 0x4192C0
PARSER = 0x419CD0
BINDINGS = {
    'BackColour': 0xA027CC,
    'FogColour': 0xA027D0,
    'AmbientLightLevel': 0xA027D4,
    'DirectionalLightLevel': 0xA027D8,
    'ProceduralTexturingXOffset': 0xA027DC,
    'ProceduralTexturingYOffset': 0xA027E0,
}


def cstring(sb: Sandbox, text: str) -> int:
    address = sb.alloc(len(text) + 1)
    sb.uc.mem_write(address, text.encode('ascii') + b'\0')
    return address


def sandbox(image: Image) -> Sandbox:
    return Sandbox(image, [
        (0x418F60, 0x419119), (0x4192C0, 0x419530),
        (0x419830, 0x419A50), (0x419CD0, 0x41AA40),
        (0x41B350, 0x41B384), (0x407E2D, 0x407E32),
        (0x407F2C, 0x407F31), (0x402441, 0x402446), (0x40C955, 0x40C95A),
        (0x86F490, 0x86F526), (0x86F5E0, 0x86F731),
    ], stubs={0x7CBB70: lambda sb: (sb.alloc(sb.arg(0)), 0)}, budget=500_000)


def construct(sb: Sandbox) -> None:
    sb.reset()
    sb.write(OBJECT, 'I', VTABLE)
    result = sb.call(CONSTRUCTOR, this=OBJECT)
    assert result.esp_delta == 0, result.esp_delta


def resolve(sb: Sandbox, group: str, field: str) -> int | None:
    row = sb.object(4)
    result = sb.call(RESOLVER, args=(cstring(sb, group), cstring(sb, field), row, 0, 0), this=OBJECT)
    assert result.esp_delta == 0, result.esp_delta
    if not result.eax:
        return None
    index = sb.read(row, 'I')
    return sb.read(sb.read(OBJECT + 4, 'I') + index * 12, 'I')


def load(sb: Sandbox, text: str, overlay: bool = True) -> None:
    result = sb.call(PARSER, args=(cstring(sb, 'oracle.sam'), cstring(sb, text), int(overlay)), this=OBJECT)
    assert result.esp_delta == 0, result.esp_delta
    assert result.eax == 1, f'parser rejected controlled input: {text!r}'


def verify(image: Image) -> int:
    if image.identity != 'tpi':
        raise ValueError('this oracle requires the identified TPI executable')
    sb = sandbox(image)
    construct(sb)
    assert sb.call(0x86F703, args=(ord('_'),)).eax == 0, 'the original alphanumeric classifier rejects underscore'
    for name, address in BINDINGS.items():
        assert resolve(sb, 'ThemeEngine', name) == address, name
        assert sb.read(address, 'I') == 0, name
    assert resolve(sb, 'ThemeEngine', 'MissingColour') is None
    assert resolve(sb, 'themeengine', 'BackColour') is None
    # Distinct sentinels prove that each field writes its own constructor-resolved word.
    values = [0x112233, 0x445566, 0xFF555568, 0xFFFFFFD8, -17, 29]
    text = ''.join(f'ThemeEngine.{name} {value}\n' for name, value in zip(BINDINGS, values))
    load(sb, text)
    assert [sb.read(address, 'I') for address in BINDINGS.values()] == [v & 0xFFFFFFFF for v in values]
    # Shipped-style spacing, CRLF and comments run through the real CRT classifiers.
    load(sb, 'ThemeEngine.BackColour\t\t0\t# c\r\n#ThemeEngine.FogColour 5\r\n')
    assert sb.read(BINDINGS['BackColour'], 'I') == 0
    assert sb.read(BINDINGS['FogColour'], 'I') == values[1], 'commented-out field must not overwrite the earlier layer'
    # Missing fields preserve the earlier layer; repeated assignments overwrite in file order.
    load(sb, 'ThemeEngine.BackColour 7\nThemeEngine.BackColour 11\n')
    assert sb.read(BINDINGS['BackColour'], 'I') == 11
    assert sb.read(BINDINGS['FogColour'], 'I') == values[1]
    load(sb, 'ThemeEngine.BackColour 19\n', overlay=False)
    assert sb.read(BINDINGS['BackColour'], 'I') == 19
    assert all(sb.read(address, 'I') == 0 for name, address in BINDINGS.items() if name != 'BackColour')
    return len(BINDINGS)


if __name__ == '__main__':
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('OPENTPW_TPI_EXE must name the identified no-CD Game.exe')
    print(f'{verify(Image.load(path))} ThemeEngine bindings verified; scalar writes, overlay and reset verified')
