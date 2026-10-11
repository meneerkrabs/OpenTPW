# Sound: catalogues, mixer and park music

October 11, 2026. Status: the original sound catalogues are read, a software mixer plays
through SDL, the park plays its original music, the front end plays its original lobby
music and ambience, control clicks, the map/research drawer, error feedback, ride-script
sounds (RSE EVENT and REPAIREFFECT) and the crowd sentence play through the mixer, and
the advisor speaks (which lowers music and effects while it talks). Rain, thunder, guest
screams, staff and path-tool sounds are traced but not triggered yet (see "Triggered
sounds"). No original data is in the repository.

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

OpenTPW decodes the next segment in the background while the current one plays and
starts it on the frame the current one ends. A Layer II segment decodes in about 84 ms.

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
| Control click | `cat_ui` 0x1F | every activated button (control message 0x100) | `0x10137FD0` |
| Map/research drawer | `cat_ui` 0x1C open, 0xC2 close | the map and research windows | `0x1015644C` messages 0x0b/0x10 |
| Error feedback | `cat_ui` 0x1D | a rejected purchase or placement | `0x10139A40` (buy window callers `0x10163AFC`/`0x10163BFC`) |
| Ride-script sounds | any event of the routed category | an RSE `EVENT type id` instruction: type 3 level `cat_rides` (falling back to the global one, AUDIO-011), 4 level `cat_ambient` (same fallback), 5 global `cat_rides`, 6 `cat_kids`, 7 `cat_staff`, 8 `cat_ambient`, 9 `cat_ui` | `0x100AE930` (switch table `0x3F278`, guard < 10 at `0x100AE938`), submitted at the object's position |
| Repair sounds | `cat_kids` 75 (families 75–90) show; `cat_rides` 111 and 211 hide | an RSE `REPAIREFFECT 1/0` instruction | `0x100B1F78` / `0x100B1EC0`, `0x100B1F00` |
| Crowd | `cat_kids` 0x5B (branching sentence, parameter 0–100) | guests within four cells of the camera; louder/bigger sections as more gather, stopped when nobody is near | count `0x101C2490` (`0x100C313C`, radius 4, cap 100), sentence `0x100BC210`/`0x100BC2E0` |
| Advisor speech | response table: sample and LIP from the global or level speech bank | traced advisor events | `0x10006B7C` |

Traced in the binary but not triggered yet:

* Rain (`cat_ambient` 0x21, a branching sentence whose 0–100 parameter is the rain intensity
  times 100, started by `0x10103C4C` when rain begins, stopped when it ends) and thunder
  (`cat_ambient` 0x20 at the strike's position, `0x10103518`): OpenTPW has no weather
  simulation, so there is nothing to trigger them.
* The level's placed ambient sounds: `scape.omp` records (60 bytes each) that submit
  `cat_ambient` events of the global or level category (`0x10068314`); the scape reader is
  not implemented.
* Positioned ride loops: RSE `EVENT` types 0–2 go to the 3D positioned-loop sound system
  (`0x100AE97C`/`0x100AEA58` → `0x1009F968`, with the ride-type record lookup `0x100118018`);
  the corpus uses them (for example `bouncy.RSE`). That system's distance attenuation is
  not traced, so those instructions stay silent.
* Guest screams: RSE `STARTSCREAM` plays `cat_kids` 71–74 (`0x100B1A60` → `0x100ACA38`),
  `SINGLESCREAM` `cat_kids` 105/106/108/109 (`0x100B1B10` → `0x100ACE68`); which event a
  scream picks from its operands is not traced, so they stay silent rather than guessing.
* Staff sounds (`cat_staff` 135 at `0x100D2CE0` and the staff wrapper `0x100E6D20`): the
  calling staff behaviour is not identified.
* Path-tool sounds (`cat_ui` 101/139/175/206 at `0x10070B98` with path positions, 150–153 at
  `0x100748A0`, 141–144 at `0x10075618`): which tool action picks which event is not traced.
* The lobby's island ambience one-shots (`cat_locallobbysfx` 3/4/5 on a random timer,
  `0x100953F8`–`0x10095450`) and the global lobby effects (`cat_globallobbysfx` 1 thunder
  with the lobby lightning flash `0x1009533C`, 4 at `0x10183DA4`, 2/3 in the online globe
  `0x100923A8`): the lobby has no lightning or globe ambience to trigger them.
* Visitor click (`cat_kids` 129 when the visitor status panel opens, `0x1010B850`) and
  visitor panel sounds (`cat_ui` 95 at `0x101125D8`): OpenTPW selects objects, not visitors.
* The slap reactions 639–641 of `cat_speech` (`0x10007068`): OpenTPW has no slap
  interaction.
* `cat_ui` 0xBD: a control whose flag word (read at `0x1017F618`) has a bit set outside
  bits 24–26 plays 0xBD instead of 0x1F (`0x10138030`); no OpenTPW control maps those bits
  (AUDIO-004).

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
| AUDIO-001 | The second user volume the original ducks during speech is the effects channel | Names of the `TbSysCommand` volume commands at `0x100BB18C` |
| AUDIO-002 | Banks resolve to `<map folder's parent>/<name>HD.sdt`, then `global/<name>HD.sdt` | `TbMapStreamer::BankDoesNotExist` (`0x10015234`) and the quality suffix rule |
| AUDIO-003 | Pitch, delay, 3D position and reverb of a sound are not applied | `TbSoundSampleInfo` pitch units and the placeholder 3D update |
| AUDIO-004 | The park view's 0xBD click plays for controls whose flag word (`0x1017F618` reads control+0x44) has a bit set outside 24–26; no key or control property is mapped to it | The meaning of the flag bits in the control's +0x44 word |
| AUDIO-005 | A sentence's next segment is chosen when the current one starts | When `CPlaceHolderSentence::SoundCallback` (`0x1001A1F0`) runs relative to the end of a sample |
| AUDIO-006 | Guests within four cells of the camera are counted by cell rectangle | The area shape inside the radius of the guest counter (`0x100C313C`) |
| AUDIO-007 | The drawer sounds (0x1C open, 0xC2 close) play when the map and research windows open and close | The original drawer's owning screens (`0x1015644C`) beside OpenTPW's window layout |
| AUDIO-008 | A ride script's `REPAIREFFECT 1` plays `cat_kids` event 75 | The repair-level field (object+20) that picks among the event families 75–90 |
| AUDIO-009 | A rejected placement ("Cannot build") plays the error feedback 0x1D | The original build-tool rejection path beside the traced buy-window callers of `0x10139A40` |
| AUDIO-010 | The lobby sentences restart on every island selection | When the original restarts the lobby sentences (`0x1009598C`) |
| AUDIO-011 | An RSE EVENT id missing from the level catalogue plays from the global category of the same name (`incagod.RSE`'s EVENT 3 id 43 exists only in the global `cat_rides`) | The sound library's own category fallback rule |

The option volumes keep their 0–10 steps (UI-030); the original stores percentages whose
defaults are `DefaultVolume.*` in `sound.sam` (effects 75, music 60, speech 75, movie 100).
