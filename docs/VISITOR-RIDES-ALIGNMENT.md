# Visitor rides-column alignment

The All Visitors list now uses left alignment for its rides count. Number and time
remain centred; cash remains right-aligned. Headers, values, sorting, refresh and
selection are unchanged.

The previous UI-907 explanation treated the alignment store as a different
constructor. It is part of the rides-cell constructor `0x48fde0`, which stores 0
(left) in the alignment byte `+0xd8` at `0x48fe28`. The row factory `0x4939d0`
sends the alignment call `0x65c428` with 1, 2 and 1 (centred, right, centred) to
the number, cash and time cells and none to the rides cell it makes at
`0x493b38`, so the original's rides column is left-aligned.

`VisitorRidesAlignmentTests` draws the real visitor list into the existing CPU
`UiBatch`, with synthetic digits and no original media. It checks the actual rides
value and position, the other three text columns, two text scales and the existing
RTL alignment mirroring. The tests ran in exact-head CI before #495 merged, and
UI-907 is retired. Native pixels, font choice and the other visitor
timing/membership gates are separate (their own register IDs).
