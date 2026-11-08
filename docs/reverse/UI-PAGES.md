# UI page grouping: the remaining orchestration

Companion note to the ported page helpers (`OriginalPageAllocator` and `OriginalPageListMerge`). It records what the page grouping still needs and why it is not a pure algorithm.

Status: the two pure building blocks are done. `OriginalPageAllocator` (`0x58ECA0`) is merged; `OriginalPageListMerge` (`0x58EAE0`) reproduces isolated native execution in its own pull request. What is left is the orchestration.

## The texture record

Two size pairs matter and they are easy to confuse:

| Field | Meaning |
|---|---|
| `+0x10`, `+0x12` | the **authored** size, signed 16-bit |
| `+0x14`, `+0x16` | the **working** size, signed 16-bit; the halving steps rewrite these |
| `+0x08` | flags: bits `0x30` are the remaining halvings, `0x30000` marks a device-dependent texture, `0x40000000` marks a page occupant |
| `+0x1C` | the page handle, written by the placement |
| `+0x38` | the rank key |
| `+0x04` | the next link |

`0x58F9A5..0x58F9DD` reads `+0x10`/`+0x12`, rounds them up to 8 (`(v+7) & ~7`) and writes them into `+0x14`/`+0x16`. That is a **reset to the authored size**. Separately, `0x58EAE0` compares `+0x12` at `0x58EB62` and then `+0x10` at `0x58EB71`, so its order is authored height then authored width descending: the largest authored textures come first.

## The frame driver 0x58F940

It clears six dwords at `this+0x1830` (`mov ecx,6; rep stosd`) — the per-width-class used counters — then loops over the pending list at `this+0x18C4`, calling `0x58F190(this, &pendingHead)`.

- `1`: something was placed; keep going.
- `0`: give up. The remaining pending records are normalised and the list is emptied (`0x58FA3F..0x58FAD8`).
- `-1`: no class had room. The driver resets the head (`flags &= 0xFFFF0FCF`, format folded into bits `0x30` at `0x58F9B3..0x58F9D6`, working size reset from the authored size at `0x58F9D9`/`0x58F9DD`), unlinks it at `0x58FA2F..0x58FA34` and continues. **No page is created here and the texture is not placed**; it simply leaves the frame. `docs/MD2-MODELS.md` already worded this as "leaves it unplaced".

## The manager 0x58F190

`this` layout, as read:

| Offset | Meaning |
|---|---|
| `+0x0000` | page table, 6 classes x 256 entries x 4 bytes (0x1800 bytes) |
| `+0x1800` | six dwords, per-class **page count** (capped at `0x100`): divided into the class's free slots for the average-free-per-page test, and decremented when a page is evicted |
| `+0x1860` | six dwords, the class's remaining **byte budget**; a page costs `bpp x size^2` |
| `+0x1818` | six dwords, per-class limit |
| `+0x1830` | six dwords, per-class used count, cleared each frame |
| `+0x18C4` | the frame's pending list head |
| `+0x18CC/18E4/18E8` | the flags, alignment mask and fit gate the allocator reads |
| `+0x1918/191C/1924` | allocator max-width/height/retry outputs |

A page-table entry holds a page record whose width and height are dwords at `+8` and `+0xC` (`0x58F24F`, `0x58F57E`).

### Page sizes and the budget

The page size is **the class's own size**, not a texture's. The table at `0x7022E8` holds `0x100, 0x80, 0x40, 0x20, 0x10, 0x08` — 256, 128, 64, 32, 16 and 8 square — and `0x58D6BE` passes that entry as the size to the creator. The creator `0x58D1D0` costs a page at `bpp x size^2`, where `bpp` is `0x7022D8[device kind]` (`2` for kinds 0 to 2 and `4` for kind 3, `0x58D204..0x58D21A`), and charges it against the class's budget at `+0x1860`.

`0x58D1D0` creates pages while the budget lasts and the class holds fewer than `0x100` (256) pages (`0x58D261`), appending the new record at `table[class][count]` and incrementing `+0x1800` (`0x58D2D5..0x58D309`). `0x58D400` seeds the budget at `0x58D42C..0x58D43A` from `[0x8C1620]`, or `0x2000000` (32 MiB) when that is zero, and `0x58D670` repeats the pass for the other classes, evicting a class's last-entry page when it has anything to reclaim (`0x58D6EE..0x58D763`).

So a page is 256, 128, 64, 32, 16 or 8 square by class, capped at 256 pages per class and bounded by a byte budget that starts at 32 MiB unless the device reports its own. `0x5906E0` is not a size setter: it copies the six 256-entry slot tables between two buffers.

The routine opens the same way for both paths: start class is the width class `0x590CF0` of the head when the head is device dependent (`0x590FB0`) and class 0 otherwise (`0x58F1A5..0x58F1C7`); the first class at or after it with `used < limit` is taken, and class 6 returns `-1` (`0x58F1C9..0x58F200`); the entry is `table[class][used]` (`0x58F212..0x58F23E`). Then it splits.

### Non-device textures (0x590FB0 false): merge and pack

