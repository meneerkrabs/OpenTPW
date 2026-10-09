# PowerPC advisor and audio evidence

2026-10-09. This lane recovers static behavior in the identified Feral Mac
application and sound library. It adds evidence and bounded witnesses only;
OpenTPW gameplay and the shared PEF/clock readers are unchanged. The original
program was not executed. Original assets, section contents and disassembly
remain outside Git.

## Reproduction and identities

Run from the repository root using Python 3 and the existing toolkit:

```sh
python3 tools/ppc-analysis/lanes/advisor/evidence.py /Users/sander/server/game-assets/mac-feral/bin --asset-root /Users/sander/server/game-assets/theme-park-world/Data
python3 -m unittest discover -s tools/ppc-analysis/lanes/advisor -p 'test_*.py' -v
python3 -m unittest discover -s tools/ppc-analysis -p 'test_*.py' -v
```

The helper uses `pef.py` and the validated transition-vector/import utilities
from `timer_evidence.py`, bypasses the heuristic function finder, and checks
full-file SHA-256 before interpreting any original binary. Its JSON contains
addresses, identities, selected interpreted values and supplied-asset metadata.
Manual control-flow review used the already-installed local `llvm-mc`; no new
dependency is required to reproduce the witness. These are bounded witnesses,
not a general decompiler or formal verification of all instructions.

| Binary | SHA-256 |
| --- | --- |
| `SimThemePark.data` | `04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5` |
| `sound_shared.data` | `7132c2f1d772de25b458b9c6e0303e130e6c536d7650a9d2cde5c8cacd6bb94f` |

All addresses below are **section-relative offsets**, not runtime virtual
addresses. Code is section 0 and data is section 1; both application and sound
library use TOC offset `0x8000`. Export transition-vector addresses are stated
separately. Application routine labels below describe recovered roles, except
RTTI identities such as `CMsgAdvisor`; they are not invented exported symbols.

Confidence is high for identity, relocation, selected operand and field facts.
Control-flow interpretation is high where the full bounded routine was reviewed.
Absolute clock units are conditional on the underlying timer route. No Mac
static fact establishes Windows Patch2 behavior, visual fidelity or device
latency.

## Original advisor flow

The manual `--advisor-say N` implementation covers only presentation of a selected
sample. The original has a separate response map, scored pending-message queue,
per-message history, event subscriptions, animation sequence and timed speech
start. A sample number and an advisor response ID are different identifiers.

| Role | Application code | Grounding |
| --- | --- | --- |
| Presentation initialization | `0x6264–0x6388` | Clears 360-byte presentation state, resolves five mouth nodes and animation durations |
| Response playback/start | `0x6b7c–0x7068` | Response map search, LIP path/load, sound event, duration, animation and deadlines |
| Presentation update | `0x7434–0x78c8` | Sound-handle update, pending start, animation continuation, LIP toggle and mouth visibility |
| Advisor controller initialization | `0x8028–0x82a4` | Registers seven message types, resets queue/time fields |
| Controller update | `0x86bc–0x8b10` | Scores/selects pending advice and records history |
| Message enqueue | `0x8b78–0x8d34` | Eligibility checks, first empty slot, weakest-slot replacement |
| Eligibility checks | `0x9008–0x92ec` | Tutorial option, repeat interval, once-only/slap and duplicate limits |
| Minimum/maximum score scans | `0x9350–0x9418` / `0x9418–0x94dc` | Eight 24-byte pending records |
| Game event handler | `0x94dc–0x9ea8` | Event-ID switch; event 10 clears history |
| Staff event handler | `0x9ea8–0xa130` | Staff category and status select advice message |
| Message receiver | `0xa228–0xb678` | Type-ID dispatch to advisor/research/events/staff/pranks/rides/challenges |
| Scoring data loader | `0xcfd4–0xd054` | Loads `data:advisor:advisor.sam` |

### Automatic triggers and priorities

Initialization registers these message IDs. Their identities are tied to the
RTTI virtual table and a getter returning the type ID, rather than deduced from
nearby strings alone. The receiver's relocated jump table is data `0x1e120`;
lookup uses `(typeID - 5) * 4`.

| Type ID | RTTI identity | Type getter, code | Receiver destination, code |
| ---: | --- | --- | --- |
| 5 | `CMsgAdvisor` | `0xd4434` | `0xa28c` |
| 17 | `CMsgResearchCompleted` | `0xf26b8` | `0xa42c` |
| 19 | `CMsgEvent` | `0xcd0ac` | `0xad40` → event handler `0x94dc` |
| 23 | `CMsgStaffInfo` | `0xf8e40` | `0xad50` → staff handler `0x9ea8` |
| 14 | `CMsgPrankery` | `0xefdb8` | `0xad60` |
| 8 | `CMsgRideCondemned` | `0xe30e4` | `0xb2d0` |
| 25 | `CMsgChallenge` | `0xd1218` | `0xb484` |

Research completion branches on category 0–4 and constructs advice message
IDs 93–97. The staff handler forms `status * 5 + categoryOffset`, with category
0–4 offsets 34, 33, 36, 35 and 37 respectively (`0x9f00–0x9f48`). Event ID 10
selects the diagnostic identifying `RESET_FOR_EASY_MODE` and clears the 351
history records (`0x9dcc–0x9e80`). This is specific trigger-path evidence;
producer conditions and all scenario acceptance behavior remain unverified.

Pending advice has eight 24-byte records starting at controller offset `+20`.
Record serialization identifies `mThingId`, `mSecondary`, `mMessage`, `mScore`,
`mValid`, `mResponseNo`, `mOverrideOnlyOnce` and `mScoreOverridden`
(`0xbc10–0xbec4`). Valid records contribute their signed score; an invalid score
is treated as −1 in the scanned paths. The maximum scan updates only for a
**strictly greater** score; the minimum scan only for a **strictly smaller**
score. Equal scores retain the earliest valid slot. Relevant conditional
branches are `0x9494` and `0x93d0`. Under queue pressure, incoming advice replaces
the weakest entry only when its score is strictly higher (`0x8cd4–0x8cd8`).

The supplied `Advisor.sam` independently provides general minimum-score 25,
minimum-any-message interval 5, minimum-same-message interval 120, per-group
repeat/once/slap controls and individual advice scores. The binary loads and
uses those controls: eligibility code checks tutorial-option byte `+53`,
per-message history time, once-only state, slap count and duplicate count;
controller update compares the candidate score with scoring state field `+32`.
These connections establish that scored automatic advice exists. They do not
fully prove the interpretation of every SAM property or every score producer.
Do not derive a complete controller by reading configuration names alone.

## Clip selection and local speech

The response table starts at application data `0x18ff4`, with 610 32-byte
records followed by response-ID sentinel 9999. Playback linearly searches by
response ID (`0x6bf8–0x6c24`). Record fields used by the consumer are:

| Record offset | Recovered use |
| --- | --- |
| `+0` | Advisor response ID |
| `+4` | Audio sample number, copied to presentation `+28` |
| `+8` | Number used for `sp_%03d.lip` |
| `+12` | Animation sequence ID; −1 selects generated sequence |
| `+16` low/high halves | Advisor model index / global-vs-local speech selector |
| `+20`, `+24` | Additional animation/visibility controls |
| `+28` | Value returned by response metadata lookup `0x7274` |

Playback formats `:Speech:lips:sp_%03d.lip` at `0x6d04` and prefixes either
`Data:Global` (`0x6d20`) or the current-level root returned by `0x115f38`
(`0x6d30`). It likewise selects sound-bank fields `+20` or `+32` from the bank
state at data `0x97f8c` (`0x6e8c` / `0x6e94`). Only response IDs **1, 399, 400,
401 and 402** have a nonzero local selector in this identified table; each
selects local `sp_001` and sample 1. Other responses use the global selector.
This directly contradicts an always-global fidelity rule.

The supplied baseline global speech bank has 641 entries. One-based sample 638
is `z_error.mp2`; 639–641 are three ouch variants. The original response table's
response 0 uses sample 638 and LIP number 0, rather than treating response 0 as
sample 0. The slap path `0x7068–0x7160` chooses sample 639/640/641 with `rand() % 3`.
Two ouch variants have no paired global LIP, so a consumer must not require a
LIP for every sound event.

The helper verifies identities and parses the actual supplied global LIPs,
SDT metadata and four loose level LIPs without saving their contents. Counts:
639 global LIPs; 641 global speech entries; 640 Layer II first frames and one
Layer I first frame. The four level `sp_001.LIP` files match global LIP members
fantasy `sp_473`, hallow `sp_476`, jungle `sp_479`, space `sp_478`, respectively.
These are **asset identities**, not proof that the corresponding global response
ID is 473/476/479/478; the binary uses a distinct response map.

## LIP timestamps, talking and mouth selection

### Actual clock and mark conversion

LIP bytes are loaded, converted with imported
`SamsUtilities::_USwapBlock32` at call `0x6e4c`, and indexed as signed 32-bit
words. The terminal word is −1. At `0x6fe4–0x6ff8` and `0x770c–0x7720`, the
consumer constructs multiplier `0x10624dd3`, takes signed multiply-high,
arithmetic-shifts by 6 and adds the sign correction. This is integer division
by 1,000, truncated toward zero. The helper checks the interpreted operands;
a derived algebraic test checks both signed extremes, boundary values and
10,000 deterministic random words against that arithmetic meaning. It executes
no original machine instructions.

The converted mark is added to the presentation start time `+4`, not to a PCM
sample count. The actual source chain is:

`0x7454 → 0x10e864 → 0x11a428 → 0x117c00 → 0x10edb4 → LbTime_GetClock`

`0x10e864` addresses a clock subobject at `+68`. `0x11a428` adds its offset
field `+24`. `0x117c00` either returns the live source minus field `+16` or, when
field `+20` is set, returns frozen field `+12` minus `+16`. The underlying
`0x10edb4` accumulates unsigned differences between `LbTime_GetClock` results
without the main scaled-clock multiplier. The advisor also uses its own pause
query (`0x10e924`) and compensation fields `+284/+288/+292` to freeze/update
its presentation clock (`0x7448–0x74c8`).

