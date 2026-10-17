## The browser game runs on CoreCLR WebAssembly in .NET 11, but only about 10 to 15 times slower than Mono AOT, so Mono stays the default (October 17, 2026)

`dotnet publish -p:UseMonoRuntime=false` (.NET 11 RC2) produces a browser build that boots, reaches the lobby in 8.7 s,
opens Jungle and gives the same texture hash `1CC5AEB033691043` and simulation hash `0B45FAA2ED526C78` as the Mono builds.
The earlier spike's "no first frame in 600 s" was the measuring harness stuck at the Game Mode screen, not the runtime.

- **Why it is slow.** CoreCLR on WebAssembly executes everything in its interpreter in .NET 11: ReadyToRun for browser-wasm
  (dotnet/runtime#133378) is a .NET 12 change, the RC2 SDK rejects `PublishReadyToRun` (NETSDK1095) and the runtime pack has
  no prebuilt framework images. It is also not yet tuned (dotnet/runtime#124218).
- **Measured** (medians of 3, Mono AOT / Mono interpreter / CoreCLR): Jungle ready 10.2 / 13.1 / 98.4 s, first frame 2.4 / 3.3 / 8.7 s,
  Jungle steady 60 fps at 9.8 % busy / 60 fps at 23.8 % busy / 7.9 fps at 100 % busy, texture decode 0.56 / 2.4 / 17.0 s,
  3000-tick run 0.20 / 0.36 / 3.1 s, bundle 19.9 / 13.3 / 13.8 MB Brotli. Table in docs/BROWSER-PERFORMANCE.md.
- **Decision.** Mono AOT stays the published build. CoreCLR is a documented opt-in (docs/WEB.md); delete the Web project's
  `obj` and `bin` when switching runtime, or the link step fails with a duplicate symbol. Revisit with .NET 12.
