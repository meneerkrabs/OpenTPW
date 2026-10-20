## Closing a ride withdraws its call at once (October 20, 2026)

When a ride refuses the guest it called at the stand point (closed, broken down
or condemned, or its admission slot still busy), the guest now does what tp.exe's
state 13 does: it walks back to its place in the queue and stands there again, or
leaves the queue when no route leads back. It used to stay at the stand point and
offer itself again every tick until the ride's next update sent it away, and a
priced shop or sideshow ran its price test, and recorded the guest's view of the
price, on every one of those ticks.

Closing a ride now also clears its call at once, as the original's close does,
instead of at the ride's next update. A called guest that has not started walking
waits; one on its way is turned back at a closed ride and called again once the
ride reopens, or admitted straight away when it arrives after the reopening. With
that, GUEST-011 is retired. Follow-up to the post-merge review of #519.
