## RIDES-031: Theme Park Inc's line validator is the same routine (November 10, 2026)

The entry's last open clause was "Theme Park Inc's own validator twin of `0x535670`". It is not a twin in the sense of a second implementation to port: it is the same routine, and every difference between the two builds is data.

In `Game.exe` it is `0x67E320` (`0x67E320..0x67EAA6`), the same `__thiscall` with `ret 8`, reached through the import thunk `0x401ACD` from the same five kinds of call site — the first-click start check with modes 1 and 3, the `SetCell` collapser, the ghost, the ghost-refused path and one internal call. The branch graph, the record offsets (`+0x00`, `+0x08`, `+0x0E`, `+0x0F`, `+0x10`), the sticky refusal latch and the special cases all map one to one.

What differs is data:

| | Theme Park World | Theme Park Inc |
|---|---|---|
| end of line | `0xB` | `0xE` |
| ignored | `0xD`, for mode `0x3A` | `0x10`, for `0x3A`, `0x3D` and `0x3E` |
| queue connects onto a path | `8` | `0`, with the same two link globals set |
| `0x22` | — | `8` |
| `0x88`, `0x89` | — | `0` |
| `0x36` | `1` when the latch is set | always `0` |
| `0x1B`, `0x17` | — | remapped to `4` and refused outright over an object cell |
| owner overwrite priority | record `+0x54` | record `+0x7C` |

OpenTPW collapses the answer to allow, refuse, ends-the-line or sets-the-link, and drives the pointer and ghost from the flags rather than from the integer — so the changed codes are invisible to it. What a port would actually need is TPI's **mode table**. The declaration now records that, and the entry's clause is no longer an unread question.
