# PNG writer shape validation

`PngImage.EncodeRgba` validates the positive dimensions and complete RGBA buffer
before creating its output streams. The pixel count uses 64-bit multiplication
and is compared with the aligned buffer's pixel count; multiplying maximal
dimensions by four would overflow even 64-bit arithmetic. Once the shape fits,
row byte counts and offsets use checked arithmetic.

A bounded regression reproduced the prior failure: width `0x40000001`, height
one and four pixel bytes passed wrapped 32-bit shape/stride checks and returned
a PNG advertising an enormous width with a tiny scanline. Invalid shapes now
raise `ArgumentException` before encoding. This is an exposed writer argument
defect; the existing thumbnail and texture producers use bounded dimensions.

Synthetic tests cover wrapped/extreme dimensions, partial or excess buffers and
decoded RGBA roundtrips through the existing STB decoder. They include all alpha
values, non-square images and a 4097-by-one image, so the encoder gains no
arbitrary dimension cap. Compression, PNG chunks, CRCs and valid pixel data keep
their existing behavior. Existing PNG/package and texture-pack controls pass.

The encoder still builds the complete PNG and compressed data in memory. Valid
large images remain subject to the runtime's memory and stream limits; no large
image, GPU/browser performance or original-asset qualification was performed.
