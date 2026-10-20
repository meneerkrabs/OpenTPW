namespace OpenTPW;

/// <summary>The Instant Action research finale's fixed object and its explicit dismissal.</summary>
public static class ParkFinale
{
	private static bool IsEnd( ObjectCatalogEntry entry ) =>
		entry.IsFixedItem && string.Equals( entry.SettingsName, "End", StringComparison.OrdinalIgnoreCase );

	/// <summary>Whether a live fixed finale object is present, including one restored by the layout loader.</summary>
	// [BIN:TPW-EXE:0x00516C00 winning sequence active] the bank tests whether the finale's thing handle is nonzero.
	public static bool IsActive( ParkObjects objects ) => objects.Objects.Any( item => !item.IsDeleted && IsEnd( item.Entry ) );

	/// <summary>Only a new research-completion notification creates the finale; opening an already researched park does not.</summary>
	// [BIN:TPW-EXE:0x00503ED3 Instant Action research finale] the last research item in game type 2 posts advice 167 and calls 0x516B00. Type 0 also requires the player-wide records, and type 1 never enters this path.
	// [BIN:TPW-EXE:0x00516B31 finale object] finds End case-insensitively, constructs it at the fixed-item origin and stores its thing handle.
	// [APPROX:RIDES-019] Instant Action creates and dismisses End, with its bankruptcy guard. Full Simulation's player-wide prerequisite, winning-camera follow, original winning-screen layout and post-dismissal profile/dialog changes remain unimplemented; a resumed finale reopens the bounded dismissal screen instead of restoring the original winning UI state.
	public static OriginalObject? StartForResearchCompletion( ParkObjects objects, ParkGameMode mode )
	{
		if ( mode != ParkGameMode.InstantAction )
			return null;
		if ( objects.Objects.FirstOrDefault( item => !item.IsDeleted && IsEnd( item.Entry ) ) is { } existing )
			return existing;
		var entry = objects.Catalog.Entries.FirstOrDefault( IsEnd );
		// Missing/modded catalogues remain usable: no substitute object, charge or claimed victory.
		return entry == null ? null : objects.Add( entry, 0, 0, 0 );
	}

	/// <summary>Removes the finale through normal object cleanup: scripts, ducking, held sounds, particles and thing ownership end with it.</summary>
	// [BIN:TPW-EXE:0x00516C10 winning sequence exit] requests destruction of the winning thing (0x50B780) and clears its handle; the script destructor clears DIPMUSIC (0x558513).
	public static bool Dismiss( ParkObjects objects )
	{
		var removed = false;
		// A malformed/restored layout can contain more than one End: none may leave an undismissable duck request behind.
		foreach ( var item in objects.Objects.Where( item => !item.IsDeleted && IsEnd( item.Entry ) ).ToArray() )
			removed |= objects.Remove( item );
		return removed;
	}
}
