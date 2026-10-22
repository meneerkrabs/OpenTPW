## Backspace removes pool bridges and pool water (October 22, 2026)

With no build tool on, Backspace over a pool bridge now takes the bridge down, and over a pool water cell it erases that one cell. Both belong to the pools extension; the original game has no pools.
- **Bridge:** same rules and refund as the old remove row (refused while somebody is on it). The bridge wins over the water under it.
- **Water:** same rules and refund as the pool tool's erase mode (75 back; refused for swimmers, a changing room's access, water an extra or attraction needs, and water under a bridge).
- **Buy list:** the "Remove pool bridge" row is gone, with its two strings in every language. "Remove pool water" stays for erasing a large pool in one drag. The bridge help text now points to Backspace.
- **Co-op:** the existing Backspace command carries both, so rights and replay are unchanged.

### Review fixes (October 22, 2026)

- **Extension off:** Backspace over pool water or a bridge does nothing (no message, no sound), instead of "You can't build here".
- **Held key:** the auto-repeats of a held Backspace are ignored over pool targets, so the bridge and then the water under it are not both taken by one hold.
- **Bridge tool:** the unreachable bridge-erase mode of the pool tool is gone; the removal command kind stays so old logs decode.
- **Docs:** a path laid on an approach cell before the bridge goes after the bridge; old command logs replay only on the build that recorded them.

Verified:
- Level tests for bridge, water, water under a bridge, a pre-laid path under an approach cell, occupied bridge, swimmer, changing room, extras, extension off; the HUD key (refused target, other cell, held key); a twin-park replay hash session with the pool cases.
