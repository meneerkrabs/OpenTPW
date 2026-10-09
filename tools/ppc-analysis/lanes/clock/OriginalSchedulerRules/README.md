# OriginalSchedulerRules

Standalone .NET 8 integration contract for the identified Feral Mac clock rules.
It has no package dependencies, accesses no original assets, and is not wired
into OpenTPW. The executable runs synthetic checks; the public clock/scheduler
classes provide an explicit contract for a later integration review.

Evidence: [PPC-clock.md](../../../../../docs/reverse/PPC-clock.md), especially
commits `126ee3e` and `3b9b322`. Mac evidence does not qualify Windows/Patch 2.

## Inputs and supported behavior

`OriginalElapsedClock` starts from a caller-supplied raw millisecond word and
optional double accumulator. It models unsigned source subtraction, double fused
scale accumulation, finite unsigned saturation, and integer pause bookkeeping.
A paused read returns the stored elapsed word without sampling. Pause/resume
methods are idempotent. Control input explicitly supplies release/press, raw time,
and whether the identified host-state field is one. Release speed actions apply
×/÷1.25 with 0.25–2 clamps; a scale change does not sample the old scale first.
Clock origin, control routing, host-state meaning and direct unclamped scale
setters remain integration responsibilities. Multiple raw counter periods cannot
be inferred from two sampled words.

`SchedulerRules.Advance` accepts immutable state and explicit observed clock,
flag-8 override, flag-1 exclusion and numeric mode. It models 31 ms ceiling
steps, the signed 2000 ms backlog clamp, unsigned phase/counter increments,
independent script-manager counter, every-eight world phase, three eligible
world phases per normal callback, and consumed phases after that cap. Work runs when application flag 8 is set OR gameplay flag 1 is clear.
Only flag 8 clear AND flag 1 set suppresses scripts/world work while advancing
scheduled time and phase. Modes 0 and 2 follow the direct turn branch; mode 1
follows the identified wrapper branch. Complete user-facing names and runtime entry
routes for these numbers remain unresolved. Inputs assume the normal callback
path resets its turn-work cap. Inputs are a fixed snapshot for this bounded
call; changes caused by native callbacks within a loop remain an integration
responsibility. No physical frame rate is promised.

A frozen clock prevents new elapsed time, but an existing scheduler backlog can
still be serviced: pause is not an invented additional world-exclusion flag.
Eligible ordinary script IDs match the independent manager counter's low three
bits, after that counter increments; the native phase-bypass flag remains outside
this slice.

## Explicit unsupported boundaries

`UndefinedBoundary` is a **new contract diagnostic**, not a field found in the
original game. Unknown modes, crossing the signed timestamp half-range, or a
scheduled timestamp overflow return the original state and zero speculative
work. The original signed-boundary runtime behavior remains unqualified.
Nonfinite conversion inputs throw as an explicit unsupported-domain guard;
this does not claim the original executable threw. Native FP exception modes,
NaNs, nondefault native rounding modes, forced-step sources, alternate injected
clocks and serialized clock-load
validation are outside this contract. The caller must inspect `IsDefined` before
using scheduler outputs.

No calendar helper selects a civil calendar: the original conversion depends on
Mac Script Manager configuration. Its epoch and turn conversion remain documented
evidence until that calendar assumption is explicitly bound for an integration.

## Run

With an installed .NET 8 SDK:

```
dotnet restore tools/ppc-analysis/lanes/clock/OriginalSchedulerRules/OriginalSchedulerRules.csproj
dotnet build tools/ppc-analysis/lanes/clock/OriginalSchedulerRules/OriginalSchedulerRules.csproj --no-restore
dotnet run --project tools/ppc-analysis/lanes/clock/OriginalSchedulerRules/OriginalSchedulerRules.csproj --no-build
```

The local `NuGet.Config` clears package sources; restore uses the SDK's installed
framework reference packs. Checks cover zero delay, ceiling/overshoot, backlog
clamp and dropped phases, gates, modes, pause/resume, release scaling, integer
pause offsets, unsigned source/counter wrap, saturation and unsupported signed
boundaries. Build enables SDK analyzers and treats warnings as errors.

## Native branch regression

`NativeBranchRules.json` stores decoded BO/BI/target metadata, not instruction
bytes. Regenerate it with `native_scheduler_branches.py` against the identified
Mac binary. The checks interpret BO 4 as branch on EQ clear, BO 12 as branch on
EQ set, then follow the recorded destinations to build the gate truth table and
mode routes independently of the scheduler implementation. This corrected the
prior inverted flag-8 gate and incorrect mode-0 suppression in `a5263bb`.

## Saved shared-clock epochs

`SharedClockEpochs.cs` models a separate, source-proven layer: capture/save use
adjusted scaled and unscaled words; alignment computes `saved-currentBase`
modulo2^32; later getters add those outer offsets modulo2^32. Base inputs must
exclude the outer offsets being replaced. Scaled base still includes native
forced/normal selection, pause semantics and transition offset+56; unscaled
base is its pause-aware getter. This does not serialize or reset source doubles,
scale, pause/forced state, scheduler counters, script IDs or deadlines.

`ApplyPostLoadHook` models only the decoded numeric-selector comparison: incoming
r6==1 skips this hook, other values invoke it. Numeric load selector, GameType,
callback state and world state are separate fields. Complete selector1 lifecycle
and native partial-read mutations remain unqualified. Requiring exactly8 bytes
for `SavedClockEpochs.ReadLittleEndian` is strict evidence-tool policy, not a
claim about original I/O errors.

`NativeEpochRules.json` holds decoded BO/BI/target metadata for the regression.
Generate it with `clock_epoch_evidence.py /path/to/mac-feral/bin --contract-rules`.
The seven new checks cover real-word re-alignment, a restored deadline63ms away,
unsigned offset/read wrap, saturated-base outer wrap, independently decoded
selector truth tables, retained offsets on the skip path and exact pair width.
The project now has42 standalone checks. No live game references this module.

The later selector-lifecycle witness refines the entry-route boundary: selector0
returns after initial header processing; selector1 returns after the generic
prefix operation, before SSEM/KOLC/TNAV and saved-script graph reading. Both
bypass the pre/post hooks. `AppliesPostLoadAlignment` remains the local predicate
at its qualified call site; it must not be used alone to decide whether a full
entry route actually executes that call. No source-zero reset follows from
these bypasses, and generic header/prefix callee mutations remain unqualified.
