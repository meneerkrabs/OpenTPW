# Sound: catalogues, mixer and park music

October 18, 2026. Status: the original sound catalogues are read, a software mixer plays through SDL, and every sound the
original submits through its sound entry (`0x51bfc0`) and its placed twin (`0x51c130`) that can be read statically is
played, bar the few listed under "Still not played" below. The park plays its original music; the front end its lobby
music and ambience; controls click (including the pause menu's backdrop); the map/research drawer, error feedback, the message
tube, the postcard and the research window play their cues; the build tools play their size-classed placement and demolition cues and
their click sounds (path, queue, coaster and kart/water track tool); ride scripts play their EVENT and REPAIREFFECT sounds
and screams through the positional API (the census over 262 shipped objects and 308 scripts finds every node key resolving);
coaster trains that bump hard crash; the weather plays its rain and thunder; the level's placed ambients (`scape.omp`, types 1 and
3) play with the mixer's distance law; the crowd sentence, the guests' and the staff's voices (walking, idle and work sounds from their
state machines), the visitor click, the queue-joins-a-path cue and the advisor's speech and its two key sounds play; and the positioned
effects go through a software reverb that follows the ridden ride's `SndReverb.map` preset (OpenTPW's own stand-in by design, `[EXT:audio-reverb]`). A button plays `cat_ui` 0xBD instead of 0x1F when its parent has style bit 0x10 (AUDIO-004 is retired; see "Triggered sounds"). Ride-script sounds of type 10
play from the object type's own sound list ("The object type's own sound list (EVENT type 10)"; no shipped script uses it). No list row clicks: every list owner of tp.exe is traced and none turns a row into message 0x100; a still click on a scroll thumb plays nothing; and the keyboard clicks only where a dialog turns Enter in its text field (or the save screen's Escape) into its tick ("List owners"; AUDIO-041 is retired). The slap reactions 639–641 of `cat_speech` are played by the
cut-off path in `AutomaticAdvisor.CutOffSpeech`: withdrawing a help message while its speech is still playing cuts the speech off and plays
one of the three (`cat_speech` 0x27F + `rand() % 3`; tp.exe `0x5994E0`/`0x5995A4`;
`AdvisorHelpTests.ACutOffSlapsOnlyWhileTheSpeechPlays`). No original data is in the repository.

The integration regressions force a rainy storm, pass its rain level and due thunder
through the live audio callbacks, and assert nonzero decoded PCM. They also exercise
staff turn gating, a flat single scream away from the world origin, terrain-to-listener
coordinate conversion, and ride removal with both held and refused scream chains.
These checks require `OPENTPW_GAME_PATH`; they use a mixer without an SDL device.
The earlier native front-end and jungle smoke runs did not reach rain, thunder or
staff chatter, so those runs do not verify their natural gameplay triggers. The
weather cadence, staff presentation stream and held-chain timing
retain the qualifications in the approximation register. The second static research round over
tp.exe (October 14) settled or narrowed most of the open items; see "Register round 2" below.

## Placed sound EVENT origins

Placed objects route sound EVENT types 3-9 through the positional audio API. The key operand
(the second) picks the anchor, as the original's `0x556b90(object, &position, 0, key, 0x200)` does
for every sound type (`0x5575c7` on; tp.exe):

* a **non-negative key** is the model node of that key, looked up with selector `0x200` (the particle
  resolver's selector is `0x100`; both use `ParticleKeyResolver`), at the node's current animated
  position (node `+0x30/+0x34/+0x38`), sampled when the instruction executes, undoing imported
  heightfield centering and converting engine axes and scale through `GameAudio.SoundPosition` to the
  sound space `UpdateListener` uses;
* a **negative key** is the object's footprint middle (`0x556d42`; `0x466b70` reads the footprint
  rectangle, scaled by 10 with a one-cell offset): the middle of the rotated bounds of its shape,
  `(min + max + 1) * 5` per axis, at the object's base height. A fixed item (authored in park
  coordinates, no footprint) uses its model origin;
