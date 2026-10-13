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

The future placed-object hook can retain one resolver for `Runtime.Model` and
call:

```csharp
resolver.Resolve(key, ParticleKeyResolver.EventSelector,
    placedObject.NodeTransforms, placedObject.Placement, includeDirection: true);
```

The caller must select the intended pose at the event boundary. `PTCL-900`
records that the native lookup-table refresh gates and callback timing are still
unqualified. No runtime hook is installed by this change. EVENT classification
and dispatch should be reviewed independently before connecting a constructor.

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
