namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-lockstep] The session digest (docs/COOP.md, "Checks"): the world state hash (<see cref="Level.ComputeStateHash"/>) and the state a session's peers share that
/// the world hash leaves out: the object layout, the weather, the per-player state later commands read, the session clock. Versioned on its own; never saved. The host takes it at
/// its checkpoints and every guest compares its own at the same point of the stream.
/// </summary>
internal static class CoopDigest
{
	/// <summary>Bumped whenever the digest's sections or their order change (2: no drags hash as an empty set of them).</summary>
	public const int Version = 2;

	public static ulong Compute( Level level ) => Compute( level, out _ );

	/// <summary><see cref="Compute(Level)"/>, with the world hash it took (a session's recording keeps it as its checkpoint).</summary>
	public static ulong Compute( Level level, out ulong worldHash )
	{
		ArgumentNullException.ThrowIfNull( level );
		var hash = new StateHasher();
		hash.Add( Version );
		worldHash = level.ComputeStateHash();
		hash.Add( worldHash );
		using ( FrameMarks.Measure( "digest.session" ) )
			level.AddSessionState( hash );
		return hash.Value;
	}

	/// <summary>
	/// [EXT:perf-run] The digest's cost on <paramref name="level"/> (docs/COOP.md, "Measurements"): the median time of <paramref name="repeats"/> digests and of as many world hashes alone,
	/// in milliseconds. Measurement only: it reads the park and changes nothing.
	/// </summary>
	internal static (double Digest, double WorldHash) MeasureCost( Level level, int repeats )
	{
		ArgumentNullException.ThrowIfNull( level );
		ArgumentOutOfRangeException.ThrowIfLessThan( repeats, 1 );
		static double Median( Func<ulong> work, int repeats )
		{
			var times = new double[repeats];
			for ( var index = 0; index < repeats; index++ )
			{
				var start = System.Diagnostics.Stopwatch.GetTimestamp();
				work();
				times[index] = System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
			}
			Array.Sort( times );
			return times[repeats / 2];
		}
		return (Median( () => Compute( level ), repeats ), Median( level.ComputeStateHash, repeats ));
	}
}

public partial class Level
{
	/// <summary>
	/// [EXT:online-coop-lockstep] Adds what the session digest covers beyond the world hash (<see cref="CoopDigest"/>): the placed objects in list order and the prototype ride (catalogue
	/// key, cell, rotation, base, attraction id, deleted flag, economy link, run flags); the weather without its frame-drained thunder queue (<see cref="ParkWeather.TakeThunder"/> empties
	/// it once a game frame, so at a tick's end it depends on how ticks fell into frames); each player's carried catalogue entry, hand and coaster drag, the pending hire and the stale
	/// on-pointer candidate, the coasters' being-edited marks; the session clock. Never presentation a player may switch on or off alone: the snow cover is the winter snow
	/// extension's, which changes no simulation and is no part of a session's identity, so two peers may differ in it.
	/// </summary>
	internal void AddSessionState( StateHasher hash )
	{
		var objects = Objects?.Objects;
		hash.Add( objects?.Count ?? -1 );
		if ( objects != null )
			foreach ( var item in objects )
				AddSessionLayout( hash, item );
		hash.Add( PlacedRide != null );
		if ( PlacedRide != null )
			AddSessionLayout( hash, PlacedRide );

		hash.Add( Weather != null );
		if ( Weather?.CaptureSnapshot() is { } weather )
		{
			hash.Add( weather.RandomState );
			hash.Add( weather.Started );
			hash.Add( weather.LastTurn );
			hash.Add( weather.LastChanged );
			hash.Add( weather.NextRollDay );
			hash.Add( weather.PendingQuality ?? -1 );
			hash.Add( weather.ApplyDay );
			hash.Add( weather.Quality );
			hash.Add( weather.Snow );
			hash.Add( weather.Rain );
			hash.Add( weather.Lightning );
			hash.Add( weather.CurrentDrops );
			hash.Add( weather.TargetDrops );
			hash.Add( weather.LightningCountdown );
			hash.Add( weather.Bolts );
			hash.Add( weather.ThunderCountdown );
			hash.Add( weather.LastTop.X );
			hash.Add( weather.LastTop.Y );
			hash.Add( weather.LastTop.Z );
		}

		hash.Add( carriedEntries?.Count ?? 0 );
		if ( carriedEntries != null )
			foreach ( var (player, key) in carriedEntries.OrderBy( pair => pair.Key ) )
			{
				hash.Add( player );
				hash.Add( key );
			}
		hash.Add( StaffHands != null );
		StaffHands?.AddSessionState( hash );
		var staff = Park?.Economy.Staff;
		hash.Add( staff?.PendingPlacementId ?? -1 );
		hash.Add( staff?.StaleOnPointerId ?? -1 );
		// [EXT:online-coop-snapshot-join] the set of drags is made the first time anything reads it, on one machine alone: none hashes as an empty set
		if ( coasterEdits != null )
			coasterEdits.AddSessionState( hash );
		else
			hash.Add( 0 );
		var scripts = Objects?.ScriptWorld;
		hash.Add( scripts?.Coasters.Rides.Count ?? -1 );
		if ( scripts != null )
			foreach ( var ride in scripts.Coasters.Rides )
				hash.Add( ride.BeingEdited );
		hash.Add( scripts?.SessionTime?.Ticks ?? -1 );
	}

	private void AddSessionLayout( StateHasher hash, OriginalObject item )
	{
		hash.Add( ParkCommandCodec.EntryKey( item.Entry ) );
		hash.Add( item.Placement.X );
		hash.Add( item.Placement.Y );
		hash.Add( item.Placement.Rotation );
		hash.Add( item.Placement.GridOrigin.X );
		hash.Add( item.Placement.GridOrigin.Y );
		hash.Add( item.Placement.BaseHeight );
		hash.Add( item.Visitors.AttractionId );
		hash.Add( item.IsDeleted );
		hash.Add( GetEconomyInstance( item ) ?? -1 );
		var (stopped, wasRunning) = item.Runtime.RunFlags;
		hash.Add( stopped );
		hash.Add( wasRunning );
	}
}
