## The Publish Park form shows its check button (October 24, 2026)

The Publish Park form, opened from the pause menu, had no visible submit button. Its check button had no model, so it drew nothing but a faint label, and the close button's "Back" label was clipped over its round model. OpenTPW's website toggle and export button sat below the window and covered the pause menu's lines behind it.

- **Check and close buttons.** The check button (-1) now uses the original `b_okay` model and the close button (-2) the original `b_exit` model, both without a text label, as in the postcard window and the other tables of this window family (for example the load dialog, table 0x7523f0, at the same place). The check button's hover help is "Publish Park"; the close button's is the original help 2.
- **OpenTPW's own controls.** The website toggle and the export button now sit in the strip of the window's green area below the description field, side by side. The status line moved to the free column left of the fields. No control covers an original one or leaves the window.
- **Refused parks.** A park that records mods still hides both buttons; the status column now says why (the existing refusal text) instead of showing nothing.

**Verified:** the front-end smoke test opens the form from the park (English and Dutch captures), checks that the check, close, website and export controls lie inside the window and that the check button has a model; a unit test checks the layout constants.
