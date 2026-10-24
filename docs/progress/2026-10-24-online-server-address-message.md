## The login dialog reports a missing server address in the game's language (October 24, 2026)

With the server address field empty, logging in showed the .NET message "Invalid URI: The URI is empty." in English in the login dialog's status line and in the lobby's status at the bottom right, whatever the game language.

- **The address is checked before connecting** (`OnlineSession.ParseServerUrl`, used by `UseServer`):
  - An empty field says "Enter the server address." and does not connect. The login dialog then puts the remembered server, or the browser build's own address (`SuggestedServerUrl`), back in the field, so the next try logs in there. It never logs in to a default by itself: the player may have cleared the field on purpose, and logging in sends a password.
  - A malformed address, or one `OnlineClient.ValidateServerUrl` refuses (no http/https, credentials, query or fragment, http:// to a public host), says "Enter a server address in the form https://server.example/ (http:// only for a server on this computer or the local network)."
  - Both are new OpenTPW labels (`OnlineLabel.EnterServerAddress`, `OnlineLabel.InvalidServerAddress`) in all nine languages of the online tables, with plain apostrophes in French. The same check covers "Forgot password?" and the developer panel.
- **Connection failures no longer append the .NET reason.**
  - A failed or timed-out request, or a response that broke off (`HttpRequestException`, `TaskCanceledException`, `HttpIOException`), shows only the original "Couldn't connect to server" error (`ERRORMSG.str` 79). Before, the reason was appended in brackets, and a timeout showed "A task was canceled."
  - A chat connection that fails on the network shows the original "Couldn't connect to chat server" (`ERRORMSG.str` 69), which OpenTPW had defined but never used. Before, it showed the raw socket message, or the general connection error with its reason.
  - The .NET reason goes to the log at info level.

**Verified:** `OnlineServerAddressTests` (new): the labels in every language, the refused addresses, an empty address with a default server offered, and a Dutch login dialog with an empty address and with `http://example.com`, whose status and the lobby's show the Dutch text and whose emptied field gets the offered server back. With the game data, 953 online, chat, co-op, postcard, presence and language tests pass (28 skipped need the language CDs); the native smokes pass.

Still open:
- `OnlineException` texts are shown as they are: errors the server sends in its own words, and the client's English text for a chat socket the server refused (401).
- File errors while exporting or importing a park (`IOException`, `UnauthorizedAccessException`) still show the system's message.
