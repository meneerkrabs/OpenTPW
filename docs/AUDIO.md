# Sound: catalogues, mixer and park music

October 13, 2026. Status: the original sound catalogues are read, a software mixer plays
through SDL, the park plays its original music, the front end plays its original lobby
music and ambience, control clicks, the map/research drawer, error feedback, ride-script
sounds (RSE EVENT and REPAIREFFECT), the crowd sentence and the advisor's speech play
through the mixer. The park's weather plays its rain and thunder, the level's placed
ambients (scape.omp) play with the mixer's distance law, ride screams play, the staff's
walking and idle voices play, the path tool's laying and refused clicks play, a queue joining a path
plays `cat_ui` 139, Backspace over a path cell plays `cat_ui` 95 as it removes the cell, and opening a
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
weather cadence, staff presentation stream, listener mapping and held-chain timing
retain the qualifications in the approximation register.

## Placed sound EVENT origins

Placed objects now route sound EVENT types 3–9 through the existing positional audio
API. The origin is the same placement/render transform applied to model-space zero,
sampled when the instruction executes, undoing imported heightfield centering and
converting engine axes/scale through `GameAudio.SoundPosition` to the same original
sound space used by `UpdateListener`. It does not follow an animated dummy key.
Headless runtimes, an absent origin or a nonfinite result keep the existing flat
fallback. Types 0, 1, 2 and 10 never sample an audio origin or produce sound through
this route. AUDIO-908 qualifies the unverified relation to the native emitter anchor
and its update timing; no native box midpoint for screams is inferred.

The existing positioned API's flag `0x8` loop policy (AUDIO-019) remains intact;
formerly flat placed calls now reach that policy. Returned loop voices belong to
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
They check every routed type, coordinates/range, flat refusal controls, per-call
sampling and loop ownership. Original assets, GPU behavior and native emitter
geometry/timing remain unqualified.

## Placed repair-hide pair

`REPAIREFFECT 0` submits the traced global `cat_rides` events 111 and 211 using
one sample of the same trusted sound-space origin as placed EVENT sounds. The
existing `PlayAt` distance/pan and flag-`0x8` loop policy applies independently to
each returned voice. Live hide loops join the existing exact-runtime ledger, so
Stop, object removal and root-fault containment stop only that owner's loops.
They trigger the same narrow live-audio capture refusal; one-shots, finished
loops, missing audio and flat/headless fallback do not add a save restriction.
No audio handle or save schema is introduced. If the second submission fails,
only a partially submitted first loop is stopped before the original exception
propagates; a first one-shot keeps its existing lifetime.

All nonzero show values keep the old flat `cat_kids` family-75 path without
sampling an origin or adding ownership. AUDIO-008's unknown repair level, two
float operands and show-family selection remain untouched, as do the visual
repair effect and unsupported VM result/count. The public
`GameAudio.PlayRepairEffect(int show)` API retains its flat behavior.

`RepairHideAudioTests` exercises actual placed root VMs, imported-terrain
centering, listener/mixer equivalence for both IDs, per-pair sampling, missing or
nonfinite origins, show controls, owner/fault teardown and narrow capture policy.
Only existing managed PCM and synthetic model/catalog data are used. AUDIO-908
still qualifies the emitter anchor and update timing; no native anchor, pan-law,
visual, original-asset or GPU parity is claimed. Loop positions remain the sampled
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

The event flags select the player (`CAudioPlaceHolderList::CreatePlaceHolder` `0x10011F20`):
without bit 0x4 a one-shot; with 0x10 a linear sentence; otherwise without 0x2 a one-shot
(branching) sentence, and with 0x2 a branching sentence (0x400), a shuffled sentence (0x100)
or a plain sentence. The park music event (flags 0x606) is a branching sentence; the lobby
music event (0x206) a plain sentence; UI events are one-shots.

Choices use the library's linear congruential generator (`seed × 0x19660D + 0x3C6EF35F`,
top 16 bits of the 32-bit result): a sound by the running sum of its weights
(`ChooseRandomSound` `0x1000FF40`), a sample by its cumulative weight, where a sentence
does not repeat the sample it just played when there are more than two
(`ChooseRandomSample` `0x1000FCB4`), and a volume in the sound's range
(`GetRandomVolume` `0x1000F32C`).

