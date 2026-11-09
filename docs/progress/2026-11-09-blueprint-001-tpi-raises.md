## BLUEPRINT-001: Theme Park Inc raises the terrain too (November 9, 2026)

The register said a Theme Park Inc world "keeps the unraised terrain", and OpenTPW's `BlueprintMarkers` comment repeated it. That is wrong: Theme Park Inc applies the same type-gated, quantised terrain raise for its ghosts.

Its placement handler `0x67AD00` (6010 bytes, reached through the import thunk `0x403DEB` from 101 sites, so a direct `e8` scan finds none) does exactly what the Theme Park World side does. The gate helper `0x67AC80` tests the record types `0xB`, `0xC`, `0x10`, `0x11` and `0x19`; the handler then tests the map types `7`/`0x1E` and `4`/`9`/`10`; the raise is `ceil(h/10)*10` of the cell ground, read through `0x4A8760`, the twin of tp.exe's `0x452AE0` — same 10-unit cell pitch, same view rotation, and the same byte-per-cell height grid scaled by `0.392157` (the only occurrence of 100/255 in the binary, at `0x93AFC4`). Unequal neighbouring raises are filled in steps of 10.

Two corrections follow for the record. The earlier "Theme Park Inc keeps the terrain" was an OpenTPW limitation described as the original's behaviour, so the register text no longer says it; what stays open is only whether OpenTPW's own Theme Park Inc path supplies those cell and record types to `BlueprintHeights`, which is a data question for that slice. And the register's type list belonged to tp.exe's `0x5330CA`..`0x533F69` family, not to the tile ghost's `0x536CA0`, which tests only `0xB`, `0x10` and `0x19`.

The `[APPROX:BLUEPRINT-001]` text and `docs/reverse/BLUEPRINT.md` now record this. No behaviour changes in this PR: the port itself depends on the type question above and would be a separate change.
