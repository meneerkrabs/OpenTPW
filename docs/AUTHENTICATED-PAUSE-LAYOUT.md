# Authenticated park pause-menu layout

This bounded #470 host adapter uses the existing authenticated online-session
state when constructing the park pause menu. Its compact layout starts at zero
with no inter-line gap, matching the online-state layout already documented in
`UiDialogs` (UI-013). Offline and connected-but-unauthorized sessions retain the
default top of 10 and gap of 5. Disabled online extensions retain that default.

The menu captures this choice once when opened. Signing in or clearing the token
while it remains open does not move its lines; closing and reopening samples the
current state. The flow reads its existing session without creating one. No login,
logout, token, transport, menu-action or permission policy is changed.

The optional layout argument is per menu. Fallback bounds and font-measured bounds
both use it, so hit regions follow the displayed lines. Other callers, including
the lobby menu, keep their defaults. The existing explicit resume callback and
focus-pause behavior are retained.

Synthetic tests exercise the actual flow-to-HUD menu path with state-only online
fixtures, measured and fallback rectangles, hit regions, snapshot/reopen behavior,
and default callers. They do not perform authentication or contact a server.
The tests ran in exact-head CI before #470 merged; no rendered visual execution
is claimed. The mapping follows tp.exe's own lifecycle: its net-session field
`+0x60` (read by `0x5b6450` when the menu is built, `0x48c36c`) is set only by the
login reply "Authorization OK" (`0x5b6363`); every other store writes 0: every other
login reply (`0x5b6375`-`0x5b63dd`), the login teardown (`0x5b6588`) and the other
session resets (`0x5b595f`, `0x5b5e7d`, `0x5b7a84`, `0x5b7d6a`, `0x5b7f0b`). It is not font-bank
parity and does not retire UI-013.
