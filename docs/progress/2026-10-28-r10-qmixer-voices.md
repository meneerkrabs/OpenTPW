## QMixer voices: flat placement, axis mapping, voice flags and fade arithmetic (October 28, 2026)

Round 10 package G reads the sound path of `tp.exe` and `QMixer.dll` statically (nothing was run) and ports what it finds.

**AUDIO-018 and ADVISOR-012.** The axis mapping of the camera rows is read: the adapters (`0x6D2640`, `0x6D2A10`, `0x6D2B20`)
hand QMixer (x, z, y) because the driver byte `+8` starts as 2 and nothing writes it, and QMixer negates the third component
(`0x18001020`) because the engine byte `+4` is the inverse of bit 3 of the zeroed `QSWaveMixInitEx` flags. The two reflections
make a rotation, so the pan keeps the side `AudioListener.Azimuth` already gave it; a test replays the whole pipeline over 400
random cameras. The flat voices are read too: an event with the any-distance flag `0x200` (all of `cat_speechSFX.map`, the
advisor's speech included) is placed by `0x6BC51D` through `QSWaveMixSetPosition` with the integers 0, 1, 0, which QMixer turns
into azimuth 0, distance 9.2e-5 (`0x18007ABD`, `0x18009DC0`): distance gain 1 and `SetPan(0)`. `AudioVoice.PlacedFlat` now makes
such a voice centred and never turned down by distance even when its caller gave it a place and range. ADVISOR-012 is retired.
QMixer's Doppler is read (`0x1800A178`-`0x1800A2C3`, `0x18005550`) and pinned on the offline mixer (`AudioDopplerTests`) but not applied (`AudioListener.DopplerEnabled` is off): voices without the loop flag would play at 1 / (1 + x), x = (direction . relative velocity) / 331.36, at most 10, from the listener velocity of `tp.exe` (`0x6D27B0`), the position change of each changed listener update with the previous place starting at the origin (so a park's first update, measured from the lobby listener at (0, 0, -50), `GameAudio.UpdateLobbyListener`, gives its whole place as a velocity, and the factor depends on the frame rate). The gameplay capture of the PC game shows the same lobby sentence at the same speed before and after a park, which this reading does not predict, so the gate or reset that keeps the factor at 1 is still missing and AUDIO-018 stays with that item plus the pause's listener height (UI-096), the dB quantisation of the volume product and the device-unit frequency rounding (`0x1800C760`).

**AUDIO-003.** The fixed-pitch flag `0x10000` is unreachable: `SoundVoiceFlags.FromEvent` mirrors the builder `0x6BBE90`, every
other write to a voice's flag word was scanned, and a test covers all 65,536 event words. AUDIO-003 is retired (its reverb part
was already `[EXT:audio-reverb]`); the voice-flag questions of the sound code now go through `SoundVoiceFlags`.

**AUDIO-040.** The fade poll runs on the library's own `_beginthread` worker, a new thread at the default x87 control word
`0x27F` (53-bit precision), while the main thread is at `0x7F` (24 bits). `HeldSoundLifetime` multiplies the group floats and the
volume in doubles for the poll and in floats elsewhere (master 0.75 and effects 0.7 on 60 give 32 and 31). The worker's
`Sleep(100/250)` scheduling and the missing saved quality preference stay, narrowed.

**AUDIO-038.** A sentence stopped out of range replays its segment from the beginning with fresh volume, pitch and delay draws
and keeps the successor chosen ahead (`0x6BDF50`, `0x6BC410`, `0x6B9AB0`); before, it jumped to the successor. A started
any-distance voice ranks in the pool at the origin of the sound space, where its sample start put it (`0x6BB9F0`).
Still open: a stop inside the decoder's last look-ahead, the initial submission's admission and the group-enable stop and
restart.

No simulation state, save or determinism digest changes; sound draws stay on the shared sound seed. Tests are synthetic.
