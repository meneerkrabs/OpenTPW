## Spanish, Korean and Polish, and Theme Park Inc in its own languages (October 17, 2026)

OpenTPW reads nine languages now: English, Danish, Dutch, French, German and Swedish, plus **Spanish** (the Spanish CD), **Korean** (the EA Slim Pack CD) and
**Polish** (the Benelux/Polish CD, whose table was the one left out). Theme Park Inc uses its own five language folders too.

- **The two-byte `MBToUni.dat`.** The Polish and Korean tables are not single-byte: the header is a lead-byte count (1 and 67), the last byte (0xFF) and
  the last lead byte's trail count (75 and 252). A byte below the lead bytes is a character, a lead byte plus a trail byte (1..255) is another, in table order
  (329 and 17,270 characters; Korean 188 single bytes, 66 lead bytes with 255 trails and one with 252). The layout rests on the data: all 2,365 Korean and 2,358 Polish strings decode with no
  unknown code, and `UniToMB.dat` stores the same lead/trail pair for all 17,270 Korean characters. `BFMUReader.Decode`, `StringFile`, `GameTextCodec` and the KEYBOARD
  letters (two-byte even for ASCII) follow it; the English, Danish, Dutch, French, German and Swedish tables decode as before.
- **Spanish.** A single-byte table (248 characters): string tables, fonts, shortcuts, speech, lip-sync and banners read like the other languages; the 21 tables decode
  to 2,358 entries with `¡ ¿ ñ` and the accented vowels.
- **Korean fonts.** 32 BF4 fonts of 17,270 glyph records each (the glyph limit was 4,096); `GAMEBOLD`/`MATISSE` are absent and the SESH, CASH, CONSOLE and DATE fonts draw Latin
  only, so `UiFonts` uses the label font for a role font that lacks the language's glyphs (`EXT:ui-font-korean-*`); the language row shows `한국어` only when the font in use draws it.
  The front-end smoke passes in Korean and in Spanish with every screen read back from the GPU; captures show Hangul, not boxes.
- **Korean signs.** Every sign names `gulim.ttc` (a TrueType collection of Gulim, GulimChe, Dotum, DotumChe, 12.7 MB) with a code page 949 face name. `TrueTypeFont.ParseCollection` and
  `SignFontLibrary.Find(file, face)` read it and `SignFile` accepts `.ttc`; a Hangul sign draws. tp.exe and the shipped usp10.dll, read statically: UTF-16 text, `SSA_GLYPHS` only
  (no font fallback), Hangul on the generic Latin shaper and placer, integer GDI advances (`APPROX:COMPAT-033`).
- **CD detection.** `GameEditions.Detect` names a single-language CD's language from its autorun cabinet (`spanish.tre`, `Korean.tre`), the language folder beside `Data`
  or a lone locale file; such a CD works as the game folder (its own language is the default) and, root or `Data`, as an overlay over another game.
- **OpenTPW's own texts.** All 151 `OpenTpwText` labels, the 64 online labels and the four account e-mails are written in Spanish, Korean and Polish (the PR lists the terms to check).
- **Theme Park Inc.** `--language Dutch` (or French, German, Swedish) reads TPI's own tables for the slice: its text HUD (35 new `Tpi*` labels in nine languages), its object names, and the names
  of the objects the cross-edition mod brings into Theme Park World. The slice has no advisor, so there is no advisor text. A Dutch capture of the science world is in the PR.
- **Checks.** Synthetic tests for both layouts, the codec, CD detection and the TrueType collection; data tests gated on `OPENTPW_ES_PATH`, `OPENTPW_KR_PATH`, `OPENTPW_PL_PATH` and
  `OPENTPW_TPI_PATH`. No CD data, font or game file is committed.
