namespace OpenTPW;

/// <summary>
/// Explicit state of one <see cref="OriginalObjectRuntime"/> (park-resume slice): the entry identity the state
/// belongs to, the animator, the head slot table, the lifecycle counters and flags, and the ride script's identity
/// (its instruction state lives in the <see cref="RideScriptWorldSnapshot"/>). Sound chains are live audio and
/// cannot survive a restart: <see cref="HeldScream"/>/<see cref="OwnedScreamCount"/> are captured so the restore
/// reports them as gaps instead of silently dropping them.
/// </summary>
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
	/// <summary>Whether the script's effects held a STARTSCREAM chain at capture (restorable only as a reported gap).</summary>
	public required bool HeldScream { get; init; }
	/// <summary>Chains the effects still owned at capture (screams play on; not restorable across a restart).</summary>
	public required int OwnedScreamCount { get; init; }
	/// <summary>
	/// Fault message of a stopped runtime's script: its VM is unregistered, so the world snapshot does not carry it.
	/// Null unless the runtime is stopped and its script had faulted.
	/// </summary>
	public string? StoppedScriptFault { get; init; }

	/// <summary>Deep copy.</summary>
	public OriginalObjectRuntimeSnapshot DeepCopy() => this with
	{
		Animator = Animator.DeepCopy(),
		Heads = Heads?.DeepCopy(),
		UnresolvedAnimations = UnresolvedAnimations.ToArray()
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
		if ( StoppedScriptFault != null && ( !Stopped || !HasScript ) )
			throw new InvalidDataException( "runtime snapshot carries a stopped script fault without a stopped scripted runtime." );
	}
}
