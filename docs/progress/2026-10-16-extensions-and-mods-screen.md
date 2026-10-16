## The Extensions and mods screen (October 16, 2026)

Phase 3 of the mod system (docs/MODS.md): Options > OpenTPW has an **Extensions and mods** button beside Game files, opening
`ModsScreen` (`[EXT:mods-ui]`), built from the original options pieces (`f_screen`, two `f_optpanel` columns, scrolling lists, the
`b_on` switch).

- **Rows and statuses.** The official extensions and the community mods found at startup each show name, version and a status:
  on, off, incompatible, missing input folder, conflict, needs another mod, or cannot be used. The status is the real
  `ModSet.Resolve` result for the pending choices (`ModRefusal` now carries a `ModRefusalKind`), not a second rule set.
- **Details.** Author, version, description, content digest (once the mod is accepted), dependencies, folder and the refusal reason.
- **Input folders.** A mod with an `original-data` input gets a folder row that reuses the Game files chooser and typed-path entry.
  The chosen folder is inspected out of process and judged by `GameEditions.Detect` (`HasKnownStandardSam` or folder structure);
  the result is shown, a folder of the wrong edition is not saved, and the choice is stored as `inputs:{id:{input:path}}` in `mods.json`.
- **OK / Cancel.** OK writes `mods.json` only when something changed and shows the restart notice; Cancel discards. Nothing is applied
  while the game runs, and with no edits nothing is written.
- **Languages.** All texts are in the six languages. The front-end smoke test captures the screen.
- No simulation, save or pinned digest changes: the screen only edits `mods.json`.
