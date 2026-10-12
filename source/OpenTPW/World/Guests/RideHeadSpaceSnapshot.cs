namespace OpenTPW;

/// <summary>
/// Explicit state of a <see cref="RideHeadSpace"/> (park-resume slice): the slot table (the visitor on each head
/// id's slot, 0 = free) and the full state of the slot-draw generator. The node table itself comes from the model
/// again; the restore checks its length. DTOs own their arrays.
/// </summary>
public sealed record RideHeadSpaceSnapshot
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; init; } = CurrentSchemaVersion;
	/// <summary>Visitor per slot (slot n = head id n + 1), 0 = free; length <see cref="RideHeadSpace.Count"/>.</summary>
	public required int[] Visitors { get; init; }
	/// <summary>Full explicit state of the slot draw's generator (a seed alone cannot continue it).</summary>
	public required ScriptRandomState RandomState { get; init; }

	/// <summary>Deep copy.</summary>
	public RideHeadSpaceSnapshot DeepCopy() => new() { Visitors = (int[])Visitors.Clone(), RandomState = RandomState.DeepCopy() };

	/// <summary>Pure structural validation; throws <see cref="InvalidDataException"/> on any violation.</summary>
	public void ValidateSnapshot()
	{
		if ( SchemaVersion != CurrentSchemaVersion )
			throw new InvalidDataException( $"head-space snapshot schema version {SchemaVersion} is not {CurrentSchemaVersion}." );
		if ( Visitors.Length > RideScriptFile.MaximumVariables )
			throw new InvalidDataException( $"head-space slot table has {Visitors.Length} slots, above any corpus model." );
		if ( Visitors.Any( visitor => visitor < 0 ) )
			throw new InvalidDataException( "head-space slot table holds a negative visitor." );
		RandomState.Validate();
	}
}
