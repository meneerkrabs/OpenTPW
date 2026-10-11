# Review ORACLE-V: PowerPC differential oracle (16bb38e)

October 10, 2026. Independent review of `16bb38e` (base `c6c19ea`): the user-mode PowerPC
interpreter `tools/ppc-analysis/oracle/`, its targets, the C# bridge and `docs/reverse/ORACLE.md`.
Merge checks against fork main `8a000d8`.

Method:

- The disassembly was read with a local `llvm-mc`/`llvm-objdump` (PowerPC 750) on a scratch
  copy of the code section outside the repository. It was not read with the oracle's own
  `disasm.py`.
- Interpreter semantics were checked against reference formulas written from the architecture.
  A brute-force nearest-single search checked the FP rounding.
- Writer traces were confirmed by running the writer fragments in the sandbox, with every call
  stubbed.

Witness: `tools/ppc-analysis/lanes/review/test_oracle_v1.py`. It pins inputs, decoded fields
and results only. No original bytes or listings are pinned.

## Verdict: ACCEPT-WITH-FIXES

- There are no blockers. Every risky instruction that was spot-checked has the architected
  semantics.
- Every escape path stops the run.
- F1, F3, F5, F6, F7 and F8 reproduce from the disassembly.
- Before QUEUE-I or PATH relies on F1/F3, fix OV-1. Fix OV-2 and OV-3 before the oracle becomes
  part of a gate. OV-4 is recommended.

## Findings

**OV-1 (medium, docs/reverse/ORACLE.md §5 F1/F3): the writers are traced, and they refute both
"open readings" of each finding.**

F1 writer: the ride build `0xda874` stores `+46 = 0` (`0xda8f0`). The fragment
`0xdab6c..0xdacbc` then ORs in one bit per boolean of the type record (`r28` = `0xdc320` →
`0x118018` lookup). The economy lane's `schema.py` names each record field:

| Record offset | Field | Ride `+46` bit |
| --- | --- | --- |
| +244 | `UsageInfo.ProvidesRelief` | 0x1 |
| +252 | `UsageInfo.ChillsYouOut` | 0x2 |
| +60 | `Info.IsChoosable` | 0x4 |
| +64 | `Info.HasQueue` | **0x8** |
| +240 | `UsageInfo.ProvidesSecurity` | 0x10 |
| +260 | `UsageInfo.RideHandlesSprite` | 0x20 |
| +248 | `UsageInfo.HoldsLitter` | 0x40 |
| +272 | `UsageInfo.IsFireworks` | 0x80 |
| +72 | `Info.RunsContinuously` | 0x100 |

The witness runs the fragment and confirms every bit. The other `sth +46` sites are the save
byte swap `0x9c7fc` (it preserves the bits) and unrelated structs.

So bit `0x8` is `Info.HasQueue`, not inverted and not "no queue path". `0xdcb74` returns **100
for objects without HasQueue** and the QWTC formula for HasQueue rides. The formula path also
asserts "No queue constant entered in SAM file" when QWTC ≤ 0, which fits rides that carry a
QWTC.

Example: Belly Bounce (QWTC 130, CAP 5, DUR 30, G = 1) gets limit **21**, not 100. The
witness pins this.

Fix in QUEUE-I:

- `RideVisitorBridge.ComputeQueueLimit` becomes `if ( !parameters.HasQueue ) return 100;` with
  its doc comments updated.
- QUEUE-plan §4 step 3 and its "`min(100, 4 × cells)` for HasQueue rides" consequence need
  rewriting.
- So does the APPROX:QUEUE-013 rationale in `QueuePaths.cs`.
- The `EXPECTED` pins for `queue-limit` then flip: `plan` and `csharp` become the agreements.

F3 writer: `data:0x84b2c` has one TOC slot (−27980). The only code that writes through it is
`0x84ea4`, the line tool:

1. It stores 0 at entry.
2. For each intermediate cell it calls `0x80c50` with the word still 0.
3. It stores **1** just before the final cell's `0x80c50` call.
4. It stores 0 again on every exit.

