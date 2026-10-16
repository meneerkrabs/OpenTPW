# WAD directory and input boundaries

The WAD reader retains the existing 88-byte header and contiguous 40-byte
record interpretation. Before interpreting a record it verifies that a
nonnegative declared count fits the available bytes. Before seeking to a name
or exposing a member it checks the full unsigned offset-plus-length in wide
arithmetic against the physical archive length. Name lengths must include at
least the existing terminator slot; terminator contents, versions, compression
codes and the unused file-list fields retain their prior interpretation.

Input reads consume all remaining bytes from the caller's current position.
Seekable short-reading streams therefore work; premature EOF reports
`InvalidDataException` rather than supplying zero bytes to the parser. Lengths
that cannot fit the managed byte-array representation reject before allocation;
this is a representation boundary, not an inferred archive-format size cap.
WAD stream construction retains its existing ownership: it consumes and closes
the supplied stream, including on a read failure.

A path-backed archive still releases its initial full buffer after directory
construction and lazily reads/decompresses/cache-stores requested members. The
initial snapshot used for directory parsing is unchanged; this is not a new
seek-only parser or eager member decoder. The existing `GetData` API's explicit
past-end zero padding remains supported for direct callers. Directory entries
claiming bytes outside the archive now reject instead of becoming synthetic
member contents or large allocation requests.

Every `OpenFile` stream has its own position and lifetime. A decoded member's
cached byte array keeps its existing lifetime after archive disposal; an uncached
member or direct backing read refuses a disposed archive. Existing lazy cache
reuse and directory performance protections remain intact.

Independent `WadArchiveBoundaryReviewTests` also covers a source stream starting
after a prefix, valid aliased filename/member spans with separate member caches,
one-byte empty names and zero-length EOF members. Synthetic changes to ignored
version/file-list/decompressed-size fields, non-RefPack compression codes and the
filename's final byte preserve the reader's existing interpretation.

`WadArchiveBoundaryTests` uses tiny synthetic archives to reproduce the prior
short-read failure, hidden premature EOF, wrapped stream length, negative/huge
counts and unsafe unsigned ranges before the fix. Valid controls cover memory
and path reads, member stream isolation, direct-read padding and cached-data
lifetime. Focused WAD/on-demand/filesystem checks pass without original assets.

Merged onto main, the original-data suite passes with the original archives (13 October,
2413 tests): every member of every shipped WAD reads as before
(`ArchivesFromAPathReadTheSameMembersAsArchivesInMemory`,
`EveryCompressedMemberOfTheOriginalArchivesDecodesAsBefore`, `ReadsTheOriginalCdArchives`),
so no original archive trips the new range or name checks. One behaviour change is
deliberate: an archive with a member that points past its end is now refused as a whole
instead of mounting with that member read as zeros; a dropped-in bonus WAD like that no
longer mounts. Valid large archive construction still requires its initial buffer; no
arbitrary resource cap, decompression policy, path-name policy or general reload
transaction was introduced. A reopened read (`GetData` on a path-backed archive) checks the
requested range before it reserves the member's buffer.

## Declared decompressed size (tp.exe, October 16, 2026)

A directory entry's u32 at +0x18 is the member's decompressed size. tp.exe's tree builder (0x637690)
reports it as a compressed member's size (a raw member's is its data length), and opening a Refpack
member (0x61A800 → 0x638B20) allocates exactly that many bytes for the decoder 0x641F10, which takes no
capacity and checks nothing; its return value (the header's 24-bit size) is discarded. A larger real
output would overflow that buffer and a smaller one leave its tail unset; neither happens in the corpus:
in all 6,804 Refpack members of the 315 archives the declared size, the Refpack header size and the
decoded length agree, and the 8,501 raw members declare 0 (`RefpackTests` checks every compressed member).
`WadArchiveFile.Size` and `GetFileSize` report the declared size without decompressing. Compression type 1
fails to open and type 2 is a second codec (0x638C90 → 0x5F81D0) that no shipped member uses.
