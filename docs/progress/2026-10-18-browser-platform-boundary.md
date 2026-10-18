## Browser settings boundary and canonical text regressions (October 18, 2026)

The browser no longer compiles the desktop user.config settings, their GameDir helper or the unused
legacy cursor, and drops its ConfigurationManager package reference. Its LegacySettings replacement
returns no legacy game path; shared audio and game-files code keeps the existing fallback. Desktop
sources are unchanged. FormD object-name matching and FormC mod path keys retain their existing
semantics. Asset-free WASM smoke checks now verify that boundary, canonical accent/split-name
matching and compatibility distinctions, twice with networking disabled after boot, alongside the
postcard checks. CPU tests pin the same object-name normalization rules. See [WEB](../WEB.md).
