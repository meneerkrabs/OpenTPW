## Buy window: objects from outside the game data get their preview model (October 17, 2026)

The buy window's preview frame stayed black for the pools extension's objects (pool lamp, underwater lamp, round fountain, staff room, stepping stone, rope swing, water jets, whirlpool and the rest), and for every other object that does not come from the game data (data mods, bonus content). When mod objects were added, `OriginalBuildCatalog` gave such an object no preview model at all, because the HUD read models and textures only from the game's own file system.

- `OriginalBuildCatalog` now gives every object its preview model (`P<name>.MD2`, or the main model), wherever it comes from.
- `ParkHud.GetIcon` reads an external object's model from its own root (`ObjectAssets.LoadModel( entry.FileSystem, … )`) and takes the textures of its archive as run-time textures, decoded from that root the way the park draws the object (`OriginalObject.TryLoadTexture`); game-data textures (a theme's `sharetex`) still go through the path resolver.
- Tests: `PoolsContentTests.EveryPoolObjectInTheBuyWindowHasAPreviewModelFromItsOwnRoot` (every listed pool object in all four themes has a model that builds a preview with every texture found), and `OriginalUiAssetTests.BuildMenuContainsEveryBuildableThemeObjectWithItsOriginalData` now expects a preview model for bonus objects as well.
