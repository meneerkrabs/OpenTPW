## Jumps, tunnels and the Space Racers Teleport (October 18, 2026)

The track add-ons of the go-kart and water rides are playable, following tp.exe through the whole cycle (docs/TRACKS.md "Add-ons"):

- **Upgrade list.** The ride window's upgrade button opens the original's list window (layout table 0x755750, rows 0x4AE140) instead of OpenTPW's confirmation box:
  the ride's researched add-ons with their price, then the next level or, while the ride waits for its upgrade, a cancel row that drops the request and opens the ride
  again (new `ParkEconomy.TryCancelUpgrade`). A list without rows shows the original's message (Instant Action, level 2, nothing researched).
- **Build tool.** An add-on row arms the build tool for that ride when the park can pay. The hover snaps to the ride's tiles and turns the add-on along the tile's flow
  (0x5236B0), the placement stands its track cells on straight, unflagged tiles of the ride (0x528A70), and the click walks the circuit again (0x524B45): the karts or
  boats leave, the pieces' clips start or stop, and a valid ride opens.
- **Walk and ends.** The sections of add-on tiles number their run and point at the add-on; on a run's second tile the add-on's two open ends show or hide by what lies
  beyond them (0x52ADE3), with the original's quirk that these bits gather over the whole walk.
- **Vehicles.** The karts and boats drive on the add-on's own surface (0x452A60). The Space Racers Teleport hides a vehicle, its shadow and its riders' heads in its first
  gate, plays its pass clip and shows the vehicle again past the second gate (0x548C81 → 0x548E80); the waiting flag of its section is saved.
- **Window.** A click on an add-on opens its ride's window, and the window's arrows skip add-ons (0x486920, 0x4828F0).

`APPROX:TRACK-008` is retired. `[EXT:addon-sale-walk]`: removing an add-on walks its ride's circuit at once, where the original leaves the tiles without pieces until the next
walk. Not traced: what a ride's demolition does with the add-ons on its track, and the Extra Tube's own window. A Debug build captures a ride with an add-on through
`--debug-kart-track` or `--debug-water-track` with `--debug-track-addon <Info.Id>`. The front page no longer lists jumps and tunnels as missing, and the README no
longer calls the coaster and tour riders' heads missing.
