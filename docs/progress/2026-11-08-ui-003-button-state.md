## UI-003 button state selection read (November 8, 2026)

Answered the register's open question about which caller drives a model button's state. Nothing passes bit `0x1` or `0x80000` to the control's state entry `0x476F10`, so a button never sets the first-child chain index (`+0xC`) or the base (`+0xA`) that `0x476E80` copies for other controls.

Instead the button's applier `0x668AD3` asks `0x668820` for the state index, ignores any value above 5, and dispatches through the jump table `0x668BD4`. Each of the six cases fills a descriptor — the button's byte `+0x158` (plus one in one case) as its first word, its byte `+0x159` as the second, and the dword at `+0x14C` or `+0x154` as a third field — and sends message `0x4C000` to the control's slot 3.

What those descriptor fields mean for the drawn frame is still unread, so the register now states that precisely instead of the vaguer "which chain index a button state selects". No runtime behaviour changes, no APPROX ID is added or retired, and no save or determinism data moves.
