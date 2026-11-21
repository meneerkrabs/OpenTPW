## Theme Park Inc: the staff hiring pool runs in the park (November 21, 2026)

A Theme Park Inc park now keeps the executable's staff hiring pool, so the pool's random draws come at the same points as in the original. Hiring is not possible yet, so the pool has no members.

- **Opening pool.** A new park builds it at once (`0x630CB0`, called by the new-world init `0x6482BA`), before the layout loads.
- **Turn update.** Every turn, after the thing loop and before the calendar, the world update's tail runs the pool update (`0x649868` → `0x5D65E0` → `0x631B40`): candidates expire, the pool refills and the minimums are topped up. The update draws from the world generator and runs in every world state, the lost one included.
- **Promotion rank.** A new career starts at rank 1 (`0x74BDC7`), so candidates come from the agencies of rank 1. OpenTPW has no promotions yet.
- **Name test.** The pool's name test compares texts. No retail language holds a staff name twice across the six name tables (a new language test checks all five), so role and index decide that test exactly.
- **Saves.** A Science continuation save keeps the pool: its slots, the members' state and day counters, the strike records and the last wage demand's day. A Theme Park Inc save from before the pool is refused, because the pool's draws share the world generator.

The experimental Theme Park Inc HUD has a Staff button (top right) that opens a panel listing the hiring pool's candidates in slot order (role, name, experience and monthly wage) and, under a heading, the hired members. A one-line note says that hiring comes in a later version. The panel only reads the pool: it does not pause or change the simulation and draws from no random generator.

Role names come from `STAFF_TYPES.str`, which holds each role's singular and plural name as consecutive entries, so a role reads entry `2 * role`. Candidate names come from the per-role `*_NAMES.str` tables (35 entries each, no leading header entries), indexed by the candidate's name index directly, as the executable's name formatter (`0x62FFB0`) passes the candidate's name-index word (+4) to the string lookup `0x8934A0`.

Verified with these checks:

- The Theme Park Inc, original-UI, save and economy filters pass with the retail data (1558 tests). They include new tests for the snapshot round trip, for refusing foreign snapshots and for the name tables.
- The silent, offscreen Science smoke exits 0.
- The browser build with the determinism pins builds.

The original game was not launched, and no binary or data is included.
