## Ride, shop and sideshow settings (October 16, 2026)

The object windows' settings read in tp.exe and built (RIDES-033, QUEUE-011 and WALK-001 retired, UI-024 narrowed):

- **Ride settings** apply when the window closes or steps to another object, as the original's apply `0x4af440` does (run by the base
  window procedure `0x48d200` on message 0x14 and by the arrows): `SetSpeed` `0x4dd6e0` writes the script's bias word through `0x55a300`,
  capacity and duration follow, duration only for a type with a duration unit. Loaded values are clamped to the slider range (`0x66b088`).
- **Upgrade completion** writes speed, capacity (through `0x4dd7f0`) and duration from the level's Init values when above 0 and sets the
  state of repair to 100 (`0x4df8f0`); some shipped sideshows now start at the clamped capacity.
- **Shops** get the 3-step quality slider with its lamps and the special ingredient amount 0..100 for ingredient types 1..4 (`0x4b06d2`,
  `0x4e2200`), per object or for the whole type (`0x4b0aa0`, `0x4b0ae0`); original saves import `mAmountOfSpecialIngredient`.
- **Sideshows** get the win-chance slider and the prize box; shop and sideshow rows take their labels from the original label routines
  (three UIStrings values were one entry low). The typed price editor stays an extension, limited to 0..500.

New optional, validated save fields: the shop's quality and ingredient amount, and the runtime's ride speed, capacity and duration.