The [clock witness](FINDINGS.md) proves `LbTime_GetClock` divides a 64-bit
absolute timer by 1,000 and returns its low quotient. Its documented fallback
route supplies system microseconds. **For that route**, these advisor deadlines
are milliseconds, and the raw LIP mark scale is microseconds. The recovered
local LIP division by 1,000 is unconditional; the absolute time-unit assertion
is still conditional on the original timer route. No unconditional runtime
choice of that route or exact 1,000,000 units/second is claimed here.

Actual global `sp_001` marks 2,226,893 / 2,812,380 / 4,058,820 become **2,226 /
2,812 / 4,058 clock milliseconds relative to the chosen start**, not floating
point seconds rounded to sample time. Jungle and space level clips start with
zero marks, a relevant boundary that the synthetic witness covers.

### Toggle and visibility behavior

Successful LIP load initializes talking state `+16` to 1 and LIP-active state
`+316` to 1 (`0x6e68–0x6e74`). The first mark establishes deadline `+276`.
When current clock is **strictly greater** than that deadline, update performs
one boolean inversion of talking (`0x76c0–0x76d8`), consumes one next word,
and computes the next absolute deadline. There is no loop to consume every
expired mark in one update. A delayed frame therefore processes one mark per
presentation update; equality does not toggle yet. On the terminal −1,
`0x7734–0x7744` clears both talking and LIP-active.

Mouth selection is separate from LIP mark content. Initialization stores five
resolved mesh indices at state `+64`, repeating for two advisor model slots.
The name resolver at `0x19b394–0x19b504` compares these case-insensitively:

1. `mouth - normal`
2. `mouth - aah`
3. `mouth - eee`
4. `mouth - ooh`
5. `mouth - sss`

Talking **and** LIP-active permit a new mouth selection when the current clock
is strictly later than change deadline `+304`. The original calls `rand`,
selects `rand() % mouthCount + 1`, and sets the next deadline to current time
+100 (`0x7770–0x77a4`). Mouth count is 5, so **Normal is also a possible talking
selection**. Outside that condition the desired selector becomes 1, Normal
(`0x77ac–0x77b0`). When desired and previous selectors differ, update sets
mesh flag `0x10` on the former node and clears that flag on the desired node,
using node-record stride 160 (`0x77d8–0x7868`). The ordinary geometry visibility
consumer still needs independent render-lane verification of all node flags.
The actual selection/toggle logic is established without inferring phonemes
from audio amplitude or undecoded mouth animation tracks.

## Animation, initial timing and geometry

The original does play advisor animations. Initialization queries category 5
animation durations for indices 0–19 through `0xa7e74` (`0x6350`). Start helper
`0x293cc` calls `0xa6cc0` with category 5, index `animationID - 1`, speed 1 and
flags 16 (`0x29410–0x29424`). Presentation update starts subsequent clips
through the same routine at `0x7664`. Generated sequences begin with animation
14, choose among durations until the sample-length budget is exhausted, and
finish with animation 15 (`0x63f0–0x65f4`). The fixed-sequence builder has
additional flag/control entries (`0x65f4–0x66ec`). This does not decode vertex
payloads, prove the on-screen pose, or establish all sequence semantics.

Speech start is not simply tied to the first rendered frame. Playback first
sets a start-base bias of −200 clock units (`0x6cf0`) and a pending start
deadline of that base +1000 (`0x6d7c`). It creates the audio event and queries
length; subsequent sequence setup may clear the pending deadline unless its
first animation is 14. If the deadline survives, playback stops the initial
handle, clears talking, and shifts the LIP/start base by +1000
(`0x6f88–0x6fb0`). Update later creates the speech event when the pending
start deadline is reached (`0x7538–0x7590`). The original therefore has at least
an immediate and an animation-led delayed path. The asset sample, response
animation and flags must all be retained to reproduce this scheduling.

The setup routine `0x29084–0x2925c` supplies independently verified data values:
object fields `+56/+60/+64` receive `(0.6, −0.6, 0.2)` as the translation of
the identity matrix at object `+8` (proved in the phase-two follow-up below),
and an associated structure
receives four 1.0 components at `+4/+8/+12/+16`, and its `+44/+48/+52` fields
receive `(26.666667, −26.666667, 800)`. It calls imported `CSystem::UpdateLights`
at `0x290dc` and `0x29108`. Scale-vector construction at `0x29208–0x29244`
passes `(0.01125, 0.001, 0.015)` to `0xa2850`; the first component is the product
of 0.015 and 0.75. MapWho linking passes selector flags 16 (`0x29200`), which
the engine resolves to bucket 0, rather than a viewport ID. These are concrete
geometry/lighting leads; neither vector establishes an advisor camera location. Viewport placement, projection, front-face convention and
all hat/accessory/blink visibility rules remain open.

## Audio scheduling and the Layer I gap

Application wrapper `0xbb4fc` sends a bank/sample/location audio event through
the audio-system virtual interface; `0xbbfc0` constructs the imported
`TbSysCommand::GetSampleLength` command. Sound export execution at code
`0x1bd20` resolves the handle and copies a duration field from the placeholder
child. A printable message labels that length in milliseconds. No PCM-consumed
position is read by the recovered advisor LIP path.

Application service routine `0xbb18c` always submits imported `Tick` first
(`0xbb1dc`), updates dirty volume/enable controls, then submits `Process`
(`0xbb4c8`). Commands include `SetMusicVolume`, `SetSpeechVolume`, `ToggleSpeech`,
`ToggleMusic` and `ToggleSFX`, all resolved through loader imports. Advisor
start calls `0xbc118` with 1; completion calls it with 0. That routine sets
speech-active field `+44` and dirty flag `+40` on sound-control state. The dirty
update branches on that field and speech-enabled `+64`; music/user-volume
updates use configured percentages and multiply/divide by 100 on the active
branch (`0xbb200–0xbb3b4`). This establishes speech-linked mix control without
assuming a particular ducking percentage or device output policy.

| Sound export | Transition vector, data | Code |
| --- | --- | --- |
| `CMpegBase::Decode(short*)` | `0x904` | `0x1bf8` |
| `CMpegBase::Decode_Layer1(short*)` | `0x994` | `0x2d0c` |
| `CMpegBase::Decode_Layer2(short*)` | `0x9a4` | `0x3ee4` |
| `CPlaceHolderSentence::SoundCallback(void*, void*)` | `0x1afc` | `0x1a1f0` |
| `TbSysCommand::GetSampleLength::Execute(...)` | `0x1b9c` | `0x1bd20` |
| `CServiceQueue::Poll()` | `0x9ec` | `0x5a98` |
| `TbSysCommand::Tick::Execute(...)` | `0x1eb4` | `0x1da44` |

Decoder dispatch checks the layer field: the direct call at `0x1c64` reaches
Layer I `0x2d0c`; `0x1c78` reaches Layer II `0x3ee4`. The first routine has a
substantial implementation, not an unavailable-code stub. The supplied baseline
UI bank has 32 Layer I first frames and global music has two Layer II first
frames, identity-pinned by the helper. Thus OpenTPW's missing Layer I decoding
blocks original UI/sound playback and the error speech sample even though its
Layer II manual advisor clip is playable. The Layer I path is missing in
OpenTPW, not in the identified original sound library.

The sentence callback checks for a next child sample, updates the current
child, fills sample info and constructs the next sample
(`0x1a1f0–0x1a2e8`). That verifies callback-driven chaining exists. Complete
music-bank/category scheduling, playlist selection, environmental priorities,
voice limits, fade duration and Mac device output buffering remain dependencies.
Do not replace all original audio control with a single fire-and-forget PCM queue.

## Fidelity handoff and remaining dependencies

No approximation annotation is removed by this evidence-only lane. The
following table distinguishes narrower established facts from completion:

| ID | Evidence result / next implementation dependency |
| --- | --- |
| ADVISOR-001 | Original random five-mouth selection recovered; implement selector cadence and node flag behavior, then verify rendering |
| ADVISOR-002 | Mouth and additional sequence visibility paths recovered; full accessory/blink/role rules still open |
| ADVISOR-003 | Original viewport rectangle/scale still open |
| ADVISOR-004 | Original camera/projection still open; recovered light vectors do not establish it |
| ADVISOR-005 | Original light setup operands recovered; final material/render comparison still open |
| ADVISOR-006 | Category 5 animation sequence/duration consumers recovered; vertex payload and resulting poses remain open |
| ADVISOR-007 | Original winding/culling still open |
| ADVISOR-008 | Immediate and pending-start branches recovered; integrate response animation state and verify A/V start |
| ADVISOR-009 | Five local-response mappings directly established; implement local banks/LIPs and language resolution |
| ADVISOR-010 | Original advisor clock uses unscaled clock with freeze/compensation, not PCM consumption; implement clock semantics |
| ADVISOR-011 | Original missing-device behavior not recovered; current wall-clock fallback remains unverified |
| ADVISOR-012 | Original channel/output routing not recovered; mono duplication remains unverified |
| ADVISOR-013 | Direct /1000, start-talking, strict threshold, one mark/update, terminal close recovered; absolute unit remains timer-route conditional |
| ADVISOR-014 | Phase two matches all 257 half-window coefficients to the original synthesis table; full published-standard provenance and decoder equivalence remain separate |

Implementation order: preserve separate message/response/sample identities and
history; wire automatic game-message inputs; add eligibility and bounded scored
queue; select global/local banks and LIPs; use the recovered advisor clock and
speech/animation scheduling; apply mouth selection/visibility; decode required
animation payloads and Layer I sound; compare original presentation and platform
behavior when authorized capture evidence becomes available. No independent
clock or score assumptions should be introduced to fill these dependencies.

Verification completed: identity-pinned binary/asset witness succeeds; all 15
lane tests pass, including queue ties, integer conversion, zero/fractional marks,
strict deadlines, one-expired-mark updates, terminal close and malformed asset
bounds. Shared PEF/timer tests are run separately. Lint/static checks are Python
compilation and `git diff --check`; no gameplay changes require a game build.


## Phase two: full descriptor linkage and narrower controller rules

