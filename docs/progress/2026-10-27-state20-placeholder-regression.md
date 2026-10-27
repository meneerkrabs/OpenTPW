## State-20 guest placeholder regression (October 27, 2026)

Extended the synthetic, media-free `OriginalGuestPlaceholderImportTests` from #803
to cover original state 20 (`WalkingToVehicle`), which the current importer rejects
alongside states 11–17. The added parameterized case keeps the state-6 positive
control, foreign-model protection, rejection diagnostic and unchanged guest RNG
assertion. The mixed fixture now includes a state-20 placeholder in a deliberately
non-numeric used-list order. Both exercise pool snapshot restoration, the complete
independent free-list oracle and subsequent allocations crossing the reserved IDs.

This is new state coverage, not a reimplementation of #803 or a fixture copied from
the private Fantasy saves. `SAVE-THINGS.md` already records Fantasy guest 39 in
state 20; this change documents the corresponding unsupported-state boundary.
No runtime importer, RNG behavior or state acceptance changes.

The arrival-vehicle integration gate remains: importing a live state-20 walk needs
restored vehicle identities and script continuation as well as validated saved
destination and continuation behavior. The no-cached/current-vehicle scheduler
slice does not provide that. Queue, rider and guard links remain separate missing
restoration work. SAVE-002 and proposal #536 remain open.

Static source/fixture and patch checks passed. C# compilation and execution were
not run because this preparation environment has no `dotnet`; the focused test
class and repository checks remain pending. No original media or executable was
read or run, and no original-runtime parity is claimed.
