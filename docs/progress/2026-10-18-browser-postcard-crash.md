## Sending a postcard no longer stops the browser game (October 18, 2026)

- **Bug.** In the browser, pressing Send on a postcard stopped the game with "`NormalizationForm.FormKC` and `NormalizationForm.FormKD` are not supported in browser environments or WebAssembly": the postcard checks its recipients for duplicates with `OnlineText.NormalizeName`, which folded names with compatibility normalization (FormKC).
- **Fix.** The browser client folds with FormC, which WebAssembly supports; desktop clients and the server keep FormKC, and the server stays the authority for player identities.
