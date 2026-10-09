# Ride/RSE evidence from the Feral Mac executable

2026-10-09. Static inspection only: no original game execution, and no native
instructions, executable sections, or original assets are stored in Git. This
lane changes evidence/tools only. Its findings qualify the identified **Mac**
implementation; equivalence to the Windows baseline or official Patch 2 runtime
remains unproved. PC scripts are independently parsed below, including the
verified Patch 2 installation described in [PATCH-2](../PATCH-2.md).

The main executable is `game-assets/mac-feral/bin/SimThemePark.data`, SHA-256
`04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5`.
All native addresses below are offsets in its **code section 0**, unless stated
as data section 1. The application has no exported game functions. Function
labels below describe their observed role, not recovered exported names.
Transition-vector offsets must not be mistaken for code addresses.

**Strongest findings:** the native interpreter changes branch state on COPY,
returns zero for DIV/MOD by zero, shares ordinary animation channel 0 with
explicit channel 0, returns animation IDs/state from GETANIM_CH, and applies
TRIGANIMSPEED's fourth operand. These contradict assumptions in
[RSE-VM](../RSE-VM.md) and [OBJECTS](../OBJECTS.md). No hook becomes a complete
game effect merely because its native switch case is located.

## Reproduce and verification

```sh
python3 tools/ppc-analysis/lanes/rides/evidence.py /Users/sander/server/game-assets/mac-feral/bin/SimThemePark.data
OPENTPW_MAC_APP=/Users/sander/server/game-assets/mac-feral/bin/SimThemePark.data python3 -m unittest discover -s tools/ppc-analysis/lanes/rides -p 'test_*.py' -v
/Users/sander/.local/share/opentpw-dotnet/dotnet run --project tools/ppc-analysis/lanes/rides/CorpusWitness.csproj -- /Users/sander/server/game-assets/theme-park-world /tmp/rides-baseline.json
/Users/sander/.local/share/opentpw-dotnet/dotnet run --no-build --project tools/ppc-analysis/lanes/rides/CorpusWitness.csproj -- /Users/sander/server/game-assets/theme-park-world-patch2 /tmp/rides-patch2.json
```

The Python witness refuses any different executable SHA before parsing. It
uses the existing bounded `pef.py` and `timer_evidence.py` instruction-field and
linked-branch readers. It bypasses `analyze.py`'s heuristic traceback discovery.
Native instructions were locally inspected with LLVM outside Git; the
checked-in witness reports only selected interpreted facts, addresses, and
identities. Debug-name relocations independently bind the selected opcode
numbers to the native names rather than relying on the OpenTPW enum alone.

Checked-in metadata: `tools/ppc-analysis/lanes/rides/mac-witness.json`,
`pc-baseline-witness.json`, and `pc-patch2-witness.json`. Fifteen Python tests pass,
including the identified original-file witness and synthetic failures for
wrong identity, absent/import relocations, invalid switch targets, wrong
instruction kinds, signed/absolute linked branches, native controller contracts,
and pure math. Both C# corpus runs
reuse the repository's WAD/RSE readers and add no packages: baseline **308
scripts / 11,915 instructions**; verified Patch 2 **308 / 11,913**. The two
removed Jelly FLUSHANIM instructions explain this difference. Existing reader
nullable/member warnings and the existing Zio NU1901 warning persist.

Confidence convention: **high** = identity-pinned instruction/relocation path
and interpreted operations; **medium** = a descriptive role inferred from
callers, data structures, or corpus control flow; **unresolved** = the required
path has not been recovered. These are static confidence levels, not native
runtime or cross-platform qualification.

## Interpreter behavior

The instruction dispatcher starts at `0xaf534`. Its opcode guard accepts
0..105 and its relocated switch table is data `0x3f2a4`, reached through TOC
slot data `0x2f58` with TOC base `0x8000`. Native debug-name records start at
data `0x3ef20`, eight bytes per opcode. The witness validates selected names
and cases against both tables. The dispatcher is an **individual instruction
step**, not a whole scheduled script slice. Scheduling, milliseconds, clock
scaling, and clip frame rate belong to the CLOCK lane.

