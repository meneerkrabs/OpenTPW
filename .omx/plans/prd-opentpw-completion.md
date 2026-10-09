# PRD — OpenTPW completion

Approved 2026-10-09: save and execute the full completion plan.
Canonical roadmap: docs/COMPLETION-PLAN.md.
Evidence ledgers: docs/FEATURE-MATRIX.md and docs/REFERENCE-CORPUS.md.
These requirements do not assert that current M0/M1 work has passed any gate.

## Acceptance
- M0: reproducible .NET 8 build, portable paths, real-asset diagnostics, passing tests.
- M1: macOS launch, Jungle sandbox, actual ride geometry placement and explicit start/stop.
- M2: deterministic park model and persistence, validated original read-only import.
- M3: visitor/queue/ride/economy/staff gameplay loop with headless regression tests.
- M4: evidence-backed original ride VM, all ride types and rollercoasters.
- M5: four worlds and complete offline scenarios/progression.
- M6: audiovisual/UI parity and performance qualification.
- M7: three mandatory independent native release gates: macOS-arm64, Windows and
  Linux; all three must pass before full release acceptance.

## Acceptance contracts
- Final scope remains the complete offline game, all required rides, coaster types,
  scenarios and required original VM opcodes of the selected edition. Unsupported
  diagnostics can pass a named interim subset, never final acceptance.
- Track implementation (missing/approximated/implemented/reference-verified),
  macOS/Windows/Linux qualification and original VM fidelity independently.
- Exact edition label, locale and patch remain unknown until supported by evidence.
  Verified local executable hashes are in docs/RECOMPILATION-ASSESSMENT.md;
  binary intake does not prove reference execution or game completeness.
- M0 records revision, SDK/restore inputs, extracted asset manifest and a named
  corpus with decoded-content assertions. Optional asset tests without fixtures
  are inconclusive; --validate-assets is a strict gate that fails missing required
  files and malformed selected fixtures.
- M0 inventories NAudio/Windows audio, SDL, SPIR-V, ImGui/fonts and transitive native
  libraries. M1 proves launch-critical backend, architecture, shader, font, editor
  and input paths; explicit optional unsupported paths remain completion blockers.
- Before accepting M3/M4 semantics, obtain bounded original runtime traces with
  identity hashes, initial conditions, actions, observations and comparison criteria.
  Static analysis, stable simulation and procedural motion are not runtime fidelity.
- Proposed M2 determinism contract: fixed tick frequency, stable update/event order,
  named RNG algorithm with full persisted state, render-independent simulation and
  exact canonical cross-platform replay. Original-reference tolerances are declared
  per field before comparison. This contract is a requirement, not implemented status.
- .NET 8 support ends November 10, 2026. Record an explicit supported-runtime decision
  before release qualification; no SDK/package upgrade is authorized by this note.
- Each native platform gate records supported OS/CPU targets, matching process/native
  library architectures, package launch, rendering/input/audio, strict asset validation,
  canonical simulation replay, save/load and long-session evidence. Windows/Linux
  smoke tests alone and compatibility runs do not qualify a native release.
  NativeAOT is optional; a supported platform-native .NET JIT runtime is acceptable.

## Constraints
- Optional configurable world upscaling follows docs/UPSCALING-DESIGN.md: Native
  is the default, portable Linear/Nearest first, UI at output resolution, settings
  outside park saves, no simulation/replay changes. Offered baseline modes require
  three-platform qualification; optional vendor/temporal experiments do not block
  native gameplay and do not authorize dependencies or SDK upgrades.
- Do not redistribute original copyrighted assets or bypass copy protection.
- No unrequested dependencies, branches, commits or public deployment.
- Online restoration remains separately scoped.
- Preserve upstream engine where practical; use binary analysis as reference.
- Recompilation and NativeAOT are evaluated separately; no unsupported automatic-port claim.
- Persist honest progress; a milestone is not completion of the entire product.
