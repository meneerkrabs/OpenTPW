# Offline completion feature matrix

Requirements ledger created October 9, 2026. Not a fresh runtime audit or a claim
that concurrent M0/M1 implementation has passed. Full offline scope is unchanged;
online service restoration remains separately scoped.

## Status rules

- Implementation: `unassessed`, `missing`, `approximated`, `implemented`,
  `reference-verified`. Implemented/self-tested does not mean original fidelity.
- Platform: `unverified`, `blocked`, `prototype-pass`, `qualified` independently
  for macOS, Windows and Linux. macOS qualification ultimately requires arm64
  without Rosetta; an x64 bootstrap is recorded only as a prototype pass.
- M7 requires three separate native release gates: macOS-arm64, Windows and Linux.
  Record supported OS/CPU targets for Windows/Linux and matching process/library
  architectures. Each platform requires distribution-package launch, rendering,
  input/audio, strict asset validation, canonical replay, save/load and long-session
  evidence. Smoke tests or compatibility runs alone cannot produce `qualified`.
  Full release stays blocked until all three gates pass. A supported native .NET
  JIT runtime is allowed; NativeAOT is optional, not a release prerequisite.
- Original VM: `unverified`, `missing`, `approximated`, `reference-verified`, or
  `not-applicable` with a reason. Procedural motion never verifies original scripts.
- Every promotion requires revision, asset identity, test/trace ID, observed result
  and platform/runtime architecture. Do not infer status from filenames or builds.
- Interim unsupported diagnostics are allowed for a named subset only. Required
  missing/approximated/unsupported/unassessed features block final acceptance.
  Final required entries need reference verification and all three platform
  qualifications; any VM-applicable entry needs original VM reference verification.

## Selected edition

Optional presentation extension (requested October 9, 2026): configurable world
upscaling, design in [UPSCALING-DESIGN.md](UPSCALING-DESIGN.md). Implementation:
`missing`; macOS/Windows/Linux: `unverified`; original VM: `not-applicable`
(presentation only). Native remains the default and fidelity baseline. Offered
portable modes require platform qualification; optional vendor/temporal experiments
are not mandatory original-game requirements or permission to add dependencies.

Current implementation evidence is in [PROGRESS.md](PROGRESS.md). The native
Mac sandbox, 60 Hz prototype clock and three RID publish builds are partial
evidence only; they do not satisfy the aggregate offline requirements below.

Exact label, locale and patch: **unknown pending evidence**. The supplied ISO is
an intake identity, not proof of an edition. Executable hashes and PE observations
are recorded in [RECOMPILATION-ASSESSMENT.md](RECOMPILATION-ASSESSMENT.md).
Selected fixtures/traces belong in [REFERENCE-CORPUS.md](REFERENCE-CORPUS.md).
Expand aggregate rows into every required ride, opcode, world and scenario once
the selected edition inventory is evidenced; aggregate rows cannot conceal gaps.

## Required offline coverage

All initial entries remain unassessed/unverified because this ledger does not
adjudicate ongoing implementation or invent test results.

| Required area | Gate | Implementation | macOS | Windows | Linux | Original VM | Evidence / remaining requirement |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Asset intake, parsers and decoded content | M0 | unassessed | unverified | unverified | unverified | unverified | Named hashed corpus and strict validation |
| Rendering, terrain, camera and grid placement | M1/M2 | unassessed | unverified | unverified | unverified | unverified | Separate prototype geometry from original MAP semantics |
| Park map, paths, entrances and construction rules | M2/M3 | unassessed | unverified | unverified | unverified | unverified | Original constraints and reference traces |
| Fixed-tick simulation and RNG/replay | M2 | unassessed | unverified | unverified | unverified | unverified | Proposed contract; no determinism claim |
| Versioned saves and original read-only import | M2 | unassessed | unverified | unverified | unverified | unverified | Full state, safe writes, import evidence |
| Visitors, needs, routing and queues | M3 | unassessed | unverified | unverified | unverified | unverified | Stability plus original-behavior comparisons |
| Economy, shops, toilets and staffing | M3/M5 | unassessed | unverified | unverified | unverified | unverified | Reference values/events, not invented rules |
| Every required fixed ride and original scripts | M4 | unassessed | unverified | unverified | unverified | unverified | Per-ride/per-opcode inventory and traces |
| Coaster construction, ride behavior and rating | M4 | unassessed | unverified | unverified | unverified | unverified | All required types and constraints |
| Four worlds and every offline scenario/unlock | M5 | unassessed | unverified | unverified | unverified | unverified | Per-world/scenario executable acceptance |
| Research, adviser, breakdowns and decoration effects | M5 | unassessed | unverified | unverified | unverified | unverified | Original progression/event evidence |
| UI, localization, fonts and shortcuts | M6 | unassessed | unverified | unverified | unverified | unverified | Selected-locale inventory and reference captures |
| Audio, music, video and first-person/ride cameras | M6 | unassessed | unverified | unverified | unverified | unverified | Decoder/runtime evidence and reference comparisons |
| Native dependencies, packaging and long sessions | M7 | unassessed | unverified | unverified | unverified | not-applicable | Platform qualification, not VM semantics |

## Runtime qualification checklist

Required M0 inventory; launch-critical paths must be evidenced before M1 acceptance.
Record exact package/native versions, transitives, binary architecture/load path,
license/distribution constraints and actual results. No dependency change is implied.

| Runtime surface | Known risk / required evidence | macOS | Windows | Linux |
| --- | --- | --- | --- | --- |
| NAudio/Media Foundation/WaveOut | Existing Windows-audio path; explicit optional failure versus later portable audio | unverified | unverified | unverified |
| SDL2 window/input | Native loading, window creation, resize, focus and UI-owned input | unverified | unverified | unverified |
| SPIR-V/shader translation | Native compiler architecture, backend-specific shader output and real render | unverified | unverified | unverified |
| ImGui/fonts/editor | Native binding/font load, editor toggle and launch without unsupported-path crash | unverified | unverified | unverified |
| Process and all native transitives | Consistent architecture; Rosetta only interim; final macOS arm64 without Rosetta | unverified | unverified | unverified |
| .NET toolchain | Pinned reproduction inputs and supported-runtime decision before release | unverified | unverified | unverified |

.NET 8 support ends November 10, 2026 according to
[Microsoft's support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
Record the release-runtime decision before M7; do not silently upgrade.
Runtime Windows-audio context: [NAudio v2.2.1](https://github.com/naudio/NAudio/tree/v2.2.1).
