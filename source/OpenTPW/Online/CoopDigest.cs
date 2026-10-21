namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-lockstep] The session digest (docs/COOP.md, "Checks"): the world state hash (<see cref="Level.ComputeStateHash"/>) and the state a session's peers share that
/// the world hash leaves out: the object layout, the weather, the per-player state later commands read, the session clock. Versioned on its own; never saved. The host takes it at
/// its checkpoints and every guest compares its own at the same point of the stream.
/// </summary>
internal static class CoopDigest
{
	/// <summary>Bumped whenever the digest's sections or their order change.</summary>
	public const int Version = 1;

	public static ulong Compute( Level level )
	{
		ArgumentNullException.ThrowIfNull( level );
		var hash = new StateHasher();
		hash.Add( Version );
		hash.Add( level.ComputeStateHash() );
		level.AddSessionState( hash );
		return hash.Value;
	}
}

public partial class Level
{
	/// <summary>
	/// [EXT:online-coop-lockstep] Adds what the session digest covers beyond the world hash (<see cref="CoopDigest"/>): the placed objects in list order and the prototype ride (catalogue
	/// key, cell, rotation, base, attraction id, deleted flag, economy link, run flags); the weather without its frame-drained thunder queue (<see cref="ParkWeather.TakeThunder"/> empties
	/// it once a game frame, so at a tick's end it depends on how ticks fell into frames) and the snow cover of the last turn; each player's carried catalogue entry, hand and coaster
	/// drag, the pending hire and the stale on-pointer candidate, the coasters' being-edited marks; the session clock.
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
		// the cover of the last park turn, never the drawn value the frames ease toward it
		hash.Add( snow?.Cover ?? -1d );

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
		hash.Add( coasterEdits != null );
		coasterEdits?.AddSessionState( hash );
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
