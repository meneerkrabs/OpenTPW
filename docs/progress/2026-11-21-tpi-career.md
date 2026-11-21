## Theme Park Inc: the career, the mission files and the Global Master Save (November 21, 2026)

A Theme Park Inc park now runs on the executable's career object (`0xBFC484`), and the player's progress is saved as the original's Global Master Save. The layout is in [TPI-CAREER.md](../reverse/TPI-CAREER.md).

- **Mission files.** The generic SAM binding is ported: the slot rule `0x418F60`, the reset `0x419830`, the parser `0x419CD0` and the resolver `0x4192C0`. The mission object (schema `0x9A0C68`) reads all 18 shipped `miss00..17.sam`. Array overflows, range errors and malformed values stop the parse as in the original.
- **Career.** `ThemeParkIncCareer` ports the reset, the mission load and advance (with the region table), the promotion, the golden tickets, the abilities, the zone records and the career side of the completion tick. The challenge system now reads its mission counter and firing line. A won challenge's tickets, promotion and zone count go to the career.
- **Global Master Save.** OpenTPW keeps every section of `.GMS`, writes the file and reads it with the original's lenient rules: a marker that differs is a successful load that stops there, and trailing bytes are ignored. The three corpus files write back byte for byte.
- **Player file.** The career is saved as `opentpw-tpi/users/1New Player/New Player.GMS` in OpenTPW's own save folder, never in the original Theme Park Inc's `save` folder (user decision). Saving the Science park writes it, and leaving a park runs the full save (the player file, then the Science park save). Enter Game and Load A Game take the career from it. A Science save from before the career is refused with a message (user decision).
- **Cheats.** With `opentpw.cheats` on, the challenge panel offers the executable's debug buttons Skip Mission and Get Promoted.

Verified with these checks:

- The Theme Park Inc, save, resume and front-end filters pass with the retail data and the corpus saves.
- The silent, offscreen Science smoke exits 0.
- The browser build with the determinism pins builds.

The mission controller, the mission window and the zone switch come in later work packages. The original game was not launched, and no binary, save bytes or data are included.
