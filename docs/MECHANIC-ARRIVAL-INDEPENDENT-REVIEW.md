# Mechanic arrival independent review

Reviewed source: `2056c8a98b11e3482fabaff60c16b96a2b305940`, based on
`7a42183524adbbc90a77a02ba9eb299851039803`. The author migration design was read
before production edits. Its real five-step and disconnected-path baseline
failures were inspected separately from the initial fixture-setup failures.

`MechanicArrivalIndependentReviewTests` adds actual economy/path/save controls
for a foreign world with identical IDs and arrived coordinates, a reconstructed
employee rejected by its previous world, older `BusyTicks` units with an attached
new world, and fractional life-gauge eligibility using the existing float-byte
rule. Waiting state and hashes survive actual JSON reconstruction. Started work
keeps its existing countdown when a path disappears; the new gate is not reapplied.

The existing strike/rest transitions run before arrival observation. An inactive
strike ends and rest without a Staff Room releases the employee; picked-up staff
remain excluded. The independent controls pin those existing transitions without
adding cancellation or refund behavior. An invalid pending state refuses before
replacing the previous file or changing cash, assignment or work. A valid string
`Guard` enum tests the new role check rather than merely causing an enum parse error;
the absent-phase legacy parser remains unchanged.

A controlled CPU-only Level fixture calls the real `SetupStaff` and
`AdvanceSimulation` methods. It verifies that the economy receives that Level's
staff world and that a fresh job without a placed-object access mapping stays
pending through the actual fixed-step route. The author separately proves positive
reachable arrival and exact completion with real `StaffWorldSimulation` path steps.
This fixture does not exercise renderer construction, GPU disposal or native assets.

RepairTimer, dispatch ranking, staff vitals, upgrade purchase, Level fixed-step
ordering, research and edition source were independently compared with the base
and remain unchanged. Canonical hash schema 14 includes the new gameplay phase;
save-format versions stay unchanged. Replay requires the matching host world
binding; an arbitrary provider is neither serialized nor hashed. Legacy missing
phase and new headless jobs retain started work, while restored waiting without a
bridge freezes until rebound.

These synthetic controls do not qualify native staff movement, timer sampling,
arrival-update order, visuals, original assets, platform gameplay or full parity.
Cell-based movement, dispatch-computed work and the observation update remain
explicit host policies. No production correction was made in this review.
