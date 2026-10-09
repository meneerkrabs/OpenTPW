# OpenTPW: plan to a fully working game

Reference date: October 9, 2026. Status: approved for phased execution.

## Goal and definition of done

OpenTPW becomes an independently maintained reimplementation with which a user
with original game files can play the complete offline Theme Park World game.
Apple Silicon/macOS is the first platform; Windows and Linux follow on the same
game core. A demo, a running model, a successful build or a native executable is
not the same as a complete game.

Full offline acceptance covers the four worlds, scenarios and unlocks, park
building, attractions and roller coasters, visitors, staff, economy, research,
advisor, audio, UI, camera/first-person, and reliable saving/loading. For each
component, the difference from the chosen original version is tracked explicitly.
Restoring the discontinued online service is a separate, later project: it was
excluded from the previously approved scope and is not added silently. The same
applies to Theme Park Inc (Sim Coaster) and the sandbox modes with TPW, TPI or
combined content: goals after offline TPW acceptance, with their own gates and
without affecting the critical path above (see README and docs/THEME-PARK-INC.md).

`docs/FEATURE-MATRIX.md` separates implementation, macOS/Windows/Linux
qualification and original VM fidelity. An intermediate gate may accept a named
subset with explicit unsupported diagnostics. Final acceptance requires all
offline features, attractions and required opcodes of the selected edition;
`missing`, `approximated`, unknown requirements and unsupported requirements
remain blockers. A passing build, self-consistent replay or prototype is not
reference verification.

## Checked starting situation

This is the inspection baseline of upstream `453e779`, not the current status of
concurrent M0/M1 edits. New implementation or test claims require fresh evidence.

- Fork: https://github.com/meneerkrabs/OpenTPW; upstream `main` at `453e779`.
- Latest upstream commit on the reference date: September 7, 2026, editor reconnected.
- .NET 8/C#, Veldrid, own file parsers and the existing MSTest project.
- `source/OpenTPW/World/Level.cs` creates lobby islands, not a playable park.
- `source/OpenTPW.Files/Public/MapFile.cs` does not reconstruct a map.
- `source/OpenTPW/World/Ride.cs` has no visible attraction model; the VM has
  incomplete parsing and animation opcodes. No working original ride cycle.
- `source/OpenTPW/Client/Renderer.cs` forces Vulkan and contains two build errors
  with the current .NET 8 toolchain. Paths are pinned to Windows separators.
- The native SPIR-V library in the current dependency is macOS x86_64, not arm64.
- The old `apple-silicon` branch is a divergent architecture from March 2024; no
  safe wholesale merge with `main`.
- The supplied ISO was downloaded locally and its SHA-1 was checked against the
  Archive.org metadata: `e47675b295a958f82b9b21dee0546a5ad9eea1e8`. Assets stay
  outside git; integration tests use `OPENTPW_GAME_PATH`.
- The exact edition label, locale and patch level are unknown until asset/binary
  evidence identifies them; the ISO name or checksum does not prove that
  identification.
- Locally verified executable hashes and PE information are in
  `docs/RECOMPILATION-ASSESSMENT.md`; this proves neither a runnable reference nor
  fidelity.

## Route choice: reimplementation plus targeted reverse engineering

The existing C# engine remains the basis for now. Use the original executable as
a behavioral reference, not as an unnoticed bundled runtime. Investigate original
logic in a targeted way where formats, simulation calculations and script opcodes
are unclear. Replace this approach only after a measurable experiment that
demonstrably saves work.

| Route | Benefit | Limitation / decision |
| --- | --- | --- |
| OpenTPW C# reimplementation | Reuse existing parsers/rendering; fast vertical slices | Missing simulation must actually be implemented; preferred route |
| Matching decompilation with Ghidra/reccmp | Reconstruct original functions and compare them | Verify compiler/ABI and binary first; deploy targeted |
| Static recompilation of original executable | Original machine code to new native code | Windows PE/x86, imports, callbacks and graphics runtime are not solved by console tools |
| Wine/compatibility | Run the original game as a reference | Not a completed OpenTPW port; separate from product acceptance |
| Rosetta for current Intel Mac libraries | Fast temporary Mac bootstrap | Not native arm64; not the end product or long-term foundation |
| .NET NativeAOT | Later improve startup/distribution of own engine | Does not restore original game code; qualify reflection/native dependencies first |

### Lessons from developments since 2024

1. N64Recomp translates MIPS/N64 code; XenonRecomp targets PowerPC/XEX. The
   success of these projects is no evidence that a Windows x86 game works
   automatically with the same tools. Frontend, ABI and platform runtime differ.