The other users only read the word (`0x80c50`, `0x84154`, `0x8408c`). The witness runs
`0x84ea4` on four drags with the calls stubbed. The word is `[0, …, 0, 1]` per placed cell and
0 afterwards.

PATH-plan's name "last-cell flag" is therefore right. Rule 7 has the wrong polarity: queue over
path is allowed on every cell **except** the last cell of a drag.

Fix:

- `ParkCellMap.CanChangeCellType` becomes `type == 3 && old == 1 && !lastCell`.
- PATH-plan §3.1 rule 7 changes to match.
- The callers already pass `lastCell = index == line.Count - 1`.

**OV-2 (medium-low, `oracle/harness.py:47-52`): a stale shell variable switches the oracle on.**
`bin_root()` reads `OPENTPW_MAC_BIN` first. `run_evidence_checks.py` neither sets nor strips it:
`FIXTURE_VARIABLES` has only `OPENTPW_PPC_BIN_ROOT`. With `OPENTPW_MAC_BIN` exported and no
`--mac-bin`, the runner reported `oracle: 46 ran, 0 skipped`, C# bridge included.

That breaks the runner's contract that a stale shell variable cannot change the result. Fix
one of these:

- read only `OPENTPW_PPC_BIN_ROOT` in the harness;
- or add `OPENTPW_MAC_BIN` to `FIXTURE_VARIABLES` and to `--mac-bin`.

**OV-3 (medium, `oracle/bridge.py:20-73`, `test_oracle.py:93-101`): an unusable SDK fails the
plan pins too.** `bridge.get()` accepts any `dotnet` it finds. The bridge targets `net10.0`
under `global.json` 10.0.401.

With a net8-only SDK (`OPENTPW_DOTNET=~/.local/share/opentpw-dotnet/dotnet`, or a PATH `dotnet`
on CI or Windows), `Bridge.build()` raises `RuntimeError` inside `Oracle.setUpClass`. Every
binary-vs-plan test then errors, not only the C# sides.

Two related problems:

- The oracle picks its SDK independently of the runner's `--dotnet` flag. A plain
  `--mac-bin` run builds and runs C#.
- On a cache miss, `dotnet build` writes `obj/` into `source/*/` and `oracle/dotnet/`. These
  directories are gitignored, but the runner documents "no bin/obj lands in the checkout".

Fixes:

- Treat a build or run failure as `skipped` for the C# sides (`_csharp`), with the build tail
  as the reason.
- Probe `dotnet --list-sdks` for a 10.x SDK.
- Build from a scratch copy, as `CSharpMutation` already does.

**OV-4 (low-medium, `oracle/bridge.py:40-51`): the cache digest misses inputs.**
`_source_digest` hashes `source/OpenTPW`, `source/OpenTPW.Files`, `oracle/dotnet` and
`EconomyTestData.cs`. `OpenTPW.csproj` also references `OpenTPW.Common`, `OpenTPW.ModKit` and
`OpenTPW.Online`. `Directory.Build.*` and `global.json` are not hashed either.

A change that touches only those inputs reuses a stale bridge, so "C# on this checkout" would
compare old code. Fix:

- Hash the transitive `ProjectReference` closure, or all of `source/` without `bin`/`obj`,
  plus `global.json`.
- Use `relative_to(...).as_posix()` so the key is the same on every OS.

**OV-5 (low, `oracle/targets.py:86-91`): `run_binary` turns every `Unsupported` into
`UNDEFINED`.** That covers `sc`, `tw` and any unmodelled opcode. The run still stops, so
the policy holds. But an interpreter gap in a future target would become a counted value
instead of a hard error.

Today only `det-rse-rand` (bound −1 → `divw` by 0) relies on this. Fix: raise a dedicated
`Undefined(Unsupported)` for architecturally undefined results only (`divw`, `divwu` by 0,
`INT_MIN / −1`), and catch only that.

