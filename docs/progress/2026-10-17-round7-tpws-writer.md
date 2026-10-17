## Original-format saves can be written (October 17, 2026)

A park save in the original's format is now written byte for byte. `ClassicZlibDeflater` reproduces the zlib 1.1.3 stream the game's BILZ writer makes (`deflateInit2(-1, 8, 15, 9, 0)`, one
`Z_FINISH`, tp.exe `0x619280`; the stock `ZLibStream` is another zlib), `SaveContainerWriter` writes the header (`0x415F50`) and the BILZ chunk, and `OriginalSavePayload` models the 19 blocks with the
fields their loaders read and writes them in the payload writer's order (`0x4164C0`, its 17 delimiters read from `0x6FD8B8..0x6FD8F8`; the World writer `0x516C80`'s 26 labelled variables and thing records
checked on its own label calls). Reading each of the eleven private corpus saves (seven parks and autosaves, four `restart.INTS`) and the shipped Jungle `Easymode.TPWI` into the models and writing them again gives
identical payload and container bytes in all twelve.

`OriginalParkExport` writes an OpenTPW park's economy back into a template original save of the same level: the park turn, calendar caches, open flag, world generator state, the bank (account words and
loans, `SaveBank.ToBody`) and the object controls' first-purchase turns; everything else is the template's. Importing each original into an economy and exporting onto its own bytes reproduces the file except for the
first-purchase stamps the original's own load writes too (`0x4DB690`); the four restart saves come back unchanged, and the export reads back through the loader's split and both importers to the same economy.
Placed objects, built paths, staff, guests and a park without a template are not exported (TPWS-PAYLOAD.md, "Writing original-format saves", lists what each needs). SAVE-012's unknown now says so; no
APPROX ID is retired. Exposing the export to players is left to the user (it needs the player's own original save as the template).
