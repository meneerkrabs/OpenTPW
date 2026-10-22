# Challenge terminal rows: bounded lifecycle

ECON-058 remains open. This slice adds success/failure captions and terminal-row lifetime to the
[existing progress path](CHALLENGE-PROGRESS.md). It does not change challenge scoring, saves, offers,
Accept/Decline control mapping, follow-up ordering or the unanswered-offer repost extension.
Only independent code and numeric behavioral facts are included; no original strings or assets are copied.

## Payload, identity and replacement

All 66 success/failure records use the existing runtime-localized typed formatter. Success uses the
emitted prize; failure uses emitted target and stored raw value, not a freshly measured result or daily
average. The immutable side channel remains ordered: follow-up offer first, then success of the
post-follow-up slot only when that slot has no further follow-up. There is no generic-completion cleanup.

A terminal replaces its exact progress tag/context synchronously before insertion capacity is checked.
Missing or unsupported localization still performs that replacement, but posts no invented caption,
new-row sound or timer. Warnings distinguish the presentation kind as well as the type. New terminal
insertion refuses a full host tube; it does not evict another message. Success/failure tags are not
self-replacement tags, so repeated same-tag terminals coexist. The selected panel and timer events own
individual row instances independently of replacement identity. Reading, deleting or releasing one
duplicate never closes or deletes another.

Progress refresh/open/close/delete behavior is unchanged. Offers keep their wording, buttons, ordinary
message ownership and repost behavior. Generic messages still leave on opening. All new rows now
share the traced ten-slot limit and refuse full occupancy without eviction, insertion sound or help.
The configured DWORD at `0x750610` is ten; `0x48EC40` scans ten reusable records independently of the
advisor queue. Hidden rows remain occupied until their final callback (`0x48DC58`). Reuse appends
after surviving rows (`0x4A1278` / `0x65F447`), represented by the host's ordered lists, not a widget pool.
`PostGameMessage` and `KeepChallengeOfferPosted` report actual insertion. A full tube leaves an absent
offer awaiting its answer, and an ordinary later update retries after space opens; no decline or queue
is invented. `ApplyChallengePresentation` still reports a recognized processed transition, not admission.
This bounded capacity/admission correction does not close UI-025 or claim layout, timer or save parity.

## Clock and terminal states

The original caller chain supplies a real-time UI-millisecond clock (`0x489D99`, `0x659674`, `0x5F5F10`),
independent of simulation speed. The managed implementation injects a monotonic `long` millisecond clock;
its default is `Environment.TickCount64`. Neither scaled park time, UI animation delta nor advisor time
is used for terminal lifetime.

- Insertion starts a 30000 ms lifetime and a 25000 ms pre-expiry timer.
- Opening a terminal permanently disables both timers. Closing or clicking the selected icon retains
  that row and never restarts expiry.
- Unread expiry hides/disables the row and schedules final release 500 ms after the expiry handler runs.
  The hidden slot still occupies capacity, and following rows retain their positions until final release.
- Right-click or panel delete closes the selected panel immediately and uses the same delayed release.
- Cross-tag replacement releases eligible visible matches immediately. Hidden removal-pending rows
  cannot be released by that replacement path's visibility guard.
- Final release affects only its owned terminal instance. Terminals carry no Accept/Decline action.

These are removal stages, not a claim of an observed 500 ms fade or original animation curves.

## Suspension and queue boundaries

The exposed suspension transition has one flag. A suspended-to-resumed transition resets active timer
origins to the current clock, giving an unread terminal a fresh full lifetime. It preserves disabled
pre-expiry timers and read cancellation. Repeated suspend/resume calls are idempotent; they do not form
a nested counter. A pre-expiry timer that already fired does not fire again after resume.

The named managed host mapping includes:

- Explicit pause-key and speed-control pause/resume actions, plus the initial restored user pause state
- The Escape pause menu, full-screen map and options screen
- The traced object/attraction delete and staff-dismiss confirmation screens

It intentionally does not use `Hud.Paused` or a zero simulation time scale as a universal predicate.
Direct load/save and postcard are counterexamples: their original game-pause calls do not themselves
prove UI-timer suspension. Load/save under Escape inherit the host's still-open pause screen. Unrelated
modals, texture-download holds and ordinary park windows are not automatically treated as equivalent.
The host screen set is a managed mapping, not an exact emulation of the original ownership gates or
nested-screen interactions.

Native focus-loss integration remains UI-096/ECON-058. The original ordinary offline deactivation path
pauses via an explicit overlay; focus return does not itself prove game resume. The network branch's
UI-active delivery suppression is different again. This slice does not add an automatic focus-return
resume or infer suspension merely from application focus.

Timer events are generated before HUD input and dispatched after it. A read between generation and
expiry dispatch is protected by the row's active flag; stopping a timer is not assumed to erase queued
messages. The managed queue coalesces each terminal's one-shot events. It does not reproduce the full
native queue, every simultaneous pre-expiry/expiry ordering, repeated whole-interval messages,
disabled-timer phase advancement, or the original signed/unsigned 32-bit wrap arithmetic.

Delayed final release uses the unscaled clock independently of lifetime suspension. Its 500 ms is a
minimum due time; release requires a UI pump and is measured from delivered removal, not a stale nominal
expiry deadline. The research finale's input-owning early return still pumps queued events and due releases, including
under native input capture. It is not added to the suspension allowlist.

## Particles, persistence and offer policy remain open

Effects 90 and 91 are particles, not sounds. The original requests 90 once at first effective visibility,
and retains 91 from the pre-expiry timer, moving/hiding/destroying its owned emitter with the row.
There is no established HUD UI-emitter ownership/rendering interface here. Both effects remain omitted
under PTCL-908; the implementation consumes the pre-expiry timer but invents no visuals or warning audio
and does not borrow world-particle handles. No original particle appearance was observed.

Tube state is not saved (UI-025). A newly created HUD after loading has no old rows, selection or queued
timer events. If a new terminal is emitted it receives a fresh lifetime; this is not restoration of the
original saved row or remaining timer. In-place economy replacement keeps the existing HUD policy and
is not a new tube-persistence implementation.

Original offers have a 90000 ms lifetime and their final action-27 removal attempts Decline against the
then-current awaiting offer, including a new offer that arrived during the 500 ms release gap. That
conflicts with the retained repost extension. This slice adds neither offer auto-expiry/auto-decline
nor an identity guard, and does not label either possible policy as original behavior. The adjacent
Accept/Decline control-order discrepancy is outside this change.

## Validation

Deterministic synthetic tests cover all terminal types, formatting and missing localization, exact
replacement, full/hidden occupancy, duplicate-instance selection, read/close/delete differences,
25/30-second and 500 ms boundaries, queue/read races, resume restart/idempotence, stopped pre-expiry,
delayed delivery, the named screen mapping, speed controls, read-only action guards, new-HUD load isolation,
follow-up ordering, generic refusal and slot reuse, full-screen finale/captured-input pumping, and the explicit long-clock wrap boundary. The existing progress,
message/audio, presentation-state and challenge suites remain applicable. No original execution or
runtime visual validation is claimed; the progress fragment records checks actually run.
