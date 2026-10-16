# Sound: catalogues, mixer and park music

October 14, 2026. Status: the original sound catalogues are read, a software mixer plays
through SDL, the park plays its original music, the front end plays its original lobby
music and ambience, control clicks, the map/research drawer, error feedback, ride-script
sounds (RSE EVENT and REPAIREFFECT), the crowd sentence and the advisor's speech play
through the mixer. The park's weather plays its rain and thunder, the level's placed
ambients (scape.omp) play with the mixer's distance law, ride screams play, the staff's
walking and idle voices play, the path tool's laying and refused clicks play, a queue joining a path
plays `cat_ui` 139, Backspace over a path cell (and, as an extension, a queue cell) plays `cat_ui` 95 as it removes the cell, and opening a
visitor's details plays the visitor click (`cat_kids` 129). Still silent: the second source of 139 (a
laid path cell linking to a queue) and the lobby's global effects
(see "Traced but not triggered"). No original data is in the repository.

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

Headless runtimes (no placement) and a nonfinite result keep the flat path. Types 0, 1, 2 and 10 never
sample an audio origin or produce sound through this route. The dispatcher truncates the three
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

`REPAIREFFECT 0` submits the traced global `cat_rides` events 111 and 211 using
one sample of the object's origin (the placement/render transform of model-space zero, the
original's object position `0x556a80`). The existing `PlayAt` distance/pan and flag-`0x8` loop policy
applies independently to each returned voice. Live hide loops join the existing exact-runtime ledger, so
Stop, object removal and root-fault containment stop only that owner's loops.
They trigger the same narrow live-audio capture refusal; one-shots, finished
loops, missing audio and flat/headless fallback do not add a save restriction.
No audio handle or save schema is introduced. If the second submission fails,
only a partially submitted first loop is stopped before the original exception
propagates; a first one-shot keeps its existing lifetime.

`REPAIREFFECT 1` (any nonzero value) plays a `cat_kids` repair sound **at the object** too, as the original does
(`0x55635f` -> `0x551320(band, 1, 1, x, y, z)` with the object's position): the band is `[[vm+0x1c]+0x14]`, i.e. the
script's own variable slot 5 (the array at `[vm+0x1c]` holds the script variables, `0x556408`; the standard ride scripts
keep `VAR_ONRIDE` there), and picks the family (band 1 -> 75, 2-3 and negative -> 79, 4-7 -> 83, >= 8 -> 87, 0 silent;
index `(a + b) / 50` capped at 3, so 0 here). `GameAudio.RepairStartEventForBand` does the mapping. The
public `GameAudio.PlayRepairEffect(int show)` API retains its flat behavior (band 1).

`RepairHideAudioTests` exercises actual placed root VMs, imported-terrain
centering, listener/mixer equivalence for both IDs, per-pair sampling, missing or
nonfinite origins, show controls, owner/fault teardown and narrow capture policy.
Only existing managed PCM and synthetic model/catalog data are used. Loop positions remain the sampled
instruction-time positions, rather than following later provider movement.

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
object-effects hook still records these requests as unimplemented for the audio
service; the particle side is described in [PARTICLES.md](PARTICLES.md): a
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
recomputes the volume (unless a fixed volume was set) and redraws the pitch. The crowd event's code is 7 and sounds
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

