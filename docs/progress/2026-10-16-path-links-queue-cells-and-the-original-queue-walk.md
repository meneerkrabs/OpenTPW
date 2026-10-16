## Path links, queue cells and the original queue walk from tp.exe (October 16, 2026)

Paths and queues were read in tp.exe via REA and built; six unknowns are retired, six narrowed and two blocked:

- **Path link writer** (PATH-004 retired): the commit's link pass `0x52744E` calls the writer `0x5348D0`, and the clear uses the unlink `0x5367A0`; the area
  clear tool (tool `0x3A`, the Clear key `0x40C5E0`, clicks `0x525DA3`, clear `0x52BDD0`) is built (PATH-005 narrowed around the missing owner word). The writer's
  cat_ui sound 139 at (10x, 0, 10x) from `0x5348E0` is played (AUDIO-023 retired).
- **Queue cells and links** (QUEUE-003 narrowed): queue cells carry the links the queue line's link pass writes (cell `+0x0C`, queue link `+0x0D`, the back link
  `0x535C7E`); a line ending on a path cell is accepted as the validator's code 8 (`0x535D06`, PATH-008 retired). The queue models are drawn from the theme's
  table `0x7630B0` with the placement `0x522900`/`0x5229E0` (PATH-015 narrowed).
- **The original queue walk** (QUEUE-006 retired): guests reach the back of a queue by the original's queue search and the edge test `0x4D8750` over those links
  instead of OpenTPW's earlier search.
- **Entering the path tool** (PATH-001 retired): an entry click calls SetMode(1) without a start (`0x4879D0` → `0x52F200`) and the next click sets it; the
  refused ghost gate `0x524A63`, the start check `0x527222`..`0x5272B8` and the lay sound `0x5271B7` follow.
- **Path textures** (PATH-014 retired): the chooser roll (`0x535FCC`/`0x53602C`) and the refresh `0x5365D0` are a presentation stub with its own CRT generator, seed 1.
- **Other**: ECON-049 (`0x4DE4A0`) and WALK-006 are narrowed, GATE-006 has a note only; QUEUE-014 and QUEUE-010 stay blocked.
- **Two new approximations**: QUEUE-023 (a closed ride is not opened when its queue becomes connected, `0x4DE1F0` with `0x4DE4A0`) and PATH-016 (map init
  `0x536490` links InitialPath cells with the link writer's mode 1, where OpenTPW gives them links `0x0F`). Both were checked in the executable's bytes.

The pinned run hash moves from `0xA2FAA54FEB56DA05` to `0x09894103FAFB8D45` (schema 19): the synthetic park's queue now carries links and guests walk it with the original's
queue walk, so positions, joins and the world stream move. It is on top of main's pin after the staff walks (#229), which left the earlier pin unchanged; see docs/DETERMINISM.md.

No save field is new. An older save, and an imported original park, gets queue links rebuilt when its ride is registered (`QueuePaths.RebuildMissingLinks`, only when no
cell of the queue has a link byte), so staff and guests restored from them walk the same links. `Level.RefreshRenderedCells()` redraws the path layer, which the park-resume
smoke needed. Verified with 4510 tests passing without game data, 4950 with it, all five native smokes, the evidence checks (21 Python suites) and
`tools/fidelity_register.py --check` (237 unresolved unique IDs).

Still open: QUEUE-014 (the queue tool's straight segments from a clicked start) and QUEUE-010 (the guest's position after leaving a queue), QUEUE-023 and PATH-016 above, the
owner word of the area clear (PATH-005), the queue models' ground deformation and torch animation (PATH-015).
