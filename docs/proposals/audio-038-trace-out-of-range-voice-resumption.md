# AUDIO-038: out-of-range loop output recreation

## Status and boundary

PR #553 now contains a bounded loop-only correction, following independent static review of the native stop/restart contract. It does not retire AUDIO-038. Finite one-shot restart/expiry, sentence selected-descriptor timing, initial submission admission and the broader host group-enable model remain residuals. Held-chain child playback and sentence scheduling are unchanged.

The implementation retains `AudioVoice` as the public logical Stop/move handle, but releases its per-output cursor when a catalogue loop leaves range. A later admitted tick creates a new output cursor at the selected source's beginning. Logical serial, selected PCM, parameter table and explicit-stop state survive independently of the output. A released loop does not mix, duck other channels, finish, or emit a segment-completion callback.

## Native evidence

Static addresses below refer to `sp20_nocd/tp.exe`, SHA-256 `5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805`. No executable execution or original-media capture is claimed.

- `0x6BCA10 -> 0x6BCA70`: range failure sets placeholder flag `0x800` before sending type-3 stop for sample handle `+0x24`. The immediate routine does not clear the handle.
- `0x6B8D60 -> 0x6BAB50`: validate the output handle against its slot/nested stream. A valid handle reaches `0x6B7E60`; only successful backend stop reaches `0x6BB7C0`.
- `0x6BB7C0 -> 0x6C6260 -> 0x6C8E50`: the output wrapper invokes owner virtual `+4`, passing output handle, logical owner handle and reason. It then clears its callback owner pointer.
- `0x6BBE40`: a matching logical owner handle (`+0x2C`) clears output handle `+0x24` and playing bit 1. Range flag `0x800` prevents terminal bit `0x10` being set. This resolves the old incomplete inference that the range-stopped output handle necessarily remained alive. Invalid lookups and failed stops do not establish this acknowledgment.
- `0x6B62B0`: group/range/admission precede pooled ticks. `0x6BD800` re-enters setup `0x6BC410` after the output callback clears playing state. `0x6BC410` reuses the selected sound/sample, evaluates volume, pitch and delay, and reinstalls the callback owner. `0x6B9AB0` calls `0x6B9A60` with existing handle zero. The source creation path builds a new output; the PCM source constructor `0x6C6E70` zeros its cursor/state.
- The elapsed modulo in `0x6BC534–0x6BC55D` is a late-start eligibility test, not a seek offset. Event flag 8 makes an expiry-exempt loop. Native finite-source setup would redraw delay and recheck expiry against the original base deadline; this PR does not implement finite-source return.
- `0x6BC760` muted-group handling additionally clears `+0x24` explicitly after stop dispatch. Zero host gain is not treated here as proof that this separate native group-enable switch is disabled.

### Sentences and held children are distinct

Sample-backed sentence tick `0x6BDF50` reuses `0x6BC410`, but refill callback `0x6BDE00` may already have replaced the placeholder's selected sample before the previous output audibly finishes. Recreating the selected descriptor therefore does not justify either “always replay the audible segment” or “always consume the prefetched segment.” Mapping the existing early-prefetch extension requires a separate controlled selection/refill fixture.

Held-chain root tick `0x6BDAE0 -> 0x6BDB50` directly walks locally linked children. Creation `0x6BDBB0 -> 0x6B72F0 -> 0x6C3DE0` does not insert those children into the manager pool. Both child vtables `0x70A970` and `0x70A9E0` override sample setup to `0x6C3AE0`, without the callback-owner setup of `0x6BC410`. Root range exclusion prevents further child ticks/spawns, not a recursive output stop. Explicit stop `0x6BD9B0` is the separate cascade. Existing children continue playing out; they are not extra pool entries.

## Implementation guardrails

- Range release and explicit Stop are separate operations; explicit Stop and channel StopAll remain terminal even while no output exists.
- Restart is gated by the same twelve-owner rank/newest selection and by held lifetime `CanAdmit`/`Suspended`. A non-admittable retained loop does not consume a candidate slot. Simultaneous loop restarts draw in admitted pool order.
- Pool eviction alone does not release already-playing output. Restart does not renew serial or create another logical pool entry.
- No sound/sample chooser runs on range return. Retained parameters re-evaluate volume, pitch, then delay. Held lifetime preserves its volume where its existing contract requires it and receives the new output reply.
- Held fade StopPending/terminal gates remain authoritative. Range release loses physical output-reply availability; a later fade poll can end that owner before return.
- The software mixer is synchronous on the audio host thread. Released output state is unreachable; active logical voices reject completion notifications. This is a managed safety property, not a claim that the native callback compares output tokens (it compares owner identity).
- Raw mixer sources retain their existing behavior. Catalogue range admission is not added to held children or arbitrary mixer callers.

## Synthetic acceptance and validation

`AudioLoopRangeTests` uses synthetic PCM, fake catalogs, explicit mixer ticks and injected clocks. It covers fresh output phase and identity, owner retention, idempotence, unchanged sample choice, ordered volume/pitch/delay draws, simultaneous return order, any-distance/flat/zero-range exceptions, explicit/channel stop, twelve-slot and rank-zero admission, suspended-owner exclusion, placed parameter updates, held fade termination/group gain, released-speech ducking, and held-child cursor preservation. The prior pitch test is updated to distinguish retained logical owner from released output. The raw-mixer spatial test is explicitly scoped as raw mixer behavior.

Local validation is limited to source review, whitespace checks and Python documentation/register validators. Local .NET execution was not attempted because of the existing execution restriction. Runtime acceptance requires successful focused/full test execution in the authorized exact-head CI and independent frozen-source review. No passing runtime result is claimed in this proposal.

## Remaining work

1. Finite one-shots: retain original expiry base, apply old-delay admission followed by new-delay expiry recheck, and preserve late-start rejection before enabling restart.
2. Sentences: test selected S before refill versus selected T after refill, independent of the audible-buffer boundary, before changing early-prefetch mapping.
3. Initial submission and general host group-enable parity remain outside this loop-return slice. Current host zero-gain behavior is preserved.
4. Exact codec/device buffering has not been captured from the original. Synthetic fresh-source tests are not an acoustic cross-device parity claim.

Do not publish original media or private game data in fixtures or commits. Keep the residual annotation and review/CI gates visible.
