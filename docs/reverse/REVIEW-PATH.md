# Review PATH-V: the path builder on the shared cell map (d323398, d21fb4a)

October 10, 2026. Independent review of the path stack on `952ab0f` (fork main
`5ec2622` + queue integration `025d410` + QUEUE-FIX; QUEUE-V2 re-reviews that
base): `d323398` (PATH-R research, `docs/reverse/PATH-plan.md` and
`lanes/path/`) and `d21fb4a` (PATH-I implementation).

All binary results are static. Nothing original was executed. Claims are
bounded to the decoded sites, the scratch harnesses and the runs listed here.
The scratch copies (`git archive` of `952ab0f`, `5ec2622` and `d21fb4a`) live
outside the repository.
Witness: `tools/ppc-analysis/lanes/review/test_path_v1.py`. It has its own PEF
reader and PowerPC field decoder. It does not import `pef.py`, `ppcdis.py` or
`path_evidence.py`. Its code section equals the repository loader's byte for
byte (2,014,896 bytes), and its data section equals the loader's first 339,516
bytes. TOC slots are read as raw data words. They hold data-section offsets,
which matched the repository's relocation addends for every slot used.

## Merge blockers

**B1. The front-end smoke test is red, inherited from the queue base.** On
`d21fb4a`, `bash scripts/run.sh --front-end --smoke-test` fails with
"Escape opens the pause menu". The cause is not PATH:

- `952ab0f` fails at the same check;
- fork main `5ec2622` passes (338 frames);
- `85f0163` (QUEUE-I, integrated as `025d410`) made `ParkHud.OnObjectPlaced`
  enter the queue tool after a menu-chosen HasQueue ride is placed. The smoke
  test places "Belly Bounce" that way. Instrumented: before Back the tool is
  `Queue`, and after Back the tool is `None` with `Paused = false`. So the single
  Back correctly leaves the queue tool, and the test was never updated.

Exact fix, in `source/OpenTPW/FrontEnd/FrontEndSmokeTest.cs`, step
"closed state":

```csharp
			Require( !secondObject!.IsOpen, "HUD door closes the selected original object" );
			Require( flow.Level!.CellTool.Mode == CellToolMode.Queue, "placing a HasQueue ride from the menu enters the queue tool" );
			flow.InjectedInput = UiInput.Key( UiKeys.Back );
		} );
		Wait( "queue tool closed", 3 );
		Do( "queue tool closed", () =>
		{
			Require( !flow.Level!.CellTool.IsActive && !flow.Hud!.Paused, "the first Back leaves the queue tool, not the park" );
			flow.InjectedInput = UiInput.Key( UiKeys.Back );
		} );
		Wait( "pause opens", 3 );
```

With exactly this change, the scratch copy of `d21fb4a` passes (342 frames). If
QUEUE-V2 fixes it on the base first, use `flow.Level!.QueueToolRide != null`
instead of the `CellTool` checks.

**B2. The ending click commits a line that the original skips.** The original
commit decodes as follows: at `0x7105c..0x71084`, when the snapped end equals the
start and the vertex count is not 1, `bf eq` jumps to `0x71174`. That skips the
push and every `LayLine` call, including the raw-mode commit at `0x710c0`.

`CellBuildTool.Click` still calls `Writer.Commit( start, start )` in that case.
The start cell is already a path, so the commit bumps its placement counter.
Probe P3: the counter goes 0 → 1, and the world hash changes. This happens at
the end of every path session. The native smoke shows it on `(52, 19)`. The
`BIN:STP-PPC:0x10070F84` comment states the skip for the push but not for the
`LayLine`. Exact fix, in `source/OpenTPW/World/CellBuildTool.cs`:

```csharp
		var end = ParkPathBuilder.SnapEnd( start, cell );
		if ( end == start && vertices.Count != 1 )
		{
			End();
			return null;
		}
		var pushed = false;
		if ( vertices.Count < VertexCapacity )
		{
			vertices.Add( end );
			pushed = true;
		}
		var result = Writer.Commit( start, end );
```

