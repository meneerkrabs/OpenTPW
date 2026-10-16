## Faster pull-request CI (October 16, 2026)

A pull request's CI took about eight minutes, nearly all of it the server image's browser build: the AOT compile of the
WebAssembly page ran 363 of the job's 404 seconds, while the three build-and-test jobs finish in two to four minutes. A pull
request now builds the image with `--build-arg WEB_AOT=false` (the interpreter page, same sources); main and the release tags,
whose images are published, keep the AOT build, so an AOT-only failure shows on main. Larger GitHub runners would not be free:
the unlimited minutes of a public repository cover the standard runners only.