The identified Mac library leaves its chooser seed unchanged after a draw. Applying
that fixed draw to ongoing crowd ambience made OpenTPW alternate between just two
clips even though the shipped crowd sections contain 7–52 samples. Crowd sample
selection now uses its own advancing, seeded presentation stream (AUDIO-035). The
crowd event, branching rules, sample weights and timing stay the same; music and
other sounds keep their existing chooser. The effects option still controls the mix. This presentation
stream does not consume gameplay random draws, and starts again on park entry rather
than persisting across saves. Its variation policy is not claimed as original-game
behaviour.

The crowd also sends its nearby-visitor count as parameter 7 (Mac game
`0x100BC298–0x100BC2A0`). For crowd sections whose volume mask selects that parameter,
the sentence now applies the original `low + floor((high - low) × parameter / 100)`
interpolation (`sound_shared` `0x1000F32C`, `0x1000F3C8–0x1000F3F4`), using the first
volume-mask slot (`0x1000F2E0`). The shipped crowd bounds are 14–22 percent. Sections
without that selector retain their range-based volume, and the other parameter
producers remain outside this crowd fix. Gain is evaluated when a segment starts;
propagation into an already-playing child remains unverified (AUDIO-036).

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
| Control click | `cat_ui` 0x1F | every activated button (control message 0x100) | `0x10137FD0` |
| Map/research drawer | `cat_ui` 0x1C open, 0xC2 close | the map and research windows | `0x1015644C` messages 0x0b/0x10 |
| Error feedback | `cat_ui` 0x1D | a rejected purchase or placement | `0x10139A40` (buy window callers `0x10163AFC`/`0x10163BFC`) |
| Path tool | `cat_ui` 101 lay, 175 refused | a committed path line; a refused commit | `0x10070DE0` (commit), `0x10073D1C` (refused, AUDIO-022) |
| Ride-script sounds | any event of the routed category | an RSE `EVENT type id` instruction: type 3 level `cat_rides` (falling back to the global one, AUDIO-011), 4 level `cat_ambient` (same fallback), 5 global `cat_rides`, 6 `cat_kids`, 7 `cat_staff`, 8 `cat_ambient`, 9 `cat_ui` | `0x100AE930` (dispatcher, guard `bgt` 10 at `0x100AE938`), submitted at the object's position where the object has one |
| Ride screams | `cat_kids` 71–74 held per ride (STARTSCREAM), STOPSCREAM stops it; `cat_kids` 75–109 one-shot (SINGLESCREAM) | a ride script's scream instructions; band 1 → 71, 2–3 → 72, 4–7 → 73, 8+ → 74; parameter (level + 50) / 2 | `0x100B19DC` (STARTSCREAM), `0x100B1A74` (STOPSCREAM), `0x100B1A90` (SINGLESCREAM), `0x100ACA38`, `0x100ACB7C`, `0x100ACC24`, `0x100ACE68` (flat, AUDIO-032) |
| Repair sounds | `cat_kids` 75 (families 75–90) show; `cat_rides` 111 and 211 hide | an RSE `REPAIREFFECT 1/0` instruction | `0x100B1F78` / `0x100B1EC0`, `0x100B1F00` |
| Rain | `cat_ambient` 0x21 (branching sentence, parameter 8 = the drops' level 0–100) | the weather's current drops: started above 0, stopped at 0 | `0x10103394` (level), `0x100BC3D4`–`0x100BC41C` (global voice) |
| Thunder | `cat_ambient` 0x20 at the last bolt's top (x, 300, z) | a lightning strike's thunder countdown ends | `0x101034C8`–`0x10103518` |
| Placed ambients | `cat_ambient` (global) or the level's `cat_ambient`, the effect the scape record names | the level's `scape.omp` type 1 records, started with the park; range from the record | `scape.omp` records (`PlacedObjectsFile`), distance law of the mixer (AUDIO-018) |
| Staff voices | `cat_staff` 160–169: walking/idle per role (handyman 160/161, mechanic 162/163, entertainer 164/165, guard 166/167, researcher 168/169) | a staff member each park turn, 1 in 16 draws (AUDIO-024) | `0x100D5EAC`, `0x100D6434`, `0x100D9BAC`, `0x100D9F10`, `0x100D2DDC`, `0x100D2E44`, `0x100D4EF0`, `0x100D4E18`, `0x100F0460`, `0x100F0630` |
| Crowd | `cat_kids` 0x5B (branching sentence, parameter 0–100) | guests within four cells of the camera; louder/bigger sections as more gather, stopped when nobody is near | count `0x101C2490` (`0x100C313C`, radius 4, cap 100), sentence `0x100BC210`/`0x100BC2E0` |
| Advisor speech | response table: sample and LIP from the global or level speech bank | traced advisor events | `0x10006B7C` |

Traced in the binary but not triggered yet:

* The custom-bank type 10 is not routed. Type 0 is the dispatcher's default/unsupported path.
  `EVENT` types 1 and 2 create **particles**, not positioned sound loops; see the particle section above.
* Staff chase (`cat_staff` 136, `0x100D49F8`), catch (137, `0x100D516C`), the researching turn (138,
  `0x100F05EC`) and a performance's end (135, `0x100D2D98`): the events are traced, but OpenTPW has no
  chase, catch, performance or per-turn research action to hang them on.
* `cat_ui` 139 is the sound of a queue joining a path, not a path-tool link-end. Its first source plays now: the queue tool's
  commit when the line ends on a path cell, money on (`0x100711CC`, tp.exe `0x527587`; the validator `0x84154` returns code
  8 for that line, which is also no longer treated as a refusal). Its second source is not played yet: a laid path cell
  linking to a neighbouring queue cell whose link points back (`0x10082D6C`, tp.exe `0x5348D0`), because OpenTPW's path
  linking has no queue-link check (PATH-004, AUDIO-023). The count-classed removal sounds `cat_ui` 141–144 and 150–153
  (`0x10075618`, `0x100748A0`) are demolish sounds, not path-tool sounds.
* `cat_ui` 95 is the "backtrack" key command (Backspace, `0x10112430`, tp.exe `0x40BDA0`), and it plays now: with no tool on and
  a path cell under the cursor it plays at that cell, then that one cell is removed (ClearCell's path case), even when a fixed
  cell stays. Inside the path tool the original's Backspace does nothing; OpenTPW keeps its segment undo there (PATH-002).
* The weather's own visuals are drawn now, as unverified procedural approximations (AUDIO-900..904,
  "Weather visuals" below); the forecast's warning is shown as an OpenTPW presentation whose wording
  and cadence are approximations (AUDIO-905..907), because the original's forecast message handler is
  not traced (AUDIO-031).
* The placed records of type 3 (a sound that follows the listener in a rectangle) are counted, not
  played; a sentence-type placed effect plays its first sound once (AUDIO-020).
* The visitor click (`cat_kids` 129, flat) plays whenever a visitor's details open: the status panel's open routine
  (`0x1010B808`, tp.exe `0x4B79B0`) plays it unconditionally, for a click on a visitor in the park, a roster row's
  activation, the map's visitor markers and the selection strip. OpenTPW plays it for the park click and the roster
  activation; it has no separate status panel (UI-910) and no map markers or strip yet.
* Global lobby effects now play (see "Lobby effects" below): the thunder (`cat_globallobbysfx` 1) with the lobby's
  lightning, the globe's music and ambience (2, 3) and the enter-park sound (4).
* The advisor controller's own effects, `cat_ui` 197/0xC5 (`0x100069DC`) and
  198/0xC6 (`0x100075DC`): which advisor state plays them is not traced.
* The slap reactions 639–641 of `cat_speech` (`0x10007068`): OpenTPW has no slap
  interaction.
* `cat_ui` 0xBD: a control whose flag word (read at `0x1017F618`) has a bit set outside
  bits 24–26 plays 0xBD instead of 0x1F (`0x10138030`); no OpenTPW control maps those bits
  (AUDIO-004).

## Lobby effects

The front end's island effects follow the original's islands update (`CLBScreenComponentIslands::vf2`, `0x10094474`),
stepped by `LobbyEffects` 30 times a second (UI-940; the original runs once per rendered frame):

* **The nearest island** to the camera drives everything, not the selected one.
* **The lobby's own generator** (state × 0x19660D + 0x3C6EF35F, the full state returned, `0x100904A8`), seeded once from
  the clock (`0x1008CDBC`). The original's flying meshes and camera wander draw from it too; OpenTPW has neither yet, so
  the rhythm of strikes and one-shots differs (UI-941).
* **Lightning** (only hallow: `LIGHTNING 63`): a draw with `(state & LIGHTNING) == 1` strikes; four draws place the top
  within ±50 of the island at height 500 and the bottom within ±10 of it at height 0; the bolt lives 500 ms, the screen
  flashes white for one frame and `cat_globallobbysfx` 1 plays flat at once (`0x10095124`, `0x10095310`). The bolt is
  drawn by the park's weather renderer (CRain, "Weather visuals"), at the lobby's own scale; the flash is an opaque white
  frame under the menus (its blend is not decoded).
* **Rain**: 10^17 / (mean squared distance from the camera to the rainy islands)^4 drops, none below 20, at most 400
  (`0x10094F8C`): about 173 at the orbit radius around hallow, drawn by the same renderer.
* **Island one-shots**: a draw with `(state & 15) == 1` plays event 3 + (fresh draw mod 3) of the island's
  `cat_locallobbysfx` (`0x10095340`), replacing OpenTPW's earlier 5–15 s timer (AUDIO-017 is resolved).
* **Sky**: one flat colour eased 1/16 of the way toward the island's `SKYCOLOUR` each tick (`0x1008FAA0`, tp.exe
  `0x5D96C0`). Both builds ease the red channel toward the blue target, and two settings flags gate the eased sky; neither
  is reproduced before a capture confirms it (UI-018).
