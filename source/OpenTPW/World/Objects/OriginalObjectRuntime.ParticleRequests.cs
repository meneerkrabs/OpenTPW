using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

public sealed partial class OriginalObjectRuntime
{
	private ObjectPlacement? particlePlacement;
	private ParticleKeyResolver? particleKeys;
	/// <summary>The particle effects this object's ADDOBJ records hold, by the record's slot operand (tp.exe's object list at obj+0xB0, record +0x18); KILLOBJ ends them.</summary>
	private readonly List<(int Slot, int Handle)> particleObjects = new();

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

	internal void BindParticlePlacement(ObjectPlacement placement) => particlePlacement = placement;

	private ParticleEventAttempt PrepareParticle(int scriptId, int type, int key, int effect, int control)
	{
		ParticleEventAttempt Refuse(ParticleEventPreparationStatus status, ParticleKeyStatus? keyStatus = null) =>
			new(AttractionId, scriptId, type, key, effect, status, keyStatus, null);
		if ((effect & 0x8000) != 0) return Refuse(ParticleEventPreparationStatus.UnsupportedScopedEffect);
		if (effect < 0 || effect > 104) return Refuse(ParticleEventPreparationStatus.InvalidEffectId);
		if (particlePlacement is not { } placement) return Refuse(ParticleEventPreparationStatus.NoPlacement);
		particleKeys ??= new ParticleKeyResolver(Model);
		var pose = particleKeys.Resolve(key, ParticleKeyResolver.EventSelector, Animator.NodeTransforms, placement, includeDirection: type == 2);
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

	// [APPROX:PTCL-908] only EVENT 1/2, ADDOBJ 1/2 and the per-type object effects (OriginalObjectRuntime.ObjectEffects.cs) start particles; a refusal (negative or unresolved key, scoped effect, headless runtime) starts nothing, and an ADDOBJ effect starts at the key's pose without following the node afterwards; the latest attempt stays as owner-local diagnostics, cleared on Stop, while the effect lives in the level's ParticleSystem (docs/PARTICLES.md)
	internal void ObserveParticleEvent(RideEffectCall call)
	{
		if (stopped || call.Opcode != Opcode.EVENT || call.Argument(0) is not (1 or 2)) return;
		LastParticleEventAttempt = PrepareParticle(call.VM.ScriptId, call.Argument(0), call.Argument(1), call.Argument(2), 1000);
		// [BIN:TPW-EXE:0x552615 RSE EVENT] fire and forget: dispatcher 0x5573D0 with control 1000, the handle is discarded and the effect does not follow the node
		if (LastParticleEventAttempt is { Request: { } request })
			ParticleRequested?.Invoke(request);
	}

	/// <summary>
	/// RSE ADDOBJ (type key effect slot) with type 1 or 2: the same dispatcher as EVENT, but the handle is kept under the slot operand so KILLOBJ can end the effect.
	/// </summary>
	// [BIN:TPW-EXE:0x551F5F ADDOBJ] operands type, key, effect (each a signed word), slot; control 1000; 0x557970 calls the dispatcher 0x5573D0 and keeps the record (a zero handle frees it)
	// [BIN:TPW-EXE:0x5522C5 KILLOBJ] every record with the slot: types 1 and 2 call 0x51FF70(handle, -2)
	internal void ObserveParticleObject(RideEffectCall call)
	{
		if (stopped || call.OperandCount < 4 || call.Argument(0) is not (1 or 2)) return;
		PruneParticleObjects();
		var attempt = PrepareParticle(call.VM.ScriptId, call.Argument(0), (short)call.Argument(1), (short)call.Argument(2), 1000);
		LastParticleObjectAttempt = attempt;
		// native keeps any non-zero handle (0x5579F9); -1 is this port's refusal, and a generation from 0x8000 makes handles negative
		if (attempt.Request is { } request && ParticleStarter?.Invoke(request) is int handle && handle != 0 && handle != -1)
			particleObjects.Add((call.Argument(3), handle));
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
		return particleObjects.Count == 0 ? null : particleObjects.Select(record => new ParticleObjectRecord { Slot = record.Slot, Handle = record.Handle }).ToArray();
	}

	private void RestoreParticleObjects(IReadOnlyList<ParticleObjectRecord>? records)
	{
		particleObjects.Clear();
		// not pruned here: the pool is restored after the runtimes, the first ADDOBJ, KILLOBJ or capture drops the ones that ended
		if (records != null)
			foreach (var record in records) particleObjects.Add((record.Slot, record.Handle));
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