Additional reproducible witnesses:

```sh
python3 tools/ppc-analysis/lanes/advisor/controller_evidence.py /Users/sander/server/game-assets/mac-feral/bin
python3 tools/ppc-analysis/lanes/advisor/controller_evidence.py /Users/sander/server/game-assets/mac-feral/bin --details > /tmp/advisor-controller-detail.json
python3 -m unittest discover -s tools/ppc-analysis/lanes/advisor -p 'test_*.py' -v
```

Detailed output covers all 351 message descriptors and all 610 response bindings,
including callback addresses, direct score-property meanings, response ranges,
model and bank selectors. It remains interpreted metadata outside Git; the
proprietary table contents are not checked in as fixtures. The additional engine
identity is SHA-256 `c549123f647dcf33c3e2c3d515bffe2cfcb19d11680f743f02ca42bb09dbd73b`.

### Scored message, response and sample are separate domains

The scored-message descriptor table is data `0x1f2b4`, **351 records of 48
bytes**. Its message IDs are exactly 0–350. Every shipped descriptor selects
variant mode 2. The descriptor's response range selects the separate 610-record
response table, which in turn selects a bank sample and LIP stem. For example,
message 0 chooses response 1; response 1 selects local sample 1 and local
`sp_001.LIP`. It does not select global sample or global LIP 0.

| Descriptor field | Established meaning and consumer |
| --- | --- |
| `+0` | Zero permits the background score scan; nonzero skips that scan (`0xe548–0xe550`) |
| `+4` | Message ID; validated against all 351 indexed records |
| `+8` | Message group; used by repeat/once/slap accessors |
| `+12` | Game-mode eligibility selector; 2 always passes, 0 checks mode 0, 1 checks mode 2 (`0xe128`) |
| `+16..+24` | Three-word member-function callback descriptor installed by static initialization |
| `+28` low byte | Maximum pending duplicates; used by accessor `0xd770` and eligibility count comparison |
| `+32` | First response ID; accessor `0xd468` |
| `+36` | Response variant count; accessor `0xd5ec` |
| `+40` | Variant selection mode; accessor `0xdc44`; all shipped values are 2 |
| `+44` | Literal 352 in the inspected descriptors; behavioral use remains unresolved |

The initializer at `0x148a0` copies callback metadata from data
`0x1e240 + messageID * 12` into each descriptor. The new witness verifies **all
1,053 word-copy provenances** over the straight-line copy body
`0x148d4–0x16f38`. It rejects any instruction outside the reviewed load/store
subset; it does not execute the initializer or original arithmetic. Every
source has adjustment 0, direct-member marker −1, and a relocated transition
vector whose code entry and TOC are resolved. There are 241 distinct callback
code entries across the 351 slots.

The schema at data `0x2349c` contains 774 60-byte records before its type-12
terminator. Its getter is `CAdvisorBalance::vf0`, code `0xbee4`. The reviewed
constructor `0x16f4c` assigns scalar fields and expands the ten message groups;
391 named balance fields are recovered. Critical bindings are:

| Balance field | SAM property |
| --- | --- |
| `+24` | `GeneralAdvisor.MinTimeAnyMessage` |
| `+28` | `GeneralAdvisor.MinTimeSameMessage` |
| `+32` | `GeneralAdvisor.MinScoreForConsideration` |
| `+36 + 12*g` | `MessageGroups[g].MinTimeSameMessage` |
| `+40 + 12*g` | `MessageGroups[g].SayOnlyOnce` |
| `+44 + 12*g` | `MessageGroups[g].DiscardAfterSlaps` |
| `+160` | `Welcome.Score` |
| `+164` | `OpenPark.ScorePerWaitingPerson` |
| `+168` | `ClosePark.Score` |
| `+172/+176` | `VisitorsThirsty.ScorePerThirstyPerson` / `ThirstierThan` |
| `+180/+184` | `VisitorsHungry.ScorePerHungryPerson` / `HungrierThan` |
| `+188/+192` | `StaffHireMechanics1.ScorePerWornRide` / `PoorerStateThan` |
| `+196` | `StaffHireMechanics2.ScorePerBrokenRide` |
| `+200/+204` | `StaffHireMechanics3.IdealNumberOfRidesPerMechanic` / `ScorePerExtraRide` |

Of all 351 callback slots, 170 consist of a complete direct getter of a named
balance field. Another 46 are complete constant-return functions: 44 return
zero, message 167 returns 10,000 and message 168 returns 9,999. The remaining
**135 slots have conditional or computed score functions**; the detailed output
provides their exact addresses instead of claiming their formulas are solved.
Thus 216 score bindings now have direct implementation meaning. Every remaining
computed producer is an explicit bounded dependency.

Selected complete message-to-score-to-response links:

| Message | Score callback | Score meaning | Response range |
| ---: | --- | --- | --- |
| 0 | `0xe8c4` | `Welcome.Score` | 1 |
| 1 | `0xe8cc` | Gated waiting-person count × `OpenPark.ScorePerWaitingPerson` | 2–4 |
| 2 | `0xe9b0` | Gated `ClosePark.Score` | 5–7 |
| 3 | `0xea80` | Thirst count × `VisitorsThirsty.ScorePerThirstyPerson` | 8–10 |
| 4 | `0xeacc` | Hunger count × `VisitorsHungry.ScorePerHungryPerson` | 11–13 |
| 5 | `0xeb18` | Gated worn-ride count × `StaffHireMechanics1.ScorePerWornRide` | 14–16 |
| 6 | `0xeb8c` | Gated broken-ride count × `StaffHireMechanics2.ScorePerBrokenRide` | 17–19 |
| 33 / 34 | `0xfe9c` / `0xfea4` | Unhappy mechanic / handyman configured score | 97–99 / 100–102 |
| 93 | `0x113a4` | `ResearchRideResearched.Score` | 248–249 |
| 94 | `0x113ac` | `ResearchAddonResearched.Score` | 250 |
| 95 | `0x113b4` | `ResearchShopResearched.Score` | 251–252 |
| 96 | `0x113bc` | `ResearchSideshowResearched.Score` | 253–254 |
| 97 | `0x113c4` | `ResearchFeatureResearched.Score` | 255–256 |
| 291 | `0x1145c` | `HelpMessage.Score` | 551, absent from response map |

The guest lane independently verified thirst/hunger producers `0xc3a60` and
`0xc3964`: they count live category-1 guests accepted by a map-cell predicate
and compare truncated need values **strictly greater** than the supplied
threshold. The callbacks pass threshold low byte 99 and multiply by configured
score 1. For normal 0–100 need values, only integer value 100 qualifies. Waiting
producer `0xc3758` visits both ticket-booth cells, counts category-1 guests whose
state is 3, stores the current result with `0xc9020` and reads it back with
`0xc9018`; no history smoothing was established. Open/close-park gating and the
135 computed slots require their individual predicate proofs.

The full response set is IDs 0–612 with **396, 397 and 551 absent**. Descriptor
291 references response 551; the static playback search would fail for that
ID. This is a witnessed table mismatch, not an observed original-runtime error.
Keep it explicit rather than silently substituting a neighboring clip. The
response `+28` metadata lookup is passed into a `CMsgTag` constructor at
`0x1165b4`; the UI lane verified that field's constructor identity. Its default
383 is not established as a help-text or direct string-table index. Node
visibility controls and this tag's final consumer remain dependencies.

### Cyclic variants, thresholds and interruption

All shipped descriptor modes are 2, whose path reads the previous response
variant at history `+228`, adds one (`0x8974–0x8978`), and resets to zero if the
result is not below the variant count (`0x89d8–0x89e0`). Initial history value
−1 therefore starts at variant 0. The response ID is `firstResponse + variant`.
Random variant modes exist in code but are not selected by these shipped records;
random mouth and slap selection are separate rules.

Both background candidates and queued candidates must have score **strictly
greater** than `MinScoreForConsideration` (`0x878c`, `0x8850`). With supplied
value 25, a score of exactly 25 does not begin advice.

After refreshing background advice, the controller calls `0xa130` at `0x87c8`.
That function compares current advisor clock against `lastActionStarted` at
controller `+216` plus `lastActionDuration` at `+220`. It returns busy only
while current time is below the endpoint. A busy result exits controller update
before picking/starting queued advice. High-scoring advice can evict a weaker
**pending** entry but does not preempt this active action through the ordinary
scored-controller path. User slap/cancellation and explicit UI/script calls are
separate paths; no global no-interruption rule is inferred.

### Repeat eligibility uses game ticks, not speech milliseconds

History setter `0x121098` copies game object field `+0x1da70c`. Elapsed getter
`0x1210f8` calls world getter `0x10a9a4`, reads that field, shifts **each** live
and saved value right by two, then subtracts. Repeat eligibility compares this
result directly with the group repeat property (`0x90dc–0x90f0`):

`elapsed = (liveGameTick >> 2) - (savedGameTick >> 2)`

The clock lane independently identified this field as `mGameTick`, incremented
once at `0x105398–0x1053a0`. The active main scheduler nominally advances it every
248 ms of its selected scaled clock, making four ticks approximately 992 ms.
Mode, game flags, pause, speed and capped catch-up affect advancement. A SAM
repeat value 120 is therefore **120 groups of four game ticks**, not a proved
wall-clock duration of 120 exact seconds. On raw-counter wrap the shifted
subtraction is not a smoothly wrapping 30-bit seconds counter. The synthetic
edge test records that arithmetic rather than repairing it.

### Deferred scheduler arithmetic

Let `t` be the advisor presentation clock at response request. The recovered
branches give the following separate quantities:

| Quantity | Recovered expression |
| --- | --- |
| Initial LIP/start base | `t - 200` |
| Pending speech-start deadline | `t + 800` |
| Deferred LIP/start base | `t + 800` after shifting initial base by 1000 |
| Animation budget from audio length `D` | `D + 200 + 300` |
| Playback returned action span | `sequenceDuration + endingClipDuration + 1000` |
| Controller reserved duration | Returned span plus another 1000 |

