using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

public sealed partial class OriginalObjectRuntime
{
	private ObjectPlacement? particlePlacement;
	private ParticleKeyResolver? particleKeys;
	/// <summary>The particle effects this object's ADDOBJ records hold, by the record's slot operand (tp.exe's object list at obj+0xB0, record +0x18), with their type (+8) and key (+0x10); KILLOBJ ends them.</summary>
	private readonly List<(int Slot, int Handle, int Type, int Key)> particleObjects = new();

	/// <summary>Only the latest EVENT1/2 attempt: polling diagnostics, not a retained queue or particle delivery interface.</summary>
	public ParticleEventAttempt? LastParticleEventAttempt { get; private set; }

	/// <summary>The latest ADDOBJ type 1/2 attempt, kept apart from <see cref="LastParticleEventAttempt"/>.</summary>
	public ParticleEventAttempt? LastParticleObjectAttempt { get; private set; }

	/// <summary>Raised for every prepared EVENT1/2 request (not for refusals), so a particle renderer can start the effect; null in headless hosts.</summary>
	internal static event Action<ParticleEventRequest>? ParticleRequested;

	/// <summary>Starts a prepared ADDOBJ request and returns its particle handle (0 or -1 when nothing started; every other value, negative generations included, is a handle); null in headless hosts.</summary>
	internal static Func<ParticleEventRequest, int>? ParticleStarter { get; set; }

	/// <summary>Ends the effect of a handle ADDOBJ returned (the pool's life -2: no death effect); null in headless hosts.</summary>
	internal static Action<int>? ParticleKiller { get; set; }

	/// <summary>Whether a handle still names a live emitter; true when the host has no pool to ask. Used to drop records of effects that ended.</summary>
	internal static Func<int, bool>? ParticleAlive { get; set; }

	/// <summary>Moves a kept ADDOBJ effect to its key's current pose (and, for type 2, its direction); null in headless hosts.</summary>
	internal static Action<int, ParticleFollow>? ParticleFollower { get; set; }

	internal void BindParticlePlacement(ObjectPlacement placement) => particlePlacement = placement;

	/// <summary>The effect a WALKOFF with walk action 2 creates: effect 0x13 at the slot's start node, fire and forget.</summary>
	// [BIN:TPW-EXE:0x5572EB WALKOFF action 2] the node is looked up with the class 0x800 (0x556b90, the key resolver of the EVENT effects) and 0x521E60 is called with effect 0x13 and the node's position, each coordinate through ftol and shifted left by 10; the handle is not kept
	internal ParticleEventRequest? PrepareWalkOffEffect( int startNode )
	{
		if ( stopped || particlePlacement is not { } placement )
			return null;
		particleKeys ??= new ParticleKeyResolver( Model );
		var pose = particleKeys.Resolve( startNode, ParticleKeyResolver.WalkSelector, KeyPose, placement );
		return pose.Status switch
		{
			ParticleKeyStatus.Resolved => new ParticleEventRequest( AttractionId, 0, 1, startNode, WalkOffEffectId, 1000, pose.EnginePosition!.Value, null, pose.AttributeIndex, pose.NodeIndex, WholeUnits: true ),
			ParticleKeyStatus.ResolvedAtWorldOrigin => new ParticleEventRequest( AttractionId, 0, 1, startNode, WalkOffEffectId, 1000, NVector3.Zero, null, pose.AttributeIndex, pose.NodeIndex, AtWorldOrigin: true ),
			_ => null
		};
	}

	internal const int WalkOffEffectId = 0x13;

	private void StartWalkOffEffect( int startNode )
	{
		if ( PrepareWalkOffEffect( startNode ) is { } request )
			ParticleStarter?.Invoke( request );
	}

