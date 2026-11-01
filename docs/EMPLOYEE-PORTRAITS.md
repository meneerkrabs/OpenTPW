# Employee portraits: the independent clone and nominal geometry

The employee window's portrait is the original's independent clone of the selected member's
sprite source (round 11, package W). The clone, the visitor window's and the hire window's
figure are sprites of the registry (`0x007B49F0`) that the sprite scheduler (`0x475360`, the
even 31 ms substeps, `Level.ScheduleSprites`) steps with every other sprite. They live in
`PortraitSprites`, are presentation only and are neither hashed nor saved.

## The clone's lifecycle

- **Full copy** (`0x486530` -> `0x475CF0`, from the window's refresh `0x4B5330`, vtable +0x18, run
  as the window is built and by the cycle arrows): an earlier clone is deleted first, the member
  becomes the window's person (`[0x7C24D8]`), and a member without a source (handle word +0x0C
  is 0: a member resting in the Staff Room) binds nothing. Otherwise the whole 0x118-byte
  record is copied and the clone's x, y, z, scale 8.0 and flags `(f & ~0x200) | 0x50000`
  (`StaffSpriteCloneCopy.Create`) replace the copied ones. The clone keeps the source's due
  time and interval from that moment.
- **Partial copy** (`0x486610` -> `0x475E10`, window message `0x1E`, which the HUD pass
  `0x4813B0` sends every frame after the substeps and before the world is drawn): while the
  person is bound and has a source, the program counter, kind, variant, slot, intro and hold,
  alpha, secondary scale, heading, output word, frame and frame count are copied; the restart
  counter, the stack, the depths, the due time and the interval are not
  (`StaffSpriteCloneCopy.Refresh`). A member who gets a new source is followed through the
  current handle; a member whose source is gone leaves the clone standing, and the scheduler
  keeps stepping it by its own due time and interval, so it plays on after its source is gone.
  A full copy made while there was no source cannot bind one later: the window needs another
  refresh.
- **Delete** (vtable +0x20, `0x4B6B80` -> `0x486630`, the window's close): the clone goes and the
  bound person is cleared.

Because the partial copy runs after the substeps of the frame and before the draw, a bound
clone shows its source's frame of that moment; its own steps only show once the source is gone.
`StaffRenderer.SpriteFrame` draws it from the bank of its kind and variant words, at its slot
and frame, with the direction of its heading word and no camera turn (flag `0x40000` is part of
`0x51000`). The staff's sprite placement (`0x475480`) is not ported, so the clone's heading
word is the member's current heading sector at each copy.

## Employee-only nominal geometry

The employee portrait now uses its own nominal 2.4 canvas units per reference sprite pixel.
The authoritative layout node `0x511` has no explicit inner override. The original type-1
control copies its outer endpoints into its inner rectangle on construction, so `Inner ?? Rect`
gives `(386,280,264,264)`. Frame artwork stays at those same outer bounds and Center anchor.
No hire or visitor inset is substituted.

Signed integer endpoint arithmetic precedes viewport mapping: feet X is
`left + ((right - left) >> 1) + 8`; feet Y is `bottom - ((bottom - top) >> 2) - 2`.
The employee's scale-8 clone helper supplies that final two-unit upward offset, giving authored
feet `(526,476)`. Its nominal pixel factor is `8 * 0.05 * (1536 / 2) / 128 = 2.4`.
The unchanged shared draw applies each frame's `128/A` and `128/B` to both hotspot offsets
and dimensions, including mirrored left edges and reversed U coordinates.

The hire window has its own inner bounds and feet `(626,362)` at 2.4 with no clone-helper
Y offset. The visitor keeps `(598,450)` at 3.6, including its three-unit upward offset.
Generic figures retain 3.2 and rectangle-relative feet. UI-082 owns hire geometry only;
UI-092 owns employee geometry and its remaining qualifications.

Static layout/helper tracing supports nominal geometry, not exact x87/viewport/raster output,
the window depth word or GPU rendered-pixel parity.

## Verification

`PortraitSpritesTests` covers the placement arithmetic, the full copy (record, scale, flags, due
time), the partial copy (what it copies and what it keeps), source loss and return, a missing
source, replacement and close, the visitor body clone, the hire figure's script and phases and the
windows' calls (refresh, update, close). `EmployeePortraitGeometryTests` adds actual employee
callback/Draw checks with synthetic 5- and 8-direction banks, mirrored and unmirrored headings,
unequal A/B densities, non-square art and negative origins; its independent arithmetic oracle
checks all six positions/UVs, texture identity and exactly one quad across authored, small, wide,
square, odd-sized and minimum-scale viewports. Actual hire, employee and visitor widgets, plus a
generic default figure, have separate Draw controls so equal hire/employee scales cannot hide a
wrong hotspot. The source guard is `tools/test_employee_portrait_geometry_contract.py`.
Real-data GPU/visual comparison remains a separate verification requirement.

## Independent VM core seam

The [bounded staff sprite VM](STAFF-SPRITE-VM.md) has clean semantic programs, complete
control/register state and exact full/partial clone-copy operations; the world's sources, the
employee clone and the hire figure (`StaffSpriteProgram.HireFigure`, the script `0x74F758`) run on it.