2. reccmp and LEGO Island show a usable Windows track: reconstruct source, compare
   functions against the original, then port platform parts. Matching accuracy and
   functional completeness remain separate quality measures.
3. isle-portable shows that original gameplay and replaceable rendering/input/audio
   are separate workstreams. Adopt that boundary; no unnecessary engine rewrite.
4. SDL 3.2 was released in January 2025, including a modern GPU API. This is a
   candidate for a future backend if the existing Veldrid stack demonstrably blocks
   progress, not a reason to rewrite a working parser/game core now.
5. Apple's announced general Rosetta support continues through macOS 27; after
   that, more limited support remains for certain older games. Therefore plan for
   genuine arm64-native dependencies and test them separately.
6. OpenTPW issue #31 reports that TGQ videos contain EA TQI payloads and can be
   read with FFmpeg/VLC. Verify this with the chosen assets before building a new
   video decoder; the report is not an already delivered video feature.

### Decisive recompilation experiment

Budget: at most 16 engineer-hours as a decision experiment, not a delivery
promise. Inventory the original PE/executable and imports without running the
installer or crack. Select one pure parser or simulation function with known
input/output. Compare Ghidra-assisted reconstruction and, only if demonstrably
supported, static lifting with an implementation in the existing C# core. Require
reproducible outputs, a complete runtime dependency list, and less expected total
effort. Without that evidence, no pivot to a new recompilation toolchain. Do not
remove or bypass copy protection yourself as part of this experiment; see
"Provenance of game rules" for the permitted static analysis.

### Provenance of game rules (decision October 9, 2026)

A game rule counts as original behavior only if it is traced to logic in an
original executable: for Theme Park World the PowerPC executable `SimTheme Park`
(Mac CD, November 2000, PEF, unencrypted) and its shared libraries; for Theme Park
Inc `Game.exe`. A rule that comes only from a manual, website, community source or
gameplay test remains `[APPROX]`, however plausible. Data files keep `[DATA]`
provenance: they prove values, not the rule that uses them.

The Theme Park Inc `Game.exe` on the CD is SafeDisc-encrypted. By decision of the
project owner, the decrypted no-CD `Game.exe` that comes with the same CD
(`WIN10FIX+NOCDFIX/noCD Crack/tpinc_nocd/Game.exe`, same section layout as the
original) may be used for static analysis. Basis: decompilation for
interoperability by a lawful user (Art. 6 Software Directive 2009/24/EC, Art. 45m
Auteurswet, Dutch Copyright Act). "Abandonware" is not a legal basis: copyright
lies with EA. Conditions:

- No executable, decrypted code, disassembly dumps or crack files in git, issues
  or artifacts; only own descriptions with hash and function/address references.
- Clean-room: rules are described in own words and reimplemented; no copied or
  mechanically translated original code.
- Only for interoperability of OpenTPW; OpenTPW does not distribute or require
  no-CD files, and players still need their own original copy.
- The Windows TPW executable (`TP.ICD`) does not fall under this as long as no
  comparable decision exists.

Evidence references get their own label, for example
`[BIN:STP-PPC:<function or address>]` and `[BIN:TPI-EXE:<function or address>]`,
with the SHA-256 of the analyzed executable in `RECOMPILATION-ASSESSMENT.md`. The
fidelity register (`tools/fidelity_register.py`) must still learn that label
before the first rule is marked this way.

### Evidence and determinism contract (requirements, not yet delivered)

`docs/REFERENCE-CORPUS.md` specifies the selected corpus and the trace fields.
Before acceptance of M3/M4 semantics, a bounded original behavior trace is
required, linked to executable/asset hashes, initial state, actions, observations
and comparison criteria fixed in advance. Static-analysis evidence and a runtime
oracle remain separate labels; without a runnable original, behavior remains
unverified.

M2 must record a fixed simulation tick, stable update/event order and a named RNG
algorithm with fully serialized state. Rendering/frame rate must not drive the
simulation. Canonical simulation state must be exactly replayable on all target
platforms; any numeric tolerances for comparison with the original must be
justified in advance per field and must not hide changed game rules. This contract
describes future acceptance, not the current prototype motion or save function.

### Runtime and toolchain qualification

M0 inventories versions, transitive native libraries, load path, OS/architecture,
license/distribution and test status for NAudio/Media Foundation/WaveOut, SDL2,
SPIR-V cross-compilation and ImGui/fonts. The existing Windows audio paths are not
a macOS audio backend. Choosing Metal does not prove that shaders, fonts, input,
audio and editor launch work. M1 qualifies all launch-critical paths; optional
paths may temporarily remain explicitly unsupported, but they block later final
acceptance. With Rosetta, both the process architecture and every loaded native
library must match. M7 requires genuine arm64 qualification without Rosetta.

