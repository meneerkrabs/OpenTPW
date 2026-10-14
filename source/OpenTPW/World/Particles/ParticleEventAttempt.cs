using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

public enum ParticleEventPreparationStatus
{
	Prepared,
	NoPlacement,
	UnsupportedScopedEffect,
	InvalidEffectId,
	UnresolvedKey
}

/// <summary>Frozen managed key pose and operands, not a constructed particle, pool handle, velocity or successful game effect.</summary>
public readonly record struct ParticleEventRequest(int OwnerAttractionId, int ScriptId, int Type, int Key, int EffectId,
	int Control, NVector3 EnginePosition, NVector3? EngineUnitDirection, int AttributeIndex, int NodeIndex, bool AtWorldOrigin = false);

/// <summary>Latest owner-local preparation diagnostic. A refusal has no request/pose; IDs are local metadata, not global handles.</summary>
public readonly record struct ParticleEventAttempt(int OwnerAttractionId, int ScriptId, int Type, int Key, int EffectId,
	ParticleEventPreparationStatus Status, ParticleKeyStatus? KeyStatus, ParticleEventRequest? Request);
