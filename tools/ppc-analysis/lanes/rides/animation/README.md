# Bounded animation scalar helper

`OriginalAnimationState.cs` is a standalone C# evidence helper, with only .NET
framework dependencies. It is not linked into the game, and does not implement
an animator, model decoder, controller, or pose state machine.

```sh
dotnet run --project tools/ppc-analysis/lanes/rides/animation/AnimationWitness.csproj
dotnet run --configuration Release --project tools/ppc-analysis/lanes/rides/animation/AnimationWitness.csproj
python3 tools/ppc-analysis/lanes/rides/animation_evidence.py /path/to/SimThemePark.data
```

The executable input is SHA-qualified to the Feral Mac application. Proof
comes from rides `51425bd`/`15313d2`, clock `126ee3e`/`3b9b322`, independent
review `d0e5c75`, and the additional `animation_evidence.py` checks. Original
instructions are never executed or stored here. C# tests contain synthetic
inputs only. The corpus tool separately records PC member/script hashes and
interpreted selectors; those do not establish Windows runtime equivalence.

Supported scalar facts:

- Category0..11 maps to normalized suffixes `c,d,i,l,s,m,e,u,w,b,r,o`.
  Variant v requests numbered suffix v+1; unnumbered retry applies to the first
  variant only. Metadata is case folded; the helper does not establish native
  path/case lookup or load a clip.
- Default channel is0, record stride56. Explicit channel0 shares that index.
- Flag0x40 selects supplied unscaled milliseconds; otherwise supplied scaled
  milliseconds. The caller supplies already-qualified clocks and valid model
  update preconditions; the helper does not infer clocks from frame cadence.
- An unsigned elapsed millisecond value converts through an exact double
  representation to a single result. Multiply30, divide1000, and multiply
  channel speed each produce a single result. `FrameBeforeEnd` reports the
  intermediate values so operation order/rounding can be checked independently.
- GETANIM_CH returns current animation ID, or−1 under flag4. No-animation ID12
  is a query value, not a playing/not-playing boolean.
- Script speed is single-precision `0.5 + bias/100`. TRIGANIMSPEED forms single
  `script_speed * int32_operand / 1000`, stores an int16 copy separately, and
  uses the original int32 operand for its signed integer deadline divisor.
- Signed trigger cases16/19/21/23 use `max(native_return-300,300)`. The ordinary
  deadline divides the adjusted value by script speed as single precision,
  truncates to int32, and adds to the uint32 clock. The speed case instead
  divides `adjusted*1000` by the original per-mille operand. These methods
  require an actual native return; they do not synthesize a bare clip duration
  or implement TRIGWAITANIM's ID/deferred waiting protocol.

Unsupported boundaries return a reason and no value. `RequireValue()` throws
rather than manufacturing a default frame or duration. Those boundaries are
before-start/clock-wrap ambiguity, exact/late clip end, other channel flags,
idle or unknown category states, nonpositive/exceptional floating-point
inputs, signed deadline numerator overflow, and WAITANIM's different unsigned
conversion/clamp path. The native completion comparison is strictly greater
than total frames; the helper does not turn that observation into a guessed
last pose, completion transition, or loop reset. Normal/subnormal/exceptional
FPSCR configuration and original runtime rounding modes remain unqualified;
the ordinary finite .NET test domain uses nearest-even single arithmetic.

Eleven tests cover first frame, pre-start/wrap, exact/late end, clock flags,
channel bounds, queries, script/per-mille speed, clamp thresholds around600,
ordinary deadlines, int16 storage, and exact float bits after long gaps. Both
Debug and Release runs pass. The PC corpus corroborates13 Totem selectors
against actual MD2 member hashes, and four TRIGANIMSPEED calls with operands
4000/2000/4000/1800 across Fantasy/Hallow/Jungle/Space gates respectively.
Runtime mixing, vertex poses, stop/loop/end behavior and full animation/ride
fidelity remain outside this helper.
