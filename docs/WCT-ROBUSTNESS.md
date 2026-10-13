# WCT decoder bounds audit

Review base: `0af4a608f4770c316652d4a7db767b907ebc5c27` (2026-10-13).
This audit uses synthetic inputs only. Original asset reads are blocked; fresh
retail corpus compatibility and original-runtime qualification remain deferred.

Three safe regression tests failed before the fixes: a zero-width image was
accepted, a one-byte compressed chunk raised `IndexOutOfRangeException`, and an
8x8 colour payload with only 80 one-byte coefficients was silently accepted
although Y/Cb/Cr require 64 + 16 + 16. The channel's in-place shift retained the
old array length, allowing missing Cr bytes to come from duplicated earlier data.

Inspection also found unchecked image/scratch array products, unvalidated chunk
counts, ignored BILZ size metadata, and an LZSS EOF path that repeatedly consumed
fabricated zero bits and grew its output indefinitely. The old unbounded EOF path
was not executed during the red run; the checked implementation is exercised by
bounded truncated-stream tests.

## Boundaries enforced

- Width and height must be positive. The existing aligned side `S` is retained;
  `2*S*S` scratch elements and `width*height*4` RGBA bytes must fit
  `Array.MaxLength`, computed with wide arithmetic before allocation. This is a
  storage requirement of the existing decoder, not an invented WCT dimension cap.
- Colour chunk count must be positive; alpha count must be nonnegative and must
  be positive when alpha is enabled. Declared chunks must be read completely.
  Seekable streams check remaining bytes before allocation; nonseekable streams
  grow storage only as actual input arrives, accepting short reads.
- BILZ needs its complete 28-byte header. Its signed decompressed count must be
  nonnegative and its compressed count positive and available in the containing
  chunk. Inflation uses that compressed segment and must produce exactly the
  declared decompressed count; malformed/truncated zlib data is rejected.
- A valid BILZ stream may contain unused decoded padding. Inflation validates the
  complete stream, but retains only the maximum prefix the decoder can consume:
  three encoded bytes per coefficient (one escape plus an Int16). Colour needs
  `S*S + 2*(S/2)*(S/2)` coefficients; alpha needs `S*S`. No fixed 256px assumption
  or arbitrary decoded-padding cap is introduced.
- Minimum coefficient bytes and the complete variable-length escape encoding are
  checked before image-plane allocation, including large dimensions paired with
  tiny payloads or insufficient escaped coefficients. Each channel consumes only
  its real remaining bytes; absent coefficients or incomplete Int16 escapes are refused.
- LZSS uses the additive `BitReader.GetBitsChecked` API, which refuses incomplete
  16-bit-word input rather than inventing bits. The legacy `GetBits` zero-padding
  behavior remains unchanged for other callers. Output growth uses wide
  arithmetic and cannot exceed `Array.MaxLength`. Valid backward references and
  overlapping copies retain the existing algorithm and offset-error behavior.

Malformed WCT headers, dimensions, chunk lengths, BILZ sizes and coefficient
truncation now report `InvalidDataException`. Two pre-existing malformed synthetic
pins intentionally change from `IndexOutOfRangeException` to this exception. Valid
RGBA hash pins are unchanged. Unknown flag bits, version/bpp values, quantizers,
WCT checksum treatment and null-stream fallback are not reinterpreted.

## Verification and limits

`WctBoundsTests` covers valid BILZ/LZSS coefficients and Int16 escapes, alpha,
truncated headers/chunks/bitstreams, signed and oversized claims, mismatch of BILZ
actual sizes, array arithmetic, finite allocation bounds for invalid large images,
nonseekable short reads, unused inflation padding, and unchanged legacy bit-read
padding. Existing synthetic RGBA pins, quadratic scratch-allocation tests and
LZSS window/overlap tests provide compatibility checks.

Final verification passed 46 synthetic/related cases, including all eleven new
bounds cases; eighteen original-data cases skipped with their corpus environment
unset. Build/typecheck succeeded with existing repository warnings. The changed
C# style check, new-test whitespace check, CRLF-aware diff check, fidelity register
check and its eleven Python tests passed. No RGBA hash pin changed.

These checks remove malformed-input allocation and truncation defects; they are
not a universal workload budget. Valid large images still require substantial
memory. Valid compressed tails are drained to validate size/checksum, so CPU work
remains proportional to the complete decompressed stream. LZSS retains its valid
decoded output rather than introducing a new stream/window algorithm. Original
corpus validation must be rerun when the private asset volume is readable; no
original assets, binary execution or disassembly are part of this change.

```sh
dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --filter 'FullyQualifiedName~WctBoundsTests|FullyQualifiedName~TextureDecoderIndexingSyntheticTests|FullyQualifiedName~TextureDecoderScratchAllocationTests|FullyQualifiedName~LzssWindowTests'
```

Independent review additionally reproduced an incomplete 1024x1 escaped-coefficient
payload allocating 36,220,592 bytes before refusal. A structural coefficient scan
now rejects it before image-plane allocation; the regression bounds allocation
below 16 MiB. Added tests also validate a corrupted padding checksum after the
retained prefix and compare valid escaped alpha plus BILZ/LZSS padding pixels.
These checks use synthetic inputs; fresh retail compatibility remains deferred.
