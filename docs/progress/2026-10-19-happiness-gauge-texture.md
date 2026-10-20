## Happiness gauge uses the vertical meter art (October 19, 2026)

Replaced the HUD's synthetic red/yellow/green fill with the source-backed vertical meter:
its empty right image half and filled left image half are cropped upright at one shared boundary.
The shell and child now use the decoded control rectangles, with the existing bottom-left anchor
applied once. Null/zero draws the empty image, and image failures have no fabricated fallback.
The established two-second sampling and byte-mean mapping, including 50→511 and 100→1023, are unchanged.

The crop helper is shared with research while preserving its art, staffing-limit value, wrapper
and UI-064 state. Added synthetic C# tests for raw boundaries, actual HUD composition, mapping,
sampling cadence, read-only draws and missing/malformed resources; extended research regressions.
Python integration/math checks were run locally. The .NET SDK is unavailable here, so C# execution
and actual renderer captures remain pending exact-head CI and a supported render environment.

[UI-901](../proposals/ui-901-trace-happiness-gauge-model-response.md) remains open for rendered
shell/meter composition and alpha verification. No original executable was run, no original-runtime
pixel parity is claimed, and no original media is redistributed. Generic tooltip routing remains
outside this drawing change. The earlier plan-only progress entry is preserved as history.
