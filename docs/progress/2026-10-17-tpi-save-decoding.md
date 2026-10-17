## Theme Park Inc park saves decoded read-only (October 17, 2026)

Static reading of the no-CD `Game.exe` against the one Theme Park Inc park save, `prebuilt.TPWS`. The container header
fields were misnamed: the "file type" at `0x604` and "version 133" at `0x608` are the end of the header block and the
stamp `htonl(0x01221985)`; the language byte follows the magic (and, in Theme Park Inc, the version block), so a Spanish
Theme Park World save (language 1) was misdetected as Theme Park Inc. `SaveReader` now parses the version block
generically (any string kept), reads the language, stamp and online word, and tells the two layouts apart by the
block's shape. A Theme Park Inc payload splits by reader rules (`SavePayloadProfile.ThemeParkInc`: 45 message lists,
48-byte camera, empty UI) instead of a delimiter search, and its World block is read into typed records
(`ThemeParkIncSaveWorld`: variables, control manager, staff pool, calendar, status-byte cell map, the marker-checked thing
chain with typed attractions including their history rings, the bank and the small things, and the special rides) and
consumes exactly to its delimiter. The science prebuilt import uses it: scene records are joined to attractions by mesh
instance id and Info.Id, replacing the exact-23-records requirement. Raw and inferred parts are SAVE-940 to SAVE-944.
Docs: SAVE-CONTAINER.md, THEME-PARK-INC.md, TPWS-PAYLOAD.md, FORMAT-BACKLOG.md.
