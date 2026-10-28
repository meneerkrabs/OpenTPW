## Isolated synthetic path-render diagnostic preparation (October 27, 2026)

Prepared a default-off diagnostic that exercises real `OriginalTerrain`, same-size
texture-pack GPU replacement, procedural atlas refresh and the first suspended-camera
frame through the real `Level.Render` preparation/snapshot order. Its data is generated;
no original game media or copied game/save corpus is an input.

A separate optional artifact job leaves normal gates unchanged. It proposes four pinned
shader resources, a minimal loopback-only page, final published PE resource inspection,
SDK/package byte provenance and fail-closed allowlisted staging. It never uploads raw
publish output. Initial packaging is deliberately interpreter-only/untrimmed/PE, not AOT.

Source-only validation covers JavaScript observation assertions and synthetic artifact
parser/rejection tests. C# compilation, actual evaluated MSBuild output, final PE audit,
runnable artifact generation, browser rendering/PNGs and real-render mutation controls
have not run in this preparation. This does **not** close #883's rendered verification gap.

See `tools/web-path-render/README.md` for the exact coverage boundary and later authorized
build/render steps. No deployment or ordinary runtime packaging changes are proposed.
