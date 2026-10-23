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
/// original has but OpenTPW lacks (clock A, substep and manager-pass counters, the C-library stream, world
/// state, calendar rate, input log) and OpenTPW state outside the hash (sound seed, object
/// placements and run flags, the clock's pending time) are listed in docs/DETERMINISM.md.
/// </summary>
public static class WorldStateHash
{
	/// <summary>Bumped whenever the hashed fields or their order change.</summary>
	public const int SchemaVersion = 21;

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
		// world_rng_state: the one world generator (tp.exe 0x00516330) every system draws from; the economy's, else the guests', else the scripts'.
		hash.Add( (economy?.World ?? sources.Guests?.World ?? sources.Scripts?.World)?.State ?? 0 );
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
		// the sections' work by name, for a measured frame (FrameMarks: a boolean test while off)
		using ( FrameMarks.Measure( "hash.guests" ) )
			sources.Guests?.AddCanonicalState( hash );
		// cell_map_digest: the park cell map (path and queue cells, flags, placement counters, links) guests walk on.
		using ( FrameMarks.Measure( "hash.cells" ) )
			sources.Guests?.Grid.Cells.AddCanonicalState( hash );
		// script_table_digest: every live script in id order.
		hash.Add( sources.Scripts != null );
		if ( sources.Scripts != null )
		{
			hash.Add( sources.Scripts.NextAttractionId );
			hash.Add( sources.Scripts.ClockMilliseconds );
			hash.Add( sources.Scripts.Scripts.Count );
			// the script pass counter (which pass of eight a script's slice falls on) joins the hash once a pass has run, so a world that never ran one keeps its pin
			if ( sources.Scripts.PassCounter != 0 )
				hash.Add( sources.Scripts.PassCounter );
			using ( FrameMarks.Measure( "hash.scripts" ) )
				foreach ( var script in sources.Scripts.Scripts.OrderBy( script => script.ScriptId ) )
					script.AddCanonicalState( hash );
			// coaster_rng_state and the coasters (tracks, samples, ratings, trains and riders) in creation order.
			using ( FrameMarks.Measure( "hash.coasters" ) )
				sources.Scripts.Coasters.AddCanonicalState( hash );
			// the kart and water ride controllers join the hash only while one exists (every other park keeps its pins)
			if ( sources.Scripts.Bumpers.Controllers.Count > 0 )
				sources.Scripts.Bumpers.AddCanonicalState( hash );
			// likewise the tour rides' flying-car controllers
			if ( sources.Scripts.Tours.Controllers.Count > 0 )
				sources.Scripts.Tours.AddCanonicalState( hash );
		}
		// economy_digest: everything the park save stores, with object keys sorted.
		if ( economy != null )
		{
			// the analyser's caches are saved for an exact continuation but are a reading aid, not park state: they stay out of the digest
			JsonNode? node;
			using ( FrameMarks.Measure( "hash.economyNode" ) )
				node = JsonSerializer.SerializeToNode( ParkSaveFile.Capture( economy ) );
			if ( node is JsonObject root )
			{
				root.Remove( nameof( ParkSaveFile.Data.AnalyserCaches ) );
				// the statistics history only feeds the park information window's graph, which no simulation step reads: it stays out of the digest so the pins keep
				root.Remove( nameof( ParkSaveFile.Data.StatisticsHistory ) );
				// The buy-list stars are presentation only. Completion, progress, effort and every other research field remain hashed.
				if ( root[nameof( ParkSaveFile.Data.Research )] is JsonObject research )
					research.Remove( nameof( ParkSaveFile.ResearchData.RecentItems ) );
				// Role order feeds only the locator radios. Roster order, training, candidates and all staff gameplay state remain hashed.
				if ( root[nameof( ParkSaveFile.Data.Staff )] is JsonObject staff )
					staff.Remove( nameof( ParkSaveFile.StaffData.RoleOrder ) );
				// Challenge RawValue feeds only the localized progress caption; scoring continues to use Baseline and the live measures.
				if ( root[nameof( ParkSaveFile.Data.Objectives )] is JsonObject objectives && objectives[nameof( ParkSaveFile.ObjectivesData.Current )] is JsonObject challenge )
					challenge.Remove( nameof( ActiveChallenge.RawValue ) );
			}
			using ( FrameMarks.Measure( "hash.economyCanonical" ) )
				hash.Add( Canonical( node ) );
		}
		// [EXT:smarter-guests] the identity state joins the hash only while the extension or a script mod is on, so every park without it keeps its pins and SchemaVersion
		if ( sources.Guests?.SmarterGuests is { } smarter )
			smarter.AddCanonicalState( hash, sources.Guests.Guests );
		// [EXT:script-mods] likewise the script mods' state, counters and flags: only while a script mod is enabled, with no SchemaVersion bump (docs/SCRIPT-MODS.md, "Saves and versioning")
		sources.Guests?.ScriptMods?.AddCanonicalState( hash );
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
