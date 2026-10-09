"""Bounded native sign witnesses and an offline arithmetic reference, never a renderer.

No payload or text is emitted. The arithmetic helper accepts synthetic source RGB
and already-computed normal/light terms; it cannot predict original final pixels.
"""
from pathlib import Path
import math
import struct

from corpus import span
from phase2 import ENGINE_SHA
from witness import APP_SHA, identified, d_form, branch, import_at, relocation, require, x_form


def native_sign_reader(bin_root: Path) -> dict:
    app = identified(bin_root / 'SimThemePark.data', APP_SHA)
    engine = identified(bin_root / 'libraries/engine_shared.data', ENGINE_SHA)
    for at, fields in [(0xabae8, (3, 29, 12)), (0xabb0c, (3, 29, 16)),
                       (0xabb2c, (3, 29, 56)), (0xabb38, (3, 29, 856)),
                       (0xabb44, (3, 29, 456)), (0xabb50, (3, 29, 964)),
                       (0xabd54, (3, 29, 916)), (0xabd64, (3, 29, 1024))]:
        d_form(app, at, 14, fields)
    for at, target in [(0xabb34, 0xaa5a0), (0xabb4c, 0xaa5a0),
                        (0xabb40, 0xaa8a4), (0xabb58, 0xaa8a4)]:
        require(branch(app, at), target, 'native record reader')
    # Font helper stream reads are 64 + 260 + 4 + 4 + 60 bytes.
    for at, count in [(0xaa5ac, 64), (0xaa5d8, 260), (0xaa5ec, 4),
                      (0xaa610, 4), (0xaa634, 60)]:
        d_form(app, at, 14, (4, 0, count))
    # Effect helper explicitly reads and byte-swaps eleven consecutive words.
    for index in range(1, 11):
        at = 0xaa8e4 + (index - 1) * 36
        d_form(app, at, 14, (3, 30, index * 4))
        d_form(app, at + 4, 14, (4, 0, 4))
        require(branch(app, at + 12), 0x34b0, 'effect fread helper')
    resolved = {}
    for at, name, expected in [(0xabd5c, 'load__6BitmapFPv', 0x3e7fc),
                               (0xabd6c, 'load__6BitmapFPv', 0x3e7fc),
                               (0xabd94, 'load__6BitmapFPv', 0x3e7fc),
                               (0xabdac, 'load_wavelet__6BitmapFPvP8CWavelet', 0x3e97c),
                               (0xab1c8, 'descimate__6BitmapFR6Bitmap', 0x3f338),
                               (0xac304, 'colourblt__6BitmapFR6BitmapUcUcUcUc', 0x3f200)]:
        imported = import_at(app, branch(app, at))
        require(imported['symbol'], name, 'sign bitmap import')
        exports = [export for export in engine.exports if export.name == name]
        require(len(exports), 1, 'engine export identity')
        require(relocation(engine, exports[0].value, engine.code.index), expected, 'engine entry')
        resolved[hex(at)] = {'symbol': name, 'engine_code': expected}
    # Raw load reads three scalar words, stores width/height/bytes-per-pixel,
    # and uses their product for the stream read rather than a fixed bitmap size.
    for at, fields in [(0x3e828, (3, 1, 64)), (0x3e84c, (3, 1, 60)),
                       (0x3e878, (3, 1, 56))]:
        d_form(engine, at, 14, fields)
    for at, fields in [(0x3e90c, (27, 31, 0)), (0x3e910, (29, 31, 4)),
                       (0x3e914, (28, 31, 8))]:
        d_form(engine, at, 36, fields)
    x_form(engine, 0x3e94c, 235, (0, 3, 0))
    x_form(engine, 0x3e958, 235, (4, 4, 0))
    # Stored effect bounds are overwritten after actual text measurement.
    for at, fields in [(0xac0d8, (0, 29, 892)), (0xac0e0, (0, 29, 896)),
                       (0xac180, (0, 29, 1000)), (0xac188, (0, 29, 1004))]:
        d_form(app, at, 36, fields)
    # Relief source channels 1/2/3; source alpha is not copied into the effect.
    for at, fields in [(0xab43c, (4, 5, 1)), (0xab440, (3, 5, 2)),
                       (0xab444, (0, 5, 3)), (0xab540, (0, 29, 0))]:
        d_form(app, at, 34, fields)
    return {'app_sha256': APP_SHA, 'engine_sha256': ENGINE_SHA,
            'header_bytes': 17, 'font_bytes': 392, 'effect_bytes': 44,
            'optional_paint_bytes': 20, 'bitmap_header_bytes': 12,
            'resolved_calls': resolved, 'source_rgb_byte_offsets': [1, 2, 3],
            'effect_alpha': 'generated mask, not source-image alpha',
            'stored_effect_extent_origin': 'overwritten from measured text bounds',
            'limit': 'source descimation, mask shaping, layer composition and final packing are not implemented by this witness'}


def metadata(data: bytes) -> dict:
    if len(data) > 4 * 1024 * 1024:
        raise ValueError('sign input limit')
    span(data, 0, 889)
    version, = struct.unpack_from('<I', data)
    if version not in (100, 101):
        raise ValueError('sign version')
    styles = struct.unpack_from('<2I', data, 9)
    effects = [struct.unpack('<I8f2i', span(data, 409 + 436 * index, 44)) for index in range(2)]
    position = 889
    for style in styles:
        if style:
            span(data, position, 20)
            position += 20
    images = []
    for index in range(2 + bool(data[8])):
        width, height, channels = struct.unpack('<3I', span(data, position, 12))
        size = width * height * channels
        if min(width, height, channels) <= 0 or size > 4 * 1024 * 1024:
            raise ValueError('sign bitmap bounds')
        wavelet = index == 2 and version == 101
        payload_size = len(data) - position - 12 if wavelet else size
        if not payload_size:
            raise ValueError('sign missing bitmap payload')
        span(data, position + 12, payload_size)
        images.append({'offset': position, 'width': width, 'height': height,
                       'bytes_per_pixel': channels, 'payload_bytes': payload_size, 'wavelet': wavelet})
        position += 12 + payload_size
    return {'version': version, 'styles': styles, 'effects': effects,
            'images': images, 'trailing_bytes': len(data) - position}


def relief_channel_reference(source_rgb, mask_alpha, material, diffuse_dot, specular_dot):
    """Selected relief-channel arithmetic only; callers supply synthetic spatial terms.

    Native output truncates after clamp. This is neither a float-accuracy oracle
    nor a complete shader: source descimation and native normal construction remain
    prerequisites. Mask zero takes the native zero-all-channels branch.
    """
    if len(source_rgb) != 3 or len(material) != 4 or not 0 <= mask_alpha <= 255:
        raise ValueError('reference inputs')
    if any(not math.isfinite(value) for value in (*source_rgb, *material, diffuse_dot, specular_dot)):
        raise ValueError('reference non-finite inputs')
    if not all(0 <= value <= 255 for value in source_rgb):
        raise ValueError('reference channel bounds')
    if mask_alpha == 0:
        return (0, 0, 0, 0)
    base, diffuse, specular, exponent = material
    term = base + diffuse * diffuse_dot if diffuse_dot > 0 else base
    white = specular * specular_dot ** exponent if specular_dot > 0 else 0
    rgb = tuple(int(max(0, min(255, value * term + 255 * white))) for value in source_rgb)
    return (mask_alpha, *rgb)
