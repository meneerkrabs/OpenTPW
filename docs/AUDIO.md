# Sound: catalogues, mixer and park music

October 9, 2026. Status: the original sound catalogues are read, a software mixer plays
through SDL, the park plays its original music, the park view click plays its original
UI sound, and the advisor speaks through the mixer (which lowers music and effects while
it talks). Ambient, ride, guest, staff and most UI sounds are not triggered yet. No
original data is in the repository.

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

| Sound | Event | Source |
| --- | --- | --- |
| Park music | `cat_music` 2 (branching sentence) | `0x100BC144`, parameter `0x101C2444` |
| Click in the park view | `cat_ui` 0x1F | `0x10137FD0` |
| Advisor speech | response table: sample and LIP from the global or level speech bank | `0x10006B7C` |

Other calls found in the binary (not triggered yet): `cat_ui` 0x1C (map/research drawer,
`0x1015644C`), 0x1D (error feedback, `0x10139A40`), 0xBD (park view click with a modifier),
the slap reactions 639–641 of `cat_speech` (`0x10007068`), the crowd event 0x5B of
`cat_kids` with a 0–100 parameter (guests within four cells of the camera, `0x101C2490`),
and the lobby events of `cat_globallobbysfx` and `cat_locallobbysfx`.

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
| AUDIO-004 | The park view's 0xBD click modifier is not mapped to a key | The input flag behind `0x1017F618` |
| AUDIO-005 | A sentence's next segment is chosen when the current one starts | When `CPlaceHolderSentence::SoundCallback` (`0x1001A1F0`) runs relative to the end of a sample |

The option volumes keep their 0–10 steps (UI-030); the original stores percentages whose
defaults are `DefaultVolume.*` in `sound.sam` (effects 75, music 60, speech 75, movie 100).