The pending audio start accepts **equality**, because its update skips only
while `deadline - now` is greater than zero (`0x7548–0x754c`). LIP toggles and
mouth changes require strictly greater deadlines. Combining them into one
uniform comparison changes an original boundary.

The two margin values are independently visible at `0x6ec0`, `0x6ef8`,
`0x7048` and `0x8a10`; the helper provides a derived arithmetic witness and
synthetic cases for immediate/deferred paths. Sequence duration and final clip
state must be supplied by the original animation path; they are not replaced
by audio length. Original audio start latency remains unobserved.

### Geometry field meaning and shared-render boundary

The phase-one translation fields are now tied to an actual matrix initializer:
`0xa1e9c` clears 64 bytes and sets 1.0 at diagonal offsets 0, 20, 40 and 60.
Advisor setup calls it on context `+8`. Translation is therefore matrix offsets
48/52/56, `(0.6, −0.6, 0.2)`, not a light position. Contexts start at data
`0x559f4`, stride 188. The actor retains that matrix address at `+200`
(`0x291d8–0x291e4`); animation start retrieves it at `0xa6d18` and passes it via
`0x55f54` to imported `CMesh::SetLocalMatrix(int, sMatrix&)` at `0x55f84` with
flags 1. The engine export transition vector is data `0x1060`, code `0x1903c`.
The nonuniform `(0.01125, 0.001, 0.015)` scale modifies the model matrix basis
through `0xa2850`. Final screen coordinates still require the render/projection
consumer; treating the translation directly as pixel placement is unjustified.

MapWho linking selector 16 is decoded by engine `CMapWho::Link`, code `0x13e50`:
its bucket calculation is `(selector >> 4) - 1`, then `20 + bucket*4`. It links
advisor geometry into bucket 0 at MapWho offset `+20`. **16 is not a private
viewport ID.** The shared render path `0x145f4` receives a viewport configuration
whose matrix pointer is at `+20` and rectangle values at `+4/+8/+12/+16`, saves
and replaces the global matrix, then renders bucket lists. This narrows the
remaining projection dependency to that shared configuration and mesh render
mode. No separate bottom-left square viewport or camera `(0,0,-70)` was proved.

Phase-two verification: the controller witness verifies all callback copies,
all descriptor/response bindings and selected scheduling/geometry operands on
the identified binaries. Normal and optimized Python output match. Ten new
synthetic controller tests bring the advisor lane total to **25 passing tests**;
the shared toolkit has 16 passing tests. Python compilation and diff checks
pass. All approximation-status decisions remain with integration and original
presentation/capture verification.


### Original sample indexing and complete synthesis-window correspondence

Original sound bank retrieval supplies a direct indexing proof: exported
`TbFileBank::RetrieveSample` (vector data `0xb3c`, code `0x691c`) subtracts one
for its cache index at `0x6928` but accesses the SDT offset table with
`sampleID * 4` at `0x6944`. The count occupies table word zero, so sample 1
selects the first entry offset. `TbMMFileBank::RetrieveSample` (vector data
`0xb7c`, code `0x6e6c`) subtracts one at `0x6e84` before selecting a sample
record. This proves the earlier one-based association: original sample 638
selects `z_error`, and 639–641 select the supplied ouch entries.

`CMpegBase::PolySynth` (vector data `0x9bc`, code `0x53a0`) addresses its
coefficient table at sound data `0x8410`, using TOC +1040 at `0x53bc`; its
coefficient consumer begins at `0x545c`. The table has 544 float entries,
17 rows of 32, with each row's 16 coefficients duplicated. The new witness
compares every original entry with the existing clean-room `HalfWindow` in
`Mp2Decoder.cs`, under this exact correspondence:

`original[row*32 + tap] = -0.5 * Dint[32*tap + row]`

Here rows are 0–16 and taps are 0–15. `Dint` is the existing 512-entry synthesis
window before division by 65,536, including its stated symmetry; each original
coefficient repeats at row offset `+16`. All 544 comparisons are exact.
Mapping each selected full-window index to its symmetric half-window index
covers **all 257** existing `HalfWindow` values, with no missing entries. This
is not a short prefix match or a waveform-correlation inference. The original
coefficient block SHA-256 is
`a81ef42fead481ee76ce956b404aab71f47e2da031a20a79d5eaa2aa3c6a046c`.

The scale relates original PCM-scaled coefficients to normalized window
values; the helper reports the numerical relation and consumer addresses
without checking original coefficient contents into Git. It does not execute
the original synthesis routine. This materially narrows `ADVISOR-014`: every
existing clean-room window coefficient corresponds to the identified original
binary. Full comparison with published ISO text, DCT/arithmetic equivalence,
rounding, clipping and all decoder formats remain distinct claims. Registry
status and source comments should be revised only after independent integration
review of this narrower evidence.


## Phase three: standalone original-consumer LIP driver

[OriginalAdvisorLipDriver.cs](../../tools/ppc-analysis/lanes/advisor/OriginalAdvisorLipDriver.cs)
is a dependency-free C# transcription of the proven state transitions, kept in
the advisor evidence lane. Its standalone .NET 8 project runs 21 synthetic
checks with the existing SDK:

```sh
/Users/sander/.local/share/opentpw-dotnet/dotnet run --project tools/ppc-analysis/lanes/advisor/OriginalAdvisorLipDriver.csproj --configuration Release
/Users/sander/.local/share/opentpw-dotnet/dotnet format whitespace tools/ppc-analysis/lanes/advisor/OriginalAdvisorLipDriver.csproj --verify-no-changes --no-restore
```

`BeginResponse` accepts raw `uint` words including the terminal raw −1, a
caller-supplied signed 32-bit **pause-aware unscaled advisor clock**, and whether
the animation sequence retained deferred speech. Empty words represent missing
LIP. The helper preserves the initial −200 lead, retained +800 deferred deadline,
strict LIP/mouth comparisons, one mark per update, and pending speech equality.
`Update` returns a speech-start request and desired mouth-selector change; it
neither plays audio nor applies mesh flags. The supplied `Func<int>` must provide
nonnegative rand-compatible values. Choosing a .NET RNG is not an original RNG
proof. Mouth cadence persists across response requests.

The integration dependencies are explicit: reconstruct the actual unscaled
clock subobject and its offset/freeze/compensation, retain the original response
animation's deferred-start decision, provide the original random stream, connect
speech events and termination, and apply the selected mouth within recovered
animation/visibility rules. The helper has **no PCM, SDL, wall-clock, game-speed
or existing runtime `Clock` bridge**. The current [manual advisor playback](../LIPS.md)
continues to use its documented approximation until those dependencies are
connected and independently verified; standalone test success does not change
runtime fidelity status.

Tests cover equality and missed frames, pause/resume using supplied frozen time,
expired backlogs during pause, all five mouth selectors including Normal, one
random draw after a delayed update, raw and converted negative sentinels,
initial-negative-mark behavior, absent LIP, immediate/deferred boundaries,
input ownership, retained cadence and bounded malformed input. The helper's
size/terminator checks are its own input boundary, not claims about the original
loader's validation. An early raw sentinel after the first mark stops safely
before unused remaining words.

A negative-data edge requires careful transcription: at `0x7734`, the original
update checks the **converted** result for −1 after signed division. Thus a
non-sentinel raw word in −1000..−1999 also ends LIP state when consumed as a next
mark. Initial first-mark setup instead schedules base + converted mark without
that terminal-state check. The C# tests cover both cases; the earlier Python
`LipCursor` witness is corrected to reflect the next-mark check. Positive
supplied LIP assets are unaffected. Python advisor witnesses now have 26 passing
tests, alongside the 21 C# checks and 16 shared toolkit tests.


## Phase four: standalone score queue, eligibility and history helper

[OriginalAdvisorScoreQueue.cs](../../tools/ppc-analysis/lanes/advisor/OriginalAdvisorScoreQueue.cs)
implements the bounded, reviewed consumer slice in the existing standalone C#
project. Inputs are caller-supplied descriptors/group controls, cached scores,
initial history, raw game ticks, unscaled advisor-clock values and external
playback results. No original 351/610-record corpus is embedded and no computed
score function is invented. Synthetic descriptor IDs and response IDs in tests
are deliberately outside the original tables.

The combined project now runs 52 C# checks: the original 21 LIP checks and 31
queue/history checks. The original LIP driver and its clock/audio boundaries
remain unchanged. The score helper provides separate operations:

- `Eligibility` evaluates the supplied tutorial, repeat, once-only, slap and
  pending-duplicate controls.
- `Enqueue` fills the first free slot or replaces the earliest weakest entry
  only for a strictly greater score; its result separates acknowledgement from
  whether an item was stored.
- `SelectNext` gates on the explicit active-action clock, chooses the earliest
  maximum cached score strictly above the supplied minimum, and resolves the
  shipped cyclic response variant. It does not begin playback.
- `BeginPlaybackAttempt` consumes the selected pending record before the
  external score/playback wrapper runs, freeing its slot for any subsequent
  admission. `CompletePlaybackAttempt` confirms the supplied wrapper outcome;
  only success updates history and reserves returned playback span plus 1000
  original clock units. Failure does not restore the consumed record.

The response dispatcher, appropriate computed-score/override wrapper,
application game-mode conditions, remaining 135 score producers, general
background scan, tutorial replay/cancellation, full message payload and runtime
clock/audio/geometry bridges remain external dependencies. In particular,
`playbackSucceeded` is an explicit original-wrapper outcome supplied by the
caller, not inferred from cached score or availability of a PCM sample. The
helper's stale-selection and descriptor/count validation are its own input
boundaries, not additional claimed original gameplay rules.

### Playback completion retains the native selected variant

The native controller retains its chosen variant in `r29` across the external
score/playback wrapper and saves that value at app `0x8a3c`. The response wrapper
can resolve an out-of-range requested variant to the first response at
`0xb8c8`/`0xb904`; that lookup does not replace the requested variant saved in
history. The pure helper now retains its begin-time selection for this purpose.
Completion validates slot, advice identity (including the cached score and
controls in that immutable record), requested variant and resolved response ID.
A changed tuple is rejected before state changes, leaving the authentic attempt
completable. Successful completion writes history from the retained tuple.

