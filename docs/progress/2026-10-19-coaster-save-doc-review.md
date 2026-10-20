## Coaster save reader docs after review (October 19, 2026)

Follow-up to the coaster save reader correction (#687) from its post-merge review: the saver skips only the station and end nodes (`0x437426`/`0x437605` test descriptor bit 0x10; the "pylon-less nodes" were never shown to sit mid-chain), the slot allocator `0x468760` stores a saved slot other than -1 as given (no allocation, fallback or global change, so "preferred slot" is gone from TPWS-PAYLOAD.md, `SaveCoasterList` and the SAVE-922 text), and the loader's match path sets `ctl |= 2` (`0x4386BA`) before `|= 0x4000`.
