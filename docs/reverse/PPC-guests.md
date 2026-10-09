# Feral PowerPC guest evidence

2026-10-09. Static evidence for the identified Feral Mac application only.
No original game was executed. No gameplay code was changed and no original
approximation ID is resolved by this lane.

`SimThemePark.data` SHA-256:
`04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5`.
Code is PEF section 0; instantiated data is section 1; addresses below are
section offsets, not OS addresses. Main transition vector is data:`0x8d48`;
its relocated TOC word at `0x8d4c` establishes TOC `0x8000`.
The application has **zero exports**. Gameplay names below are analyst labels,
grounded by direct call operands and diagnostic strings, not exported function
names. Function discovery from `analyze.py` was used for navigation only, with
its unsafe heuristic traceback scan removed in a temporary copy outside Git.
The final witness uses the validated PEF reader and exact bounded code hashes,
relocations, and decoded operands. LLVM 21.1.8 `llvm-mc`, target
`powerpc-apple-darwin`, independently decoded the inspected blocks locally.
No disassembly, assets, or extracted instructions are committed.

Reproduce from the repository root:

```sh
python3 tools/ppc-analysis/lanes/guests/evidence.py /Users/sander/server/game-assets/mac-feral/bin
OPENTPW_PPC_BIN_ROOT=/Users/sander/server/game-assets/mac-feral/bin python3 -m unittest discover -s tools/ppc-analysis/lanes/guests -p 'test_*.py' -v
```

The witness fails closed on binary identity, selected code-block digests,
relocated targets, operation classes, field offsets, and direct-call targets.
It prints interpreted facts and coefficient values only. Without the local
binary, ten arithmetic/decoder/rejection tests run and the original-input test skips.
With the specified input all eleven tests pass, zero skips. The original-input
witness pins 33 bounded functions, including constructors and environmental
effect writers; it is not a claim of complete instruction interpretation.

## Movement and coordinates

High confidence: destination routine code:`0xff224..0xff738` receives navigation
position X/Y at object offsets `+8/+12`. At `0xff24c/0xff258` and
`0xff264/0xff270`, arithmetic shifts by 16 produce the start and destination
cell coordinates. Route waypoints use `(cell << 16) + 32768`, visible at
`0xff360..0xff384`: **16.16 cell coordinates and half-cell centres**.
This supports a cell coordinate convention, not OpenTPW's constant walking
speed or its random lateral offsets.

Speed setter code:`0xffe38..0xffeb0` caps its floating input at 2.0, then stores
`trunc(input * 0.4 * 65536)` at navigation `+24` and
`trunc(input * 0.2 * 65536)` at `+28`. Each stored integer has a minimum of 655.
The doubles 0.2, 0.4, and 65536 live at data:`0x5650/0x5658/0x5660`;
the cap is data:`0x5668`. These are **per-consumer step quantities**, not
cells/second. The caller at `0xe6ba8` passes a speed derived from fields
`+192/+194/+196`, a balance input, and a smoothed `+200` field; its update at
`0xe6b28` also decays `+196` by integer 99/100. It does not establish
`Energy < 20 => speed * 0.7`.

Routing call path: `0xe6864 -> 0xffcfc -> 0xff224 -> 0x101c1c`, with
`0x101c1c` calling `0x10292c` and `0x101434`. The destination routine retains
a bounded cell route and additional accumulated distances; no proof identifies
the current cached breadth-first flow fields as the original algorithm.
Unresolved: full route-search control flow and neighbour costs, movement update
cadence, units of the speed caller's balance input, and the save/navigation
coordinate boundary. Multiplying the stored step by OpenTPW's 60 Hz would be
an unsupported rate inference.

## Needs, happiness, and time

High confidence field identities come from score diagnostics and their argument
loads, not variable-name guessing:

