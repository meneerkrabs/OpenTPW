## The advisor speaks without a stall after a join or a park start (October 23, 2026)

A guest's build of a shared park now prepares the joined park's advisor as a park start's build does, and both builds decode the advisor's first lines ahead ([COOP.md](../COOP.md), "The game's join" and "Measurements"; `[EXT:online-coop-snapshot-join]`, `[EXT:park-entry-preparation]`). This is the fourth part of C2c-2b-3.

- The join's build reads what its advisor's first line reads: the advisor's tables, its shader, its rigs, their clip lengths and the theme costume. It keeps them in a store of its own, which the live game's frames do not drop, and the swap hands them to the advisor.
- A park's build, a join's and a park start's, decodes the lines its start says first ahead in small slices: the Welcome, and in Instant Action the prebuilt park's line. The advisor plays the same audio as before, without decoding it when it speaks. In the browser such a line now starts on time instead of a few frames late. A line read before a mod or language change is decoded again.
- The joined advisor's clock starts with the park's first update, as a park start's does.
- On the desktop the frame that swaps a joined park in takes 70 to 78 ms instead of 168 to 228, and the advisor's part of it 5 ms instead of 106. In the page compiled ahead of time that frame takes 90 ms with its drawing instead of 110.
