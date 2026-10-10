using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenTPW;

/// <summary>FNV-1a 64 over little-endian values; floats are hashed by bit pattern, never with a tolerance.</summary>
public sealed class StateHasher
{
	public ulong Value { get; private set; } = 14695981039346656037UL;

	public void Add( long value )
	{
		for ( var shift = 0; shift < 64; shift += 8 )
			AddByte( (byte)(value >> shift) );
	}

	public void Add( ulong value ) => Add( unchecked((long)value) );
	public void Add( bool value ) => Add( value ? 1L : 0L );
	public void Add( float value ) => Add( BitConverter.SingleToInt32Bits( value ) );
	public void Add( double value ) => Add( BitConverter.DoubleToInt64Bits( value ) );

	public void Add( string value )
	{
		Add( value.Length );
		foreach ( var character in value )
			Add( character );
	}

	private void AddByte( byte value )
	{
		Value ^= value;
		Value *= 1099511628211UL;
	}
}

/// <summary>The parts of a running park the canonical hash reads; absent parts are hashed as absent.</summary>
public sealed record WorldStateSources
{
	public WorldSeed Seed { get; init; } = WorldSeed.Default;
	public ParkEconomy? Economy { get; init; }
	public GuestSimulation? Guests { get; init; }
	public RideScriptWorld? Scripts { get; init; }
}

/// <summary>
/// Canonical state hash of a park run (docs/DETERMINISM.md): the subset of the replay fields of
/// docs/reverse/DET-plan.md §4.5 that OpenTPW has today, in that order. Same world seed and same inputs give
/// the same hash, in one process or in separate ones; a different seed gives a different one. Fields the
/// original has but OpenTPW lacks (clock A, substep and manager-pass counters, the coaster and C-library
/// streams, world state, calendar rate, input log) and OpenTPW state outside the hash (sound seed, object
/// placements and run flags, the clock's pending time) are listed in docs/DETERMINISM.md.
/// </summary>
public static class WorldStateHash
{
	/// <summary>Bumped whenever the hashed fields or their order change.</summary>
	public const int SchemaVersion = 5;

	public static ulong Compute( WorldStateSources sources )
	{
		ArgumentNullException.ThrowIfNull( sources );
		var hash = new StateHasher();
		hash.Add( SchemaVersion );
		hash.Add( sources.Seed.Value );
		// substep_counter: OpenTPW's 60 Hz fixed-step count, kept by the guest simulation.
		hash.Add( sources.Guests?.TickCount ?? -1 );
		var economy = sources.Economy;
		hash.Add( economy != null );
		if ( economy != null )
			hash.Add( economy.Turn );
		// world_rng_state: OpenTPW has one stream per system instead of the original's one world generator.
		hash.Add( economy?.Random.State ?? 0 );
		hash.Add( sources.Guests?.RandomState ?? 0 );
		hash.Add( sources.Scripts?.RandomState ?? 0 );
		// sound_seed is not hashed: it is presentation state that draws never move (sound_shared 0x100118C8 is
		// its only store); the park save keeps it.
		if ( economy != null )
		{
			hash.Add( (int)economy.Speed );
			hash.Add( economy.Speed == GameSpeed.Paused );
			hash.Add( (int)economy.Mode );
		}
		hash.Add( ParkCalendar.Epoch.Ticks );
		// thing_table_digest: guests in id order, then the registered attractions.
		hash.Add( sources.Guests != null );
		sources.Guests?.AddCanonicalState( hash );
		// cell_map_digest: the park cell map (path and queue cells, flags, placement counters, links) guests walk on.
		sources.Guests?.Grid.Cells.AddCanonicalState( hash );
		// script_table_digest: every live script in id order.
		hash.Add( sources.Scripts != null );
		if ( sources.Scripts != null )
		{
			hash.Add( sources.Scripts.NextAttractionId );
			hash.Add( sources.Scripts.Scripts.Count );
			foreach ( var script in sources.Scripts.Scripts.OrderBy( script => script.ScriptId ) )
				script.AddCanonicalState( hash );
		}
		// economy_digest: everything the park save stores, with object keys sorted.
		if ( economy != null )
			hash.Add( Canonical( JsonSerializer.SerializeToNode( ParkSaveFile.Capture( economy ) ) ) );
		return hash.Value;
	}

	private static string Canonical( JsonNode? node ) => node switch
	{
		JsonObject item => "{" + string.Join( ",", item.OrderBy( pair => pair.Key, StringComparer.Ordinal ).Select( pair => JsonValue.Create( pair.Key ).ToJsonString() + ":" + Canonical( pair.Value ) ) ) + "}",
		JsonArray list => "[" + string.Join( ",", list.Select( Canonical ) ) + "]",
		null => "null",
		_ => node.ToJsonString()
	};
}
