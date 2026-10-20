## The ride window shows why a ride stays closed, and a coaster circuit closes only when it can complete (October 18, 2026)

A coaster could be closed through a closing link that fails the validator and then never opened: a player's Hades had a closed circuit whose last pylon stood one
cell from the station and was marked invalid, so the completion click and every new pylon were refused, the reopening guard refused the door, and nothing said why.
The closing click on the station end now tests the closing link and the pylons first, for a fresh circuit as already for one the demolish button cut open
(`[EXT:coaster-completion-click]`); the hovered closing target shows the refusal before the click. A refused click leaves the circuit open and the HUD says why,
in all eleven languages: the track can't be completed from the last pylon, or the pylons marked red must move. A closed circuit whose closing link fails (closed by
an earlier build, or a pylon moved after the closing) opens again when the track tool is entered (`[EXT:coaster-failing-closure-opens]`), so it is built on from its
last pylon. The reopening guard's coaster test, and the reopening after a repair, take the guests' predicate (complete, not crossing itself, not in the tool;
tp.exe `0x441970`) instead of the trains' state.

The ride, shop and sideshow windows show the original's status banner, the `chev` control over the preview (tp.exe `0x485f60` with mode 2, `0x4ad720`): CONDEMNED,
TRACK INCOMPLETE, TRACK INVALID, the upgrade, repair and cleaning statuses by how far the mechanic or handyman has come (none hired, on strike, on the way, at work,
waiting; at work is a world mechanic in state 13 or a handyman in state 11 on the object, `0x5065d0`), BROKEN DOWN, QUEUE NOT CONNECTED and CLOSED, the first that
applies, in UITEXT 364-389, wrapped into the control, in the colours of table `0x74fb50` on the text only (`0x65c5d5`). The ride window's refresh (`0x4ad4e0`)
greys the door for the two track statuses and while a closed ride's reopening guard refuses, and hides the door, queue, track, call, move, Ride it! and upgrade
buttons of a condemned ride; the shop and sideshow windows show the banner and do neither. EXIT NOT CONNECTED is not shown yet (`APPROX:ECON-049`: OpenTPW's
entrance and exit cells keep no link byte for the exit test `0x4de5a0`). docs/UI.md and docs/COASTERS.md describe both.

Tests: `ObjectWindowStatusTests` (the ride window's banner, colour and door through a close and a mechanic call; a world mechanic from the call to the work; a
condemned ride's hidden buttons and a condemned shop's door; the shop window's banner and its door during a call), `RideStateRulesTests.TheStatusBannerTakesTheFirstStatusThatApplies`,
and `CoasterTests` (`ClosingThenCompletingAppendsTheStationEndNodes` with a refused closing, `ACircuitClosedThroughAFailingLinkOpensAgainForTheTool`,
`TheClosingTargetShowsWhatItsClickAnswers`).
