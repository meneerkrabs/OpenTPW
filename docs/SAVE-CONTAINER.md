# Original offline container decoding

Evidence date: October 9, 2026. This is bounded **read-only container decoding**,
not original park import, original save writing or a claim of playable TPWS support.
No original executable has been run. Edition/locale/patch remain unidentified.

## Evidence and supported envelope

The existing SaveReader expected initial bytes `f4 01 00 00` (integer 500), version
133 and an offline BILZ chunk at `0x60d`. Synthetic tests preserve its successful
decode/text-view behavior. They also reproduced a bug: the first decode disposed
the reader's backing stream, so a second decode failed. Intake previously closed
caller-owned streams and assumed one Read returned the entire file.

The only local real fixture selected for this slice is
`Data/levels/jungle/Easymode.TPWI` from the supplied ISO extraction. It starts with
`90 01 00 00` (integer 400), not 500. Both observed/legacy prefixes are accepted for
this fixed-layout version-133 offline decoder. Their semantic meaning is not
established; do not use the prefix alone to identify edition or payload type.
Actual original TPWS files are not in the selected corpus yet.

| Offset | Size | Checked/observed value |
| --- | --- | --- |
| `0x000` | 4 | Initial little-endian value 400 in TPWI; 500 in legacy synthetic envelope |
| `0x604` | 4 | Raw file-type field; local bytes `00 01 22 19` = `0x19220100`; semantics unverified |
| `0x608` | 1 | Version 133 only |
| `0x609` | 1 | Offline 0; nonzero explicitly unsupported |
| `0x60a` | 3 | Uninterpreted bytes; not assumed universal zero padding |
| `0x60d` | 4 | ASCII `BILZ` at the exact offset; no scanning for a convenient later match |
| `0x611` | 4 | Declared decoded length, 1,608,309 in this fixture |
| `0x615` | 4 | Declared whole BILZ chunk length, 36,930 including its 28-byte header |
| `0x619` | 16 | Uninterpreted bytes; local uint32 values 15, 9, 0, 0 |
| `0x629` | to EOF | Single zlib stream, 36,902 compressed bytes in this fixture |

The decoded-size/chunk-size interpretation agrees with independent Python zlib
decoding of this exact fixture, including checksum, completed stream and zero
unused bytes. It is not generalized evidence for every game version/save layout.

## Limits, ownership and failure behavior

`SaveReader` accepts readable streams, including nonseekable short-read streams,
starting at their current position. It leaves caller-owned streams open; the
path constructor closes the file it opens. The reader implements IDisposable;
repeat reads before disposal work and reads after disposal fail explicitly.
The compatibility `buffer` accessor returns a copy, not mutable internal bytes.

Input and decoded data are each capped at 64 MiB. Per-reader limits can be lowered,
not raised beyond those hard caps. Larger legitimate files need evidence and a
reviewed limit change; this is not a guarantee that all original parks fit.

No allocation uses an unchecked uint32 header length. Inflation uses bounded
chunks, rejects output exceeding the declared length, verifies the final exact
length and requires zlib completion/checksum with no trailing input. Declared
chunk size must match the entire remaining file. Dictionary-based streams,
unknown versions, online flags, corrupt/truncated data and trailing bytes are
rejected. No best-effort partial payload is returned on error.

Header inspection does not alone certify valid compressed content; `ReadFile`
performs inflation validation. The CLI runs both before reporting success.
It prints metadata/hashes only; it does not dump proprietary payloads, write to
the original files, initialize graphics, or create game save/cache directories.

## Regression coverage and remaining gates

Synthetic tests cover valid legacy/TPWI envelopes, empty payload, repeated reads,
null-stripped text view, ownership, short reads, current stream position, malformed
headers, online/version rejection, exact/over-limit sizes, false decoded lengths,
checksums, dictionary/header corruption, truncated streams and trailing data.
The optional original fixture test checks exact header and both SHA-256 hashes;
without that licensed fixture it reports inconclusive, not a compatibility pass.

The decoded payload's writer-order block split (17 trailing delimiters), the
World block (variables, calendar, per-cell grid, thing list closed at the `WRLD`
delimiter), the typed things and the RideSystem object records are documented in
TPWS-PAYLOAD.md and reverse/SAVE-plan.md (`SavePayloadLayout`, `SaveWorld`,
`SaveThingChain`, `SaveCellGrid`, `SaveObjectList`, read-only
`OriginalParkImport`). The Coasters block (between the stored `EMAK`/`SAOC`
delimiters) has a bounded structural reader (`SaveCoasterBlock`; TPWS-PAYLOAD.md
"Coasters block"): header, auxiliary links, sections and links are typed, the
train slots decode once the coaster type's counts are known, nothing is imported and nothing is written. A park
started from the shipped save imports its turn,
bank, objects, staff and advisor controller (`OriginalEconomyImport`; values not
imported are reported as SAVE-001 to SAVE-010 and SAVE-920 to SAVE-923); the
other blocks stay opaque.
Every block but the World and a Coasters block with a coaster ends where its tp.exe
reader stops (`SaveBlockWidths`), like the original loader, which runs each block's own
reader and then checks the block's tag (`0x415270`); only those two are found by their
delimiter's bytes, so a payload that splits in more than one way there, or holds one of
their delimiters' bytes more than 256 times, is rejected although the original loader would
read it (SAVE-011).
The four local `scape.omp` files start with `OBJ_`; this is not evidence that they
are terrain tile grids. No save writer exists: original saves are never written,
because round-trip evidence is missing and Easymode alone holds 18,288
uninitialized `0xCD` bytes, so byte equality cannot be the criterion
(SAVE-plan §8.3).

The upstream format document is readable through the GitHub contents API
(`opentpw-docs` `src/formats/tpws-ints-lays.md`). It agrees with the container
fields above (magic 500 for TPWS, file type `00 01 22 19`, version byte, online
flag, `BILZ` header) and names INTS/LAYS as other file kinds; it says nothing
about the decoded payload. Original runtime fidelity, real TPWS variants and
online saves remain unverified. Nonempty TPW coaster bodies support structural inspection only;
the runtime importer refuses them before constructing objects (see [TPWS-PAYLOAD.md](TPWS-PAYLOAD.md#coasters-block-savecoasterblock)).
