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
object fields `+56/+60/+64` receive `(0.6, −0.6, 0.2)`, an associated structure
receives four 1.0 components at `+4/+8/+12/+16`, and its `+44/+48/+52` fields
receive `(26.666667, −26.666667, 800)`. It calls imported `CSystem::UpdateLights`
at `0x290dc` and `0x29108`. Scale-vector construction at `0x29208–0x29244`
passes `(0.01125, 0.001, 0.015)` to `0xa2850`; the first component is the product
of 0.015 and 0.75. MapWho linking uses ID 16 (`0x29200`). These are concrete
geometry/lighting leads; treating either vector as an advisor camera location
would be unjustified. Viewport placement, projection, front-face convention and
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
| ADVISOR-014 | No new coefficient-table provenance proof; original decoder existence does not validate every clean-room synthesis coefficient |

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
