## Blueprint object refusal evidence corrected (October 18, 2026)

Static inspection of the complete ordinary-object hover/check/ghost route disproved the proposed independent-cell colour change.
The hover first calls placement in check-only mode; any refused cell makes that call return zero, which sets the global force-red
flag before the immediate ghost pass. This applies even to the NoModify fixed-path return that does not set the validator's sticky
refusal. The Move hover uses the same two-pass pattern. The all-red ordinary footprint is therefore retained.

The BLUEPRINT-003 source annotation and register now describe that caller evidence instead of treating independent per-cell
colouring as a missing feature. The ID remains open for genuine residuals, including refused outside-access markers, special
shape branches, track markers, arrow models, the queue-option marker and the fence ghost. See the
[caller and return-chain evidence](../reverse/BLUEPRINT.md#object-preflight-refusal-correction-october-18-2026).

There is no runtime behavior change. The existing refused-footprint regression is extended to every rotation. Fidelity/progress
checks and their Python suites pass; the focused .NET test command was attempted but cannot run locally because the SDK is absent
(`dotnet: command not found`). Original software was not executed, and no original assets or disassembly are included.
