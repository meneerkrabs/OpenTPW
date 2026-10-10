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

Detailed missing/partial format work is tracked in [FORMAT-BACKLOG.md](FORMAT-BACKLOG.md).
BF4 decoding and strings are tested in English, Danish, Dutch, French, German
and Swedish. Original-style lobby/HUD/options render on Metal with glyph readback;
positions and several behaviors remain approximated ([UI.md](UI.md)). Original
visual fidelity and D3D11/Vulkan execution remain unverified.
MD2, MAP, RSE, TPWI, LIP, MTR and TGQ have bounded readers (FORMAT-BACKLOG.md).
All 274 original objects and optional bonus objects enter the catalog; guests use
rides/shops/sideshows/toilets through the visitor bridge. The economy and HUD share
purchase/sale ownership. Missing vertex animation, sounds/particles and coaster,
kart and tour controllers remain explicit gaps ([OBJECTS.md](OBJECTS.md)).

The verified [Patch 2](PATCH-2.md) data copy is a separate reference baseline;
28 physical Data differences and 24 WAD member differences are known. Static
[Mac timer evidence](reverse/FINDINGS.md) does not prove gameplay tick rates.
[The fidelity register](FIDELITY-REGISTER.md) contains 135 unresolved original-area
IDs and six online-extension uncertainties; its CI check covers explicit tags only.
Online file sharing/server work is an extension ([ONLINE.md](ONLINE.md)); shared
edited layouts and exact original service behavior remain unimplemented/unverified.

Compatibility slice ([COMPATIBILITY.md](COMPATIBILITY.md)): in-world TrueType sign text
from `fonts.wad` (`partial`: gate demo in `--load-original-level`; ride, shop and sideshow models draw
their name on `sign1`/`sign2` in the park and in the buy-window preview; board images and
sign layout values are tagged approximations), `--cd-data` media
fallback (`implemented`, macOS headless/native verified), original detail presets
(`partial`: filtering/mipmaps applied, most options lack a renderer feature), Original /
Recommended / Custom fix profile with save-metadata serialization (`implemented`; save
integration owned by the economy slice).

Optional presentation extension (requested October 9, 2026): configurable world
upscaling, design in [UPSCALING-DESIGN.md](UPSCALING-DESIGN.md), together with
high/arbitrary resolutions, HiDPI drawables, borderless/exclusive fullscreen and an
integer BF4 UI scale. Implementation: `partial` (M6-U1 Native/Linear/Nearest with
presets/custom scale; vendor/temporal methods and dynamic resolution deferred);
macOS: `partial` (Metal smoke at 1280x720, 1920x1080 borderless, 2560x1440 via the
test pixel scale and 50% Linear/Nearest; real Retina hardware and exclusive fullscreen
unverified); Windows/Linux: `unverified` (not run); original VM: `not-applicable`
(presentation only). Native remains the default and fidelity baseline. Offered
portable modes require platform qualification; optional vendor/temporal experiments
are not mandatory original-game requirements or permission to add dependencies.

Current implementation evidence is in [PROGRESS.md](PROGRESS.md). The native
Mac sandbox, 60 Hz prototype clock and three RID publish builds are partial
evidence only; they do not satisfy the aggregate offline requirements below.

The selected PC Data baseline, six language overlays and the verified English
EuroAmer Patch 2 copy have independent evidence. Exact runnable original edition
and executable behavior remain unqualified; a successful data patch does not
qualify that runtime. Executable hashes and PE observations are recorded in
[RECOMPILATION-ASSESSMENT.md](RECOMPILATION-ASSESSMENT.md).
Selected fixtures/traces belong in [REFERENCE-CORPUS.md](REFERENCE-CORPUS.md).
Expand aggregate rows into every required ride, opcode, world and scenario once
the selected edition inventory is evidenced; aggregate rows cannot conceal gaps.

## Required offline coverage

Rows without complete supporting evidence remain unassessed. Prototype passes
and approximation inventories below do not qualify the aggregate release gates.

