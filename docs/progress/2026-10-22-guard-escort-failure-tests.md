## Guard escort admission failure regressions (October 22, 2026)

Added an asset-free ECON-022 regression package for the existing guard state machine:
six cases combine missing lanes, short lanes and an unreachable path island with two
world seeds whose release-side bits differ. A successful booth-route control checks
that the same fixture actually enters escort, retains its guest and draws no release
side. Production code is unchanged.

The tests run StaffWorldSimulation.UpdateStaffMember, including its real navigation,
state transitions, sounds and world generator. Only the IGuardGuests boundary records
hold, lane lookup and release; it does not choose the guard's state, route result or
random draw. The disconnected-path fixture independently verifies both walk masks
fail before running the state machine. Assertions protect hold/release order, exactly
one release at the guard's cell, both route-attempt masks, no draw before set-down,
exactly one draw at set-down, catch/set-down sound order, and cleared chase ownership.
The successful control rejects an implementation that simply releases every catch.

Source baseline: 274345b1a4d48cfd378202431240b9b5448e3b43. Existing
GuardEntertainerTests covers successful escort, orphaned held guests and pickup during
escort, but not failed escort admission. Guards.cs records the catch/failure sequence
at 0x004D6790, the release-side draw at 0x004FEBC1 and the state setter at 0x004D65D0.
These are existing repository annotations, not a newly executed original-game oracle.

Local validation is source inspection and static/Python checks only. C# compilation
and execution have not run; independent review and CI are still required. No .NET
process, OpenCode call, browser, original executable or original game media was used.
ECON-022 and proposal #516 remain open: native booth-versus-entrance fallback,
post-failure mask parity, stranded caching, imported object order, access-cell writers
and pickup environment-row lifetime are not resolved by these tests.
