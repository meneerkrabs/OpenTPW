# Original offline container decoding

Evidence date: October 9, 2026. This began as bounded **read-only container decoding**
and is not a claim of playable TPWS support; later slices added the park import and, on
October 17, 2026, a byte-exact writer (see "Writing original-format saves" in TPWS-PAYLOAD.md).
No original executable has been run. Edition/locale/patch remain unidentified.

## Evidence and supported envelope

The existing SaveReader expected initial bytes `f4 01 00 00` (integer 500), the stamp
whose last byte is 133 and an offline BILZ chunk at `0x60d`. Synthetic tests preserve its successful
decode/text-view behavior. They also reproduced a bug: the first decode disposed
the reader's backing stream, so a second decode failed. Intake previously closed
caller-owned streams and assumed one Read returned the entire file.

The only local real fixture selected for this slice is
`Data/levels/jungle/Easymode.TPWI` from the supplied ISO extraction. It starts with
`90 01 00 00` (integer 400), not 500. Both observed/legacy prefixes are accepted for
this fixed-layout version-133 offline decoder. Their semantic meaning is not
established; do not use the prefix alone to identify edition or payload type.
Actual original TPWS files are not in the selected corpus yet.

The header is written by `tp.exe` (writer `0x415f50`) and by the Theme Park Inc `Game.exe`
(writer `0x440a20`) with the same fields; earlier revisions of this document named two of
them wrongly (a "file type" at `0x604` and a "version 133" at `0x608`): bytes `00 01 22 19` at
`0x604` were the last byte of the header block and the first three bytes of the stamp, and 133
(`0x85`) is the stamp's last byte.

| Offset (Theme Park World) | Size | Field |
| --- | --- | --- |
| `0x000` | 4 | Magic, little-endian: 500 in a TPWS/INTS container, 400 in TPWI (the Theme Park Inc loader only rejects a value above 500 in mode 2); not an edition identifier |
| `0x004` | 1 | Language, 0 to 6 (1 is Spanish); `SaveContainerInfo.Language` |
| `0x005` | `0x500` | Banner, UTF-16, NUL-terminated within the field; `SaveContainerHeader.Banner` |
| `0x505` | `0x100` | Header block. Its first 32 bytes are a 256-bit bitmap of the content the save needs; Theme Park Inc checks it against the installed content at `0x6505f0` and fails with error 8. `SaveContainerHeader.RequiredContent` |
| `0x605` | 4 | Stamp: `htonl(0x01221985)`, file bytes `01 22 19 85`, read big-endian as `SaveContainerInfo.Stamp`; the reader accepts only this value |
| `0x609` | 4 | Online flag, a u32; any nonzero value is explicitly unsupported |
| `0x60d` | 4 | ASCII `BILZ` at the exact offset; no scanning for a convenient later match |
| `0x611` | 4 | Declared decoded length, 1,608,309 in the Jungle Easymode fixture |
| `0x615` | 4 | Declared whole BILZ chunk length, 36,930 including its 28-byte header |
| `0x619` | 16 | Uninterpreted bytes; local uint32 values 15, 9, 0, 0 |
| `0x629` | to EOF | Single zlib stream, 36,902 compressed bytes in this fixture |

### Theme Park Inc version block

The Theme Park Inc writer inserts a **version block** between the magic and the language byte
(writer `0x43ea60`, reader `0x43eba0`), which moves every later offset: a `u8` has-version flag,
then, when it is 1, a `u32` length and that many bytes, the first line of `Data\Ver.txt` including
its NUL. The retail prebuilt holds `01 13 00 00 00` and `"Version: Beta_21\r\n\0"` (24 bytes, so BILZ
sits at `0x625`). The Theme Park Inc loader (`0x43f370`) rejects a different string with error 9 and
accepts a flag of 0 (one byte, no string); `tp.exe`'s writer (`0x415f50`) writes the identical header
without the block. `SaveReader` parses the block generically and keeps the string
(`SaveContainerInfo.VersionText`, `VersionBlockBytes`, `IsThemeParkInc`); it does not compare it with a
particular version.

