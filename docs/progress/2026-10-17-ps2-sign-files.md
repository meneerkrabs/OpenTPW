## PS2 sign files corrected in docs/PS2.md (October 17, 2026)

- **What was wrong.** The PS2 notes said sign text lives in `<ride>/sign/_name1.ssh`. No such file exists in the export index.
- **What the data shows.** Sign text is baked per language in `<ride>/textures/sign_{eng,fre,ger,jpn,jap}.ssh` (509 files, nearly all 128x64, type 0x85, some 0x84). The PAL ELF `SLES_500.32` holds
  the selector strings `sign_ENG/FRE/GER/ITA/SPA/DUT/SWE/JPN` at file offsets 0x261510..0x261580. The 234 files under `sign/` (233 of them 16x128) are material and paint strips,
  the counterpart of the PC relief strips; `FANTASY/Rides/acorn/Sign/acorn1_fre.ssh` (256x128) is the one baked sign there.
- **Consequence for COMPAT-003.** The baked art uses different fonts and layout, half the resolution and a lossy GM block format, so it is not a pixel oracle for the PC sign pipeline's glyph coverage.
