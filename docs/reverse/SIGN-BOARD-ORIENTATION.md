# Sign board row orientation: packing versus sampling

This is a bounded COMPAT-018 evidence note, not original-platform pixel or GPU acceptance. No runtime behavior changes. The approximation remains open.

## Reference and scope

Static inspection of the existing TPW PC reference, SHA-256 `5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805`. No original executable was run or redistributed. Host source baseline: `5ca26919aac42f801fe4f640e4090fe298260182`.

The source composition surface is twice the destination width and height (0x5ECB8C–0x5ECBBA). The two outputs consume the first destination-height rows, not the whole composition surface.

## Native CPU destination packing

The 32-bit output branch at 0x5ECF2C selects format zero. At 0x5ECF37–0x5ECF54, each destination pointer starts at:

`outputBase + (height - 1) * width * 4`

The source begins at the composition surface pointer `[sign + 0x34]`. Each iteration copies `width` source pixels to the left destination, then the next `width` pixels to the right destination (0x5ECF79–0x5ECFA5). Both destination row bases decrement by `width * 4` (0x5ECFA7–0x5ECFC7), while the source pointer continues forward.

The 16-bit format-two branch follows the same row order. Its initial destination offsets are `(height - 1) * width * 2` (0x5ECFD7–0x5ECFEF), pixels are packed from four high nibbles, and both row bases decrement by `width * 2` (0x5ED0A2–0x5ED0CC).

Thus composition row `y` is stored in destination row `height - 1 - y` in both branches. This establishes the CPU packing direction only; it does not independently establish the original GPU uploader, sampling state, or final screen orientation.

## Host representation and compensating UV transform

`SignSurfaceComposer.Compose` returns top-down RGBA halves. It preserves the composition row index, selects the left/right half by source X, and converts ARGB channel order to RGBA. `ObjectSignImages` supplies those bytes to `Texture`; `Texture.CreateNative` uploads the byte sequence without a row reversal.

The object sign material is `content/shaders/test.shader` (`OriginalObject`). That shader's vertex stage uses `vTexCoords.y = 1.0 - vTexCoords.y`. Therefore a comparison of CPU byte arrays alone omits a relevant coordinate reversal. Do not add another image flip solely because the native pack is bottom-up: the host's top-down representation and shader transform must be considered together.

This source inspection does not prove native GPU equivalence. A controlled asymmetric original-platform capture or independently verified original upload/UV consumer remains necessary for that claim. Backend orientation, filtering, alpha policy and decoded original board pixels are separate questions.

## Synthetic regression boundary

`SignBoardOrientationTests.AsymmetricRawBoardPreservesEveryRowAndBothHalfSeams` calls the real sign parser and CPU composer with a fully opaque raw board. Distinct row, column and half channels make vertical/horizontal reversal, swapped halves and channel permutation detectable across every output pixel. Expected values include the native alpha-blit's division by 256 rather than 255.

The fixture contains no original media, uses no fonts or GPU, and does not validate the wavelet decoder. Existing wavelet arithmetic/plane tests remain separate. Passing this CPU test must not be reported as an original-platform or browser/GPU comparison.
