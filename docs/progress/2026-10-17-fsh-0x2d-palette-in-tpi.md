## Theme Park Inc's FSH palette codes, read from Game.exe (October 17, 2026)

Format round 4. Theme Park Inc's single SHPI loader handles only palette codes `0x24` and `0x2A` (converter `0x6B7000`).
For a `0x2D` palette it returns with the fill routine's 256-entry stack table unwritten, so the original's colours for the
one such file (`icewall1.fsh`) are undefined. `[EXT:fsh-0x2d-palette]` therefore stays an OpenTPW choice. The live alpha
scan is `0x6B73A0`; the copy `0x6B73F0` is unreferenced. That scan marks `icewall1` as an alpha texture. The `0x24` comment
also had the 24-bit path wrong: it writes nothing, and only the 16-bit path ORs the alpha mask. Docs and comments only;
behaviour is unchanged. Docs: FSH.md.