The callback ordering contract is explicit: select, begin/consume, invoke the
external wrapper, then complete with the unchanged selection and the clocks
observed after its return. The existing freed-slot test exercises admission
inside that callback interval. The one-outstanding-attempt validation belongs
to this helper API; the reviewed native path does not establish a general
reentrancy or threading rule. Callers must serialize that sequence. The value
selection is not a unique attempt receipt: deliberately reusing the same advice
instance and identical tuple in a later attempt is indistinguishable from an
old value. A future asynchronous bridge needs an explicit attempt identity if
it permits delayed callbacks crossing attempts.

Six additional regression cases cover variant 0 changed to 2, response ID,
slot, cloned or changed advice (message, cached score and overrides), duplicate
completion, and a stale callback after the slot/message has been reused by a
new advice instance. Every rejected active-attempt callback leaves history and
reservation clocks intact and permits authentic completion. All previous 46
cases remain, including native callback ordering and preservation of an
out-of-range requested variant in history. The changed-variant test failed
against the preceding implementation before the tuple validation was added.

### Boundary facts pinned before modelling

`controller_evidence.py` now verifies these extra original operands and branch
conditions and reports their code-range identities as `queue_edges`:

| Boundary | Verified original behavior |
| --- | --- |
| Zero cached score | Admission at `0x8b78` has no minimum-score gate; a valid zero can occupy a free slot. Selection at `0x8850` rejects scores at or below the minimum and leaves pending records intact. Background-score production is a separate path. |
| Full-queue equal/lower incoming score | `0x8cd8` skips replacement, then `0x8d10` still returns acknowledgement 1. Acknowledgement is not proof of insertion. |
| Busy numeric representation | `0xa154` uses plain 32-bit addition and `0xa158` uses **unsigned** comparison: `now < unchecked(start + duration)`. It is not elapsed-time subtraction or signed LIP deadline comparison. Equality releases the gate. |
| Repeat-history sentinel | `0x1210ec` returns `savedGameTick >> 2`; `0x90a4` skips the interval gate when that value is zero. Saved raw ticks 0, 1, 2 and 3 all take this route; once-only history still applies independently. |
| Repeat boundary | `0x90ec` compares unsigned elapsed groups against the supplied interval. Strictly less rejects; equality accepts. |
| Raw tick wrap | `0x121124` performs plain 32-bit subtraction after separate shifts. `live=0, saved=0xfffffffc` yields `0xc0000001`, not a repaired smooth elapsed interval. |
| Once override | `0x9100` and `0x9160` skip once/slap checks; tutorial, repeat and duplicate gates still apply. |
| Slap counter | `0xe0fc` uses signed comparison of stored 32-bit patterns against a nonzero configured limit. The helper retains unsigned storage and casts for that comparison. |
| Duplicate limit | Count must be strictly below the descriptor's low-byte maximum (`0x9230–0x9234`). Zero allows no new pending copy; once override does not bypass this gate. |
| Cyclic arithmetic | The previous variant increments with wrapping signed 32-bit addition and resets only if the signed result is not below count. Corrupt `int.MaxValue` history becomes `int.MinValue`; it is not silently normalized. |
| Playback revalidation | `0xb7d8` invalidates the pending record before recomputing score. The computed wrapper's `0xb858` accepts score **equal to** the minimum, while cached selection requires strictly greater. The helper exposes this separate inclusive comparison without running a producer. |
| Explicit out-of-range variant | The wrapper chooses the first response (`0xb8c8–0xb904`), but controller history still stores the requested variant (`0x8a3c`). The helper keeps requested variant and resolved response ID separate. |
| Success-only history | The controller saves raw game tick, variant and played flag after wrapper success (`0x8a28–0x8a54`). Failure consumes the pending record without these updates. |

Already-admitted pending records are not retroactively purged or re-evaluated
against once-only admission history in the reviewed selection path. A synthetic
case therefore permits two copies admitted before the first play, then rejects
a new once-only admission while the already-pending second copy remains
selectable. This is distinct from assuming that every successful play performs
a global queue eligibility cleanup.

The unsigned busy gate and signed LIP/mouth gates deliberately have different
clock representations/comparisons in the helpers. Both require explicit
recovered clock input; neither manufacture wall-clock units or connect to the
current runtime `Clock`. The Python cyclic witness is also corrected to preserve
signed increment wrap and has a new regression case.

Verification: all 46 C# checks pass, including queue-full ties, zero/minimum
scores, cyclic wrap, repeat equality/sentinel, raw tick wrap, unsigned busy
endpoints, playing priority, failed playback, once/duplicate overrides and
post-dispatch clock/history capture. The SDK analyzer build has zero warnings
and errors; formatting matches the repository's CRLF convention. Git whitespace
checks recognize CRLF via a command-local setting; no global configuration is
changed. Python advisor tests now total 27, with 16 shared toolkit tests; the
identity-pinned descriptor/asset witnesses remain successful.


## Phase five: Layer I runtime codec

The runtime `Mp2Decoder.Decode` now dispatches Layer I frames as well as the
existing LSF Layer II path. `Mp2Decoder.Layer1.cs` implements valid MPEG-1/2
Layer I allocation, scalefactors, requantization and intensity stereo; shared
synthesis/interleaving is reused with the correct per-layer channel stride.
The legacy public name and Layer II `SamplesPerFrame` constant remain compatible;
`LayerOneSamplesPerFrame` is 384. `MP2File.FrameData` documentation now identifies
both layers, since the original `.mp2` suffix does not select the codec.
No SDL, UI, game-clock or automatic-event code changes are included.

Reproduce the new static and independent decoder witnesses:

```sh
python3 tools/ppc-analysis/lanes/advisor/layer1/pef_layer1_evidence.py /Users/sander/server/game-assets/mac-feral/bin
/Users/sander/.local/share/opentpw-dotnet/dotnet run --project tools/ppc-analysis/lanes/advisor/layer1/Layer1Corpus.csproj --configuration Release -- /Users/sander/server/game-assets/theme-park-world/Data /tmp/advisor-layer1-corpus-report.json
OPENTPW_GAME_PATH=/Users/sander/server/game-assets/theme-park-world /Users/sander/.local/share/opentpw-dotnet/dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --configuration Release --filter 'FullyQualifiedName~Layer1DecoderTests|FullyQualifiedName~Mp2DecoderTests|FullyQualifiedName~LipSyncTimelineTests'
```

The static witness pins the existing sound-library identity and resolves
`Decode_Layer1` to code `0x2d0c`, transition vector data `0x994`. It verifies
allocation width 4 (`0x2de8`), 32 subbands (`0x2df8`), scalefactor width 6
(`0x2e20`), sample width allocation+1 (`0x2fe4`), twelve synthesis slots
(`0x3114`), shared synthesis `0x53a0`, and normal output bytes/channel 768
(`0x1bc0`). Original data tables `0x2c54` / `0x2cb4` match the MPEG-1/2 Layer I
bitrate tables, and `0x2c30` / `0x2c3c` match their rate tables.

The original requantizer's numerator at `0x3028–0x303c` is
`code + 1 - (1 << allocation)`, multiplied at `0x306c` by the combined factor
selected from data `0x2d78 + 256*(allocation+1) + 4*scalefactor`.
Every one of its 14 × 63 valid factors exactly matches
`float(2^(2 - scalefactor/3) / (2^(allocation+1) - 1))`. The formula is computed
in double before conversion, matching all 882 originals rather than introducing
the 105 one-ULP mismatches found if the scalefactor is prematurely rounded.
Decoder-range SHA-256 (`0x2d0c–0x3144`) is
`f8e1d76371b8da64e91ff51330a328c2a861261c6a335466dbb14d2d8d07889e`;
the relevant combined-factor region SHA-256 is
`2395bc2ab462d2ad135094e9574cf794f0d7a8b70b014184be3aaa79283c3158`.
This proves interpreted static operands/table correspondence, not original
execution or hardware output.

Test-first evidence: eighteen initial generated Layer I cases failed with the
existing decoder's Layer I rejection before production changes. Generated cases
now cover header version/rates, silence/tone, allocation/sample widths, both PCM
clip extremes, independent/intensity stereo, all joint boundaries, CRC/padding
alignment, reserved values and truncated frames. Independent corpus results:

| Check | Result |
| --- | --- |
| Physical Layer I entries / banks | 2,650 / 33 |
| Unique private Layer I streams | 2,279: 2,270 MPEG-2 mono, four MPEG-2 stereo, five MPEG-1 mono |
| Complete private Layer I frames | 142,278 |
| Generated reference streams | 16 |
| Existing Layer II regression streams | Three: global speech `sp_001` and both global music entries |
| Compared interleaved PCM samples | 56,410,752 |
| Reference | Installed FFmpeg 8.0.1 `mp1`/`mp2` decoder, no source/code dependency |
| Maximum absolute PCM error | 1 signed-16-bit unit |
| Trailing private Layer I data | One byte on every unique stream, explicitly excluded from reference complete-frame prefixes |

No compressed stream or PCM fixture is committed. The report outside Git records
bank/stream identities, actual/reference PCM hashes and error/signal totals.
`z_error` compressed-stream SHA-256 is
`948aa426d01917a422a7cb590a36c46f17049cf3ff56404d49c390229dbda94a`;
its independent 9,600-sample PCM SHA-256 is
`1e86de97c3e240d3ed363fa8fcb3303d06fba3a6755060283fbf440d6e19a48a`.
Private tests use reference sample/RMS tolerances and bank hashes rather than
checking original waveform contents into Git.

Strict profile limits remain explicit: MPEG-1 Layer II, MPEG-2.5, Layer III,
free format and Layer I nonzero de-emphasis are unsupported; reserved/forbidden
values and truncated complete-frame payloads are rejected. CRC words are skipped,
not verified. Undefined SF63 mutes in the identified Mac table but is rejected
by this strict reader; none of 3,253,907 private parsed scale indices is 63.
The actual original device half-rate, mixing, channel routing, full bank scheduling
and event-to-sound dispatch remain independent runtime dependencies. The merged
SDT reader now reads the unsigned packed rate, byte-sized bit depth/type, raw
sample field and original entry names at their proven 40-byte offsets.
`MP2File` also obtains sample rate/channels from a complete supported first
MPEG frame through the existing decoder parser, preserving supplied container
hints for unsupported or invalid headers. Playback callers use decoded
`Mp2Audio` format metadata. These corrections do not claim the remaining
runtime bridges are solved.


