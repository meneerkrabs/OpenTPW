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

## Deterministic hashes

The retained target is already a hashed field. The historical pre-ExitRide mappings and the fresh joint-baseline diagnostic requirements are documented in [the determinism log](DETERMINISM.md#historical-pre-exitride-retained-target-diagnostics-2026-10-20). The historical mappings are not current pin evidence. The separately documented fresh joint-baseline mappings support only the bounded observed pin reconciliation; inverse projection and default paired-replay guards retain the main baseline, and fresh candidate CI remains required. This does not retire the broader fidelity identifier.
