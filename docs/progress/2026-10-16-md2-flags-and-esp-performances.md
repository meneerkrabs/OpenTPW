## MD2 slot flags, node bit 0x8000 and the ESP performance entries (October 16, 2026)

Format round 3. A read-only REA research pass over tp.exe, with the key claims re-read in the assembly:

- **ESP slots 16–19 are the entertainers' performance entries** (`SpritePerformance`, `SpriteAnimationFile.Performances`).
  - The loader `0x540D90` counts entries with a non-zero first byte. The accessor `0x540C70` returns the program (byte 0 − 1), the
    slot (byte 1 − 1) and two program registers.
  - The entertainer's search `0x4D46D0` plays script 13 + (r mod (n − 1)), or 13 for one entry.
  - Exactly the twelve entertainer banks have one entry, program 0 on slot 4, so every entertainer performs slot 4. Before, OpenTPW
    picked among slots 4, 10, 3 and 11 per member.
  - GUEST-015 is narrowed: what remains approximated is the program's steps for Hallow SPR_DR's third and fourth bytes.
- **MD2 texture-slot bits 0x10/0x20 mean wrap.** On the Direct3D path they set render-key bit 0x400000, and the stage decoder
  `0x567620` sets `D3DTSS_ADDRESS` WRAP with linear minification for it, else CLAMP with point minification.
  - Bits 0x1/0x2 have no reader: the runtime model generator writes them. Transparency follows the image's own alpha bits
    (`0x575771`, `0x590C40`).
  - OpenTPW still samples object textures with a mirrored repeat. Choosing wrap or clamp per slot is noted as engine work.
- **Node bit 0x8000 is the UI widgets' hidden-state bit.** It is set by the widget constructor `0x476A90` and the state selector
  `0x476E80`. The draw and picking walks treat it as 0x10. `UiModel`'s single chosen frame already gives that result.

Docs: GUESTS.md, MD2-MODELS.md, FORMAT-BACKLOG.md, README format table. Tests: the parser, the entertainer clip, and a corpus
check that exactly the twelve entertainer banks carry one entry, program 0 on slot 4.
