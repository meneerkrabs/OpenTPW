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
World alpha 0/F0/FF is the opaque state 0x1256; EF is the blended state 0x556 (alpha
test at 0x10, depth writes) and stays fractional. Additive overrides (0x200552, 0x200522
with the alpha test at 0x10: an alpha-0 additive particle is invisible), near/far
submission order, the no-depth-write flag, interleaved material runs and
screen-space particles (an untextured screen-space particle of alpha 0 or >= 0xF0 is
opaque too, 0x57C6FA) have separate controls. A visible red/blue opaque
control, black corner, exact draw counts and an advancing frame counter prevent a
blank or skipped frame from passing. The harness draws outside a level's translucent
pass, so every quad is drawn in the original's submission order; the deferred list
(`WorldPass.SubmitParticle`) is covered by unit tests. The readback occurs
synchronously after the exported draw, before returning to the
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


## Runtime state words

`ParticleState` ports the state word of a particle's batch (docs/PARTICLES.md,
"Drawing"; PTCL-905 narrowed in round 11). Opaque states keep depth testing and
write depth unless flag 0x4000 adds 0x800; the blended states alpha-test at 0x10 and
write depth. Adjacent complete-state runs preserve original submission order and
world evening tint. Opaque vertex alpha is set to 255 specifically because the host's
final world composition alpha-blends its render target. This preserves opaque RGB
through that host stage; it is not evidence of a native alpha-byte rewrite. No
original-media parity is claimed.