| Guest field offset | Meaning | Binding evidence |
| --- | --- | --- |
| `+420` | thirst | `0xe94b8`, diagnostic reference `0xe9510` |
| `+424` | hunger | `0xe9528`, diagnostic reference `0xe9578` |
| `+428` | toilet | `0xe95a0`, diagnostic reference `0xe960c` |
| `+432` | illness | `0xe963c`, diagnostic reference `0xe96a8` |
| `+444` | exit counter | save/load name `mExitLevel` at `0xe8048` |
| `+544` | state | store at `0xef728`, dispatch load at `0xef268` |

The score diagnostic string base code:`0x1d04c2` is loaded through relocated
TOC slot data:`0x3440`. The bounded needs block is
code:`0xeece4..0xef240`; caller `0xfaa5c` enters it, then `0xfaa64` calls the
state handler. It obtains the guest's 16-bit thing ID through `0xfa9a4`.
At `0xeed24..0xeed40`, the update proceeds only when the low two bits of a
global counter equal the low two bits of that ID. The counter comes from
`*(data:0x11ef04) + 0x1da70c`; its TOC pointer slot is data:`0xa54`.

On an eligible update, `+444` decreases by one. Encoded current cell
`1 + X + (Y << 7)` is passed through `0xd7310`; that helper returns
`map_base + 0x1b1104 + 10*(cell_id - 1)`. Three **signed 16-bit cell values**
at `+0/+2/+4` are added to guest floats `+412/+432/+424` respectively.
Each result is clamped to `[0,100]`. The initialization and meaning of those
cell values must be traced before assigning a constant hunger or illness rate.
The field `+412` is used as the happiness-like counter elsewhere in this block;
its complete binding remains outside the bounded witness.

When the same global counter's low four bits are zero, the block additionally
adds 1 to toilet and 2 each to hunger and thirst, again clamping to `[0,100]`.
These guards are nested: the fixed increments require both counter mod 16 = 0
and guest thing ID mod 4 = 0. The allocator follow-up below establishes that
other ID phases exist. Do not describe these writes as “every visitor, every
16 ticks.”
At `0xeef6c..0xef0b8`, each of illness, hunger, thirst, and toilet reaching
integer 100 can add a separate −1 to `+412`, with the same clamp.
States 16 and 17 are checked later at `0xef0ec`; they do **not** bypass the
earlier needs or exit-counter writes. This differs from OpenTPW's paused
exit timer while riding.

Additional inspected thought selection code:`0xe8c58`: combined hunger/thirst
thought is selected when both integer fields exceed 90. Individual hunger,
thirst, toilet, and illness comparisons use the floating 99 constant after
integer conversion. This is not evidence for the current thought threshold 70.

The constructor at `0xe7644..0xe7bbc` establishes additional starting values:
`+412` is set to 50.0 at `0xe7820`; thirst and hunger are set from separate RNG
results modulo 50 at `0xe785c/0xe7888`, and toilet from an RNG result modulo 30
at `0xe78bc`. Thus their integer starting domains are 0..49, 0..49, and 0..29,
respectively, assuming the ordinary unsigned RNG result domain. Illness is
zeroed at `0xe78c8`. This is arithmetic evidence for distinct initial needs,
not a claim about RNG distribution or seed equivalence. OpenTPW starts
happiness at 60 and all needs at zero.

No Energy field or the current energy drain/recovery mechanism was established.
Do not relabel illness or generic movement adjustors as energy. The current
`Energy = 100`, resting/riding recovery, nausea decay, and need growth per
second remain approximations. Exact rates need counter advancement/caller
frequency, map-cell value initialization, and park
clock conversion. The OS timer witness alone cannot close these dependencies.

### Thing-ID allocation and scheduling follow-up

High confidence: guest constructor `0xe7644` sets category argument 1 and calls
person constructor `0xe4810` at `0xe767c`; the latter calls thing constructor
`0xfa718` at `0xe483c`. The thing constructor calls allocator `0x105238` at
`0xfa768`, then stores the returned 16-bit ID at guest `+0` (`0xfa77c`).
The getter `0xfa9a4` merely copies that halfword. It does not strip type bits,
shift an aligned handle, or otherwise transform the allocation result.

