## Browser audio startup uses the guarded legacy settings reader (October 18, 2026)

The optional reverb-map lookup added by the audio update read the desktop
`Settings.Default.GamePath` while starting the lobby sound. In WebAssembly that
initializes an unsupported configuration system and stops startup with
"Could not start: Configuration system failed to initialize".

Audio now reads the existing `LegacySettings.GamePath` accessor once, only when
no reverb map is already loaded. It returns no path in the browser, so the
optional map stays unavailable and the rest of the audio initialization continues.
Desktop installations retain the map lookup; unreadable legacy configuration is
handled by the existing guarded reader. No saves or stored settings are removed.

The browser startup tests include a source-contract regression for this access.
That check and the Web build do not by themselves qualify actual browser gameplay.
