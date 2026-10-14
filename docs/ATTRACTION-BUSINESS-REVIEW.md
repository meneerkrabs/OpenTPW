# Attraction business detail review

Reviewed `1949997dcfdacd4261aa094b2b9ab46322cdc996` against
`c4b1a2203cdd52efa8377d1d4be2b2593d674a41`. No production correction was needed.
The HUD projects only the existing mapped shop/sideshow customer count, last-month
profit and total profit. No economy, payment, balance, statistics or save rule was
added. Native counter meaning and original UI layout remain qualified.

The cache is a private value-only three-long snapshot keyed by the actual
attraction ID. It clears with the existing frame/category refresh and requires
matching park/finance economy identity, mapped record InfoId and Shop/Sideshow
kind. Labels consume the cached frame rather than reread mutable records. Missing,
read-only, TPI, wrong-kind and foreign bindings retain the existing unknown
placeholder. Ride users, remaining life and satisfaction are not inferred.

Fresh expanded focused checks passed 211 tests with 11 original-data skips and no
failures. Four independent controls add mixed shop/sideshow visible-category cache
isolation, same-info remapping and replacement of a mapped record with frame-freeze
and next-frame refresh, a foreign status economy with matching identifiers, and
shared money-format compatibility in en-US/fr-FR cultures. Values beyond the
double exact-integer boundary, both signed maxima, grouping and long.MinValue
remain exact. The formatter changes only magnitude conversion to decimal; currency
labels, grouping and ordinary signed-long output remain unchanged.

Author red logs were inspected. Real visitor payments and month rollover produced
recorded counters while the old actual HUD displayed unknown. The separate signed
minimum test reproduced OverflowException in FormatMoney with four ordinary
controls passing. Fresh controls cover the real payment/month/snapshot/selection
route, counterpart isolation, category/sort/removal healing, negative sideshow
profit and accepted extreme total-profit saves. The earlier empty-category
fixture mismatch was corrected to preserve the existing unknown behavior.

No rendering/GPU/browser execution or original assets/executable oracle were
used. Synthetic actual HUD/model controls establish host projection behavior,
not native accounting or original visual parity. No broad full CPU suite was run
for this bounded review.