Free-slot initializer `0x104e84..0x104f38` constructs 20-byte slots with IDs
1..10239 in slot `+4`, linked through `+16`. Its two-slot loop writes the
index at `0x104edc/0x104ef4`, increasing it by one between writes, and the last
slot is explicitly assigned 10239 at `0x104f28`. The allocator pops the free
head, links it into the live list, stores the thing pointer in slot `+0`, and
returns slot `+4` unchanged at `0x1052e0..0x1052e4`. TOC slots
`0x17b4/0x17b0/0xa44` resolve to free-head/live-head/table-base pointer globals
data:`0xecef0/0xeceec/0xecef4`. Fresh IDs therefore span all four low-bit phases;
allocation does **not** repair the nested needs guard by aligning guest IDs.
Reused/save-loaded IDs and other creation paths require separate validation.

Park-turn function `0x10536c..0x10565c` increments the same `+0x1da70c`
counter once at `0x105398..0x1053a0`, before traversing the live list. Its
object loop calls `0xfa9b0` at `0x10541c`; the guest case in that dispatcher
calls needs at `0xfaa5c` and state dispatch at `0xfaa64`. No per-object counter
increment occurs in this bounded loop. Eligibility flag `thing+3`, park mode,
list membership, and save-loaded allocation still affect which objects update.
For this path, the fixed hunger/thirst/toilet increments are confined to
phase-zero IDs. Whether that is intentional Feral behavior, a port defect,
or compensated by another need source is unresolved. It is not evidence for
a uniform visitor growth rate and must not silently become one in OpenTPW.

## Attraction score and choice

High confidence: score code:`0xe9164..0xe9a84` computes **matches normalized by
the active weight sum**, not OpenTPW's additive attraction-value formula.
The active weight object is data:`0x54860`, loaded from TOC slot `0xa7c`.
The seven weight offsets are `+112,+116,+120,+124,+128,+132,+136`.
Names inferred from the corresponding diagnostic terms are distance, queue,
excitement, thirst, hunger, toilet, illness. SAM schema contains those names
at data:`0x3532c..0x35494`, but this lane has not traced the loader's write
binding into all seven runtime offsets: use the offsets when implementing
a further witness.

Distance is squared cell displacement, not shortest path length. The block
computes `min(100, trunc(100*(dx*dx+dy*dy)/450))`, then subtracts from 100.
The witness verifies the multiply-high division sequence over the complete
bounded squared-displacement domain of two 128-cell coordinates; the divisor
is derived from the instructions, not fitted from gameplay observations.
An optional signed cell-record value at `+8` divides this match with an unsigned
division (`0xe9398`) when nonzero; its valid sign and initialization are
unresolved. For squared distance
≤8, queue match is `100 - floor(queue_count*100/(4*max(object_field_60,1)))`;
otherwise the queue match and its weight become zero.

Excitement match is `2*(50 - clamp(abs(preferred - excitement),0,50))`.
Its weight becomes zero for the flagged category checked at `0xe9478`.
Hunger and thirst each quantize the unsigned byte-converted guest need and
the attraction effect by division by ten. The pair indexes an **11×11 byte
lookup table** at data:`0x41222` through TOC slot `0x1620`:
`index = 11*floor(effect/10) + floor(need/10)`.
The effect fields are `+324` for thirst and `+328` for hunger on the object
metadata returned by `0xdc320`. For example, need 50/effect 50 gives match 17;
need 100/effect 100 gives 100. This disproves treating the need term as a
linear quarter of the guest need.

Toilet/illness matches use a separate 21-entry table at data:`0x4129b`,
TOC slot `0x1624`, indexed by integer `(need+4)/5` only when object flag bit
0 is set. Otherwise the match is zero. Values are nonlinear (need 50 gives
2, need 100 gives 100). The base score at `0xe96c0..0xe9714` is unsigned
integer `sum(weight*match)/sum(active_weights)`. This lane does not establish
protection against zero denominator or out-of-range metadata.

