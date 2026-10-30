# Bounded staff sprite VM core

UI-092 remains open. `StaffSpriteVm`, `StaffSpriteVmState`, and `StaffSpriteCloneCopy`
provide an independently testable, GPU-free implementation seam. Gameplay, the
staff renderer, employee windows, the sprite scheduler and save formats do not
consume this core yet. In particular, this change does not replace the current
employee portrait's world-publication/fallback behavior.

## State and copy contract

The detached state represents the complete record's 70 word positions: opaque
header word, handle, current/restart/last PC, program table, run state, descending
20-word stack, IF/call/loop depths, signed 32-bit due/interval, 19 raw register
words, 17 opaque draw-cache words and the output-valid word. No texture, person
object, registry entry or original pointer is kept in it.

A semantic PC includes both the named program and the instruction index. Stack
words use complete semantic PC tokens too. They are deliberately not native word
offsets and are not a new save wire format. A source PC may therefore be copied
into a clone without changing the program identity of its retained loop return.
The restart PC and last PC are independent of the current PC.

Full clone creation deep-copies all words, then overrides only the handle, XYZ,
primary scale and flags. Partial refresh copies exactly:

- Program table and current PC
- Kind, variant, slot, intro and hold
- Alpha, secondary scale and heading
- Frame, frame count and output-valid word

Both operations transform the clone's existing flags as
`(flags & ~0x200) | 0x50000`. Partial refresh never copies the source flags first.
It preserves all other fields, including inactive stack/cache words, source-
independent due/interval, scratch, run state, restart/last PC and depths. Copy
operations do not infer lifecycle, validate resources or normalize control state.
Callers must provide the correct live source and distinct owned clone; identity,
generation and registration checks belong to the later owner integration.

## Programs and instructions

The immutable clean semantic catalog contains current host staff templates 1, 2,
3, 9, 10 and 17 through 23, guard release template 24 and decoded template 25.
Catch naturally jumps to hold; release jumps to stand. Template 25 changes neither
slot nor flags. Its specific staff gameplay caller remains unestablished.

All four performance programs are distinct graphs. Program 0 has its actual IF,
DO/WHILE and scratch state: a positive intro yields 0 through intro once, then the
main range starts at intro; a positive hold adds hold+1 zero-frame yields. Program
1's inclusive comparison yields frame count as well as the preceding frames.
Programs 2 and 3 repeatedly yield frame 2. The VM preserves these raw results;
it does not clamp them into plausible atlas frames. Resource/range validation is
still required by a future renderer. No claim is made that all four performance
programs are used by shipped staff assets.

The required operations are SET, ADD, raw COPY, SLOT, FRAME, FRAME-register, JMP,
IF register greater than literal, ENDIF, DO and WHILE register not-equal/less-or-
equal to register. SET and ADD convert integer operands for float registers 1
through 6; other registers use unchecked integer words. Float ADD uses a wider
intermediate before the single-precision store. SLOT does not update frame count.
False IF skips its structured body but still dispatches its ENDIF. Natural jumps
do not replace the restart identity. Unsupported opcodes/PCs fail explicitly.

The bounded staff graphs contain no END. Synthetic programs test the interpreter's
terminal rule without attributing termination to a supported staff animation.

## Timing, output and safety boundaries

Each caller-supplied scheduler opportunity uses direct signed `now > due`, with
unchecked `due = now + interval`. Equality does nothing. Late samples execute at
most one run of at most 50 instruction dispatches, with no catch-up. This class
neither reads a clock nor invents registry, source/clone, batching or UI-refresh
ordering. Different partial-refresh boundaries may legitimately produce different
clone histories. Host/native cadence equivalence is not claimed.

FRAME writes the frame register, sets output-valid to frame != -1, and yields.
The output word is not a rendered-frame publication. Run state 3 does not execute
instructions but still rebases due. END sets state 4 and clears output; a later
due opportunity reports `RemovalDue` to the future registry owner without removing
or recreating anything itself. Template replacement does not resume state 3 or 4.

