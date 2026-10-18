## Resume interrupted coaster loop pairs (October 18, 2026)

The coaster panel now resumes the second loop pylon after reopening or returning
from an edit tool. The pending condition comes from the chain endpoint's node
flag and survives leaving loop mode; Build stays disabled until the second
pylon is placed. Repeated Loop selection preserves the pair, while selecting
Loop again after a completed pair starts the next one. Disabled-selection
refresh respects the existing availability and upgrade gates.

The [implementation note](../COASTER-LOOP-RESUMPTION.md) corrects the earlier
selected-pylon premise: the proven routines inspect the incomplete tail or the
completed-cut predecessor, not an arbitrary mouse-picked pylon. COAST-035 remains
open for the auxiliary dispatch identity and original loader gate.

Added synthetic panel coverage includes interruptions, repeated selection,
preview flags, cut endpoints, unavailable controls and saved node flags. The
local environment has no .NET SDK, so the C# tests and build were not run here;
CI must execute them. Fidelity/progress consistency, their 23 Python tests, the
9 CI-scope tests and the Python evidence runner passed (1,537 tests, 595 skipped,
7 expected failures). No original-runtime or visual result is claimed.
