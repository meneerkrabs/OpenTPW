# COMPAT-010: Trace texture filter state mapping

## Current bounded implementation (2026-10-20)

Independent host texture creation flags; native filtering/state mapping remains open. See [implementation contract](../TEXTURE-CREATION-FLAGS.md). Recovered the independently source-reviewed frozen change and integrated current main de0a3bff. Synthetic C# fixtures are included; exact-head CI and rendered validation are not yet claimed. No fidelity identifier is retired. The original plan below is historical and its plan-only description is superseded for this bounded slice; unresolved evidence gates remain.

## Historical proposal

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `approximation`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
Material selects preset samplers; clamped slots retain preset MINFILTER while traced PC state would use point, and option-to-state mapping remains unresolved.

## Source sites
- `source/OpenTPW/Render/Assets/Material.cs:117` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Render/Assets/Material.cs:118`: `var world = type is SamplerType.Anisotropic or SamplerType.AnisotropicWrap or SamplerType.AnisotropicRepeat;`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Texture-state writer/option reader trace resolving TEXTUREFILTERING versus SPRITESHADOW indices.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Build a mapping table for option bits, slot address state and sampler flags; implement only verified per-slot filter differences.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Wrap/clamp crossed with point/bilinear/trilinear; each original preset; magnification/minification/mipmap transitions; no silent loss of extension anisotropy.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: Material and MD2 texture-state owners.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
