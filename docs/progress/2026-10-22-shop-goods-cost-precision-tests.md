## Shop goods cost host-precision regressions (October 22, 2026)

Added asset-free synthetic regression tests for the current ShopGoodsCost single-precision
host contract, using an independent BigInteger rational rounding oracle. Coverage includes
8,396 combinations of masked setting bytes, exact ingredient classes 1/3 versus other
integer values, clamp transitions, and unsigned cost boundaries near 2^24, 2^29, 2^31
and uint.MaxValue. Dedicated anchors exercise nearest-even ties, truncation, signed
wrapping, and four vectors distinguishing exact-integer multiplication from premature
rounding of the cost to binary32. Production arithmetic is unchanged.

A separate Python Fraction oracle and production-shaped arithmetic model agreed on all
8,396 matrix cases and the dedicated anchors. This is arithmetic cross-check evidence,
not a C# test pass: compilation and .NET execution remain pending. No native executable
was run and no original game assets were used.

ECON-059 and proposal #515 remain open research: these regressions do not identify the
original simulation thread's FPU precision or establish native parity. Native control-word
or discriminating sale-output evidence is still required before selecting a different
precision contract or retiring the approximation.