The advisor plays its clip on the speech channel; its lip-sync position is the mixer
output the listener has been given since the clip started.

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
| Control click | `cat_ui` 0x1F | every activated control, front end included (control message 0x100) | tp.exe `0x485780` (a global hook, `0x658d9f`/`0x658e42` call it for every control message); Mac `0x10137FD0` |
| Map/research drawer | `cat_ui` 0x1C open, 0x1C then 0xC2 close | the map and research windows | tp.exe `0x4a14f0` (open `0x4a1737`, close `0x4a186f`, slide end `0x4a1947`) |
| Error feedback | `cat_ui` 0x1D | a rejected purchase or placement | `0x10139A40` (buy window callers `0x10163AFC`/`0x10163BFC`) |
| Path tool | `cat_ui` 101 lay, 175 refused | a committed path line; a click on a refused path or queue ghost (or a refused track commit), at the clicked cell (x * 10, 0, z * 10) | `0x10070DE0` (commit), tp.exe `0x52496e` (refused `0xAF`, `0x524a80`, at the release that follows a park-view press (byte `0x8186d8`, set by the press handler `0x524790`) outside a layout replay (`0x785a2c`); AUDIO-022 is retired) |
| Ride-script sounds | any event of the routed category | an RSE `EVENT type key id` instruction: type 3 level `cat_rides` (no fallback to the global one: an id the level lacks is silent, `0x6c5870`; AUDIO-011 is retired), 4 level `cat_ambient` (likewise), 5 global `cat_rides`, 6 `cat_kids`, 7 `cat_staff`, 8 `cat_ambient`, 9 `cat_ui` | `0x100AE930` (dispatcher, guard `bgt` 10 at `0x100AE938`); tp.exe `0x5575c7` (anchored at the key's model node, the footprint middle for a negative key, nothing for an unknown key; see "Placed sound EVENT origins") |
| Ride screams | `cat_kids` 71–74 held per ride (STARTSCREAM), STOPSCREAM stops it; `cat_kids` 75–109 one-shot (SINGLESCREAM) | a ride script's scream instructions; band 1 → 71, 2–3 → 72, 4–7 → 73, 8+ → 74; parameter (level + 50) / 2 (parameter 6 on the PC); **positioned at the ride's footprint middle** | `0x100B19DC` (STARTSCREAM), `0x100B1A74` (STOPSCREAM), `0x100B1A90` (SINGLESCREAM), `0x100ACA38`, `0x100ACB7C`, `0x100ACC24`, `0x100ACE68`; tp.exe `0x551130` (STARTSCREAM submit: positioned, 0x47-0x4A at (x, y, z), parameter 6 = clamp((a + b) / 2, 0, 100)), `0x555edb`, `0x555f1b` -> `0x556af0` -> `0x551320` |
| Repair sounds | `cat_kids` 75 (families 75–90) show; `cat_rides` 111 and 211 hide | an RSE `REPAIREFFECT 1/0` instruction, both at the object | `0x100B1F78` / `0x100B1EC0`, `0x100B1F00`; tp.exe `0x55635f` / `0x551320` |
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
A chain outside the pool or out of range starts no new child, its first included (AUDIO-038); every other event voice out of range without the any-distance flag (event flag `0x200`: the crowd sentence, music, speech, most UI sounds) has its sample stopped by mixer message 3 (`0x6bca70`), a sentence standing still until the listener returns. The visitors'
chatter and laughter is the crowd sentence (`cat_kids` 0x5B), a continuous bed at 14–22 % volume while guests are within four cells
of the camera: its segments have no delay in the data.

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

Traced in the binary but not triggered yet:

* The custom-bank type 10 is not routed. Type 0 is the dispatcher's default/unsupported path.
  `EVENT` types 1 and 2 create **particles**, not positioned sound loops; see the particle section above.
* Staff chase (`cat_staff` 136, `0x100D49F8`), catch (137, `0x100D516C`), the researching turn (138,
  `0x100F05EC`) and a performance's end (135, `0x100D2D98`): the events are traced, but OpenTPW has no
  chase, catch, performance or per-turn research action to hang them on.
* `cat_ui` 139 is the sound of a queue joining a path, not a path-tool link-end. Its first source plays now: the queue tool's
  commit when the line ends on a path cell, money on (`0x100711CC`, tp.exe `0x527587`; the validator `0x84154` returns code
  8 for that line, which is also no longer treated as a refusal). Its second source plays too: the link writer (tp.exe
  `0x5348D0`, `PathLinkWriter`) reports a laid path cell beside a queue cell whose link byte points back at it (a path laid
  over the queue's join cell, or over the queue's end), and the sound plays at (10x, 0, 10x): tp.exe takes both coordinates
  from the cell's x (`0x5348E0`). The count-classed removal sounds `cat_ui` 141–144 and 150–153
  (`0x10075618`, `0x100748A0`) are demolish sounds, not path-tool sounds.
* `cat_ui` 95 is the "backtrack" key command (Backspace, `0x10112430`, tp.exe `0x40BDA0`), and it plays now: with no tool on and
  a path cell under the cursor it plays at that cell, then that cell loses one placement (ClearCell's counted path case,
  tp.exe `0x40BE8B`) and goes only when it was laid once; the sound plays even when the cell stays. Over a queue cell OpenTPW plays it too and takes the queue back from that cell
  (`[EXT:queue-backtrack]`; the original's key does nothing there). Inside the path tool the original's Backspace is Back (tp.exe `0x52FE50`): it takes the last segment back without
  a refund and plays no sound, as OpenTPW's segment undo does.
* The weather's own visuals are drawn now, as procedural approximations (AUDIO-900..902, 904,
  "Weather visuals" below); the forecast's warning is shown as an OpenTPW extension (banner, wording
  and cadence are `[EXT:forecast-banner]`, `[EXT:forecast-wording]`, `[EXT:forecast-cadence]`): the PC has
  no forecast message (event type 2 has a single subscriber, the weather thing itself, `0x511fa9`/`0x511fb8`).
