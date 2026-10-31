# TRACK-010: path memory is not an underlay (superseded)

This note audited, on 2026-10-22, how OpenTPW remembered the paths a track tile had cleared (the flag 0x20, `TrackBuilder.FlagOverPath`) and proposed an underlay to replace that memory. Round 11 settled it
the other way: the paths never leave the map layer. The type op of tp.exe never writes a cell's type (`[TPW-EXE:0x538FC0]`), so `TrackBuilder` keeps the path or queue a tile is laid over (only a line's last tile
removes the paths under it, `0x5390C0`) and the bridge ops give the tile its flag word (`TrackBridges`, `docs/TRACKS.md` "Bridges"). The tests of this note (`TrackBridgePathMemoryTests`, `TrackBridgeRouteTests`) are gone with the memory bit;
`TrackBridgeOpsTests`, `TrackBridgeRouteLevelTests` and the oracle golden `TrackBridgeWalkGoldenTests` replace them.
