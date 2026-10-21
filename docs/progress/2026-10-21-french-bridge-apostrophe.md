## Pool bridge help: a plain apostrophe the French fonts can draw (October 21, 2026)

The localized pool bridge strings added on October 18 used the typographic apostrophe (U+2019) in their French text. The game's French UI fonts (for example TITLESMALL.bf4) have no glyph for it, so the with-data test `EveryUiStringHasGlyphsInEveryUiFontOfItsLanguage(French)` failed and the help line would have shown a gap. All four now use the plain apostrophe, which every shipped font has.
