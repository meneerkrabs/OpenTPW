# MD2-900: Texture frame lifecycle

## Evidence-backed implementation scope (October 20, 2026)

This advances the original plan-only proposal. The draw reader is already present
(0x580530 → 0x57A420, ANIMATINGTEXTURES); it is not implemented a second time.
The new [bounded PC lifetime contract](../reverse/TEXTURE-FRAME-LIFETIME.md)
identifies ordinary replacement/unbind/replay cleanup and distinguishes
ended-but-bound channels from native held ID12.

Ordinary outgoing texture clips reset every raw-flag0x40 slot, including fixed
items. A new delayed first key does not retain the outgoing bound frame. Held
ID12 instead keeps the endpoint but ceases writing and skips outgoing cleanup
on its next start. Save/restore preserves legitimate history without treating
restoration as replacement. Synthetic regression coverage exercises those gates.

## Validation and completion boundary

Independent source review and exact-head CI are required before merge. Local
runtime tests have not been run for this candidate. Static evidence is not a
claim of a visual or original-runtime comparison. No original media is published.

MD2-900 remains registered for aggregate return-from-list-pass cleanup and
headerflag4/additive-mode restoration outside the always-posed set-mode route;
RIDES-032 and invalid-frame defensive policy MD2-901 remain separate. This is
not universal texture-lifetime equivalence or an approximation retirement.
