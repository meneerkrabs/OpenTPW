## A refused guest walks back to its queue place (October 20, 2026)

When a ride refuses the guest it called at the stand point (closed, broken down
or condemned, or its admission slot still busy), the guest now does what tp.exe's
state 13 does: it walks back to its place in the queue and stands there again, or
leaves the queue when no route leads back. It used to stay at the stand point and
offer itself again every tick until the ride's next update sent it away, and a
priced shop or sideshow ran its price test, and recorded the guest's view of the
price, on every one of those ticks. A busy admission slot now also clears the
ride's pending call, so the guest is called again later. Follow-up to the
post-merge review of #519; no approximation ID changes.
