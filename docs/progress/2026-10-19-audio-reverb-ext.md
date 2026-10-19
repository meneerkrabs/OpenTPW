## The room reverb is a design choice: `EXT:audio-reverb` (October 19, 2026)

`APPROX:AUDIO-039` is retired. The user decided on October 19, 2026: "nagalm mag EXT" (the reverb may be an extension).
The original sends the 13-dword `SndReverb.map` record (tp.exe `0x6C46B0`) to an EAX 2.0 listener property set or to the
backend's vtable slot `0x50`; QMixer.dll only forwards it to a COM object (`+0x10C`, made at `0x1800B530`) and holds no
DSP, so the actual reverb lives in the sound device or driver and can be neither recovered statically nor captured.
`ReverbUnit`, its constants, its size mapping, the dry preset 0 and the voices it covers are now labelled
`[EXT:audio-reverb]`; the preset selection (`0x55A470` -> `0x51C8D0`) stays ported. [AUDIO.md](../AUDIO.md) keeps the
evidence and states the stand-in is OpenTPW's own by design. The website, README and AUDIO-003's reverb reference no
longer list the reverb as missing. Labels and text only: no behaviour or determinism pin changed.