	private ParticleEventAttempt PrepareParticle(int scriptId, int type, int key, int effect, int control)
	{
		ParticleEventAttempt Refuse(ParticleEventPreparationStatus status, ParticleKeyStatus? keyStatus = null) =>
			new(AttractionId, scriptId, type, key, effect, status, keyStatus, null);
		if ((effect & 0x8000) != 0) return Refuse(ParticleEventPreparationStatus.UnsupportedScopedEffect);
		if (effect < 0 || effect > 104) return Refuse(ParticleEventPreparationStatus.InvalidEffectId);
		if (particlePlacement is not { } placement) return Refuse(ParticleEventPreparationStatus.NoPlacement);
		if (key < 0)
		{
			// [BIN:TPW-EXE:0x556B90 negative key] a negative key (or an object without a model) is the footprint box's middle at the object's base height (0x466B70, as the EVENT sounds use it), and (0, 0, 0) when the object has no box; the direction output is not written, so a type-2 effect would take the dispatcher's unset buffer (0x5573D0 local_c)
			if (type == 2) return Refuse(ParticleEventPreparationStatus.UnresolvedKey);
			if (particleFootprint is not { } footprint) return Refuse(ParticleEventPreparationStatus.NoPlacement);
			return footprint() is { } box
				? new ParticleEventAttempt(AttractionId, scriptId, type, key, effect, ParticleEventPreparationStatus.Prepared, ParticleKeyStatus.Resolved,
					new ParticleEventRequest(AttractionId, scriptId, type, key, effect, control, box.EngineMiddle, null, -1, -1))
				: new ParticleEventAttempt(AttractionId, scriptId, type, key, effect, ParticleEventPreparationStatus.Prepared, ParticleKeyStatus.ResolvedAtWorldOrigin,
					new ParticleEventRequest(AttractionId, scriptId, type, key, effect, control, NVector3.Zero, null, -1, -1, AtWorldOrigin: true));
		}
		particleKeys ??= new ParticleKeyResolver(Model);
		var pose = particleKeys.Resolve(key, ParticleKeyResolver.EventSelector, KeyPose, placement, includeDirection: type == 2);
		return pose.Status switch
		{
			ParticleKeyStatus.Resolved => new ParticleEventAttempt(AttractionId, scriptId, type, key, effect, ParticleEventPreparationStatus.Prepared, pose.Status,
				new ParticleEventRequest(AttractionId, scriptId, type, key, effect, control, pose.EnginePosition!.Value,
					pose.EngineUnitDirection, pose.AttributeIndex, pose.NodeIndex)),
			ParticleKeyStatus.ResolvedAtWorldOrigin => new ParticleEventAttempt(AttractionId, scriptId, type, key, effect, ParticleEventPreparationStatus.Prepared, pose.Status,
				new ParticleEventRequest(AttractionId, scriptId, type, key, effect, control, NVector3.Zero, null, pose.AttributeIndex, pose.NodeIndex, AtWorldOrigin: true)),
			_ => Refuse(ParticleEventPreparationStatus.UnresolvedKey, pose.Status)
		};
	}

	// [APPROX:PTCL-908] EVENT 1/2, ADDOBJ and ADDOBJ_EXT 1/2, the stinkbomb litter's effect (ParkLitterEffects) and the per-type object effects (OriginalObjectRuntime.ObjectEffects.cs) start particles (the land effects, the rider and guest beams are in Level.PlacedParticles.cs and the bump and guest code); a refusal (unresolved key, scoped effect, a type-2 effect with a negative key, headless runtime) starts nothing; the latest attempt stays as owner-local diagnostics, cleared on Stop, while the effect lives in the level's ParticleSystem (docs/PARTICLES.md)
	internal void ObserveParticleEvent(RideEffectCall call)
	{
		if (stopped || call.Opcode is not (Opcode.EVENT or Opcode.EVENT_EXT) || call.Argument(0) is not (1 or 2)) return;
		// [BIN:TPW-EXE:0x552707 EVENT_EXT] opcode 14: type, key, effect and control (an immediate is a signed word, a variable its whole value); the dispatcher sets a control other than 1000 on the emitter (0x51FFD0)
		var control = call.Opcode == Opcode.EVENT_EXT && call.OperandCount > 3 ? Word(call, 3) : 1000;
		LastParticleEventAttempt = PrepareParticle(call.VM.ScriptId, call.Argument(0), call.Argument(1), call.Argument(2), control);
		// [BIN:TPW-EXE:0x552615 RSE EVENT] fire and forget: dispatcher 0x5573D0 with control 1000 (EVENT_EXT: its operand), the handle is discarded and the effect does not follow the node
		if (LastParticleEventAttempt is { Request: { } request })
			ParticleRequested?.Invoke(request);
	}

	/// <summary>
	/// RSE ADDOBJ (type key effect slot) with type 1 or 2: the same dispatcher as EVENT, but the handle is kept under the slot operand so KILLOBJ can end the effect.
	/// </summary>
	// [BIN:TPW-EXE:0x551F5F ADDOBJ] operands type, key, effect (each a signed word), slot; control 1000; 0x557970 calls the dispatcher 0x5573D0 and keeps the record (a zero handle frees it)
	// [BIN:TPW-EXE:0x5520E8 ADDOBJ_EXT] opcode 9: operands type, key, effect, control, slot (an immediate is a signed word, a variable its whole value); the same record and 0x557970 call with the control operand in place of 1000, which the dispatcher sets on the emitter when it is not 1000 (0x51FFD0)
	// [BIN:TPW-EXE:0x5522C5 KILLOBJ] every record with the slot: types 1 and 2 call 0x51FF70(handle, -2)
	internal void ObserveParticleObject(RideEffectCall call)
	{
		var extended = call.Opcode == Opcode.ADDOBJ_EXT;
		if (stopped || call.OperandCount < (extended ? 5 : 4) || call.Argument(0) is not (1 or 2)) return;
		PruneParticleObjects();
		var type = call.Argument(0);
		var key = Word(call, 1);
		var attempt = PrepareParticle(call.VM.ScriptId, type, key, Word(call, 2), extended ? Word(call, 3) : 1000);
		LastParticleObjectAttempt = attempt;
		// native keeps any non-zero handle (0x5579F9); -1 is this port's refusal, and a generation from 0x8000 makes handles negative
		if (attempt.Request is { } request && ParticleStarter?.Invoke(request) is int handle && handle != 0 && handle != -1)
			particleObjects.Add((call.Argument(extended ? 4 : 3), handle, type, key));
	}

