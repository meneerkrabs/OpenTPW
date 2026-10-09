"""Bounded native sign witnesses and an offline arithmetic reference, never a renderer.

No payload or text is emitted. The arithmetic helper accepts synthetic source RGB
and already-computed normal/light terms; it cannot predict original final pixels.
"""
from pathlib import Path
import math
import struct

from corpus import span
from phase2 import ENGINE_SHA
from witness import APP_SHA, identified, d_form, branch, import_at, relocation, require, x_form, mask_form


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


def native_surface(bin_root: Path) -> dict:
    app = identified(bin_root / 'SimThemePark.data', APP_SHA)
    engine = identified(bin_root / 'libraries/engine_shared.data', ENGINE_SHA)
    # Horizontal and vertical channel sums are divided separately. The vertical
    # sums additionally keep only their low sixteen bits before division.
    for at, fields in [(0x3f6a0, (6, 6, 0)), (0x3f6a8, (7, 7, 0)),
                       (0x3f6b8, (8, 8, 0)), (0x3f6cc, (9, 9, 0)),
                       (0x3f908, (5, 0, 5)), (0x3f91c, (7, 4, 7))]:
        x_form(engine, at, 459, fields)
    for at, fields in [(0x3f6b0, (6, 4, 0)), (0x3f6c0, (7, 4, 1)),
                       (0x3f6c8, (8, 4, 2)), (0x3f6d4, (9, 4, 3))]:
        d_form(engine, at, 38, fields)
    mask_form(engine, 0x3f8fc, (7, 0, 0, 16, 31, 0))
    mask_form(engine, 0x3f904, (8, 4, 0, 16, 31, 0))
    # Normal gradients use forward neighbours; no boundary clamping is present
    # at these reads. Mask construction must make the accesses valid.
    x_form(app, 0xab344, 87, (0, 30, 3))
    d_form(app, 0xab348, 34, (4, 30, 0))
    d_form(app, 0xab34c, 34, (3, 30, 1))
    for at, name in [(0xab1e4, 'sin'), (0xab1f4, 'cos'), (0xab228, 'sin'),
                     (0xab238, 'cos'), (0xab264, 'sqrt'), (0xab4bc, 'pow')]:
        require(import_at(app, branch(app, at))['symbol'], name, 'relief MathLib input')
    require(struct.unpack_from('>f', app.data_section.data, 0x5228)[0],
            0.01745329238474369, 'native angle constant')
    require(branch(app, 0xaaca8), 0xa8f7c, 'Gaussian kernel builder')
    require(import_at(app, branch(app, 0xa9018))['symbol'], 'exp', 'Gaussian MathLib input')
    for offset, value in [(0x5248, 2.5), (0x5258, -0.5), (0x5260, 6.2831853)]:
        require(struct.unpack_from('>d', app.data_section.data, offset)[0], value, 'Gaussian constant')
    # The original mask is read separately from the shape target. Empty-alpha
    # pixels bypass the convolution; horizontal neighbours clamp at the edges.
    for at, fields in [(0xaae14, (0, 10, 0)), (0xaaecc, (0, 11, 0))]:
        d_form(app, at, 34 if at == 0xaae14 else 38, fields)
    # Colour/effect lerps have signed >>8 products, not floating /255.
    for at, fields in [(0x3f0e8, (0, 9, 0)), (0x3f108, (0, 9, 0)),
                       (0x3f12c, (0, 9, 0)), (0x3f270, (12, 12, 9)),
                       (0x3f2b0, (12, 31, 12))]:
        x_form(engine, at, 235, fields)
    for at, fields in [(0xac50c, (0, 31, 0)), (0xac534, (0, 31, 0))]:
        d_form(app, at, 32, fields)
    for at, fields in [(0xac518, (0, 27, 0)), (0xac540, (0, 26, 0))]:
        d_form(app, at, 36, fields)
    require(import_at(app, branch(app, 0xac4f4))['symbol'],
            'swizzle_for_gimex__6BitmapFv', '32-bit output swizzle')
    for at, fields in [(0xac5d0, (10, 3, 0)), (0xac5d8, (8, 3, 1)),
                       (0xac5dc, (7, 3, 2)), (0xac5e4, (9, 3, 3))]:
        d_form(app, at, 34, fields)
    for at, fields in [(0xac5e0, (10, 10, 0, 24, 27, 0)),
                       (0xac5e8, (8, 8, 0, 24, 27, 0)),
                       (0xac5ec, (7, 7, 0, 24, 27, 0)),
                       (0xac5fc, (7, 7, 4, 20, 23, 0))]:
        mask_form(app, at, fields)
    for at, fields in [(0xac608, (8, 7, 8, 16, 19, 0)),
                       (0xac60c, (9, 7, 0, 24, 27, 0)),
                       (0xac610, (10, 7, 28, 28, 31, 0))]:
        word = int.from_bytes(app.code.data[at:at + 4], 'big')
        require(word >> 26, 20, 'output nibble insertion')
        require((word >> 21 & 31, word >> 16 & 31, word >> 11 & 31,
                 word >> 6 & 31, word >> 1 & 31, word & 1), fields, 'output nibble operands')
    for at, fields in [(0x3f0ec, (0, 0, 8)), (0x3f10c, (0, 0, 8)),
                       (0x3f130, (0, 0, 8)), (0x3f274, (12, 12, 8)),
                       (0x3f2b4, (12, 12, 8))]:
        x_form(engine, at, 824, fields)
    d_form(app, 0xac614, 44, (7, 12, 0))
    return {'app_sha256': APP_SHA, 'engine_sha256': ENGINE_SHA,
            'filter': 'horizontal truncation followed by vertical truncation; byte bucket counts',
            'normal': 'normalized forward right/down gradients plus z=1',
            'layer_lerp': 'signed product shifted right by eight; alpha=max',
            'mode_1': 'BGRA bytes', 'mode_2': 'RGBA4444, big-endian native store',
            'rows': 'top base-height source rows, horizontally split, reverse destination rows',
            'unproved': ['original coverage', 'nonzero mask morphology', 'forward-gradient boundary validity', 'wavelet decode',
                         'MathLib and PowerPC float exactness', 'GPU format and UV orientation']}


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
