namespace OpenTPW;

/// <summary>
/// The player's record list of the parks whose research is complete (tp.exe's per-theme player records), as a park reaches it. OpenTPW has one player
/// ([EXT:no-player-profiles]), so the list lives in the player's own progress file (<see cref="PlayerTicketLedger.ResearchCompleted"/>).
/// </summary>
internal interface IPlayerResearchRecords
{
	/// <summary>Records that the research of the park of <paramref name="theme"/> is complete (tp.exe <c>0x5B0CA0</c>).</summary>
	void RecordCompleted( string theme );

	/// <summary>Whether every park's research is complete once the park of <paramref name="theme"/>'s is (tp.exe <c>0x5B0C40</c>).</summary>
	bool CompletedEverywhereWith( string theme );
}

public partial class Level
{
	/// <summary>The player's record list of completed parks, attached by the game flow; null when no player progress belongs to this park.</summary>
	internal IPlayerResearchRecords? ResearchRecords { get; set; }

	// [BIN:TPW-EXE:0x00503EC4 finale game types] the completion handler starts the finale in game type 2 (Instant Action) and in type 0 (Full Simulation) when the player's records allow it; type 1 returns
	private bool SupportsResearchFinale => !IsReadOnlyVisit && !GameEditions.IsThemeParkInc && Park?.Economy.Mode is ParkGameMode.InstantAction or ParkGameMode.FullSimulation;

	/// <summary>A live finale, including one restored with the park's objects.</summary>
	public bool ResearchFinaleActive => SupportsResearchFinale && ParkFinale.IsActive( Objects );

	/// <summary>
	/// Whether the completion of this park's research starts the end-of-park finale (and its advice 167): always in Instant Action, in the Full Simulation only when the
	/// player's records show every park's research complete with this one's.
	/// </summary>
	// [BIN:TPW-EXE:0x00503EF7 full simulation finale] game type 0 calls 0x5B0C40 on the player object and returns when it is 0; type 2 goes straight to the advice (0x503ED5)
	internal bool ResearchCompletionStartsFinale() => SupportsResearchFinale && Park!.Economy.Mode switch
	{
		ParkGameMode.InstantAction => true,
		ParkGameMode.FullSimulation => ResearchRecords?.CompletedEverywhereWith( LevelName ) == true,
		_ => false,
	};

	private void StartResearchFinale()
	{
		if ( !SupportsResearchFinale )
			return;
		// [BIN:TPW-EXE:0x00503E94 research record] the current theme's record is marked complete in every game type, before the game type is tested
		ResearchRecords?.RecordCompleted( LevelName );
		if ( !ResearchCompletionStartsFinale() )
			return;
		// [BIN:TPW-EXE:0x00516B8B finale construction] End is constructed with the common object constructor 0x004DB090, so it gets its economy record like an arrival vehicle
		if ( ParkFinale.StartForResearchCompletion( Objects ) is { } finale )
			BindFixedItemMadeInPlay( finale );
	}

	/// <summary>[EXT:debug-hud] Builds End as a completion that starts the finale would, without the research or the records (captures); false without an End definition.</summary>
	internal bool DebugStartFinale()
	{
		if ( Park == null || ParkFinale.StartForResearchCompletion( Objects ) is not { } finale )
			return false;
		BindFixedItemMadeInPlay( finale );
		return true;
	}

	/// <summary>Rebound on setup and economy-only load; binding never creates an End or replays completed research.</summary>
	private void BindResearchFinale() => Park!.Economy.WinningSequenceActive = () => ResearchFinaleActive;

	/// <summary>The winning screen's exit removes End; the next bank message can close the park again.</summary>
	public bool DismissResearchFinale() => SupportsResearchFinale && ParkFinale.Dismiss( Objects );
}
