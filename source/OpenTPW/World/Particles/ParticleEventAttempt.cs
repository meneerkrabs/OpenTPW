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
	int Control, NVector3 EnginePosition, NVector3? EngineUnitDirection, int AttributeIndex, int NodeIndex, bool AtWorldOrigin = false,
	(int X, int Y, int Z)? VolumeExtent = null);

/// <summary>Latest owner-local preparation diagnostic. A refusal has no request/pose; IDs are local metadata, not global handles.</summary>
public readonly record struct ParticleEventAttempt(int OwnerAttractionId, int ScriptId, int Type, int Key, int EffectId,
	ParticleEventPreparationStatus Status, ParticleKeyStatus? KeyStatus, ParticleEventRequest? Request);

/// <summary>
/// Where a kept ADDOBJ effect's key stands now: an engine position (or the world origin for a key entry without a matrix) and, for a
/// type-2 effect, the unit direction (<see cref="Directed"/> with no direction is the native default (0, 0, 1)).
/// </summary>
public readonly record struct ParticleFollow(NVector3 EnginePosition, NVector3? EngineUnitDirection, bool AtWorldOrigin, bool Directed = false);
