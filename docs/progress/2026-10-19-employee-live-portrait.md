## Employee portraits follow current world output (October 19, 2026)

- The employee portrait prefers the selected member's successfully prepared world slot, step and heading, with camera yaw fixed at zero. Work, strike, research and performance phases no longer default to the hire walk when live output is available.
- Reads do not tick/restart/select animation, consume movement or retain GPU handles. Exact member/ThingId, current level/renderer, world-pass and atlas-generation checks reject stale output, including a renderer omitted from a world pass.
- The pre-existing qualified hire-loop fallback and its clock/key updates remain unchanged, as does the hire screen. This is not native independent-clone or source-loss lifecycle parity; UI-092 remains open, with native VM/flag history, depth/occlusion and real-data visuals still qualified. UI-082 still owns sizing.
- Added synthetic C# coverage of the actual CPU world preparation and HUD callback, including repeated reads, selection/reopen/rename/modal, mirroring, source eligibility, skipped passes and resource lifetime. Local checks pass: six Python source-integration checks, fidelity/progress/scope/release-train tooling, website-document tests after generating the fidelity register, and 21 Python evidence suites (1,537 tests reported, 595 skipped without private fixtures). The local environment lacks the pinned .NET SDK, so focused/full C# execution is deferred to exact-head CI; no GPU or original-media visual comparison was run.

See [employee portrait behavior and verification limits](../EMPLOYEE-PORTRAITS.md).
