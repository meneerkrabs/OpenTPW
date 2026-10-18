## Browser postcard sending preserves Unicode player identities (October 18, 2026)

Sending a postcard validated its recipients with .NET's compatibility normalization
(FormKC), which is unsupported in WebAssembly. The exception escaped the postcard
window and stopped the game's frame loop before the card reached the outbox.
The initial browser-only FormC workaround avoided the crash but did not recognize
all compatibility-equivalent recipients as duplicates.

The browser now supplies NFKC through JavaScript's Unicode normalization, after
.NET validates the input. Identity casing still uses .NET's ToUpperInvariant,
and desktop/server identities keep the existing algorithm. Compatibility-equivalent
names remain duplicates; account keys, sender checks and recipient routing are
unchanged. A platform-not-supported failure in the postcard window now shows the
existing failure dialog and retains the draft instead of stopping the game.

Regression coverage includes compatibility-equivalent recipients, accented names,
malformed Unicode, JavaScript module registration and draft preservation. The
asset-free browser smoke test exercises the actual WebAssembly postcard path;
it does not establish full gameplay with an original game installation.
