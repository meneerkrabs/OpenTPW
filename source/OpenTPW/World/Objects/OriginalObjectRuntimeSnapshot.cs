using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>
/// Explicit state of one <see cref="OriginalObjectRuntime"/> (park-resume slice): the entry identity the state
/// belongs to, the animator, the head slot table, the lifecycle counters and flags, and the ride script's identity
/// (its instruction state lives in the <see cref="RideScriptWorldSnapshot"/>). Sound chains are live audio and are not
/// saved: the scream is saved as the script holds it (its handle and STARTSCREAM/SCREAMLEVEL operands, and the count of
/// handles a refused STARTSCREAM lost), the same with or without audio, and the held scream plays again once a resumed
/// park's audio runs.
/// </summary>
/// <summary>One ADDOBJ record of a live particle effect: the slot operand KILLOBJ names and the pool handle.</summary>
public sealed record ParticleObjectRecord
{
	public int Slot { get; init; }
	public int Handle { get; init; }
	/// <summary>The ADDOBJ type (1 or 2), or 0 in a save written before records kept it: such a record is not moved to its key.</summary>
	public int Type { get; init; }
	/// <summary>The ADDOBJ key the effect follows (a negative key follows nothing).</summary>
	public int Key { get; init; }
}

public sealed record OriginalObjectRuntimeSnapshot
{
	public const int CurrentSchemaVersion = 1;
	/// <summary>Bound for the unresolved-animation list (a corpus object accumulates a handful at most).</summary>
	public const int MaximumUnresolvedAnimations = 1024;

	public int SchemaVersion { get; init; } = CurrentSchemaVersion;
	/// <summary>The attraction id this runtime was allocated (the bridge's id; the allocator frontier moves past it on restore).</summary>
	public required int AttractionId { get; init; }
	/// <summary>Asset identity: the entry's data-root-relative archive path.</summary>
	public required string EntryArchivePath { get; init; }
	/// <summary>Asset identity: the entry's <c>Info.Id</c>.</summary>
	public required int EntryInfoId { get; init; }
	/// <summary>Asset identity: the entry's main model member.</summary>
	public required string ModelPath { get; init; }
	/// <summary>Whether the entry has a script (the candidate must match).</summary>
	public required bool HasScript { get; init; }
	/// <summary><see cref="RideScriptIdentity.Hash"/> of the root script; 0 without one.</summary>
	public required ulong ScriptIdentityHash { get; init; }
	public required ObjectAnimatorSnapshot Animator { get; init; }
	/// <summary>Head slot table state, or null when the model has no head id 1 (no head space).</summary>
	public RideHeadSpaceSnapshot? Heads { get; init; }
	/// <summary>Stopped by <see cref="OriginalObjectRuntime.Stop"/>; the restore replays the teardown.</summary>
	public required bool Stopped { get; init; }
	/// <summary>Last tick's VAR_RUNNING edge detector (drives <see cref="OriginalObjectRuntime.CompletedCycles"/>).</summary>
	public required bool WasRunning { get; init; }
	public required int CompletedCycles { get; init; }
	public required int AnimationsPlayed { get; init; }
	/// <summary>(animation, variant) requests without a matching member, as "a/v".</summary>
	public required IReadOnlyList<string> UnresolvedAnimations { get; init; }
	/// <summary>
	/// Whether the script held a scream handle at capture, with or without audio (the same value as <see cref="ScriptHoldsScream"/>; saves written before
	/// that field and before the handle followed the script told whether a chain played). A resumed park plays the held scream again once its audio runs.
	/// </summary>
	public required bool HeldScream { get; init; }
	/// <summary>The held scream's STARTSCREAM band, level (the last SCREAMLEVEL's) and script speed word; 0 without a held scream and in saves written before they were kept.</summary>
	public int HeldScreamBand { get; init; }
	public int HeldScreamLevel { get; init; }
	public int HeldScreamSpeed { get; init; }
	/// <summary>Handles a refused STARTSCREAM lost before the capture (their chains scream on unreferenced until the object stops): restored as a count, their chains do not scream again.</summary>
	public required int OwnedScreamCount { get; init; }
	/// <summary>
	/// Fault message of a stopped runtime's script: its VM is unregistered, so the world snapshot does not carry it.
	/// Null unless the runtime is stopped and its script had faulted.
	/// </summary>
	public string? StoppedScriptFault { get; init; }
	/// <summary>Maximum ADDOBJ particle records of one object (a script holds a handful).</summary>
	public const int MaximumParticleObjects = 4096;
	/// <summary>The ADDOBJ type 1/2 records of live particle effects (slot operand and pool handle), or null when none; the pool's generations and counters restore exactly, so the handles stay valid.</summary>
	public IReadOnlyList<ParticleObjectRecord>? ParticleObjects { get; init; }
	/// <summary>The pool handle of the repair effect REPAIREFFECT 1 started (the original keeps it at the script's +0xCC), or 0 when none is live; older saves have none.</summary>
	public int RepairParticle { get; init; }
	/// <summary>
	/// The ride words the settings setters wrote (SPEED <c>+0x58</c>, CAPACITY <c>+0x5d</c>, DURATION <c>+0x5c</c>), each null while it holds the catalogue
	/// tier's value; older saves have none. The script's bias and variables 2 and 3 come back with the script's own snapshot.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public int? RideSpeed { get; init; }
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public int? RideCapacity { get; init; }
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public int? RideDuration { get; init; }
	/// <summary>[EXT:cheats-ride-vehicles] True once the cheats window raised the ride's vehicle limit; absent otherwise, so every other snapshot keeps its bytes (docs/CHEATS.md).</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public bool? VehiclesRaised { get; init; }
	/// <summary>
	/// [EXT:object-lights] The on/off state and colour of the object's lights (docs/LIGHTS.md), or null for an object whose model has none (every original object: such a snapshot is byte-identical to one
	/// written before lights existed). Presentation only; no hash reads it.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public IReadOnlyList<ObjectLightRecord>? Lights { get; init; }
	/// <summary>Whether the script's own view of its scream handle is set (the scream diagnostics follow it, with or without audio); null in older saves, which fall back to <see cref="HeldScream"/>. The restore takes this one when the two differ.</summary>
	public bool? ScriptHoldsScream { get; init; }