* **The globe** (`0x100923A8`) sets the rain to 0 and the sky target to (243, 203, 191) and starts its music
  (`cat_globallobbysfx` 2) and ambience (3), stopped when it closes; OpenTPW stops the lobby's own music meanwhile (UI-942).
* **Entering a park** (`0x10183CC0`) plays `cat_globallobbysfx` 4 flat; the original skips it for game type 2 (UI-942).

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
  default is 5) and 0.8 for snow, plus 0.1 x the camera's movement as parallax. At the original scale
  (0.2 engine units a unit) the field is a small near-field around the eye: streaks are 0.1 units
  long and 0.2 x that wide. The camera's 0.1 near plane clips the nearest 0.5 units of it (AUDIO-904).
* Rain: grey `3 x max(0, int(12 x (6 - depth)))` (216 at the eye, 0 at depth 6), alpha 0x80 at the
  streak's trailing end and 0xFF at the leading end, only depth > 0 drawn, additive blend (AUDIO-900).
* Snow: flakes of half-size 0.1 map to atlas quadrant `index & 3`, full white, x and y spread by
  `1 + depth/8`, jittering by +-1/128 each frame. The original's other mode (flag `0x115E94` bit 8)
  swirls flakes along a closed 128-point spline; its meaning is not identified, so it is not drawn (AUDIO-901).