**OV-6 (low, `oracle/ppc32.py:151-152, 179, 194, 784`): floating-point nits.** No current
target reaches them: none changes FPSCR[RN], and no target feeds two NaNs to a multiply-add.

- An exact-zero sum or difference under RN = 3 (toward −∞) gives +0. IEEE and the architecture
  give −0.
- When both FRB and FRC are NaN, `fmadd` propagates FRC. The architecture's order is FRA, FRB,
  FRC.
- The comment at `ppc32.py:784` ("pinned to 0 and flagged via OV") is stale. The code raises.

Both deviations are expected failures in the witness.

**OV-7 (low, docs, ORACLE.md §5 F7 table vs `targets.coast_step_plan`): the reference reproduces
the binary only because the previous force and bank are always zero.** ORACLE.md labels `+812`
as "GForceSickness × |ΔF|₁" and `+832` as "BankExcitement × |Δbank|". The binary does subtract
the previous vector `data:0x57784` and the previous bank `data:0x57780` (`fsub` in double, then
`fabs`/`frsp`).

The reference uses |F|₁ and |bank|, and the inputs write zeros to both previous values. This is
faithful to the game. COAST-plan says they are cleared at reset and never written afterwards. A
TOC scan agrees: only `0x4154c`/`0x41568` (reset) and the step read those slots. The witness
pins this.

A probe with nonzero previous values gave:

| Reference | Mismatches |
| --- | --- |
| `plan-stale-fz` | 309 / 309 |
| A Δ-aware reference | 0 / 309 |

Say in §4/§5 that the target relies on previous = 0, or add the Δ-aware reading. Then a later
writer of those words cannot hide behind the pin.

**OV-8 (nit, `oracle/ppc32.py:384-403`): a branch to a fragment's stop address must lie inside
`allowed`.** `_transfer` validates the target before the loop tests `stop`. RAND reaches its
stop by fall-through and is unaffected. Either document this or check `stop` in `_transfer`.

**OV-9 (info): coverage.** The target pins alone do not catch these interpreter mutations:

- double-rounded `fmadds`;
- NaN → EQ in `fcmp`;
- `srawi` without CA;
- unsigned `cmpw`;
- unsigned `mulhw`;
- `frsp` truncating.

`test_ppc32.py` catches all of them. It misses only these: SO stickiness, `fsel` with a NaN
operand, `stfs` bit selection of a non-single double, a fmadds double-rounding tie and the NaN
operand order. `test_oracle_v1.py` covers the first four and pins the NaN order as OV-6.

The mutation checks in `test_oracle.py` are meaningful:

- 4f → 5f moves the queue mismatches from 6 to 66.
- A `rlwinm.` without record breaks F1, F3 and F8.

The unbounce inputs never have start > now (negative elapsed). The C-remainder sign path is
therefore not exercised. That is harmless while start ≤ now holds in the game.

## F1–F8

| Finding | Re-derived | Result |
| --- | --- | --- |
| F1 queue limit `0xdcb74` | `0xdcba4` `lhz +46`, `rlwinm.` bit 28, `bf eq` → formula at `0xdcbb8`; clear bit → `li 100` | **Confirmed**. Writer: bit 0x8 = `Info.HasQueue` (OV-1). Correct fix: 100 when **not** HasQueue |
| F2 saturation | not re-derived; the `csharp-flag-clear` run reproduces 6/515 | as reported |
| F3 `CanChangeCellType` `0x8408c` | `0x84124..0x84148`: new 3, old 1, word via TOC −27980; `== 0` → allow | **Confirmed**. Writer `0x84ea4`: 1 only on the drag's last cell. Correct fix: `!lastCell` |
| F4 SetCellType boundaries | not re-derived | as reported (2/511) |
| F5 UNBOUNCE `0xae020` | `cmpw deadline, now; bf lt` (strict); `/1000` and `/200` by multiply-high, `add.` must be 0 | **Confirmed**. C# `TakeExpired` (`Until <= now`, no window) differs |
| F6 smoothing | `0xe6b9c fmadds`; `0xe6bbc mulli 99`, `sth` u16, `mulhwu`/`srwi` /100 | **Confirmed** |
| F7 coaster step `0x415b4` | single caller `0x3ffa8`. `f0` = last `fmuls` stored to `sample+40`. Nothing writes `f0` in `0x3ff94..0x3ffa4`, and the step defines `f0` only on the inverted path | **Confirmed** (and see OV-7) |
| F8 coaster final `0x41900` | `0x419d0`/`0x419d8` `rlwinm.` bits 25/24 (0x40/0x80), each `bt eq` skips the increment | **Confirmed**: counts sections with **both** bits |

