## ARRIVAL-008: the Theme Park Inc weather thing read (November 9, 2026)

The register said the weather thing's per-turn effects — "the sky quality `+0x74`, drops, lightning and thunder" — were not modelled. The routines are now read, and one of the four names was wrong.

The thing is constructed at `0x641420` and its per-turn update is `0x642230`, reached by the thing sweep through the type-15 case `0x637720`. The month handler `0x641F60` applies a pending spell and rolls the next one; both were already ported in `ThemeParkIncWeatherSpells`.

- **Drops** need no random draw: the target `+0x48` comes from the spell's band arithmetic, and the update moves `+0x44` towards it by SpeedOfChange 20 per turn, writing the renderer's drop count.
- **Lightning and thunder** hang off `+0x50` (countdown), `+0x54` (bolts this period), `+0x58` (thunder arming) and the strike point `+0x5C..64`. A strike takes **six world-LCG draws in a fixed order** — x, z, dx, dz, the thunder arm and the countdown reset — so enabling them changes every later guest and staff draw.
- **`+0x74` is not a quality**: it is a sky-texture scroll angle in degrees. The constructor `0x64149B` sets 90.0, a spell nudges it by `r-45` wrapped to 0..360, and the sky renderer `0x687420` reads it once per frame. The overcast scalar is `+0x78`, used only at spell start.

So the item should be split rather than kept as one: the drops with the per-turn state are self-contained and testable now; lightning and thunder are one unit but need a determinism decision because of the six draws; and the sky angle can be stored but cannot be verified until a Theme Park Inc sky and light path exists in the slice.

The declaration in `GuestSimulation` and the summary row in `docs/THEME-PARK-INC.md` now record the read behaviour. No behaviour change in this PR.
