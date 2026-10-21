## Windows build runs without administrator rights (October 21, 2026)

Found while reproducing issue #754 in a Windows VM: `OpenTPW.exe` asked for administrator rights at every start (`requireAdministrator` in `source/OpenTPW/app.manifest`, inherited from the original upstream project). Nothing in OpenTPW needs them: it only reads the game folder and the registry and writes its settings, online files and fallback saves under the user's AppData.

- **What.** The manifest now requests `asInvoker`, so the game starts without a UAC prompt; a test guards the level.
- **Saves.** Without elevation a game folder under Program Files is usually read-only, even when its `save` folder already exists. The save folder choice now writes and deletes a probe file instead of only creating the directory, so such a folder falls back to the settings folder's `save` before the first save instead of failing at it. Unit tests cover a writable folder (saves stay beside the game, no probe left behind) and an existing read-only one.
