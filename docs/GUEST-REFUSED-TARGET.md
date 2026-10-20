# Retaining a full-room refusal target

A guest refused because the queue has no room keeps the attraction as its target.
The refusal still changes neither its thought, happiness, history nor decision stamp.
A later removal of that attraction therefore applies the existing small happiness
step once and returns the guest to its decision state, at the same position.

The target is cleared at a real major choice, including a choice that finds no
replacement. Observational choice inspection leaves it alone. Successful bin
selection replaces it; ordinary intervening activity, wandering and departure
can retain it. Other refusal results keep their existing clearing behavior.

The existing snapshot field carries this state without a schema addition. Live
validation permits only the specified retained-target state/activity combinations,
continues to require a registered attraction, and keeps queue and rider membership
checks. Old saves with a zero target remain zero.

This is a bounded GUEST-004 change. Completed visits, the moved-back-cell failure
path, minor destinations and the separate major-destination field remain open.
Source review and synthetic tests do not establish all original guest behavior.
