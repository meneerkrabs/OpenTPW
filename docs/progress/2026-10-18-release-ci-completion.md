## Release train wakes after main CI (October 18, 2026)

Build and release tooling only; no game behaviour changes:

- The release train wakes after successful push CI on `main`, with the existing hourly schedule as a best-effort fallback. A completion event never substitutes for checking all push CI runs on the exact candidate SHA; PR/fork/manual/tag events cannot trigger publication through this path.
- The final plan now refetches and checks the latest `main` as well as the tags. Changed plans abort dispatch. Release runs waiting for approval or concurrency also block a new train, and run queries include all pages.
- Retrying an unfinished release requires successful CI on its tagged commit, rather than accepting the current main's results. Existing version concurrency and idempotent publication remain in place; manual dry runs still dispatch nothing.
- Planner, mocked GitHub gathering, and the workflow's real dispatch shell are tested for failed/pending CI, duplicate wakes, changed versions and moving main. No live release is needed for these tests.
