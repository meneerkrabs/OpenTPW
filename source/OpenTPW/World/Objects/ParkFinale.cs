namespace OpenTPW;

/// <summary>The end-of-park finale's fixed object, End, and its explicit dismissal (docs/reverse/INSTANT-ACTION-FINALE.md).</summary>
public static class ParkFinale
{
	private static bool IsEnd( ObjectCatalogEntry entry ) =>
		entry.IsFixedItem && string.Equals( entry.SettingsName, "End", StringComparison.OrdinalIgnoreCase );

	/// <summary>Whether a live fixed finale object is present, including one restored by the layout loader.</summary>
	// [BIN:TPW-EXE:0x00516C00 winning sequence active] the bank tests whether the finale's thing handle is nonzero.
	public static bool IsActive( ParkObjects objects ) => objects.Objects.Any( item => !item.IsDeleted && IsEnd( item.Entry ) );

	/// <summary>Whether <paramref name="item"/> is a finale object (live or removed).</summary>
	public static bool IsFinaleObject( OriginalObject item ) => IsEnd( item.Entry );

	/// <summary>The live finale object, null when there is none.</summary>
	public static OriginalObject? Find( ParkObjects objects ) => objects.Objects.FirstOrDefault( item => !item.IsDeleted && IsEnd( item.Entry ) );

	/// <summary>
	/// Creates End for a new research completion that starts the finale (the caller decides that: <see cref="Level.ResearchCompletionStartsFinale"/>); opening an already
	/// researched park does not. End takes its slot in the park's thing pool like every object, as the original's constructor allocates one.
	/// </summary>
	// [BIN:TPW-EXE:0x00503ED3 research finale] when nothing is left to research, game type 2 (and type 0 with the player's records complete) posts advice 167 and calls 0x516B00
	// [BIN:TPW-EXE:0x00516B31 finale object] finds End case-insensitively, constructs it at the fixed-item origin and stores its thing handle; 0x481B60 then opens the winning screen (0x48AE70)
	public static OriginalObject? StartForResearchCompletion( ParkObjects objects )
	{
		if ( Find( objects ) is { } existing )
			return existing;
		var entry = objects.Catalog.Entries.FirstOrDefault( IsEnd );
		// Missing/modded catalogues remain usable: no substitute object, charge or claimed victory.
		// [BIN:TPW-EXE:0x00516BC7 missing End] without the definition the original logs "Could not find the %s feature", keeps a zero handle and still calls 0x481B60 with no thing, whose ride camera call (0x4E15B0) reads through it; a catalogue without End starts nothing here
		return entry == null ? null : objects.Add( entry, 0, 0, 0 );
	}

	/// <summary>Removes the finale through normal object cleanup: scripts, ducking, held sounds, particles and thing ownership end with it.</summary>
	// [BIN:TPW-EXE:0x00516C10 winning sequence exit] requests destruction of the winning thing (0x50B780), clears its handle and leaves the ride camera (0x42A190); the script destructor clears DIPMUSIC (0x558513).
	public static bool Dismiss( ParkObjects objects )
	{
		var removed = false;
		// A malformed/restored layout can contain more than one End: none may leave an undismissable duck request behind.
		foreach ( var item in objects.Objects.Where( item => !item.IsDeleted && IsEnd( item.Entry ) ).ToArray() )
			removed |= objects.Remove( item );
		return removed;
	}
}