Final codec validation: 49 focused MPEG/LIP tests pass with zero skips on the
provided private root; all 46 standalone advisor-helper checks, 27 Python lane
checks and 16 shared toolkit checks also pass. The linked decoder-only SDK
analyzer build is clean with warnings treated as errors. The full repository
build/test path still emits pre-existing nullable/member-hiding and dependency
audit warnings outside the changed codec. New C# files pass scoped formatting;
existing source LF endings are preserved to keep the diff bounded. The static
Layer I witness gives identical normal/optimized Python output, and the external
PCM report meets the fixed 1-LSB acceptance limit.

## Phase 6: validated compressed-entry format metadata

`MP2File` now reads sample rate and channel count through the existing MPEG
header parser at the declared entry `Header` offset. A supported, complete first
frame overrides legacy container hints; it does not decode PCM or establish
that subsequent frames or payload allocations are valid. The new `Channels`
property reports that header count. Unsupported, truncated or invalid first
headers preserve the supplied container rate and any mono/stereo type hint;
channels are zero when neither is known. Archive listing therefore does not
become contingent on successful decoding.

The existing only constructor caller is `SoundFile.GetFile`; runtime advisor
playback reads its entry bytes directly and uses decoded `Mp2Audio` metadata.
In the identified 40-byte banks, rate/bit-depth/type are packed at entry
`+24/+26/+27`. The legacy `SoundFile` reads signed Int16 followed by Int32
fields, making its type hint unreliable and 44,100 wrap negative. This bounded
fix recognizes MPEG from its validated frame header regardless of that type.
A full packed-container parser correction, and metadata for non-MPEG or
unsupported formats, remain separate dependencies.

A generated 44,100-Hz entry initially failed because the preceding constructor
reported 22,050. Fourteen metadata cases now pass: all supported Layer I rates
and channel modes, supported Layer II rates/mono/stereo, mismatched container
hints and fixed-offset `SoundData`, invalid/truncated/unsupported headers,
invalid entry offsets, and actual private `SfxHD.sdt/keyexplode` (44,100 Hz) and
`speechHD.SDT/sp_001` (22,050 Hz). The private checks compare metadata to decoded
format. The combined focused MPEG/LIP/metadata run passes 63 cases with zero
skips, preserving all preceding 49 codec/LIP checks. No original stream or PCM
bytes are committed; the previous full-corpus PCM comparison remains unchanged.

## Phase 7: sound catalog routes and automatic advisor speech bindings

The new `audio_event_evidence.py` pins the two existing binary identities,
relocations, selected call/field operands and bounded code-range hashes. It
never runs original instructions. Its packed catalog reader derives strides
from the sound library: category 24 bytes at `0x162d8`, sound 20 bytes at
`0x1642c`, event 42 bytes at `0x165fc`, sample choice 16 bytes and child link
8 bytes. Flags, thresholds, event spans and scheduling policies remain opaque.
The reader bounds counts, consumes the entire input and validates one-based
child-element references. It is an evidence reader, not a runtime scheduler.

### RSE sound events use category IDs, distinct from advisor game events

App `0xaf9e8` (opcode 13, `EVENT`) and `0xafa60` (opcode 14, `EVENT_EXT`)
call the effect dispatcher `0xae930`. `EVENT` supplies final control argument
1000. The relocated switch is data `0x3f278`. Types 3–9 submit through audio
wrapper `0xbb4fc`, using the category-handle array at data `0x97f8c`:

| RSE type | Category selected | Handle-array offset | Load / submit, app code |
| ---: | --- | ---: | --- |
| 3 | Local `cat_rides` | +28 | `0xaeba0` / `0xaebc0` |
| 4 | Local `cat_ambient` | +24 | `0xaec04` / `0xaec24` |
| 5 | Global `cat_rides` | +8 | `0xaee54` / `0xaee74` |
| 6 | Global `cat_kids` | +4 | `0xaeeb8` / `0xaeed8` |
| 7 | Global `cat_staff` | +16 | `0xaed8c` / `0xaedac` |
| 8 | Global `cat_ambient` | +0 | `0xaed28` / `0xaed48` |
| 9 | Global `cat_ui` | +12 | `0xaedf0` / `0xaee10` |

Global names are grounded in registration `0xbc6f0–0xbc864`; local registration
`0xbc864–0xbc9a0` places ambient/rides/speech/music at +24/+28/+32/+36. Type 10
at `0xaec2c` resolves the current thing's custom bank and submits it at
`0xaecd4`; failure reaches the missing-custom-bank diagnostic at `0xaece0`.
Types 1/2 take separate non-audio branches; type 0 is the unsupported/default
route. These are numeric dispatch meanings, not invented event aliases.

`SPAWNSOUND` stores the child script ID at parent script +20 (`0xb12a0`).
The sound-variable accessor `0xb5c5c–0xb5da4` resolves parent and child IDs,
checks variable index against child +140 and reads child variable-array +28.
Wrapper `0xbcaf8` passes its variable-index argument to that accessor at
`0xbcb20`; a zero result suppresses submission at `0xbcb28`, otherwise it
passes the resolved catalog ID to `0xbb4fc` at `0xbcb40`.

Train initialization `0x3bb6c` reads sound-child indices 5–8 into train
+84/+88/+92/+96 and creates handles at +100/+104 through `0xbcaf8`, with
indices 0 and 1 respectively. Train identity is established by the rides lane's
caller `0x3efd0`: it passes the train in r4 after a separate car loop of stride
96; sound refresh `0x3c21c` advances train records by 128. These train sound
fields must not be confused with car +84/+88/+92 projected coordinates.

The SDK-only asset command below uses existing `WadArchive`/`RideScriptFile`
readers and parses all 28 supplied baseline EventMaps without running scripts.
It writes only interpreted variable indices, SET literals and identities to
an external report. For baseline `b_drip`, `c_hade` and `coaster1`, indices 5–8
are `VAR_PAR0–3`; for the bumper maps they are `VAR_EVT5–8`. Variable indices
therefore cannot receive universal event names. The three coaster `VAR_EVT0`
values 145/175/204 identify local catalog records with 6/6/5 elements; their
sample choices reference bank IDs 3/2/3. Sound-library `ReadSounds` rebases
that field through its bank-fixup array at `0x16af4–0x16b04`. Resolving those bank IDs requires the
native bank remap state before attaching SDT clip names. Simply indexing the
main RideHD bank would yield the wrong names.

```sh
/Users/sander/.local/share/opentpw-dotnet/dotnet run --project tools/ppc-analysis/lanes/advisor/audio-events/AudioEventAssets.csproj --configuration Release -- /Users/sander/server/game-assets/theme-park-world/Data /tmp/advisor-event-map-corpus-validated.json
python3 tools/ppc-analysis/lanes/advisor/audio_event_evidence.py /Users/sander/server/game-assets/mac-feral/bin --assets /Users/sander/server/game-assets/theme-park-world/Data --event-maps /tmp/advisor-event-map-corpus-validated.json > /tmp/advisor-audio-event-evidence.json
```

The bounded reader consumes all 31 loose supplied SFX catalogs exactly,
covering 1,267 catalog IDs. A sample-choice bank ID is its packed field +12
(sound placeholder `0xf5d0`), while the sample ID is +0 (`0xf5e8`); bank lookup
subtracts one from a sample index at sound code `0x6fa4`. This establishes
one-based SDT member indexing only after the bank is resolved.

### Local music uses catalog 2, with a multi-sample bank definition

App `0xbc144` submits catalog ID 2 through local music handle-array +36 at
`0xbc174`, then calls the audio interface parameter wrapper `0xbaf70` with
control 4 and argument 0 at `0xbc188`. It is called during main-loop transitions
at `0x1c1c40`. During updates, `0x1c246c` calls `0xbc1d4`, which applies
control 4 with the supplied argument to the retained music handle. Exit
`0x1c2994` calls `0xbc1a4`, passing that handle to `0xbba58`. Complete
play/stop/fade semantics of those interface/control operations are not
established here.

The supplied fantasy `cat_musicSFX.map` contains exactly catalog ID 2, with 89
sample references across its elements. Every reference selects bank 1 and is
within the supplied 89-entry music bank. `cat_musicBANK.map`, whose 11-byte
record stride is grounded at sound code `0x15194`, names family `Music\Music`.
The first listed reference is sample 44, `Level1-a.mp2`, in `Music/MusicHD.sdt`.
Catalog selection, random thresholds, sentence chaining and playlist ordering
are separate mechanisms; these records do not prove a simple sequential or
uniform-random playlist.

| Selected asset | SHA-256 |
| --- | --- |
| Fantasy `Music/cat_musicSFX.map` | `bdd080f8bece1d03df33cf2868ef82d4433b62543f0994b9d24e59fefda8b65e` |
| Fantasy `Music/cat_musicBANK.map` | `ad45ce6ab74cef5c284f2ae6e7be5780d99a3e9fae1608990672b305528328b4` |
| Fantasy `Music/MusicHD.sdt` | `6d35cd515ba59027be57d492eb9803554e1ff51ffc231381a5f09e0ca07a0852` |

### Actual CMsgEvent producers reach selected speech and LIP records

`CMsgEvent` constructor `0x116528` installs RTTI table data `0x408a4` and
stores its event-ID argument at object +8 (`0x116540`). Advisor reception reads
that field at `0xad40` and calls `0x94dc` at `0xad48`. Its switch table is
**data `0x1e0f4`**, independent of the RSE sound switch and catalog namespace.
The table routes IDs 0/2/3/4 to concrete advice records:

