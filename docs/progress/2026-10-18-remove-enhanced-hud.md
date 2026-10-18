## Enhanced HUD/icons removed (October 18, 2026)

The "HUD and icons" choice and the redrawn interface art behind it are gone, on the project owner's decision (October 18, 2026): PR #395 fixes the
WCT planar-row decoder, so the original icons now decode crisply and the 55 AI-redrawn buttons are no longer needed. Removed: `content/hero-art`,
its tools (prompts, fit.py, keyfill.py and the predecoder), `PredecodedUiArt` and its tests, the `EnhancedInterface` setting and options row
(with its nine-language strings), `TexturePackSwitch`'s interface switch, the browser build's predecoded staging target, the Dockerfile copies, the
`--texture-pack-hero-dir` builder option (`HeroTextures` in `pack.json`) and the `hero-art` folder link in the TPI smoke script. Old settings files
that still carry `EnhancedInterface` load as before; the key is ignored and not written back. Interface textures now come from the original game,
or from the selected world pack when it has them. The texture-pack extension itself (`[EXT:texture-pack]`, locally built packs) is unchanged.
Docs (TEXTURE-PACKS, UI, WEB, BROWSER-PERFORMANCE, README) updated; earlier measurements keep their numbers with a note that the art was later removed.
