# Windows manual gameplay evidence

Evidence reviewed: October 9, 2026. Source supplied by the user:
`theme-park-world_win_manual_europe_en_ii5.pdf`. PDF metadata title:
*Theme Park World - Windows Manual - English*; copyright page identifies
Electronic Arts, 1999 (PDF 39). This is the Windows English manual, not a
verified specification for every release, platform or patch.

- Size: 1,376,752 bytes; 39 PDF pages.
- SHA-256: `c96eb25f3dc13f7f0824bbf03f9bbeb3bb94e9f4756d4d8dfa09ac71732b0668`.
- Inspection: local `pdfinfo` and `pdftotext -layout`; extracted text stayed
  outside Git. No PDF, screenshots or extracted game assets are included here.

## Citation convention and limits

**PDF** means the one-based page in the supplied file. **Printed** means the
number printed on an individual manual page: most PDF pages contain a spread.
For example, PDF 15 contains printed pages 28–29; the key rule is on printed
28. The warning sheet is PDF 1 and is not part of that numbered sequence.
References below always give both locators, avoiding ambiguity between viewer
page numbers and manual page numbers.

These are paraphrases of player-facing instructions. They establish advertised
mechanics and controls, not the original implementation's formulas, units,
thresholds or binary layouts. They cannot promote `.MD2`, `.MAP`, `.RSE`,
`.TPWS`, audio, fonts or other format decoders to complete. A documented
mechanic is also not evidence that OpenTPW already implements it. Instructions
and strategic advice should be distinguished from hard numeric rules.

## Mechanics stated in the manual

| Area | Source-supported behavior | Source |
| --- | --- | --- |
| Player and mode flow | Creating a player includes choosing Instant Action or Full Simulation; selecting an existing player resumes that player's game and opens the lobby. | PDF 3, printed 4–5 |
| Instant Action | Starts with a small built park, staff and automatic research. The park is already open; one scientist is present, and hiring more speeds discovery. Loans are unavailable. | PDF 3, printed 4; PDF 5, printed 8–9; PDF 13, printed 24; PDF 24, printed 46; PDF 25, printed 48 |
| Full Simulation | Starts with an empty plot; the player makes the initial decisions. Hiring a cleaner and mechanic before opening is advice, not a stated enforced condition. | PDF 3, printed 4; PDF 6, printed 10; PDF 13, printed 24 |
| Paths and placement | Drag/click lays paths; Backspace removes recently laid sections. Paths must connect to the park entrance, and visitors rarely cross grass to reach attractions. Ride blueprints rotate clockwise with comma and counterclockwise with full stop; placing a ride switches to its queue blueprint. | PDF 6, printed 11; PDF 7, printed 12–13 |
| Ride access | All rides need a completed queue connected to a path; without it the ride is inaccessible. An exit path must also connect to the path network. Queues can be edited and undone section by section. | PDF 8, printed 14 |
| Shops and sideshows | Shops provide food, drink and gifts; shop price, quality and ingredients can be adjusted. Shops and sideshows use the same tile for entrance and exit. Sideshow game price, prize cost and winning chance can be set; settings can be applied to other objects of the same type. | PDF 9, printed 16; PDF 14, printed 26; PDF 18, printed 34–35 |
| Admission and willingness to pay | Park tickets are a main income source; gate controls set the fee and open/close the park. Better/popular parks support higher admission. Happy visitors are willing to pay somewhat more at shops, according to the hints. | PDF 13, printed 24–25; PDF 37, printed 72 |
| Ride maintenance | Broken rides visibly smoke. Poor repair reduces reliability and hastens wear; the manual recommends preventive repairs because breakdowns reduce remaining life. | PDF 17, printed 33 |
| Staff roles and rest | Cleaners handle litter, toilets and sickness; mechanics maintain rides; entertainers help queueing visitors; guards benefit from cameras; scientists research. Staff need rest and rooms, tired staff work more slowly, and unhappy staff can strike. | PDF 19, printed 36; PDF 21, printed 40–41; PDF 22, printed 42–43 |
| Staff management | Staff can be hired, placed, moved and fired. Each worker can receive a rectangular patrol area; the nearest staff member is summoned to a job, according to the patrol instructions. Training budgets per role improve skills over time; wages are monthly. | PDF 13, printed 24; PDF 22, printed 43; PDF 23, printed 44; PDF 25, printed 48; PDF 5, printed 9 |
| Research and upgrades | Research priorities and workload can be adjusted; excessive workload can cause unhappiness and strikes. Most rides have two researched standard upgrades affecting reliability, speed or capacity; a mechanic performs a version upgrade and the ride reopens when complete. Additional track components require an existing straight section. | PDF 24, printed 46–47 |
| Tickets and keys | Golden tickets purchase mystery objects. Every third earned ticket grants a golden key. Spending tickets on mystery objects preserves the right to keys already won. Keys unlock further theme worlds. | PDF 15, printed 28; independently PDF 4, printed 7 and PDF 14, printed 27 |
| Challenges and advisor | The advisor offers challenges with cash rewards and deadlines; failed challenges are not offered again. Advice helps explain progress toward tickets, and the advisor announces researched upgrades. Advisor and tutorial advice have separate option toggles. | PDF 15, printed 28–29; PDF 24, printed 47; PDF 28, printed 55 |
| Visitor information and scenery | Visitor lists and mood bubbles reveal needs and dissatisfaction. Bins reduce litter; cameras support guards; vegetation improves appearance and reduces toilet smells; fountains improve surroundings. | PDF 19, printed 37; PDF 21, printed 40; PDF 18, printed 35; PDF 37, printed 72 |
| Information and save flow | Park graphs offer 1-, 3- and 12-year views. The map pauses the game. Named park saves are available; exiting to the lobby saves the park automatically and quitting saves the player's game. | PDF 23, printed 45; PDF 25, printed 49; PDF 28, printed 54 |
| Online features | The documented original service publishes parks to cities, shows ranks/visits/votes, supports park visits and chat, lets owners unpublish their parks, and supports postcards. Visiting a park switches to chat mode. | PDF 30, printed 59; PDF 32, printed 62–63; PDF 33, printed 64–65; PDF 34, printed 66–67 |

