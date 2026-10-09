# Test specification — OpenTPW completion

Required future evidence, not a report of implemented or passing tests.
Corpus and per-platform/fidelity status live in docs/REFERENCE-CORPUS.md and
docs/FEATURE-MATRIX.md. Preserve the full offline scope.

## M0
- Build solution using .NET 8 on macOS.
- Temporary-directory tests for both separator styles, POSIX absolute roots,
  traversal prevention, file roundtrips and archive resolution.
- Asset-backed tests using OPENTPW_GAME_PATH read SAM/WAD textures and ride models.
- Name selected fixtures and assert decoded dimensions, topology/settings and known
  content, not only successful reads. Record edition/locale/patch uncertainty,
  executable identity from docs/RECOMPILATION-ASSESSMENT.md and extracted hashes.
- Optional asset tests with absent fixtures must report inconclusive/skipped, never
  count as asset-gate evidence. Explicit --validate-assets must fail missing required
  files, bad archives and invalid selected fixtures with actionable diagnostics.
- Test truncated records, invalid offsets/counts, bounded allocations/decompression,
  case-sensitive filenames and archive path traversal with synthetic fixtures.
- Missing assets: actionable failure, never silently succeed.
- ISO checksum matches source metadata; assets not tracked in git.
- A matching ISO checksum alone does not identify edition/patch or certify all assets.
- Record revision, SDK version, restore inputs and reproducible commands; separate
  asset-free CI from private licensed-corpus validation and public-package checks.
- Inventory NAudio/Media Foundation/WaveOut, SDL, SPIR-V, ImGui/fonts and transitive
  native libraries by version, architecture, OS, load path and qualification status.

## M1
- CPU ray-plane/grid placement: center/edges/parallel/behind/outside/nonfinite/UI-owned clicks.
- One click creates one ride; footprint remains inside park.
- Deterministic elapsed-time motion; stop freezes phase; restart continues.
- Launch application with Metal; record process/library architecture, rendered
  terrain/ride and UI controls. A Rosetta process is not an arm64-native pass.
- Test editor toggle and camera controls; document temporary Rosetta dependency if present.
- Qualify shader translation, fonts, SDL window/input and editor launch. Identify
  Windows-only audio paths; optional unsupported audio must fail explicitly, not
  crash launch, and cannot satisfy later audiovisual acceptance.
- A procedurally animated prototype is not an original-script-fidelity pass.

## M2–M7
- User-prioritized missing/partial formats: follow docs/FORMAT-BACKLOG.md. BF4
  requires raw-four-bit/RLE/monochrome samples, signed glyph metrics, bounded
  offsets/sizes/allocation, short reads, original pinned coverage and complete
  selected corpus decoding. CPU parsing must not claim game text/atlas fidelity;
  original UI rendering and each native graphics backend are separate gates.
- M6 optional upscaling: validate presets/custom bounds, dimensions/DPI rounding,
  settings persistence, Native baseline, shader bindings, final output including
  sharp UI, aspect/UV/color and unchanged picking. Exercise runtime mode switches,
  resize/minimize/fullscreen/focus, resource retirement and explicit fallbacks on
  each native backend; benchmark CPU/GPU separately. Equal actions/ticks must yield
  identical canonical replaystate across modes. Internal resolve captures excluding
  UI do not qualify final upscaling output. Details: docs/UPSCALING-DESIGN.md.
- Golden binary fixtures and reference traces per implemented format/opcode.
- Before M3/M4 semantic acceptance, compare bounded original runtime traces using
  hashed identities, initial state/actions, observations and predeclared criteria.
  If no safe runnable reference exists, mark runtime fidelity unverified; static
  reconstruction and self-consistency tests do not substitute for that gate.
- Proposed M2 contract tests: fixed tick frequency, stable update/event ordering,
  render-independent state, named RNG algorithm and full RNG/tick state persistence.
  Verify exact canonical simulation replay across platforms and render framerates;
  declare any original-reference numeric tolerances per field before comparisons.
- Save/reload roundtrip, atomic write interruption, schema migration and malformed data.
- Headless visitor pathfinding/queue/economy/staff invariants over long simulated runs.
- Named interim subsets: each selected ride load/build/run/stop/delete; unknown
  opcodes produce explicit unsupported diagnostics, not silent execution.
- Full M4/final acceptance: every required ride/coaster type and opcode from the
  selected edition is implemented and reference-verified; missing, approximated,
  unsupported or unassessed required entries are blockers, even with passing tests.
- Every world's scenarios/unlocks survive persistence and have executable acceptance fixtures.
- UI/audio/video/font/language and first-person reference checks.
- Three mandatory independent native release gates: macOS-arm64 without Rosetta,
  native Windows and native Linux. Record supported OS/CPU targets and matching
  process/native-library architectures; Wine/compatibility evidence is not a pass.
- On each native platform, verify the actual distribution package launches and passes
  rendering/input/audio, strict --validate-assets, canonical simulation replay,
  save/load-corpus and long-session tests. Smoke tests alone are insufficient.
  Packages contain no original game assets; full release remains blocked until all
  three platform gates pass. NativeAOT is optional, not the definition of native.
- Record independent macOS/Windows/Linux evidence and original VM fidelity; one
  platform's prototype pass cannot promote another platform or the original VM.
- Before release qualification, document a supported-runtime decision against
  .NET 8's November 10, 2026 support end; do not upgrade without explicit approval.
- Architect review plus fresh build/tests before declaring each delivered gate.
