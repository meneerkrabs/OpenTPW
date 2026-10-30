## Theme Park Inc science smoke runs again (October 30, 2026)

- **Floor art decodes by file type.** The floor composites under a placed object
  read each texture slot's art through `OriginalObject.DecodeSlotArt`, which sent
  every game-data file to the WCT reader. Theme Park Inc's slots resolve to `.fsh`
  and `.tga` files (the science Log Mill's `ns_cnr04` is
  `sharetex/ns_cnr04.fsh`), so the science world stopped loading with "WCT
  dimensions must be positive". The art is now decoded by its extension, as the
  object textures already were; a game-data `.wct` still goes through the texture
  pack lookup. A with-data test decodes every texture slot of the three Theme Park
  Inc worlds this way.
- **No staff role is read without a handyman.** The slice has no staff world, so
  each park turn ends in the headless handyman dispatch, which read the handyman
  role before looking for a handyman. Theme Park Inc's settings hold no staff
  roles, so the first turn threw. The dispatch now returns when no handyman is
  free; Theme Park World parks behave as before.
- **The slice HUD's limits line matches the capability note again.** The note
  gained "in-game interface (only its Personnel File start screen)" with the
  Personnel File screen, but the HUD's translated line kept the old wording, so
  `--tpi-smoke-test` refused it. All eleven languages now say it, and a unit test
  pins the English line to the note.
