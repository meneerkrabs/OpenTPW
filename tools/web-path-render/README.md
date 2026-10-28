# Synthetic real-path framebuffer diagnostic (default off)

This diagnostic is not a mock renderer. It calls the real `OriginalTerrain` constructor,
texture-pack switch/decoder/GPU reload, terrain atlas, `Renderer.Frame`, `Level.Render`,
pre-snapshot `PrepareRender`, meshes, materials and browser shaders. Its only terrain,
MAP/LND, WCT and same-size red/blue pack images are generated in memory; no game directory
or copied game/save corpus is accepted.

`Program.PathRenderOracle.cs` is excluded from ordinary builds. The separate
`OpenTpwPathRenderOracle=true` property includes only `SyntheticLnd.cs` from the tests,
**not** `OpenTpwDeterminismPins` or its corpus/framework. The minimal page never starts the
game loop, imports user files, accesses storage, downloads assets or runs the online client.
It is loopback-only. Unexpected media/audio/page requests throw.

## Build/artifact route — requires authorization, not run by this change

The separate `Synthetic path diagnostic artifact` workflow has a default-false manual
`build_artifact` input. Its independent job uses the existing runner, pinned
.NET SDK and pinned actions with `contents: read`. It does **not** run a browser and does
not alter the ordinary AOT/interpreter gates.

The initial artifact is deliberately **Mono interpreter-only, untrimmed, WebCIL-disabled,
with no native app build**. This lets the audit inspect actual final PE DLLs and match
untouched dependency DLL/runtime WASM/ICU/JS bytes to restored SDK/NuGet files. It is not
AOT/shipped-packaging acceptance. The normal published app is unaffected.

The upload step runs only after all checks succeed:

1. Evaluated compiler items are production C# plus the one pinned synthetic helper;
   evaluated resources are exactly four pinned textual shaders; evaluated content is
   exactly the diagnostic HTML/JS and the existing WebGL/text bridges.
2. `tools/PathOracleAudit` inspects final published PE metadata without executing it.
   `OpenTPW.Web` must have exactly those four resources; Veldrid/Online none. Test/extra
   OpenTPW assemblies, linked resources/modules, malformed PE and links are rejected.
3. `stage.py` parses SDK boot JSON without executing generated JS. Only an explicit local
   boot closure is accepted. VFS, game content, settings, module initializers, lazy or
   satellite assets, debug resources, WebCIL/native app images and unknown SDK shapes fail.
4. Each nonproject DLL, runtime WASM, ICU and runtime JS must match exact installed
   SDK/NuGet bytes. Inline `dotnet.js` must equal the SDK loader template with only the
   parsed boot JSON marker substitution and empty bundler imports.
5. Four static outputs must match their pinned source bytes. Only the verified closure,
   driver files and metadata are copied to a **fresh** stage. Raw publish directories,
   media, save/test corpora, compressed duplicates, symbols, source maps, bin/obj and
   build logs are never uploaded. Unknown raw publish files fail closed.

See `artifact-allowlist.json` and the workflow's explicit commands. Actual SDK item and
boot formats remain a first-build compatibility gate: inspect a failure, do not weaken
isolation or upload raw output to work around it. This source-only candidate has not
compiled or generated a runnable bundle yet.

The staged artifact contains:
- `wwwroot/`: exactly four static files plus audited boot/runtime closure;
- `tools/web-path-render/{run.mjs,assertions.mjs,README.md,artifact-allowlist.json}`;
- `tools/web-perf/lib.mjs` (existing shared browser driver);
- complete `source-tree.json` (names/modes/Git hashes only), `compiled-sources.json`,
  `pe-audit.json`, `provenance.json`, `artifact-manifest.json`, `VERIFIED.txt`.

## Later authorized render run

After independently validating artifact provenance and hashes, use a clean cloud browser
with no game data. From the extracted artifact root:

```sh
node --experimental-websocket tools/web-path-render/run.mjs wwwroot \
  --chrome /usr/bin/google-chrome --source-sha <exact-built-commit> --runtime interpreter \
  --report /tmp/path-render-interpreter.json
```

The runner makes ten ordered draws. A/B/A equal-size pack replacements must change native
GPU identity and the procedural **and** static path pixels. The first suspended-camera
frame must already show newly built paths and ground; after a path edit, the old cell must
be ground and the new cell visible. The draw, 3x3 readPixels samples and PNG capture happen
in the same JS task, with no warm-up or hidden settling frame. The report pins served
bundle bytes and records browser/GL identity. Inspect actual PNGs too.

The camera mode is real `FreeWalkCameraMode`; its pose is fixed overhead for repeatable
sampling, not a gameplay camera-control test. A private OriginalPark constructor and
render-only Level fixture avoid gameplay setup but do not replace affected code.

## Verification limits and negative controls

Local `node --test tools/web-path-render/assertions.test.mjs` checks fabricated observation
records only. `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/web-path-render
-p test_stage.py` checks parser/staging rules on synthetic fake DLL/report records only.
Neither proves C# compilation, PE inspection or rendering. The optional workflow builds and exercises the actual PE inspector against nine generated
assembly cases (one exact positive, eight rejection cases) before its first artifact can
be uploaded. These SDK-dependent cases have not run in this source-only preparation.

Two later, local-only real-render mutation builds must demonstrate sensitivity:
- move `PrepareRender` from `Level.Render` into `OriginalTerrain.OnRender`: first suspended
  frame must fail while the next settles;
- remove the generation comparison in `ProceduralCellArt.Same`: equal-size B replacement
  must fail inside the procedural area while its outside static path changes.

Never publish those mutations or relax assertions just to get green. A passing interpreter
run does not prove AOT, native Vulkan/Metal/D3D, physical GPU drivers (software WebGL must be
labeled), ordinary gameplay UI/controls, real media/maps/packs, cancellation/error/offscreen
flows, alpha/rotation or all path types. Keep the separate real-render verification gap
open until actual positive and negative rendering evidence exists.
