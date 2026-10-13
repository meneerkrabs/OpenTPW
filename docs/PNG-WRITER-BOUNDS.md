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

Independent review reproduced the parent encoder's bounded wrapped-width
failure and restored production source byte for byte. Positive `int` dimensions
have a pixel product below `long.MaxValue`; a successful comparison to the
span's pixel count also implies that every row byte count and offset fits `int`.
Additional actual STB roundtrips cover nonzero-offset spans, surrounding-byte
isolation and a one-by-65,537 image, alongside other aligned wrapped-width
refusals. These controls add no image size policy or codec changes.
