## Tracks and coasters sweep: loop pairs, demolition refunds, the circuit walker and junction links from tp.exe (October 16, 2026)

A sweep over the track and coaster approximations, read in tp.exe via REA; seven are narrowed with behaviour changes:

- **The loop pair** (COAST-035): the only writers of `0x79c664` are the loop button `0x442cd0` and its key `0x444650`; `0x4352bf`
  decrements it per placed pylon and `0x4446dd` zeroes it on mode exit, and `0x436230` marks the cursor node with flag `0x80` while it is
  2, so OpenTPW's ghost now carries the first-of-pair mark.
- **The loop button** (COAST-028) is enabled only at ride level 1 or more (`0x4980D6`) and hidden at level 0 (`0x49880F`).
- **Demolition refunds** (TRACK-007): each tile laid for the ride refunds track cell cost × the ride's scrap percentage / 100
  (`0x527EE0`, unsigned `0x53B5FE`), followed for a type with a queue by one charge of 2 × cost × percentage / 100 (signed `0x528157`);
  the ClearCell refund conditions `0x53B54A..0x53B57F` are decoded.
- **The circuit walker** (TRACK-014) is `0x52A970`: corners take `mask ^ opposite(heading)` and rewrite `+0xD`, other cells keep their
  heading, steps are 0-based and written in both modes, and the failures are the original's.
- **Junction links** (TRACK-004): the line op `0x82` (`0x539670`) gives a tile with two arms or fewer the line's drawing direction and ORs
  it into a tile with more, and station tiles take the rotation heading (`0x524F1E`).
- **Bridges** (TRACK-010): the shape test `0x538AA4..0x538B05` is "two arms or fewer and not a corner", so ends and armless tiles bridge
  too.
- **The bump module tick** (TRACK-019) runs once per substep before every ride's script step (`0x54F55F`), and the drop-in overlap test
  walks the whole module pool (`0x54A040`).

The pinned run hash is unchanged and saves are unchanged. Verified with 4583 tests passing without game data, 5031 with it, and all five
native smokes.

Still open: the closing-target marks `0x200000` of `0x4360c0` and the link `0x43eb70` draws into the marked start; the walks from the
station that limit demolition (`0x530830`, `0x530B70`); the clear op's removal of a line's direction bits (`0x5227F0`); the type-25 walk;
the map layer under bridges; and the tool's edit sub-modes.