Keep the rest. The original ignores the push result (`0x7bcf4`'s return is not
read), so `!pushed` may stay in the end test as an OpenTPW guard. Reword the BIN
comment: "push and LayLine(mode, start, end) unless the end equals the start
with more than one vertex stored (then nothing is laid); end the tool (start −1,
SetMode(0)) when end == start". The refusal part belongs to S2.

Tested in scratch: `PathBuilderTests`, `QueueTests` and `DeterminismTests` pass
(91/91, no re-pin, because the synthetic park never uses the tool). The native
jungle smoke also passes (4323 frames). `ParkHud` ignores the null result:
`result is { Completed: false }`.

No other blockers.

## Verdicts

| Check | Verdict |
| --- | --- |
| 1. Seven `CanChangeCellType` rules | **Confirmed**, rule by rule (section 1) |
| 1. `SetCellType`: Costs.PathCell +1472 / QueueCell +1468, money test, free counter bump | **Confirmed** |
| 1. `LayLine` strict axis, tie keeps X | **Confirmed** (preview `0x6f4d8` and commit `0x71010` snap the same way). An unlisted skip exists (N1) |
| 1. 1024-entry vertex stack | **Confirmed** as the buffer size. The guard is `count > 1024` (N2) |
| 1. Commit / end-of-tool sequence | **Partly**. B2 (the skip) and S2 (the end flag reads the ghost-clear `LayLine(0x81)`, not the commit) |
| 1. InitialPath = path + NoModify; ClearCell NoModify and no refund | **Confirmed** (N3, N5 are nuances) |
| 1. Unowned flag check, HasQueue → mode 3, hover help 441–445 | Not re-decoded. Taken as the author's high/medium claims. Unowned is never set in OpenTPW (PATH-007), so NotOwned is reachable only in tests |
| 1. "Bounded" labelling where scans do not follow branches | **Confirmed** for §3.3 (validator), §5.5 (slope) and §6 (bulldozer route). PATH-009's reading of `data:0x7de2d` is weaker than stated (S3) |
| 2. Ownership flip preserves guests | **Confirmed**. Raw hashes `0x3622300C23D95AB8` (cross, 92 admissions) and `0x17B0E01EDEB2D4FB` (jungle Easymode, 108) reproduce on both `952ab0f` and `d21fb4a` |
| 2. Version counter explains the pin move | **Confirmed** (section 2) |
| 3. Validation order, CODE8, money boundary, extend, NoModify, bounds, objects, Preview | **Confirmed** (section 3). CODE8 is refused, labelled PATH-008 |
| 4. Path tool enter/leave, queue tool via shared tool, Back/Escape, right click | **Confirmed**, with S1 (remove tool) and S5 (paused clicks) |
| 4. "Level.Update ignores HUD-captured clicks" keeps placement flows | **Confirmed** for placement: the sandbox smoke, the jungle smoke and the front-end placement steps pass. The front-end smoke itself is red for B1 |
| 5. Determinism guard, register, CRLF, whitespace, `as_posix` | **Confirmed** (section 5) |
| 6. Numbers | **Reproduced exactly** (section 5) |

## 1. Operands (independent decode)

`Binary` in `test_path_v1.py` asserts each operand below. That class needs
`OPENTPW_MAC_BIN`, and `SimThemePark.data` must have SHA-256 `04809cd4…e295f5`.

- **`CanChangeCellType` `0x8408c(cell r3, new r4)`.** The compare chain, in
  order:
  1. `new == 0` → 1;
  2. `new == 4 && old == 4` → **0**;
  3. `21/21` → 1;
  4. `new 1 && old 3` → 1;
  5. `new == old` (`cmpw r4, r0`) or `old == 0` → 1;
  6. `new 4 && old 1` → 1;
  7. `new 3 && old 1 && *data:0x84b2c ≠ 0` → 1;
  8. otherwise 0.

  `ParkCellMap.CanChangeCellType` is identical. The witness also mutates the
  rule-4 immediate and the default result in a copy of the code and checks that
  the decoder catches both.
