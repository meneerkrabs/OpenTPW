## Co-op: a steady restart test (October 21, 2026)

The co-op server test that restarts the server while a host streams frames failed now and then on the macOS runners: when the guest read slower than the host sent, its queue reached its limit and the server rightly cut it off for slowness before the restart, so it never saw the restart's close. The test now gives the guest a queue that takes the whole burst, since it is about the restart, not about slow readers (which other tests cover).