Online descriptions are evidence of the original user experience, not evidence
that the original service is available today, or a protocol specification for
OpenTPW's online replacement.

## Comparison with current slice documentation

The following comparisons describe the documentation at baseline `a7eb89f`.
They are follow-up evidence, not claims that the runtime or approximation
register has been corrected by adding this document.

| Current documentation | Finding | Evidence and remaining work |
| --- | --- | --- |
| `ECONOMY.md`, keys/progression and ECON-040 | **Contradiction:** one key per four tickets differs from the stated one per three earned tickets. | PDF 15, printed 28; PDF 4, printed 7; PDF 14, printed 27. Only the earning interval is proven here; the starting key count remains unproven. |
| `ECONOMY.md`, ECON-029 | Spending tickets is explicitly described, rather than merely inferred from settings. Previously won keys survive those purchases. | PDF 15, printed 28. This does not prove wallet representation, exact item costs or award timing. |
| `ECONOMY.md`, ECON-019; `UI.md`, game-mode flow/UI-015 | Staffless automatic research and identical mode behavior differ from the documented scientist, mode starts and loan restriction. The original style choice belongs to player creation. | PDF 3, printed 4–5; PDF 24, printed 46; PDF 25, printed 48. The scientist's grade and discovery rate are unspecified. |
| `GUESTS.md`, queue positions/no-exit approximations; `OBJECTS.md`, RIDES-018/RIDES-028 | Missing access behavior: real connected queues and exit paths are required; nearest-path fallback and disappearing guests are implementation approximations. | PDF 8, printed 14. The exact joining algorithm, collision rules and behavior of an already occupied ride after path removal remain unproven. |
| `OBJECTS.md`, developer build controls; `UI.md`, build tool/UI-031 | The manual provides original comma/full-stop rotation and automatic queue placement after placing a ride. Current R rotation and single object placement describe a different control flow. | PDF 7, printed 13. This does not establish continuation after placing every shop or miscellaneous item. |
| `ECONOMY.md`, staff and maintenance | Missing staff rest, mood/strike and patrol mechanics; preventive maintenance must affect life/reliability. Staff training is described as a budget over time. | PDF 17, printed 33; PDF 19, printed 36; PDF 22, printed 42–43; PDF 23, printed 44; PDF 25, printed 48. Timing and quantitative effects remain unknown. |
| `GUESTS.md`, money and unused price judgments | The manual supports qualitative willingness-to-pay effects from park appeal and shop happiness, beyond checking a guest's purse. | PDF 13, printed 25; PDF 37, printed 72. No admission/shop pricing formula is supplied. |
| `UI.md`, help/UI-010 and frontend/save gaps | Help is described at the bottom, while the current popup-help box is an acknowledged top-center choice. Player creation and automatic exit/quit saves are additional original behaviors not covered by the current frontend. | PDF 6, printed 11; PDF 3, printed 4–5; PDF 28, printed 54. Manual pictures are not pixel-layout verification. |

## What remains unproven

- Starting golden key count and whether entering a theme consumes keys; the
  initially available Lost Kingdom/Halloween worlds (PDF 4, printed 6) do not
  establish either rule numerically. Tickets-to-key interval alone must not
  resolve all of ECON-040.
- Golden-ticket thresholds, check intervals, exact award ordering and repeat
  eligibility. The manual's suggested accomplishments are not formulas.
- Tick/day/month conversion, arrival distributions, research rate or order,
  staff candidate grades, hiring fees, training rate, workload limits, rest
  recovery, patrol/pathfinding implementation and maintenance durations.
- Quantitative guest needs, queue patience/capacity, admission willingness,
  shop quality/ingredient effects, happiness premiums, nausea and scenery
  influence; strategic advice supplies no numeric coefficients.
- Interest calculation, loan early repayment arithmetic, bankruptcy timing,
  scrap value and wear/lifetime formulas. Monthly payments and displayed totals
  do not specify how those totals are calculated.
- Exact original HUD placement, colors, animations, typography and scale;
  option defaults, rendering behavior and patch-dependent differences.
- Save serialization, file formats, RSE instruction semantics, decoder coverage
  and online wire protocols. These need separate binary, asset or runtime
  evidence and cannot be inferred from this manual.

## Verification

Document identity and page count were checked with `shasum -a 256` and
`pdfinfo`. Cited spreads were read from a local text extraction, including the
three independent statements of the three-ticket key rule. Slice comparisons
were checked against `ECONOMY.md`, `GUESTS.md`, `OBJECTS.md` and `UI.md` at the
baseline above. No original executable was run and no runtime behavior changed.