| Behavior | Native evidence | Confidence / consequence |
|---|---|---|
| Literal operands sign-extend 16 bits; variable values are int32 | Operand resolver `0xae6ec`: variable-table pointer at script+28; signed literal path `0xae710` | High; confirms −1 for raw literal 65535 |
| Branches inspect a shared signed accumulator | Script+72 is compared with zero at `0xb0900`, `0xb0944`, `0xb0988`, `0xb09cc` | High; zero, negative, strictly positive, nonzero cases agree with corpus control flow |
| COPY into a variable changes branch state | Opcode 3 case `0xaf5f0`, result copied back to script+72 at `0xaf638` | High; current VM's COPY-preserves-flags claim is contradicted |
| SUB is destination first | Opcode 5 `0xaf650` resolves the second/third operands, subtracts at `0xaf69c`, stores accumulator, optionally destination | High; agrees with corpus-derived operand order |
| DIV/MOD by zero produce zero | DIV case `0xb0c58`: zero arm `0xb0ca8`; MOD `0xb0cd8`: zero arm `0xb0d30`; both store zero to script+72 and then optional destination | High; current fault-on-zero policy is contradicted |
| CMP subtracts variable first operand and resolved second operand | Opcode 39 `0xb0da0`, subtraction `0xb0de8`, accumulator store `0xb0dec` | High for variable first operands; do not generalize to arbitrary literal first operands |
| RAND includes max | Case `0xb07e4`; sign-extends max, takes remainder modulo max+1, stores accumulator at `0xb082c` | High for nonnegative corpus bounds; PRNG identity and stream sharing are unresolved |
| Literal destinations do not uniformly mean “do arithmetic, update flags, discard write” | COPY, ADD, TEST and CMP guard their variable operand path; e.g. ADD reaches its arithmetic only after variable-kind test at `0xb0bac..b0bc4` | High; audit each opcode rather than applying one generic destination rule |
| JSR/RETURN use a descending/ascending index on a stack array | Helpers `0xaf430` / `0xaf4ac`, stack pointer script+64, declared bound script+84 | High for LIFO behavior; native invalid-stack handling sets script PC to a negative sentinel rather than the VM fault message |

Host fault policy and resource limits can intentionally be stricter, but must
be described as implementation policy. Existing synthetic tests proving the
VM's current policy do not establish the original behavior. The native
interpreter contains all 106 cases, including opcodes absent from the PC
corpus; dispatch existence alone does not validate operands or side effects.

## Animation numbers, channels, and queries

Loader `0x58a3c` reads a 12-record table at data `0x3d300` through relocated TOC
slot `0x2c04`. Each record holds the numeric category and its suffix character:

| Animation ID | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Suffix, case folded | c | d | i | l | s | m | e | u | w | b | r | o |

At `0x58e70..58e90` the loader constructs the numbered member with category
suffix and **variant+1**, using the format located at code `0x1c97dc`.
For a category with no numbered first member it retries the unnumbered member
(`0x59048..59060`, format code `0x1c97e9`). Thus the ID→suffix and numbered
variant rule underlying RIDES-008 are **high-confidence Mac evidence**,
including two categories (1/d, 8/w) omitted by `ObjectAnimations.GetLetter`.
Names for these two categories remain unknown. This does not prove full lookup
ordering across archives or texture roots, nor Windows lookup equivalence.

The shared animation trigger is `0xa6cc0`; it indexes channel records by
`channel * 56` at `0xa6cec`, from model+16. Plain TRIGANIM (`0xafb10`),
WAITANIM (`0xafc68`), and LOOPANIM (`0xafd7c`) pass channel **0**. TRIGANIM_CH
passes its fourth operand in that same channel argument (`0xb01c4`). The
ordinary channel is not a separate namespace. This is a direct contradiction
of RIDES-005. Totem's PC script uses explicit channels 1, 2, 3; other corpus
scripts can use explicit channel 0.