Both numerator products/additions and denominator additions retain the low
32 bits. The queue match is not clamped after `100 - queuePercent`: a near
queue count 9 and raw object `+60` value 1 produces match −125 as register
bits `4294967171`. With distance match 100 and only active weights distance 1
and queue 2, the wrapped numerator is `4294967146`; `DIVWU` by 3 yields
`1431655715`. Treating that as a negative signed score or clamping the queue
match to zero changes the original arithmetic. Later history divisions use
signed interpretation. Candidate selection and plausible metadata domains
must therefore be reviewed before treating a normalized quotient as a usable
gameplay score.

Subsequent multipliers handle new rides, indoor rides in rain, gold-ticket
rides, and expensive rides. Two history arrays hold four object IDs each:
matching positions penalize the score by dividing by 5, 4, 3, or 2, with
integer truncation. The arrays are guest `+480..+486` and `+488..+494`.
These differ from OpenTPW's single `LastAttractionId` penalty and omitted
`DecisionVariable` behavior.

Selection caller code:`0xe8ff4..0xe9164` visits a linked object collection;
after the score call at `0xe90a0`, scores below 10 are ignored. Equal-best
score acceptance is gated by the global counter parity, and the candidate
must pass destination/path checks. No injected random `0..9` bonus appears
in the bounded score routine. Minor decisions call the same scoring routine
at `0xe9d2c`. Unresolved: full candidate prefilters and visibility radius,
major/minor decision cadence, runtime weight binding, flag classification,
history expiration, and price eligibility. These prevent a drop-in scoring
replacement even though the current formula is demonstrably different.

## Queue behavior and states

The relocated 22-entry jump table is data:`0x41314` through TOC slot `0x3450`.
State `+544` indexes it at `0xef268..0xef284`; setter `0xef700` stores the
state before a separate entry-action dispatch. Thus the 0..21 state domain
has binary evidence independent of the language strings. Important routes:

| State | Handler call from dispatcher | Meaning grounded by call/string |
| --- | --- | --- |
| 10 | `0xef4a0 -> 0xeccb0` | heading to ride; moved-back-of-queue handling |
| 11 | `0xef4ac -> 0xed244` | standing queue, admission/boredom checks |
| 12 | `0xef4b8 -> 0xe6454` | moving to a queue position; returns to 11 |
| 13 | `0xef4f0 -> 0xed7e0` | admission/price checks; waits for script |
| 14 | `0xef4f8` branch | readiness check, then changes to 16 |
| 15 | `0xef580 -> 0xedae4` | leaving ride and restoring prior destination |
| 16 | `0xef608` branch | separately handled usage state |

This exposes omitted state 12 and makes state 14's ride readiness behavior
more specific than the current offer/board callback model. It does not prove
every callback or RSE visitor opcode; those require the script/ride lane.

Queue destination path is `0xee604 -> 0xdd144` (position lookup) then
`0xddc2c -> 0xddcc4` (queue coordinates) and `0xe6864` (destination).
At `0xddd2c..0xddd50`, the coordinate helper subtracts **four positions per
queue cell**, following a prior-cell link until fewer than four positions
remain or the terminator is reached. Queue cells therefore have a concrete
role; the current three-abreast entrance-cell offsets are an approximation.
The remaining intra-cell offsets depend on connection orientation and an RNG
call; exact positions and RNG equivalence were not established.

Queue acceptance code:`0xdcb74..0xdcd34` reads upgrade metadata `+436` as
`QueueWaitTimeConstant`, supported by its diagnostic at `0xdcc78`. It
combines that constant with object fields `+84`, `+89`, `+88`, metadata
`+424`, and 4.0 before converting/comparing against queue count.
The computed expression includes capacity/throughput and operating duration;
it is not a universal `queue_limit = 4*capacity`. Full metadata bindings and
the conversion helper's rounding are still required. A separate flagged case
sets limit 100.

