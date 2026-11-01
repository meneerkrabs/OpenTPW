## Cameras and picking (November 1, 2026)

Round 11 package Q reads the camera and picking items in tp.exe (REA, `dis.py`, the Unicorn oracle; the Mac decomp only pointed to the code).

- **RIDES-021 (narrowed)**: the picker's model test runs the normal refresh `0x473C30` / `0x473A40` once per instance on the root node (every non-fixed object has root flag `0x10000` from the conform `0x469940` and model flag `0x20000` from the loader `0x413C10`). The group flag `0x80000` (`0x447790`, `0x4477D0`) and the node bits `0x20` and `0x8000` are shown unreachable by a sweep of the whole `.text` and a census of the 274 shipped object models. Open: the track-tile piece instance (`0x53BF30`) and the hover highlight (`0x469180`).
- **UI-930 (narrowed)**: the result is published two frames behind the sprite pass (`0x532C17`, `0x540B30`, `0x540B6E..0x540B7E`, `0x46C110` to `0x7B05AC`); equal clip W keeps the first sprite visited (`0x5890E0`). A question about the host quad size is recorded, not changed.
