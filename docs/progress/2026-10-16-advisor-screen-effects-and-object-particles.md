## Advisor screen effects, object particles and the refused path click from tp.exe (October 16, 2026)

Four advisor, particle and audio items were built from tp.exe, read via REA:

- **The refused path click** (AUDIO-022 retired): the click is two handlers routed by `0x46c210` (message `0xF0`, slots +4 and +8 of
  vtable `0x6fe9e0`). The press handler `0x524790` sets byte `0x8186d8` and every exit of the release handler `0x524960` clears it, so
  cat_ui 175 plays at the release that follows a park press. The gate word `0x785a2c` is the layout-replay recorder (`0x785a08` + 0x24,
  set by `0x404140`), which OpenTPW never runs. Wired into both the park HUD and the Theme Park Inc HUD.
- **The advisor's ticket and key screen effects** (ADVISOR-025 narrowed): the timer also starts the screen-space Key effect, template 87
  at native (90000, 0, 7000), top right (`0x5999AD`). `0x4B9340` is the lobby key panel, inert in the park; the park HUD counter is
  refreshed by the HUD's own message `0x1E` handler `0x4A0FB0`. Screen-space particles are now drawn.
- **Advisor effect groups** (ADVISOR-026 narrowed): the accessory-bit switch of `0x598DC0` (`0x598EAA`) and the effect-group system
  `0x5ED5A0` (size), `0x5ED660` (move), `0x5ED700` (orbit) and the per-frame `0x5ED770` are ported: the centre follows t unclamped, the
  radii t clamped to 1, the angle is `frac(i/n + t*turns)*512` on the truncated 512-entry table. Group 0 is emptied at response start
  (`0x5993B3`), response end (`0x59992D`) and on kill (`0x59972A`).
- **ADDOBJ particle effects** (PTCL-908 narrowed): effects follow their key every object pass (`0x55189D`, `0x51FE30`, type 2 turned via
  `0x51FD90`); ADDOBJ_EXT (opcode 9, `0x5520E8`) has a handler; a negative key starts at the footprint box middle (`0x556B90` →
  `0x466B70`), and type 2 with a negative key is refused.

Everything is presentation-only and draws from no random stream; the determinism pin is unchanged. `ParticleObjectRecord` gains optional
`Type` and `Key` fields (type 0–2); older saves load and their records simply do not follow a key. Verified with 4309 tests passing
without game data, 4740 with it, and all five native smokes.

Review fix: the coaster pylon debit comment in `CoasterBuilder` read `0x785a2c == 0` as "money on"; the word is the layout-replay
recorder (the debit at `0x4352A0` is skipped only while a layout replays), so the comment now says so.

Still open: OpenTPW's lobby has no key panel; the order of the screen particle pass against the HUD and the advisor image (PTCL-909: they
draw in the world pass, under both); advisor node bits are rebuilt per response instead of living on the model instance; x87 precision; the
scoped effects of `+0x4E0` loaded by `0x414420` into pool slots (`0x51FA20`) from names at `0x788F40`; and ADVISOR-006, which waits on
the reader of mesh bit 0x10 (RIDES-042).
