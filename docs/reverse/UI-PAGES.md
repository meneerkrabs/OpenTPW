# UI page grouping: the remaining orchestration

Companion note to the ported page helpers (`OriginalPageAllocator` and `OriginalPageListMerge`). It records what the page grouping still needs and why it is not a pure algorithm.

Status: the two pure building blocks are done. `OriginalPageAllocator` (`0x58ECA0`) is merged;
`OriginalPageListMerge` (`0x58EAE0`) reproduces isolated native execution in its own pull request.
What is left is the orchestration, and it is **not** a pure algorithm.

## The frame driver 0x58F940

`0x58F940` clears six dwords at `this+0x1830` (`mov ecx,6; rep stosd`) — the
per-width-class used counters — then loops over the pending list at
`this+0x18C4`, calling `0x58F190(this, &pendingHead)` once per iteration.

- Return `1`: something was placed, keep going.
- Return `0`: give up; the leftover pending records are normalised and the list
  is emptied (`0x58FA3F..0x58FAD8`).
- Return `-1`: no class had room. The driver then takes the pending head,
  clears its format bits (`flags &= 0xFFFF0FCF`, then folds the format into bits
  `0x30` via `(flags>>2)&0x30`), rounds its size up to 8 (`(w+7)&~7`,
  `(h+7)&~7`), refreshes its rank with the `0x58CB40` code inline, unlinks it and
  sets the result to 1.

**Consequence:** a page is not a fixed 256/128/64. A new page is created from
*the first texture that fits nowhere*, rounded up to 8. The old note "page size is
256, 128 or 64 by device, 0x58DB0C" describes a different thing and should not be
used as the page-size policy.

## The page manager 0x58F190

`this` layout, as read:

| Offset | Meaning |
|---|---|
| `+0x0000` | page table, 6 classes x 256 slots x 4 bytes (0x1800 bytes) |
| `+0x1800` | six dwords, per-class divisor used by the ratio helper 0x58EA20 |
| `+0x1818` | six dwords, per-class limit (compared against the used count) |
| `+0x1830` | six dwords, per-class used count, cleared each frame |
| `+0x18C4` | the frame's pending list head (what the driver walks) |
| `+0x18CC/18E4/18E8` | the cache flags, alignment mask and fit gate the allocator reads |
| `+0x1918/191C/1924` | allocator max-width/height/retry outputs |

Flow, by address:

1. `0x58F1A5..0x58F1C7` — start class: `0x590CF0` width class of the pending head
   when it is device dependent (`0x590FB0`), else class 0.
2. `0x58F1C9..0x58F200` — first class at or after that with `used < limit`;
   reaching class 6 returns `-1`.
3. `0x58F212..0x58F23E` — the page slot is `table[class][used]`, and its first
   record becomes the working page.
4. `0x58F24F..0x58F2FE` — if the pending texture is wider than the page, halve it
   (flags `-0x10`, size `(v+1)>>1`, rank refresh) and repeat; if it cannot shrink
   and still does not fit, return `-1`.
5. `0x58F301..0x58F3FB` — when the texture can still shrink and the class is below
   5, compare the free share of this class against the next with `0x58EA20`
   (`2 x (limit-used)/divisor` against `(limit-used)/divisor`); if the next class
   is better, shrink once more and move there.
6. `0x58F3FD` — unlink the texture from the pending list, splice it into the page
   list with `0x58E8D0` (sorted by the `+0x38` rank, descending), zero its `+0x1C`,
   increment the class used count, then `0x58B330` (device) and `0x590350`
   (the placement that writes the page surface into each slot at
   `0x590401..0x590404`).
7. `0x58F502..0x58F540` — merge (0x58EAE0) and allocate (0x58ECA0, fifth argument
   1, page record as the fourth argument).
8. `0x58F545..0x58F569` — a zero placement count releases the page
   (`0x58E790`) and returns `-1`.
9. `0x58F56C..0x58F6A0` — while the allocator reports success, look for a lower
   class whose page still has room and repeat the merge/allocate there
   (`0x58F574..0x58F692`); on the retry flag, release the page.
10. `0x58F6A0..0x58F784` — a changed class moves the page with `0x58EA20`, the
    placement records are bubble-sorted by their `+0x10` dword
    (`0x58F720..0x58F77E`), then `0x58B330`, the virtual call `[vtable+0x10]`,
    `0x58E820`.
11. `0x58F7B8..0x58F8B7` — per placement, `0x590350` places the texture, the record
    moves between the page list and the caller's list, and the pending placement
    counter `0x8C4B6C` is decremented.
12. `0x58F8BD..0x58F91F` — a final `0x58B330` when something was placed,
    `0x58E790` for leftovers, and `0x590FF0` on the completed page.

Two more counters appear here: `0x8C4B6C` (pending placements, decremented per
placement, read at `0x58F615` and `0x58F898`) and `0x8C4B70` (shrink steps, at
`0x58F2DC`). They are process statistics, not allocator state.

## Why this is not portable as pure code

`0x58B330`, `0x590350`, `0x590FF0`, `0x4217E0` and `0x58B380` are the actual
device side: they create/lock the page surface, upload each texture into it, and
retire it. `0x590350` is the routine the UI-004 note already identified as the one
that sets a slot's page pointer. The grouping policy and the upload are the same
call chain; there is no seam where "decide the grouping" ends and "draw" begins.

OpenTPW today has no page surfaces: `UiRenderer` creates one GPU texture per
`UiTexture` path (`GetResourceSet`), and the world loaders keep their own separate
caches. Porting the orchestrator therefore first requires a page-surface layer
with its own lifetime, which is a renderer design decision, not a fidelity port:

- which page format/size caps OpenTPW would use, given `[EXT:texture32-always]`;
- whether world and UI textures really share pages in OpenTPW (the original does,
  because both go through the same texture manager);
- what replaces the device calls, and how a page rebuild is scheduled.

## What is still separately required

1. The persistent control show order (already narrowed in UI-004: controls prepend
   into `scene+0x18` on show and are unlinked on hide; the per-window show event
   order is unread).
2. The frame-wide deferred sort, which mixes the world's stashed records with the
   UI's (`0x582AD0 -> 0x5829E0`), so the UI tie permutation depends on world depth
   keys.
3. The page-surface layer above.

## Recommendation

Do not port `0x58F190` until the page-surface layer exists; a policy-only port
would be an invented approximation of a device-coupled routine. The next useful
fidelity step that is still pure is the show-order research (item 1), which needs
no device work and directly explains the between-page draw order.
