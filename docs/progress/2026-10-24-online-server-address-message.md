## The login dialog reports a missing server address in the game's language (October 24, 2026)

With the server address field empty, logging in showed the .NET message "Invalid URI: The URI is empty." in English in the login dialog's status line and in the lobby's status at the bottom right, whatever the game language.

- **The address is checked before connecting** (`OnlineSession.ParseServerUrl`, used by `UseServer`):
  - An empty field takes the default server when there is one (the browser build's own address, `SuggestedServerUrl`); otherwise the status says "Enter the server address."
  - A malformed address, or one `OnlineClient.ValidateServerUrl` refuses (no http/https, credentials, query or fragment, http:// to a public host), says "Enter a server address that starts with https:// (http:// only for a server on this computer or the local network)."
  - Both are new OpenTPW labels (`OnlineLabel.EnterServerAddress`, `OnlineLabel.InvalidServerAddress`) in all nine languages of the online tables, with plain apostrophes in French. The same check covers "Forgot password?" and the developer panel.
- **Connection failures no longer append the .NET reason.**
  - A failed or timed-out request (`HttpRequestException`, `TaskCanceledException`) shows only the original "Couldn't connect to server" error (`ERRORMSG.str` 79). Before, the reason was appended in brackets, and a timeout showed "A task was canceled."
  - A chat connection that fails on the network shows the original "Couldn't connect to chat server" (`ERRORMSG.str` 69), which OpenTPW had defined but never used. Before, it showed the raw socket message, or the general connection error with its reason.
  - The .NET reason goes to the log at info level.

**Verified:** `OnlineServerAddressTests` (new): the labels in every language, the refused addresses and the default server, and a Dutch login dialog with an empty address and with `http://example.com`, whose status and the lobby's show the Dutch text. 900 online, chat, co-op, postcard and presence tests pass with the game data; the native smokes pass.

Still open:
- Errors the server sends (`OnlineException`) are shown as the server words them.
- File errors while exporting or importing a park (`IOException`, `UnauthorizedAccessException`) still show the system's message.
