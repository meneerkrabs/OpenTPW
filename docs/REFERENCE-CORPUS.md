# Reference corpus and behavior evidence

Requirements and limited container evidence as of October 9, 2026.
No original executable has been run or trace verified by this document change.
Keep copyrighted assets, saves and captures out of git/public packages. Store
private corpus locations separately; public regression fixtures should be synthetic.

## Identity and provenance

- Exact selected edition label, locale and patch: **unknown pending evidence**.
- Executable SHA-256/PE observations: see
  [RECOMPILATION-ASSESSMENT.md](RECOMPILATION-ASSESSMENT.md), which records locally
  verified `tp.exe` and `TP.ICD` hashes. Do not reinterpret linker fields as proof
  of compiler/ABI, executable safety, edition, or successful reference execution.
- Record ISO identity and extracted-file relative path, size and hash. A checksum
  matching supplied metadata proves identity agreement, not semantic completeness.
- Each fixture records source edition evidence, required/optional status, consumer,
  expected decoded structure/content, provenance and a private retrieval location.
- Unidentified version differences remain open; do not infer North-American retail
  identity from a video issue or treat its sample as the selected corpus.

## Planned fixture registry

These are bounded selection tasks, not invented asset paths or completed tests.
Populate exact paths/hashes and assertions from real inventory before closing M0.

| Fixture ID | Required selection | Evidence still needed |
| --- | --- | --- |
| ASSET-TEXTURE-01 | One real SAM/WAD texture | Archive/member hashes, dimensions/format, known decoded content |
| ASSET-RIDE-01 | One real ride model and associated settings/script | Member hashes, topology/hierarchy/settings, opcode inventory |
| ASSET-TERRAIN-01 | One real Jungle terrain/model asset | Actual path/hash and geometry; no implied MAP reconstruction |
| SAVE-REFERENCE-01 | One original read-only save/map sample | Edition, known initial park state, import assertions; M2 requirement |
| TRACE-SEMANTICS-01 | One bounded original parser/simulation behavior | Runtime oracle or explicitly static-only evidence; comparison below |

## Test modes and strict asset gate

### Recorded read-only container fixture

`SAVE-CONTAINER-TPWI-01`: `Data/levels/jungle/Easymode.TPWI`, 38,479 bytes.
Container SHA-256:
`6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a`.
Decoded payload: 1,608,309 bytes; SHA-256:
`a3c9a28252c37ad49a8eb78e4a0c5e1d5229d01548fa35801db67015d2589173`.
Consumer: `OriginalSaveAssetTests` / `SaveReader` / `--inspect-save`. Expected header:
initial value 400, raw type `0x19220100`, version 133, offline 0, BILZ at `0x60d`,
chunk size 36,930 including header, completed zlib stream without trailing data.
Provenance: supplied ISO extraction; edition/locale/patch unknown. Optional private
fixture under `OPENTPW_GAME_PATH`; no asset bytes stored in this repository.
Scope: decoding identity only, not known park state or original runtime behavior.
`SAVE-REFERENCE-01` remains open. Details: [SAVE-CONTAINER.md](SAVE-CONTAINER.md).

Read-only `scape.omp` inventory, semantics unverified (all start with `OBJ_`):

| Relative path within Data | Bytes | SHA-256 |
| --- | --- | --- |
| `levels/jungle/scape.omp` | 590 | `87387a9baa4ae69b5bd2c1da32941969c8f4042f59557a22005d195a17e56809` |
| `levels/fantasy/scape.omp` | 411 | `57224eaaf1f2894673e9e893e08d6972e02e48ec530e8e412ba769e6fd60bdb8` |
| `levels/hallow/scape.omp` | 413 | `554b9252aa67c85041ac5fae5482ebbb8e1a7192ca8e9e81c23bca2fbd139949` |
| `levels/space/scape.omp` | 409 | `e2f6a2ec89a43b14159386219e2026c915c48649a483faaad8fca9ce43d28bbb` |

These hashes inventory original files; they do not establish a tile/grid importer.

### Required test modes

BF4 private corpus: see [BF4-FONTS.md](BF4-FONTS.md) for the 33 English fonts,
8,217 entries, three pinned file/sample identities, metrics and limitations.
These are CPU decoder assertions; original text rendering remains unverified.

