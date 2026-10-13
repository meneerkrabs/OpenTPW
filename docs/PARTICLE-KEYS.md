# Particle model keys

`ParticleKeyResolver` supplies the model-key boundary needed by EVENT1 and EVENT2
particle constructors. It does not construct, simulate or render particles. The
geometry reader currently accepts MD2 221.203 only.

For selector `0x100`, the first dummy attribute whose `Value` equals the
nonnegative key and whose `Type` intersects the selector selects node
`model.Counts.Unknown46 + attributeIndex`. Names are irrelevant. Attribute flags
`0x30` enable the node matrix; node flag `0x400` becomes the lookup direction
reversal flag `0x10`. These statements are static findings from the Feral PPC
loader and key resolver, not an original-runtime comparison.

Pass the object's current model-space hierarchy matrices, or explicitly its
rest matrices. Position is the selected matrix's translation. Direction starts
with its third basis row, is optionally reversed, then normalized. The helper
converts these to the shared engine convention: Z up, placed quarter-turn
rotation, grid anchor, grid origin, base height and `ObjectPlacement.ModelScale`.
`EngineUnitDirection` is unit length; native normalization's multiplier is
available as `NativeDirectionMultiplier` (`1024`). It is not an engine velocity.
Native fixed-point conversion, particle speed and template parameter handling
belong to the future constructor.

Unsupported selectors (including camera `0x40` and head `0x80`), negative keys,
missing keys, records without qualified matrices, and invalid transforms return
status values with no pose. In particular, a negative key follows native object
box midpoint and height logic that is not qualified here: model bounds must not
be substituted for that object box.

The owner-bound preparation hook retains one resolver for `Runtime.Model` and
calls:

```csharp
resolver.Resolve(key, ParticleKeyResolver.EventSelector,
    placedObject.NodeTransforms, placedObject.Placement, includeDirection: true);
```

The caller must select the intended pose at the event boundary. `PTCL-900`
records that the native lookup-table refresh gates and callback timing are still
unqualified. The resolver itself supplies no particle constructor. The preparation
hook below preserves the independently reviewed EVENT classification.

## Latest owner-local EVENT preparation

`OriginalObjectRuntime.LastParticleEventAttempt` exposes a bounded immutable
diagnostic from actual EVENT types 1/2. The placed-object constructor binds a
copy of its placement; each instruction resolves against the runtime's current
animator matrices. A prepared request records owner attraction ID, calling
script ID, resolved key/effect operands, implicit EVENT control 1000, and frozen
engine position (plus unit direction only for type 2). Direct IDs 0–104 are the
qualified range, not proof that a template exists or a particle was allocated.

There is only one latest attempt: later type-1/2 instructions replace it,
including refusals. Other EVENT types leave it alone. There are no callbacks,
queues, delivery acknowledgements, generation handles or renderer/physics
resources. IDs are local metadata in the context of that runtime/world, not
globally unique handles. Copied values are historical snapshots; they do not
follow subsequent animation or confer an active particle identity.

Headless runtimes, unresolved/negative keys, invalid transforms, scoped effect
bit `0x8000` and unsupported direct IDs produce no request/pose. Stop/removal
clears the placement, cached resolver and latest attempt, and a stopped owner
does not create new diagnostics. Children share their owning runtime's effects.
The diagnostic is neither save state nor canonical simulation state.

Every EVENT keeps its existing unimplemented return/count/log behavior, even
when preparation succeeds. This is a polling preparation foundation (PTCL-901),
not particle execution, reliable event delivery, runtime coverage completion or
original parity. Native fixed-point position/velocity, template contents,
scoped lookup, lifetimes, renderer and refresh timing remain unqualified.
Synthetic tests exercise actual VM/object effects, animated poses, independent
owners/children, explicit refusals and teardown, with real sound controls and
zero particle PCM; they create no GPU resources or original assets.
Independent review also exercises variable-operand copies, sound-child ownership,
explicit Stop clearing, other-owner isolation, and exclusion from runtime
snapshots and canonical effect state. These are managed diagnostic controls;
they add no native execution or event-delivery qualification.
The combined runtime also has an actual root-fault control: Stop clears its
diagnostic, placement/resolver and looping EVENT voice while another owner
retains its diagnostic and audible loop. Stopped capture becomes eligible; the
other live loop retains its existing capture refusal. Sound-route fixtures use
a listener-local source without changing the global listener.

Verification:

```sh
OPENTPW_GAME_PATH=/path/to/theme-park-world dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --filter FullyQualifiedName~ParticleKeyResolverTests
OPENTPW_PPC_BIN_ROOT=/path/to/mac-feral/bin python3 -m unittest discover -s tools/ppc-analysis/lanes/advisor -p test_particle_key_evidence.py -v
python3 tools/fidelity_register.py --check
```

Tests cover placed rotations, direction reversal and normalization, current
animation versus retained bind pose, explicit refusals, and shipped ride model
metadata with a moving animation witness. The identity-pinned checker reads
selected operands/constants from the supplied original binary; it executes no
original instructions and emits no disassembly or binary copy.

Independent review (2026-10-13) added separate synthetic cases for a malformed
first duplicate, a same-value camera entry before a particle entry, independent
numeric placement/direction witnesses, zero/NaN/infinite/overflowing directions,
non-affine frames, nonfinite placement, input-stream lifetime and result snapshots.
The resolver refuses grid anchors whose shared placement calculation (`X * 10`,
`Y * 10` in integer arithmetic) would overflow, before producing any pose.

The reviewed resolver suite passes 13 synthetic cases; the build succeeds with
existing repository warnings. Fidelity validation and its 11 tests pass. The
shipped-model case was excluded and the static identity checker was skipped:
reads of the private USBSSD asset volume were blocked. Those checks remain
required qualification, alongside the documented native refresh/event timing;
this review does not establish original-runtime particle behavior.
