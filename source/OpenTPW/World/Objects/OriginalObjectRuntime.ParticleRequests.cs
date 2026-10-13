namespace OpenTPW;

public sealed partial class OriginalObjectRuntime
{
	private ObjectPlacement? particlePlacement;
	private ParticleKeyResolver? particleKeys;

	/// <summary>Only the latest EVENT1/2 attempt: polling diagnostics, not a retained queue or particle delivery interface.</summary>
	public ParticleEventAttempt? LastParticleEventAttempt { get; private set; }

	internal void BindParticlePlacement(ObjectPlacement placement) => particlePlacement = placement;

	// [APPROX:PTCL-901] one owner-local managed preparation snapshot is replaced at EVENT1/2 and cleared on Stop; not a native particle pool, lifetime, delivery stream or save state
	internal void ObserveParticleEvent(RideEffectCall call)
	{
		if (stopped || call.Opcode != Opcode.EVENT || call.Argument(0) is not (1 or 2)) return;
		var type = call.Argument(0); var key = call.Argument(1); var effect = call.Argument(2);
		ParticleEventAttempt Refuse(ParticleEventPreparationStatus status, ParticleKeyStatus? keyStatus = null) =>
			new(AttractionId, call.VM.ScriptId, type, key, effect, status, keyStatus, null);
		if ((effect & 0x8000) != 0)
			LastParticleEventAttempt = Refuse(ParticleEventPreparationStatus.UnsupportedScopedEffect);
		else if (effect < 0 || effect > 104)
			LastParticleEventAttempt = Refuse(ParticleEventPreparationStatus.InvalidEffectId);
		else if (particlePlacement is not { } placement)
			LastParticleEventAttempt = Refuse(ParticleEventPreparationStatus.NoPlacement);
		else
		{
			particleKeys ??= new ParticleKeyResolver(Model);
			var pose = particleKeys.Resolve(key, ParticleKeyResolver.EventSelector, Animator.NodeTransforms, placement, includeDirection: type == 2);
			LastParticleEventAttempt = pose.Status != ParticleKeyStatus.Resolved
				? Refuse(ParticleEventPreparationStatus.UnresolvedKey, pose.Status)
				: new ParticleEventAttempt(AttractionId, call.VM.ScriptId, type, key, effect, ParticleEventPreparationStatus.Prepared, pose.Status,
					new ParticleEventRequest(AttractionId, call.VM.ScriptId, type, key, effect, 1000, pose.EnginePosition!.Value,
						pose.EngineUnitDirection, pose.AttributeIndex, pose.NodeIndex));
		}
	}

	private void ClearParticleRequests()
	{
		particlePlacement = null; particleKeys = null; LastParticleEventAttempt = null;
	}
}
