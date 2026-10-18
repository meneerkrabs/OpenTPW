## A held scream resumes the same with or without audio (October 18, 2026)

The opt-in water variant of the native park-resume smoke failed at the save boundary. The water ride's script held a STARTSCREAM
chain, and the runtime snapshot's `HeldScream` was true at capture and false after the restore. `HeldScream` meant "the effects hold
a chain", and the restore started that chain while it built the candidate park, before any audio existed, so `GameAudio.StartScream`
returned nothing. The normal load from the front end, where audio does run, lost the scream as well: `GameFlow.ResumePark` enters the
park's audio after building the candidate, and entering it stops every chain. The held operands and the leaked-chain count followed
the chain too, so a save's content and hashes depended on whether audio ran. A rejected candidate could also leave a started chain
playing in the live park.

The scream is now the script's state. `OriginalObjectEffects` keeps the handle as the script sees it: a STARTSCREAM with a band takes
it, a second start is refused, and STOPSCREAM or the object's stop clears it. It also keeps the band, level and speed word (SCREAMLEVEL
changes the level while the handle is held) and the number of handles that refused starts lost (tp.exe `0x551130` stores 0 over the
handle). The refusal, the saved fields and the scream diagnostics come from this state. Both STARTSCREAM operands and SCREAMLEVEL's
operand are resolved whether or not a handle is held (tp.exe `0x555E5E`, `0x555FDA`). So headless, muted and audible runs save the
same park.

A restore brings the state back exactly and starts no sound. After `GameAudio.EnterPark`, `GameFlow.ResumePark` calls
`Level.ResumeHeldScreams`, which starts each held scream's chain from its operands. A lost handle comes back as a count only; its
chain does not scream again, and a stopped object restores no scream. Older saves load: the script's view (`ScriptHoldsScream`) wins,
and a save without it falls back to `HeldScream`. A save made without audio before this change kept the handle but not its operands,
so its held scream stays silent until the script stops it. `GameAudio.StartScream` no longer refuses on a held chain, because the
script's handle decides.

Verification: `HeldScreamResumeTests` (new) checks that a sound service changes nothing that is saved, that a restore reproduces the
state and starts no chain until `ResumeHeldScream`, that a stopped object or snapshot holds no scream, and both older-save
fallbacks. The scream, runtime-snapshot and particle tests are updated for the new state. The full with-data suite passes (7190
passed, 131 skipped), as do the five default smokes. The water variant of the park-resume smoke passes too: the restore reproduces
the boundary and all 600 continuation ticks and logs "1 held scream(s) restored, 1 playing again". Its restore now fails if a held
scream does not play again after the load while the sound service runs. [AUDIO.md](../AUDIO.md) ("Held screams in a park save") and
[PARK-SAVES.md](../PARK-SAVES.md) describe the behaviour; PARK-SAVES.md no longer says that a screaming ride or a speaking advisor
blocks a save.
