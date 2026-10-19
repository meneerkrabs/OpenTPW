## Browser and desktop compute the same park: the determinism gate (October 19, 2026)

Co-op phase C1b. Lockstep with browsers that join and host needs the WebAssembly build to compute the desktop's simulation bits.

- **Measured first.** The asset-free tests that pin simulation results now also run inside the browser build: 632 tests covering the
  economy, guests, pools, umbrellas, winter, weather, smarter guests, script mods, cheats, coasters, tours, bump and track rides,
  staff and loans. They are compiled into the page with `-p:OpenTpwDeterminismPins=true` and run in headless, muted Chrome
  (`tools/web-determinism`). The new `Browser determinism` workflow runs them on every pull request for Mono AOT and the interpreter.
  - Before any change, every pin already passed in the browser, and a new with-data digest (`--determinism-digest` against
    tools/web-perf's `determinism` scenario) was identical in all four themes. The digest covers the fixed park run, the run with
    staff, two years of weather, and the M4 scenario's 659 to 692 state hashes of rides, shops and object scripts.
  - The C libraries behind `System.Math` still disagree: Apple's libm and the browser's musl differ in the last bit for hundreds to
    thousands of 20,000 probe inputs per function.
- **Removed the dependency.** `DetMath`/`DetMathF` port FreeBSD msun's sin, cos, atan, atan2, acos, pow and log2 to C# with a fixed
  order of IEEE operations. They give the same bits everywhere and are bit-identical to the browser's musl for the trigonometric
  functions.
  - About 30 simulation call sites use them: loans, coaster geometry, rating and crossing, tours, bump rides, walk and guest
    headings, and the sideshow tail. They are labelled `[EXT:deterministic-math]` (the user's decision).
  - The start-up sine and arctangent tables, built with DetMath too, are unchanged entry for entry and keep their binary-evidence
    labels.
  - No pin moved. New pins: DetMath's results, the lookup tables, and the correctly rounded square root and fused multiply-add.
- **Guarded.** A second source scan bans the platform's elementary functions, rotation builders, `Guid.NewGuid` and `Parallel` in
  every simulation folder, with a listed presentation allowlist and a `[DET:presentation]` line marker
  ([DETERMINISM.md](../DETERMINISM.md), "Browser and native").
