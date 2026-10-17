## Untriggered sounds and QMixer's pan and volume law (October 17, 2026)

Sounds the original submits from engine code now play in OpenTPW: the upgrade sound, the size-classed placement and demolition cues, the
message tube's posted and clicked sounds, the postcard, the research window's hums and effort slider, and the guests' till, drink, meal,
toilet, pause, vomit and ride-removed voices. Each trigger is tagged with its tp.exe address at the code. All of it is presentation: the
voices use no world or guest random draws and the determinism pin did not move.

Positioned mono voices follow QMixer.dll's software channel stage: the offset is rotated into the camera's basis (pitch included), the pan is
44.444 hundredths of a dB per degree mirrored behind the listener, and the quieter channel is 10^(-|pan|/2000) of the louder one, so the centre
is not 3 dB down. Tests cover the mixer law and which events fire.

AUDIO-009 is retired (the build tools never refuse with a sound). AUDIO-003, AUDIO-004 and AUDIO-038 keep their narrowed text: reverb, the
senders of the 0xBD click and the in-range resume of a stopped sentence are still unread. AUDIO-018 now lists only the axis mapping of the
camera rows and the flat voices' placement as open.
