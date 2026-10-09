# Bounded cleanup: native slice

Scope: ShaderCompiler.cs, SandboxSmokeTest.cs, FixedStepClock.cs and their
regressions. Preserve the 122-test behavior lock and native GPU smoke behavior.

1. Remove redundant PNG write/copy in SandboxSmokeTest; write once to ignored
   artifacts. Rename the capture helper to reflect that it captures a frame,
   not exclusively terrain. Keep save/load temporary isolation unchanged.
2. Keep the unified SPIR-V compilation and test seam; no new layers/dependencies.
3. Re-run native tests, fresh RID builds and published Mac smoke. No unrelated
   Material dead-code cleanup or whole-repository formatting.

Review limitations: native subagent authentication is unavailable; no independent
architect signoff. Full completion gates remain open regardless of this slice.
