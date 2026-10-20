## Ride scripts keep, move and end their sounds (October 18, 2026)

The sound records of the ride scripts' `ADDOBJ` (types 3-9 and the EXT form: 334 of the 644 shipped uses, in 60 scripts) now work as tp.exe's do. The sound is
submitted like an `EVENT` of that type and kept under the record's slot; a repeating event runs as a sentence, a one-shot as a sound that starts when its sample is decoded.
`KILLOBJ` stops the sounds of its slot at once, `FADEOBJ` (113 uses) does the same (the original's fade call is only taken while the sound library's
SoundQuality bit `0x400` is set; further static inspection corrects the earlier fixed-60-percent interpretation to repeated integer decay to zero, with timing and sentence subtype mapping still gated by `APPROX:AUDIO-040`), and `SETOBJPARAM` (the bus engine's parameter 20) gives the sound its parameter.
Keyed records follow their model node every eighth scheduler pass (the pass whose number matches the script's id, `0x551A1C`), and a script's end or its object's removal ends the records it holds.
The records are per script, as in the original, and `FADEOBJ` also ends the particle records of its slot (it skipped them before).

Sounds are presentation: no record is in a hash or in a park save. The original's save does hold the records, but its loader starts no sound, so a resumed park stays silent until the
script starts its sounds again, and a looping ambient no longer blocks a save. The unimplemented-effect counter no longer counts effects that were served (`STARTSCREAM`, `STOPSCREAM`,
`SINGLESCREAM`, `EVENT`, `ADDOBJ` 1-2, `KILLOBJ`, `REPAIREFFECT`): what is still counted is the four light opcodes, the bounce riders' node and base words, and `ADDOBJ`/`EVENT` type 10.