GETANIM_CH case `0xb05b8` passes an accumulator output pointer and the channel
to query `0xa7dcc` at `0xb0608`. The query writes the record's current animation
ID (+4), can separately write the variant (+8), and returns the state flags
(+0). If returned flags contain bit 4, the dispatcher overwrites the
accumulator with **−1** (`0xb061c..620`). Consequently GETANIM_CH is **current
animation ID, or −1 under that state flag**, not “1 while playing, 0 after”.
The record's no-animation sentinel is 12, visible in trigger/flush/query
guards. The exact transitions producing bit 4 still need a full state-machine
test before claiming what every queried sign means. RIDES-006's boolean is
contradicted, but a replacement boolean would remain wrong.

TRIGANIMSPEED (`0xaffc4`) stores the signed fourth operand in script+228 and
passes `base_speed * operand / 1000` to `0xa6cc0` (`0xb003c..b0070`). It also
divides its wait deadline's duration by that speed (`0xb00dc..e0`). Ignoring the
operand (RIDES-007) is contradicted. Speed 4000 is four times base speed, not
an absolute 4000 ms clip duration. Zero/negative speeds require recovery of
the original valid-input contract; don't invent clamping.

Repeated ordinary LOOPANIM compares animation+variant with script+168 and
returns immediately if equal (`0xafd2c..afd40`). Thus preserving an unchanged
ordinary loop has a native basis (RIDES-004), but this cached token is
script-local and does not establish every externally flushed or explicit
channel case. The animator also supports deferred animations (record+36/40)
under conditions inspected at `0xa6d24..a6dbc`; latest-start-wins mixing and
holding final poses are still unresolved.

The CLOCK lane independently owns timing. Cross-inspection here confirms
`0xa7000`: channel flag bit 0x40 chooses the unscaled timestamp, otherwise the
main scaled timestamp; frame progress is `(now-start)*30/1000*channel_speed`.
This supports RIDES-001's **30 clip frames/s for this Mac path**, independently
of the main simulation step. It does not resolve vertex animation decoding
or blending, which belong to FORMATS.

## Controller boundaries, passengers, capacity, and phases

These are three distinct native bridges, not synonyms for one generic ride:

| Opcode | Interpreter call | Bridge entry | Native command range | Stored controller reference |
|---|---|---|---|---|
| TOUR (53) | `0xb0e7c` | `0xb5eb0` | 0..18, table data `0x3f458` | script+156 |
| BUMP (54) | `0xb0e88` | `0xb6684` | 0..17, table data `0x3f4a4` | host object+36, looked up through script+172 |
| COAST (55) | `0xb0e94` | `0xb6df4` | 0..8, table data `0x3f4ec` | script+224 |

Raw command IDs in all 308 baseline scripts: TOUR 76 uses across IDs
1,3,4,5,8,9,10,11,12,14,15,16,17; BUMP 199 across
1,2,3,4,5,6,7,8,9,10,11,12,13,14,16,17; COAST 144 across all IDs 1..8.
The verified Patch 2 command histograms are identical. `ScriptDefs.Bumper`
contains disparate values such as 47/54/115 and cannot be used directly as
the raw RSE command enum. `ScriptDefs.Coaster` matches IDs 1..6 and 8; its ID 7
is a consumed-operand no-op in this Mac bridge. The contracts below verify
every label by its native index.

Selected command behavior, confidence high for direction/arithmetic and
medium for the descriptive controller role:

| Native command | Evidence-backed operation |
|---|---|
| TOUR 1 | Resolves parameter, locates an engine node, creates a tour object through `0x63b60`, stores script+156. Missing node takes a native diagnostic path. |
| TOUR 2 | Calls `0x66550` with script+156, consuming but ignoring the parameter slot. |
| TOUR 3 / 4 | 3 accepts a **variable** visitor input and calls `0x66a64`, sets accumulator. 4 calls `0x66784`, writes output visitor to a variable and accumulator. Literal destination/input is not equivalent. |
| TOUR 8 | Resolves parameter, multiplies by **1000** at `0xb6288`, calls `0x66bcc`. Unit/meaning must be reconciled with CLOCK and duration schema. |
| TOUR 10 / 11 / 15 | Calls `0x672e0` / `0x67360` / `0x67314`, sets accumulator; consumes but ignores parameter slot. |
| TOUR 16 | Calls `0x6745c`, writes output to variable and accumulator. |
| BUMP 4 | Calls vehicle creation/launch routine `0x242d8`. Consumes but ignores parameter slot. |
| BUMP 5 | Copies host object+40 directly to the branch accumulator; consumes but ignores parameter. |
| BUMP 7 / 6 / 3 | Calls `0x1dc20` / `0x1de20` / `0x1e54c` respectively. Native phase changes differ; don't collapse start, close, and stop actions. |
| BUMP 11 | Calls `0x1e9bc`, writes result to a variable when present and to accumulator. |
| BUMP 12 | Calls `0x23fe8`, sets accumulator; ignores parameter slot. |
| BUMP 13 / 14 | 13 sends `parameter * 30`; 14 sends `-parameter` to the same duration setter `0x1e6f4`, which stores controller+4. Both set accumulator to original parameter. Different sign encodings must survive. |
| BUMP 16 | Calls `0x25638(controller, 1)`, sets accumulator. |
| COAST 8 | Creates/looks up controller via `0x3dafc`, stores script+224, applies script speed through `0x3de68`. Parameter is consumed but ignored. |
| COAST 1 | Resolves visitor input, calls `0x3dbc0`. Passenger queues and available slots live in the coaster controller. |
| COAST 2 / 3 | Calls `0x3dd14` / `0x3dd5c`, writes accumulator and optional variable output. 2 queries remaining queue admission room; 3 extracts a departing visitor. |
| COAST 4 | Resolves input and calls `0x3ddfc`, which translates states 0,1,2 into controller flags 16,32,64. |
| COAST 5 | Resolves input and calls close/open controller routine `0x3dec8`; conditions depend on existing flags. |
| COAST 6 | Resolves capacity input and calls `0x3df24`, which clamps against global/config/controller limits before updating. |
| COAST 7 | Consumes but ignores parameter; no controller call or branch-accumulator store. |

The vehicle-launch routine `0x242d8` checks controller active vehicle count
(+92) against configured limit (+100), absolute cap **64**, and phase (+80)
before admitting another vehicle (`0x24364..24384`). It increments active count
on successful allocation. Start/stop routine `0x1e54c` changes phase 1→2 and
iterates vehicle records, with type-dependent extra animation actions.
Vehicle retirement `0x25738` decrements active count; when zero, selected ride
types set phase back to 1 (`0x25a30..34`). These observations establish an
independent controller loading/running lifecycle. Interpreting the phase names
relies partly on the native diagnostic at that reset point, so the phase
labels are medium confidence while offsets, comparisons, and stores are high.

The PC corpus further disproves a universal “completed cycle means
VAR_RUNNING 1→0” contract: Totem writes 1 at word 66 and 0 at 185, while Hallow
bumper writes 0 at 17 and 1 at 135; Space Moonshot has **no literal COPY to
VAR_RUNNING** in its 55-instruction controller-driven script. Water rides can
write only zero and delegate running state to BUMP queries. These are static
write inventories, not proof that no other instruction can write that variable.
RIDES-023 cannot safely account for all controller rides using only the
Totem-shaped edge. Income/visit accounting still requires the actual host
admission/departure and controller completion paths.

