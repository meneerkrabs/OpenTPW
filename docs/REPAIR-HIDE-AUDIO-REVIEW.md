# Positioned repair-hide audio review

Reviewed `8cbd3559032586113eff1660d3ac57f62c4f67de` against
`7ebd3121e18c1c52fdce48324f5945ea0718e813`. No production correction was required.

Only `REPAIREFFECT 0` samples the existing finite object sound-space provider,
once for the global ride catalogue's 111/211 pair. The public `PlayRepairEffect(int)`
remains flat. All nonzero show operands retain the existing flat Kids75 family,
without sampling the provider or acquiring new loop ownership. Unsupported effect
counts, zero return and incoming VM flags remain unchanged; this implements the
sound-origin boundary, not repair visuals or native repair completion.

Fresh independent CPU checks passed 79 tests with 15 original-data skips and no
failures. Four added controls verify current placed-provider sampling, inherited
child-script ownership, global selection despite matching level catalogue IDs,
and silent out-of-range voices. The placement control replaces a synthetic
object's current placement record through a backing-field seam; it does not
claim an interactive move tool or renderer update. Earlier submitted loops retain
their instruction-time positions. Range gain silences voices outside the range;
submission does not cull them. Such loops remain alive, owned and capture-refused
until stopped, while another owner's voices remain audible.

The author's actual placed/import-centred red controls failed because both voices
had null positions. Its partial-submission red control showed a first loop that
survived after the second submission threw. Both failures were inspected, and the
final controls pass independently. The internal helper now stops only that
partially submitted loop and rethrows the original exception. The tests preserve
its `range` parameter, first one-shot lifetime and another owner's audio. They
then explicitly abort the root and simulate it to test existing fault cleanup;
a range-validation exception is not claimed to become a VM fault automatically.

Alive loop snapshots remain unsupported audio persistence. Finished voices,
one-shots, headless/missing audio and stopped owners remain eligible. No schema,
VM exception policy, catalogue flags, mixer law, repair-level family, two-float
operands or model/controller behavior changed. Native anchors/update timing and
pan/distance laws remain qualified by AUDIO-908/AUDIO-033. Original assets, GPU,
devices, browser runtime and native parity are not established by this review.
