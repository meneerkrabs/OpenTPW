## Sign and compatibility design choices labelled as extensions (October 16, 2026)

The user approved relabelling these sign and graphics choices as OpenTPW extensions. Behaviour is unchanged; only labels and text moved.

- **COMPAT-009 retired:** the 16 sub-scanline unhinted coverage of the TrueType rasterizer is `[EXT:sign-text-antialias]`.
- **COMPAT-001 narrowed:** the reduced 256-wide mask is `[EXT:sign-text-mask-width]`; the lower half of the original DIB stays open.
- **COMPAT-002 narrowed:** fractional advances are `[EXT:sign-fractional-advance]`; the Mac GDI layer's width rounding stays open.
- **COMPAT-003 narrowed:** OpenTPW glyph coverage and double-precision blur sums are `[EXT:sign-coverage-precision]`; the upsampling of relief strips stays open.
- **COMPAT-010 narrowed:** the Veldrid sampler choice is `[EXT:texture-filter-samplers]`; the option stored at `[0x785898]` and the engine-flag path stay open.
- **COMPAT-032 narrowed:** alpha test and blend without depth sorting is `[EXT:sign-face-alpha-blend]`; the state-word mapping of the original texture alpha stays open.
