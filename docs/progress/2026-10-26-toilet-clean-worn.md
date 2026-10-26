## A cleaned toilet no longer keeps its dirty look (October 26, 2026)

GitHub issue 829 reported that closing a toilet for cleaning did nothing, and that the toilet came back open and still dirty even when a cleaner passed by. This traced the whole toilet flow in tp.exe and ran it in the game with the original Jungle data.

- **What the original does.** The toilet window (`0x497640`, refresh `0x4973A0`) has a door and the call button (`b_callhandy`). The door only closes the toilet (`0x48CCF0` → `0x4DF300`) and sends nobody. The call button (`0x4DFE30`) closes it too, sets the request and brings a handyman from anywhere. A handyman with no litter in range takes a called toilet, or one whose gauge byte is below 25 within his DetectionRange (`0x4D7880`); he never reads the open word or the patrol area. The end of his clean (`0x4D73C0` state 11 → `0x4DFD80`, `0x4DF390`) swaps the dirty row for the clean one, writes script variable 8 (`VAR_WORN`) = 0, sets the gauge to 100, clears the claim and the call, and opens the toilet. The window greys its door while the reopening guard `0x4DF290` refuses.
- **What was wrong.** The clean never wrote `VAR_WORN` = 0, so a toilet reopened after a clean kept the worn state its script was given below a gauge byte of 25. Everything else (dispatch, walk, duration, reopening) already matched.
- **The fix.** `ParkEconomy.FinishToiletClean` and the headless handyman's clean write `VAR_WORN` = 0. The toilet window's door now greys exactly while the reopening guard refuses (`0x4973A0`), as the ride window's does for a closed ride.
- **How a player cleans a toilet.** Press the handyman call button in the toilet's window (or wait for a handyman to pass a dirty one); closing it with the door alone calls nobody.
- **Tests.** `ToiletCleaningFlowTests` covers the door, the call, the refused reopening, the clean and its `VAR_WORN` write on a level with a placed toilet, the handyman's world (closed dirty toilet in range, clean closed toilet until called, out of range until called), the headless dispatch, the save round trip, and the shipped Jungle balance.

Still open:
- The toilet window's door greying has no HUD test yet.
