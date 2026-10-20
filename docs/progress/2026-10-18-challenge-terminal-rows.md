## Localized challenge terminal rows and bounded UI timers (October 18, 2026)

Success/failure captions now use all 66 recovered numeric records and the immutable localized payload.
Distinct terminal instances retain read/closed rows, cancel lifetime on first read, and expire unread
on an injected unscaled 30-second UI clock. Expiry/delete retain hidden occupancy for a 500 ms delayed
release; exact cross-tag replacement remains synchronous. Suspend/resume restarts active origins without
reviving disabled timers, and delayed release stays independent of suspension.

The [terminal contract](../reverse/CHALLENGE-TERMINALS.md) names the host pause/screen mapping and the
remaining queue, focus, wrap, persistence and generic-capacity limits. Offers and their repost behavior,
existing progress refresh, scoring and saves are unchanged. ECON-058/UI-025 remain open, and UI particles
90/91 remain omitted under PTCL-908. No original assets, strings or private evidence are included.

Thirty new deterministic synthetic regressions cover localization, duplicate instances, queue/read races,
full-tube replacement, hidden hit-testing, clock/resume boundaries, follow-up ordering, read-only guards
and timer pumping through the full-screen finale's captured-input return.

Local validation passed: fidelity/progress checks, all 108 tooling tests, committed-diff whitespace checks,
and the evidence aggregate (21 Python suites, 1,537 tests, 595 skipped; the review lane has seven expected
failures). C# execution was attempted but blocked because `dotnet` is not installed. These C# regressions
still require SDK-enabled CI; no runtime visual validation is claimed.