* Bolts: from the strike's top (x, 300, z) straight down to original y = 0 (not the terrain; the
  original's ground point is a float draw, so OpenTPW uses the top's x and z), recursive midpoint
  displacement with roughness 0.6, splitting until a segment's squared length is under 6 + 2u (about
  2 to 2.8 units), a 9/512 branch chance per split up to depth 4 (length `2.5 x (512..1535) / (96 x depth)`,
  bent downward), shape fixed per bolt from a seeded generator, ribbon half-width `1.3 / (depth + 1)`,
  opaque grey pulsing in 500 ms cycles, 192-255 plus rand(0..63), alive `WeatherEffects.LightningTime`
  (2000 ms) with up to 16 at once. There is no screen flash and no sky darkening in the park. The
  displacement, branch heading and pulse curve are OpenTPW's (AUDIO-902).
* Time is the wall-clock frame time capped at 500 ms, frozen while the park is paused; the engine
  zeroes dt under a flag that is not identified (AUDIO-903). The visual random stream is separate from
  every simulation stream, so world hashes are unchanged.
* The renderer is created with the weather itself (`ParkWeather.Created`), inside the staged-candidate
  scope when a save is resumed, and deleted with the level's other entities. GPU allocations are made
  once (one vertex buffer, three materials), browser-safe; the shader is precompiled for the browser in
  `content/shaders/web/weather.json`.
