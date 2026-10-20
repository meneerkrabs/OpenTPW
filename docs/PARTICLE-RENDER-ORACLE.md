# Synthetic particle framebuffer diagnostic

This opt-in diagnostic exercises the actual `ParticleRenderer`, white-block atlas,
world target, normal 4x MSAA resolve and output composition in the browser. It needs
no original game files. The browser pin build alone includes its exports; ordinary
published builds exclude them.

## Run and interpret

Build with `OpenTpwDeterminismPins=true`, then run:

```sh
node --experimental-websocket tools/web-particle-render/run.mjs out/web/wwwroot \
  --chrome /usr/bin/google-chrome --report particle-render.json
```

Alternatively manually dispatch **Browser determinism** with
`particle_render_oracle=true`. The default is false; the existing pin classes,
required checks and 60-minute job budget are unchanged. Both runtime legs use their
already-built page and existing Chrome infrastructure. The extra step has a
five-minute limit. Its JSON is retained alongside the ordinary pin report.

**The pre-correction baseline is expected to fail.** Exact head `0fc8390` was
measured in interpreter and AOT: 17 cases, zero setup/readback errors and the same
five opacity/depth mismatches. The bounded runtime correction now targets those
failures; it is not a rendered pass until fresh exact-head execution proves it. The strict diagnostic exits nonzero
on any contract mismatch or setup/readback failure; it has no measure/pass override.
The artifact records actual pixels, frame/draw counts, mismatches and errors. A
source-reviewed fixture is not a rendered pass. Once a separately reviewed runtime
correction passes this diagnostic, making it an ordinary required check needs its
own review.

The pure acceptance-rule checks can run with `node tools/web-particle-render/assertions.test.mjs`. They verify blank-frame and wrong-colour/depth rejection; they do not execute a GPU.

## Controls

Synthetic particles have constant colours, no motion, fixed depth and zero sprite
frames. The real particle snapshot and emitter order are asserted before rendering.
World alpha 0/F0/FF should be opaque; EF remains fractional. Additive overrides,
near/far submission order, the no-depth-write flag, interleaved material runs and
unchanged screen-space behaviour have separate controls. A visible red/blue opaque
control, black corner, exact draw counts and an advancing frame counter prevent a
blank or skipped frame from passing. The existing public PTCL-905 annotation is the
contract source. Textured page modes, alpha testing and full native parity remain
outside this diagnostic.

The readback occurs synchronously after the exported draw, before returning to the
browser event loop. It binds the default read framebuffer, samples a 3x3 interior
block and background corner, then restores the previous read binding. It checks
context availability, framebuffer completeness and GL errors. Context options and
`preserveDrawingBuffer` are unchanged. RGB comparisons allow two byte levels for
rounding; controls are compared in the presented colour space.

A fresh page without a running game is required. The fixture restores camera
matrices and its temporary render callback, deletes only its particle entities and
their owned resources, and drains their deletion queue after device idle. The
harness closes the isolated page/browser, which releases remaining renderer and
context resources. No game storage or original media are read or uploaded.


## Bounded runtime correction

Only untextured, non-additive world particles with alpha 0 or >=0xF0 select opaque
blending, with depth writes unless flag 0x4000 is set. This follows the existing
public native contract (0x57C6FA–0x57C71B, additive override 0x57C7C8, no-write
0x57C7F4). Adjacent complete-state runs preserve original submission order and
world evening tint. New opaque materials retain normal depth testing and owned
resource cleanup. Textured, fractional-alpha, additive and screen paths retain
their previous behavior; no global depth-write or alpha-test switch is introduced.

Opaque vertex alpha is set to 255 specifically because the host's final world
composition alpha-blends its render target. This preserves opaque RGB through that
host stage; it is not evidence of a native alpha-byte rewrite. PTCL-905 remains
open for textured page modes, alpha thresholds, additive depth, point filtering,
and screen/native pass semantics. No original-media parity is claimed.

The initial harness execution failed before readback because burst creation leaves
culling bounds empty. The fixture now performs the normal startup-skip tick and
one update tick, then asserts populated bounds and the unchanged particle snapshot.
Constant palette, equal birth/death sizes and zero motion preserve its inputs.
