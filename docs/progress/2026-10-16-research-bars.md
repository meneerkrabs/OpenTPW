## Research lab bars and the WCT chroma scales (October 16, 2026)

The research lab's bars at the right end of each row were drawn as a black cell with white stripes: the row drew only the fluid
(`res_fluid`), whose idle frame is the empty black fluid, and not the tube (`res_pipe`, root `pipe`) that frames it in the original. Each category
control of the table at file offset `0x35299c` holds both models; the tube is now drawn over the fluid, 17 units below the panel's top.

The tube showed a second, older fault: yellow-green caps where the original's are orange. The wavelet texture header lists two chroma scales,
and the decoder gave the first chroma plane the first short and the second plane the second. The planes take them crosswise, so saturated reds and
oranges had decoded as yellow-greens (muted art barely changed, which hid it). With the scales crosswise, `pipe.wct` has orange caps and
`ridestatbar.wct` starts red-orange and runs to green, as the video shows (UI-106 retired: its "decode difference" was this).
The video-read gauge ramp (UI-046) stays until the sprite itself is drawn.

Of the 22 pinned corpus textures only one changed (`Sp_Botplate1b.wct`, whose two scales differ); its pin was re-recorded. The synthetic pins are
unchanged with their chroma pairs swapped, which shows the plane to header mapping is all that moved. New tests decode the cap and the bar's two ends.
