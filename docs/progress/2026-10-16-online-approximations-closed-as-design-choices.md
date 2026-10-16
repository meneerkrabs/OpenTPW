## Online approximations closed as design choices (October 16, 2026)

The eighteen open `APPROX:ONLINE-*` IDs recorded uncertainty inside OpenTPW's own online extension, not required original
behavior, so they are closed as explicit OpenTPW design choices:

- Each site now carries an `EXT:online-*` label that states what OpenTPW does (for example `online-word-filter`,
  `online-globe-tick`, `online-chat-park-view`, `online-visit-count-guard`); behavior is unchanged.
- [ONLINE.md](../ONLINE.md#online-design-choices) replaces its approximation register with a design-choices table: label,
  former ID, OpenTPW's decision and the known original evidence (Mac traces of the word filter, tp.exe addresses of the
  globe, chat overlay and park-name masking, manual pages) for a later fidelity pass; nothing there is claimed as reproduced.
- The fidelity register has no ONLINE area and no Markdown register any more: `ONLINE` is a closed area whose former IDs
  stay reserved (recorded in `tools/fidelity_retired.txt`), and a new `APPROX:ONLINE-*` tag fails the check with a
  pointer to EXT labels. Every unresolved ID in the register is now in the original areas; the website status page
  renders the register from the sources in memory and reads its new sentence.
