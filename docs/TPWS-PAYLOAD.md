# Decoded TPWI/TPWS payload: marker layout

Evidence date: October 9, 2026. This is a **read-only marker inventory** of the
decoded BILZ payload (see SAVE-CONTAINER.md). Section contents are not decoded.
It is not a park importer, not save writing and not proof of TPWS support.
No original executable has been run.

## Fixture inventory

| Location | TPWS/TPWI/INTS/LAYS files |
| --- | --- |
| Loose `Data` tree (all extensions) | 1: `levels/jungle/Easymode.TPWI` |
| `TPWORLD.ISO` (7z listing, 2,783 files) | 1: `Data/levels/jungle/Easymode.TPWI`, byte-identical to the loose copy |
| WAD member names / ISO-wide filename strings | none observed |

No TPWS, INTS or LAYS file exists in the available corpus. Every conclusion
below therefore rests on one fixture and must be revalidated on others.

Pinned hashes (SHA-256):

- Container: `6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a`
- Decoded payload (1,608,309 bytes): `a3c9a28252c37ad49a8eb78e4a0c5e1d5229d01548fa35801db67015d2589173`
- Untagged prefix (first 1,495,462 bytes): `58521ff5b0804eeea19358ac68cc3fc219babb8bb3672e3687c892c21d75e3e5`
- Original `TP.ICD` used for the static tag table: `df66561b91794368674a35abade40661398cd703697089c781a685a533fe550e`

## Upstream document

The upstream page (`opentpw-docs` `src/formats/tpws-ints-lays.md`, read via the
GitHub contents API) describes only the outer container: magic, copyright text,
file type `00 01 22 19`, version, online flag, `BILZ` header and zlib stream.
"INTS" and "LAYS" there are **file kinds** (initial save; `upload.LAYS` online
save), not payload sections. It documents nothing about the decoded payload.

## Section markers

The payload has no observed tag+length chunk framing. What is verified:

1. Static bytes of the original `TP.ICD` (inspected, not executed) contain one
   contiguous 68-byte table of 17 four-character values, in this order:
   `DLRW CSPS TRAP SSEM KART RYLF ESSR EMAK KOLC TNAV SYSG SYSR SAOC NUOS SVDA STHC CSDA`
   (bytes as stored; read as little-endian uint32 multi-character constants
   they spell e.g. `WRLD`, `SPSC`, `PART`, `MESS`).
2. Each of those 17 byte sequences occurs **exactly once** in the decoded
   Easymode payload, at unaligned offsets, all after a large untagged prefix.

Observed layout. "Span" is the distance to the next marker including the 4
marker bytes; it is derived, not a length field read from the file.

| Marker | Offset | Span | Notes (observation only) |
| --- | --- | --- | --- |
| (prefix) | 0 | 1,495,462 | Untagged; mostly 84-byte-stride records with variable-size interruptions |
| `DLRW` | 1,495,462 | 5,456 | Immediately followed by bytes `TPCS` |
| `CSPS` | 1,500,918 | 76,256 | Followed by `LCTP`; contains 16-byte names such as `WaterFall`, `tButton`, `Tag2` |
| `TRAP` | 1,577,174 | 270 | |
| `SSEM` | 1,577,444 | 12 | |
| `KOLC` | 1,577,456 | 8 | |
| `TNAV` | 1,577,464 | 40 | |
| `SYSG` | 1,577,504 | 17,530 | |
| `SYSR` | 1,595,034 | 48 | Third uint32 is 44 (= span − 4); ends with ASCII `20:25:29Oct 21 1999` |
| `KART` | 1,595,082 | 456 | Followed by `FLY_` |
| `RYLF` | 1,595,538 | 10,860 | Contains `PAD_` fill, length-prefixed object names/paths ending `OBJ ` |
| `ESSR` | 1,606,398 | 44 | |
| `EMAK` | 1,606,442 | 20 | |
| `SAOC` | 1,606,462 | 376 | Followed by `ADV_` |
| `SVDA` | 1,606,838 | 1,449 | Contains length-prefixed `\data\...\cat_*` sound/speech/music paths |
| `NUOS` | 1,608,287 | 6 | |
| `STHC` | 1,608,293 | 8 | |
| `CSDA` | 1,608,301 | 8 | Last 4 payload bytes are zero |

Payload order differs from table order (`KOLC TNAV SYSG SYSR` precede `KART`;
`SVDA` precedes `NUOS`). Markers are not followed by a consistent size field
(only `SYSR` happens to carry a matching value). Sub-tags such as `TPCS`,
`LCTP`, `FLY_`, `ADV_` and an unrelated `RSSE` inside `RYLF` are not in the
table and are not treated as sections. The `SYSR` text has `__TIME__`/`__DATE__`
shape and the date matches the ISO file date (1999-10-21); its meaning is not
established. Section names, subsystem roles and the prefix (likely terrain/tile
state) are **unverified interpretations** and are not encoded in code.

## Reader (`SavePayloadLayout`)

`SavePayloadLayout.Parse( ReadOnlySpan<byte> )` / `Parse( SaveReader )`:

- Payload capped at 64 MiB (same as the decoded-payload cap).
- Searches only for the 17 known byte sequences. Each must occur exactly once;
  a missing marker, a duplicate (ambiguous, e.g. a false match inside opaque
  data) or overlapping markers throw `InvalidDataException`. No partial result.
- Reports payload length, untagged prefix length and sections sorted by offset
  (tag bytes, uint32 value, offset, derived span). Spans tile the payload exactly.
- Any order is accepted; `MatchesObservedEasymodeOrder` reports whether it
  equals the single observed order. Order is not used to validate.
- Contents are not exposed or interpreted beyond these offsets.

## Tests

`TpwsPayloadTests`: synthetic layouts (observed and table order, through
`SaveReader`), empty payload, missing/duplicate/false/overlapping markers and the
size cap. The private fixture test pins the payload hash, prefix hash and all
17 offsets/spans; without `OPENTPW_GAME_PATH` it is inconclusive, not a pass.

## Remaining gates

- Any second fixture (an actual TPWS, another TPWI, INTS or LAYS) to test
  marker uniqueness/order and spans; online (LAYS) payloads are unsupported.
- Schema for each section and for the untagged prefix, with known-state
  reference saves; until then all contents stay opaque.
- A read-only importer only after section schemas are verified.
