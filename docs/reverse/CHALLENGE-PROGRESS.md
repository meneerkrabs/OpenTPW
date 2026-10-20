# Challenge progress: recovered contract and partial live implementation

ECON-058 remains open for the offer lifecycle and integration residuals described below. The all-33 progress
and terminal paths use numeric bindings and runtime localized strings. No original text or assets are included.
The evidence is static; neither original execution nor OpenTPW visual validation is claimed here.

## Numeric bindings and formatting

The four challenge dispatch tables map types 1–33 to offer, progress, success and failure tags. Type zero
and unknown types have no caption. The complete 132 records are represented in `ChallengeMessageRecords`,
including text entry, formatter selector, replacement tag, icon, actions and lifetime. Source lookup probes
an indexed row and then searches by tag; physical row position is not the tag. No metadata gate remains.

All progress records replace themselves, use icon selector 4, actions 0/0 and lifetime 0. Progress advice
`0x160` is a separate field skipped by the advisor. The four exceptional offer replacements are
274 → 306, 275 → 307, 277 → 309 and 279 → 312. Other offers use the no-text replacement sentinel 383,
so do not replace a row. Every success/failure record replaces its corresponding progress tag and has
a 30000 lifetime; offers have lifetime 90000.
A later static caller trace establishes unscaled UI milliseconds and restart-on-resume semantics; the bounded
terminal implementation and its host mapping are documented in [Challenge terminals](CHALLENGE-TERMINALS.md).

Formatting uses existing `StringTemplate.Parts`, keyed by argument ID. Target value is 1, target duration 2,
prize 3, stored raw value 4, signed whole days left 5, object name or staff plural 6, and second object name 7.
The selector supplies a specific subset; templates may reorder, repeat or omit IDs. Object IDs resolve
through `ObjectNameTable` and the current language's `OBJECT_NAMES`; staff types 0–4 select plural entries
1, 3, 5, 7 and 9 in `STAFF_TYPES`. No English names are substituted.

The integer emitter reads the 32-bit magnitude as signed decimal, ignoring the separate sign flag. Thus
negative ordinary values display their magnitude, while negating `int.MinValue` wraps and remains negative.
Signed payload arithmetic is kept separate from rendering. Object names use at most 63 UTF-16 units and
rows at most 255; truncation avoids leaving a trailing half surrogate. No currency or unit decoration is added.
Missing templates, unsupported exposed IDs and required missing names skip the caption with a once-per-type
warning instead of an invented generic caption. The shared BFST parser remains unchanged; unsupported
chunks discarded there cannot be recovered by this formatter.

## Stored raw state and unchanged scoring

`ActiveChallenge.RawValue` is a separately saved 32-bit presentation snapshot. Ordinary/follow-up offers
reset it; accepting preserves it. Eligible count events update it immediately, including between checks,
with target-object and carried-object filters. Daily types retain raw sums rather than divided averages.
Percentage types 9/10 update only on an allowed checked turn with positive census; skill type 29 updates
only on an allowed check. End-only challenges retain snapshots until their deadline check. Other absolute
checks retain stored raw state, including imported values.

Fourth turns only copy that snapshot and signed whole days remaining, truncating division toward zero.
They do not invoke measurement or check completion, including at/past the deadline. Existing scoring,
warning, deadline, payout, decline and generic gameplay event ordering remain unchanged.

Original imports retain raw state for every active type. New saves serialize it directly with the existing
active challenge record. The presentation-only RawValue is excluded from the canonical gameplay hash;
scoring baseline and all existing gameplay state remain hashed. Older saves infer only counts from restored counters minus baseline and daily
sums from baseline; other snapshots default to zero because their historical value was never saved. The
restore hook runs after counters are restored and never takes a fresh absolute/percentage/skill measurement.

## Immutable transitions and row ownership

A separate immutable presentation side channel captures event-time values and offer wording. The runtime
forwards it across economy replacement, detaching the previous objectives instance. Gameplay events keep
their existing order and ledger effects.

Follow-up offer presentation precedes the success helper. The helper selects the post-follow-up slot and
posts success only if that slot's `FollowupType` is zero. Missing/forfeited follow-ups with a nonzero
follow-up type emit no success. This deliberately avoids deleting an old progress row merely because a
generic completion event was raised. Failure payloads carry raw state rather than the scoring average.

Progress owns a stable `(tag, context-short, context-int)` identity, with challenge contexts `(0,0)`.
Refreshing updates matching row text and the selected panel, without insertion sound, reordering or action
changes. Close retains the row; clicking its selected icon closes the panel; explicit delete removes it.
The next fourth-turn progress reinserts once. A full tube can refresh an existing row, but refuses a new
progress row without evicting an old message. Generic message behavior is unchanged.

## Deliberate residuals and validation

These are bounded progress/terminal slices, not complete original challenge-message parity:

- Existing offer wording, Accept/Decline buttons and unanswered-offer repost extension are retained.
- Exact incoming offer/result replacement effects run once per actual transition; repost does not repeat them.
- Success/failure now remove their exact replacement progress rows synchronously, format the immutable
  emitted payload, and insert distinct retained terminal instances with the new-row sound and lifetime.
  The [terminal lifecycle](CHALLENGE-TERMINALS.md) describes read cancellation, delayed release and explicit residuals.
- Original offer expiry/removal dispatch to Decline, generic tube ownership/capacity, persistence and art
  remain separately scoped. The original ten-slot tube is distinct from the advisor's eight-slot queue.

Synthetic tests cover all 132 records, all 33 live types, typed formatting/localization, integer edges,
raw event/snapshot timing, imports and old/new save behavior, follow-up ordering, replacement exceptions,
open/close/delete/full-tube behavior, sound counts, and runtime replacement forwarding. Optional installed
locale checks explicitly skip unavailable tables. C# execution and visual validation still need an environment
with the pinned SDK and runnable OpenTPW content; the progress note records the checks actually run.
