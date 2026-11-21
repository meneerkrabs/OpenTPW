## Theme Park Inc: a read-only Staff panel (November 21, 2026)

The experimental Theme Park Inc HUD has a Staff button that opens a panel listing the hiring pool's candidates in slot order (role, name, experience and monthly wage) and, under a heading, the hired members. A one-line note says that hiring comes in a later version. The panel only reads the pool: it does not pause or change the simulation and draws from no random generator.

Role names come from `STAFF_TYPES.str`, which holds each role's singular and plural name as consecutive entries, so a role reads entry `2 * role`. Candidate names come from the per-role `*_NAMES.str` tables (35 entries each, no leading header entries), indexed by the candidate's name index directly. The executable's name formatter (`0x62FFB0`) reads "index + 4"; that is most likely the in-memory table header and is not applied (unverified against the executable).
