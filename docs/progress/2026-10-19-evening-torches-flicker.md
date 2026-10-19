## The torches' flicker and flame (October 19, 2026)

The evening extension's torches ([EVENING.md](../EVENING.md#torches)) now burn: their flame moves and their light flickers, following the user's decision of October 19, 2026 ("Alleen licht en sfeer", a deterministic flicker preferred; `[EXT:evening-torches-flicker]`). Both are presentation only.

- `TorchFlicker.At(seconds, seed)`: a fixed sum of three sines (0.9, 2.3 and 4.7 Hz, a gentle flicker) with phases from the torch's cell, so each torch flickers the same way in every run and neighbours do not pulse together. The clock is the extension's own play time, which stands still while the park is paused and is never saved.
- The lamp table gains a per-light `flicker` (0 to 0.5) and a per-row `flame` node. The four torch rows flicker by a quarter (the light dips in step with its flame) and name their `flame` node; every other row stays steady.
- `EveningFlames` draws each torch's flame node stretched (height 1 +/- 0.14) and leaning slowly (at most about 3.4 degrees) about its base every frame, by day too, through a presentation-only transform on the drawn parts (`OriginalObject.SetPresentationNodeTransform`); the animator's node transforms, the lamps, the snow cover and the simulation are untouched.

Verification: four new `EveningTorchTests` (the flicker's determinism, bounds, smoothness and neighbour difference; the flame transform's fixed base and bounds; the table's torch rows and refused values; the flame cache following placement and removal), the evening world-hash tests unchanged, and captures of each theme at day, dusk and night outside the repository.