Totem SHA is pinned above and in [RSE-SCRIPTS](../RSE-SCRIPTS.md). Hallow bumper
SHA is `55f4fbb9820e9a4cf401bf1dca64677b1371ca71c048550197f5033667eb1163`.
Space Moonshot script SHA is
`8cc600039af76e352da92438bd62f5ba2367cb164d8686ec7f3a5554e3403e82`.
The metadata also pins the selected kart, water ride, and Bouncy script
identities, header capacities, and explicit-channel sets across both PC sets.
Space Bouncy's header BounceSize 10→12 changes independently of SAM maximum
capacity 12→10 in Patch 2; never substitute header bounce slots for ride
capacity. Patch 2 passenger capacity and teleport changes are asset evidence,
not Mac runtime proof.

## Original entrance/exit geometry

Routine `0xde1d8` selects entry or exit packed cell references from host+50/+52
and fractional stand/appear coordinates from definition+212/+216 or +220/+224.
It checks coordinates against [0,1], then reads rotation degrees host+16.
For selected coordinate `(x,y)` it applies:

| Rotation | Fractional coordinate |
|---|---|
| 0 | `(x,y)` |
| 90 | `(y,1-x)` |
| 180 | `(1-x,1-y)` |
| 270 | `(1-y,x)` |

The result is multiplied by **255**, truncated to integers, and stored in
bytes (`0xde500..de528`). The packed cell reference is decoded separately by
subtracting one, extracting seven X bits and the subsequent Y bits. This is
high-confidence evidence for the original entry/exit fractional coordinate
rotation, including 180 degrees, and for 255 rather than 256 quantization.
It does **not** prove the meaning of Shape symbols, rotation of an arbitrary
non-square footprint, side entrance precedence, or nearest-path substitution.
Those must remain separate from this narrower access-position finding.
The 0.1/0.9 position changes and teleport flag in the verified Patch 2
Moonshot/Shocker assets are consistent inputs to this path, but the teleport
controller behavior itself is not recovered here.

## RIDES approximation disposition and next implementations

| Register IDs | Disposition and exact remaining dependency |
|---|---|
| 001 | Mac clip rate 30 frames/s supported with CLOCK; Windows cadence/clock equivalence remains open. |
| 002,003 | Unresolved channel mixing and post-completion pose: finish the `0xa7190` / node-application paths and deferred state transitions before changing rendering. |
| 004 | Ordinary same-token LOOPANIM preservation supported; cover flush/child/explicit-channel interactions separately. |
| 005 | Contradicted: ordinary channel is 0 in Mac. Implement shared channel numbering, then run Totem and explicit-channel-0 synthetic sequences. |
| 006 | Contradicted boolean: implement ID/state query, then test current ID 0/5/12, flag-4 −1, empty/missing model behavior. |
| 007 | Contradicted ignored speed: implement signed per-mille speed and deadline relationship; confirm zero/negative input contract. |
| 008 | Numeric suffix table and v+1 supported for Mac; add categories 1/d and 8/w without inventing names; test numbered-first fallback. |
| 009,010 | Unresolved definition layering/difficulty and name-index tables: trace definition loader/name mapping; exports alone are insufficient. |
| 011,012 | Unresolved Shape symbol meanings and side-access precedence: trace Shape parser and packed-cell placement; access-position helper does not decide these. |
| 013 | Fractional access-position rotation supported at 0/90/180/270; non-square footprint transform remains unresolved. |
| 014 | Unresolved footprint base height/terrain conformance: requires HMP/placement path, not model bounds. |
| 015 | Unresolved imported/new ride open state: requires host constructor and save state restoration. |
| 016 | Raw uniform duration is unsafe for controller effects: BUMP13/14 and TOUR8 perform different conversions. Trace Info.DurationUnit/upgrades→host→script initialization before selecting units. |
| 017,018 | Unresolved menu/build eligibility and collision/slope/path/land checks: inspect build menu and placement validation. |
| 019 | Unresolved fixed-item initialization per level: trace scenario construction rather than generalizing one save. |
| 020,021 | Prototype blocking and build cursor centering are not original placement evidence; trace original anchor/rotation construction. |
| 022 | Unresolved archive/gtexture/sharetex order: model suffix lookup is not texture lookup. |
| 023 | Universal VAR_RUNNING cycle edge unsupported for controllers; implement explicit controller completion/accounting only after host-event recovery. |
| 024,025 | Unresolved bonus merge/collision and name fallback: trace catalog insertion and language lookup. |
| 026,027 | Presentation/prototype engine scale and radius remain implementation conventions; no original gameplay conclusion. |
| 028 | Unresolved queue joining/nearest walkable path: original access-cell geometry alone does not prove path substitution. |
| 029 | Developer-tool uncharged Totem handling is an extension policy, not original evidence. |

