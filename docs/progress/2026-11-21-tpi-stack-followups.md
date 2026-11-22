## Theme Park Inc: follow-ups from the stack review (November 21, 2026)

Small corrections after the Theme Park Inc staff, challenge, boundary, career and mission stack (#974 to #980) was merged.

- **One link resolver.** The check that keeps the player file out of the game folder now uses the same symbolic-link resolver as the sign export (`PathLinks.ResolveLinks`, moved out of `SignExport` so the browser build has it too). The copy in `GameFlow` is gone, together with its wrong comment about link cycles.
- **Tests without game data.** The folder tests (the symbolic-link case, skipped on Windows, where creating a link needs a privilege) and the source contract that only the replacing teardown tears a park down for Continue and Load layout now run without `OPENTPW_TPI_PATH`. A new test checks that the replacing teardown holds the flag the leave's save tests, and releases it when the teardown throws.
- **Docs.** `THEME-PARK-INC.md` and `RUNNING.md` no longer say the slice runs only money, cleaners and challenges: the career and the mission controller run too, and the player file is the one native format written. The economy section's anchor is now `#theme-park-inc-economy`.
- **Net worth.** Theme Park Inc's own net-worth routine `0x5AFFB0` was read against the cell-point rule the slice shares with Theme Park World. It skips unowned squares with the same bit 0x40 (`0x5B0017`), scores the same cell types, skips condemned objects (`0x5E3970`) and adds the same upgrade values; only the square size (72 bytes) and the cache offsets differ.

Verified: the player-folder, player-file and sign-export tests pass, the register stays at 76 open IDs, and the progress and CI contract checks pass. The original game was not launched, and no binary or data is included.
