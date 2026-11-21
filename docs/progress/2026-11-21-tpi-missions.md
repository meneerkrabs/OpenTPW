## Theme Park Inc: the mission controller and the obstacle types (November 21, 2026)

A Theme Park Inc park now runs the executable's mission controller (park thing class 21, `OBJH`). The details are in [TPI-MISSIONS.md](../reverse/TPI-MISSIONS.md).

- **Exit statistics.** The start in the viewed zone (`0x5EEF60`) takes each type's baseline, and the test runs once a game day on every fourth world tick (`0x5EF520`, `0x5EFF00`, `0x74C670`). Types 1, 2, 3, 5 and 8 are measured from the park: cash less loans, challenges won and the guest census. Types 4 (last month's takings, whose ride and facility rows OpenTPW's ledger does not keep), 6 and 7 are never decided, so miss17 cannot be completed.
- **Requirements.** IsMissionComplete (`0x5F0600`), MetRequirements (`0x55A620`) and the obstacle state (`0x5F01D0`). The staff HQ's obstacle slots are not modelled, so an obstacle mission's requirements are never met.
- **Completion.** The wrong-zone refusal (`0x5F0770`) and the completion tick (`0x5F0940`): tickets, the extent for the counter before the advance, the theme, the advance, the next obstacle (`0x6476F0`) and the new statistics. Skip Mission now runs this whole tick.
- **Messages.** 0x23 to 0x29 drive the state machine. Promotions from challenges and the Get Promoted cheat go through 0x25. The objective letter is recorded; its window is not built yet.
- **Save.** The Science continuation save keeps the state, the thing and the current obstacle, which a new park takes from the prebuilt (the 1700 gate). A load registers the research again and drops a pending research message, as `0x5F03C0` does.
- **Obstacle types.** Each zone's `obstacles` archives are read with the map grid parser `0x41AD90` and its symbol table `0x98DF88`.

**No mission completes in play, except through the cheats extension's Skip Mission.** Nothing calls OnMissionComplete, because there is no mission window or Proceed control. Its camera flight is not ported either, and the flight's end is the only sender of message 0x29, which completes the mission. An obstacle mission also needs the research (message 0x23) and the staff HQ's obstacle slots, which OpenTPW does not run. The obstacles are not things of the world yet, and their opening `0x5E8BD0` is not ported.

Verified with these checks:

- The new controller tests cover each exit type, the completion branches, the wrong zone, the messages and the save round trip.
- An end-to-end test over the shipped Science data takes miss00 from 40 guests to mission 1. It checks the 1700 camera request, extent.000 (1259 to 3030 owned squares), the lake as the next obstacle and the catalogue's thinking spaces.
- The Theme Park Inc, save and cheat filters pass.
- The silent, offscreen Science smoke exits 0.

The original game was not launched, and no binary, save bytes or data are included.
