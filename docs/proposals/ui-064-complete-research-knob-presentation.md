# UI-064: research workload control and capacity display

## Implemented scope, with one startup residual

This bounded TPW implementation replaces the original proposal's unresolved input and display questions with traced behavior and focused regressions. It retains UI-064 for the raw-zero startup geometry described below. Independent source review and exact-head CI must pass before merge. No original executable was run and no original-runtime or rendered-pixel equivalence is claimed.

## Input and state contract

The slider constructor initializes a one-unit step (`0x66ADC9`); the research builder sets range 0..1023 without overriding it. A qualified completed track click, not button-down, adds or subtracts one raw unit. Release dispatch retains the original press recipient (`0x658B84`), and the generic click qualification reconstructs the saved press coordinates (`0x65F927`–`0x65F99E`). The slider tests the narrower track rectangle and compares the saved Y with the current ball bottom (`0x66B8AA`). Equality selects the upward step. Ball-child priority remains separate from track eligibility.

Raw value stays separate from rounded workload. Opening and changed summed researcher ability reproject workload through the setter; ordinary frames and category/target changes do not. Keyboard workload adjustment explicitly synchronizes raw value as the existing host extension.

Dragging uses the press-time ball rectangle and press pointer (`0x65CF3D`, `0x66BE55`), not accumulated clamped deltas. Staffing refresh can move the current ball while retaining that baseline. Only actual pointer movement recomputes the drag; a stationary held frame must not overwrite the refresh. Release normalizes the rectangle from raw value. The integer position rule remains `220 + trunc((1023-v)*414/1023)`.

The captured-click path is opt-in to this control. Ordinary widgets keep their click policy. Host removal, disabled/read-only transitions and lost capture cancel the interaction. Existing global click timing, slop, frame-hitch and double-click differences remain outside this slice.

## Display contract

Child `0xD2D11` is the existing cameo, with frame `trunc(2*(workload-70)/7)` (`0x4A9A5E`). It uses nine states, 0..8.

Child `0xD2D12` displays the staffing limit, not workload, in rectangle (354,238)–(422,683). The vertical gauge (`0x477870`, `0x477CC0`, `0x477D20`) partitions its two quads at `683 - 445*clamp(limit,0,1024)/1024`. The original texture's left half is filled and right half empty, verified by private static decoding and inspection. Geometry and UVs are cropped together, retaining upright texture orientation. No original images or asset bytes are included.

## Regression requirements

- Real-screen completed-click routing, saved point, narrower track, ball priority, release outside, movement/time cancellation and no repeated page during hold.
- Raw sub-workload accumulation, both overshoot/reversal bounds, release normalization, staffing changes during page/grab and stationary-frame behavior.
- Screen removal/reopen, disabled/read-only/lost capture, unchanged default-widget routing and no stale or duplicate callback.
- Keyboard synchronization and both hum parameter bindings.
- Cameo state boundaries and actual gauge vertices/UVs at empty, partial and full capacity.

## Explicit residual and verification boundary

An unchanged initial raw-zero SetPos can leave the authored table ball position in the bounded native construction path. Later indirect/queued initialization has not been exhaustively resolved. The implementation preserves OpenTPW's existing normalized bottom position for this edge; it does not substitute an unproven table position or retire UI-064.

Local synthetic source checks and tests are recorded in the progress fragment and PR. The local environment lacks a .NET SDK, so C# execution must be established by the exact published-head CI. Static texture inspection and CPU draw assertions do not establish original display equivalence; generic model transform, depth and filtering qualifications remain separate.
