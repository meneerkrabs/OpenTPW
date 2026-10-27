## Report Python test failures before later CI checks (October 27, 2026)

A Windows CI job reported success even though its release-train tests logged six failures
and seven errors. Later passing Python commands overwrote the failed command's exit code
in the multi-command PowerShell step. That step now explicitly uses the supported Bash
shell's fail-fast behavior, keeping every existing command and platform in the matrix.

The workflow-shell tests now launch the absolute Bash path discovered by `shutil.which`
instead of a bare executable name, and failed assertions include captured stdout as well
as stderr. Windows selecting its WSL launcher before the Bash found on PATH is the
suspected launch cause, inferred from the code, runner image and failure signatures;
it has not been confirmed by a Windows runtime test.

Local Linux validation passed all 31 baseline release-train tests and all 33 updated tests,
with no skips. The two added mocked regressions check absolute-path dispatch and refusal
to fall back to a bare name when Bash is missing. A bare-name mutation failed the new
regression, and a synthetic Bash run preserved an early Python exit code 7 without running
a later command. Windows validation remains pending. These are CI and test-harness changes,
not game-functionality or fidelity claims.