## Interpreter (spot checks, all pass)

- Integer:
  - `adde/subfe/subfze/addme[o][.]` CA, OV and sticky SO, CR0 incl. SO;
  - `subfic`/`addic` CA, `nego` OV;
  - `mullwo` OV, `mulhw`/`mulhwu`;
  - `divw` truncation (−2³¹/−1 and /0 raise `Unsupported`), `divwu`;
  - `sraw` for shift amounts 0..64 and `srawi` CA for every shift amount, `slw`/`srw` ≥ 32;
  - `cntlzw(0) = 32` with the record form;
  - `rlwinm[.]`/`rlwimi`/`rlwnm` with random masks, including wrap masks (mb > me).
- Branch: all 32 BO values × CR bit × CTR ∈ {0, 1, 2} for `bc`; `bdnzlr`; `bcctr` with
  decrement raises; `lmw`/`stmw`.
- FP:
  - `fmadds` against brute-force nearest single, plus a constructed double-rounding tie;
  - `fadds`/`fdivs` in all four rounding modes;
  - `fmadd` against Python exact-to-double;
  - `frsp` in all four rounding modes;
  - `fctiw`/`fctiwz` on ties, ±2³¹ edges, ±inf, NaN and every RN;
  - `fcmpu`/`fcmpo` CR field and FPCC with NaN, other CR fields untouched;
  - `lfs` keeps an SNaN signalling.

## Sandbox

Every escape stops the run:

- `bl`, `blr` or `bctr` outside allowed code;
- falling off the end of a range;
- `sc`, `tw`, `mftb`, `dcbz`;
- a store into code;
- a stub returning to an LR outside allowed code;
- an unstubbed call or import (`test_oracle.py`).

Imports resolve to unmapped trap vectors. Only `SimThemePark.data` is read, the PC executable
is never touched, and only stdlib is used. Building the bridge restores NuGet packages that the
project already has; on a cold cache that restore needs the network. No original code runs
while it does.

## Git hygiene, tests and merge

- Original bytes: none. ORACLE.md quotes three single instructions as citations
  (`rlwinm. r0,r0,0,28,28`, `bc 4,2,…`, "`lwz` via TOC −27980"). No listings, dumps or word
  pins.
- CRLF: `git diff --stat c6c19ea 16bb38e` and `--ignore-cr-at-eol` are identical (14 files,
  +3207/−2). There are no CR bytes, and the `diff --check` with
  `blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol` is clean.
- `run_evidence_checks.py` on `16bb38e`:
  - without `--mac-bin`: OK, 999 tests, 274 skipped (oracle: 46 ran, 14 skipped);
  - with `--mac-bin` and `--dotnet ~/.local/share/opentpw-dotnet/dotnet`: OK, 999 tests, 161
    skipped (oracle: 46 ran, 0 skipped).
- The same runs with this witness added are both OK, with 1023 tests (280 and 161 skipped).
  `lanes/review` reports its 5 expected failures (OV-2, OV-3, OV-4, OV-6 ×2) in the "failed"
  column.
- Trial merge onto `8a000d8` is clean, including `test_round8.py` and `run_evidence_checks.py`.
  With `--mac-bin` and `--dotnet`: OK, 1002 tests, 161 skipped. The merge was aborted
  afterwards.
- `repo-relative as_posix()`: the runner and `test_round8.py` comply. The bridge digest does not
  (OV-4).