| CMsgEvent ID | Pending advice ID / construct call | Configured score field | Descriptor response IDs | Bank / speech and LIP IDs |
| ---: | --- | --- | --- | --- |
| 0 | 0 / `0x9548` | `Welcome.Score` | 1 | Local / 1 |
| 2 | 106 / `0x98d4` | `Bankrupted.Score` | 274, 275 | Global / 424, 425 |
| 3 | 128 / `0x9a88` | `ParkNowOpen.Score` | 308, 309 | Global / 342, 343 |
| 4 | 129 / `0x9c3c` | `ParkNowClosed.Score` | 310, 311 | Global / 344, 345 |

Rows follow the existing 351-descriptor table data `0x1f2b4` into the
610-response table data `0x18ff4`; they identify candidates, not unconditional
playback. Eligibility, queue score, busy-action and cyclic response rules
still apply. Event 0 also constructs advice 323 when the global mode value is
2 (`0x96f4–0x9720`); its `PrebuiltPark.Score` descriptor resolves response 587
and global speech/LIP 606. Event 10 clears history, as established earlier.

Concrete producer constructor calls are `0xcc464` (ID 2), `0x108fd4` (ID 3),
`0x109118` (ID 4), `0x104d2c` (ID 0), and main-loop `0x1c2108` (ID 10)
followed by `0x1c2174` (ID 0). In `0x108ee4`, the ID 3 branch changes game
field +0x1da710 from nonzero to zero at `0x108f50`; the ID 4 branch changes
it from zero to one at `0x109044`. The matching score properties corroborate
park open/closed messaging, while transition preconditions and the financial
threshold at the ID 2 producer remain owned gameplay proofs. No RSE audio
callback is established as a producer of these advisor messages.

Verification: eleven new synthetic catalog/bank bounds and reference cases pass,
bringing the lane Python total to 38. The native operand/relocation witness and
31-catalog corpus scan pass. The C# asset command parses all 28 EventMaps; the
standalone advisor helpers retain all 52 passing cases. No original code,
compressed audio, PCM, script bytes or disassembly is committed. Remaining
runtime blockers are category/bank remap state, event element selection and
parameter semantics, full music sequencing, environmental priorities/voice
limits, plus game-message/score and pause-aware clock integration.

## Phase 8: category bank ordinals resolve to SDT members

`bank_remap_evidence.py` advances the earlier bank-remap dependency with
identity-pinned native operands and actual selected PC/Mac assets. The transient
map belongs to a **category registration**, while the loaded-bank registry is
shared. A catalog ID, a serialized bank ordinal, a global loaded-bank index,
a sample ordinal and an SDT entry name are separate identities.

### Native registration and fixup lifetime

| Native sound-library evidence | Established role |
| --- | --- |
| `0x14bc0 → 0x15d4c → 0x157d0 → 0x15100` | Register category BANK definitions before reading SFX definitions. |
| `0x15194`, `0x15208` | Bank records occupy 11 bytes and are processed in stored order. |
| `0x151dc`, `0x151e8`, `0x151cc` | Reset serialized handle/cache fields and replace the serialized string pointer. These stored pointer-shaped words do not supply an SDT filename or a stable runtime handle. |
| `0x15b8c–0x15b94`, `0x15c24–0x15c30` | A newly registered bank takes the next logical counter at streamer +52 and stores its global registry index in vector +44. |
| `0x15590–0x15598`, `0x15628–0x15638` | A reused loaded bank also takes the next logical counter and appends its existing registry index. Reuse does not collapse or skip a BANK-file ordinal. |
| `0x14c00 → 0x161c0 → 0x15f00 → 0x162bc` | Read the SFX catalog after the logical bank map has been built. |
| `0x16a58`, `0x16af4–0x16b04` | Read the packed sample-choice bank at +12; for nonzero values, index `bankOrdinal - 1` into streamer +44 and overwrite that field with the global registry index. |
| `0x14c10–0x14c34`, `0x14fa0–0x14fc8` | Free/reset the transient vector, capacity +48 and logical counter +52 after registration. The lifetime is BANK load → SFX fixup → temporary-map teardown. |
| `0xf5d0`, `0xf5e8` | The placeholder resolves the rewritten bank while retaining sample ID at choice +0. |
| `0x6fa4` | `TbMMFileBank::GetSamplePosition` subtracts one before indexing its entry-position array: sample IDs are one-based. |
| `0x6a64` | Identified `TbFileBank::GetSampleName` returns zero. This path does not establish playback by stored SDT name. |

Bank zero is a native special branch that skips this remap. The bounded resolver
rejects zero or out-of-range ordinals instead of silently assigning the first
bank. Registration success, alternate-root flags, filename quality selection
and mixer/device policy remain separate dependencies. The corpus command takes
`HD.sdt` explicitly; matching that supplied family is not proof that every
original runtime always chooses HD data. Selected BANK flag bytes contain no
0x20 alternate-root route, which the bounded corpus check refuses to invent.

### Actual corpus resolution

The existing BANK reader bounds every path string, record count and end of
input. `bank_records` keeps file order and registration flags while ignoring
serialized cache/pointer fields. Each SFX sample choice first resolves its
BANK-file ordinal, then its SDT sample ordinal. Names are preserved exactly as
stored in the fixed 16-byte SDT name field; no suffix repair, prefix matching,
case folding or duplicate-name collapse is applied to entries.

| Corpus | Catalogs | Logical BANK records | Referenced SDT paths | Resolved sample choices |
| --- | ---: | ---: | ---: | ---: |
| Supplied PC baseline `Data` tree | 31 | 53 | 47 | 3,631 |
| Selected Mac HFS copies: global UI and fantasy/hallow/jungle rides | 4 | 13 | 13 | 1,105 |

The UI lane copied 21 original Mac data forks read-only to
`/tmp/ppc-advisor-mac-Data`, preserving case and recording source paths,
lengths and SHA-256 outside Git. The HFS image remained unchanged at SHA-256
`46edf2f94ce9a36834f7760ef3e3852e623863a8a8ef99ef629872b15d599365` and was
unmounted afterward. No HFS global state was modified by this advisor lane.
The four selected BANK/SFX pairs are byte-identical between these supplied
PC and Mac copies; their resolved choices agree. This is selected asset
correspondence, not Windows executable or device parity.

| Catalog context / ID | BANK ordinal / family | SDT ordinals / actual stored names |
| --- | --- | --- |
| Global `cat_ui` / 31 | 1 / `Sound\sfUi` | 10 / `BUTTON01.mp2` |
| Fantasy `cat_rides` / 145 | 3 / `Sound\xRide` | 15–20 / `dull_crmbl1.mp2`, `dull_crmbl2.mp2`, `dull_crmbl3.mp2`, `dull_crmbl5.mp2`, `dull_crmbl6.mp2`, `dull_crmbl7.mp2` |
| Hallow `cat_rides` / 175 | 2 / `Sound\xRide` | 8–13 / `mt_crmbl1.mp2` through `mt_crmbl6.mp2` |
| Jungle `cat_rides` / 204 | 3 / `Sound\xRide` | 8–12 / `wd_crmbl1e.mp2`, `wd_crmbl2e.mp2`, `wd_crmbl3e.mp2`, `wd_crmbl5e.mp2`, `wd_crmbl7e.mp2` |

Fantasy and jungle list banks `[Ride, sRide, xRide]`; hallow lists
`[ride, xRide, sRide]`. Thus bank 2/3 have context-dependent meanings. Likewise,
Jungle catalog 145 resolves to `Sound\Ride` sample 217, `fallz2.mp2`, while
Fantasy catalog 145 resolves to the six xRide members above. A number alone
cannot receive a global event or clip alias.

Stored names also cannot replace numeric identities: global `UIHD.sdt` has
two entries named `tp_balloon_pop_`; Jungle `AmbientHD.sdt` has two each named
`TP STRANGE DEEP` and `TP STRANGELY DE`. They remain distinct ordinal/offset
records even when their truncated strings coincide. The current production
SDT reader's packed-field/natural-name fixes were integrated separately; this
lane adds no gameplay or archive-lookup changes.

### Role in EVENT and SPAWNSOUND

The recovered RSE `EVENT` category selector chooses local/global registered
categories as shown in phase 7. Its catalog ID selects a sound definition;
that definition's sample choices use the category BANK map before reaching
loaded-bank/sample positions. `SPAWNSOUND` loads the child script; its variable
getter and wrapper resolve a variable to that catalog ID, with zero suppressing
submission. Neither operation supplies an SDT name directly. The mappings above
now attach concrete names to selected numeric choices without treating the
EventMap variable, sound catalog record and SDT ordinal as interchangeable.

```sh
OPENTPW_PPC_BIN_ROOT=/Users/sander/server/game-assets/mac-feral/bin OPENTPW_PC_DATA=/Users/sander/server/game-assets/theme-park-world/Data OPENTPW_MAC_DATA=/tmp/ppc-advisor-mac-Data python3 -m unittest discover -s tools/ppc-analysis/lanes/advisor -p 'test_*.py' -v
python3 tools/ppc-analysis/lanes/advisor/bank_remap_evidence.py /Users/sander/server/game-assets/mac-feral/bin --pc-data /Users/sander/server/game-assets/theme-park-world/Data --mac-data /tmp/ppc-advisor-mac-Data > /tmp/advisor-bank-remap-pc-mac.json
```

All 48 lane Python checks pass with the selected fixtures. Ten new cases cover
record order, ignored cache words, bank-before-sample resolution, duplicate
truncated names, invalid ordinals/paths/quality suffixes, context-specific IDs
and actual native/PC/Mac operands and mappings. Confidence is high for these
identities and bounds. Phase 9 below recovers selected element weighting and
parameter consumers. Exact filename quality/root policy, environmental priority and audio
output scheduling remain unimplemented dependencies; no PCM-clock LIP bridge
or original runtime parity is inferred.

## Phase 9: loaded weights, selection state and sound parameters

`sound_selection_evidence.py` adds identity-pinned operands and bounded pure
selection algebra. It accepts a seed snapshot, explicit descriptors and
parameter values. It does not advance shared random state or schedule output.
All addresses below are code section 0 or explicitly named data section 1 in
the same identified Mac sound PEF used above, with TOC `0x8000`.

### Disk weights are not always runtime weights

