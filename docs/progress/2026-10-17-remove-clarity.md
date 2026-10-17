## Clarity texture pack removed (October 17, 2026)

The Clarity pack (`clarity-enhanced`) is gone, on the project owner's decision. Removed: its distribution manifest and checksum, the in-game
download screen and installer (`TexturePackDownload`, `TexturePackDownloadScreen`), the browser's lazy texture delivery (`opentpw-textures.js`,
`Program.AddTextureFile` and friends, the CSP hosts for the texture CDN), the packaging, snapshot and static-page tools with their tests, the
publish workflow and the Dockerfile copy of the manifest. Clarity was the only downloadable pack, so no download system remains.
The options now offer Original, Enhanced (the former "Enhanced (legacy)", i.e. the `enhanced` pack the local builder makes, with the label and
help text updated in all nine languages), Detailed and other installed packs. A saved `clarity-enhanced` choice reads as Original, is logged once
and is written back (`GraphicsSettings.Load`); the HUD choice is kept. A local `<config>/texture-packs/clarity-enhanced` directory is listed like
any other pack. The shipped HD hero art and the HUD/icons option are unchanged. Docs rewritten; tests removed or adapted and a migration test added.
