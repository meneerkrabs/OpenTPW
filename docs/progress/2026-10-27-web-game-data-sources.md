## Browser game data from an archive.org CD image, an address or a local image (October 27, 2026)

The web start screen no longer asks for a folder first. It leads with **Load from archive.org**, one click, which
streams the CD image of the third-party archive.org item `tpworld_202508` once, keeps only the game's data files
(935 files, 322 MB) in the browser's origin-private store with the usual manifest, and starts the game; the next visit
shows Play and fetches nothing. Alternatives sit below it: an address of a `.zip`, `.iso` or `.bin`, a local `.iso`, `.bin` (+ `.cue`)
or `.zip` (chosen or dropped), and the existing game folder. Nothing is fetched or read before a click. OpenTPW still ships no game
data, and only data files are read: the upload's patched executable is never requested or mentioned, and no executable of any image is read.

The pipeline (`opentpw-cdimage.js`, no dependencies) streams bytes, converts raw 2352-byte Mode 1 or Mode 2 sectors to
2048-byte user data, reads the ISO 9660 (Joliet) tree from the first sectors, and hands each wanted file to the store when
its last sector has passed, so memory holds at most one file (32 MB) and nothing is stored whole. A ZIP is read through its central
directory when the server answers ranges (other members are never requested), else parsed in the stream and inflated with
`DecompressionStream('deflate-raw')`. Progress, cancel, a storage-quota check, `navigator.storage.persist()`, restart that skips
files already kept, and separate messages for network/CORS, HTTP, quota and image problems are in the page.

Research on the real item (documented in docs/WEB.md): the ZIP is deflate, the image a single `MODE1/2352` track of 303,008 sectors, the
disc plain ISO 9660 + Joliet with all four languages as ordinary folders (no installer archives to unpack for game data), and 843 raw
sectors in front of the first file are copy-protection data without sync, outside every file. The item's ZIP download redirects to a host
that answers ranges but sends **no CORS header**, so a page cannot read it; the per-member address streams the BIN with CORS but ignores
`Range`. The page uses the latter.

The OpenTPW server's content security policy for the page now allows `https:` in `connect-src` (and nothing else changes: scripts stay the page's own), so a player's chosen data address works on a hosted page; pinned by `ServesTheBrowserGameBesideTheApi`.

Verification: 31 synthetic-fixture unit tests (`tools/test_web_cdimage.mjs`, in CI; ZIP, sectors, ISO, ranges, errors, cancel). With the
real ZIP all 935 kept files are byte-identical (SHA-256) to an existing copy of the same CD. `tools/web-archive-smoke.mjs` loads it in
muted headless Chrome through a ranged ZIP, an unranged ZIP, the file chooser, and a raw BIN with and without ranges (6 to 9 s each),
enters the Jungle park from the kept files and starts a second visit from Play with no request to the image server; it also checks that the
cross-origin failure case gives the CORS message, and `--probe-archive` streamed the first 15 MB of the real archive.org endpoint in the page
and cancelled. Open: only Chrome was run end to end; one full real download through the page (about 713 MB, about 100 s) was not repeated
beyond the probe, so the progress numbers of the default source rest on the measured 7 MB/s and the item's known size.
