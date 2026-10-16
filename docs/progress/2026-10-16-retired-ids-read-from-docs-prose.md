## Retired IDs read from plain docs prose (October 16, 2026)

`tools/fidelity_register.py` recorded a retired or reserved ID only when a docs table row keyed it or prose cited it as
`APPROX:ID`. Many docs retire IDs in plain words instead (docs/TRACKS.md: "Retired with a code change: …", "… retired
October 14"), so `tools/fidelity_retired.txt` had no TRACK entries at all and a retired TRACK number could have been
declared again with another meaning. The register now also reads a bounded prose form: an ID list directly before
"retired" (with an optional is/are/was/were), or directly after a "Retired …:" lead. List items are full IDs or bare
numbers that take the prefix of the full ID before them, joined by commas and "and"; parentheticals are skipped, and
anything else between the list and the word (such as "narrowed" or "collided with") does not match; negated,
conditional and questioned claims ("Not retired:", "Retired once …:", "Is … retired?") are skipped. The
committed list grew by 83 IDs across 19 areas, among them 13 TRACK IDs; the tools test suite covers the accepted and the
rejected phrasings.
