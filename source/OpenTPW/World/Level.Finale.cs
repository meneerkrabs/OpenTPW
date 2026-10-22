namespace OpenTPW;

public partial class Level
{
	private bool SupportsResearchFinale => !IsReadOnlyVisit && !GameEditions.IsThemeParkInc && Park?.Economy.Mode == ParkGameMode.InstantAction;

	/// <summary>A live Instant Action finale, including one restored with the park's objects.</summary>
	public bool ResearchFinaleActive => SupportsResearchFinale && ParkFinale.IsActive( Objects );

	private void StartResearchFinale()
	{
		if ( !SupportsResearchFinale )
			return;
		// a live End is adopted (ParkFinale.StartForResearchCompletion): nothing is constructed, so nothing is recorded or stamped
		var adopted = ParkFinale.IsActive( Objects );
		// [BIN:TPW-EXE:0x00516B8B finale construction] End is constructed with the common object constructor 0x004DB090, so it gets its economy record like an arrival vehicle
		if ( ParkFinale.StartForResearchCompletion( Objects, Park!.Economy.Mode ) is { } finale && !adopted )
			BindFixedItemMadeInPlay( finale );
	}

	/// <summary>Rebound on setup and economy-only load; binding never creates an End or replays completed research.</summary>
	private void BindResearchFinale() => Park!.Economy.WinningSequenceActive = () => ResearchFinaleActive;

	/// <summary>The winning screen's exit removes End; the next bank message can close the park again.</summary>
	public bool DismissResearchFinale() => SupportsResearchFinale && ParkFinale.Dismiss( Objects );
}