Standing-in-queue handler has a timeout compare at `0xed670..0xed678` against
guest timestamp `+508 + 100`, and then leaves the queue with a boredom
diagnostic. It also tests toilet >80 and route/admission consistency.
Units of 100 remain unresolved. Queue edits, dismissed guests, moved queue
ends, and destination failure have dedicated paths rather than being reduced
to OpenTPW's direct queue list and nearest-path fallback.

## Phase 2: environmental deltas and alternate need writes

High confidence initialization: map/global binder code:`0x104e4c..0x104e84`
stores the game object in data:`0x11ef04` and `game+728` in data:`0x11ef00`.
This explains the 728-byte difference between map-relative and game-relative
layout addresses. The game constructor `0x104500..0x10465c` constructs
16,384 ten-byte cell records at game `+0x1b13dc` using the transition vector
data:`0x6f50`. That vector relocates to code:`0xefddc`, TOC `0x8000`.
The cell constructor calls CFM import glue `0x1c483c` at `0xefdf8`; its
relocated import is `memset`, with fill zero and length ten. Cell records
therefore start with zero environmental deltas, not a baked-in growth rate.
The cell records can also be read during map load via `0xd7014`; their
post-load values still require separate save-layout qualification.

Region add/remove wrappers `0xd7340/0xd7370` call common writer `0xd73a0`.
They supply an add/remove flag, effect index, and encoded source cell. The
writer uses twelve-byte effect entries at map `+0x1d9104 + 12*index`:
five signed 16-bit coefficients at entry offsets 0,2,4,6,8, and an unsigned
radius byte at offset 10. It clips the **square** radius footprint against
grid 0..127, not a Euclidean circle. For each covered cell and component,
the contribution is
`trunc_toward_zero(sign*coefficient/(abs(dx)+abs(dy)+1))`, added to the signed
16-bit cell component. Add uses sign +1, remove −1. There is no saturation
in this writer; the stored halfword can wrap. The guest update consumes only
the first three components; the score distance modifier consumes component 4
(byte offset 8), and security code `0xf7b2c` consumes component 3.

This proves an environmental source for the otherwise unexplained signed
cell values. It does not erase the fixed growth branch's phase restriction.
Integer attenuation also matters: a coefficient −1 contributes only at the
source cell, because −1/2 truncates to zero. The witness tests signed
attenuation and exact add/remove cancellation within the no-overflow domain.

Runtime call indices and PC baseline labels agree at useful anchors:
entertainer setup `0xd2894` adds index 0; guard setup `0xd45f0` adds index 3;
litter dispatcher `0xd8668/0xd8688` adds indices 2/5; removal counterparts
include `0xd89ec/0xd8a10`. Toilet cleanliness transition calls remove then
add at `0xe2764/0xe278c`, and object teardown chooses index 6 or 1 at
`0xdc134/0xdc160`. Firework stop uses index 7 at `0xe07e8`.
The inspected Windows baseline `Data/levels/Standard.sam` labels indices
0..7 as entertainer, clean toilet, vomit, guard, camera, stinkbomb, dirty
toilet, and fireworks. Its SHA-256 is
`3d39433641df70dba73e6ce0f4ae576a5a334ed93e61adc1cdd570b751a8bf4b`.
This is source-corpus comparison, **not** proof of Mac-loaded balance values.
The exact Mac SAM loader-to-effect-record binding remains unresolved.

Alternate need writes are concrete visit effects. Ride outcome code:
`0xea318..0xea5a4` uses preferred/excitement differences 5/15/40 to select
the positive happiness amounts from balance offsets 36/40/44, with clamps
0..100. Differences above 40 skip those bonuses; this bounded function has
no corresponding −SmallHappinessChange branch. Its illness increment is
`trunc(excitement/divisor) * trunc((100-byte(trunc(hunger)))/20)`, then clamped
0..100; the divisor is balance offset 48. For bounded valid needs, excitement
70/divisor 10 produces illness +35 at hunger 0, +7 at hunger 80, and +0
at hunger 81. It is not simply excitement/divisor.