Recommended bounded implementation order: (1) correct VM accumulator/zero
arithmetic with synthetic branch regressions and re-pin corpus traces; (2)
correct IDs, channels and speed with renderer-independent state tests; (3)
create distinct TOUR/BUMP/COAST controller contracts using raw command IDs and
per-command input/output directions; (4) connect real vehicle/passenger
lifecycle and accounting; (5) recover placement/footprint/save-state gates.
No gameplay files were changed by this lane.

The 51 hooked opcodes remain a subsystem backlog: object creation/fade/kill,
EVENT mapping and sound banks, animation state/blending, visitors/heads,
limbo/bounce/walk/float state, three track controllers, lights, screams, repair
effects, calendar, reverb/music, and particles. Visitor hooks implemented in
the current runtime must still be qualified against native paths. Locating
native entry points for some of these does not implement the remaining
effects or justify claiming original-fidelity completion.

## Controller contracts and math witnesses

Additional reproductions, using the same SHA-qualified executable:

```sh
python3 tools/ppc-analysis/lanes/rides/contracts.py /Users/sander/server/game-assets/mac-feral/bin/SimThemePark.data
python3 tools/ppc-analysis/lanes/rides/controller_native.py /Users/sander/server/game-assets/mac-feral/bin/SimThemePark.data
```

`controller-contracts.json` records all **39 non-default native commands**.
The verifier obtains each case directly from its switch index, verifies every
direct non-diagnostic call, and verifies whether the case explicitly stores
the script branch accumulator. `controller-native.json` additionally pins
the passenger/layout/arithmetic fields below. `controller_math.py` contains
seven pure, renderer-independent witnesses; these are not gameplay systems.

Every bridge consumes a raw command word and a parameter slot. First operands
are literal command IDs in every PC corpus call. A generic resolved command
variable is not established by native evidence. Parameter contracts distinguish
required-variable input/output, optional-variable output, resolved input
(signed16 literal or int32 variable), and consumed-but-ignored operands.
For required-variable cases, a literal operand skips the operation and does
not obtain the result. Optional-output queries still set the accumulator with
a literal destination. The JSON records the individual contracts.

**Preserve accumulator:** COAST 1,4,5,6,7,8; BUMP 3,6,7,8,9,10,17;
TOUR 1,2,5,8,9,12,14,17,18. Other valid commands explicitly store their query,
admission, original duration input, or host-field result. Thus the current
VM's unconditional `SetFlags(Effect(...))` for all controller calls changes
native branch behavior. The contract describes explicit bridge stores under
valid host/controller preconditions; it does not certify every indirect
callback or invalid controller handle.

COAST 0, BUMP 0/15, and TOUR 0/6/7/13 select their default diagnostic cases.
Larger commands take a diagnostic path. Invalid script reads set the native
negative PC sentinel; invalid object handles and initialization gates require
the host preconditions and are not uniformly safe native operations. A future
OpenTPW controller may reject these states safely, but should document that
as its own policy. The native diagnostic helper has not been qualified for
every fatal/nonfatal setting.

Representative **PC code-word** callsites from the new corpus metadata:

