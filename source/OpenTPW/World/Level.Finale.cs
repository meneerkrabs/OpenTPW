namespace OpenTPW;

public partial class Level
{
	private bool SupportsResearchFinale => !IsReadOnlyVisit && !GameEditions.IsThemeParkInc && Park?.Economy.Mode == ParkGameMode.InstantAction;

	/// <summary>A live Instant Action finale, including one restored with the park's objects.</summary>
	public bool ResearchFinaleActive => SupportsResearchFinale && ParkFinale.IsActive( Objects );

	private void StartResearchFinale()
	{
		if ( SupportsResearchFinale )
			ParkFinale.StartForResearchCompletion( Objects, Park!.Economy.Mode );
	}

	/// <summary>Rebound on setup and economy-only load; binding never creates an End or replays completed research.</summary>
	private void BindResearchFinale() => Park!.Economy.WinningSequenceActive = () => ResearchFinaleActive;

	/// <summary>The winning screen's exit removes End; the next bank message can close the park again.</summary>
	public bool DismissResearchFinale() => SupportsResearchFinale && ParkFinale.Dismiss( Objects );
}
