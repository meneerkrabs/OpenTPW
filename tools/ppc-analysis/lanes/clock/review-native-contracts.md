# Independent review of the standalone native contracts

Reviewed exact commits `00a6393a737846397f83a9787f525293d0b92037` (guest),
`3ae2dd944644bb137260d913b0b6dee6a37946df` (advisor LIP),
`8b256a36bbfe9d3f7e7f7481d2436326737a3b7f` (advisor queue), and
`643f13245abf2ff5d54eae93457a374105b7780a` (UI metadata reader).
Temporary canonical-path Git snapshots were used; no peer/root source was
written. Original binaries were inspected statically, not executed or emulated.
No original bytes, tables, disassembly or assets are stored in this review.

Shared native identity: Feral `SimThemePark.data` SHA-256
`04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5`,
code section 0, TOC data section 1 offset `0x8000`. These are Mac contracts;
Windows/Patch 2 runtime equivalence and production wiring remain separate.

## Guest `00a6393`: objection on temporary-history control flow

**HIGH correctness mismatch — `GuestOriginalRules.ApplyHistory` / `ApplyOneHistory`.**
The used-history array `guest+480..486` is first-match: after a matching division,
branches at `0xe98f4`, `0xe991c`, and `0xe9950` go to `0xe9978`, skipping the other
used slots. The temporary array `+488..494` behaves differently. Its divisions
at `0xe99ac..0xe99b8`, `0xe99d8..0xe99dc`, `0xe99fc..0xe9a0c`, and
`0xe9a2c..0xe9a30` fall through to subsequent comparisons: every matching
slot divides cumulatively by 5, 4, 3, then 2. The helper returns after the first
match in both arrays.

A safe primitive counterexample, confirmed against the exact C# source:
score 600, attraction ID 7, used `[0,0,0,0]`, temporary `[7,7,7,7]` gives
**helper 120 versus native fall-through 600/5/4/3/2 = 5**. All inputs satisfy
the helper's declared types/lengths. No unique-temporary-history invariant is
proven or required by its API. Repair the second history to apply all matches,
or prove and explicitly enforce an appropriate narrower domain; synthetic
first-match tests cannot justify the present implementation.

Other bounded arithmetic is accepted:

- `0xddd3c` uses unsigned comparison for four-position queue-cell stepping;
  quotient/remainder is correct only for the declared sufficiently long chain,
  not its native terminator/geometry handling.
- Distance divisor 450, signed-short-to-unsigned optional divisor, near/far
  queue gate at squared displacement >8, wrapping count*100 and capacity<<2,
  excitement byte difference, and seven-term low-word normalization match
  their inspected paths. Products/additions wrap; normalization uses DIVWU.
- Ride illness `0xea4ec..0xea52c` uses signed integer divisions and multiplication;
  `0xea540/0xea544` convert/round the increment and add to float illness before
  clamping. Shop subtraction/clamping preserves the bounded finite need domain.
- Caller tables/weights/category gates are explicit. Zero or wrapped-zero
  divisors, nonfinite/out-of-range needs and unsupported table shapes are helper
  diagnostics, not recovered native exception behavior. For example, rejecting
  a shop need above 100 does not prove the native subtraction/clamp would reject it.

Validation: all 18 standalone tests and the small identified guest binary witness
pass. Their success did not detect the temporary-history error; the original
branches and independent primitive comparison provide the contrary evidence.

## Advisor LIP `3ae2dd9`: conditional acceptance

The exact driver file is unchanged in `8b256a3`; its 21 checks were run as part
of the combined 46-check executable. The identified binary/selected asset witness
also passes. Direct inspection confirms:

- Initial and update mark conversion uses signed raw-word /1000 with truncation
  toward zero (`0x6fe4..0x6ff8`, `0x770c..0x7720`). Clock additions wrap int32.
- Pending speech tests signed deadline-minus-now, accepting equality; LIP and
  mouth updates use signed **strict now > deadline** (`0x76c0`, `0x7778`).
- `0x76c8..0x7754` consumes one mark and toggles once, then handles the raw or
  converted -1 end condition before mouth selection. It has no catch-up loop.
  The initial converted -1 path differs from the update end check, as the driver
  documents. Input word storage is copied from the caller.
