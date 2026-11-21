## Theme Park Inc: staff HQ slots, training and research (November 21, 2026)

The staff HQ's twenty obstacle slots, staff training and the research lab with its researchers are ported (docs/reverse/TPI-STAFF.md §12).

- **Staff HQ** (`ThemeParkIncStaffHeadquarters`): the slot table from the mission's `ObstacleStaff` (`0x634600`), ability-only assignment (`0x634B00`), release and replacement on messages 0x21/0x2A
  (`0x634730`), the trained-staff counts and assignment on 0x2B, and the rebuild on 0x23/0x24 (`0x634D20`). The mission controller's MetRequirements (`0x55A620`) now reads these slots; the
  prebuilt keeps its saved slots. The staff type codes are the role order (r7 had guard and entertainer swapped).
- **Training** (`ThemeParkIncTraining`, staff world states 7 and 8): abilities by role (`0x62B6C0`), the career's discovered abilities and 10000 experience (`0x639B20`), the room capacity of three
  (`0x639F40`), the walk to the training room with message 0x2A, 300 updates (`SkillIncrement` 10 to `ProgessUpperLimit` 3000), the reward (motivation 100, +5000 experience, the agency's
  percent), the finish with message 0x2B, and the cancel on a removed room. The text HUD's staff panel opens a hired member's ability list.
- **Research** (`ThemeParkIncResearch`): the prebuilt's lab and object controls, the tick every sixth world tick from listed scientists in state 24, the cash per brain point, message 0x11 (the
  career's CDK flag, a Park Expansion's 0x23), the obstacle project (`0x61CBF0`), the consultant pick, the catalogue's researched gate, and the panel's rules as text HUD rows. Researchers are hired
  and placed with their start, look for work, room finder and states 25/24.
- **CRT `rand()` decisions (user, 2026-11-21)**: the level project after a completion (`0x61FE70`) is ported and takes no draw for the discarded `rand()` at `0x61FF1A` (always 1 cash per brain
  point); the training walk's stuck `rand()` only picks a thought bubble that is not drawn, so no draw is taken. A new zone's lab fill (`0x61FD50`, `rand() % 2 + 1`) is not ported: it waits for the
  user's decision at the zone package.

- **Review fixes**: a Science save from before the HQ and the lab is refused by its missing HQ with the start-again message, before any asset check (docs/PARK-SAVES.md); the staff
  route `0x62EB10` (the closed park's entrance refusal and the walk mask) and the cell steer `0x60B7F0` are ported, so a refused walk to the training room searches again with mask 1; the lab tick,
  the advance's cash, the percent done and the researcher's step round every x87 operation to a single (control word 0x7F after DDSCL_FPUSETUP, `0x6BF33F`); a destroyed staff room's messages
  reach the lab (0x21) and the staff (0xA) at the click, as `0x637C40` delivers them at the demolish (`0x4D30F8` → `0x660D80` → `0x666DA0`), not at the turn's deferred free; the lab's save keeps the
  object types' research words, so a headless economy completes projects; the staff-destroyed listeners `0x61DAD0` and `0x634730` are removed (no member leaves the park in OpenTPW); the mission window's Proceed sends message 0x24 through one economy entry point
  (`ParkEconomy.PressThemeParkIncMissionComplete`), so the staff HQ rebuilds before the controller hears it, and the window reads the lab's Park Expansion progress (`0x61E5E0`) and the HQ's slots.

Verified: new `ThemeParkIncStaffHqTests`, `ThemeParkIncTrainingTests` and `ThemeParkIncResearchTests`, the Theme Park Inc and save filters, and the silent, offscreen Science smoke (which now checks the
HQ slots, the seeded lab and the research row).
