## Lobby: sky colour, bats and butterflies, island clips (October 16, 2026)

The lobby's animations read in tp.exe and built (UI-018 narrowed):

- **The lobby generator** returns its step rotated right by 13 bits (`ror 0xd` at `0x5e0f4b`, also `0x5d98d1`, `0x5da0d4`, `0x5e113f`).
- **The sky**: the original clears to the scene's fog colour `0xFF44DDFF` (`0x5d8b50`, `0x5737aa`) and the island's SKYCOLOUR eases
  the engine sky's colour grid by (target − current) >> 4 a frame from black, the red channel moving toward the blue target (`0x5d96c0`);
  until the cloud layer is drawn, OpenTPW paints the backdrop with that eased colour as a stand-in (UI-018).
- **Bats and butterflies** (FLYINGMESH) spawn as groups 50 units above their island's camera target (`0x5d9db0`, `0x5da020`), turn 0.1 ×
  step toward a random point, fly at their speed and pick a new point within √500 (`0x5d9830`, `0x5d9b50`), facing their direction and
  looping their `m1` wing cycle (`0x5d81c0`); their draws come from the lobby generator ahead of the islands (`0x5d5c50`).
- **Islands play their M1/M2 clips** when the current clip has under 1 ms left (`0x5e11d0`, `0x473f50`), picked with a presentation stand-in
  for the C runtime's `rand()`.

Still open: the engine sky's cloud layer and the scene fog are not drawn, the enter-zoom sequence (`0x5e1e30` → `0x5e1e50`) is read but not
built, and the key-award animation is not traced. UI-941 narrows to the camera wander.
