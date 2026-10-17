## Original save guest bodies name their queue, admission, resume and event-ring fields (October 17, 2026)

The guest body of an original park save was already read to the last byte; this round traces what its remaining fields mean in `tp.exe` and tightens
the reader. The queue links (`mQNext`, `mQPrev`), the admission flag (`OnAdmittance` `0x5012B0`, cleared by the dismissal `0x5012F0`), the saved
state (the state to resume after a spot animation, `0x4FC800`), the balloon sprite's previous position and every guest-object offset are documented on
`SaveGuest` and in `docs/TPWS-PAYLOAD.md`. The person's event ring is typed (`SavePerson.ThoughtEvents`, `RecentEvents`: 32 slots of type and value words,
head advanced by `AddEvent` `0x50C100`). The reader now refuses what the game's own code cannot write: a state above 21 (all 52 callers of the state setter pass at most 0x15; the setter itself does not guard it), an admission flag above 1, a ring
head above 31 or an event type above 0x19. A corpus test reads every guest of Easymode and the eleven private saves (35 guests) and checks that each body
is consumed exactly; a refused guest aborts the whole park import, and the bounds are verified against this tp.exe build only. Synthetic tests cover the round trip, truncation and the refused values. No guest in the available saves stands in a queue or on a
ride, so those fields rest on the code; the SAVE-002 wording now names what stays unimported.
