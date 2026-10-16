## Research lab bars and the WCT chroma scales (October 16, 2026)

The research lab's bars at the right end of each row were drawn as a black cell with white stripes: the row drew only the fluid
(`res_fluid`), whose idle frame is the empty black fluid, and not the tube (`res_pipe`, root `pipe`) that frames it in the original. Each category
control of the table at file offset `0x35299c` holds both models; the tube is now drawn over the fluid, 17 units below the panel's top.

The tube's rects are the table's own: it holds five pipe children at x 1497..1804, each 17 units under its panel's top (y 175..294, 335..455, 496..615, 657..776, 818..937), and each row now draws its own rect. The fluid still shows its empty black frame (with the white stripe) while a row's research rate is zero, as the original's idle rows do (video 34:30: two running rows are green and bubbling, the idle ones black with the stripe between the orange caps).

The tube showed a second, older fault: yellow-green caps where the original's are orange. The wavelet texture header lists two chroma scales,
and the decoder gave the first chroma plane the first short and the second plane the second. The planes take them crosswise, so saturated reds and
oranges had decoded as yellow-greens (muted art barely changed, which hid it). With the scales crosswise, `pipe.wct` has orange caps and
`ridestatbar.wct` starts red-orange and runs to green, as the video shows (UI-106 retired: its "decode difference" was this).
The video-read gauge ramp (UI-046) stays until the sprite itself is drawn.

The assignment is settled in tp.exe: the texture loader (`0x564e20`) pushes the header's third short as the first chroma scale argument and the second short as the second, and the decoder (`0x593ab0`, and its twin for the other header type, `0x595920`) applies the first to the plane its colour conversion multiplies by 1.772 (Cb) and the second to the plane it multiplies by 1.402 (Cr). A census of the retail data finds the two chroma shorts equal in 6280 of 8375 WAD textures and in all 41 loose ones (so most of the corpus is untouched) and different in the other 2095, among them most UI button art (44 / 33).

Of the 22 originally pinned corpus textures only one changed (`Sp_Botplate1b.wct`, whose two scales differ); its pin was re-recorded, and three textures with differing scales (`pipe`, `b_1person`, `ridestatbar`) are pinned now. The synthetic pins are
unchanged with their chroma pairs swapped, which shows the plane to header mapping is all that moved. New tests decode the cap and the bar's two ends.
