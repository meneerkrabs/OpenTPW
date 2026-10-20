# Independent host texture creation flags

The host `OpenTPW.TextureFlags` enum had implicit values `PinkChromaKey=1`, `PointFilter=2`, `Wrap=3`, and `Repeat=4`. Consequently, requesting wrapping also enabled pink chroma-key preprocessing, while requesting pink keying plus point filtering selected the wrapping sampler.

`Wrap` now has the independent bit 8. None=0, Pink=1, Point=2 and Repeat=4 retain their values. The actual uncached creation path uses the same CPU pixel preprocessing and an extracted sampler selector, preserving explicit addressing precedence: Repeat, then Wrap, then Point, then the existing default. Source callers requesting only wrapping no longer implicitly discard opaque magenta pixels. Callers can still explicitly combine wrapping with chroma keying.

The repository carries these flags in process, including texture-pack reload requests; no host-enum serialization or numeric interchange was found. The similarly named nested `TextureFile.TextureFlags` is a separate binary-format enum and is unchanged. External code compiled with the former Wrap constant must be rebuilt; this is not a universal binary-compatibility guarantee.

`TextureCreationFlagsTests` exercises the exact CPU preprocessing and sampler selector used by creation, including all 16 combinations, opaque magenta and a non-key neighbor. C# execution and GPU validation remain pending. The tests do not construct GPU textures or prove presented pixels.

This is a host API correction discovered while investigating COMPAT-010. Native option/state mapping, clamped-slot minification, current preset filtering and MD2 shader addressing remain separate. Cached-path behavior and texture cache identity are outside this slice. No fidelity ID is retired.