Template replacement changes only current/restart PC, stack pointer and the three
depths. Clock, run state, old output, last PC and inactive stack words survive.
Applying a resolved performance entry additionally updates slot and frame count,
never intro/hold. A zero interval request means no change; a raw state can still
have zero or negative intervals and uses the signed arithmetic above.

Fresh construction has the proven control/clock setup, alpha 255 and primary/
secondary scale 1. Pose and bank metadata are still caller inputs. Zeroing unused
scratch, stack, last-PC and cache words is a host safety choice: the native
constructor does not initialize every allocated word. This API does not claim
byte-for-byte reconstruction of a native allocation or previously lost history.

Diagnostics are distinct from unsafe or unsupported data:

- ENDIF decrements below zero and reports the diagnostic without repairing depth.
- WHILE at nonpositive loop depth consumes its operands and leaves control state
  unchanged; subsequent instructions still execute.
- DO on a full stack leaves its words untouched but still increments loop depth.
- False WHILE may pop/discard an unknown word, or an empty pop, without executing it.
- A taken WHILE needs a supported semantic return PC. Native empty-pop returns
  table offset zero; that target is outside this staff subset, so the core reports
  `UnsupportedStackPc`. It does not guess a program or reset the clone.

Bad stack-pointer bounds, unsupported table/current/return PCs, invalid register
operands, missing IF boundaries and unsupported instructions return an explicit
`Invalid` result and clear output. The offending instruction remains identifiable.
Nonfinite float arithmetic is explicitly unqualified; arbitrary raw float bits
can still be copied. Signed diagnostic depths and unused stack words are not
rejected by blanket state validation. A cap hit remains running and retains its
existing output; it is reported separately from a frame yield.

SET flags is ordinary executable behavior. A clone can lose its portrait flags
while executing without source refresh; Tick never silently reapplies the screen
mask. Future rendering must respect the actual screen/world route. Draw-cache
words are preserved by copying but not recomputed by this GPU-free core; native
frame-resource/cache work and draw eligibility remain integration work.

## Verification and remaining work

`StaffSpriteVmTests` is synthetic C# coverage of complete sentinel copy manifests,
deep ownership, independent `StaffScripts` output oracles, all four performance
graphs, actual loop PCs/scratch/depths, cross-program copied-PC/retained-stack
hybrids, catch/release continuations, flags loss, template/output transitions,
state 3/4, signed wrap/equality/zero interval, instruction caps and malformed
boundaries. `test_staff_sprite_vm_contract.py` guards the exact copy allowlist,
bounded timing and the deliberate absence of gameplay integration. Existing CI
runs both the guard and the C# test project.

Local structural guards and repository checks are recorded in the progress note.
The local environment has no .NET SDK: compilation and C# execution were not run
locally and still require exact-head CI. Structural guards do not substitute for
those tests. No original software was executed/emulated, and no GPU or native
render comparison was performed.

Remaining UI-092 work includes reviewed source/window ownership and lifecycle,
scheduler enrollment and ordering, pending source requests, persistence and
migration, resource/cache/flag-aware rendering, source loss/return and rename
window flows, and accepted behavior/render evidence. Nothing in this core derives
missing PC/stack history from elapsed animation time.

**Consumed by the world (round 11, package A).** The staff world's sources (`StaffSpriteState`, `StaffSpriteEvents`) now run this core: the state setters, the walk step, the pre-update, the drop and the pick-up queue and apply templates, and the sprite scheduler (`0x475360`) steps the sources on the even 31 ms substeps (`Level.ScheduleSprites`). The saved wrapper and source are an optional validated field of the staff world's resume file. The hire window's figure and the employee window's portrait stay on the host's renderer-call clock (package W). See `docs/STAFF-PLACEMENT.md`.