- **`SetCellType` `0x82ac4`.**
  - Same nonzero type: `lha +32`, `addi 1`, `sth +32`, then `b` to the success
    return, with no Spend.
  - Type 1 gets its price from `bl 0x7bc58`, which loads slot → `data:0x84ad4`.
    Type 3 gets it from `bl 0x7bc70` → `data:0x84ad0`. Both are skipped when the
    byte `data:0x7de2d` is nonzero.
  - The loader `0x10f29c` reads `lwz +1472` into `bl 0x7bc4c` (stores
    `data:0x84ad4`) and `lwz +1468` into `bl 0x7bc64` (stores `data:0x84ad0`).
  - Money test, only when `game+36 == 0`: `bl 0xcbf48`, then
    `subf r5 = balance − cost`, `srawi r3 = r5 >> 31`, `subfc` (CA = 1) and
    `adde r4 = sign + 0 + CA`. So affordable means signed `balance − cost ≥ 0`.
    The model is exact on the boundaries 20/20 → yes and 19/20 → no.
  - Spend (`0xcbfdc`) is called at `0x82cec` (type 1) and `0x82d2c` (type 3).

  That +1472 is the key `Costs.PathCell` rests on the economy schema reading. I
  did not re-derive it; the PC value 20 matches the smoke's $120 for 6 cells.
- **`LayLine` `0x84ea4(type, x0, y0, &x1, &y1)`.**
  - `cmpw |dx|, |dy|` then `bf gt` to `0x85038`: a strict `>` walks X on row
    `y0`, everything else walks Y on column `x0`.
  - Each cell is first tested by `0xd70d8`, which checks `0 ≤ x < 128` and
    `0 ≤ y < 128`. A failing cell is **skipped**, not refused (N1).
  - The last-cell flag is `data:0x84b2c`. A first-cell flag `data:0x84b30` is
    raised before the first cell and cleared after each one. PATH-plan does not
    mention it, and PATH-I does not need it.
  - There is no Spend in `0x84ea4..0x85174`.
- **Snap.** Preview `0x6f4d8`, commit `0x71010`: `cmpw |dx|, |dy|` then
  `bt lt` past `end.y = start.y`. So `|dx| ≥ |dy|` keeps X, and a tie keeps X.
- **Commit `0x70f84..0x71304`.**
  - `0x7107c` `bl 0x7bce8` (count), `cmpwi 1`, then `bf eq` to `0x71174` when
    end == start (B2).
  - The push is `0x71090`, and the commit `LayLine(mode)` is `0x710c0`.
  - The session-end flag at `sp+2380` is `cntlzw` of the **last** call's
    result: `li r3, 0x81` and `bl 0x84ea4` at `0x71160`/`0x71164`. It is read
    at `0x7123c` (S2).
- **Vertex push `0x7bcf4`.** The count is `data:0x84ab8` and the buffer
  `data:0x82ab8`. The guard is `cmpwi count, 1024` with `bf gt` to the store.
  So only `count > 1024` refuses, and the 1025th point is written at
  `0x82ab8 + 8192 = 0x84ab8`, the count word itself (N2).
- **`ClearCell` `0x859b4`, path case.**
  - NOMODIFY test: `rlwinm.` bit `0x20` of `+14`, then `bl 0x6e110`.
  - `a == b == 0` gives `li −1`, `sth +32`.
  - The link loop calls `0xdd57c` for a queue-end neighbour, and the case ends
    with `SetCellType(cell, 0)` (`0x85c90`).
  - No `0xcbf50` (Earn) in `0x85a9c..0x85ca0`, so there is no refund.
  - `0x6e110` counts all eight bits of the link byte `+12` (eight single-bit
    `rlwinm.`) (N3).
- **InitialPath `0x85538`.** `lbz +38`, and `rlwinm.` bit `0x08` gives type 1.
  The cell is linked through `0x82d6c`, and `li 32`, `sth +14` set the flags to
  exactly `0x20`. Cells without `0x20` then get `ori 0x40`.