- Mouth randomness requires both talking and active LIP (`0x7758..0x776c`),
  runs at most once per expired update, and sets deadline now+100. Otherwise the
  desired mouth is normal. A missing LIP can start deferred speech without
  enabling random mouth selection.

The RNG delegate's nonnegative/CRT-compatible promise, bounded first-mark/sentinel
input requirement and exception diagnostics are contract conditions. They do
not prove original treatment of every malformed LIP or reentrant delegate. No
PCM clock, original random sequence, speech/animation/visibility callback or
live presentation-update cadence is supplied, so acceptance is conditional on
those caller bindings and the declared numeric domain.

## Advisor queue `8b256a3`: normal-path acceptance, completion integrity gap

Native comparisons match the normal helper paths: cached selection is strictly
above the threshold (`0x8850`), computed-wrapper revalidation accepts equality;
lowest/highest ties retain the first slot; full eligible queues acknowledge even
without replacement (`0x8d10`). Eligibility order preserves tutorial/repeat before
once/slap overrides, and duplicate limits still apply. Repeat comparison is
unsigned (`0x90ec`), with saved-quarter zero bypass; elapsed is separately shifted
raw game ticks then low-word subtraction (`0x12111c..0x121124`). Busy testing is
unsigned now < wrapping(start+duration) (`0xa154..0xa15c`). Slap comparison is
signed (`0xe0fc..0xe100`). Cyclic increment/wrap and response-range checks are
signed (`0x8978`, `0x89d8`, `0xb8c8`), including unnormalized corrupt signed wrap.

Consumption before the external wrapper and success-only history/reservation
updates agree with the reviewed path. Scores/producers, playback success and
post-attempt clocks remain explicit inputs, not fabricated events.

**MEDIUM contract-integrity gap — `CompletePlaybackAttempt`.** The method checks
slot and advice reference but not the complete begun selection. A confirmed safe
API counterexample begins selected variant 0, then completes the same slot/advice
with `Variant=2`; the helper accepts it and stores history variant 2. Native
controller `r29` retains its chosen variant across the wrapper and stores that
at `0x8a3c`. Reject changed selection fields or store the private pending tuple's
variant. This is not an objection to callers that pass the exact original tuple,
but its current stale-completion diagnostic is weaker than the stated lifecycle.

All 46 combined advisor checks pass. They establish regression coverage, not
native proof, and do not cover that altered-completion case.

## UI `643f132`: conditional metadata-reader acceptance

The native entry/interpreter `0x181aac/0x181afc` reads big-endian halfwords;
command 0 combines low/high halves for attributes/ID and signed rectangles,
allocates through `0x17fe78`, then recursively reads the child scope. Command 5
ends scope. The source preserves allocation order, parent indexes, ordered
properties, and external untyped properties without executing a GUI callback.
Signed-byte XOR followed by low-word multiply 47 matches the name hash.
There is no floating-point layout/FMA implementation here to qualify: renderer
projection, font extents and geometry/style semantics remain external.

The native type-0 factory returns null at `0x18020c`; the reader instead raises
an explicit unsupported-type diagnostic. Likewise unknown existing-parent types,
repeated implied-child lookup, opaque geometry subtypes and budget failures are
not an emulation of all native normal-return/lookup/allocation paths. This is
acceptable for its clearly declared fresh-allocation metadata subset.

Validation: 19 standalone checks pass. The small native UI and phase-two witnesses
pass. Off-Git exports from the pinned PEF independently compare **55 tables /
934 controls**, consumed words, IDs/types/attributes/rectangles/parent relationships,
effective properties, **3 constructor font bindings** and **2 identified label
editions** with the C# reader. The label verification uses the actual paired
BFST/BFMU resources; no general edition index shift is inferred. Original private
inputs remained outside Git. These tests do not qualify live GUI execution,
stateful reuse of existing children or Windows runtime widget behavior.

## Integration disposition

Hold guest history acceptance until its temporary-array fall-through is repaired
or a uniqueness domain is independently established. Advisor LIP and normal queue
arithmetic are conditionally accepted; strengthen completion tuple integrity
before treating its diagnostic lifecycle as validated. UI is accepted only as
bounded metadata for the identified fresh-allocation inputs. None of these
standalone modules is a complete original subsystem or currently wired into the
production simulation.
