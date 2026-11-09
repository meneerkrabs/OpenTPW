## Theme Park Inc's attraction serialiser read (November 9, 2026)

SAVE-006's residual in the current tree is small — an imported attraction takes its name from its type's sign because OpenTPW has no Rename Item dialog (`0x48D370`), its open word comes from the level, and staff claims are imported as saved — but the field set behind it was not recorded anywhere.

The object's loader and writer are the two halves of `0x5DC430`, and the operating fields sit `+0x14` above the Theme Park World layout: speed `+0x6C`, capacity `+0x71`, duration `+0x70`, `mCanLoad` `+0x7C`, the claimed staff id `+0x72` and its turn `+0x74`, the two name lines at `+0xA8`/`+0xEA`, and the three gauges at `+0x60`, `+0x58` and `+0x54`. Theme Park World's third gauge at `+0x4C` is `mFirstInQ` in this build.

The loader stores every one of them into the live object without validation, and each has a reader except the third gauge — which only its constructor, the writer and the loader touch. That confirms the register's "no reader found" for the field of the same meaning, so importing it would round-trip a value nothing reads.

`docs/THEME-PARK-INC.md` now records the offsets, the writer and loader sites, the readers and the gauge verdict.