The byte after the magic is the language of a Theme Park World container and the flag of a Theme Park
Inc one, so it does not identify the layout by itself (a Spanish Theme Park World save has 1 there). The
reader takes the Theme Park Inc layout only when a flag of 1 is followed by a plausible block: a length of 2 to 256
and that many printable ASCII bytes ending in NUL (the Theme Park World banner is UTF-16 text, whose
second bytes are NUL, so it never reads as one). A flag of 0 is taken as the Theme Park Inc
no-string block only when the BILZ chunk sits one byte after the Theme Park World offset, not at it. A
container the reader cannot place in either layout fails with the missing BILZ chunk.

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
headers, language, stamp and online-word rejection, the Theme Park Inc version block (any string, no string, a
Spanish Theme Park World container that must not read as one), exact/over-limit sizes, false decoded lengths,
checksums, dictionary/header corruption, truncated streams and trailing data.
The optional original fixture test checks exact header and both SHA-256 hashes;
without that licensed fixture it reports inconclusive, not a compatibility pass.

The decoded payload's writer-order block split (17 trailing delimiters), the
World block (variables, calendar, per-cell grid, thing list closed at the `WRLD`
delimiter), the typed things and the RideSystem object records are documented in
TPWS-PAYLOAD.md and reverse/SAVE-plan.md (`SavePayloadLayout`, `SaveWorld`,
`SaveThingChain`, `SaveCellGrid`, `SaveRideSystemBlock`, read-only
`OriginalParkImport`). The Coasters block (between the stored `EMAK`/`SAOC`
delimiters) has a bounded structural reader (`SaveCoasterBlock`; TPWS-PAYLOAD.md
"Coasters block"): header, auxiliary links, sections and links are typed, the
train slots decode once the coaster type's counts are known, nothing is imported and nothing is written. A park
started from the shipped save imports its turn,
bank, objects, staff and advisor controller (`OriginalEconomyImport`; values not
imported are reported as SAVE-002 to SAVE-010, SAVE-920 and SAVE-921 (SAVE-923 is retired)); the
other blocks are split by their widths, and the raw dumps of the original's engine structures are not restored (`[EXT:original-engine-state-dumps]`), except the Particles pool, the RSSE script ids, list order and pass counter, and the camera, which a level that starts from the save applies. A level can start from a player's own save (`--load-original-level <theme> --original-save <file>`) instead of the shipped `Easymode.TPWI`.
A Theme Park Inc payload splits on the same principle with its own reader rules
(`SavePayloadProfile.ThemeParkInc`: 45 message lists, a 48-byte camera, an empty UI block and the typed
`ThemeParkIncSaveWorld`; see [THEME-PARK-INC.md](THEME-PARK-INC.md#theme-park-inc-park-save-layout)).
Every block but a Coasters block with a coaster ends where its tp.exe
reader stops (`SaveBlockWidths`, the World included), like the original loader, which runs each block's own
reader and then checks the block's tag (`0x415270`); only that one (and a World that does not read as the original's, a synthetic
payload) is found by its delimiter's bytes, so a payload that splits in more than one way there, or holds its delimiter's bytes more than 256 times, is rejected although the original loader would
read it (SAVE-011).
The four local `scape.omp` files start with `OBJ_`; this is not evidence that they
are terrain tile grids. Original saves can be written: `SaveContainerWriter` and
`OriginalSavePayload` reproduce all eleven corpus saves and the shipped Easymode byte for
byte (the 18,288 uninitialised `0xCD` bytes of Easymode sit in raw runs and are carried;
TPWS-PAYLOAD.md, "Writing original-format saves"). Theme Park Inc containers are not written.

The upstream format document is readable through the GitHub contents API
(`opentpw-docs` `src/formats/tpws-ints-lays.md`). It agrees with the container
fields above (magic 500 for TPWS, the stamp, online
flag, `BILZ` header; it still names the stamp's bytes a file type and a version, see above) and names INTS/LAYS as other file kinds; it says nothing
about the decoded payload. Original runtime fidelity, real TPWS variants and
online saves remain unverified. Nonempty TPW coaster bodies support structural inspection only;
the runtime importer refuses them before constructing objects (see [TPWS-PAYLOAD.md](TPWS-PAYLOAD.md#coasters-block-savecoasterblock)).