| Required area | Gate | Implementation | macOS | Windows | Linux | Original VM | Evidence / remaining requirement |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Asset intake, parsers and decoded content | M0 | unassessed | unverified | unverified | unverified | unverified | Named hashed corpus and strict validation |
| Rendering, terrain, camera and grid placement | M1/M2 | unassessed | unverified | unverified | unverified | unverified | Separate prototype geometry from original MAP semantics |
| Park map, paths, entrances and construction rules | M2/M3 | unassessed | unverified | unverified | unverified | unverified | Original constraints and reference traces |
| Fixed-tick simulation and RNG/replay | M2 | unassessed | unverified | unverified | unverified | unverified | Proposed contract; no determinism claim |
| Versioned saves and original read-only import | M2 | unassessed | unverified | unverified | unverified | unverified | Full state, safe writes, import evidence |
| Visitors, needs, routing and queues | M3 | approximated | prototype-pass | unverified | unverified | approximated | [GUESTS.md](GUESTS.md): sprites/.sam/strings original, rates and formulas approximated; RSE visitor opcodes inferred from the corpus. macOS: native smoke in all four themes (Metal, arm64). Needs original-behavior comparisons |
| Economy, shops, toilets and staffing | M3/M5 | approximated | prototype-pass | unverified | unverified | not-applicable | `docs/ECONOMY.md`: settings-driven ledger, prices, loans, bankruptcy, staff wages/training, versioned park saves; formulas without original reference are labelled approximations; staff behaviour and guest spending wired to the guests slice. Evidence: `ParkEconomyTests`, `ParkEconomyOriginalDataTests`, native `--load-original-level` smoke (all four themes), macOS arm64 |
| Every required fixed ride and original scripts | M4 | approximated | prototype-pass | unverified | unverified | approximated | [OBJECTS.md](OBJECTS.md): original catalog/scripts/models, guest/economy links and all-theme Metal smoke; vertex animation, effects, controllers and original traces remain incomplete |
| Coaster construction, ride behavior and rating | M4 | unassessed | unverified | unverified | unverified | unverified | All required types and constraints |
| Four worlds and every offline scenario/unlock | M5 | unassessed | unverified | unverified | unverified | unverified | Per-world/scenario executable acceptance |
| Research, adviser, breakdowns and decoration effects | M5 | unassessed | unverified | unverified | unverified | unverified | Original progression/event evidence; research, wear/repair, challenges and golden tickets are approximated in `docs/ECONOMY.md` (adviser and decoration effects missing) |
| UI, localization, fonts and shortcuts | M6 | approximated | prototype-pass | unverified | unverified | not-applicable | Original-style front end/HUD/options from ui.wad/lobby.wad and string tables, six languages, Metal readback smoke ([UI.md](UI.md)); original positions of code-placed elements, profiles/online, reference captures pending |
| Audio, music, video and first-person/ride cameras | M6 | unassessed | unverified | unverified | unverified | unverified | Decoder/runtime evidence and reference comparisons |
| Native dependencies, packaging and long sessions | M7 | unassessed | unverified | unverified | unverified | not-applicable | Platform qualification, not VM semantics |

## Runtime qualification checklist

Required M0 inventory; launch-critical paths must be evidenced before M1 acceptance.
Record exact package/native versions, transitives, binary architecture/load path,
license/distribution constraints and actual results. No dependency change is implied.

| Runtime surface | Known risk / required evidence | macOS | Windows | Linux |
| --- | --- | --- | --- | --- |
| NAudio/Media Foundation/WaveOut | Existing Windows-audio path; explicit optional failure versus later portable audio | unverified | unverified | unverified |
| SDL2 window/input | Native loading, window creation, resize, focus and UI-owned input | unverified | unverified | prototype-pass (x64, Xvfb + llvmpipe: sandbox and front-end smoke tests pass with original Mac-edition data, including scripted mouse/keyboard navigation and window/fullscreen changes; real input devices and desktops unverified) |
| SPIR-V/shader translation | Native compiler architecture, backend-specific shader output and real render | unverified | unverified | prototype-pass (x64: native shader tests; GPU readback of original models, animations and BF4 text in the smoke tests) |
| ImGui/fonts/editor | Native binding/font load, editor toggle and launch without unsupported-path crash | unverified | unverified | unverified |
| Process and all native transitives | Consistent architecture; Rosetta only interim; final macOS arm64 without Rosetta | unverified | unverified | prototype-pass (linux-x64 self-contained publish starts; needs system libSDL2/libvulkan); blocked for linux-arm64 (no libveldrid-spirv/libcimgui in the packages) |
| .NET toolchain | Pinned reproduction inputs and supported-runtime decision before release | unverified | unverified | prototype-pass (x64: .NET 10.0.401 build, full test suite and front-end smoke test) |

Runtime decision (October 9, 2026, by the project owner): .NET 10 (LTS, supported until
November 14, 2028) replaces .NET 8, whose support ends November 10, 2026 according to
[Microsoft's support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
NuGet package versions are unchanged by this switch.
Runtime Windows-audio context: [NAudio v2.2.1](https://github.com/naudio/NAudio/tree/v2.2.1).