| Family / archive member | Command at word | Parameter / following operation |
|---|---|---|
| COAST / Fantasy `b_drip.wad/B_DRIP.RSE` | 1 at 53 | Variable index0 `VAR_LETMEON`; followed by COPY |
| same | 2 at 44 | Literal0; followed by BRANCH_Z; query works without a writable destination |
| same | 3 at 35 | Variable index1 `VAR_LETMEOFF`; followed by BRANCH_Z |
| same | 6 at 22 | Variable index2 `VAR_CAPACITY` |
| same | 7 at 28 | Variable index8 `VAR_WORN`; native Mac bridge consumes it without implementing a controller change |
| BUMP / Fantasy `bbugs.wad/bbugs.RSE` | 1 at 94 | Variable index0 `VAR_LETMEON` |
| same | 2 at 210 | Variable index1 `VAR_LETMEOFF`; followed by BRANCH_Z |
| same | 4 at 47 | Literal0; followed by BRANCH_Z; creation can fail |
| same | 12 at 100 | Literal0; followed by ADD; boarding-group transfer |
| TOUR / Fantasy `twetours.wad/twetours.RSE` | 3 at 191 | Variable index15 `VAR_PEEPID` |
| same | 4 at 119 | Variable index13 `VAR_TEMP`; followed by BRANCH_Z |
| same | 8 at 210 | Variable index3 `VAR_DURATION` |
| same | 16 at 42 | Variable index9 `VAR_RUNNING`; followed by TEST |
| TOUR / Hallow `tourride.wad/TourRide.RSE` | 17 at 26 | Literal2048; direct setter `0x67404` writes controller+32 |

The corpus metadata pins each representative's member SHA, word position,
operand kind/value/name and next opcode, plus kind counts across all uses.
For the common root layouts it additionally confirms array indices
0/1/2/3/5/8/9 as LETMEON/LETMEOFF/CAPACITY/DURATION/ONRIDE/WORN/RUNNING.
It reports −1 for missing names in child/event scripts, so this is explicitly
not a universal header layout. Fantasy B_DRIP SHA is
`38dc4c86eedfeb45cf24be3570274724dcdef528928282b4f3a8f3682eed4230`;
Fantasy twetours SHA is
`30668366a55afd08016b702a007c1ca48c84fabe358b3e129b51c2005ba6cbbb`.
These independently corroborate the GUESTS lane's host array-index0 admission
protocol and the ECONOMY lane's array-index5 occupancy reads.

### Boarding, departure, and occupancy

BUMP controllers are indexed in 208-byte records; individual vehicle records
have stride172. The passenger links contain visitor ID at +0, controller
handle at +4, engine node at +8 and next link at +16. Link-size20 is a working
layout inference from these fields; allocation/serialization linkage must
still establish its complete schema.

BUMP1 (`0x251a8`) requires a valid controller with nonzero phase and a free
passenger link, takes a link from the global free list and **prepends** it to
controller+196, returning1; failure returns0. BUMP2 (`0x25294`) pops
controller+200, returns the visitor ID, and returns the link to the free list;
an empty list returns0. BUMP12 (`0x23fe8`) finds an active vehicle belonging
to the controller with an empty passenger head, transfers **the pending group**
from controller+196 into vehicle+48, clears the pending head, updates flags,
and starts loading actions. It is not an inert validity query.

During unloading (`0x254c8`), vehicle links move to controller+200 and the
controller passenger count +96 decreases per moved link; engine node cleanup
is separate. Vehicle count +92 decreases when the vehicle is retired
(`0x25738`), independently of passenger count. Therefore capacity, cars,
passengers, seat/engine-node occupancy, and script ONRIDE must remain distinct
quantities. `VAR_RUNNING` from BUMP11 is a vehicle count, not a passenger count.
Full boarding-count increments, host guest attribution, cost/income callbacks,
and per-type node grouping still require their complete paths.

