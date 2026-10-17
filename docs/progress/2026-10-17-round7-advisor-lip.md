## Advisor node bits: every clip starts from an all-shown model (October 17, 2026)

The advisor's model shows and hides its hats, props and tickets through node bit 0x10. OpenTPW assumed the bits persist and rebuilt them from a
hidden base state, which hid the ticket the level advisors hold in the ticket advice (clips 17 and 18) and carried a hidden Ticket2 from clip 19
into clip 20. tp.exe clears them all at every clip end: the sampler `0x4735D0` starts the idle set 12 through `0x472F60`, which calls `0x472310`
for the clip that ended, and the advisor model's flag 8 makes that clear every node. So each clip starts from an all-shown model and hides only its
own node list, then the selectors, the bug head and its blink toggles act. ADVISOR-026 is retired; nothing persists between clips or responses.
ADVISOR-017 is narrowed: all eight callers of the game pause `0x4092A0` are identified (the map window and the postcard included), and the map
window and the Game Options dialog also run the advisor stop path, which OpenTPW does not do yet. Checked with silent offscreen captures of the
jungle ticket advice (response 374) on a clocked advisor; the lip, mouth and clock code was re-read against `0x599A90`-`0x599BEA` and needed no change.
The determinism pin does not move: presentation only, no world RNG.