Shop outcome code:`0xeaaf8..0xeb49c` loads object metadata at `0xeabb8`.
At `0xeabc4..0xeaca4`, it subtracts signed metadata `+324` from thirst and
`+328` from hunger, clamping each to 0..100. It adds metadata `+332` to
illness (`0xeace0..0xead44`). Additional configured effects can increase
toilet/thirst; a toilet-flag path at `0xeb2b0` clears toilet (`0xeb2e0`) and
can clear illness after a >90 check (`0xeb32c`). Source metadata fields and
category flags require full loader binding before replacing current visits.
A bounded search of direct float writes in the guest method range found
constructor, serialization, ride/shop outcomes, entry logic, and the needs
routine; it did not identify another unconditional periodic thirst/hunger
growth routine. Indirect writes, callbacks, and other ranges are not excluded.

## Phase 2: language states, sprites, and admission

The Windows English `KIDSTATES.str` decodes to 22 entries; its SHA-256 is
`31004291979ba7d255633af3a87789d9dfbe9cd317b4742a17ea3beda2f1b9be`.
`THOUGHTS.str` has 18 entries, SHA-256
`92c6a9c38cbc7bc9d3f02ac2a8ec020924650b06d6a23bc9b3c84a2ee9c1723f`.
Decoding used the repository BFST/BFMU format and English `MBToUni.dat`,
SHA-256 `69f23492ef61a27ed79dd4df67978536720d403f7e2733acb6b43e6f4f78c587`.
These are PC corpus identities, not extracted Mac language resources.
The 22-entry Mac update and entry-action dispatch tables are respectively
data:`0x41314` and `0x4136c`. Along with the earlier table, the corpus supplies
omitted distinctions: 3 waits for park opening; 8 has no visible label;
9 discards litter; 12 is the second queue state; 17 is ejection; and 20 is a
second homeward road-crossing state. State 17 must not be treated as ordinary
ride usage merely because the needs code groups 16/17 in one later check.

Sprite binding remains partially unresolved. State-entry code `0xef700` calls
`0xd2290` with address `guest+12`; that two-instruction setter stores the
requested behavior at its argument `+4`, i.e. guest `+16`. Entry states 0,2,5,
9,10 supply mode 1; states 1,3,4,11 supply mode 3 at the shown branch targets.
These are behavior values, **not demonstrated ESP slot numbers**. Sprite
creation `0xe79dc -> 0xd2308` uses guest fields `+36/+32`, independently of
PeepTypes index `+496`; the latter is used for balance selection. The current
`type mod 8` kid-bank choice is consequently not established by this path.
Position updater `0xe5ee0` interpolates fixed-point navigation positions and
calls `0xb7ce8`, which writes sprite XYZ at sprite offsets 136/140/144.
Direction/frame selection, behavior-to-ESP binding, and the five-direction
mirroring rule still need the sprite class/update path, not just this setter.

Admission has a precise host/script handshake. Object method
`0xe03d0..0xe05c8` is named by its own admission diagnostic. It validates
availability and the pending visitor (`object+104`), clears `+104`, retrieves
the script through `0xb5758`, reads **script array index 0** through `0xb5be0`
at `0xe0530`, and writes the visitor there through `0xb57d4` at `0xe0550`
only if the existing value is zero. Those helpers directly index script
`+28` with bounds `0 <= index < script+140`; they do not resolve a name.
Some PC script headers label index 0 as VAR_LETMEON, but per-script layout
must be established before making that label universal.

