"""Offline selected native sign arithmetic. Synthetic fixtures only in Git.

This is a reference for proved operations, not a renderer. Native text coverage,
nonzero mask morphology, arbitrary upsampling and wavelet images are unsupported.
Python math is not a PowerPC single-precision or MathLib rounding oracle.
"""
import math

from sign_evidence import relief_channel_reference


class SignShadeReference:
    @staticmethod
    def gaussian_kernel(sigma):
        """Native double kernel: size=int(2.5*sigma)+3, origin=(size-1)//2.

        Even sizes are retained. The native finite kernel is renormalized by
        its actual sum after evaluating exp, rather than assuming an infinite
        Gaussian integrates to one.
        """
        if not math.isfinite(sigma) or not 0 < sigma <= 8:
            raise ValueError('bounded positive Gaussian sigma required')
        size = int(2.5 * sigma) + 3
        origin = (size - 1) // 2
        values = [[math.exp(-0.5 * ((x - origin) ** 2 + (y - origin) ** 2) / (sigma * sigma)) / (6.2831853 * sigma * sigma)
                   for x in range(size)] for y in range(size)]
        total = sum(sum(row) for row in values)
        return tuple(tuple(value / total for value in row) for row in values)

    @staticmethod
    def normal_mask(coverage, width, height, mask_word, sigma):
        """Proved zero-iteration path and Gaussian stage; morphology unsupported.

        Native shape begins as binary ink; the original coverage remains a
        separate alpha input. The convolution replicates edge coordinates and
        only replaces shape samples where original coverage is nonzero.
        """
        if not (1 <= width <= 512 and 1 <= height <= 512) or len(coverage) != width * height:
            raise ValueError('reference mask bounds')
        if mask_word != 0:
            raise ValueError('nonzero mask morphology is unsupported')
        if not math.isfinite(sigma) or sigma < 0:
            raise ValueError('reference Gaussian parameter')
        shape = bytes(255 if value else 0 for value in coverage)
        if sigma == 0:
            return shape
        kernel = SignShadeReference.gaussian_kernel(sigma)
        origin = (len(kernel) - 1) // 2
        result = bytearray(shape)
        for y in range(height):
            for x in range(width):
                if coverage[y * width + x] == 0:
                    continue
                total = 0.0
                for ky, row in enumerate(kernel):
                    sy = max(0, min(height - 1, y + ky - origin))
                    for kx, weight in enumerate(row):
                        sx = max(0, min(width - 1, x + kx - origin))
                        total += weight * shape[sy * width + sx]
                result[y * width + x] = int(total) & 255
        return bytes(result)

    @staticmethod
    def descimate_argb(source, width, height, target_width, target_height):
        """Two-stage integer bucket average, engine 0x3f338; downsampling only.

        Native bucket counts are bytes, and vertical channel totals are masked
        to 16 bits. Restrict dimensions to 255, so those representations stay
        within their proven domain and no zero-bucket upsample is invented.
        """
        if not (1 <= target_width <= width <= 255 and 1 <= target_height <= height <= 255):
            raise ValueError('reference supports positive downsample dimensions <=255 only')
        if len(source) != width * height * 4:
            raise ValueError('reference ARGB payload size')

        def counts(size, target):
            step = ((size << 16) + 32768) // target
            return [((index + 1) * step >> 16) - (index * step >> 16) for index in range(target)]

        x_counts, y_counts = counts(width, target_width), counts(height, target_height)
        temporary = bytearray()
        result = bytearray()
        source_row = 0
        for rows in y_counts:
            temporary.clear()
            for row in range(source_row, source_row + rows):
                column = 0
                for columns in x_counts:
                    for channel in range(4):
                        temporary.append(sum(source[(row * width + x) * 4 + channel] for x in range(column, column + columns)) // columns)
                    column += columns
            for column in range(target_width):
                for channel in range(4):
                    total = sum(temporary[(row * target_width + column) * 4 + channel] for row in range(rows)) & 65535
                    result.append(total // rows)
            source_row += rows
        return bytes(result)

    @staticmethod
    def relief_normal(current, right, below, amplitude):
        if not all(0 <= value <= 255 for value in (current, right, below)) or not math.isfinite(amplitude):
            raise ValueError('reference gradient inputs')
        x = (right - current) * amplitude / 255
        y = (below - current) * amplitude / 255
        magnitude = math.sqrt(x * x + y * y + 1)
        return (x / magnitude, y / magnitude, 1 / magnitude)

    @staticmethod
    def light_vectors(angle6, angle7):
        if not all(math.isfinite(value) for value in (angle6, angle7)):
            raise ValueError('reference angle inputs')
        # Native constant is a single float, not math.pi / 180.
        radians = 0.01745329238474369
        theta, phi = -angle6 * radians, angle7 * radians
        light = (math.cos(phi) * math.cos(theta), -math.sin(phi) * math.cos(theta), -math.sin(theta))
        length = math.sqrt(sum(value * value for value in light))
        light = tuple(value / length for value in light)
        half = tuple((value + (1 if index == 2 else 0)) * 0.5 for index, value in enumerate(light))
        length = math.sqrt(sum(value * value for value in half))
        if length == 0:
            raise ValueError('undefined half-vector')
        return light, tuple(value / length for value in half)

    @staticmethod
    def shade_sample(source_rgb, coverage, current, right, below, parameters):
        """Shade supplied normal-mask neighbours; does not build that mask here."""
        if len(parameters) != 8:
            raise ValueError('eight native parameters required')
        if coverage == 0:
            return (0, 0, 0, 0)
        normal = SignShadeReference.relief_normal(current, right, below, parameters[0])
        light, half = SignShadeReference.light_vectors(parameters[6], parameters[7])
        dot = lambda vector: sum(a * b for a, b in zip(normal, vector))
        return relief_channel_reference(source_rgb, coverage, parameters[2:6], dot(light), dot(half))

    @staticmethod
    def effect_blend(source_argb, destination_argb):
        """Engine alphablt four-byte path: max alpha and signed >>8 lerp."""
        SignShadeReference._pixels(source_argb, destination_argb)
        alpha = source_argb[0]
        return (max(alpha, destination_argb[0]), *(d + ((alpha * (s - d)) >> 8) for s, d in zip(source_argb[1:], destination_argb[1:])))

    @staticmethod
    def paint_blend(mask, paint_rgba, destination_argb):
        """Engine colourblt scales both coverage and supplied RGB by paint A."""
        SignShadeReference._pixels(paint_rgba, destination_argb)
        if not 0 <= mask <= 255:
            raise ValueError('reference mask')
        alpha = (mask * paint_rgba[3]) >> 8
        target = tuple((paint_rgba[3] * color) >> 8 for color in paint_rgba[:3])
        return (max(alpha, destination_argb[0]), *(d + ((alpha * (s - d)) >> 8) for s, d in zip(target, destination_argb[1:])))

    @staticmethod
    def composition_plan(styles, reverse_flag, extra_image=False):
        """Pinned app ordering only. Masks and effects must already be built."""
        if len(styles) != 2 or any(style not in (0, 1, 2) for style in styles):
            raise ValueError('unsupported effect style')
        result = ['clear']
        if extra_image:
            result.append('extra-image-alpha-blit')
        if styles == (2, 2) or styles == [2, 2]:
            result.append('max-paint-masks-then-paint-0')
        elif 2 in styles:
            result.append(f'paint-{styles.index(2)}')
        for index in ((1, 0) if reverse_flag else (0, 1)):
            if styles[index] == 1:
                result.append(f'paint-{index}')
            result.append(f'effect-{index}')
        return result

    @staticmethod
    def split_pack(source_argb, half_width, height, mode):
        """App copy loops consume the top height rows, split horizontally and flip rows.

        Input width must be exactly twice half_width; remaining lower rows are
        ignored by the original loops. Mode1 produces BGRA bytes, mode2 big-endian
        RGBA4444 words. No model UV orientation or host GPU format is inferred.
        """
        if not (1 <= half_width <= 256 and 1 <= height <= 256) or mode not in (1, 2):
            raise ValueError('reference pack bounds or mode')
        row_bytes = half_width * 2 * 4
        if len(source_argb) < row_bytes * height or len(source_argb) % row_bytes:
            raise ValueError('reference pack source dimensions')
        size = 4 if mode == 1 else 2
        outputs = [bytearray(half_width * height * size) for _ in range(2)]
        for y in range(height):
            for half in range(2):
                for x in range(half_width):
                    at = (y * 2 * half_width + half * half_width + x) * 4
                    a, r, g, b = source_argb[at:at + 4]
                    packed = bytes((b, g, r, a)) if mode == 1 else (((r >> 4) << 12) | ((g >> 4) << 8) | ((b >> 4) << 4) | (a >> 4)).to_bytes(2, 'big')
                    out = ((height - y - 1) * half_width + x) * size
                    outputs[half][out:out + size] = packed
        return tuple(bytes(output) for output in outputs)

    @staticmethod
    def _pixels(*pixels):
        if any(len(pixel) != 4 or any(not isinstance(value, int) or not 0 <= value <= 255 for value in pixel) for pixel in pixels):
            raise ValueError('reference byte pixels')
