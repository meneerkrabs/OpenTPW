## The park has its sky (October 16, 2026)

The video camera placed with the HUD's camera button looks over the horizon, and the sky showed pure black there: the park's
world pass never drew a backdrop (the old unlit sky cube was removed with the lobby rework in 5f230946 and nothing replaced it),
so everything not covered by terrain or objects stayed at the framebuffer's black clear. It is not a regression of today's
merges (the overhead camera never looks at the horizon). The park now draws the engine's sky from tp.exe via REA (UI-107):

- **The clear** is the level's own fog colour, `ThemeEngine.FogColour` of its Standard.sam (jungle and fantasy `0x48D8F8`, hallow
  `0x112244`, space `0xF88430`; `0x4080E0` copies it to the fog colour the viewport clear, `0x5737AA`, uses).
- **The cloud layers** (`0x5852B0` loads `sky\sky.tga`, `sky_rgb.tga`, `sky_cyl.tga`; `0x584EF0`, `0x585720`, `0x5863C0`): a 16 x 16
  grid 2400 wide hanging 300 above the camera and sagging by a fifth of the distance from its centre, tinted per vertex from
  `sky_rgb.tga` sampled at 16 x 16 points, drawn as 1, 2 or 4 layers (SKYQUALITY, now applied; 0 draws no sky, as the original's
  draw is gated on the count) with their own texture step, alpha (0xCF down by 0x38 per layer) and mirroring, and scrolling by
  the frame time (`0x585F10`: offset += velocity x frame seconds x 25, wrapped into -1..1, held still for a frame of a second or more).
- **The backdrop cylinder** (`sky_cyl.tga`): 16 segments of 4 rings, radius 720 (the grid's half width x 0.6), rings from 156 above to
  156 below the camera, its haze fading into the cloud layer at the top. Both are centred on the camera (the view with its
  translation zeroed) and draw without depth, so the sky only turns with the view.
- Presentation only: the scroll uses the render frame time like the original's, no world random numbers are drawn, and a level
  without `sky.tga` or `sky_cyl.tga` draws no sky.

Still open under UI-107: the moon billboards of the hallow and space levels (`SkyObjects`, `0x5860D0`), the flag-dependent variants
of the draw, and the park's own render flags behind the clear. Checked with native captures from a ground-level camera in the jungle,
hallow, space and fantasy levels; the overhead camera is unchanged.
