## Track edit and station as tp.exe clears and samples them (October 27, 2026)

Package R9-F. The kart and water track tools clear, cut and replay cells with the original's clear op, the vehicles sample the owner object of each probe, and the edit hover marks its tiles.

- **TRACK-004 retired:** `ClearCell` is the full port of `0x53B280` (pair clear `0x522730` + link `0x522850`, forced, pass (-1, -1) and line-step forms, exit tail restore, replay flag `0x81B46C`).
- **TRACK-007 retired:** the demolition and Backspace replay the corner stacks of the collectors `0x530830` and `0x530B70` line by line (`0x52FE50`, mode `0x35`). A junction a chain passes keeps its other arms, a full crossing takes the line step's rule.
- **TRACK-018 narrowed:** the tool stack holds 1025 points (`0x52FBF0`) and a full stack lets the tool go on (`0x5252D9`); the hover marks the pick and the cut path with the m_break and m_erase tile faces (`0x10F` of `0x52B400`). Left: the pick hover's glide, walls, neighbour raise and blink (BLUEPRINT-003).
- **TRACK-001 narrowed:** a cell without a piece (no track, a station tile, an add-on tile) is sampled through its owner object (`0x452A60`), and the vehicle keeps its height without one. The Halloween and space stations read their faces (node translations baked); the directed edge gate runs in host float, equal to the x87 sequence at control word `0x7F`. Left: the mode-4 mesh builder's node filters.
- **TRACK-005 narrowed:** the station placement's arm writes (`0x5255D8..0x52572C`) do not depend on the exit and entry type ops, which the end state of the dual-arm placement matches. Left: the exit and entry validation and the first line's start on an existing tile.
- Determinism pins are unchanged. No save format change.