	/// <summary>An RSE operand as the handlers read it: a variable's whole value, an immediate's signed word.</summary>
	private static int Word(RideEffectCall call, int index) => call.IsVariable(index) ? call.Argument(index) : (short)call.Argument(index);

	/// <summary>
	/// Every scheduler pass moves the effects of the type 1 and 2 records with a key to that key's node, the position truncated to whole
	/// units, and turns a type-2 effect to the node's direction.
	/// </summary>
	// [BIN:TPW-EXE:0x55189D ADDOBJ follow] the object pass 0x5516B0 walks the records on every run (before the 8-pass script gate): with key +0x10 >= 0, type 1 calls 0x51FE30(handle, ftol(x) << 10, ftol(y) << 10, ftol(z) << 10) from the node's matrix +0x30/+0x34/+0x38 (0 without a matrix); type 2 does the same and then 0x51FD90 with the node's +0x20 row normalised (negated with the entry's bit 0x10, (0, 0, 1) without a matrix, 0x551B30) times 1024
	private void FollowParticleObjects()
	{
		if (ParticleFollower is not { } follow || particleObjects.Count == 0 || particlePlacement is not { } placement) return;
		foreach (var (_, handle, type, key) in particleObjects)
		{
			if (type is not (1 or 2) || key < 0) continue;
			particleKeys ??= new ParticleKeyResolver(Model);
			var pose = particleKeys.Resolve(key, ParticleKeyResolver.EventSelector, KeyPose, placement, includeDirection: type == 2);
			if (pose.Status == ParticleKeyStatus.Resolved)
				follow(handle, new ParticleFollow(pose.EnginePosition!.Value, type == 2 ? pose.EngineUnitDirection : null, false));
			else if (pose.Status == ParticleKeyStatus.ResolvedAtWorldOrigin)
				follow(handle, new ParticleFollow(NVector3.Zero, null, true, Directed: type == 2));
		}
	}

	/// <summary>RSE KILLOBJ: ends the particle effects ADDOBJ started under the slot.</summary>
	internal void KillParticleObjects(int slot)
	{
		for (var index = particleObjects.Count - 1; index >= 0; index--)
		{
			if (particleObjects[index].Slot != slot) continue;
			ParticleKiller?.Invoke(particleObjects[index].Handle);
			particleObjects.RemoveAt(index);
		}
	}

	/// <summary>Drops the records whose emitter has ended (a finished effect frees its slot; the handle would otherwise pile up).</summary>
	private void PruneParticleObjects()
	{
		if (ParticleAlive is not { } alive) return;
		particleObjects.RemoveAll(record => !alive(record.Handle));
	}

	/// <summary>The ADDOBJ records of live effects for the runtime snapshot, or null when there are none.</summary>
	private IReadOnlyList<ParticleObjectRecord>? CaptureParticleObjects()
	{
		PruneParticleObjects();
		return particleObjects.Count == 0 ? null : particleObjects.Select(record => new ParticleObjectRecord { Slot = record.Slot, Handle = record.Handle, Type = record.Type, Key = record.Key }).ToArray();
	}

	private void RestoreParticleObjects(IReadOnlyList<ParticleObjectRecord>? records)
	{
		particleObjects.Clear();
		// not pruned here: the pool is restored after the runtimes, the first ADDOBJ, KILLOBJ or capture drops the ones that ended
		if (records != null)
			foreach (var record in records) particleObjects.Add((record.Slot, record.Handle, record.Type, record.Key));
	}

	private void ClearParticleRequests()
	{
		particlePlacement = null; particleKeys = null; LastParticleEventAttempt = null; LastParticleObjectAttempt = null; particleFootprint = null;
		// [BIN:TPW-EXE:0x55854b script destructor] the repair effect's handle (+0xCC) ends with life -2
		if ( repairParticle != 0 )
			ParticleKiller?.Invoke( repairParticle );
		repairParticle = 0;
		// [BIN:TPW-EXE:0x558703 object destructor] the object's ADDOBJ records of types 1 and 2 end their effects (0x51FF70 life -2); effects started by EVENT have no record and run on
		foreach (var record in particleObjects) ParticleKiller?.Invoke(record.Handle);
		particleObjects.Clear();
	}
}