	/// <summary>Deep copy.</summary>
	public OriginalObjectRuntimeSnapshot DeepCopy() => this with
	{
		Animator = Animator.DeepCopy(),
		Heads = Heads?.DeepCopy(),
		UnresolvedAnimations = UnresolvedAnimations.ToArray(),
		ParticleObjects = ParticleObjects?.ToArray(),
		Lights = Lights?.ToArray()
	};

	/// <summary>Pure structural validation; throws <see cref="InvalidDataException"/> on any violation.</summary>
	public void ValidateSnapshot()
	{
		if ( SchemaVersion != CurrentSchemaVersion )
			throw new InvalidDataException( $"runtime snapshot schema version {SchemaVersion} is not {CurrentSchemaVersion}." );
		if ( AttractionId < 1 )
			throw new InvalidDataException( $"attraction id {AttractionId} is not positive." );
		if ( EntryArchivePath == null || ModelPath == null )
			throw new InvalidDataException( "runtime snapshot has no entry identity." );
		if ( EntryArchivePath.Length == 0 || ModelPath.Length == 0 )
			throw new InvalidDataException( "runtime snapshot has an empty entry identity." );
		if ( !HasScript && ScriptIdentityHash != 0 )
			throw new InvalidDataException( "runtime snapshot has no script but a script identity." );
		Animator.ValidateSnapshot();
		Heads?.ValidateSnapshot();
		if ( CompletedCycles < 0 || AnimationsPlayed < 0 )
			throw new InvalidDataException( "runtime snapshot counters are negative." );
		if ( UnresolvedAnimations.Count > MaximumUnresolvedAnimations )
			throw new InvalidDataException( $"runtime snapshot lists {UnresolvedAnimations.Count} unresolved animations, above the bound." );
		if ( UnresolvedAnimations.Any( entry => entry == null ) )
			throw new InvalidDataException( "runtime snapshot has a null unresolved animation." );
		if ( OwnedScreamCount < 0 )
			throw new InvalidDataException( "runtime snapshot has a negative scream-chain count." );
		if ( ParticleObjects != null && ( ParticleObjects.Count > MaximumParticleObjects || ParticleObjects.Any( record => record == null || record.Handle == 0 || record.Type is < 0 or > 2 ) ) )
			throw new InvalidDataException( "runtime snapshot has too many or malformed particle object records." );
		// the SPEED word +0x58 is a full dword (any int; only the script's bias word +0xc0 is a short), CAPACITY and DURATION are bytes
		// a raised ride's capacity may pass the byte (OriginalObjectRuntime.MaximumCapacity, checked against the entry on restore)
		if ( RideCapacity < 0 || RideCapacity > (VehiclesRaised == true ? OriginalObjectRuntime.MaximumRaisedCapacity : byte.MaxValue) || RideDuration is < 0 or > byte.MaxValue )
			throw new InvalidDataException( "runtime snapshot has a ride capacity word outside 0..255 (a raised ride's up to 4095) or a duration word outside 0..255." );
		if ( VehiclesRaised == false )
			throw new InvalidDataException( "runtime snapshot writes a vehicle limit that was not raised." );
		if ( Lights != null )
			ObjectLightBank.ValidateRecords( Lights );
		if ( StoppedScriptFault != null && ( !Stopped || !HasScript ) )
			throw new InvalidDataException( "runtime snapshot carries a stopped script fault without a stopped scripted runtime." );
	}
}
