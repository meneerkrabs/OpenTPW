## Research group threshold index confirmed in tp.exe (October 17, 2026)

The research group opener of tp.exe (`0x503B80`) compares the finished percentage with the dword at `0x785540 + 4 * highest`. The open question was
whether that address holds `ResearchTech[0]` or `ResearchTech[1]`. The static answer is `ResearchTech[1]`, so the threshold for opening group
`highest + 1` is `ResearchTech[highest + 1].PercentageForThisTech`, which is what OpenTPW already reads:

- The balance key table `0x73FC70` is laid out by `0x401030`: every field takes one dword from `instance + 8` (the block at `0x785040`), and an array of
  N elements of F fields takes N * F dwords plus one spare dword after it. The key loader `0x4017A0` stores element n at the field address + 4 * F * n,
  with no offset on n.
- Simulating that pass over the 284 table records gives `ResearchTech[0].PercentageForThisTech` = `0x78553C`, `ResearchCategories[0].Effort` =
  `0x785568` and `Research.StartingWorkLoad` = `0x785580`. The last two match the code: `0x503330` loops over the five categories from `0x785568` up to
  `0x78557C`, and `0x502D79` loads `0x785580` as the start workload.
- The shipped `Standard.sam` (layered under every park, Jungle included) holds `ResearchTech[0..4]` = 0, 0, 80, 85, 85, so groups 1 and 2 open at
  0 % and 80 % opens group 3 the way OpenTPW has it. No behaviour changed; only a `[BIN:TPW-EXE]` comment was added in `ParkResearch.cs`.

Also checked and left open: OpenTPW keeps the golden tickets per park. The original's global ticket award `0x5AF940` returns 4 (message "Global ticket award
moved here", `0x75A71C`) when a better record moves a global ticket from another park to this one; OpenTPW has no profile-wide holder for the four global
tickets and no such message.