* The placed records of type 3 (a sound that follows the listener in a rectangle: the original submits
  `0x51bfc0` and binds the rectangle with `0x51c5d0`) are counted, not played.
* The visitor click (`cat_kids` 129, flat) plays whenever a visitor's details open: the status panel's open routine
  (`0x1010B808`, tp.exe `0x4B79B0`) plays it unconditionally, for a click on a visitor in the park, a roster row's
  activation, the map's visitor markers and the selection strip. OpenTPW plays it for the park click and the roster
  activation; it has no separate status panel and no map markers or strip yet.
* Entertainers and guards in a level play their work sounds from their state machines: `cat_staff` 135 at the end of a
  performance (tp.exe `0x4D4810`), 136 when a guard sets off after a prankster (`0x4D6260`) and 137 when he catches it
  (`0x4D6790`), and `cat_kids` 128 when he sets the guest down (`0x4FEB50`). A researcher at work plays `cat_staff` 138 on one draw in sixteen
  (`0x5029F0`, state 15).
* A prankster's stinkbomb plays `cat_kids` 207 (0xCF) at the prankster (tp.exe `0x4FF289`, through the thing sound
  `0x4FAA00`). A guest's litter going into a bin plays no sound of its own: it sets the bin script's variable 0
  (`VAR_TRIGGER`) to 1 (`0x4FFF20`, `0x55A0B0`), and the bin's script does the rest.
* Global lobby effects now play (see "Lobby effects" below): the thunder (`cat_globallobbysfx` 1) with the lobby's
  lightning, the globe's music and ambience (2, 3) and the enter-park sound (4).
* The advisor controller's own effects, `cat_ui` 197/0xC5 (`0x100069DC`) and
  198/0xC6 (`0x100075DC`): which advisor state plays them is not traced.
* The slap reactions 639–641 of `cat_speech` (`0x10007068`): OpenTPW has no slap
  interaction.
* `cat_ui` 0xBD: a control whose construction style (`+0x48`, the base window constructor `0x65bcad`'s second
  argument) has bit `0x10` plays 0xBD instead of 0x1F (`0x4857cb`; the Mac's "bits outside 24-26" is a
  big-endian rendering of the same test); which original controls are created with it is not traced, so
  no OpenTPW control plays it (AUDIO-004).

## Lobby effects

The front end's island effects follow the original's islands update (`CLBScreenComponentIslands::vf2`, `0x10094474`),
stepped by `LobbyEffects` at the 60 Hz reference rate (`[EXT:frame-driven-at-60hz]`, `APPROX:UI-940` is retired; the original runs once per rendered frame, `0x5d5c50` → `0x5e0470`):

