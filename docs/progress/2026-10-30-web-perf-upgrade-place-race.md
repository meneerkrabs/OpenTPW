## Web performance harness test race (October 30, 2026)

- **Upgrade refusal test no longer races the freed place.** The harness test for
  bounded upgrade refusals runs with one forwarding place. The local server frees
  an upgrade's place only once its own end of the socket has closed, which can come
  just after the client has read the whole answer. On a slow runner the next upgrade
  sometimes arrived first and was refused with 503 (no place left) instead of the
  expected 502 (seen once on macOS CI for PR #910). The test now waits until the
  server holds no connection before each next upgrade, so it still checks that
  every answer frees its place. The server logic is unchanged.
