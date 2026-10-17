## Load Park list flow is an approved EXT (October 17, 2026)

- **What changed.** Labels and docs only; behaviour is unchanged. The original's load list (attribute 0x81) selects the row under the
  pointer and loads on one click (`0x6656a0`, `0x6655a2`); OpenTPW keeps its select-then-load flow because its Load Park button
  (`[EXT:load-chooser]`) acts on the selection, which a hover would retarget on the way to the button. The user approved keeping the
  button on 2026-10-17, so `APPROX:UI-108` is retired and the flow is `[EXT:load-list-select-then-load]`.
