# Challenge progress: traced contract and remaining data gate

ECON-058 remains open. This is a static evidence correction, not live progress-message implementation.
No original execution or copied original text/assets is included.

## Dispatch and reusable formatting

The running-challenge branch of `0x4D1E90` posts progress on turns where `(turn & 3) == 0`.
`0x4D3390` maps the 33 valid challenge types to tags 280 through 312; invalid/type-zero cases return
no-text tag 383. Advice `0x160` is a separate field, deliberately skipped by the advisor, not the text tag.

The message constructor `0x40F7A0` receives the slot's prize, target duration, target value, target object
IDs and staff type, signed whole days left, and the system's stored accumulator at `+0x3DC`.
`0x509E10` chooses formatting by the tag record's `+0x1C`, not by the challenge type itself.
Its relevant typed argument IDs are target value 1, target time 2, prize 3, raw progress 4, days left 5,
first object name or staff plural 6, and second object name 7. The selector decides which arguments exist.

`StringTemplate.Parts` already preserves `ParameterType`. `UiText.FormatByType` demonstrates the needed
ID-keyed substitution; `StringTemplate.Fill` alone accepts occurrence-ordered values. Localized templates
can reorder arguments, so a second generic parser or fixed positional substitution is unnecessary and unsafe.

The missing records are still essential. Tag lookup `0x50AF40` reads 32-byte records at `0x75ECA8`:
`+0x0C` selects the TAG_SYSTEM entry, `+4` the replacement identity, and `+0x1C` the formatter selector.
The traced type-to-tag dispatch does not determine those fields. Existing advisor-caption mappings do not
cover the progress tags. Do not infer correspondence from nearby English sentences.

## The displayed value is stored state

The fourth-turn branch copies `+0x3DC`; it does not run the challenge measurement first.
Ordinary and follow-up offers reset that field. Eligible events accumulate counts and raw daily sums.
The checked-turn function `0x4D1660` updates percentage types 9/10 only with a positive guest census,
and staff-skill type 29 only when checked. A nonpositive census retains the old value. `CheckAtEndOnly`
returns before those measurements until the end check. Daily-average comparisons divide the raw sum
without replacing it. Other absolute checks do not generally write the displayed accumulator.

The original save writer persists `mValue`. OpenTPW's `ActiveChallenge.Baseline` currently means either
a count offset or daily sum, not a universal displayed snapshot. Its import drops saved `mValue` for
other types. Future progress work must retain this state explicitly without changing completion,
deadline, warning or scoring behavior. Fresh `Measure()` on every fourth turn would change semantics.

## Tube refresh is conditional

`0x48EB20` matches the incoming replacement tag and two context fields against existing items.
For challenge messages those context fields are zero. If incoming tag and replacement tag are equal,
matching text is refreshed in place, including the selected open panel. It does not reinsert, play a new
posted sound, reset timers/actions or change order; a full tube can still refresh a match. If incoming
and replacement tags differ, matched rows are removed and normal insertion follows. No-match insertion
has its ordinary capacity, sound and lifetime behavior.

Therefore the old claim that progress always replaces the offer "in place" was too strong. The actual
progress/offer relationship needs the missing replacement fields. OpenTPW's current pending string/action
lists and open-panel lifecycle also need a deliberate identity/ownership contract, rather than a guessed
challenge-index key presented as original behavior.

## Acceptance once the records are available

- Verify actual entry/selector/replacement records and all valid/unknown type-to-tag dispatches.
- Use synthetic reordered/repeated typed arguments and localized object/staff names.
- Cover raw sums, checked-turn snapshots, zero census, end-only checks and imported state round trips.
- Preserve fourth-turn/deadline/warning behavior and signed whole-day arithmetic.
- Check pending/open same-tag refresh without sound/count/order/timer/action mutation, including full tubes.
- Check cross-tag replacement, context mismatch, deletion/close/reopen, follow-ups, completion/failure and load.
- Run challenge, HUD, audio, determinism and save suites, then independently inspect open-panel behavior.

The currently readable evidence does not supply the required tag records. This is a precise data gate,
not a conclusion that all formatting or all original evidence is unavailable. No runtime tests are claimed
by this note.