Record SDK version, restore inputs and reproduction commands for M0. .NET 8 support
ends on November 10, 2026 according to Microsoft. Decision of the project owner
(October 9, 2026): move to .NET 10 (LTS until November 14, 2028), SDK 10.0.401 in
`global.json`; NuGet packages remain unchanged and are not part of this decision.
NativeAOT remains a separate, optional qualification.

## Execution phases and hard gates

### M0 — Reproducible base and inventory
- Record edition/asset manifest/hashes; identify missing files.
- Keep the exact edition label/patch unknown while evidence is missing; record
  locale and executable identity via `docs/RECOMPILATION-ASSESSMENT.md`.
- Ensure a clean .NET build, portable asset paths, and separation of unit and
  asset tests.
- Create a start command with explicit gamepad, logging and asset diagnostics.
- Register compiler/binary information for the decision experiment.
- Inventory the runtime dependencies and record SDK/restore inputs.
- Gate: a clean checkout builds; CPU tests pass; asset tests read real WAD data;
  missing assets give a clear error without fake success.
- Evidence package: revision/toolchain, selected manifest, named texture/model
  assertions and runtime matrix. Optional tests without assets are inconclusive,
  not asset evidence. The explicit `--validate-assets` gate must fail on missing
  required files, unreadable archives or invalid selected fixtures.

### M1 — First visible Mac park slice
- Use Metal on macOS; repair cross-compilation and backend requirements.
- Bootstrap with x86_64/Rosetta if necessary, but label this explicitly as
  temporary.
- Load a bounded Jungle sandbox with real terrain and attraction assets.
- Add camera controls, grid placement, footprint validation and start/stop.
- Keep any temporary model animation visibly separate from original VM fidelity.
- Gate: the app starts on this Mac; exactly one attraction is placeable; movement
  stops/starts; UI clicks do not place a second attraction; screenshots/logs and
  tests as evidence.
- Prove backend/process/native-library architecture and shader/font/editor/input
  launch paths. A bounded terrain/model and procedural motion are a prototype, not
  validated original MAP semantics, ride cycle or VM fidelity.

### M2 — Data model, map and persistence
- Develop MAP/TPWS schemas based on real files and targeted binary analysis.
- Introduce a deterministic simulation tick that works independently of rendering.
- Record tick frequency, update order, RNG algorithm/state and canonical replay
  fields per the evidence contract; verify save/reload including tick and RNG
  state.
- Create a versioned save format of its own with atomic writes and migration tests.
- Original saves first read-only import; writeback only after round-trip evidence.
- Gate: the same park after restart; seed/replay reproduce results; damaged files
  fail in a controlled way without save loss.

### M3 — One complete gameplay loop
- Build paths, entrance, queue, one attraction, shop/toilet and staff.
- Visitors: goals, path finding, queue, ride, need change and departure.
- Economy: costs, revenue, ticket price, staff and maintenance.
- Gate: 30 minutes of accelerated headless running with income/expenses and
  visitors; no stuck queues, unreachable goals or negative time progression.
- Invariants prove stability, not original economy/visitor semantics: accept those
  semantics only with linked original traces and comparison.

### M4 — Attractions, scripts and roller coasters
- Corpus of RSE files, opcode inventory, VM disassembly and golden tests.
- Execution budget, error diagnostics, object hierarchy and animation/event binding.
- Reuse the same VM for fixed attractions; no separate hardcoded hack per ride.
- Roller coaster construction, segments, terrain/footprint constraints, ride camera
  and ride assessment.
- Gate: every supported attraction goes through load/build/run/stop/delete; all
  opcodes used have tests; unknown opcodes are reported explicitly.
- This is subset acceptance only. Full M4 acceptance requires all mandatory
  attractions/roller coasters and required opcodes, reference traces and no
  unsupported requirements; record original VM fidelity separately from procedural
  animation.

### M5 — Full offline progression
- All four worlds, scenarios/goals, research, unlocks and advisor.
- Staff roles, maintenance/breakdowns, needs, shops and decoration effects.
- Gate: every scenario starts, goals are achievable, and progress survives
  save/load; the feature matrix has no open blockers for the agreed original
  edition.

