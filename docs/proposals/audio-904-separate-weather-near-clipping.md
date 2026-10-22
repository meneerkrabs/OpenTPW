# AUDIO-904: Separate weather near clipping

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
the rain field lives within +-0.8 engine units of the eye, so the scene camera's 0.1 near plane (0.5 original units; kept for the weather as [EXT:weather-near-plane]) clips the nearest drops where the original's near plane is 0.1 original units (0x54e86f and the camera init 0x42a990 -> 0x578be0: near 0.1, far 1000, field of view 90) and its polygon clipper (clip codes 0x561bc0, clipping 0x562a70 in 0x565bb0) cuts the streaks there, drawing only drops in front of the eye (0x57f1f0)

## Source inspection
- `source/OpenTPW/World/Weather/WeatherRenderer.cs:20`: 22: public sealed class WeatherRenderer : Entity; 23: {; 25: internal static readonly string[] ImagePaths = { "/generic/weather/raindrop.tga", "/generic/weather/snow.tga", "/generic/weather/lightning.tga" };; 27: private const int VerticesPerQuad = 6;

## Bounded work
Specify original weather near plane and clipping versus intentional scene-camera near-plane extension.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Compare original clipper and port projection units; choose compatibility behavior without silently altering main camera.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Drop streak crossing 0.1 original-unit near plane clips continuously rather than vanishing whole.
2. Behind-eye drops remain absent; world terrain near-plane behavior stays unchanged.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

## Public-source numerical addendum (2026-10-22; not runtime acceptance)

This addendum supersedes only the old snapshot/PR mapping for this investigation: existing draft PR #565 is `proposal/approx-audio-904` at `c0249bd6abf4e3b03d164a9c3313efd8c3752727`; public main inspected here is `807dbc34ab0b5502ff39d5a0715ea82afdfef700`. The original proposal above is retained verbatim. No original executable, game image, GPU, browser or .NET test was run. Calculations below are ideal real-number consequences of public source, not float32 rasterization or native fidelity evidence. Keep this proposal draft and AUDIO-904 unresolved.

### Verified coordinate and depth chain

Pinned public-main sources (paths below are relative to that full commit):
- `source/OpenTPW/World/Weather/WeatherRenderer.cs`: `WriteRain` output is converted to `(x,y,-z)*scale`, then drawn with identity view and `Camera.ProjMatrix`. Bolts use world positions and the scene view instead. Do not infer identical bolt geometry from the rain calculation.
- `source/OpenTPW/World/Original/OriginalParkPlacement.cs` -> `PrototypeRide.cs` -> `Objects/OriginalObject.cs`: the park scale resolves to `ObjectPlacement.ModelScale = 0.2f`.
- `source/OpenTPW/Client/GameFlow.cs`: lobby constructs weather with `engineUnitsPerOriginal: 1f`.
- `source/OpenTPW/World/Camera.cs` and `OriginalProjection.cs`: both projection branches use near `0.1f`, far `10000f` in engine units. The original-projection branch changes the x/y field of view, not near/far. At nominal 90 degrees it uses half-height 0.5, unlike the standard branch's half-height 1. The old native near/far comments (0.1/1000 original units) are inherited annotations, not revalidated native evidence here.
- `content/shaders/weather.shader`: GLSL computes projection * view * position. The System.Numerics row-vector matrix uploaded as raw uniform bytes is read as a column-major GLSL matrix, so the effective multiplication is its transpose, not a second camera transform.
- `source/OpenTPW/Client/Renderer.cs`: raw uniform upload, depth viewport 0..1, clear depth 1, preferred zero-to-one depth range. `content/shaders/web/weather.json` explicitly performs `gl_Position.z = 2.0 * gl_Position.z - gl_Position.w`, converting zero-to-one clip z to WebGL's minus-one-to-one clip interval; its y inversion does not alter depth. This is source verification, not confirmation of a running desktop backend.
- `source/OpenTPW/Render/Assets/Material.cs`: weather depth test is Less; depth clipping enabled. Rain/bolt disable depth writes, snow does not (`WeatherRenderer` flags). Existing `WeatherVisualTests.cs` exercise CPU weather generation, not the full shader/clip/depth pipeline.

Let z > 0 be a rain vertex's original camera-space depth, s the host scale, d=s*z, n=0.1, f=10000. The perspective matrix's row-vector z/w entries are M33=f/(n-f), M34=-1, M43=n*f/(n-f), M44=0. Consequently clip w=d, clip z=f*d/(f-n)-f*n/(f-n), and normalized depth D(d)=f/(f-n)-f*n/((f-n)*d). The near/far inequalities 0 <= clip z <= w give n <= d <= f. WebGL's z conversion produces the same window depth after (z_ndc+1)/2.

Thus the park clips at z=0.5 original units, lobby at z=0.1. Effective far distances are respectively 50000 and 10000 original units, not the annotated native 1000. These are eye-plane depths, not Euclidean eye distance. For rain, x/y scale cancels with w, so the near discrepancy cannot be fixed by changing field of view.

| Original rain depth | Park d | Park D | Lobby D |
| --- | --- | --- | --- |
| 0.1 | 0.02 | -4.0000400004 (clipped) | 0 |
| 0.25 | 0.05 | -1.0000100001 (clipped) | 0.6000060001 |
| 0.5 | 0.1 | 0 | 0.8000080001 |
| 1 | 0.2 | 0.5000050001 | 0.9000090001 |

### Asset-free streak boundary fixtures

Parameterize a synthetic streak's original-space depth by z(t)=a+(b-a)t, 0<=t<=1, holding all points inside the side frustum. These are analytical segments representing the two equal-depth ends of the rain quad, not claims that an existing deterministic drop seed produces them. Intersection is t=(near-a)/(b-a), applied to geometry and all varyings before perspective-correct raster interpolation. Retained ranges:

| End depths a -> b | Near 0.1 (lobby / proposed original-near target) | Current park near 0.5 |
| --- | --- | --- |
| 0.05 -> 0.15 | [1/2,1] | none |
| 0.4 -> 0.6 | [0,1] | [1/2,1] |
| 0.05 -> 0.6 | [1/11,1] | [9/11,1] |
| 0.6 -> 0.05 | [0,10/11] | [0,2/11] |
| 0.15 -> 0.4 | [0,1] | none |

`WeatherVisualState.WriteRain` rejects the entire streak if either leading or trailing Z <= 0. Therefore -0.05 -> 0.6 and 0 -> 0.6 never reach either near clipper, regardless of the retained positive interval a purely geometric clipper would have produced. Positive endpoints straddling 0.1 or 0.5 are submitted and clipped, not CPU-rejected. Exact equality is a mathematical boundary, not a guarantee of nonzero pixel coverage or float32 equality. The inherited original front-of-eye rule must be checked separately before changing this CPU gate.

Snow is explicitly outside the raw-rain-depth table: `WriteSnow` first rejects position.Z <= 0, then emits centre.Z = position.Z + SnowProjectionBias (0.1501) and a depth-dependent x/y spread. The park's emitted-depth threshold corresponds algebraically to raw centre depth 0.3499, while lobby's -0.0501 threshold is superseded by its CPU positive-depth gate. This is not a snow-parity conclusion; preserve AUDIO-901/#554 ownership and evidence gates.

### Why an independent weather near plane is not sufficient

For park's 0.1-original-unit target, a hypothetical weather near is nw=0.02 engine units. Holding f=10000 and x/y terms unchanged preserves projected x/y, but changes depth to Dw=Aw-Bw/d, where Aw=f/(f-nw), Bw=f*nw/(f-nw). With scene coefficients A=f/(f-n), B=f*n/(f-n), exact remapping is Ds=(B/Bw)*Dw + A-(B/Bw)*Aw = (499999/99999)*Dw - 400000/99999.

Counterexample under the current Less test: weather at d=0.5 is nearer than a scene surface at d=1. Correct depths are 0.80000800008 < 0.90000900009, so weather passes. The independent weather projection gives 0.96000192000, so it incorrectly fails against that same scene buffer. Comparing two differently parameterized depth buffers directly is not occlusion preservation.

For d>=0.1, the affine remap restores identical ideal scene depth (finite precision still needs testing). For the newly visible band 0.02<=d<0.1 it returns negative scene depth, e.g. -4.0000400004 at d=0.02. Applying that remap to vertex clip z restores the old near clip and loses the very band being added. A fragment-depth remap is later than clipping, but negative depth is not a portable ordinary stored depth; clamping to zero collapses all ordering in that band. At scene depth zero it also fails strict Less equality. Snow writes would additionally destroy near-band snow ordering. Do not label a clamp a full parity fix.

Design choices requiring the rendering owner's compatibility decision:
1. Retain current scene projection and document the park extension accurately, with lobby distinguished. No parity claim.
2. Separate clipping from depth comparison: retain small-near geometry, compare original eye depth against a reconstructed/copied scene depth in a dedicated pass (or remap only the shared valid range), and define the newly visible band's ordering, depth writes, multisample handling and equality behavior explicitly. An isolated synthetic render test must prove existing scene occlusion is unchanged. It cannot recover scene geometry already clipped by the unchanged camera; this limitation must be accepted rather than hidden.
3. Change the whole scene near to 0.02 only as a separately reviewed camera/compatibility change. With fixed f, the ideal depth slope B/d^2 becomes approximately one fifth at shared d, so fixed depth steps imply approximately five times worse distance resolution. This is a precision trade-off, not an authorized default fix.

### Reproduction and remaining acceptance

The research package's standalone Python 3 model uses only `fractions` and `json`: evaluate the matrix equations above at original z=[0.05,0.1,0.25,0.49,0.5,0.51,1,4], s=[0.2,1], and the seven streak pairs (five table rows plus the two CPU-rejected eye-boundary cases). Evaluate the affine remap at engine d=[0.02,0.05,0.1,0.15,0.2,1,10000]. Exact rational assertions check clip-z/w, WebGL conversion, remap identity, intersections 1/11 and 9/11, and the occlusion counterexample. The locally executed Python model passed these assertions; it is not a repository runtime test and no local artifact path is required to understand/reproduce the equations here.

Before any implementation: assign/coordinate render ownership; choose compatibility semantics; preserve unchanged scene-camera behavior unless separately approved; test forward/reverse boundary crossings, alpha/UV interpolation, side and far clipping, occluders in front/behind/equal depth, rain no-write and snow write interactions, multisampling, and desktop/browser depth conventions on the actual render path. Original clipper/reference capture remains missing. No annotation retirement, register completion, runtime mutation, or merge-readiness is established by this addendum.
