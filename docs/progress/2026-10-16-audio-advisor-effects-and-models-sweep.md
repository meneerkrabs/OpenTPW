## Audio, advisor, effects and models sweep from tp.exe and QMixer.dll (October 16, 2026)

A sweep over the audio, advisor, weather, particle, blueprint, head and model approximations, read in tp.exe via REA (and, with the
user's approval, the game's QMixer.dll for the mixer's distance law):

- **Retired:** AUDIO-008 (the repair band is the script's variable slot 5; `0x551320` maps it to events 0, 75, 79, 83 and 87), AUDIO-025
  (December's weather average is the Seasons array's count slot, `0x512e10`, raised by the loader `0x4017a0`), AUDIO-903 (the rain's time
  step is never zeroed; the mask at `0x57ffb1` is 0x4 and nothing sets it), ADVISOR-006 (a clip the archive lacks clears the animation and
  keeps the rest pose, `0x472F60`) and ADVISOR-022 (all 351 rows of the descriptor table `0x76E300`, replayed from its initializer
  `0x5A0A50`, are now bound as data, so an original save's pending advice of any message imports).
- **Weather and particles:** the lightning bolt's walk, branches, pulse colour and ground point (`0x57E810`, `0x57E4B0`, `0x57EE30`,
  `0x512C50`), the rain's 1/16384 parallax grid (`0x580013`), the flake depth, particle quads in the original's slot order (`0x57C620`)
  and the effect volume's height from the model box (`0x558F53`).
- **Advisor:** its own lit shader (ambient 0.15, `0x573FD0`; directional light, `0x429BA0`), the saved advisor clock imported from
  original saves (`0x402E00`), and single x87 precision (`0x563914`, `0x563B7A`).
- **Blueprints and heads:** ghosts drape over the terrain and ripple (`0x53DDD0`), mark every line cell and show the front row and
  access arrows (`0x536100`, `0x52967E`, `0x52989A`, `0x529B1C`); a rider head picks its frame by the largest dot product with the
  camera direction (`0x44B510`); the MD2 slot writer `0x470C90` is read.
- **Audio:** QMixer's distance law (`[BIN:QMIXER-DLL:0x1800a0d0]`, enabled by the flag tp.exe always sets at `0x6d230e`) matches
  OpenTPW's form; the 2.1 far-end floor (`0x6c3b8a`), the chain timing and stop of `0x6bdb50`/`0x6bcab0`, and the fixed-pitch and
  fixed-volume flags are read.
- **Design choices, approved by the user:** AUDIO-037 and MD2-902 are closed as extensions; AUDIO-009 (OpenTPW's extra "cannot build"
  sound, which the original never plays), AUDIO-901, AUDIO-904, PTCL-904 and PTCL-905 are split.
- **Review additions:** GATEM4-001 (the use window W) and GATEM4-002 (closing ends use) are closed as `[EXT:m4-gate-use-window]` and
  `[EXT:m4-gate-closing-ends-use]` (test-harness choices; the user approved them). New `UI-101`: the park camera's field of view is 55
  where tp.exe's projection has 90 (`0x42A990` and `0x54E86F` hand 90 degrees to `0x578BE0`). The particle runs write their uniform
  block once per material, and the blueprint ghosts' per-frame ripple updates one dynamic vertex buffer instead of allocating a model.

The pinned run hash and saves are unchanged; weather and particle presentation keep their own streams. Verified with 4636 tests passing
without game data, 5086 with it, and all five native smokes.

Still open: the mixer's rolloff value and its azimuth stage (AUDIO-018, AUDIO-033, ADVISOR-012), the lightning ribbon's clip-space
quads, per-particle depth writes and the alpha test, the advisor's lobby key panel, the remaining advisor message producers, the
blueprint raise of occupied cells, the head roll reference and the MD2 slot-frame reader.