- Asset-free tests exercise synthetic records, truncation, invalid offsets/counts,
  allocation/decompression bounds, traversal and case-sensitive resolution.
- Optional real-asset tests without fixtures report inconclusive/skipped. Their
  absence cannot be counted as successful asset validation or milestone evidence.
- Explicit `--validate-assets` is required to fail missing required files/members,
  malformed archives and invalid selected fixtures. Report failing fixture/path and
  remediation; never silently downgrade this invocation to an optional test.
- Record the actual invocation, exit result, revision/toolchain, corpus manifest
  hash and content assertions. The flag's required behavior is not an implementation
  claim. CI with no licensed assets may run asset-free tests only; strict-corpus
  evidence needs a separately provisioned private job or recorded local run.

## Original behavior trace gate

Before accepting M3/M4 simulation/script semantics, compare bounded original
runtime observations against the rewrite. Invariant tests and repeatable invented
behavior are insufficient. Required trace record:

1. Trace ID, source/rewrite revision, executable and relevant asset/save hashes.
2. Edition uncertainty, reference OS/runtime/compatibility setup and capture method.
3. Initial state/save, seed if observable, tick/time basis and controlled conditions.
4. Ordered actions/events with timestamps; inputs and observed outputs/state changes.
5. Expected outputs, numeric units, exact-match fields and justified per-field
   tolerances declared before comparison; note unobservable internal state.
6. Reproduction procedure, repeated observations/variance, actual comparison result
   and remaining mismatches. Do not assume the original exposes a controllable RNG.

Static disassembly/reconstruction, community claims, runtime traces and synthetic
regressions have separate evidence labels. If a safe runnable original is unavailable,
retain static evidence but leave runtime fidelity unverified. Do not remove or bypass
copy protection yourself to satisfy this gate; the only exception is static analysis of
the decrypted Theme Park Inc executable shipped on its CD, under the conditions in
COMPLETION-PLAN.md ("Provenance of game rules"). A game rule counts as original only
when it is traced to logic in an original executable. One verified trace does not verify all rides.

## Proposed deterministic simulation contract

These are M2 design/test requirements, **not implemented guarantees**:

- Choose and document one fixed tick frequency before M2 acceptance. Rendering and
  frame timing do not change simulation update counts, order or rules.
- Define stable entity IDs, update/event ordering and tick-assigned player inputs;
  serialize pending events and any state needed to resume that ordering.
- Name/version the RNG algorithm, seed mapping and streams. Persist full RNG state
  and tick index; avoid wall-clock/randomized runtime ordering in simulation.
- Define canonical replay serialization/state hashes including entities, economy,
  events and RNG. Require exact equality across macOS/Windows/Linux and render
  framerates at matched ticks; document numeric representation and rounding rules.
- Declare any numeric tolerance for comparisons with original observations before
  testing, separately from exact rewrite replay. Visual timing tolerances do not
  excuse changed economic rules, queue ordering or original-script semantics.
- Test save/reload continuation against uninterrupted replay, varied rendering,
  and controlled input/event order. Prototype elapsed-time motion is not this contract.

## Per-platform release evidence

Maintain separate evidence packets for native macOS-arm64, native Windows and
native Linux. These are all mandatory M7 release gates, not alternatives. Each
packet records OS/CPU targets, package/revision identity, process/native-library
architectures, launch/rendering/input/audio results, strict asset-validation result,
canonical replay comparison, save/load corpus and long-session results. Smoke
tests alone cannot qualify release; compatibility observations remain separate.
No platform gate is claimed passed here. NativeAOT remains optional.

## Official source boundaries

- [reccmp](https://github.com/isledecomp/reccmp): matching verification is not an
  automatic Windows runtime replacement; verify selected compiler/ABI suitability.
- [OpenTPW issue #31](https://github.com/OpenTPW/OpenTPW/issues/31): a report about
  North-American retail `roll.tgq`, not proof for every movie/edition or a delivered decoder.
- [Microsoft NativeAOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/):
  deployment of managed rewrite code is distinct from original-binary recompilation.

The corpus/matrix remain open until real evidence is recorded. Never promote missing,
approximated or unsupported required behavior to full-game completion.
