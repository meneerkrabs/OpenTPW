## Coaster link marker, wavelet rounding, stink effect and WALKOFF effect (October 16, 2026)

A miscellaneous pass over open approximations, read in tp.exe via REA:

- **COAST-035** (narrowed, partly built): a hovered chain start is now the closing target (node flag 0x800000, or 0x804000 when the
  closing link is refused, 0x4360c4) and the start's own tag pairs take the blue or red marker (0x43f4e0). The 300-unit reach was
  already built, so that clause is removed. Open: the reader of the 0x200000 marks set by 0x4360c0.
- **COMPAT-018** (narrowed, partly built): the wavelet YCbCr conversion rounds to nearest-even as 0x5935A0 does, including the
  512-entry clamp wrap (-128..383). Open: where the header scale enters 0x592E60, the alpha conversion and the x87 precision control.
- **COMPAT-001 / COMPAT-003** (narrowed): the PC composes 256x256 and packs the top 128 rows; the strip resampler 0x5E80D0 has no
  upsampling branch, so the nearest-row relief upsampling is OpenTPW's own.
- **COMPAT-010** (narrowed): option variables sit at 0x785874 + 4 x option index, so TEXTUREFILTERING is [0x785878].
- **PTCL-908** (partly built): a stinkbomb on the ground starts particle effect 50 at its sprite and ends it when swept
  (0x4D95BD, 0x4D9620). **PTCL-905** (narrowed): the batch depth sort has no visible effect.
- **TOUR-011** (narrowed, partly built): WALKOFF action 2 creates effect 0x13 at the slot's start node (0x5571A0, 0x5572EB).
  The walk-on hold is not built.
- **RIDES-021** (narrowed): the model triangle test 0x45D090 is read.
- Unchanged readings: BLUEPRINT-001/-003, PTCL-900/-909, RIDES-019, COMPAT-007/-032. Blocked: COMPAT-002/-004/-006, MD2-900/-901,
  PTCL-904, RIDES-040.

The determinism pin does not change (the new effects are presentation only and draw nothing from the world RNG). The sign hash
baselines moved with the wavelet colour rounding (28 half hashes of 14 signs, plus five table-named signs in `RidesRound2Tests`).

User-approved split to extensions (behaviour unchanged, labels and text only): `[EXT:sign-relief-upsample]` (COMPAT-003, the
nearest-row upsampling the original's resampler 0x5E80D0 cannot do) and `[EXT:blueprint-phase-step]` (BLUEPRINT-001, the ghost ripple
phase steps 0.1 per 1/30 s of real time instead of per rendered frame). The remaining parts of both IDs stay open.