- **Free byte `data:0x7de2d`** (S3). It is a transient park-view tool flag:
  - the hover routine sets it to 1 in mode 4 (`0x6f5dc` `li 4`, `0x6f5f4` `stb`
    through r13);
  - the click handler sets it to 1 at `0x71330` and, in mode 59, at `0x71cb8`;
  - it is cleared at `0x70ca4`, `0x71c94` and `0x722ec`.

  Bound: this is a TOC-slot scan with a 13-instruction lookahead, plus the r13
  site found by reading. Other writers through cached registers may exist.

## 2. Ownership flip and the pin (scratch)

| Build | Cross raw (seed 5, 7200 ticks) | Jungle Easymode raw (seed 1) | `HashOfAFixedRun…` |
| --- | --- | --- | --- |
| `952ab0f` (scratch class added) | `0x3622300C23D95AB8`, 92 admissions | `0x17B0E01EDEB2D4FB`, 108 | `0xE67AA45A94F4B20C` (passes) |
| `d21fb4a` | same | same | `0x10A80C328A477CBE` (passes) |
| `d21fb4a`, no cell-map digest, `SchemaVersion = 4` | | | `0x2EB8B824C2984A88` |
| … and `GuestPathGrid.Version` bumped only by `Invalidate()` (old meaning) | | | **`0xE67AA45A94F4B20C`** = base pin |

So the simulation is unchanged. The whole pin move is the schema bump, the
cell-map digest and the value of `seenGridVersion`, which is hashed in
`GuestSimulation.AddCanonicalState` and now counts every map write. The flow-field
cache now follows `ParkCellMap.Version`, so it also drops on writes that skip
`Invalidate()`. That is more correct, and no guest run changed.

## 3. Behaviour probes (scratch MSTest class on `d21fb4a`, not committed)

| Probe | Result |
| --- | --- |
| P1 money boundary | Cash 3·20: preview and commit lay 3 cells, balance 0, OtherCosts 60. Cash 3·20 − 1: the preview stops at the third cell with `NotEnoughMoney` (pending line cost, §3.3) |
| P2 Preview side effects | 20 tool hovers, previews and validates: world hash `D1F8AC5D58EF78D5` unchanged, `Cells.Version` 11 → 11, balance and OtherCosts unchanged |
| P3 ending click | Counter 0 → 1 on the start, hash changed (B2) |
| P4 order | Water with no money → `Terrain`; plus an object → `Occupied`; queue with an object → `QueueCell`; a queue end (code 8) → `QueueCell` (PATH-008); (−1, 0) and (16, 0) → `OutsideTerrain` |
| P5 off the edge | (13,3) → (19,3) on a 16-wide grid builds 3 cells for $60 and stops at (16,3). The original would skip only outside 128 (N1) |
| P6 extend | From an existing end: 4 built, 1 existing (counter +1, free), $80, tool continues. A click back onto existing path ends the tool, and Undo is then unavailable (PATH-002/003 behaviour) |
| P7 NoModify with 0x55 links and no neighbours | Removed (N3) |

The author's tests cover the rest: the order with Unowned first, every refused
type, NoModify with neighbours, no refund and the queue mode. I read them and
they assert what their names say.

## 4. Findings to fix with or after the blockers

- **S1. With the HUD, the remove tool cannot remove path cells.**
  `UpdateCellTool` only stands aside for `BuildEntry` and `IsPlacing`. With
  `IsRemovingObjects` on (the developer panel), a left click on a path cell
  enters the path tool (help 442). The HUD then reports the click as captured,
  so `Level.RemoveAt` never sees it. A click on empty ground also starts the
  path tool. Fix: `if ( paused || level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects ) return false;`