* **The nearest island** to the camera drives everything, not the selected one.
* **The lobby's own generator** (state × 0x19660D + 0x3C6EF35F, `0x100904A8`; tp.exe rotates the step right by 13 bits
  and stores and returns the rotated value, `0x5E0F4B`), seeded once from the clock (`0x1008CDBC`). The flying meshes draw from it too (`LobbyFlyingMeshes`); the camera
  wander that also does is not built, so the rhythm of strikes and one-shots differs (UI-941; the flyers and the islands step in the original's order and at the same frame rate).
* **Lightning** (only hallow: `LIGHTNING 63`): a draw with `(draw & LIGHTNING) == 1` strikes; four draws place the top
  within ±50 of the island at height 500 and the bottom within ±10 of it at height 0; the bolt lives 500 ms, the screen
  flashes white for one frame and `cat_globallobbysfx` 1 plays flat at once (`0x10095124`, `0x10095310`). The bolt is
  drawn by the park's weather renderer (CRain, "Weather visuals"), at the lobby's own scale; the flash is an opaque white
  frame under the menus (its blend is not decoded).
* **Rain**: 10^17 / (mean squared distance from the camera to the rainy islands)^4 drops, none below 20, at most 400
  (`0x10094F8C`): about 173 at the orbit radius around hallow, drawn by the same renderer.
* **Island one-shots**: a draw with `(draw & 15) == 1` plays event 3 + (fresh draw mod 3) of the island's
  `cat_locallobbysfx` (`0x10095340`), replacing OpenTPW's earlier 5–15 s timer (AUDIO-017 is resolved).
* **Sky**: the original clears the screen to the lobby's fog colour 0x44DDFF (tp.exe `0x5D8B50`, `0x5737AA`). The colour of the
  sky's cloud layer starts black and eases toward the island's `SKYCOLOUR` each tick by (target − current) >> 4 in 16.16
  fixed point, the red channel toward the *blue* target (`0x1008FAA0`, tp.exe `0x5D96C0`); while it equals its target the
  layer takes the fog colour. The capture's cloud layer is tinted that way (grey over Lost Kingdom, pink over Wonderland,
  dark over Halloween World and Space Zone). The cloud layer itself is not drawn yet, so OpenTPW paints the whole backdrop
  with that colour as a stand-in; two settings gate the eased colour whose runtime values are not read (UI-018).
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
  1/16384 grid, with f = 0.3 in its free-walk and ride cameras (the listener's mapping of the camera-state bits `0x16`) and 0.1 otherwise;
  which original views set those bits is not read (AUDIO-900). At the original scale
  (0.2 engine units a unit) the field is a small near-field around the eye: streaks are 0.1 units
  long and 0.2 x that wide. The camera's 0.1 near plane clips the nearest 0.5 units of it, where the
  original's near plane is 0.1 units (`0x578be0`, AUDIO-904). The original's camera has a field of view of 90 degrees (`0x42a990` passes pi/2 to
  `0x578be0`), OpenTPW's park camera 55 (UI-101).
* Rain: grey `3 x max(0, int(12 x (6 - depth)))` (216 at the eye, 0 at depth 6), alpha 0x80 at the
  streak's trailing end and 0xFF at the leading end, only depth > 0 drawn. The render states come from tp.exe's
  decoder `0x567620`: rain and bolts `0x200D52` (source alpha over one, no depth writes), snow `0x556` (alpha blend,
  depth writes), each with the alpha test at 0x10 of 255; the rain's `0x200D22` (one plus one) is only a fallback
  for the chips flagged at device init (`0x566840`).
* Snow: one triangle a flake, half-size 0.1, its apex at the top middle of atlas quadrant `index & 3` and its base
  at the quadrant's lower corners, full white, x and y spread by `1 + depth/8`, jittering by
  `((rand & 31) - 16) / 2048` on every axis each drawn frame (`0x57f490`; `[EXT:frame-driven-at-60hz]`: OpenTPW redraws the jitter, and the bolts' flicker,
  once per 60 Hz reference frame). The original's other mode swirls flakes along
  a closed 128-point spline under engine flag `0x8bcbc8` bit `0x100`, which nothing in tp.exe sets, so the PC
  draws only the jitter version. The flake's vertices are homogeneous clip coordinates (x, y, z + 0.1, w = z + 0.1501), so OpenTPW
  moves the flake's depth by 0.1501 for its screen position; the depth-buffer value is not reproduced (`[EXT:snow-depth-value]`, AUDIO-901).
* Bolts: from the ground point (x, 0, z) the strike drew (the original's bolt does not follow the terrain) to the sky point 300 up and offset by up to
  50 in x and z (`0x512C50`), a recursive midpoint walk (`0x57E810`) on the CRain stream (`0x57E4B0`: the midpoint moves along the
  vector P, the segment's middle with one axis replaced by `M - |M|^2 / d`, by `|d| x 0.6 x r2 x r1^2`, r the signed remainder of a draw over 0x1FFF
  divided by 8192) that splits a half again while its squared length is at least `6 + (draw & 0xFFF - 0x800) x 2 / 2048`
  (tested on each half, so a final segment is under 2.83 units), a 9/512 branch chance per split up to depth 4 (the heading
  `along-bolt x the bolt direction + along-segment x the segment direction`, lengths `(0x200 + draw & 0x3FF) / (96 x depth)`, along-segment x 2.5, the
  along-bolt term halved and the sum halved when it points against the bolt), the shape fixed per bolt from its seed word, a ribbon of
  half-width `1.3 / (depth + 1)`, grey pulsing with the remaining life (`0x57EE30`: x = (remaining ms mod 500) / 500; 255 - trunc((0.9 - x) x 71.04) below 0.9, else
  192 + trunc((1 - x) x 640); plus rand & 63; the three channels take the sum's low byte, so a sum of 256 or more wraps dark), alive
  `WeatherEffects.LightningTime` (2000 ms) with up to 16 at once. There is no screen flash and no sky darkening in the park. The ribbon of OpenTPW is
  a camera-facing world quad per segment; the original's emitter `0x57DD10` builds it in clip xy with mitred joins (AUDIO-902). OpenTPW bounds a bolt
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

### Forecast warnings

`ParkWeather`'s forecast (a quality rolled `Weather.DaysOfWarning` days ahead and applied when its
timestamp passes; see "Weather schedule" below) is presented by `WeatherForecastState` (pure, unit-tested) and `WeatherForecastPresenter`
(the entity that draws it). The presenter is created with the weather like the renderer, reads the
simulation each frame, and draws one banner line at the top centre of the screen in the overlay pass
(the chat overlay's channel: the original GAME8AA BF4 font, above every screen including pause
menus; `[EXT:forecast-banner]`). One line, one background rectangle, laid out again only when the text changes;
no per-frame GPU growth.

What it shows (all of it an OpenTPW extension, `[EXT:forecast-wording]` and `[EXT:forecast-cadence]`:
the PC has no forecast message, and no weather text exists in the shipped `Language` string tables,
which were searched):

* A park entered or loaded with weather on screen says so once: "The weather: rain." for six
  seconds of simulation time (a dry, quiet park stays silent). A paused park freezes the line
  mid-display; leaving the park or loading another ends it with the entity.
* A pending forecast shows the countdown "Forecast: thunderstorms in 4 days." (tomorrow/today on the
  last days) from the apply day and the current simulation day, and posts the same line as a message
  to the log. One message when the forecast appears, one when it applies — never per step, so no
  spam; a park resumed mid-warning announces the pending forecast once.
* The forecast's kind classifies the pending quality by the converter's bands: the snow/rain tie is
  still undecided ("snow or rain") because the apply-time draw has not happened, and the lightning
  band composes "thunderstorms". The apply message names the resolved weather ("The weather has
  changed: snow.").
* The lines are worded in the selected game language, like OpenTPW's other own labels
  (`SupplementaryStrings`, the six shipped languages; English for any other): in Dutch the countdown
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

| ID | Approximation | Evidence needed |
| --- | --- | --- |
| AUDIO-003 | The pitch is applied: every sample playback draws a step `min + r % (max - min)` from the signed range of its sound (after the volume draw, from the shared chooser) and plays at ratio 1 for step 0, otherwise 2^(sign x (\|step\| + 1) / 96) (tp.exe `0x6bc170`, mixer wrapper `0x6d2820`, float tables `0x782f40`/`0x783540`); 141 playbacks in the original's video sit on exactly the six ratios of the range 0..6. A parameter-bound pitch slot is applied to every playback (sentences, one-shots and chains) through its placeholder table (`0x6bbf60`, `0x6bc040`, `0x6bc170`). The fixed-pitch flag (`0x10000`, `0x6bc484`) is unreachable: nothing in the library sets it and the events' flags are 16-bit. Not applied: the delay of non-chain sounds (`min + r % (max - min)`, `0x6bc2d0`, stored at `+0x3c` by `0x6bc4fa`) and reverb (`SndReverb.map`, loaded at `0x6c5d20`; `tp.exe` imports none of QMixer's reverb calls); chain delays are milliseconds (video, 2026-10-15); position is applied only by the positioned playback path | What consumes the placeholder's `+0x3c` delay for one-shots and sentences (`0x6bcb60`); how `SndReverb.map` is applied |
| AUDIO-004 | `0xBD` plays for a control whose construction style (`+0x48`, `0x65bcad` arg 2, the factory `0x65dd7b` arg 2) has bit `0x10` (`0x4857cb`). The creation sites with that bit are read: type 1 and type 9 children of the row templates `0x4938c0` (visitors list), `0x4944d0` (attractions lists), `0x496cea`/`0x496cff` and `0x4b4c70`, and the table builder's type 3 controls (styles `0x10`/`0x11`, `0x6604a1`, `0x66051a`, `0x6610b6`, `0x661144`); the traced button-like sites (types 1 and 2 with styles `0`, `1`, `0x21`) play `0x1F`. No OpenTPW control plays `0xBD` | Which of those controls receive the activate message `0x100` (a click on a list row or its scroll arrows?) and which OpenTPW controls they correspond to |
| AUDIO-009 | Within the build tools' code (`0x524960`-`0x535100`, which plays only `0xAF`, `0x65`, `0xCE`, `0x8B`, `0x97`-`0x99`, `0x90`) no refusal sound exists, and `tp.exe` plays `0x1D` at exactly two sites (`0x487b51` in the park click handler `0x4879d0`, a cost above the money, and the helper `0x486ae0` of the hire window `0x4af480`); OpenTPW's own "Cannot build" sound is the extension `[EXT:cannot-build-sound]` | Whether a placement refusal plays a sound from code outside that range (not searched) |
| AUDIO-018 | The positioned voice's pan is constant-power by bearing; QMixer.dll derives distance, azimuth and elevation from the source position in `0x18009dc0` (`0x18009f32`, `0x18009f49`) but the azimuth-to-level stage was not found | The channel's processing objects after `0x18009dc0` (made by `0x180070b0` through `0x18023460`), or a capture |
| AUDIO-900 | The rain is one camera-following wrapping drop field of straight falling streaks, its count the simulated current drops, its parallax the original's absolute `-factor x camera position` on a 1/16384 grid (`0x57efd0`, `0x580013`-`0x5800b7`), its states decoded (`0x567620`: rain and bolts `0x200D52`, snow `0x556`, alpha test at 0x10/255); the factor 0.3 (`0x58000b`) applies in the free-walk and ride cameras (the listener's mapping of camera-state bits `0x16`) | What bits `0x2`, `0x4` and `0x10` of `0x790ab0` select in the original (which views): `0x42AB20` toggles `0x2` and `0x4` from input events 2 and 4, `0x576a00` turns any of them into render flag `0x100` of `0x8bd508`, nothing sets `0x10` alone |
| AUDIO-901 | Snow is the jitter version (((rand & 31) - 16) / 2048 a frame on every axis), the only one the PC draws (the swirl flag `0x8bcbc8` bit `0x100` is never set), each flake one triangle over its atlas quadrant (`0x57f490`), its depth moved by the original's w term 0.1501; the depth-buffer value (z + 0.1) / (z + 0.1501) is not written (`[EXT:snow-depth-value]`: the precompiled browser shader has no clip-space vertex path) | The space of the flake's z + 0.1 and fourth word z + 0.1501 (camera or clip); whether the base half-width h x `0x8bcbcc` (height / width) equals OpenTPW's h under its aspect-correct projection |
| AUDIO-902 | The bolt follows tp.exe: ground point and offset sky point (`0x512C50`), the midpoint displacement and branch heading on the CRain stream (`0x57E4B0`, `0x57E810`: stop threshold tested on each half, branch 9/512, depth limit 4), the pulse colour (`0x57EE30`: 255 - trunc((0.9 - x) x 71.04) below x = 0.9 else 192 + trunc((1 - x) x 640), plus rand & 63, wrapping at 256) and the 2000 ms life; the ribbon is a camera-facing world quad per segment | The ribbon emitter `0x57DD10`: clip-xy perpendicular, 1.3 / (depth + 1) clip units wide, mitred joins between the segments of one depth |
| AUDIO-904 | The scene camera's 0.1 near plane (0.5 original units) clips the nearest drops; the original's near plane is 0.1 original units (`0x54e86f`, `0x42a990` -> `0x578be0`) and its clipper cuts the streaks there; the weather keeps the scene's near plane and a vertex buffer sized once (`[EXT:weather-near-plane]`) | A near clip of 0.1 original units for the weather without changing the scene's depth mapping |
| AUDIO-033 | The distance law is read: gain 1 up to the minimum distance 2, `((max - d) / (max - min))^rolloff` up to `max` (`max(range, 2.1)`, `tp.exe` `0x6c3b8a`), 0 beyond (QMixer.dll `0x1800a0d0`, enable-word flag `0x1000`, always set by `tp.exe` `0x6d230e`); the rolloff value (a float returned by the sound system object's vtable `+0x2c` at `0x6c3bb2`) is not read, so the fork's exponent 0.8 stands | The class of the sound system object at `0xfb1f24` and its `+0x2c` method |
| AUDIO-034 | Held-chain child timing (the newest child's end is its start time plus the sound's delay, `0x6bdb50`, `0x6bc2d0`), first-sound selection (the event's first sound, then weighted links covering the root's parameter byte, `0x6c3e00`) and child stop (`0x6bd9b0`, `0x6bcab0` command 1) are read and match; when no link covers the parameter the original writes 0x7f into the parameter byte and sets the root flag 0x40 (the root's tick, `0x6bdae0`, then does nothing), where OpenTPW only waits for the next parameter call | What clears the root flag 0x40 and whether the walk restarts at once with the forced 0x7f |
| AUDIO-038 | The voice pool of 12 (tp.exe `0x6b62b0`) ranks the held chains (screams) with the running sentences, placed sounds and one-shots of the event system by priority and ground distance (`0x6bb9f0`); a voice enters only while its rank exceeds the pool's lowest (`0x6b6bc0` compares strictly), so equal ranks keep the order of the manager's voice list; out-of-range voices without the any-distance flag have their sample stopped (`0x6bca70` → `0x6b8d60` → `0x6bb7c0`); OpenTPW stops one-shots and sentence segments and a chain starts no new child, but looping voices and a chain's playing children are not stopped | The voice list's insertion order; what restarts a stopped loop (the waterfall resumes) and how a stopped sentence resumes |

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
  control; the front end's list lines and rows now click like its buttons).
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