* The forecast is presented by `WeatherForecastPresenter` — see "Forecast warnings" below; the
  pending-quality state the original's message handler would read is `ParkWeather.Forecast`.

### Forecast warnings

`ParkWeather`'s forecast (a quality rolled `Weather.DaysOfWarning` days ahead and applied when its
day passes) is presented by `WeatherForecastState` (pure, unit-tested) and `WeatherForecastPresenter`
(the entity that draws it). The presenter is created with the weather like the renderer, reads the
simulation each frame, and draws one banner line at the top centre of the screen in the overlay pass
(the chat overlay's channel: the original GAME8AA BF4 font, above every screen including pause
menus — AUDIO-905). One line, one background rectangle, laid out again only when the text changes;
no per-frame GPU growth.

What it shows (all of it OpenTPW approximation, AUDIO-906..907 — the original's forecast handler
and its text are not traced, and no weather text exists in the shipped `Language` string tables,
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
| AUDIO-002 | Banks resolve to `<map folder's parent>/<name>HD.sdt`, then `global/<name>HD.sdt`; the HW/HD suffix rule is traced (tp.exe `0x6C58F0`) | The folder order: `TbMapStreamer::BankDoesNotExist` (`0x10015234`) |
| AUDIO-003 | Pitch, delay and reverb are not applied; position is applied only by the positioned playback path | `TbSoundSampleInfo` pitch units and the placeholder 3D update |
| AUDIO-004 | The park view's 0xBD click plays for controls whose flag word (`0x1017F618` reads control+0x44) has a bit set outside 24–26; no key or control property is mapped to it | The meaning of the flag bits in the control's +0x44 word |
| AUDIO-005 | A sentence's next segment is chosen when the current one starts; the original chooses it when the current one's data runs out (`0x10008CC4` → `0x1001A1F0`) | None (a deliberate prefetch) |
| AUDIO-006 | Guests within four cells of the camera are counted by cell rectangle | The area shape inside the radius of the guest counter (`0x100C313C`) |
| AUDIO-007 | The drawer sounds (0x1C open, 0xC2 close) play when the map and research windows open and close | The original drawer's owning screens (`0x1015644C`) beside OpenTPW's window layout |
| AUDIO-008 | A ride script's `REPAIREFFECT 1` plays `cat_kids` event 75 | The repair-level field (object+20) that picks among the event families 75–90 |
| AUDIO-009 | A rejected placement ("Cannot build") plays the error feedback 0x1D | The original build-tool rejection path beside the traced buy-window callers of `0x10139A40` |
| AUDIO-010 | The lobby sentences restart on every island selection | When the original restarts the lobby sentences (`0x1009598C`) |
| AUDIO-011 | An RSE EVENT id missing from the level catalogue plays from the global category of the same name (`incagod.RSE`'s EVENT 3 id 43 exists only in the global `cat_rides`) | The sound library's own category fallback rule |
| AUDIO-012 | Every activated button plays the click 0x1F, front end included | The traced 0x1F witness is the park view's control-message handler (`0x10137FD0`); the front end's own handlers |
| AUDIO-015 | Only the level-driven rain voice is built; the weather thing's own rain voice (thing `+0x54`, `0x10103C4C`) is not | Whether the second voice is audible |
| AUDIO-018 | The positioned voice's pan is constant-power by bearing, not the original's pan law | QSound's processing |
| AUDIO-019 | A positioned one-shot whose event has flag `0x8` loops; flat one-shots do not, although the Mac's loop test (`FillSampleInfo` `0x1000FC1C`) follows both branches; inaudible loops advance silently | Whether flat plays pass through `FillSampleInfo` |
| AUDIO-020 | A placed effect that is a sentence plays its first sound once | The placed sentence's player on the Mac |
| AUDIO-021 | `scape.omp` places and the camera-midpoint listener share the park's sound units after undoing terrain centering; the midpoint follows a fork lead | The Mac's listener position and placement units traced together |
| AUDIO-022 | A refused path-tool commit plays `cat_ui` 175; the Mac site is gated and its mapping to a refusal is inferred | The tool latch `obj+36` and the refusal path |
| AUDIO-023 | The link-end sound (`cat_ui` 139) is not played; no undo sound is traced | The link-end condition; the Backspace handler |
| AUDIO-024 | A staff member's walking/idle voice draws once per park turn from a separate advancing host SplitMix64 stream seeded from the sound seed XOR ASCII `STAF`; its state is not persisted on save/load | The handlers' turn boundary, shared world draw order and presentation-stream continuity |
| AUDIO-025 | December's average is the unnamed word at weather `+0x4B4`, taken as 4; its tolerance and chance are confirmed (`0x101037D4`) | The `.sam` loader's write of `+0x4B4` |
| AUDIO-026 | The forecast applies DaysOfWarning after the roll, on day indices | The Mac's day comparison units (`now` against `+0x20`) |
| AUDIO-027 | The first quality is rolled at park entry; drops start at 0 | The Mac's initial roll |
| AUDIO-030 | The weather's draws come from the park's weather stream, not the world LCG | The Mac's seed source |
| AUDIO-031 | The forecast's warning is an OpenTPW presentation (AUDIO-905..907), not the original's; the drawn drops, snow and bolt are unverified procedural approximations (AUDIO-900..904) | The weather renderer and the forecast's handler |
| AUDIO-900 | The rain is one camera-following wrapping drop field of straight falling streaks, its count the simulated current drops | The original's drop volume, speed and streak shape (FUN_0057d300) |
| AUDIO-901 | Snow reuses the rain's drop count but falls slower with a sinusoidal sway | The original's snow motion |
| AUDIO-902 | The bolt is a jittered polyline from the strike's top (height 300) to the heightfield, shown for a fixed flash time at the strike | The original's bolt shape, segmenting and flash timing |
| AUDIO-903 | The weather's visuals advance with the frame delta scaled by the simulation speed; a paused park freezes them mid-flight | The original's pause behaviour for the weather renderer |
| AUDIO-904 | The visual field, its vertex buffer and the pending bolt queue are capped (600 drops, 4 flashes, 64 pending tops); a level with a higher MaxRaindrops draws fewer drops than it simulates | The original's per-level drop budget beyond the shipped 600 |
| AUDIO-905 | The forecast shows as one banner line at the top centre, drawn in the overlay pass with the original GAME8AA font above every screen (pause menus included) | Which surface the original warned on (the forecast's message handler) |
| AUDIO-906 | The forecast's kind classifies the pending quality by the converter's bands; the undecided snow/rain tie reads "snow or rain" and the lightning band composes "thunderstorms"; the wording is OpenTPW's (no weather text exists in the shipped Language tables) | The original's forecast message handler and its text |
| AUDIO-907 | One message when a forecast appears and one when it applies, plus one transient current-weather line on attach shown for six seconds of simulation time; a park resumed mid-warning announces the pending forecast once | The original's forecast message cadence |
| AUDIO-032 | A ride's scream plays flat; the original places it at the ride's box middle | The box-middle mapping onto OpenTPW's ride objects |
| AUDIO-033 | Minimum distance 2 is confirmed in both builds; the exponent 0.8 (the fork's QMixer lead) and the missing 2.1 maximum floor stand | The gain curve inside QMixer/SoundSprocket (outside the analysable binaries) |
| AUDIO-034 | Held-chain child timing, first-sound selection and child hard stop follow fork-only PC leads | The Mac held-voice tick, variation and stop routines |
| AUDIO-035 | Crowd sample selection uses an independent advancing host SplitMix64 stream seeded from the sound seed XOR ASCII `CROW`; its state is not persisted on save/load | The original source of crowd variation and presentation-stream continuity |
| AUDIO-036 | Crowd parameter-controlled volume is evaluated at PCM segment start | The original propagation into already-playing sentence children |
| AUDIO-013 | De-emphasis is a first-order bilinear-transform shelf of the published 50/15 µs and J.17 curves at the stream's rate | The original decoder's filter realization or an emphasised original stream |
| AUDIO-014 | A failed frame CRC is counted while the frame still decodes; no shipped frame carries a CRC word | The original player's behaviour on a failed frame CRC |

The option volumes are percentages (0–100) like the original's `{on, percent}` pairs, with the
`DefaultVolume.*` defaults of `sound.sam` (effects 75, music 60, speech 75, movie 100; the globals holding them
are medium confidence). Options files from before the change (0–10 steps) are migrated ×10 on load. Only the
slider step granularity within 0–100 stays open (UI-030).

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