- **S2. The end-on-refusal rule is labelled as traced, but it is not.** The flag
  read at `0x7123c` is the result of the ghost-clear `LayLine(0x81)`, not of
  the commit. PATH-plan §4.3 "or when the commit LayLine failed" and the BIN
  comment should say so. Ending the tool on a refused cell then belongs under
  an APPROX tag (extend PATH-003's rule, or add one).
- **S3. PATH-009's reading of the free byte.** Section 1 shows a transient flag
  set by the park view in modes 4 and 59. "Free only without an economy" can
  stay as the approximation, but the APPROX text and PATH-plan §3.2/§10 should
  cite these writers instead of "sandbox".
- **S4. The queue-mode ghost is optimistic.** `QueueLineWriter.Run`
  (preview) checks only the first new cell properly. Later cells pass when
  their type is Empty, without bounds, terrain, object or money checks, so the
  ghost can show green cells that the commit refuses. Use `check` per cell, with
  the predecessor advanced along the line, or mark later ghost cells as
  unchecked.
- **S5. Paused clicks.** `UpdateCellTool` returns early while paused, but
  `Level.HandleObjectClick` still commits `CellTool.Click`. So a paused click
  lays path or queue with no ghost and no refusal message. Decide one way for
  both.

Notes (plan accuracy, no code change needed now):

- **N1.** `LayLine` skips cells outside the 128×128 array; it does not stop.
  When the skipped cell is the end, it returns 1 with the last-cell flag left
  raised. The tool's snapped ends are grid cells, so this is unreachable from
  play. PATH-plan §4.2 should mention it.
- **N2.** The vertex guard is `count > 1024`, an off-by-one onto the count word.
  OpenTPW's 1024 is the buffer size, which is fine.
- **N3.** `0x6e110` is a popcount of the link byte. `HasPathNeighbours` checks
  linked path or queue neighbours, and its comment says "as 0x6e110 sees them".
  They differ only when links point at non-path cells. That happens for
  `FromOriginal`'s 0x0F-linked InitialPath cells (P7). Bounded, because the
  original's link writer `0x82d6c` is untraced (PATH-004).
- **N4.** HasQueue → mode 3 and hover help 441–445 were not re-decoded.
- **N5.** `ClearCell` writes 0 to all of `+14` on removal. OpenTPW keeps flags
  other than NoModify, which has no effect while Unowned is never set.

## 5. Builds, tests and checks

Tools: .NET SDK 10.0.401 at `/Users/sander/.local/share/opentpw-dotnet10`
(non-symlinked), Release. `libveldrid-spirv.dylib` was copied into the ignored
`native/` folder and not committed.

| Check | `d21fb4a` | Author |
| --- | --- | --- |
| `OpenTPW.Tests`, no assets | 975 passed / 250 skipped / 0 failed | 975/250/0 |
| `OpenTPW.Tests`, `OPENTPW_GAME_PATH` | 1154 / 71 / 0 (12 min 26 s) | 1154/71/0 |
| Native jungle smoke | passed, 4329 frames; "laid 6 cells (52, 14) -> (52, 19) for $120 through ParkHud clicks" | same |
| Native sandbox smoke | passed, 1105 frames | — |
| Native front-end smoke | **failed**: "Escape opens the pause menu" (B1). With the B1 fix: passed, 342 frames. `952ab0f` fails the same way; `5ec2622` passes (338 frames) | not reported |
| Evidence runner (`--mac-bin`, `--pc-data`) | OK: 13 Python suites, 832 tests, 108 skipped (before this witness) | 832 OK |
| `--m3-gate` (jungle, 30 min) | 13 pass / 2 fail (`build.paths`, `build.queue`) / 1 unresolved, exit 1. Unchanged as expected: the gate is not yet wired to the builder (GATE-UPD) | — |
| `fidelity_register.py --check` | OK, 186 unresolved unique IDs (176 on `952ab0f` + PATH-001..010), "ten configured C# registers" | same |
| CRLF | `git diff 952ab0f --stat` and `--ignore-cr-at-eol --stat` are identical (23 files, +3082/−134). `Game.cs` and `SandboxSmokeTest.cs` stay CRLF | — |
| Whitespace | `git diff --check` only flags the CR of those two CRLF files. No added line ends in a space or tab | — |
| Python paths | `path_evidence.py` uses `relative_to(REPO).as_posix()` | — |
| Determinism guard | The source-guard tests pass. They scan top-level `World/*.cs`, which holds the new files. New code has no `Random`, clock or `GetHashCode`. The `HashSet`/`Dictionary` uses are render-only lookups | — |
| `test_path_v1.py` | 21 tests OK with `OPENTPW_MAC_BIN`; 11 run (10 binary skipped) without it. Evidence runner with the witness: OK, 853 tests, 118 skipped (the runner does not export `OPENTPW_MAC_BIN`, as for the earlier review witnesses) | — |

## Round 2: the fixes on 267c420

October 10, 2026. Re-review of `3d78a8a` (ESC-FIX, PATH-011) and `267c420`
(fixes for B2 and S1–S5), on top of the round-1 commit `ea10b25`. The same rules
apply: static reading only, nothing original was executed, and every claim is
bounded to the sites, scratch runs and checks listed here. Scratch copies
(`git archive` of `267c420` and of the merge tree) live outside the repository.
New witness: `tools/ppc-analysis/lanes/review/test_path_v2.py`. It reuses the
round-1 witness's own PEF reader and decoder.

**Merge-ready: yes.** Every round-1 blocker and finding is fixed or answered.
The notes below need no change before merge. The gate stack (`39ccda4`)
conflicts in two files; the recipe is at the end of this section.

### Verdicts

| Finding | Verdict | Evidence |
| --- | --- | --- |
| B1 front-end smoke | **Fixed** by `3d78a8a` | Native front-end smoke passes, 342 frames, "Escape out of the queue tool". `QueueToolRide` is read through `CellTool.Writer`, which is the same check as the one proposed in round 1 |
| B2 ending click lays a line | **Fixed** | `Click` returns `null` before the push and `Commit` when `end == start` and the count is not 1. Operands re-decoded (below). `TheEndingClickLaysNothing` passes. Mutation: restoring the commit and deleting every other assertion (`IsNull`, `LastCommit`, counter, version, balance) still fails, on the **hash assertion alone** (`6101474226486134394` ≠ `18094232359882903757`). The jungle smoke asserts the end counter and the state hash too |
| S1 remove tool | **Fixed** | `UpdateCellTool` stands aside for `IsRemovingObjects`. Mutation: removing that guard fails the jungle smoke with "with the remove tool on, a park click is left to Level.RemoveAt". The extra `HandleObjectClick` reorder (remove before the cell tool) only differs when the remove tool and the cell tool are both on. Placement keeps working: the setters keep `BuildEntry` and `IsRemovingObjects` exclusive (`ObjectBuildPanel.cs:41-44`, `:59-63`; `ParkHud.cs:361-364`). All three smokes pass, including placement, sale, removal and the queue tool. The reorder itself has no test (N2-3) |
| S2 end flag | **Fixed** (relabelled) | BIN comment, PATH-plan §4.3 and PATHS.md now say the flag is the ghost-clear `LayLine(0x81)`. The refusal rule is `APPROX:PATH-012` |
| S3 PATH-009 citations | **Fixed** | `ParkPathBuilder.cs`, the register text and PATH-plan §3.2/§10 cite `0x6f5f4`, `0x71330` (mode 4 hover / click), `0x71cb8` (mode 59), and the clears at `0x70ca4`, `0x71c94`, `0x722ec`. These match the round-1 decode, which re-ran here (`test_free_byte_is_a_transient_tool_flag`). The scan bound is stated |
| S4 queue ghost | **Fixed** | Every preview cell goes through `check( x, y, built )`, which is `Level.CheckQueueCell` → `QueuePaths.CheckExtend` with the pending cells plus `CanSpendCell( Queue, pending )`. `BuildQueueCell` runs the same check with no pending cells after `RecomputeQueue`, then `TrySpendCell`. Mutations, run with `PathBuilderTests` + `QueueTests` (81 tests): restoring the old preview fails the new test; so do ignoring pending cells in the length limit (25 ≠ 35), using the ride's back cell instead of the last pending one (two tests), and ignoring pending cells in `CanSpendCell` (3 ≠ 8). Removing `pending.Contains` survives (N2-1) |
| S5 paused clicks | **Author is right; my round-1 premise was wrong** | Since `d21fb4a`, `ParkHud.Update` sets `overUi = … \|\| Stack.Screens.Count > 1` and returns it. `GameFlow` stores that in `Level.UiCapturesMouse`, and `Level.Update` calls `HandleObjectClick` only when it is false. So an open pause menu already blocked the park click. Only the economy's speed pause lets tool clicks through, and that is now an explicit choice (`APPROX:PATH-013`, asserted by the sandbox part of the jungle smoke) |
| PATH-012 / PATH-013 honesty | **Honest** | Both are APPROX entries with evidence-needed text, and neither claims a trace. PATH-012 could add that the flag is read only when `end != start` (`0x7121c..0x71238`, below); this changes no behaviour |

### Operands (independent decode, `test_path_v2.py`)

- **Compare at `0x7105c..0x71078`.** It loads `*r30` (start x) against `sp+2536`
  (end x), `cmpw`, and `bf eq` to `0x71088` (the push). It does the same for
  `*r31` / `sp+2540` (y). The roles are fixed by the commit's arguments:
  `0x710b0..0x710bc` pass `*r30`, `*r31` as `x0`, `y0` and `sp+2536`, `sp+2540`
  as `&x1`, `&y1`.
- **Count test.** `0x7107c` `bl 0x7bce8` (count), `0x71080` `cmpwi r3, 1`, and
  `0x71084` `bf eq` to `0x71174`. The push at `0x71088..0x71090` loads the end
  and calls `0x7bcf4`. This agrees with the author's reading and with the C#.
- **The commit block calls `LayLine` eight times.** The calls are at `0x710a8`
  (mode `0x87`), `0x710c0` (the raw mode, from the getter `0x7b878`), then
  `0x80`, `0x82`, `0x85`, `0x86`, `0x83` and `0x81` (`0x71164`).
  - The flag is `cntlzw` then `rlwinm 27,24,31`, i.e. `result == 0`. It is
    stored at `0x71170`.
  - `sp+2380` has exactly two uses in `0x70f84..0x71304`: that store and the
    read at `0x7123c`.
- **End test at `0x7121c..0x71238`.** If x differs, read the flag. If x and y
  are both equal, `bt eq` goes to `0x71248`, which stores start −1, and the flag
  is never read. So the skip path (B2) never sees an unwritten flag. The flag
  decides the end only after a line that moved.
- PATH-009's writers: confirmed again by the round-1 test, unchanged.

### Re-run of the round-1 probes

- **P3 ending click.** Now no counter bump, no hash change (the new unit test
  plus the smoke's `PlacementCountAt`/state-hash check).
- **B1.** Front-end smoke green, as above.
- **S1.** Smoke step plus mutation, as above.
- **S4.** New test plus four mutations, as above.
- **P1, P2, P4–P7.** Code paths unchanged by `267c420` (`ParkPathBuilder` only
  gained comments). Covered by the unchanged author tests, which pass.

### Notes (no change needed for merge)

- **N2-1.** `pending.Contains( (x, y) )` in `CheckExtend` cannot be reached from
  the tool, because a snapped line never repeats a cell. The mutation that
  removes it survives. It is harmless as a guard for the headless API.
- **N2-2.** `Level.CheckQueueCell` is not called by any unit test. The new test
  builds its own `CheckExtend` + `CanSpendCell` composition, and the `QueueTests`
  source scan only checks the call order. After the gate merge, the static
  `CheckQueueCell` below can be tested directly.
- **N2-3.** Turning on the developer remove tool while the path or queue tool
  is active leaves the tool active, with a stale ghost, until the remove tool is
  turned off. No test covers the reorder in `HandleObjectClick`, because the
  smokes drive the HUD. This is developer-panel only.
- **N2-4.** The commit block's `LayLine(0x87)` before the commit, and the
  `*r19` reset between `0x82` and `0x85` (`0x710f4..0x71104`), are not
  described in PATH-plan. Nothing in PATH-I depends on them.

### Merge with the gate stack (GATE-FIX2 `39ccda4`, on `d21fb4a`)

`git merge-tree --write-tree 267c420 39ccda4` gives tree `e757b694`, with
conflicts in exactly two files.

1. **`source/OpenTPW/World/Level.Objects.cs`.** There are two hunks, both in the
   new static `BuildQueueCell( grid, economy, ride, x, y, isBlocked, out message )`.
   - Keep the gate's static helper and the instance wrapper.
   - Make the queue check static too:

     ```csharp
     public QueueBuildResult CheckQueueCell( RideVisitorBridge ride, int x, int y, IReadOnlyList<(int X, int Y)> pending )
     {
         if ( IsReadOnlyVisit || Guests == null )
             return QueueBuildResult.Refused;
         return CheckQueueCell( Guests.Grid, Park?.Economy, ride, x, y, pending, IsQueueBlocked );
     }

     internal static QueueBuildResult CheckQueueCell( GuestPathGrid grid, ParkEconomy? economy, RideVisitorBridge ride, int x, int y, IReadOnlyList<(int X, int Y)> pending, Func<int, int, bool> isBlocked )
     {
         var check = QueuePaths.CheckExtend( grid, ride, x, y, pending, isBlocked );
         if ( check != QueueBuildResult.Ok )
             return check;
         if ( economy != null && !economy.CanSpendCell( CellPurchase.Queue, pending.Count ) )
             return QueueBuildResult.Refused;
         return QueueBuildResult.Ok;
     }
     ```

   - In the static `BuildQueueCell`, resolve the two hunks as:

     ```csharp
     ride.RecomputeQueue( grid );
     var check = CheckQueueCell( grid, economy, ride, x, y, Array.Empty<(int X, int Y)>(), isBlocked );
     if ( check is not (QueueBuildResult.Ok or QueueBuildResult.Refused) )
     …
     if ( check == QueueBuildResult.Refused || (economy != null && economy.TrySpendCell( CellPurchase.Queue ) != ParkEconomy.PurchaseResult.Ok) )
     ```

   - Point the helper's `<see cref="QueuePaths.CheckExtend"/>` at
     `CheckQueueCell`.
   - The `QueueTests` source scan still holds: the instance `CheckQueueCell`
     comes before the static one, which calls `CheckExtend`, and `RecomputeQueue`
     → `CheckQueueCell(` → `TrySpendCell` stay in order.
2. **`docs/FIDELITY-REGISTER.md`.** Only line numbers conflict. Take either side,
   then run `python3 tools/fidelity_register.py --write`. That gives **189**:
   186 + PATH-011..013, with GATE-002 → GATE-003 count-neutral. `--check` passes.

Tested on the resolved scratch tree: Release build, and `OpenTPW.Tests` without
assets **978 / 250 / 0**. The extra test is the gate's. Not run on the merged
tree: the assets tests, the smokes and `--m3-gate`.

### Builds, tests and checks (267c420)

| Check | Result | Author |
| --- | --- | --- |
| `OpenTPW.Tests`, no assets (SDK 10, Release) | 977 / 250 / 0 | 977/250/0 |
| `OpenTPW.Tests`, `OPENTPW_GAME_PATH` | 1156 / 71 / 0 (11 min 49 s) | 1156/71/0 |
| Native front-end smoke | passed, 342 frames | 342 |
| Native sandbox smoke | passed, 1108 frames | 1108 |
| Native jungle smoke (`--load-original-level jungle`) | passed, 4328 frames; "laid 6 cells (52, 14) -> (52, 19) for $120" | 4318 (frame counts vary run to run) |
| Evidence runner (`--mac-bin`, `--pc-data`) | OK: 13 Python suites, 861 tests, 119 skipped. `lanes/review` ran 356 tests, 78 skipped. Includes `test_path_v2.py` (8 tests; its `Binary` class skips because the runner does not export `OPENTPW_MAC_BIN`) | — |
| `test_path_v2.py` with `OPENTPW_MAC_BIN` | 11 tests OK | — |
| `fidelity_register.py --check` | OK, 189 | 189 |
| CRLF | `git diff d21fb4a --stat` = `--ignore-cr-at-eol --stat` (16 files, +1093/−87) | — |
| Whitespace | `git diff d21fb4a --check` empty; no added line ends in a space or tab | — |
| `libveldrid-spirv.dylib` | already in the ignored `native/osx-arm64/`; copied into the scratch tree only | — |