COAST keeps separate ring structures at controller+176 and +208. In the first
ring, +0 is allocated ring capacity, +4 the storage pointer, +8 the configured
admission limit, +12 queued count, +16 held/reserved count, +20 read cursor,
and +24 write cursor. `0x3dbc0` admits a visitor only while queued+held is less
than allocated ring capacity. `0x3dd14` returns
`min(global_admission_limit, configured_limit - queued - held)`.
The read/write cursor arithmetic wraps at allocated capacity. The departing
ring's `0x3dd5c` checks its logical boundary and count before returning a
visitor; zero indicates no departure. The meaning of its +28 boundary and
the transfer of held counts between cars still need recovery.

Tour allocation `0x63b60` is **4652 bytes = header92 + 20×record228**. Record
state is +128, signed occupancy +140, guest-ID array +144, engine model handle
+192, and grouping divisor +220. The observed array space is48 bytes before
the model field, consistent with12 visitor slots, but slot-capacity validation
has not been recovered. Admission `0x66a64` searches state1 records with
nonnegative occupancy, writes a visitor into the next slot, increments the
record count and total controller occupancy (+72), and returns1. Normal
departure `0x66784` consumes negative occupancy toward zero and decrements
the total, reading slots in reverse boarding order (−3 yields slots2,1,0).
Signed occupancy is therefore a loading/unloading state encoding,
not a direct signed visitor ID. Force-departure `0x668e8` handles either sign.
Node grouping uses `occupancy % grouping + 1`; grouping0 bypasses binding.
This arithmetic does not prove that grouping equals logical seat capacity.

### Capacity and path math, with conditional schemas

COAST6 (`0x3df24`) takes the minimum of the request, global limit, controller
definition+792, and first train definition+8, then rebuilds if the result
differs from controller+240. The train builder `0x3ea74` uses train records128
bytes and car records96 bytes. Front/rear cars whose type differs from the
center type are excluded from rider capacity; eligible cars receive
`remaining_passengers / remaining_eligible_cars`, using unsigned truncation,
with leftovers passed to later cars. Per-car rider allowance is stored at
car+68. Consequently10 riders across3 eligible cars produces **3,3,4**, not
4,3,3. `distribute_capacity` verifies conservation and this remainder order.
It does not impose a one-rider-per-car rule; passenger mesh/seat-node sharing
must be recovered separately.

`0x3ebe8` positions cars around the train's selected center. It selects front,
middle or end spacing from definition+28/+32/+36 based on logical car index,
adds spacings while moving left, subtracts while moving right, stores distance
at car+56 and divides by controller track length (+172) into car+60. Native
operations are single precision. The helper preserves rounding at every step.
Adjacent native schema labels name `fFrontCarSpacing`, `fStdCarSpacing`, and
`fEndCarSpacing`; their loader-to-structure assignments still need complete
schema linkage. The current PC WAD SAM probe records69 numeric
`Bumper.WhichTrackType` entries and their member identities; it finds no
numeric train-spacing, TrackInfo, or Direction keys under the probed names.
It does not establish that those values are absent from other formats,
shared defaults, or the native runtime.

The TOUR create call converts an engine-node angle field(+16) into
`trunc(input*4096/360)` with a signed reciprocal constant; constructor+64 then
stores `1024 - converted_angle`. The heading helper checks this arithmetic
over inputs0..359, including0→1024,90→0,180→−1024,270→−2048. Calling the
source value degrees or equating it to **TrackInfo.Direction** is still a
conditional schema interpretation. The corpus SAM probe seeks numeric
TrackInfo/Direction fields and reports observed keys, rather than inventing
their positions or cardinal enum values. Full track spline/path codecs,
direction-axis convention, station orientation, motion integration, collision,
banking, and controller state machine remain implementation dependencies.

The RSE zero-divisor helper is bounded to opcodes49/50: conditional branches
`0xb0c94` and `0xb0d14` are independently verified to reach their zero-result
arms. Signed division truncates toward zero; remainder follows the dividend's
sign. Signed minimum divided by−1 is excluded because native overflow behavior
is not qualified here. Controller modulo and floating-point geometry divisions
do not inherit the RSE zero-result rule. Pure helpers reject empty rings,
zero track length and unsupported input domains as evidence-tool policy,
not recovered original input checks.