* **census**: across every shipped ride of the four themes (262 objects, 308 scripts; `EventKeyCensusTests`, run with the
  game data), the sound EVENTs of types 3-9 have 339 negative keys (the footprint middle), 17 keys >= 0 and no variable
  keys; all 17 resolve with selector `0x200` against their own model (0 missing, 0 unsupported), so no shipped ride loses a
  sound. A node without a matrix (flags 0x30 clear) plays at the footprint middle (the original reads a zero default,
  the park's corner, which OpenTPW does not copy);
* an **unknown key** logs `RSSE: Invalid Node ID` (`0x556d2c`), returns 0 and plays **nothing**
  (`0x5575e5`). OpenTPW used to fall back to flat.

Headless runtimes (no placement) and a nonfinite result keep the flat path. Types 0, 1 and 2 never
sample an audio origin or produce sound through this route. Type 10 does in tp.exe (see "Custom-bank EVENT type 10" below), but no shipped script uses it. The dispatcher truncates the three
coordinates to whole units before the submit (`__ftol` in `0x5573d0`, as the REPAIREFFECT sounds do), and
OpenTPW does the same; the footprint middle's height is the root node's `+0x44`, the object's own height
that `0x556a80` also reads, which is the base height here (AUDIO-908 is retired).

The flag `0x8` loop applies to every one-shot, positioned or flat (the loop test `0x6bc621` follows both
branches; AUDIO-019 is retired), so formerly flat placed calls loop too. Returned loop voices belong to
the object runtime, including events from its children, and stop when that runtime
stops or the object is deleted. Park teardown deletes the park's objects and follows
the same cleanup. One-shots keep their existing mixer lifetime. This adds no new
audio handle encoding or controller rule and does not stop another object's voices.

An alive owned looping EVENT or repair-hide voice makes runtime/world-save capture refuse with a clear
reason: live audio persistence is unsupported. One-shots, finished voices and
headless/no-audio or stopped owners remain eligible. No save schema changes or
audio continuation/parity claim are introduced by this eligibility restriction.

Synthetic tests use a valid dummy-only model with no render parts, actual placed
objects, the real VM/effects path and the real mixer with synthetic decoded PCM.
They check every routed type, coordinates/range, the footprint middle for every quarter turn and a
multi-cell shape, the node key and the silent unknown key, flat refusal controls, per-call
sampling and loop ownership. Original assets, GPU behavior and native emitter
geometry/timing remain unqualified.

## Placed repair sounds

tp.exe's REPAIREFFECT handler (`0x5562A6`) switches the type's repair effect first (`0x556DE0`, a non-zero value starts it, 0 ends it), then compares the value with 0 (`cmp esi, edi; je 0x55635F`
at `0x5562C5`). **A non-zero value** submits the traced global `cat_rides` events 111 (`0x6F`) and 211 (`0xD3`) using
one sample of the object's origin (the placement/render transform of model-space zero, the
original's object position `0x556a80`, each coordinate through `__ftol`). The existing `PlayAt` distance/pan and flag-`0x8` loop policy
applies independently to each returned voice. Live pair loops join the existing exact-runtime ledger, so
Stop, object removal and root-fault containment stop only that owner's loops.
They trigger the same narrow live-audio capture refusal; one-shots, finished
loops, missing audio and flat/headless fallback do not add a save restriction.
No audio handle or save schema is introduced. If the second submission fails,
only a partially submitted first loop is stopped before the original exception
propagates; a first one-shot keeps its existing lifetime.

(Until October 17, 2026 the port had the two paths the other way round, from a Mac reading in which the pair was the "hide" path and the band sound the "show" path, and it attributed `0x55635F` to the value 1.
The PC compare is the authority: the two blocks are the same on the Mac, the pair first and the band sound at `0x100B1F78`, so the polarity of the port was inverted, not the sounds.)

**The value 0** jumps to `0x55635F` and plays a `cat_kids` one-shot at the object too
(`0x551320(band, 1, 1, x, y, z)` with the object's position): with more than five variables, the band is `[[vm+0x1c]+0x14]`, i.e. the
script's own variable slot 5 (the array at `[vm+0x1c]` holds the script variables, `0x556408`; the standard ride scripts
keep `VAR_ONRIDE` there), and picks the family (band 1 -> 75, 2-3 and negative -> 79, 4-7 -> 83, >= 8 -> 87, 0 silent;
index `(a + b) / 50` capped at 3, so 0 here). `GameAudio.RepairBandEventForBand` does the mapping. The script ends a repair with
`REPAIREFFECT 0` while nobody is aboard, so that sound is silent in play. The
public `GameAudio.PlayRepairEffect(int show)` API retains its flat behavior (band 1).

`RepairHideAudioTests` exercises actual placed root VMs, imported-terrain
centering, listener/mixer equivalence for both IDs, per-pair sampling, missing or
nonfinite origins, show controls, owner/fault teardown and narrow capture policy.
Only existing managed PCM and synthetic model/catalog data are used. Loop positions remain the sampled
instruction-time positions, rather than following later provider movement.

## Held ADDOBJ sounds

An `ADDOBJ` or `ADDOBJ_EXT` of type 3-9 starts its event like an `EVENT` of that type and keeps the sound under its slot, so a script can end it
(`KILLOBJ`, `FADEOBJ`), give it a parameter (`SETOBJPARAM`) and have the object pass move it (every eighth scheduler pass, keyed records only).
`GameAudio.StartHeldRseEvent` starts it through `SoundEventSystem.PlayHeld`: a repeating event (flags 0x4) as a positioned `SoundSentence`, a one-shot as a `PlacedSound` that starts
when its sample is decoded (flat without a placement); `HeldSound` is the handle the runtime keeps (`MoveTo`, `SetParameter`, `Stop`). The parameter call on a placed one-shot
is the sentence's (`PlacedSoundParameters`: the code's value byte into the voice's table, re-evaluating volume and pitch through slots 1-3). The records, the opcodes' tp.exe evidence, the
save and resume decision (nothing is saved, as the original's loader starts no sound) and `FADEOBJ` (quality-controlled released tails, `APPROX:AUDIO-040`) are in
[RSE-VM.md](RSE-VM.md#the-sounds-an-addobj-keeps). Tests: `HeldSoundTests`, `HeldSoundCorpusTests`, `HeldSoundFadeTests`; [fade lifecycle contract](proposals/audio-040-plan-soundquality-fade-mode.md).

## RSE particle events are outside the sound service

Static inspection of the identified Feral Mac application corrects the earlier
"positioned loops" label. `EVENT type key effect` passes its first, second and
third operands to dispatcher registers r4/r5/r6 (`0x100AF9DC–0x100AF9E8`).
The dispatcher preserves the key in r26 and the effect in r27. Types 1/2 resolve
the key through `0x100AC7F0` with selector `0x100`; a failed lookup returns before
creating an effect. This is a model-key selector, not a sound-catalog ID.

| Type | Dispatch branch / constructor | Data supplied |
| ---: | --- | --- |
| 1 | `0x100AE97C` / `0x1009FD70` | Effect ID and three position coordinates |
| 2 | `0x100AEA58` / `0x1009F968` | Effect ID, three position coordinates and a three-component direction vector |

Both constructors use the same template/pool globals as the particle tick
`0x1009F748` and particle shutdown `0x1009F354` (whose diagnostic is prefixed
`PTCL:`). They bound effect IDs to 0–104, copy a 320-byte template into an
allocated pool slot and return a generation/slot handle. They do not call the
sound submit wrapper `0x100BB4FC`. The existing `ParLib.P_EFFECT_*` names are
consistent with this classification, but names alone are not the binary proof.

The dispatcher multiplies position floats by 1024 and truncates to integers;
the constructors arithmetic-shift those coordinates right by four. Type 2's
second vector is normalized/scaled by the key resolver and initializes velocity
using the template's +0xB0 value. Effect bit `0x8000` selects an object-scoped
lookup (`0x10118018` / `0x10119EF4`) with the low 15 bits, rather than a direct
particle template ID. `EVENT` supplies control 1000; `EVENT_EXT` supplies its
fourth operand and non-1000 values reach `0x1009F5E8`. That control's meaning,
the scoped lookup semantics, complete key transforms, particle lifetimes and
rendering remain unimplemented/unqualified. Do not reinterpret these as audio
volume, loop flags or SDT member IDs. TPI particle parity is not established.

`GameAudio.RseEventCategory` and `FindRseEvent` intentionally return null for
1/2, even when an audio catalog contains a numerically matching ID. The generic
object-effects hook no longer counts them as unimplemented effects (since October 18, 2026, EVENT types 1-2 are served by the particle system); the particle side is described in [PARTICLES.md](PARTICLES.md): a
prepared EVENT 1/2 request (the owner-local preparation in
[PARTICLE-KEYS.md](PARTICLE-KEYS.md#latest-owner-local-event-preparation))
starts an effect in the level's own particle pool. No fake sound or audio
approximation ID is introduced, and a sound voice handle cannot represent a
particle pool handle (the dispatcher discards it).

Reproduce the static evidence without running original instructions:

```sh
python3 tools/ppc-analysis/lanes/advisor/rse_particle_evidence.py /path/to/mac-feral/bin
OPENTPW_PPC_BIN_ROOT=/path/to/mac-feral/bin python3 -m unittest discover -s tools/ppc-analysis/lanes/advisor -p test_rse_particle_evidence.py
```

## Sound catalogues (`cat_*SFX.map`, `cat_*BANK.map`)

Every sound the original plays is an *event* of a sound category. A category is a pair of
files: `cat_<name>SFX.map` (events, sounds, samples) and `cat_<name>BANK.map` (the sample
banks). `SoundCatalog` reads both in the order of the Mac sound library's map streamer
(`TbMapStreamer::RegisterSFXData` `0x10015F00`, `ReadCatagory`, `ReadEvents`, `ReadSounds`,
`ReadBankDataFromMap` `0x10015100` in `sound_shared`). All fields are little-endian; the
Mac swaps every one.

| Part | Size | Fields |
| --- | --- | --- |
| Header | 28 | 16-byte type GUID, u32 flag, u32, u32 category count |
| Category | 24 | u32 event count, then a pointer slot and parameters |
| Event | 20 | u32 id, u32 sound count, pointer slot, u32 priority, u16 flags, u16 |
| Sound | 42 | u16 sample count, u32 link count, volume min/max (+12/+13, percent), pitch min/max (+14/+15, signed), delay min/max (+16/+18), weight (+30), float (+34) |
| Sample | 16 | u32 1-based sample number, u32 cumulative weight (out of 65,535), u32, u16 1-based bank number, u16 |
| Link | 8 | u32 1-based target sound, u16, byte min and byte max of the parameter range |

Each category's events follow the categories, each event's sounds follow its events, and
each sound's samples and links follow its sounds. The bank map holds 11-byte records
(rewritten at load) and then, per bank, a u32 length and a name such as `Sound\UI`. All
31 SFX and 31 bank catalogues of the Mac data parse to their exact length.

**Bank folders.** The record's flag byte (`+9`) decides where a bank lives: with bit `0x20` the base is
the data root's `Global` folder (`0x6bff14`-`0x6bff3d`, string `0x782dd8`), otherwise the category's own
folder (the map folder's parent). The bank is then opened with one existence test (`0x6b9310` ->
`0x6c5a00` -> `0x6c58f0`: `HW` then `HD`, reversed under the manager flags; only `HD` ships) and a
failure returns 0: there is **no second folder** (AUDIO-002 is retired). No shipped bank record has
`0x20` (flags 1, 2, 4, 0x41, 0x42) and every shipped name resolves in its own folder, which
`AudioRegisterRound2Tests` checks against the data when `OPENTPW_GAME_PATH` is set.

The event flags select the player (`CAudioPlaceHolderList::CreatePlaceHolder` `0x10011F20`):
without bit 0x4 a one-shot; with 0x10 a linear sentence; otherwise without 0x2 a one-shot
(branching) sentence, and with 0x2 a branching sentence (0x400), a shuffled sentence (0x100)
or a plain sentence. The park music event (flags 0x606) is a branching sentence; the lobby
music event (0x206) a plain sentence; UI events are one-shots.

Choices use the sound library's **one shared, advancing** linear congruential generator, in the PC
build (tp.exe): every draw loads the shared state (`0xfb1f20`), computes `s * 0x19660D + 0x3C6EF35F`,
**stores it back**, and uses `ror(s, 13)`, then either `>> 16` or `% n`. The draw sites: a sound by the
running sum of its weights (`0x6bc810`, `>> 16`), a sample by its cumulative weight (`0x6bc680`;
`0x6bc6ce` stores, `0x6bc6d4` rotates, `>> 16`), the volume (`0x6bc090`), the pitch (`0x6bc170`) and the
delay (`0x6bc343`) as `min + r % (max - min)` (so the range's maximum is never drawn), and the branch
and link choices of the sentence classes (`0x6be450`, `0x6c3e00`). A lone sound, a lone sample and an
empty range draw nothing. A sentence does not repeat the sample it just played when there are more than
two. The state is seeded once at static initialisation (`0x72e09c` -> `0x6bb930` -> `0x5f5f10`,
QueryPerformanceCounter) and is never saved.

**Pitch.** A sample starts (`0x6bc410`) with the volume draw, then the pitch draw (`0x6bc170`): the signed bytes `+0xE`/`+0xF`
of the sound record are put in order and the step is `min + r % (max - min)` (a placeholder flag `0x10000` fixes it, a
parameter-bound pitch slot supplies it from the parameter). A non-zero step multiplies the sample rate by a float of the
tables at `0x782f40` (positive) and `0x783540` (negative, read at `+4 * |step|`), rounded to an integer for the mixer
(`0x6d2820`); the tables hold 2^((k + 1) / 96) written with six decimals (a few ULP from the exact power; OpenTPW computes them by that rule, which matches all 110 entries of both tables), so the index is one step off: step 1 is already +25 cents, step 0 is the only
unshifted one, and 2^(1/96) is never played. The scream, chain and crowd sounds draw 0..6 (steps 0-5, up to +4.4 %);
others range from -24..36 to -2..0 steps, and 1,343 shipped sounds have no range. In the video of the original, 141
playbacks sit on exactly the six ratios of 0..6 (median error 0.00014), not on 2^(1/96) or the maximum. OpenTPW draws and
applies it in `SoundEventSystem.Pitch` and `AudioVoice.SetPitch` for every one-shot, placed sound, sentence segment and
chain child; the pitched voice is shorter by its ratio and a sentence's next segment starts when it ends. The chain
delays are milliseconds (the video shows sub-second gaps and overlapping children).

OpenTPW draws everything from that one chooser (`SoundEventSystem`; the crowd's separate stream and
AUDIO-035 are retired). The state is seeded from the clock when the sound service starts, but a park
**reseeds it from its world seed at entry and saves it with the park** (`WorldRandomState.SoundSeed`;
it is not part of the canonical hash), so headless runs and tests start from a known state (with sound on the draws follow wall-clock events, so it is not a replay guarantee): that
is the extension `[EXT:sound-chooser-seed]` (retired `APPROX:AUDIO-037`): the original's chooser is clock-seeded and unsaved.

**Mac versus PC.** `SoundEvents.cs` used to follow the Mac reading (the seed is never stored: the only
store to `CAudioPlaceHolder::mRandomSeed` is the library init `0x100118C8`, the eight LCG sites only
load it, and the draw takes the top 16 bits without the rotate; `ChooseRandomSample` `0x1000FCB4`).
That contradicts the PC instructions above, which store every draw and rotate by 13. The PC reading is
used. The Mac binary was not re-read in this round (by rule); someone should recheck the Mac claim
(`[EXT:audio-chooser-mac-reading]` in `SoundEvents.cs`): it may be a misreading of the Mac code, or
the builds may differ. Applying the frozen Mac draw made the crowd alternate between two clips although
the shipped crowd sections hold 7-52 samples, which the advancing chooser avoids.

The crowd also sends its nearby-visitor count as parameter 7 (Mac game
`0x100BC298–0x100BC2A0`). For crowd sections whose volume mask selects that parameter,
the sentence now applies the original `low + floor((high - low) × parameter / 100)`
interpolation (`sound_shared` `0x1000F32C`, `0x1000F3C8–0x1000F3F4`), using the first
volume-mask slot (`0x1000F2E0`). The shipped crowd bounds are 14–22 percent. Sections
without that selector retain their range-based volume, and the other parameter
producers remain outside this crowd fix. A segment evaluates its volume when it starts, and a parameter
call re-evaluates the playing one as the PC library does (`0x6bbb80` -> `0x6bbbe0`, `SoundSentence.SetParameter`):
a placeholder's four slots hold the event's code (event `+0x12`), the current sound's two selectors (`+0x16`,
`+0x1A`, refreshed whenever the sentence moves to another sound) and 0, and a call whose code matches slot 1-3
recomputes the volume (unless a fade is in progress) and redraws the pitch. The crowd event's code is 7 and sounds
1-6 of its twelve bind 7 in slot 1 under the volume mask, so every crowd update moves the playing section's volume
and pitch; sounds 7-12 have selector 0. Every sentence holds this table (`SoundParameterTable`): the crowd, the ride
and tour vehicles' engines (EventMap variable 10, `0x51BC40`), the level rain voice (code 8, `0x51E870`; its
sounds bind 8 under the volume mask, so the drop level also sets the rain's volume within 1 to 2..5 percent) and the park music
(code 4, `0x51E79E`; no music sound binds a selector, so it only steers the branching) go through the same call, and only a call with the event's own code (slot 0) sets the value the branch
picker reads (`0x6be5e0` → `0x6be450`, table `+4`). One-shots and chains build the same table from the event and
the chosen sound (every placeholder class sets up with `0x6bbf60` and queries with `0x6bc040`). The
query `0x6bc040` tests only the masks: a bound sound takes its slot's value whether or not a matching call came, a value
never set being 0, so the weather's own rain voice (no parameter call) plays at the bottom of its range. The Mac reading
of `GetRandomVolume` kept a random volume for an unset parameter; the PC's is followed. The PC's main loop sends the crowd update (`0x51E7B0`) and the music
parameter on every 32nd world step of 31 ms of the game timer (`0x54F82D`, `WorldStepCadence`), about once a
second, so the crowd also starts and stops on that beat.

## On-demand bank boundaries

`SoundBank` retains the entry offset table and opens a fresh, owned stream for each
nonempty read. Both sources refuse entry names whose offsets overlap that table;
MPEG lookup retains its header and payload bounds checks. Stream positions use
64-bit addition even when the stored entry offset and header size are signed
32-bit fields, so a valid payload or name beyond 2 GiB does not wrap backwards.
Each reopened stream's length is checked before allocating the requested buffer.
Fragmented reads are completed with `ReadExactly`; truncated input throws and the
stream is disposed on success or failure.

`SoundBankBoundaryTests` uses small byte fixtures and sparse logical streams to
cover these boundaries, count/offset/header/size extremes, every truncated prefix,
independent lookups and stream disposal. This is synthetic host-reader evidence;
fresh original-bank compatibility and playback checks are deferred while the
asset mount is unavailable. The bank still returns a complete requested sample
buffer, trusts a stream's reported length, and cannot detect a replacement with
the same length. No new format size cap or eager bank loading is introduced.
The [independent boundary review](SOUND-BANK-BOUNDARY-REVIEW.md) records the
reproduced failures and additional valid alias and failed-read recovery controls.

## Park music

When a park starts, the game sends event 2 of the level's `cat_music` category
(`0x100BC144`). Each of its six sounds is a section of 8 to 40 segments of 17.5 s in
`Music/MusicHD.sdt`. After a segment, the branching sentence follows the link of the
current sound whose parameter range holds the event parameter
(`AssignSoundToNextBranch` `0x100192F0`). The links cover 0–14, 15–29, 30–44, 45–59,
60–74 and 75–90, one section each. The park turn sets the parameter to the number of
guests in the park halved and capped at 100, then capped at 89, and to 0 in world
state 4 (`0x101C2444`, using `0x100C2344`). So the music moves to a busier section for
every 30 guests.

OpenTPW chooses the next segment while the current one plays and starts it on the
frame the current one ends. Native builds decode on a background task. The browser
processes four MPEG frames per batch and yields through a timer between batches,
keeping drawing and input responsive. Long sentence PCM can be retained in an 8 MiB
LRU cache; the existing short-sample cache is separate. Synchronous and cooperative
PCM are identical. A native Layer II segment decoded in about 84 ms in the earlier
measurement; browser cost is higher. See [browser performance](BROWSER-PERFORMANCE.md).

## Mixer and volumes

`AudioMixer` mixes 16-bit voices (resampled linearly) into one SDL queued output at
22,050 Hz and keeps about 93 ms queued. The effects, music and speech channels use the
option volumes. While speech plays, music and effects are lowered to
`SoundInfo.DUCKINGLEVEL` (38 % in `sound.sam`), as the Mac sound service does
(`0x100BB18C`). Without an audio device the mixer runs silently on the wall clock, so lip
sync and sentences still advance. `--mute` turns sound off.

A ride script's `DIPMUSIC` sets the sound service's music duck request (`0x51E710`): while it is non-zero the music channel is silent (`AudioMixer.MusicDuckRequest`, `0x51BE5A`),
which the end-of-park sequence of `Gates.RSE` uses. `SETREVERB` chooses the reverb preset of the ridden ride (`ReverbSelector`, `0x55A470`); the preset drives the effects channel's reverb, OpenTPW's own stand-in by design (`[EXT:audio-reverb]`).
Both are described in [RSE-VM.md](RSE-VM.md#dipmusic-and-setreverb-the-sound-service-readers).

The advisor plays its clip on the speech channel; its lip-sync position is the mixer
output the listener has been given since the clip started.

### Volumes of a loaded original save

The user's decision (2026-10-19): a park started from an original save takes the save's volumes, as tp.exe does. The save's
Sound block holds the sound service's shadows of the mixer values (docs/TPWS-PAYLOAD.md, "Sound block"): master `+0x18`, effects
`+0x1C`, music `+0x20`, speech `+0x24` (percent) and the on-flags `+0x38`/`+0x3C`/`+0x40`. The loader `0x51C400` copies them back into
`0x803AA0`; park entry ends (state 9 of `0x54E360`, `0x54F3AE`) with `0x51E6F0(0)`, which sets the dirty word `+0x28`, so the next sound
tick `0x51BD70` pushes all seven to the mixer whatever dirty word the save held. The post-load pass
`0x415140` does not call the options applier `0x423DD0`, so the park plays at the save's volumes while the options screen shows the
player's own. Which parks load a save: park entry (state 9 of `0x54E360`) loads the newest file in the player's folder of the park
through `0x5ACCF0` -> `0x414D40`. That is a player's own save when one is chosen, and for an Instant Action player the copy of the level's
`easymode.TPWI` that `0x5C8190` puts in each level folder when the player is created; a new Full Simulation park has no file and loads
nothing. OpenTPW does the same for every park started from an original save, a player's own (`--original-save`) or the shipped
`Easymode.TPWI` seed (Instant Action, the reference start) (`Level.OriginalSaveSound`, `GameAudio.ApplySavedSound`,
`SavedSoundVolumes`; the import report names the values):

- **Scale and mapping.** The saved percents go to OpenTPW's channels unchanged: effects to `Effects`, music to `Music`, speech to
  `Speech`, a channel switched off plays silent (the applier pairs each on-flag with its volume), the master to the mixer's master gain.
  The saved dirty word does not matter (park entry sets it after the load).
- **The options file is never written by it.** The original's profile options are a separate struct (`0x78D8E8`, serializer
  `0x423E90` with `SFXVolume`/`MusicVolume`/`SpeechVolume`/`MovieVolume`) that the save load does not touch, and the profile file is
  written from that struct. `GameOptions` and `opentpw-options.json` stay as they are; the save's volumes hold for the session only.
- **When the options take over again.** In the original: a slider or toggle of one channel calls that channel's setter only
  (`0x4A2BF0`, messages `0x800`/`0x801`), so the other channels keep the save's values; OK on the options screen (`0x4237F0`) calls
  `0x423DD0`, which pushes every channel's profile values; leaving the park (state 12 of `0x54E360`) writes the profile through
  `0x5C8650`, and the profile serializer ends in `0x423DD0` too. OpenTPW ends a channel's saved value once the player's option for
  that channel differs from what it was when the save applied, ends all of them on OK and on leaving the park. A channel the save
  switched off stays silent until OK or leaving the park: the slider and the toggle call only the volume setter (`0x4A2E90`), and only
  `0x423DD0` sets the on-flags. Cancel on the
  options screen restores the options, so an untouched channel keeps the save's value.
- **The master volume** has no option: the original sets it to 100 at start-up (`0x54DE31`) and `0x423DD0` never sets it, so a
  save's master holds for the rest of the process; `GameAudio.MasterVolume` does the same.
- **Restart Park.** The original's `restart.INTS` is written at park entry (state 15, `0x550B30`) with the shadows the park
  started with, and the restart reloads it through `0x414D40`, so the start's volumes play again. OpenTPW's restart snapshot is its
  own format (SAVE-012), so the flow keeps the Sound block the park started from with it and applies it again after the reload.
- **Not applied:** a read-only online visit (its park is not an original save file) and a resumed OpenTPW park (its save has no
  Sound block). All twelve corpus files hold the defaults (master 100, 75/60/75, all on), so with default options loading them, or
  starting an Instant Action park from the shipped seed, changes nothing audible.

## Triggered sounds

Every game sound is submitted through one wrapper (`0x100BB4FC`: category handle, event id,
parameters, 3D position) that queues a message on the sound service. The sound manager keeps
its categories at fixed offsets (`0x100BC6F0` registers the global `cat_ambient`/`cat_kids`/
`cat_rides`/`cat_ui`/`cat_staff`/`cat_speech` at offsets 0/4/8/12/16/20, `0x100BC438` the
global `cat_globallobbysfx` at 40, `0x100BC4C0` a level's `cat_locallobbymusic`/
`cat_locallobbysfx` pair in a table, and `0x100BC864` a level's `cat_ambient`/`cat_rides`/
`cat_speech`/`cat_music` at 24/28/32/36).

| Sound | Event | Game action | Source |
| --- | --- | --- | --- |
| Park music | `cat_music` 2 (branching sentence) | a park opens; busier sections as guests arrive | `0x100BC144`, parameter `0x101C2444` |
| Lobby music | `cat_locallobbymusic` 1 (sentence) of the selected level | the front end's selected island | `0x10095A24` (start `0x1009598C`, registration `0x100BC4C0`) |
| Lobby ambience | `cat_locallobbysfx` 2 (sentence) of the selected level | as above, looping | `0x10095A50` |
| Lobby island one-shots | `cat_locallobbysfx` 3, 4, 5 on the lobby generator's draws | the front end shows (see "Lobby effects") | `0x10095340`–`0x10095450` |
| Start-up sound | `cat_ui` 0xD2 (flat one-shot, flags 0x200) | once, when the front end first shows (OpenTPW does not play it for a run that loads a park directly) | tp.exe `0x54de6a` (start-up init `0x54dcf0`, called from WinMain at `0x45abed`) |
| Control click | `cat_ui` 0x1F, or 0xBD when the button's parent has style bit 0x10 | a button release (message 0x100 to its parent). 0xBD is the up and down arrows of a vertical list scroll bar (type 3, style 0x10), the column headers of a sortable list (type 7, styles 0x91 and 0x291) and the toggles of a radio group (type 8, style 0x11). Every other button plays 0x1F. A held arrow waits 500 ms, then steps every 125 ms, and the release after repeating has started plays nothing. Dragging the thumb, clicking the track and a still click on the thumb play nothing (the thumb's procedure `0x66bb9b` replaces the button class and answers its release with 0x802 to the slider). Window procedures also send 0x100 to their own window (0x48d83b, 0x49973e, 0x49ec53, 0x4a6df5 (via the dispatcher 0x658d9f), 0x4b640a, 0x4b9f06, 0x4bb020 and 0x4bfc29; ids -1, -2 and 0xe059, on 0x802 or 0x804), and the hook clicks for those too by that window's own style: style 1 in all eight, so 0x1F ("List owners" maps them to OpenTPW's dialogs). | tp.exe hook `0x485780`, the 0xBD/0x1F choice at `0x4857cb`; the button class `0x668f9c`, the only sender of 0x100 for a mouse release over a button, posts it to its parent (`[+0x60]`) at `0x6691eb` through the queue `0x658ccc`; window procedures `0x48d83b`, `0x49973e`, `0x49ec53`, `0x4a6df5` (via the dispatcher `0x658d9f`), `0x4b640a`, `0x4b9f06`, `0x4bb020` and `0x4bfc29`; the pump `0x658e42` calls the hook at `0x658eb2` (dispatcher `0x658d9f`); arrow ctor `0x6688b3` (+0x13c = 500, +0x140 = 125), timer `0x668dd9`, release `0x668eff`. Mac `0x10137FD0` |
| Map/research drawer | `cat_ui` 0x1C open, 0x1C then 0xC2 close | the map and research windows | tp.exe `0x4a14f0` (open `0x4a1737`, close `0x4a186f`, slide end `0x4a1947`) |
| Error feedback | `cat_ui` 0x1D | a rejected purchase or placement | `0x10139A40` (buy window callers `0x10163AFC`/`0x10163BFC`) |
| Path tool | `cat_ui` 101 lay, 175 refused | a committed path line; a click on a refused path or queue ghost (or a refused track commit), at the clicked cell (x * 10, 0, z * 10) | `0x10070DE0` (commit), tp.exe `0x52496e` (refused `0xAF`, `0x524a80`, at the release that follows a park-view press (byte `0x8186d8`, set by the press handler `0x524790`) outside a layout replay (`0x785a2c`); AUDIO-022 is retired) |
| Track and coaster tool clicks | `cat_ui` 0xCE (kart and water track tools), 0x65 (coaster tool) | every release of the tool's click, at the clicked cell (x * 10, 0, z * 10) | tp.exe `0x526cb3` (0xCE, submit `0x526ce9`), `0x5263df` (0x65, submit `0x526423`) |
| Pause menu backdrop | `cat_ui` 0xC1, flat | a click on the pause menu's backdrop outside its lines | tp.exe `0x492d80` (via `0x485aa0`) |
| Coaster crash | `cat_rides` 0xC (global) at the bump | two trains bumping with new speeds more than 1.0 apart | tp.exe `0x43cd60` -> `0x439840` (submit `0x4398cd`) |
| Advisor key sounds | `cat_ui` 0xC5 at a golden-ticket clip's start, 0xC6 once per response at its key timer, both flat | the advisor presentation | tp.exe `0x598F1C`, `0x5999C4` |
| Rectangle ambients | global `cat_ambient` 2 or 3 at the listener's clamp into a rectangle | the level's `scape.omp` type 3 records, for the park's lifetime | tp.exe `0x550e00`, `0x51c5d0`, `0x6bd5f0` |
| Ride-script sounds | any event of the routed category | an RSE `EVENT type key id` instruction: type 3 level `cat_rides` (no fallback to the global one: an id the level lacks is silent, `0x6c5870`; AUDIO-011 is retired), 4 level `cat_ambient` (likewise), 5 global `cat_rides`, 6 `cat_kids`, 7 `cat_staff`, 8 `cat_ambient`, 9 `cat_ui` | `0x100AE930` (dispatcher, guard `bgt` 10 at `0x100AE938`); tp.exe `0x5575c7` (anchored at the key's model node, the footprint middle for a negative key, nothing for an unknown key; see "Placed sound EVENT origins") |
| Ride screams | `cat_kids` 71–74 held per ride (STARTSCREAM), STOPSCREAM stops it; `cat_kids` 75–109 one-shot (SINGLESCREAM) | a ride script's scream instructions; band 1 → 71, 2–3 → 72, 4–7 → 73, 8+ → 74; parameter (level + 50) / 2 (parameter 6 on the PC); **positioned at the ride's footprint middle** | `0x100B19DC` (STARTSCREAM), `0x100B1A74` (STOPSCREAM), `0x100B1A90` (SINGLESCREAM), `0x100ACA38`, `0x100ACB7C`, `0x100ACC24`, `0x100ACE68`; tp.exe `0x551130` (STARTSCREAM submit: positioned, 0x47-0x4A at (x, y, z), parameter 6 = clamp((a + b) / 2, 0, 100)), `0x555edb`, `0x555f1b` -> `0x556af0` -> `0x551320` |
| Repair sounds | `cat_rides` 111 and 211 for a non-zero value; `cat_kids` 75 (families 75–90, by the script's variable 5) for 0 | an RSE `REPAIREFFECT 1/0` instruction, both at the object | tp.exe `0x5562C5` (the compare), `0x5562CD`–`0x556351` (the pair), `0x55635F` / `0x551320` (the band sound); Mac `0x100B1EC0`, `0x100B1F00` (pair), `0x100B1F78` (band) |
| Rain | `cat_ambient` 0x21 (branching sentence, parameter 8 = the drops' level 0–100), and a second flat 0x21 voice of the weather thing's own while it rains | the weather's current drops: started above 0, stopped at 0 (also in snow); the thing's own voice starts in rain mode and stops in snow or clear | `0x10103394` (level), `0x100BC3D4`–`0x100BC41C` (global voice); tp.exe `0x512a7f` (level), `0x5131f0` / `0x51327c` (the thing's voice, handle `+0x54`) |
| Thunder | `cat_ambient` 0x20 at the last bolt's top (x, 300, z) | a lightning strike's thunder countdown ends | `0x101034C8`–`0x10103518` |
| Placed ambients | `cat_ambient` (global) or the level's `cat_ambient`, the effect the scape record names; a sentence-type effect plays as a positioned sentence | the level's `scape.omp` type 1 records, started with the park and kept for its lifetime; range from the record | tp.exe `0x550e00` (type 1 -> placed submit `0x51c130`, coordinates `/1024` toward zero, range `*2/1024`); `PlacedObjectsFile`, distance law of the mixer (AUDIO-018) |
| Staff voices | `cat_staff` 160–169: walking/idle per role (handyman 160/161, mechanic 162/163, entertainer 164/165, guard 166/167, researcher 168/169) | a staff member each park turn, 1 in 16 draws (AUDIO-024) | `0x100D5EAC`, `0x100D6434`, `0x100D9BAC`, `0x100D9F10`, `0x100D2DDC`, `0x100D2E44`, `0x100D4EF0`, `0x100D4E18`, `0x100F0460`, `0x100F0630` |
| Crowd | `cat_kids` 0x5B (branching sentence, parameter 0–100) | guests within four cells of the camera; louder/bigger sections as more gather, stopped when nobody is near | count `0x101C2490` (`0x100C313C`, radius 4, cap 100), sentence `0x100BC210`/`0x100BC2E0` |
| Advisor speech | response table: sample and LIP from the global or level speech bank | traced advisor events | `0x10006B7C` |

**How often screams come.** A held scream is a chain of one-shot children (tp.exe `0x6bdae0` → `0x6bdb50`): each child starts a
delay after the previous child's start, drawn from the event's bounds — 1–3 s for one rider (event 71), 0.5–2 s for two or three (72),
0.1–1 s for four to seven (73) and 0–0.5 s for eight or more (74) — and earlier children play out, so a full ride screams almost
continuously, as in the original. What limits it is the sound manager: it ticks only 12 audible voices (`0x6b62b0`), ranked by the event
priority with the ground distance as tie-break (`0x6bb9f0` stores the priority beside `0xFFFF − d²/100`, and the admission `0x6b6bc0`
compares both as one word), and a scream is audible only while the listener is within its range of 95 sound units (9.5 cells) over
the ground. Screams have priority 2700, below the crowd (5300) and most ride and ambient sounds, so a busy scene leaves them few places.
A one-shot keeps its place in that pool until its sample's nominal end plus 250 ms plus its drawn delay, finished or not (`0x6bc01b`, `0x6bcb60`; events flagged `0x4` or `0x8` are exempt). Among equal ranks the newer voice keeps its slot (`0x6b6a80` links a new voice at the head of the voice list, `0x6b6bc0` slots a voice before the first slot of strictly lower rank). A chain outside the pool or out of range starts no new child, its first included (AUDIO-038); every other event voice out of range without the any-distance flag (event flag `0x200`: the crowd sentence, music, speech, most UI sounds) has its sample stopped by mixer message 3 (`0x6bca70`), a sentence standing still until the listener returns. A catalogue loop retains its logical owner but releases its sample output; after return and pool admission it creates fresh output from the selected source (see [AUDIO-038](proposals/audio-038-trace-out-of-range-voice-resumption.md)). The visitors'
chatter and laughter is the crowd sentence (`cat_kids` 0x5B), a continuous bed at 14–22 % volume while guests are within four cells
of the camera: its segments have no delay in the data.

`APPROX:AUDIO-033` (the distance law's rolloff) is retired: a positioned voice is turned down to 1 inside the minimum distance 2, `((max - d) / (max - 2))^0.2` up to `max` (`max(range, 2.1)`, `tp.exe` `0x6c3b8a`) and 0 beyond (QMixer.dll `0x1800a0d0`, enable-word flag `0x1000`, always set by `tp.exe` `0x6d230e`); the exponent is the float of the sound system object's `+0x2c` method (`0x6b9bb0`), 0.2 (`0x70a624`) when the object's flag `0x40` is set and 0.8 (`0x70a628`) otherwise, and the flag is set whenever the driver is the QMixer class (`0x6d2d70`, whose `+0x4c` is `0x5d1400`, `mov al, 1`): only the fallback driver class (`0x6d1e30`) would give the software 3D layer and 0.8. This holds while the QMixer class initialises (`0x6d2de0`: COM creation of the QMixer object and its set-up calls, which the game's installer registers); if it fails, `0x6c4190` falls back to `0x6d1e30` (`+0x4c` is `xor al, al`, `0x4f6010`) and the exponent would be 0.8. OpenTPW used 0.8 before.

`APPROX:AUDIO-034` (held-chain timing and link walk) is retired: a chain's next child is made when the newest child's end has passed (`0x6bdb50`) and the link is walked then, with the parameter of that moment (`0x6bdbb0` → `0x6c3e00`); a sound without any link makes the chain start over at the event's first sound on the next pooled tick; links that exist but do not hold the parameter write `0x7f` into the root's parameter byte and set the root flag `0x40`, which only the root's parameter setter (`0x6bda90`, called after the slot write `0x6bbb80`) clears, and only for a stored byte other than `0x7f`, so the chain makes no child until a different parameter is set or it is stopped (`SoundChain.Parameter`; the ride scripts set it only when the scream starts); the link pick is `ror13(state) % total` over the covering links' target weights (`SoundChain.NextLink`).

**Scream diagnostics (October 15).** The original's video shows screams in bursts of 2-17 s with 2-4 samples a second near a
busy ride, so OpenTPW's rate is not too high; the report's candidate causes were counted. A second STARTSCREAM before a
STOPSCREAM is refused by `0x551130` (log `0x765a4c`) and leaks the old chain (the caller stores the 0 over the handle,
`0x555ee3`/`0x555ee6`); `GameAudio.Screams` counts every STARTSCREAM by band, the refusals and the stops, from the script's own
view of its handle, so the headless gates count them without audio (`--m3-gate` and `--m4-gate` print the summary; the
native `--debug-sim-minutes` prints it for a running level).

- `--m3-gate --level jungle --minutes 120` (432,000 ticks, 429 guests, 416 rides on Belly Bounce, breakdowns and repairs
  included): 791 STARTSCREAM, **0 refused**, 737 stops (every start with a band above 0 is stopped but the last); bands 0:
  53, 1: 116, 2: 141, 3: 160, 4: 202, 5: 119.
- `--m4-gate` (every ride of the four themes through its lifecycle with queued guests): 169 STARTSCREAM, **0 refused**;
  bands 0: 105, 1: 9, 2: 11, 3: 7, 4: 37.

So OpenTPW's scripts never reach the leak path, and no chain piles up. The band is the script's operand (`STARTSCREAM V5 20`:
variable 5 is `VAR_ONRIDE`, which the script maintains per rider with LIMBO and UNLIMBO); tp.exe reads the operand itself, with no
visitor lookup (`0x551130` takes it as its second argument; 0 returns, 1 picks event 0x47, 2-3 (negative too) 0x48, 4-7 0x49, 8 and
above 0x4A, parameter `(level + speed) / 2` clamped to 0..100), exactly what `GameAudio.ScreamBandEvent` and `ScreamParameter`
do. The bands run from 0 to the ride's capacity (5 for Belly Bounce, `Upgrades[0].InitCapacity`), and the data fixes that
capacity, so OpenTPW's band equals the original's for the same riders; whether the original seats as many riders as OpenTPW
(boarding and queue pace) is the guest slice's question, not the band's. Near a full ride a band of 4 or more picks the
0-500 ms and 100-1000 ms event (73, 74) in 43 % of the starts of the jungle run, as in the original's busiest minutes.

**Held screams in a park save (October 18).** The script's handle is state, not sound. `OriginalObjectEffects` keeps whether the
script holds a handle, the STARTSCREAM band, level and speed word, the level of the last SCREAMLEVEL while the handle is held,
and how many handles refused starts lost. A run without audio (headless, `--mute`) gets the same values as a run with a sound
service, and a park save stores them, so its snapshot and hashes do not depend on audio. The refusal is decided by the script's
handle. Both STARTSCREAM operands and SCREAMLEVEL's operand are resolved whether or not a handle is held (tp.exe `0x555E5E`,
`0x555FDA`). The chain only plays the held scream. This deliberately models tp.exe with its sound running: without a sound
device its chain start `0x51bfc0` returns 0 (`DAT_00802bc8`/`DAT_00802bd4` unset or a failed play), so the original then holds
no handle, never refuses and a SCREAMLEVEL (`0x51bc40`, which returns 0 the same way) clears it. OpenTPW keeps the sound-on
behaviour for every run so that saves do not depend on audio.

A restore brings the state back exactly and starts no sound, so a candidate park that a failed load rejects leaves no chain in
the live park. Once `GameFlow.ResumePark` has entered the park's audio, `Level.ResumeHeldScreams` starts each held scream's
chain from its operands. Entering the park's audio stops every chain of the park it replaces, so a chain started earlier
would go silent at once. A lost handle is restored only as a count: its chain does not scream again.

Traced in the binary and played:

* `EVENT` types 1 and 2 create **particles**, not positioned sound loops; see the particle section above.
* `cat_ui` 139 is the sound of a queue joining a path, not a path-tool link-end. Its first source is the queue tool's
  commit when the line ends on a path cell, money on (`0x100711CC`, tp.exe `0x527587`; the validator `0x84154` returns code
  8 for that line, which is also no longer treated as a refusal). Its second source is the link writer (tp.exe
  `0x5348D0`, `PathLinkWriter`): it reports a laid path cell beside a queue cell whose link byte points back at it (a path laid
  over the queue's join cell, or over the queue's end), and the sound plays at (10x, 0, 10x): tp.exe takes both coordinates
  from the cell's x (`0x5348E0`). The count-classed removal sounds `cat_ui` 141–144 and 150–153
  (`0x10075618`, `0x100748A0`) are demolish sounds, not path-tool sounds.
* `cat_ui` 95 is the "backtrack" key command (Backspace, `0x10112430`, tp.exe `0x40BDA0`): with no tool on and
  a path cell under the cursor it plays at that cell, then that cell loses one placement (ClearCell's counted path case,
  tp.exe `0x40BE8B`) and goes only when it was laid once; the sound plays even when the cell stays. Over a queue cell OpenTPW plays it too and takes the queue back from that cell
  (`[EXT:queue-backtrack]`; the original's key does nothing there). Inside the path tool the original's Backspace is Back (tp.exe `0x52FE50`): it takes the last segment back without
  a refund and plays no sound, as OpenTPW's segment undo does. With the pools extension on, the key also takes a pool bridge down, or erases one pool water cell, under the cursor and plays the same
  sound there (`[EXT:pools-bridges]`, `[EXT:pools-tool]`; the original has no pools); a refused removal plays the error sound instead (docs/POOLS.md "Removing with Backspace").
* The weather's own visuals follow the original's projection (AUDIO-900, 901 and 902 are retired); AUDIO-904 is retired too ("Near-plane measurements"); the forecast's warning is shown as an OpenTPW extension (banner, wording
  and cadence are `[EXT:forecast-banner]`, `[EXT:forecast-wording]`, `[EXT:forecast-cadence]`): the PC has
  no forecast message (event type 2 has a single subscriber, the weather thing itself, `0x511fa9`/`0x511fb8`).
* The visitor click (`cat_kids` 129, flat) plays whenever a visitor's details open: the status panel's open routine
  (`0x1010B808`, tp.exe `0x4B79B0`) plays it unconditionally, for a click on a visitor in the park, a roster row's
  activation, the map's visitor markers and the selection strip. OpenTPW plays it for the park click and the roster
  activation; it has no separate status panel and no map markers or strip yet.
* Entertainers, guards and researchers in a level play their work sounds from their state machines: `cat_staff` 135 at the end of a
  performance (tp.exe `0x4D4810`), 136 (staff chase) when a guard sets off after a prankster (`0x4D6260`), 137 (catch) when he catches it
  (`0x4D6790`) and `cat_kids` 128 when he sets the guest down (`0x4FEB50`); a researcher at work plays `cat_staff` 138 (researching turn)
  on one draw in sixteen (`0x5029F0`, state 15). (Until October 18 this list said the chase, catch, researching and performance cues had nothing to hang on;
  the state machines exist since the staff work and they play.)
* A prankster's stinkbomb plays `cat_kids` 207 (0xCF) at the prankster (tp.exe `0x4FF289`, through the thing sound
  `0x4FAA00`). A guest's litter going into a bin plays no sound of its own: it sets the bin script's variable 0
  (`VAR_TRIGGER`) to 1 (`0x4FFF20`, `0x55A0B0`), and the bin's script does the rest.
* Global lobby effects play (see "Lobby effects" below): the thunder (`cat_globallobbysfx` 1) with the lobby's
  lightning, the globe's music and ambience (2, 3) and the enter-park sound (4).
* The advisor controller's key sounds `cat_ui` 0xC5 and 0xC6 (Mac `0x100069DC` / `0x100075DC`) are the advisor presentation's own,
  not the controller's: tp.exe `0x598F1C` (in `0x598DC0`) plays 0xC5 when a response's clip starts whose table flags carry the
  golden-ticket close-up bit (`flags & 0x3E0000 == 0x40000`, with a response active), and `0x5999C4` (in `0x599880`) plays 0xC6 once per response when
  its timer entry (sequence 28 or 29) comes due, together with the Key particle. Both are one-shots submitted flat at the origin
  (`0x51BFC0(0, cat_ui, id, 0, 0, 0)`), their handles dropped. `Advisor.Update` plays them (`AdvisorAccessories.KeySound`, `AdvisorSequences.TimerSound`).
* The track tools' click sounds. The kart (mode 0xB) and water (mode 0x10) track tools play `cat_ui` 0xCE at the clicked cell
  (x * 10, 0, z * 10) for every release, the first click included and also after a refused ghost's 0xAF (tp.exe `0x526cb3`; the release handler
  `0x524960` reaches it from its mode tests `0x525a1c`/`0x525a2e`, the submit is `0x526ce9`). The coaster tool (mode 0x15) plays the path tool's
  laying sound `cat_ui` 0x65 at the clicked cell for every release, build click and edit-button release alike, before the coaster release `0x446300` and its help 0xCD
  (`0x5263df`-`0x526423`). `ParkHud` plays both next to the places that already post the tool help. Nothing else in the build tools' code
  submits a sound (`0x524960`-`0x535100`: 0xAF, 0x65, 0xCE, 0x8B, 0x97-0x99, 0x90).
* The coaster trains' crash. The collision exchange `0x43cd60` calls the crash effect `0x439840` when the two trains' new speeds differ by more than 1.0:
  `cat_rides` 0xC (global, the same sound as the karts' crunch) at the point the bumping train's front lies at on the track samples (`fmod(front, samples)` split by
  `modf`, `0x43c000` interpolates the two samples' positions, each coordinate truncated). `CoasterRide.Crashed` raises it and `OriginalObjectRuntime` plays it; the
  effect's particles are not reproduced (COASTERS.md).
* The pause menu's backdrop. The menu's full-canvas window (`0x492e80`, procedure `0x492d80`) answers a click (message `0x10006`, either button) by playing `cat_ui` 0xC1 flat
  (through the helper `0x485aa0`) and sending the activate message `0x100001` to its owner; `0xC1` is the only sound that procedure plays. `TextMenuScreen` plays it
  for a click that hits no line (both menus: the park's pause menu and the front end's). What the pause callback `0x48b6a0` does with `0x100001` for the backdrop is
  not read, so OpenTPW keeps its own right-click Back and adds no left-click Back.
* The level's rectangle ambients (`scape.omp` type 3): see "Rectangle ambients" below.
* Reverb: see "Reverb" below.
* The slap reactions 639–641 of `cat_speech` (`0x10007068`). Withdrawing a help message while its speech is still playing cuts the speech off
  and plays one of the three (`cat_speech` 0x27F + `rand() % 3`; tp.exe `0x5994E0`/`0x5995A4`). `AutomaticAdvisor.CutOffSpeech` plays it;
  `AdvisorHelpTests.ACutOffSlapsOnlyWhileTheSpeechPlays` covers the choice, and a cut-off after the speech sound has ended plays nothing.

Not played:

* `EVENT` type 0, the dispatcher's default/unsupported path. Type 10 plays from the object type's own sound list ("The object type's own sound list (EVENT type 10)" below).

### Event coverage (round 7)

Wired in round 7, each from the instruction that submits it (the tags sit at the code): the upgrade sound
`cat_rides` 0xB8 (`0x55a2e5`), the placement cues `cat_ui` 0x8D-0x90 and the demolition cues 0x96-0x99 by
footprint size (`0x528e30`, `0x5285a2`; the demolition's place is (width x 10, 0, height x 10), a quirk kept),
the message tube's posted (0x1E, `0x48edb4`) and clicked (0xC0, `0x48e043`) sounds, the postcard 0x95 (`0x4a9380`),
the research window's two hums 0x9A/0x9B and the effort slider 0x9C with its parameters (`0x4aa4f5`, `0x4a97ad`,
`0x4a9a53`), the stop-guard cue, and the guests' till (0xD0), drink (0x83), meal (0x84), toilet thought (0x85), pause (0x7E),
vomit (0xCC) and ride-removed (0x80) voices (`0x4fe1cb`, `0x4fe303`, `0x4fca3e`, `0x4fc83a`, `0x4fed86`, `0x4fb3f5`).
The staff's walking and idle voices were already wired. Script-driven events (the ride scripts' sound
instructions) play through the ride script system. The UI events that round left open are settled: 0xC1 is the pause menu's backdrop click and 0xCE the
kart and water track tools' click (see "Finishing pass"), 0xC5 and 0xC6 are the advisor presentation's key sounds, and the track tool's other
sounds are the coaster tool's 0x65 click and the trains' crash. `cat_ui` 0xBD plays for the three button families in the control-click row (AUDIO-004 is retired). The slap reactions 639–641 of `cat_speech`
are played by the cut-off path in `AutomaticAdvisor.CutOffSpeech` (`cat_speech` 0x27F + `rand() % 3`; tp.exe `0x5994E0`/`0x5995A4`;
`AdvisorHelpTests.ACutOffSlapsOnlyWhileTheSpeechPlays`).

## Finishing pass (October 18)

### Rectangle ambients (`scape.omp` type 3)

The level loader `0x550e00` handles three record types (the file opens `OBJ_`, a count and a record size; the first 15 words of a record are kept).
Type 1 is the ranged placed sound (`0x51c130`), type 2 a particle effect, and **type 3** a sound that follows the listener inside a rectangle: it submits
`0x51bfc0(0, cat, word 2, word 3/1024, word 4/1024, word 5/1024)` (the category is the global `cat_ambient` for word 1 = 0, the level's otherwise) and binds the returned handle with
`0x51c5d0(handle, x, z, w7/1024, w8/1024)`, which stores the rectangle in the voice (`0x6b64b0`: `+0x4C`, flag `0x1000` on `+0x30`). Every tick of the voice (`0x6bca10`) tests the flag, which is never
cleared, and runs `0x6bd5f0`: the voice is put at the listener's place clamped into the rectangle (x into [x1, x2]; z capped at z1 and then raised to z2, the shipped records running z down from z1 because
their extent word is negative; y is the listener's), and the mixer is told. The ordinary range test follows, so the sound is on the listener inside the rectangle and
at its nearest point (turned down by the distance law, silent past its range) outside. The shipped files hold five such records, all global `cat_ambient` events 2 or 3
(jungle two, the other three themes one each, at x 353 to 373, z 21 to 32, 97 to 195 units wide). `GameAudio.StartRectangleSound` starts each as a placed one-shot or sentence at the
clamp of the listener and `GameAudio.Update` moves it every frame (`RectangleSoundPlace`). Not verified against a capture: how audible the five ambients are (their events' own
ranges decide).

### The object type's own sound list (EVENT type 10)

The dispatcher `0x5573d0` accepts types 1 to 10. Case 10 (`0x557686`) reads the thing type from `+0xAC` and takes the type's own sound category (`0x4145e0`, type record `+0x4F0`). The
type loader fills that slot: `0x41F420` looks for `cat_rideBANK.map`, `cat_rideSFX.map` and `BankHD.sdt` in the type's archive and, when all three are there, `0x51B4F0` registers the
category `cat_ride` from the archive's folder (`0x41408D`-`0x4140A6`). The scoped particle templates `0x414420` loads afterwards share the table: each `*.emt` member goes to
`+0x4DC + 4 x atoi(the last character of its stem)`, so a template numbered 5 overwrites the list (OpenTPW plays nothing then; no shipped archive has one). Without a list case 10 logs
`RSSE: Tried to trigger a custom effect with no registered custom sound bank` (`0x765E18`) and returns 0 before the key is resolved; otherwise it submits
`0x51bfc0(0, the type's category, id, x, y, z)` with the key resolved by `0x556b90` as for types 3 to 9, and ADDOBJ keeps the handle as for those types. `ObjectTypeSounds` and
`ObjectCatalogEntry.OwnSounds` load the list once per type, `OriginalObjectRuntime` plays it (`ObjectTypeSoundsTests`; [RSE-VM.md](RSE-VM.md#the-object-types-own-sound-list-type-10)).

The census over every shipped script (308 scripts, `EventKeyCensusTests`) finds the type operand literal everywhere and the counts type 1: 67, type 2: 104, type 3: 251, type 4: 13,
type 5: 67, type 6: 25; **no script uses type 10** (or 7, 8, 9, 0). Only Jungle's loudspeaker (`features/speaker1`) ships a list (events 2-5, bank `Speaker\Bank`), and its script plays
with `ADDOBJ 4`. Its bank is resolved by the folder rule above ("Bank folders"): the rule looks for `/levels/jungle/features/Speaker/BankHD.sdt` while the archive holds its own
`BankHD.sdt`. The folder the original registers the list from is the same archive object's (`0x413DDA`/`0x41409A`), so it most likely misses that bank as well: a mismatch in the shipped
data, which no shipped script reaches.

### Reverb (`[EXT:audio-reverb]`)

The original does not mix reverb in software. `SndReverb.map` (file version 2000, 32 entries, 15 present, 820 bytes) holds 13-dword records that tp.exe sends, in `0x6C46B0`, to the audio device as an EAX 2.0
listener property set (property 1 of the listener set, 0x38 bytes) in the order lRoom (word 3), lRoomHF (4), flRoomRolloffFactor (11), flDecayTime (5), flDecayHFRatio (6), lReflections (7), flReflectionsDelay (8), lReverb (9),
flReverbDelay (10), dwEnvironment (0), flEnvironmentSize (1), flEnvironmentDiffusion (2), flAirAbsorptionHF (12) and the flags 9 (an immediate), or, when the device has no EAX path, to the backend's vtable slot `0x50`. QMixer.dll's
five reverb exports (`Environment`, `DecayTime`, `DampingFactor`, `Mix`, `Volume`; setters at `0x18003ED0`, `0x18003F40`, `0x18003FB0`) range-check their argument, store it and forward it to a COM object (`+0x10C`, made
by `0x1800B530`) through its vtable slot `+0x38`; the DLL contains no delay line, comb, allpass or reverb constant. There is therefore no software algorithm to port. The shipped presets:

| Entries | Size | Diffusion | Room | Decay | Decay HF | Reflections | Reverb | Rolloff | Air absorption |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | ---: | ---: |
| 0 | 7.5 | 0.65 | 0 mB | 1.0 s | 0.3 | 0 mB at 0 s | 0 mB | 0 | -5 dB |
| 1-8, 11-14 | 7.5 | 0.65 | 0 mB | 4.0 s | 0.3 | 0 mB at 0 s | +600 mB | 0 | -5 dB |
| 9 | 1.5 | 0.5 | -500 mB | 5.0 s | 0.1 | +500 mB at 0.15 s | 0 mB | 10 | -100 dB |
| 10 | 1.5 | 0.5 | -500 mB | 12.0 s | 0.1 | 0 mB at 0 s | 0 mB | 0 | -5 dB |

OpenTPW builds `ReverbUnit` (`source/OpenTPW/Audio/ReverbUnit.cs`), a Freeverb-style network (eight damped combs and four allpasses per side, a mono send, Freeverb's published lengths scaled to 22,050 Hz) whose parameters follow the
held record: the comb feedback gives the tail the record's decay time (10^(-3 D / T)), a one-pole damping gives the high-frequency decay ratio, the allpass gain follows the diffusion, the lengths follow sqrt(size / 7.5) within a factor of two,
the return level is 0.25 x 10^((room + reverb) / 2000) (preset 0, the default outside a ridden ride, is not mixed at all: its 0 mB record would reverberate every positioned sound of the park), the pre-delay is the reverb delay and three early taps start at the reflections delay. `AudioMixer` sends the **positioned voices of the effects channel** through it (interface clicks and other flat voices, speech and music stay dry) and adds the return before
the 16-bit conversion; no preset (no `SndReverb.map` in the game folder, which sits beside `Data`, or the preset of a level the map lacks) means no send at all. `GameAudio.SelectReverb` hands the held preset's record to the unit and `GameAudio.LoadReverbMap`
reads the file when the sound service starts.

**A design choice, not a port** (`[EXT:audio-reverb]`; `APPROX:AUDIO-039` is retired). The user decided on October 19, 2026: "nagalm mag EXT" (the reverb may be an
extension). The evidence above leaves nothing to port: tp.exe sends the 13-dword record to an EAX 2.0 property set or to the backend's vtable slot `0x50`, QMixer.dll only
forwards it to a COM object (`+0x10C`, made at `0x1800B530`) and holds no DSP, and the actual reverb lives in the sound device or its driver, which can be neither
recovered statically nor captured. The preset selection (`0x55A470` -> `0x51C8D0`, sent only on a change) and the record's word order (`0x6C46B0`) stay ported; the
network, its constants, the size mapping, the dry default preset 0 and which voices it covers (the positioned effects; the original's device may have reverbed 3D buffers
only, or every voice) are OpenTPW's own stand-in.

## QMixer.dll (original mixer, static)

`tp.exe` reaches the mixer through an adapter (`0x6d2100`-`0x6d2d80`, vtable `0x711920`) over the `QSWaveMix*` API.
[BIN:QMIXER-DLL:0x18005830] Every voice runs in the software channel stage (mode `0x1000000`); the volume
table is at `0x782e40` in `tp.exe`, the pool holds 12 voices (5 in the other mode). Volume and pan laws and the
distance law are in the AUDIO-018 row and the AUDIO-033 note above and are implemented by `AudioListener` and
`AudioMixer` (`AudioMixerLawTests`). The reverb exports hold no DSP: they forward to a COM object (see "Reverb"). Read since October 28, 2026: the `SetPosition` integer message for flat
voices (AUDIO-018 row). Not read: Doppler (AUDIO-018 row) and ducking beyond the speech duck of `sound.sam`.

AUDIO-009 is retired: the refusal stages (`0x535670`/`0x536100`) submit no sound; the refused click's only cue is the release handler's `cat_ui` 0xAF behind the refused-ghost latch (`0x524A63`, `0x524AA7`), which it plays in every tool mode, the placement modes 4 and 0x3B included (`ParkHud` plays it for a refused build click at the release, `Level.BuildClickRefused`). `[EXT:cannot-build-sound]` is narrowed to the "Cannot build" message text and to the error feedback `cat_ui` 0x1D where no 0xAF played (a refusal the host sends back; a placement still waiting for the host takes no click and so no cue, under `[EXT:online-coop-commands]`; the original skips the cue only during a layout replay, `0x785A2C`).

## Lobby effects

The front end's island effects follow the original's islands update (`CLBScreenComponentIslands::vf2`, `0x10094474`),
stepped by `LobbyEffects` at the 60 Hz reference rate (`[EXT:frame-driven-at-60hz]`, `APPROX:UI-940` is retired; the original runs once per rendered frame, `0x5d5c50` → `0x5e0470`):

* **The nearest island** to the camera drives everything, not the selected one.
* **The lobby's own generator** (state × 0x19660D + 0x3C6EF35F, `0x100904A8`; tp.exe rotates the step right by 13 bits
  and stores and returns the rotated value, `0x5E0F4B`), seeded once from the clock (`0x1008CDBC`). The flying meshes draw from it too (`LobbyFlyingMeshes`); the camera
  wander that also does is not built, so the rhythm of strikes and one-shots differs (UI-941; the flyers and the islands step in the original's order and at the same frame rate).
* **Lightning** (only hallow: `LIGHTNING 63`): a draw with `(draw & LIGHTNING) == 1` strikes; four draws place the top
  within ±50 of the island at height 500 and the bottom within ±10 of it at height 0; the bolt lives 500 ms, the screen
  flashes for one frame and `cat_globallobbysfx` 1 plays flat at once (`0x10095124`, `0x10095310`). The bolt is
  drawn by the park's weather renderer (CRain, "Weather visuals"), at the lobby's own scale. The flash is not white: tp.exe
  draws one full-screen quad (`0x57ed50`, from the lightning draw `0x57ee30`, state `0x9A1`: source blend INVDESTCOLOR,
  destination ZERO, `0x5679B0`) that inverts the picture, and clears the flag once it is drawn. In the original's order the quad comes
  first, then the bolts (`0x57ee4d`) and the raindrops (after `0x57ffec`; the rain render `0x57ff60` is called from the scene `0x576a00` at `0x576dab`), so
  the bolts and the rain stay bright on the inverted frame; the scene queues its meshes (`[0x8BCBB8]`, submitted via `0x581cd0`) and flushes them at `0x576dbe` -> `0x582170` -> `0x581db0`, after the rain call, so call order alone does not decide it. `[BIN:TPW-EXE:0x582ad0 deferred sort]` Primitives whose state lacks 0x1000/0x2000 are not drawn when submitted (`0x565590`); `0x582ad0` draws them back to front by mean depth (+0x30, sort `0x5829e0`). The quad's key is about 0.001 (its z); the bolts' key is forced to 0 (`0x5658c0(0)` in `0x57ec50`) and the drops' key is 0 (`0x57efd0`), so the bolts and drops draw after the quad. Opaque meshes (`0x1256`) draw in `0x581db0` before that flush; translucent meshes and the sky have larger keys, so they draw before the quad and are inverted. The reverse-sort condition `0x5828f0` (`([0x8BCBC8] & 0xC) == 0xC` plus bit 0x20000000) is not verified.
   OpenTPW draws the other entities, then the quad, then the weather renderer.
* **Rain**: 10^17 / (mean squared distance from the camera to the rainy islands)^4 drops, none below 20, at most 400
  (`0x10094F8C`): about 173 at the orbit radius around hallow, drawn by the same renderer.
* **Island one-shots**: a draw with `(draw & 15) == 1` plays event 3 + (fresh draw mod 3) of the island's
  `cat_locallobbysfx` (`0x10095340`), replacing OpenTPW's earlier 5–15 s timer (AUDIO-017 is resolved).
* **Sky**: the original clears the screen to the lobby's fog colour 0x44DDFF (tp.exe `0x5D8B50`, `0x5737AA`). The colour of the
  sky's cloud layer starts black and eases toward the island's `SKYCOLOUR` each tick by (target − current) >> 4 in 16.16
  fixed point, the red channel toward the *blue* target (`0x1008FAA0`, tp.exe `0x5D96C0`); while it equals its target the
  layer takes the fog colour. The capture's cloud layer is tinted that way (grey over Lost Kingdom, pink over Wonderland,
  dark over Halloween World and Space Zone). The cloud layer is the engine sky of the fantasy level's sky files (`World/Sky.cs`), drawn over the fog-coloured clear with its
  whole grid in that colour; SKYQUALITY 0 or 1 keeps the fog colour (`0x5D96C0`); the lobby's own fog range is not applied (UI-018).
* **The globe** (`0x100923A8`) sets the rain to 0 and the sky target to (243, 203, 191) and starts its music
  (`cat_globallobbysfx` 2) and ambience (3), stopped when it closes; OpenTPW stops the lobby's own music meanwhile (`EXT:online-globe-audio`).
* **Entering a park** (`0x10183CC0`, PC `0x4b8fd0`) plays `cat_globallobbysfx` 4 flat; the original skips it for game type 2. Before the sound the PC routine starts the screen-space particle effect 0x62 over the key panel (`0x521e60` is the particle creator, not a sound), which OpenTPW does too (`LobbyEnterEffect`, docs/PARTICLES.md); Instant Action skips both.

## Weather visuals

The simulation side of the weather (`ParkWeather`) drives the visuals the same way it drives the
sound: the drawn drop count is the simulated `CurrentDrops` (capped by `MaxRaindrops`), snow shows
when the snow band holds, and every lightning strike reports its bolt top (`TakeBoltTops`, at the
strike itself; the thunder still follows later through `TakeThunder`). The look is the original
engine's `CRain` renderer, read statically from the Mac `engine_shared` library (`0x1001CF38`
`GameLoad`, `0x1001C884`, `0x1001FCE4`, `0x1001ECD0` rain, `0x1001F198` snow, `0x10020150`,
`0x1001EA18`, `0x1001E2B0`, `0x1001DE6C`, `0x1001D578` bolts, `0x101035D4` the strike) and tp.exe
(`0x57F1F0`-`0x57F380`, `0x57EEC0`). `WeatherVisualState` holds the GPU-free state and geometry
(unit-tested); `WeatherRenderer` draws it (`content/shaders/weather.shader`, textured, depth-tested
without depth writes) and reads its inputs through `IWeatherVisualSource` plus an original-to-engine
mapping and a pause predicate, so the lobby can reuse it later without `ParkWeather` or `Level`.

Behaviour worth knowing:

* Images: `Data/Generic/Weather/raindrop.tga` (32x32, no alpha, white teardrop on black), `snow.tga`
  (128x128, 2x2 atlas of four flakes) and `lightning.tga` (128x128, vertical strip), read from the game
  folder like the cursor's TGAs. `generic/sweather` holds smaller copies; which the park selects is
  unknown, so the full-size set is used. A missing image just skips that weather.
* Rain and snow share one field of 1024 fixed random points in a box of -4..4 (original units) around
  the camera eye, in camera space through the view rotation only, wrapping per axis. The box scrolls
  along its fall direction (straight down) by 2.5 units a second for rain (the game's value; the engine
  default is 5) and 0.8 for snow, plus 0.1 x the camera's movement as parallax. The original's parallax
  is the wrapped absolute camera position times a factor (`CRain +0x3340`, 0.1; 0.3 while the camera-mode
  byte `0x790ab0 & 0x16` is set, `0x57fff8`-`0x58000b`), per axis `wrap(f x position + 4) - 4` into
  [-4, 4) (x and z `+f`, y `-f`; `0x580013`-`0x5800b7`), and the draw (`0x57efd0`) subtracts it from x and z
  and adds it to y, so every axis moves by `-f x position`; OpenTPW wraps `fall - f x eye` the same way on the same
  1/16384 grid, with f = 0.3 in its free-walk and ride cameras and 0.1 otherwise. The bits `0x16` of the camera-mode byte are the park's two
  first-person modes (AUDIO-900 is retired): `0x42AB20` toggles bit `0x2` (the terrain camcorder, input event 2, OpenTPW's free walk) and bit `0x4` (the ride follow, event 4) and clears
  them leaving a mode (`0x42AB5E`-`0x42AB78`), and no instruction sets `0x10` (every other store of the byte sets `0x80`, `0x100`, `0x200`, `0x800`, `0x1000` or clears bits; the only
  wider writes read the byte from a stream, `0x42CE1B` and `0x42CF61`, or put back a copy, `0x42B561`), so the overview and the lobby take 0.1; the fog, the listener and the ride-view cancel use the same test. At the original scale
  (0.2 engine units a unit) the field is a small near-field around the eye: streaks are 0.1 units
  long and 0.2 x that wide. Nothing of the rain and snow is cut before the eye and the bolts are clipped at 0.1 original
  units, as the original (`0x578be0`, AUDIO-904 is retired). The original's camera passes 90 degrees (`0x42a990`, pi/2) to
  `0x578be0`, and OpenTPW's park camera now uses the same projection (`OriginalProjection`, the pick frustum's half height 0.5).
* **Projection.** The original does not project its rain and snow: the vertices (x, y, z, w) are submitted to the polygon path as clip coordinates (`0x57F1F0`, `0x57F490`) and the viewport (`0x5796A0`) puts a vertex at `x / w` and `y / w`
  of the half screen (y down), so the weather neither takes the projection's nor the screen matrix's factors (`2 a` in x, `-2` in y). OpenTPW sends every weather vertex to the GPU as a clip-space vec4 position (x, -y, depth x w, w, the shader takes w from the tint's
  third component) with identity matrices (`WeatherProjection`), so the rasteriser interpolates texture coordinates and colour perspective-correctly across a streak, as the original's polygon path does. A drop's position, its size on the screen and the display aspect
  word `0x8BCBCC` (height / width: a flake's base half-width is `h x` it, so the flake is as wide as it is tall) are the original's at any window size.
  The depth value has two ranges in the original (the viewport `0x5796FF`, set up by `0x583220` every frame from the far argument D of the view's projection setup `0x578CA0`): below the clip-z threshold `m10 (t - n)`, with `m10 = sin(fov / 2) / (1 - n / D)` and
  `t = max(0.1, 0.003 (D + m10 n) / m10)`, it is linear in z (no divide by w; the compare is unsigned, so a negative z takes the other range), above it `K z / w + c`. The threshold is about 2.93 for D = 1000, 1.13 for 400 and 0.23 for 100. OpenTPW writes the depth in its own scene
  depth space: the equivalent distance of a surface is `n + z / m10` in the linear range and `n / (1 - z / w (1 - n / D))` (near 0.1) above it, and the same distance in the scene's projection gives the value written, so a drop is hidden by exactly the world surfaces the
  original's hides it behind. A streak vertex beyond the far plane (clip z > w) is clipped at z = w as the polygon path's clipper does (`0x562A70`); the equivalent distance is infinite beyond it and the depth 1.
  D is the argument of the frame, never a constant. The rain's only caller (`0x576DAB` in `0x576A00`) runs after the view's span selection, which is `0x56EA90` while `[0x790AB0] & 0x16` (`0x576A9F`: the frame function sets bit 0x100 of `[0x8BD508]` for the frame and restores the flags
  at its end, `0x577326`) and `0x56BB00` otherwise. The overview's (and the lobby's) is the span selection's farthest corner distance, at least 10 and with no upper limit (`0x56BD3E`; `TerrainVisibleSpans`, the lobby with its terrain's heightfield, `ISLANDFOV` rays and the viewing distance of FIRSTPERSONVIEWDISTANCE).
  The eye views' is the greatest clip z of the corners of the heightfield cell boxes the view reaches, at least 100: `0x56EA90` first calls `0x578CA0(1000)` (`0x56EAC0`), which only builds the matrix `[0x87B088]` the box test uses (`0x5783B0`), sets `[this + 0x1C]` to 100 (`0x56EAD3`), takes the
  rectangle of cells within half the viewing distance `[0x8BD4C0]` of the camera (460 without the fog bit 0x2000, which the eye views set; with bit 0x20 of `[0x8BCBC8]` the whole field is searched instead, `0x56EB03` to `0x56EB21`: only a clear of that bit was found, `0x54E8D3`, no setter, so it stays clear and `TerrainEyeViewFar` takes `wholeField` as false) and searches it (`0x56E7E0`: the eight corners of the box between the field's lowest and highest height are transformed, tested by `0x561BC0` against the six
  planes through an outcode table, dropped when all lie beyond one plane, taken whole when inside or one cell, else split at the middle cell), raising `[this + 0x1C]` to the largest clip z of each box taken; then `0x56ED79` calls `0x578CA0` with it. The 1000 stays only when the rectangle lies outside the field (`TerrainEyeViewFar`, which
  reproduces the original's code run offline). The larger D is, the larger the linear range (about 0.003 D), so the share of the 8-unit drop field in it depends on the view.
* Rain: grey `3 x max(0, int(12 x (6 - depth)))` (216 at the eye, 0 at depth 6) of the trailing end's depth for both ends, alpha 0x80 at the
  streak's trailing end and 0xFF at the leading end, only a trailing end with depth > 0 drawn (the leading end is not tested). A streak is the four clip vertices `A + (q, -p)`, `A - (q, -p)`, `B - (q, -p)`, `B + (q, -p)` with `A` the
  trailing end (y negated), `B` the leading end, `p = 0.1 dx`, `q = 0.1 dy` of the projected streak `d`, so its width follows its projected length (`0x57F1F0`), all with clip z = trailing depth + 0.04 and w = the vertex's own end's depth + 0.1. The render states come from tp.exe's
  decoder `0x567620`: rain and bolts `0x200D52` (source alpha over one, no depth writes), snow `0x556` (alpha blend,
  depth writes), each with the alpha test at 0x10 of 255; the rain's `0x200D22` (one plus one) is only a fallback
  for the chips flagged at device init (`0x566840`).
* Snow: one triangle a flake, half-size 0.1, its apex at the top middle of atlas quadrant `index & 3` and its base
  at the quadrant's lower corners, full white, x and y spread by `1 + depth/8`, jittering by
  `((rand & 31) - 16) / 2048` on every axis each drawn frame (`0x57f490`; `[EXT:frame-driven-at-60hz]`: OpenTPW redraws the jitter, and the bolts' flicker,
  once per 60 Hz reference frame). The original's other mode swirls flakes along
  a closed 128-point spline under engine flag `0x8bcbc8` bit `0x100`, which nothing in tp.exe sets, so the PC
  draws only the jitter version. The flake's vertices are homogeneous clip coordinates (x, y, z + 0.1, w = z + 0.1501) handed to the polygon path, the apex above the centre and the base `h x 0x8BCBCC`
  to each side, which OpenTPW reproduces exactly with the depth value written as described under "Projection" (AUDIO-901 is retired; the former `[EXT:snow-depth-value]` is gone). The original's textures keep the TGA's bottom-up rows
  (v runs up the picture), OpenTPW's are upright, so the flake's apex reads the lower edge of its atlas quadrant and the streak's trailing end the picture's top.
* Bolts: from the ground point (x, 0, z) the strike drew (the original's bolt does not follow the terrain) to the sky point 300 up and offset by up to
  50 in x and z (`0x512C50`), a recursive midpoint walk (`0x57E810`) on the CRain stream (`0x57E4B0`: the midpoint moves along the
  vector P, the segment's middle with one axis replaced by `M - |M|^2 / d`, by `|d| x 0.6 x r2 x r1^2`, r the signed remainder of a draw over 0x1FFF
  divided by 8192) that splits a half again while its squared length is at least `6 + (draw & 0xFFF - 0x800) x 2 / 2048`
  (tested on each half, so a final segment is under 2.83 units), a 9/512 branch chance per split up to depth 4 (the heading
  `along-bolt x the bolt direction + along-segment x the segment direction`, lengths `(0x200 + draw & 0x3FF) / (96 x depth)`, along-segment x 2.5, the
  along-bolt term halved and the sum halved when it points against the bolt), the shape fixed per bolt from its seed word, a ribbon of
  half-width `1.3 / (depth + 1)`, grey pulsing with the remaining life (`0x57EE30`: x = (remaining ms mod 500) / 500; 255 - trunc((0.9 - x) x 71.04) below 0.9, else
  192 + trunc((1 - x) x 640); plus rand & 63; the three channels take the sum's low byte, so a sum of 256 or more wraps dark), alive
  `WeatherEffects.LightningTime` (2000 ms) with up to 16 at once. There is no screen flash and no sky darkening in the park. The ribbon is the original's emitter `0x57DD10` (AUDIO-902 is retired, `WeatherBoltRibbon`): the walk's emit calls (a point pair at a branch depth, or the closing call with no first point, a branch
  resetting its depth's counter; `0x57E810`, `0x57EC50`) project each point through the scene's view-projection-screen matrix (`0x578D20`, the matrix `[0x87B088]`) and the ribbon is built in clip xy: per depth the previous projected point, its direction scaled to
  `1.3 / (depth + 1)` clip units, a call counter and the corner pair; the first call stores the start corners (point plus and minus the perpendicular) and emits nothing, each later call emits the quad (old corner 0, old corner 1, new corner 1, new corner 0) whose new
  corners are the mitre at the previous point (the perpendicular of the normalised sum of the two directions, with that point's z and w) and keeps them, and the closing call emits the end cap at its point, so a quad is emitted when the point after next arrives and a
  segment's width on the screen is `1.3 / (depth + 1) / w`. The quad is the fan (0, 1, 2), (0, 2, 3) with the texture corners (u0, v0), (u1, v0), (u1, v1), (u0, v1). OpenTPW bounds a bolt
  at 1024 segments for its vertex buffer (the longest of 20000 sampled bolts had 796).
* Time is the frame time scaled by the simulation's time scale and capped at 500 ms, as the original's
  dt on its rate-scaled clock `0x402d70` (`0x57ff60`), so it stands still while the park is paused; the
  engine also zeroes dt while the mask 0x4 of its word `0x8bd508` is set (`0x57ffb1`), which nothing in tp.exe ever sets (the word is .bss, the stores that
  exist set 0x1, 0x40, 0x80000000, 0x8000000 or the constant 0x3000141 that clears it, or put back a copy of the word itself), so OpenTPW's never
  zeroing it matches. The visual random stream is separate from every simulation stream, so world hashes are unchanged.
* The renderer is created with the weather itself (`ParkWeather.Created`), inside the staged-candidate
  scope when a save is resumed, and deleted with the level's other entities. GPU allocations are made
  once (one vertex buffer, three materials), browser-safe; the shader is precompiled for the browser in
  `content/shaders/web/weather.json`.
* The forecast is presented by `WeatherForecastPresenter` — see "Forecast warnings" below; the
  pending-quality state the original's message handler would read is `ParkWeather.Forecast`.

### Near-plane measurements (October 27, 2026)

AUDIO-904 is retired (user decision, October 27, 2026): the original near-plane behaviour is the only one and `[EXT:weather-near-plane]` and the `OPENTPW_WEATHER_NEAR` switch are removed. The measurements that led to it, kept as history (both variants were built; macOS Metal, Debug, offscreen; D3D11, Vulkan and WebGL were not run):

* Share of drawn drops the scene near plane cuts (2 million uniform points of the 8-unit box, depth above 0): rain 5.0 %, snow 2.5 % (the real-depth cut before the depth port was 12.5 %); the original cuts none.
* Depth precision: the weather writes its depth in the scene's own depth space (`WeatherProjection.WriteDepth`), so the original variant needs no near plane of its own and no change of the scene's near plane (0.1 engine units); the 24-bit step at 10 units stays 6e-5 units.
  A near plane of 0.02 for the whole camera would make it 3e-4 (five times coarser), which is the cost the EXT avoids; the weather variant does not pay it.
* Captures (first-person walk, rain; not kept in the repository): the original variant adds the drops within about 0.2 units of the eye, a few large streaks (one at the left edge); no depth artifact was seen in either.

Result: the weather's depth is written in the scene's own depth space (`WeatherProjection.WriteDepth`), held at 0 or above; the scene's near plane is untouched and the weather has none of its own.

### Forecast warnings

`ParkWeather`'s forecast (a quality rolled `Weather.DaysOfWarning` days ahead and applied when its
timestamp passes; see "Weather schedule" below) is presented by `WeatherForecastState` (pure, unit-tested) and `WeatherForecastPresenter`
(the entity that draws it). The presenter is created with the weather like the renderer, reads the
simulation each frame, and draws one banner line at the top centre of the screen in the overlay pass
(the chat overlay's channel: the original GAME8AA BF4 font, above every screen including pause
menus; `[EXT:forecast-banner]`). One line, one background rectangle, laid out again only when the text changes;
no per-frame GPU growth. The banner belongs to the park's view: while Go Online shows the online globe in the park's
place (its globe camera replaces the park's), the banner waits, and the paused park holds its line until the park returns.

What it shows (all of it an OpenTPW extension, `[EXT:forecast-wording]` and `[EXT:forecast-cadence]`:
the PC has no forecast message, and no weather text exists in the shipped `Language` string tables,
which were searched):

* A park entered or loaded with weather on screen says so once: "The weather: rain." for six
  seconds of simulation time (a dry, quiet park stays silent). A paused park freezes the line
  mid-display; leaving the park or loading another ends it with the entity.
* A pending forecast of another kind of weather than the current one shows the countdown
  "Forecast: thunderstorms in 4 days." (tomorrow/today on the last days) from the apply day and the
  current simulation day, and posts the same line as a message to the log. One message when such a
  forecast appears, one when the kind of weather changes — never per step, and nothing for a
  forecast or an apply that keeps the kind of weather: the original rolls a forecast every cycle
  whatever the weather (tp.exe `0x512880`), so dry weather would otherwise be announced as a change
  to dry weather every week. A change and the next forecast in one update post both; a park resumed
  mid-warning announces the pending forecast once.
* The forecast's kind classifies the pending quality by the converter's bands: the snow/rain tie is
  still undecided ("snow or rain") because the apply-time draw has not happened, and the lightning
  band composes "thunderstorms". The apply message names the resolved weather ("The weather has
  changed: snow.").
* The lines are worded in the selected game language, like OpenTPW's other own labels
  (`SupplementaryStrings`, the nine shipped languages; English for any other): in Dutch the countdown
  reads "Weerbericht: morgen regen.". The countdown is worded again on every step, so a language
  switch shows at once; a transient line keeps the language it was made in for its six seconds.

Seeing it: enter any park whose economy runs (an original level; the sandbox has no weather). The
weather follows the season's roll, so rain needs the roll to land in the rain or snow band — resume
a save made during rain, or wait for the next change (`Weather.DaysBetweenChanges`, seven days by
default). Debug builds also take `--debug-weather <quality 1..100>` (`[EXT:debug-weather]`, in
`Client/Game.cs`): it applies one quality through `ParkWeather`'s own converter as soon as the park's
weather exists, so captures can show rain, snow and bolts deterministically — e.g.
`--load-original-level jungle --debug-weather 8 --capture-world rain.png --capture-frames 30`
(quality 8: rain and lightning on jungle, snow and lightning on hallow). The forecast banner is
forced the same way with `--debug-forecast <quality 1..100>` (`[EXT:debug-forecast]`), which posts
one forecast through the simulation's own warning schedule; the banner draws in the overlay pass, so
captures need `--capture-interface` to include it — e.g.
`--load-original-level jungle --debug-forecast 8 --capture-interface forecast.png --capture-frames 30`.
The unit tests (`WeatherVisualTests`, `WeatherForecastTests`) cover the state math without a window:
counts, the 1024-point field and its scroll, the streak and flake geometry, bolt shape/lifetime/colour, the strike reporting,
and the forecast's announcements, countdown, changed lines and seams.

### Weather schedule

The PC weather (tp.exe) runs on game-clock timestamps (`0x4f8690`, 10^7 units a second, `0x4f8770`),
not day indices, and OpenTPW's `ParkWeather` follows it on game seconds sampled at the park turn:

* The constructor (`0x511f70`) stores the season's roll as the quality but never calls the converter
  (`0x512f00`), so the constructed quality has no effect (no rain or snow, drops 0), and sets
  `lastChanged` to **0**. The first update therefore rolls a forecast at once (AUDIO-027 is retired).
* A roll happens when no forecast is pending (flag `+0x2c`) and now >= `lastChanged` + max(0,
  `DaysBetweenChanges` - `DaysOfWarning`) days (`0x51295c`-`0x5129d0`); the weather's own handler
  (`0x5127a0`) stores the pending time (now + `DaysOfWarning` days, `+0x20`), the quality (`+0x28`), the
  flag and `mLastForecasted`.
* The apply happens when now >= `+0x20` (unless `mOverridden`, `+0x64`): the converter sets the quality and
  `lastChanged` = now (`0x512f5b`), and **the same tick falls through to the roll test** (so a cycle of
  `DaysBetweenChanges` <= `DaysOfWarning` rolls again at once).
* OpenTPW models `lastChanged = 0` as a first roll due at the first step: the original's "now" is the
  calendar's FILETIME base plus floor(15000 x turn / 4) seconds (`0x4f8690`), which changes only with the
  park turn and is `ParkCalendar.Seconds`, so a zero `lastChanged` (1601) makes the first update roll at
  once (AUDIO-026 is retired). The snapshot's `LastChanged`/`NextRollDay`/`ApplyDay` fields now hold game seconds; a save
  from before this change held day indices, and `ParkWeather.MigrateSchedule` converts it on restore: for a started
  weather at least a day old whose three values are all <= `DayIndex + DaysOfWarning + DaysBetweenChanges + 1`
  they can only be day indices and are multiplied by 86,400.
* The weather's draws come from the park's own weather stream (`[EXT:weather-stream]`); the PC draws them from the
  world LCG `0x516330` (`ror13(s * 0x19660D + 0x3C6EF35F)`, the rotated value stored; seeded with
  `time(NULL)` at world creation, `0x51586b`), shared by about 139 call sites, so reproducing its draw
  order needs every user of that LCG ([DETERMINISM.md](DETERMINISM.md)).

The thing's own rain voice follows its **mode** (`0x5131f0`): rain starts a flat, unparameterised
`cat_ambient` 0x21 voice, snow and clear stop it (`GameAudio.SetWeatherRain`), while the level-driven voice
(`GameAudio.SetRain`, parameter 8) keeps following the drop count ×100/`MaxRaindrops` in every weather, snow
included, until the drops reach 0 (`0x512a7f`).

## Advisor response table

The game asks the advisor for a *response* ID; the response table says which speech
sample, LIP file, animation and model that is, and whether the clip comes from the
global speech bank or the level's (`Speech/speechHD.SDT` and `Speech/lips`). The
table is shipped as `content/data/advisor-responses.toml` (610 responses), copied for
interoperability from the Mac application (offset 0x18FF4; see the provenance
exception in COMPLETION-PLAN.md) and regenerated or checked with
`tools/ppc-analysis/lanes/advisor/response_table.py <SimTheme Park> --write|--check <file>`.
Response 0 is the error sound (sample 638, no LIP); responses 1 and 399–402 use the
level bank's `sp_001`. `--advisor-response N` (with `--load-original-level <level>`,
default jungle) plays a response.

Which response the game asks for, and when, is decided by the advisor controller
(scores from `Advisor/Advisor.sam`, docs/reverse/PPC-advisor.md); that is not
implemented yet.

## Approximation register

AUDIO-039 is retired (October 19, 2026): the reverb is the user-approved design choice `[EXT:audio-reverb]` (see "Reverb").

AUDIO-003 is retired (October 28, 2026): the fixed-pitch flag `0x10000` (`0x6bc484`) is unreachable. The voice flag word is built from the event word by `0x6bbe90` (`SoundVoiceFlags.FromEvent`: event `0x8` → 6, `0x4` → 2, `0x200` → 8, `0x20` → `0x200`, `0x4000` → `0x20000`, a range scale → `0x80`) and every other write to it (`0x6bbe40`, `0x6bc7c0`, `0x6bc7fc`, `0x6bc9ee`, `0x6bca60`, `0x6bca79`, `0x6bcae0`, `0x6bcb0a`, `0x6bcc77`, `0x6bd5c2`, `0x6bd83a`, `0x6bd8e3`, `0x6bd8ff`, `0x6bdab6`, `0x6bdb32`, `0x6bdf8a`, `0x6be044`, `0x6be060`, `0x6be567`, `0x6be5cc`, `0x6be891`, `0x6bea75`, `0x6beaa1`) sets or clears one of the bits 0x1-0x80 and 0x100-0x8000 or stores 0; no immediate `0x10000` or `0xFFFEFFFF` exists in the library outside the reader and the mixer handle's own flag word (`0x6c51d3`, `0x6d20d8`), and `NoEventWordMakesTheFixedPitchVoiceFlag` covers all 65,536 event words. Its reverb remainder was the design choice `[EXT:audio-reverb]`, which stays; the pitch draw, the delay, the pool timing and the positioned-only placement were ported before.

AUDIO-900, AUDIO-901, AUDIO-902 and AUDIO-904 are retired (October 27, 2026): the rain parallax factor 0.3 belongs to the camcorder and the ride follow, the only views that set the camera-mode bits `0x16`; the flake and the streak are the original's clip vertices from their screen position with the display aspect word; and the bolt ribbon is the original's emitter `0x57DD10` with its mitred joins, projection and quad order ("Weather visuals").

AUDIO-004 is retired. `cat_ui` 0xBD plays instead of 0x1F when the receiver of message 0x100 has style bit 0x10 (hook `0x485780`, the choice at `0x4857cb`, called by the dispatcher `0x658d9f` and the queue pump `0x658e42` at `0x658eb2`). The button class `0x668f9c` is the only sender of 0x100 for a mouse release over a button: it posts 0x100 to its parent (`[+0x60]`) at `0x6691eb` through the queue `0x658ccc`. Window procedures also send 0x100 to their own window (`0x48d83b`, `0x49973e`, `0x49ec53`, `0x4a6df5` (via the dispatcher `0x658d9f`), `0x4b640a`, `0x4b9f06`, `0x4bb020` and `0x4bfc29`; ids -1, -2 and 0xe059, on 0x802 or 0x804), and the hook clicks for those too by that window's own style (style 1 in all eight: 0x1F). Those parents are the vertical list scroll bars (type 3, style 0x10: their arrows), the sortable lists (type 7, styles 0x91 and 0x291: their header buttons) and the radio groups (type 8, style 0x11: their toggle tabs). A held arrow waits 500 ms and then steps every 125 ms (ctor `0x6688b3`, timer `0x668dd9`); the release `0x668eff` plays nothing once repeating has started. No list owner adds a row click ("List owners" below); `UiScrollList` is silent by default and only OpenTPW's own screens opt in.

| ID | Approximation | Evidence needed |
| --- | --- | --- |
| AUDIO-018 | A positioned **mono** voice follows QMixer.dll's software channel stage, which `tp.exe` selects for every voice: the request flag 1 is ORed in unconditionally (`0x6bc61b`, after both the flat and the positioned branch of the sample start `0x6bc410`), so `0x6d23ab`-`0x6d23bb` hands `QSWaveMixConfigureChannel` the mode `0x1000000`, whose buffers have no DirectSound 3D object (that is mode `0x2000000`, `0x18005056`, `0x18005915`). The offset to the source is rotated into the listener's basis (the camera matrix rows `0x7909b0` front and `0x7909a0` up through `0x51c1d0`; QMixer normalises both, right = up × front, up' = front × right, `0x1800ce70`, `0x1800cf10`), the azimuth is `atan2(x, z)` in degrees (`0x18009dc0` → `0x1800cb10`, `fpatan` at `0x1800c7b0`), the DirectSound pan is 44.444 hundredths of a dB per degree, mirrored behind the listener so it stays within ±4000 (`0x18005730`), the gain becomes 2000·log10(gain) hundredths of a dB rounded (`0x1800ca40`; 1 → 0, 0 or less → −10000), and `0x18005830` calls SetVolume (`+0x3c`) and SetPan (`+0x40`) only when the wave format table `0x1803c6b4` says one channel (`0x18005982`; the table is {0,1,1,1,2,2,2,4,4,4} by format index, with the bits per sample {0,8,16,32,8,16,32,8,16,32} at `0x1803c6dc`): the louder channel keeps the level, the other is 10^(−\|pan\|/2000) of it (at 90° 0.01), so the centre is not 3 dB down (`AudioListener.DirectSoundPan`, `PanGains`, `Azimuth`; the park, free-walk and ride cameras pass their up vector so the pitch counts). **The axis mapping is read** (October 28, 2026): the driver byte `+8` starts as 2 (`0x6d6c54`-`0x6d6c5e`) and nothing writes it, so the adapters `0x6d2640` (source), `0x6d2a10` (listener position) and `0x6d2b20` (front and up) hand QMixer (x, z, y); QMixer negates the third component of every vector it receives when its engine byte `+4` is set (`0x18001020`), which it is because `tp.exe`'s `QSWaveMixInitEx` structure is zeroed (`0x6d1f92`) and the byte is the inverse of bit 3 of its flags (`0x18008661`-`0x1800866c`). The two reflections make the rotation (x, y, z) → (x, z, −y), so the pan keeps the side of the unmapped camera axes (`AudioQmixerVoiceTests` replays the whole pipeline). **The flat voices are read**: a voice with voice flag 8 (the event's any-distance flag `0x200`, `0x6bbe90`: speech, music and most interface sounds, every `cat_speechSFX.map` event) takes the flat branch of `0x6bc410`, which gives it the raw dwords (0, 1, 0) (`0x6bc51d`, `0x6bb220`), no distance mapping and no request flag `0x20`, so `0x6b7fd6` reaches `0x6d2640` with its flag clear and `QSWaveMixSetPosition` (`0x6fd2b8`) runs; QMixer's message 6 (`0x18003e40`, handler `0x18007abd`) reads three integers (azimuth in degrees, range, elevation), turns the range into 1 × 3.0 / 32767 (the room size `0x180086ef` that `tp.exe` never changes) and the channel takes the polar branch of `0x18009dc0`: azimuth 0, elevation 0, distance 9.2e-5, inside the default minimum distance 1 (`0x180310f0`), so the distance gain is 1 (`0x1800a0cf`-`0x1800a12f`) and the pan is 0: both channels at the voice's own gain (`AudioVoice.PlacedFlat`, set for every event with `0x200` even when it carries a place) | QMixer's Doppler factor: every voice without the loop flag (voice flag 4 makes the request flag `0x40` through `0x6bc628` → `0x6c5110`, which `0x6d22d0` turns into the channel's no-Doppler bit `0x10`, tested at `0x1800a1df`) plays at its frequency times 1 / (1 + v / 331.36), at most 10 (`0x1800a24c`-`0x1800a2b7`, applied by `0x18005550`), from the listener velocity `tp.exe` sends as the position change of each listener update (`0x6b79d0` → `0x6d2a10` → `0x6d27b0` → `QSWaveMixSetListenerVelocity`: current minus previous position, previous starting at the origin, no time base, only sent when the camera changed, `0x6b8b60`), so the first update of a park gives the listener its whole position as a velocity; the source has none (no `SetSourceVelocity` import). OpenTPW plays these voices at their own pitch. The volume's rounding to hundredths of a decibel covers the distance gain only, not its product with the voice's own volume (at most 0.005 dB) |
| AUDIO-040 | FADEOBJ uses configured quality > threshold, service-owned integer-decay tails, shared 100/250 ms polls, subtype-specific stop/continuation and terminal cleanup. The poll's effective volume is the worker thread's arithmetic: `0x6BB860` forms the product of the enabled group floats and the volume in the x87 registers and stores it once as a float before `fistp` (round to nearest even); the poll runs on the library's own `_beginthread` worker (`0x6B6260`, `0x6B9E30`, started by `0x6B9EF0`; the manager tick `0x6B62B0` calls the fade update only when the byte `+0x50` is set, which `0x6B9F66` clears), a new thread at the operating system's control word `0x27F` (53-bit precision), whereas the main thread is at `0x7F` after `DDSCL_FPUSETUP` (24 bits); the only other writers of the word are the CRT wrappers and the font generator's `0x591220`/`0x591240` (`0x576FA7`, `0x577304`, `0x577310`). The poll therefore multiplies in doubles (`HeldSoundLifetime.PollEffectiveVolume`: master 0.75 and effects 0.7 on a volume of 60 are 32, the main thread's single-precision product gives 31), every group write and sample start in singles. AUDIO-040 retains the host monotonic startup phase and the serialized poll/admission order of the original's `Sleep(100/250)` worker (operating-system scheduling, nothing to port), and a missing host SoundQuality taking the loaded SAM DEFAULTQUALITY (supplied 50; FADETOGGLE 30) instead of the original user's saved preference; these are explicit compatibility policies, not observed original timing. AUDIO-038 admission/restart and existing general sentence approximations remain separate. | The worker's scheduling cannot be read statically (a user decision would be needed to make it an extension); see the [lifecycle contract](proposals/audio-040-plan-soundquality-fade-mode.md). |
| AUDIO-038 | Range-stop acknowledgment is traced through `0x6BB7C0` → `0x6C8E50` → `0x6BBE40`: it clears sample handle `+0x24` and playing state while range bit `0x800` preserves the logical owner. Catalogue loops release output and recreate the retained selected source on a later rank/newest-admitted tick, preserving the owner serial, parameter state and terminal-stop/fade gates. Held-chain roots suppress new children while excluded; their locally linked, directly ticked children keep playing and are not extra manager-pool entries. | Finite one-shot expiry/late-start recreation and sentence selected-descriptor/refill mapping remain unresolved, as do initial submission admission, broader group-enable mapping and exact codec/device buffering. See the [bounded contract and synthetic cases](proposals/audio-038-trace-out-of-range-voice-resumption.md). |

### List owners (October 18 and 19, 2026)

AUDIO-041 is retired (October 19, 2026): **no list row of tp.exe clicks**, a still click on a scroll thumb plays nothing, and the
keyboard's clicks are mapped to OpenTPW's dialogs. Static evidence only (tp.exe); no runtime capture.

**Why a row cannot click.** The hook `0x485780` plays 0x1F or 0xBD only for message 0x100 (`0x4857b8`, the choice at `0x4857cb`);
no other site plays either id (every `0x51BFC0` call with `cat_ui` and every caller of the wrapper `0x485AA0` passes another id). The
hook runs only in the dispatcher `0x658d9f` (`0x658dd0`) and the queue pump `0x658e42` (`0x658eb2`), and the queue is filled only by
`0x658ccc` (`0x6697e3` has no other caller). Every message argument of those two senders is an immediate except five that forward or
repost a message they received (`0x65dd0a` adds 0x1000; `0x6651ba`, `0x66bc42`; `0x4a2c82` and `0x4a3ae0` send 6). So the only 0x100
senders are the button class's release (`0x6691eb`; the class `0x668f9c` is installed only by the type-2 constructor `0x6688b3`) and
eight window procedures (below). The row path of the type-7 list never reaches them:

- The type-7 constructor `0x662562` installs procedure `0x665c35` at instance `+0x114` (`0x662718`), also saving it
  at `+0x118`. Dispatch `0x65f697` calls that procedure; the screen builder `0x65fd0e` overrides the root only.
  The list instances keep the type-7 procedure; the one owner that installs its own on a list, the buy window (`0x4ad00d`:
  `0x4aca60` on `0x1f8`), handles its category toggles (0x101) and passes the rest on (`0x65f66e`), sending no 0x100 and playing nothing.
- Base `0x65f6d1` turns eligible pointer release/double-click into `0x10006`/`0x10007`; hit helper `0x65dcaf`
  reposts `0x11006`/`0x11007`. Type-7 helpers `0x6655a2` and `0x6656fa` notify the owner with activation `0x400`
  for a valid row. The common base tail (`0x665e6a`) does not convert these into button message `0x100`.
- Selection helper `0x66525c` sends `0x401`, columns `0x665494` send `0x404`, alternate clicks `0x66563d` send
  `0x402`, navigation may send viewport `0x405`, and sorting sends `0x406`. These are distinct notifications,
  not interchangeable click events. Header handler `0x665a44` starts from an incoming button `0x100`, whose
  sound the hook has already handled. No header/arrow/button sound is removed by the row correction.
- Queue/pump `0x658ccc`/`0x658e42` and immediate dispatcher `0x658d9f` preserve the message reaching hook
  `0x485780`; its click branch requires `0x100`. The highlight-model callbacks change visibility/geometry
  (`0x476bf0`, `0x476f10`), rather than dispatching a UI button message or sound.

| Owner | Row route | Scope of the conclusion |
| --- | --- | --- |
| Load `0x49e880`, layout `0x7523f0`, list `0x23bce0`, type 7/style `0x81` | `0x49e901` accepts `0x400`, resolves the row, calls load `0x5ac5d0`, then closes the root | No generic or dedicated row-click event. Self-`0x100` at `0x49ec53` belongs to the separate Save handler, not Load. |
| Find Parks `0x4bf9f0`, layout `0x758f40`, list `0xe05b`, type 7/style `0x91` | `0x4bfb5a` accepts a valid `0x400` row and opens Park Details `0x4c0ae0` | No generic or dedicated row-click event. Self-`0x100` at `0x4bfc29` belongs to search submit `0x802`, not the row. |
| Other Parks `0x4c1a10`, layout `0x7593e8`, list `0x14beb`, type 7/style `0x91` | `0x4c1aec` accepts a valid `0x400` row and opens Park Details `0x4c0ae0` | No generic or dedicated row-click event. Other row notifications fall through the saved base procedure. |

The immediate Park Details builder populates and focuses the window and posts advisor help (`0x4c18dc`), without
synthesizing `0x100` or playing a UI click. This is **not** a claim that later loading, networking, advisor speech
or gameplay activity is silent, and it is not an original runtime audio capture.

**Every owner.** The type-7 constructor `0x662562` has one caller, the control factory `0x65dd7b`, and no direct factory call asks for
type 7: they come from layout records (the stream reader `0x65fd58`, `0x65fe40`; the text-layout reader `0x66080d` hangs off `0x6607ca`,
which nothing calls). The 66 layout tables the window builders pass (`0x65fd0e`, `0x47ed80`, `0x48cea0`) hold 19 type-7 lists in 18 tables;
each list's parent is its window, whose procedure is the one the builder installs. A walk of every direct call reachable from each
procedure finds no 0x100 send except the two keyboard paths already excluded, and these `cat_ui` sounds only:

| Owner (procedure), list, style | Row messages | Sound on the row path | OpenTPW |
| --- | --- | --- | --- |
| Load `0x49e880`, `0x23bce0`, 0x81 | 0x400 loads | none (the load's own sounds are downstream) | Load screen, silent |
| Save `0x49ea30`, `0x23bce0`, 0x81 | 0x400 picks the slot | none; Enter and Escape click (below) | no save screen (OpenTPW saves at once) |
| Find Parks `0x4bf9f0`, `0xe05b`, 0x91 | 0x400 details | none; Enter in the search field clicks (below) | Find Parks, silent rows |
| Other Parks `0x4c1a10`, `0x14beb`, 0x91 | 0x400 details | none | Other Parks, silent |
| Visitors `0x493230`, `0x1e497`, 0x291 | 0x400 visitor window `0x4b79b0`, 0x402 locate and close, 0x406 sort | none | `VisitorList`, silent rows |
| Overview item lists `0x4955e0`, `0x190`, 0x291 (three tables) | info, locate, sort | none | `AttractionList`, silent rows |
| Staff `0x495da0`, `0x321`, 0x291 | 0x400 employee window | none | `StaffList` (employee view), silent |
| Hire `0x49b650`, `0x248e`, 0x291 | 0x400 hire `0x507bf0`, close, help 0x135 | none | `StaffList` (applicants); the refusal's error sound is removed |
| Loans `0x49f4e0`, `0x12c4bd`, 0x91 | 0x400 confirmation | none | `LoanList`, silent |
| Buy `0x4ac270`, `0x1f8`, 0x91 | 0x400 buys (`0x4ac4f9`) | 0x1D when the money or tickets do not cover the row (`0x4ac7f6`) | `BuyItemList`, 0x1D on a refused purchase |
| Upgrades `0x4ae430`, `0x1f4d`, 0x81 | 0x400 buys an add-on (`0x4af480`) | 0x1D when the money does not cover an add-on (`0x4af5d6`) | `UpgradeList`, 0x1D on a refused add-on |
| Staff dialog `0x4b4fc0`, `0x1e7b`, 0x91 | rename, pick-up | the carry tool's own sounds downstream | no counterpart |
| Postcard addresses `0x4a73d0`, `0x26`, 0x11 | 0x400 | none | online extension (EXT) |
| Unsent postcards `0x4bbc20`, `0xee8`, 0x11 | 0x401 | none | Outbox, rows not activatable |
| Visit chat `0x4bd4a0`, `0xc3124` 0x91 and `0xc312b` 0x1 | no row handler | none (its 0x95 at `0x4a9380` is a key command) | `ChatPanel` lists, silent |
| Dial-up chooser `0x4ba530`, `0xc`, 0x1 | none read | none | no dial-up |
| IME candidates `0x480d10` → `0x4809f0`, `0x7b79e3`, 0x81 | 0x400 picks the candidate (`ImmNotifyIME`) | none | no counterpart |

The other control types (`0x65ab64`, `0x665fcd`, `0x66d9e0`, `0x66a1e8`, `0x66d750`, `0x66d0c0`, `0x66c970`, `0x66a4c0`, `0x66c7e0`) do not
derive from the button constructor and send no 0x100, and the row templates that draw rows (`0x4938c0`, `0x4944d0`, `0x496cbd`, `0x4b4c70`)
build types 1, 6 and 9 only, so no custom row class can click. The 0x1D refusals are downstream domain feedback, which OpenTPW already plays.

**The scroll thumb.** The thumb (`b_scroller`, layout op 8: type 2, style 1, id 3) gets the procedure `0x66bb9b` (`0x65e526` overwrites
the button procedure at `+0x114`); its press (0x10005) captures, its release (0x10004) sends 0x802 to the slider (`0x66b9ef`) and returns, so
the button class never sees it and no 0x100 is posted. The slider (`0x66ba22`) has no 0x802 case and the base `0x65f6d1` forwards nothing.

**The keyboard.** A text field's key handler `0x667fee` sends its window 0x802 for Enter (unless the field has style 0x80), 0x803 for Tab,
0x804 for Escape and 0x806 for other keys; nothing else emits 0x802 except the slider thumb above. Park shortcuts call their commands directly
(no 0x100), and the list key helper `0x665b51` (style 0x200 only) moves the selection with Home, End and the arrows, never activating a row.
The eight self-senders, all on windows of style 1, so 0x1F:

| Window (procedure) | Enter | Escape | OpenTPW |
| --- | --- | --- | --- |
| Gate rename `0x48d640` (table `0x750090`) and park rename `0x4993e0` (`0x751928`) | first line: focus moves to the second, no click; second line: 0x100 -1, 0x1F, apply | closes, no click | `OpenParkRenameDialog`: the second line's Enter is the tick |
| Save `0x49ea30` | 0x100 -1, 0x1F | 0x100 -2, 0x1F | no save screen |
| New player name `0x4a6d00` (`0x753f50`) | 0x100 -1 at once (`0x4a6df5`), 0x1F | closes, no click | Game Mode: Enter is the tick, as before |
| Employee rename `0x4b6320` (`0x7572e8`) | 0x100 -1, 0x1F, apply | closes, no click | `OpenEmployeeRenameDialog`: Enter is the tick |
| Dial-up details `0x4b9d00` (`0x7583e8`) and online login `0x4baf10` (`0x7585a8`) | in field `0x49`: focus to `0x4b`; else 0x100 -1, 0x1F | closes, no click | no dial-up; the online login is OpenTPW's own (EXT) |
| Find Parks `0x4bf9f0` | 0x100 `0xe059` unless that button is held, 0x1F, search | not read | Find Parks: Enter is the search button |

The shop window's price field and the other OpenTPW fields with no listed window send no 0x100 in the original, and OpenTPW's Enter there plays
nothing. OpenTPW's host keyboard focus (Enter on a focused button, Enter on a focused list) has no original input (`[EXT:ui-key-navigation]`): a focused
button plays its own click, a list row stays silent.

**OpenTPW.** `UiScrollList` is silent by default (`ClicksOnActivate`); the Theme Park Inc text HUD (`[EXT:tpi-text-hud]`) and the Extensions and
mods screen (`[EXT:mods]`) opt in, having no original screen. The hire refusal keeps its message and loses the error sound the original never
plays there. `ListOwnerAudioTests` checks the default, the opt-ins, the Load, Find Parks and Other Parks rows, the Enter paths of Find Parks,
the employee rename and the park rename, Escape, the hire refusal and a still thumb click, with generated, distinguishable 0x1F/0xBD samples.

### Register round 2 (October 14)

The second static round over tp.exe (the PC executable counts as original evidence) retired, narrowed or
reclassified these items; the register above keeps only what is still open, and the code carries the
evidence as `[BIN:TPW-EXE:...]` notes.

* **Retired with a change**: 002 (the bank folder follows the record's flag 0x20, no fallback), 007 (the
  drawer plays 0x1C when a close starts as well as when it opens, then 0xC2), 019 (flat one-shots with
  flag 0x8 loop), 020 (a placed sentence plays as a positioned sentence), 021 (listener: the eye in the
  camera modes `0x790ab0 & 0x16`, otherwise the eye/target midpoint; scape coordinates and range truncated
  toward zero), 027 (the constructed quality is never applied and the first update rolls a forecast),
  032 (screams play at the ride's footprint middle), 035 (one shared advancing chooser; see
  "Sound catalogues"). The start-up sound, the second rain voice, the refused-click place, the repair
  sound's place and the EVENT anchor rules (908) landed in the same round.
* **Retired, already matching**: 006 (the crowd count is the Chebyshev square of radius 4, `0x4c8d30`;
  capped at 100, 0 in world state 4), 010 (the lobby sentences restart with each island step,
  `0x5e1640`/`0x5e1730`, and on the `0x4a6a33` toggle), 012 (the global UI hook `0x485780` clicks for every
  control; the front end's list lines and rows were then made to click like its buttons, which "List owners" corrects: the hook
  clicks only for message 0x100, and no list row sends it).
* **Narrowed**: 003, 004, 008, 009, 011, 015, 022, 025, 026, 034, 036, 900 and 908 (rows above); the
  corrected readings are in the sections they belong to. Unchanged: 018, 023, 033, 901-904.
* **Reclassified as documented OpenTPW decisions** (`[EXT:...]` markers in the code):
  005 `[EXT:sentence-prefetch]` (a sentence chooses and decodes its next segment ahead; the original's
  chooser is the shared clock-seeded one, so the draw's timing has no observable rule),
  013/014 `[EXT:mpeg-deemphasis]`/`[EXT:mpeg-crc]` (every one of the 411,333 shipped MPEG frames has emphasis 0 and
  no CRC, so the decoder's handling of such streams cannot be observed),
  024 `[EXT:staff-voice-fallback]` (the original always has staff state machines, so the no-staff-world mode is
  OpenTPW's own),
  030 `[EXT:weather-stream]` (the weather draws from its own stream; the rule of the PC's shared world LCG
  is known, [DETERMINISM.md](DETERMINISM.md)),
  031/905/906/907 `[EXT:forecast-presentation]`, `[EXT:forecast-banner]`, `[EXT:forecast-wording]`,
  `[EXT:forecast-cadence]` (the PC has no forecast message), and the drawer-versus-window presentation
  of 007 `[EXT:drawer-window]` (UI register).
* **Corrected**: the doc table used to say the screams play flat. STARTSCREAM is positioned on the PC
  (`0x551130` submits 0x47-0x4A at (x, y, z) with parameter 6 = clamp((a + b) / 2, 0, 100)), SINGLESCREAM
  too (`0x556af0` -> `0x551320`), both at the footprint middle (`0x466b70`); "Triggered sounds" now says so.
* **Chooser contradiction**: the Mac reading of the chooser (frozen seed, top 16 bits without the rotate,
  `SoundEvents.cs` line 151 before this round) disagrees with the PC instructions (advancing, stored,
  rotated by 13); the PC reading is used and the Mac reading is to be rechecked.
* **Retired October 15**: 036 (a parameter call re-evaluates the playing segment's volume and pitch when its code is
  in slot 1-3; the crowd's code 7 is the event's and sounds 1-6's slot-1 selector; the crowd update runs on every
  32nd world step).


The option volumes are percentages (0–100) like the original's `{on, percent}` pairs, with the
`DefaultVolume.*` defaults of `sound.sam` (effects 75, music 60, speech 75, movie 100; the globals holding them
are medium confidence). Options files from before the change (0–10 steps) are migrated ×10 on load. Only the
slider step granularity within 0–100 is 1 (tp.exe `0x66b47d`, `0x66ad77`).

## Selected SDT entry to WAV (desktop)

`--export-sdt-wav <local.sdt> <1-based entry number> <new.wav>` exports one explicit
entry through the existing CPU `SoundBank` and `Mp2Decoder`, before game assets,
window or profile setup. Entry numbering follows the original bank offset table;
damaged earlier entries do not shift the selection. The decoder's interleaved
signed 16-bit samples, sample rate and mono/stereo channel count are written with
the already-pinned NAudio WAV writer, without resampling, gain, mixing or float
conversion. Container sample-rate hints and the unqualified raw sample field
are not used as a duration or PCM-size oracle.

Supported payloads are MPEG-1/2 Layer I and MPEG-1/2 Layer II (MPEG-1 joint included) as implemented
by the existing decoder (MPEG1-LAYER2-VERIFICATION.md records the synthetic
MPEG-1 addition). Layer III, MPEG-2.5, free format and
MPEG-2 Layer II joint stereo remains unsupported; raw WAV/NONE payloads are not treated
as MPEG. There is no event/category lookup, weighted variant selection, sentence
or loop flattening, playback, or audio/LIP timing export. This exports current
decoded samples and does not qualify original audio or native playback semantics.

The export-only policy refuses missing/damaged entries, zero complete frames,
failed CRCs and unconsumed MPEG tails other than the known single zero SDT pad.
The existing 16 MiB decoder input bound is checked before allocating the selected
payload through the caller-limited `GetMpeg` overload. Runtime synchronous and
cooperative sample decodes now pass the same existing bound before payload reads;
oversized entries retain the decoder's null/trace/failed-cache behavior without
allocating the refused payload. Bank loading remains outside the content-error
catch; source-length I/O errors and scheduler cancellation still propagate.
The default unrestricted `GetMpeg` API and codec/playback/loop ownership remain
unchanged. `RuntimeSoundInputPreflightTests` exercises sparse synthetic oversized
records, the exact boundary, valid PCM/cache, changed-source and cancellation
controls; original assets and native/device timing were not exercised. No new codec or arbitrary valid-output memory
or storage-quota guarantee is claimed. Large valid decoded clips still require
the existing decoder's complete PCM buffer.

Decode and validation finish before output directories or files are created.
An existing output file is refused. WAV header finalization completes on an owned
`CreateNew` temporary file before `Flush(true)` and a non-overwriting move;
failure removes only that owned temporary. The operation does not overwrite the
input, an existing target or unrelated temporary files. Synthetic bank/decode/WAV
readback and actual CLI controls cover sample identity, rate/channels, stable
selection, damaged/truncated and unsupported input, early payload bounds and
output integrity. Original assets, devices, GPU/browser execution and native
audio parity are unqualified. This desktop-only helper is excluded from the
browser compile; the shared reader and decoder remain available there.


Runtime size preflight preserves the source-error boundary. The additive
`SoundBank.GetMpeg(number, maximumBytes, out rejectedPayloadBytes)` reports only a
validated oversized payload as null plus a positive size, before allocation.
Zero means no size refusal (including missing/damaged entries); source open/read
exceptions still propagate. Existing bounded/unbounded wrappers keep their old
return behavior and oversized `InvalidDataException` message. Metadata is read
once; the existing per-read captured-bank-length checks remain in force.

Both runtime decode paths perform the typed bank read outside the codec catch.
Only the explicit positive size refusal joins the existing logged-null/cache
path; source `InvalidDataException`, `NotSupportedException`, `IOException` and
cancellation remain retryable. Tests pin real source-factory propagation/retry,
worker/cooperative bounds, exact-limit reads, valid PCM/cache/Forget, cancellation
and public wrapper compatibility. This correction changes no decoder limit,
format, ownership, playback or original/native qualification.

Independent corrected-group controls also preserve the exact source exception
instance when its message mimics an oversized-entry refusal, at either metadata
or payload open, across synchronous, worker and cooperative decoding. A changed
source length refuses before reporting size; Forget clears cached size refusals
and retired categories do not retain them again. These are synthetic CPU tests.

Retired in the audio, advisor and effects sweep (2026-10-16): `APPROX:AUDIO-008` (the repair band is the script's variable slot 5), `APPROX:AUDIO-025` (December's average is the Seasons array's count slot) and `APPROX:AUDIO-903` (CRain's dt zeroing); AUDIO-037 is `[EXT:sound-chooser-seed]`.
