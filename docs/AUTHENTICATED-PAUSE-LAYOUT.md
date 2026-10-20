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
Source review and exact-head CI are required; no C# or rendered visual execution
is claimed here. This is a host-session mapping, not complete native online-state
or font-bank parity, and does not retire UI-013.
