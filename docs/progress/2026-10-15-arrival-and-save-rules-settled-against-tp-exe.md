## Arrival and save rules settled against tp.exe (October 15, 2026)

Seventeen arrival and save approximations were checked again, item by item, in tp.exe (leads from an earlier REA survey, every address
re-read):

- **The booth books the fee with the bank** (`0x4ffade` → `0x4d0600`): the guest's purse (`+0x1A0`) is never touched, as in the original
  (ARRIVAL-007 retired); the booth refuses an admission only at a bankrupt park, a closed one stopping visitors on their walk to it
  (next entry). The determinism hash moved to `0x9AA9973FC2A6A5D6`, and with the staff candidate draws and the guest walk to `0x944788E41E278016` (docs/DETERMINISM.md).
- **Arrival groups** keep a negative remainder after the cut (`0x4cf6e5`), which every reader treats as 0 (ARRIVAL-004 retired), and a
  guest the booth sends to wait reruns the verdict N + 1 turns later (`0x4ffa72`, `0x4ffb01`; ARRIVAL-006 narrowed).
- **The counts-test's track terms** apply to the park points that size the arrival groups (`0x4dd9ad` → `0x441970`; ARRIVAL-003 narrowed).
- SAVE-930 is OpenTPW's own layout checkpoint (`[EXT:tpi-layout-checkpoint]`); thirteen arrival and save items are narrowed, among them
  the bus, seaplane or ferry by group size (`0x4cf48c`) and the coaster save block (`0x437300`, `0x437b60`).
