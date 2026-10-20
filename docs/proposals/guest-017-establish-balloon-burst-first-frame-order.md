# GUEST-017: Establish balloon burst first-frame order

The October 18 evidence-only proposal is followed by an authorized, bounded live
implementation on October 20. It is not a full native-fidelity qualification or
retirement of GUEST-017. Current-head CI and independent review determine merge
readiness; source tests alone do not establish a passing result.

## Evidence and implementation

See [the timing contract](../reverse/BALLOON-TIMING.md) for the executable identity,
static addresses, scheduler ordering, synthetic acceptance cases and residuals.
Native creation starts invisible. Burst rebind preserves the held script deadline
and current visual. A signed strictly-later sample runs one VM step and schedules
sample+62; there is no elapsed-time catch-up and no fixed render-frame delay.

BalloonSpriteState and GuestSimulation own explicit creation, burst, deletion and
shared integer sprite-pass scheduling. The renderer places held players and draws
current output without detecting gameplay transitions. Detached bursts have their
own identities and keep frozen geometry. Own saves preserve both held and detached
presentation state, validated and deep-copied, without affecting gameplay hashes.

## Acceptance and boundaries

BalloonSpriteLifecycleTests cover creation, strict due boundaries, signed wrap,
stalls, thirteen fade outputs and halt/reclamation, guard burst versus ride delete,
create-burst-create, JSON presence/validation/deep copies, old-save reconstruction,
actual host pause/lost-park and batching, and canonical/legacy hash exclusion.
Existing balloon colour/bob tests remain unchanged.

- Native original save phase remains SAVE-013. No detached slot or original clock
  epoch is guessed from a cleared guest balloon handle.
- The fixed-60Hz and non-pooled host scheduling differences remain explicit.
- Existing pool/TPI umbrella extension behavior is not called native TPW behavior.
- Bob/placement and full render-epoch equivalence are not closed here.
- No original executable or media was executed/uploaded, and no new RNG call or
  hash pin adjustment belongs to this change.
