# Rules for GLM grunt-work runs

You are working on OpenTPW, an offline reimplementation of Theme Park World in C# (.NET 10).
This run is unattended on a GitHub runner without game data. Follow these rules exactly.

## Scope

- Do only the task you were given. Do not refactor, rename or "improve" anything else.
- Never edit `.github/`, `docs/FIDELITY-REGISTER.md` by hand, `source/OpenTPW/Client/M3Gate*`,
  `source/OpenTPW/Client/M4Gate*` or any `tools/ppc-analysis/lanes/review/` file unless the task says so.
- If production behaviour looks wrong, do not fix it: write the test for the current behaviour
  and list the doubt in `GLM-REPORT.md` at the repository root.
- No original game assets, binaries or disassembly in git. No new NuGet or pip dependencies.

## Code style

- Repository files are English.
- C# files keep their existing line endings (many are CRLF, many LF); new files follow their
  neighbours. Tabs for indentation, spaces inside parentheses: `Foo( a, b )`.
- Python paths that end up in output use `Path.as_posix()`.
- Every approximation of original behaviour carries `[APPROX:AREA-NNN]` and is registered;
  run `python3 tools/fidelity_register.py --write` then `--check` when you add one.

## Checks before you finish (all must pass)

```
python3 tools/fidelity_register.py --check
python3 -I -B tools/ppc-analysis/run_evidence_checks.py
dotnet build source/OpenTPW.sln --configuration Release --nologo
dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --configuration Release --no-build --nologo
git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check
```

Tests that need game data skip on this runner; that is expected. Do not delete, skip or weaken
an existing test to make a check pass.

## Commits

Commit your work in small commits. Message: an intent line saying why, a blank line, a short
narrative, then `Tested:` and `Not-tested:` lines, and finally
`Co-Authored-By: GLM 5.3 <noreply@z.ai>`.

Write `GLM-REPORT.md` at the repository root with: what you did, the check results, every doubt,
and anything you could not finish. The workflow keeps it out of the branch and publishes it as the run's report.
