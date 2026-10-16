## Format round 2: sound lengths, WAD sizes, sprite banks, terrain diagonals (October 16, 2026)

Three read-only research agents searched the decrypted tp.exe (REA) for the format questions still open; the
key addresses were re-read before building on them.

- **SDT:** entry +32 is the playback length in PCM bytes (0x6C82C0 copies it when type bit 0x04 is set, the fill
  routine 0x6C83C0 stops there). Sound events and advisor speech now play exactly that length; the decoder's
  tail (about a frame plus the encoder delay) is no longer heard. All 5,674 entries carry it.
- **WAD:** the declared decompressed size is what the original allocates (0x638B20) and reports; it equals the
  decoded length in all 6,804 Refpack members. `GetFileSize` returns it without decompressing.
- **Sprites:** ESP byte 0x10C chooses the full-detail FPC bank (0x5410D3), so Space's costume set SPR_SK now draws
  from its TPC like the original; the frame words are pixels per sprite unit (0x5890E0); 0x10D is never read; the
  ESP's last four slots are bytes, not animations.
- **Terrain:** heightfield cell bit 0x4 picks each cell's triangle diagonal (height lookup 0x452560, renderer
  0x56F670); ground heights (`ModelHeightfield.GetHeight`, guests and the free-walk camera) and the terrain mesh
  follow it. Bits 0x8–0x40 turn and mirror the ground texture (not drawn yet).
- **MD2:** node bit 0x10 hides only the node's own mesh, as the draw walk 0x57A530 does (RIDES-042 retired); the
  group-0 box is the animated culling box; texture flag 0x40 marks multi-frame slots.
- **MAP:** the loader ignores the five header words and never opens jungle `terrain.map`; bit 0x04 goes to a tile flag
  nothing reads. The tile set-up does not consult `Blocked`, which OpenTPW still refuses to build on.
- **MTR:** no reader of MTR files was found in the decrypted tp.exe either. **TQI:** the live video decoder is located (the documented
  `cseg` routines are dead code); reproducing it bit-exactly is the next step.
- README and docs/FORMAT-BACKLOG.md: Sounds and Map Data are now ✅; ✅ now means every field the original reads is
  understood (fields it is shown to skip may stay raw).
- An independent Codex review's findings are addressed: the playback length comes from the same header read as the audio
  and bounds the decoded PCM without a second copy, only 16 ESP entries are animations, and the README and MTR claims
  were narrowed to what the evidence shows.
- Verification (macOS arm64, original data): the full test suite passes (4,648 passed, 109 skipped). No native render was run.
