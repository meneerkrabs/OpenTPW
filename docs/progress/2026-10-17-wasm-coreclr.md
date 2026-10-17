## The browser game runs on CoreCLR WebAssembly in .NET 11, also with ReadyToRun, but still slower than Mono AOT, so Mono stays the default (October 17, 2026)

`dotnet publish -p:UseMonoRuntime=false` (.NET 11 RC2) produces a browser build that boots, reaches the lobby in 8.7 s,
opens Jungle and gives the same texture hash `1CC5AEB033691043` and simulation hash `0B45FAA2ED526C78` as the Mono builds.
The earlier spike's "no first frame in 600 s" was the measuring harness stuck at the Game Mode screen, not the runtime.

- **Plain publish is interpreter-only.** The RC2 SDK has no ReadyToRun step for browser-wasm (`PublishReadyToRun` stops with
  NETSDK1095; the SDK integration, dotnet/runtime#133378, is milestone 12.0.0 and not on release/11.0). The .NET 11 runtime
  page's WebCIL V1 / `WasmObjectWriter` remark is about crossgen2 and the loader, not an SDK switch.
- **crossgen2 itself works.** The crossgen2 package restored by the workload targets `browser`/`wasm`; `tools/web-coreclr/r2r.mjs`
  compiles the trimmed IL of a finished publish as one version bubble and patches the boot config (`tableSize`, `payloadSize`,
  hash). It boots and the hashes match. `--opt-cross-module:*` (the SDK's choice) crashes crossgen2 RC2, composite is rejected.
- **Measured** (medians of 3; Mono AOT / Mono interpreter / CoreCLR interpreter / CoreCLR + R2R): Jungle ready 10.2 / 13.1 / 98.4 / 21.4 s,
  first frame 2.4 / 3.3 / 8.7 / 4.5 s, Jungle steady 60 fps at 9.8 % busy / 60 fps at 23.8 % / 7.9 fps at 100 % / 38 fps at 97 %,
  texture decode 0.56 / 2.4 / 17.0 / 1.6 s, 3000-tick run 0.20 / 0.36 / 3.1 / 1.4 s, bundle 19.9 / 13.3 / 13.8 / 29.8 MB Brotli.
  Table in docs/BROWSER-PERFORMANCE.md.
- **Decision.** Mono AOT stays the published build. CoreCLR (and the R2R step) are documented opt-ins (docs/WEB.md); delete the Web
  project's `obj` and `bin` when switching runtime. Revisit with .NET 12.