`IsSFXHeaderValid` reads header word +16 at `0x15e60`; zero sets streamer byte
+60 to one at `0x15e70–0x15e7c`. `RegisterSFXData` repeats that conversion at
`0x15f98–0x15fac`. `ReadEvents` (`0x165b0`) then visits the loaded 42-byte
event array. With that flag set, `0x16654–0x16660` replaces event word +30
with `(currentStored - previousStored) mod 2^32`, initially previous zero.
With the flag clear it retains the stored word. Child event references are
linked afterward, so branch selection also sees the converted weights.

The supplied PC tree contains 31 SFX catalogs: 26 request this differencing,
while the four level music catalogs and global speech catalog do not. All
four selected Mac UI/ride catalogs request differencing and remain
byte-identical to their selected PC copies. For Fantasy catalog 145,
stored values 10922, 21844, 32766, 43688, 54610 and 65532 become six weights
of 10922. For Jungle catalog 204, five cumulative values become five weights
of 13107. Directly summing the serialized cumulative values would change
the native selection behavior.

### Base and branching selection consumers

| Consumer | Native operands and behavior |
| --- | --- |
| `CAudioPlaceHolder::ChooseRandomSound`, `0xff40` | Event count at sound +4, array +8, stride 42. Sum loaded event words +30 in unsigned 32-bit arithmetic; choose first sum **greater than or equal to** candidate high 16 bits (`0xffb8–0xffc4`). Exhaustion selects the first event at `0x10130`, preserving previous-index history and skipping parameter-selector refresh. |
| `CAudioPlaceHolder::ChooseRandomSample`, `0xfcb4` | Sample count is event word +0 low 16 bits, array +8, stride 16. Choose first sample threshold word +4 **greater than or equal to** candidate high 16 bits (`0xfd10–0xfd18`). Exhaustion returns null (`0xfd9c`), without history update. The sample threshold is not differenced by the event-weight conversion. |
| Base singletons | A count of one selects the first array entry directly. It bypasses random arithmetic, previous-index storage and, for events, parameter-selector refresh. Sample count zero returns null. The helper refuses an empty event array instead of modelling the native first-pointer fallback as a valid record. |
| Base anti-repeat | Only when the boolean is enabled and count is **greater than two**, equality with signed-byte history increments the selected index and takes unsigned modulo count. Event history is object byte +80 (`0x10010`, `0x10058`); sample history is +81 (`0xfd44`, `0xfd88`). This is a next-entry substitution, not another random draw. |
| `CPlaceHolderBranchingSentence::AssignSoundToNextBranch`, `0x192f0` | The current event's linked-child array is at +38, count +4, stride 8. Link bytes +6/+7 are inclusive low/high bounds for the supplied unsigned branch parameter (`0x19364–0x19378`). Sum the eligible linked events' loaded weights +30, take the full candidate modulo that total, then choose first eligible cumulative sum >= remainder (`0x1939c–0x19418`). |
| `CPlaceHolderOneShotBranchingSentenceElement::ChooseRandomSound`, `0x18028` | Reads the parent parameter byte from parameter structure +4, filters the same inclusive link ranges (`0x180c0–0x180d4`) and weights the linked events (`0x180e0`, `0x1815c`). It updates both its own and the parent's selected event pointer. |

These are selected class consumers, not a claim that every EVENT uses the base
class. Linear sentences, shuffles, droppable conversion and class selection
require their own dispatch evidence. A zero eligible weight sum reaches an
unsigned division dependency in the native branch; the pure helper reports
that unsupported input instead of inventing a fallback. Base index models
are bounded to at most 127 choices because native index temporaries/history
are signed bytes. That is a helper boundary, not a proven native rejection.
Zero weights/thresholds can select on a zero draw because equality is accepted.

### Random-state ownership boundary

All nine direct TOC address constructions for sound data `0xc2e4` occur at
`0xf404`, `0xf548`, `0xf764`, `0xf988`, `0xfce0`, `0xff80`, `0x118c8`,
`0x180f8` and `0x1939c`. The selected draw blocks read this shared sound-module
word and calculate

```
candidate = (seed * 1664525 + 1013904223) mod 2^32
```

The base event/sample choices use its high 16 bits. Volume, pitch and branch
choices use the full unsigned candidate modulo their range/weight span.
The draw blocks do not store the successor back into shared state. One pitch
fill variant spills the unchanged seed to its stack; that is not advancement.

Initializer `0x118b4` calls imported `LbTime_GetClock` at `0x118c0` and stores
its return into that word at `0x118cc`; the static constructor list calls it
at `0x3c`. Thus this selected RNG state belongs to the sound module and its
initial value comes from the clock, separately from the game's advisor-mouth
random call. Data relocation slot `0x348` also points to the seed word;
its indirect use has not been recovered. The witnesses establish direct
consumers and initializer, not a complete absence of indirect writers or an
original runtime sequence. Do not turn these pure functions into an advancing
per-draw RNG without finding a native state writer.

### Volume, pitch and parameter codes

| Packed event field | Proven selected consumer |
| --- | --- |
| +12/+13, unsigned bytes | Volume range, `GetRandomVolume` at `0xf32c`. Reversed bounds are swapped in the loaded event. With no matching parameter: `low + candidate % (high-low)`, or low for equal bounds. The high endpoint is excluded for a nonzero span. Missing event returns 100. |
| +14/+15, signed bytes | Pitch-index range, `GetRandomPitch` at `0xf45c`; explicit sign extensions `0xf48c`, `0xf490`. Same swap/range rule, with missing event returning zero. Stored byte 232 means −24. |
| +22/+26, unsigned shorts | External parameter selector codes. Normal base event selection loads them then truncates into parameter structure byte +1/+2 (`0x100d8–0x100ec`), resetting +3 to zero. These codes are not volume/pitch values or globally established EventMap names. |
| +24/+28, unsigned shorts | Destination masks. `GetParameterValue`, `0xf2e0`, first tests +24 against requested bit and returns parameter byte +5; otherwise +28 can return +6. First matching mask wins. Volume requests bit 1 at `0xf3bc`; pitch requests bit 2 at `0xf4fc`. The sentence-element override `0x187fc` reads those value bytes from its parent. |

`UpdateParameter` (`0xe9c0`) truncates incoming code and value to bytes and
updates **all** matching slots among four selector/value pairs. A match in
slot zero alone does not request dependent recomputation. Other matches call
the dependent-update virtual slot unless object flag `0x4000` inhibits it
(`0xea94–0xeaac`). No inferred speed/distance label is attached to codes 19/20.

When the requested parameter exists, volume/pitch use
`low + floor(((span * parameter) mod 2^32) / 100)`. At parameter 100 this
includes the high endpoint, unlike the random range. Values are byte-sized
on the proved storage path but are not clamped to 100 by these consumers.
`UpdateParameterDepandants` (`0xead4`) requires an active handle and manager;
it passes volume to `TbSoundSampleInfo::SetVolume` at `0xeb54→0x9400` and
pitch to `SetPitch` at `0xeb6c→0x9428`. Flag `0x400` inhibits its volume
recalculation. Sample-info setters store volume word +28, pitch word +36,
and the distinct `SetFrequency(float)` API stores float +32 at `0x943c`.

The exported `TbSoundSystemModule::ConvertPitchIndexToFrequency` (`0xbbf8`)
has a zero special case of 1.0. Its positive branch adds one to the index,
divides by float 96 and calls MathLib `pow(2, exponent)`; its negative branch
uses `(1-index)/96` and returns the reciprocal. The mathematical ratios are
therefore `2^((index+1)/96)` for positive indices and
`2^((index-1)/96)` for negative indices. Constants are data +0x668 (float
96), +0x670 (double 2) and +0x678 (float 1). This API evidence does not
establish every mixer call site, absolute sample frequency, MathLib bit-exact
rounding, or audio-device output parity. The pure expression test is an
algebra check, not an original device test.

### Selected corpus and verification

| Corpus | Sounds / events | Sample choices / child links | Variable volume / pitch ranges | Multi-sample arrays ending below 65535 |
| --- | ---: | ---: | ---: | ---: |
| Supplied PC baseline, 31 catalogs | 1,267 / 1,595 | 3,631 / 3,306 | 180 / 238 | 201 |
| Selected Mac copies, 4 catalogs | 336 / 400 | 1,105 / 277 | 31 / 46 | 73 |

These last-column arrays have a concrete possible high-draw exhaustion
dependency in the selected sample chooser; no fallback or normalization is
invented. Both supplied sets have zero nonmonotone sample-threshold arrays.
Fantasy 145 uses volume 17–85, pitch indices −24–36 and selector
19 with mask 3, so the same external byte can interpolate both ranges.
Jungle 204 has those ranges with selector 20/mask 3. Global UI 31 remains
volume 100 and pitch zero. Catalog context and IDs retain the identities
from phase 8; these parameter codes do not supply new event names.

```sh
python3 tools/ppc-analysis/lanes/advisor/sound_selection_evidence.py /Users/sander/server/game-assets/mac-feral/bin --pc-data /Users/sander/server/game-assets/theme-park-world/Data --mac-data /tmp/ppc-advisor-mac-Data > /tmp/advisor-sound-selection.json
OPENTPW_PPC_BIN_ROOT=/Users/sander/server/game-assets/mac-feral/bin OPENTPW_PC_DATA=/Users/sander/server/game-assets/theme-park-world/Data OPENTPW_MAC_DATA=/tmp/ppc-advisor-mac-Data python3 -m unittest discover -s tools/ppc-analysis/lanes/advisor -p 'test_*.py' -v
```

All 67 advisor Python cases pass with the supplied binary/PC/Mac fixtures,
with zero skips. The 19 new cases cover loader differencing/wrap, threshold and weight equality,
exhaustion, singleton bypasses, anti-repeat, inclusive branches, zero-divisor
dependencies, signed pitch, reversed bounds, random/parameter endpoints,
byte truncation/masks, frequency algebra, native operands and private PC/Mac
metadata. Confidence is high for the selected static consumers and supplied
record interpretations. Indirect RNG writers, complete class dispatch,
parameter producers, scheduling and device behavior remain separate handoffs.
