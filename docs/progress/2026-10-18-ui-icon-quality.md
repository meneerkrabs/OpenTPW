## Interface art decodes with the original's planar rows (October 18, 2026)

The original interface icons looked softer and duller in OpenTPW than in the Dutch gameplay video: the info button showed a doubled, grey-cream ring and a thin
letter where the video shows one crisp white ring and a bold "i" with a black outline, and the gold was muddy. The data was not the cause: `ui.wad` holds the
full set (`textures`, for example a 64x64 `b_info`) next to a low-detail copy (`stexture`, 51x51), the original loads the low copy only for the Low detail
preset (`0x4688b0` sets the texture directory name, `stexture` or `textures`), and OpenTPW already loads `textures`. The cause was the wavelet decoder.

A WCT whose first header flag bit is clear (flags 0x12: most of `ui.wad` and the advisor's golden ticket, 993 files counting the low-detail copies) stores each row's
coefficients planar, all scale samples and then all wavelet samples. tp.exe's decoder `0x593ab0` reads a row so (a scratch row, scale samples from its first half,
wavelet samples from its second half); the world textures (flags 0x13) go through `0x595920`, which takes scale/wavelet pairs straight from the stream. The sign boards'
wavelet decoder, pinned against the executable, already read planar rows. `TextureFile` read the 0x12 rows with an invented "half-scale" layout (source index halved, output
pairs overlapping, a pi/6 scale factor and a shifted alpha plane), which approximated the image but ghosted every edge. It now has one planar and one interleaved row
pass; the world textures (0x13) decode byte for byte as before.

Verification: a synthetic planar WCT decodes within two levels of `SignWaveletDecoder` (the old layout was 135 levels away), and `b_info` keeps the black outline around its letter
(a texel test that fails on the old decode). 28 pinned hashes of 0x12 textures were re-recorded (the interleaved synthetic cases and every world texture pin are untouched).
Capture comparison against the video: `b_info`, `b_money`, `b_resrch` and the buy-window category buttons now show the video's crisp rings and outlines.

Open, not changed: the Enhanced HUD/icons choice is on by default and replaces 55 buttons by AI redraws that were prompted from the old, ghosted decode; and the original at
medium or lower texture quality decodes into 16-bit surfaces (table `0x7011a0`, 2 bytes per pixel except at the highest quality), where OpenTPW always decodes 32-bit.