Guest state 13 calls this admission method and enters state 14. State 14's
handler calls `0xe05c8` at `0xef528`. The latter reads script index 0 and
returns whether it differs from the halfword `object+56`. Only the nonzero
result advances the dispatch through queue removal and state 16. The
complete binding of `object+56` and script controller ownership is still a
ride-lane dependency. This anchors the callback protocol to actual fields
without assuming every HUSH/WALKON path has identical timing.

Guest statistics also bind advisor consumers to actual needs. The hungry and
thirsty counters at `0xc3964/0xc3a60` traverse live thing slots, restrict category
byte `+2` to 1, require cell predicate `0xe6c6c`, and count
`byte(trunc(guest need)) > byte(threshold)`, strictly greater. The hunger/thirst
loads are `0xc3a00/0xc3afc`. Predicate `0xe6c6c` queries five current-cell
properties rather than a GuestState enum; its complete category meaning
remains unresolved. The waiting counter `0xc3758` traverses both ticket-booth
cell lists, counts category-1 things in state 3, then stores/returns the
current value through `0xc9020/0xc9018`. These helpers are a tracked-value
setter and immediate getter, not evidence of historical smoothing.

## Phase 3: isolated implementation and synthetic regressions

`tools/ppc-analysis/lanes/guests/rules/GuestOriginalRules.cs` implements bounded
arithmetic as an evidence helper outside the production project. Existing
guest/attraction types cannot provide the original history arrays, numeric
shop effects, flags, or register-weight bindings. It is deliberately not
called by `GuestSimulation` and is not a complete attraction scoring pipeline.
No recovered lookup table or original asset is embedded; interpreted tables
are caller-supplied spans and regression tables are synthetic.

The helper covers requested four-position queue groups, distance divisor 450,
near/far queue-weight gates, byte excitement matches, nonlinear needs table
addressing, seven-term normalized base scores, signed history divisions,
shop effect subtraction, and hunger-band ride illness. Tests preserve the
overfull-queue unsigned quotient and low-32-bit overflow, including the later
signed reinterpretation. Unsupported zero/wrapped-zero denominators, invalid
layouts, nonfinite needs, and inputs outside the supported domain fail closed;
these rejections do not claim to reproduce original invalid-input behavior.
No need growth cadence or original score multiplier is fabricated.

Validation: dependency-free .NET 8 console regressions pass **18/18**, Release
build reports zero warnings/errors, and the original-input Python witness
suite remains **11/11, zero skips**. Run instructions and per-method binary
anchors are in `rules/README.md`. Parent review must resolve metadata domains,
raw signed/unsigned edge behavior, lookup acquisition, full score stages,
caller units, and production test shape before integration or tag resolution.

## Untagged assumptions and integration handoffs

The explicit approximation table in `docs/GUESTS.md` does not cover all
assumptions in `GuestSimulation.cs`/`Guest.cs`: booth delay 1 second, ride-exit
delay 1 second, home-bus delay 3 seconds, spawn spacing 0.4 seconds, uniformly
selected PeepTypes, inclusive symmetric cash/exit variation, ±0.25-cell offsets,
random initial decision phase, initial needs zero/energy 100 (the binary
constructor disproves the universal zero-needs assumption), 150 need cap,
initially admitted fallback spawning when arrival lanes are absent, all guests
processed in list order, deterministic attraction-ID sorting, SplitMix64 and
modulo-based RNG, hidden riders for every usage state, and no path-link
U-turn when another neighbour exists. These are design decisions without
binary equivalence evidence, even where the loaded SAM values are original.
No claim here upgrades them.

Parent handoffs: annotate these assumptions in the fidelity register; send
counter `*(data:0x11ef04)+0x1da70c` and the phase-zero restriction to the clock
lane; trace map cell record
initialization at `map+0x1b1104` and metadata loader writes; coordinate RSE
admission state 13→14→16 with ride/script evidence. Keep current gameplay
until those dependencies and meaningful boundary tests are complete.
All recovered mechanics are Feral Mac static behavior. PC Patch 2 parity,
original runtime behavior, and cross-platform cadence remain untested.