### M6 — Audiovisual and UX parity
- Fonts, localization, menus, shortcuts, sound/music, videos and first-person.
- Reuse existing proven decoders where possible; no own codec without need.
- Measure rendering/resources and memory; improve hotspots only after profiling.
- Include optional, adjustable upscaling according to `docs/UPSCALING-DESIGN.md`:
  Native as default, first portable Linear/Nearest with fixed/custom render scale;
  scale only the 3D world, UI at output resolution and simulation untouched.
- Vendor/temporal upscalers and dynamic resolution are later optional experiments,
  not dependency approval or a blocker for native gameplay. Qualify offered
  baseline modes per backend on image, DPI/picking, fallback, resources and
  measured cost.
- Gate: reference captures and replay tests; no missing primary UI functions;
  audio/input/rendering keep working after resize, alt-tab and long sessions.

### M7 — Native and platform qualification
- Reproducible native arm64 builds for all dependencies; Rosetta not required.
- Windows/Linux backends and distribution packages; CI with CPU and asset gates.
- Three separate mandatory release gates: native macOS-arm64, native Windows and
  native Linux. For Windows/Linux, record the supported OS/CPU targets; verify
  appropriate process and native-library architectures on each target platform. A
  Wine/Rosetta/other compatibility run does not replace a native release gate.
- NativeAOT only retained with measurable startup/memory/distribution gains and
  green tests.
- Gate: macOS-arm64/Windows/Linux the same simulation replay; long sessions and
  save/load corpus without regressions; packages contain no original assets.
- All required matrix rules must be reference-verified with platform evidence;
  confirm the supported-runtime decision and the distribution/native-library
  inventory.
- Each native release gate requires package launch, rendering/input/audio, strict
  asset validation, the same canonical simulation replay, save/load and long-session
  tests. Smoke tests alone are insufficient; release acceptance remains open as long
  as one platform is unqualified. Native means an OS/CPU-appropriate runtime and
  dependencies, not mandatory NativeAOT; a supported .NET JIT runtime is allowed.

## Efficiency and way of working

- Critical path: assets/build → rendering → data model → simulation → fidelity.
- In parallel: parsers/corpus tests, platform layer, documentation; no shared write
  scope.
- Small patches per subsystem; reuse existing helpers first.
- No dependency added or upgraded without explicit permission.
- No generic ECS, multiplayer, complete renderer rewrite or AI visitor layer up front.
- For reverse engineering: keep source/evidence per unknown function and write
  golden tests; AI output never counts as evidence without runtime/reference
  verification.
- Track demo animation, compatibility execution, native rendering and complete
  gameplay as four different statuses in progress.
- Prioritize regressions and proven decoder/script knowledge above counts of
  generated lines.

## Risks and assessment

Most costs lie in missing simulation semantics and original formats, not in
compiling C# to native code. The full game is not a responsible hour/day
commitment. First measure M0/M1, then determine capacity and throughput per
subsystem. Main risks: version differences, partial VM/model parser, shader/native
interop, fidelity without a working original as oracle, and save compatibility. If
a gate fails, that phase remains open; it is not replaced by a screenshot-only demo.

## Primary sources for route choice

- https://github.com/OpenTPW/OpenTPW and issue https://github.com/OpenTPW/OpenTPW/issues/31
- https://github.com/N64Recomp/N64Recomp
- https://github.com/hedge-dev/XenonRecomp
- https://github.com/isledecomp/reccmp
- https://github.com/isledecomp/isle and https://github.com/isledecomp/isle-portable
- https://github.com/NationalSecurityAgency/ghidra
- https://github.com/libsdl-org/SDL/releases/tag/release-3.2.0
- https://developer.apple.com/documentation/apple-silicon/about-the-rosetta-translation-environment
- https://support.apple.com/en-us/102527 (Rosetta through macOS 27; limited from macOS 28)
- https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core (.NET 8: November 10, 2026)
- https://github.com/naudio/NAudio/tree/v2.2.1 (Windows audio, pinned generation)

Source claims are dated October 9, 2026. Issue #31 reports a North-American retail
`roll.tgq` sample; do not generalize this report to all videos/editions. Live
upstream documentation is no evidence for the locally pinned runtime versions.

## Progress at start

- [x] Fork and local clone.
- [x] Source inspection and scope confirmation.
- [x] ISO download and checksum; assets outside repository.
- [x] Local .NET 8 arm64 SDK; x64 bootstrap SDK is being checked.
- [ ] M0 build/asset diagnostics/tests.
- [ ] M1 visible Mac park slice.
- [ ] Recompilation decision experiment with original executable.
- [ ] M2–M7; not delivered and not to be reported as done.