`0x58F502..0x58F540` merges (`0x58EAE0`) and allocates (`0x58ECA0`, the page record as the fourth argument and the fifth argument 1). A zero placement count first rebuilds the pending list through `0x58E790` (`0x58F557`) and then returns `-1` (`0x58F545..0x58F569`). While the allocator succeeds and the page's half width and height are at least the cache maxima `+0x1918`/`+0x191C`, it steps to the **next higher** class and allocates again with the fifth argument 0, counting the attempt at `0x8C4B5C` and restoring `0x8C4B6C` on failure (`0x58F574..0x58F692`). Then `0x58F6A0`: the retry flag rebuilds the pending list through `0x58E790` (`0x58F6AE..0x58F6B8`, whose argument is the pending-list pointer, not the page); a class change calls `0x58EA20`, which divides each class's free slots by its page count (`+0x1800`), compares twice one against the other, and gates on a limit-plus-10 test and a `vtable+0xC` count test (`0x58EA20` is called only here, at `0x58F6CA`); `0x58E8D0` **evicts the page entry's previous occupants** — it clears their `+0x1C` and either re-inserts them into the pending list by `+0x38` descending (flag `0x40000000`) or normalises them; the placement records are bubble-sorted by their unsigned `+0x10` dword (`0x58F720..0x58F77E`, not a float sort); `0x58B330`, the virtual `+0x10` and `0x58E820` follow; then per placement `0x590350` places the texture and the record moves between lists, decrementing `0x8C4B6C` (`0x58F7B8..0x58F8B7`); the last `0x58B330` runs only when no placed texture can still be halved (`0x58F8BD`); a second `0x58E790` (`0x58F8F0..0x58F90D`) rebuilds a local copy of the page entry's occupant list and writes it back into entry `+0x4`; the finished page goes to `0x590FF0`.

### Device-dependent textures (0x590FB0 true): shrink, then the whole page

`0x58F24F..0x58F2FE`: while the texture is wider than the page (`+8`) it is halved (flags `-0x10`, size `(v+1)>>1`, rank refresh, steps counted at `0x8C4B70`); if it cannot shrink and still does not fit, the call returns `-1`. Then `0x58F301..0x58F3FB`: when it can still shrink and the class is below 5, an **inline** free-share comparison (`0x58F369..0x58F38E`; no `0x58EA20`, no 2x factor, ties go to the next class) may halve once more and move one class up. Then it evicts the entry's previous occupants with `0x58E8D0` (`0x58F416`) and clears entry `+0x4` (`0x58F421`), increments the class used count (`0x58F428`), calls `0x58B330` and the virtual `+0x10` (a refusal returns 0 at `0x58F43B`), and only then a single `0x590350` gives this texture the whole page (`0x58F471`), followed by `0x590FF0` and return (`0x58F4F0`).

## What the device calls actually do

`0x590350` is the placement: it writes the page handle into the texture's `+0x1C` (`0x590678`; the software path does the same at `0x5904E9`) and sets the page flag `0x1000` while clearing `0x2000` at `0x590401..0x590404`. `0x58B330` is only a six-instruction flag setter — `[page+4] = ([page+4] & 0x10000000) | arg` — and the surfaces themselves are created once at initialisation (`0x58D27A`, `0x58D297`); the per-frame device calls are the surface's vtable slots (`+0x10` at `0x58F436`/`0x58F7A4`, a zero return aborting, `+0x1C` inside `0x590350` at `0x59037E`, and `+0x14` in the tail of `0x590FF0` at `0x591007`, which also stamps `[page+0x14]` with the frame counter `0x8BCC40`), and a failing `0x590350` aborts the attempt.

The class choice, the halving, the merge, the allocation and the placement sort are all decided **before** any device call, so the policy is not hidden inside the device layer. What blocks a port is different: the pages, their capacities and the per-class limits and divisors are created by runtime initialisation that has not been read, the manager keeps the page table and the used counts across frames, and the textures stay on their pages between frames, where OpenTPW draws immediately and keeps one GPU texture per path. `0x58DB0C`'s `0x40`/`0x80`/`0x100` are immediately overwritten with `0x10` at `0x58DB34`, so that is not the page-size source either.

## Counters

`0x8C4B6C` is the allocator's own placement-count accumulator — `0x58ECA0` resets and increments it (`0x58ECE6`, `0x58F128`) and the manager saves, restores and decrements it — and `0x8C4B68` is incremented beside it. `0x8C4B5C` counts class retries and `0x8C4B70` counts halvings. The manager saves and restores `0x8C4B6C` around a failed retry, so at least that one participates in control; whether any of them is read to make a decision was not traced, so calling them statistics is unverified. The ported helper deliberately keeps none of them.

## What is still separately required

1. The page objects, capacities, limits and divisors, and their per-frame lifetime (the unread initialisation above).
2. The persistent control show order: controls prepend into the scene list at `scene+0x18` on show and are unlinked on hide; the per-window show event order is unread.
3. The frame-wide deferred sort, which mixes the world's stashed records with the UI's (`0x582AD0 -> 0x5829E0`), so the UI tie permutation depends on world depth keys.

## Recommendation

Two steps come before any renderer work. First read the capacity initialisation; only then can the device-dependent branch be ported, because that branch exists only on device kind 1 (`0x590FB0`) — which physical device kind 1 is, was not established here — and the branch reads the page width at `+8` (the shrink loop) and the per-class limits and page counts at `+0x1818`/`+0x1800` (the inline class test) from that same initialisation. Its policy is otherwise determined: shrink until it fits, optionally one class up, evict the entry's previous occupants, then give the texture the whole page. Runtime grouping still needs page objects of those six sizes with a byte budget and a cross-frame lifetime, which OpenTPW does not have; that is a renderer design decision under `[EXT:texture32-always]`, not a fidelity port.

The capacity model above is now read; the first capacity step starts at `0x58DA40`, which calls `0x58D400` at `0x58DAD6` and `0x58D670` at `0x58DAE9`. `0x58D400` also writes `+0x18F4` and `+0x18F8` (`0x58D4EA`, `0x58D4F2`, `0x58D50F`, `0x58D524`). Whether `0x58DA40` runs once per frame or once per page cycle is not established here.
