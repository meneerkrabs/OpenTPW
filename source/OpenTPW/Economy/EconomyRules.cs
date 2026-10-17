namespace OpenTPW;

/// <summary>
/// Which parts of the park economy a <see cref="ParkEconomy"/> runs and which edition's money rules it follows. Theme Park World runs
/// everything (<see cref="Full"/>). A Theme Park Inc park runs only its money (<see cref="ThemeParkIncMoney"/>): the bank, loans, the
/// calendar, build/path/queue/track charges, scrap refunds, running costs and bankruptcy, because Theme Park Inc's own data carries only
/// those balance keys (docs/THEME-PARK-INC.md, "Economy"). Staff, research and challenges are refused there, so nothing that needs them runs.
/// </summary>
/// <param name="Staff">Staff hiring, wages, vitals, mechanics and handymen, and therefore ride wear, breakdowns and repairs.</param>
/// <param name="Research">The research table that gates what may be built, and the lab.</param>
/// <param name="Challenges">Challenges and golden tickets.</param>
/// <param name="ThemeParkInc">Theme Park Inc's money rules: monthly running costs, the scrap value's condemned-object branch, no Buy Land, no upgrade purchase (it needs a mechanic).</param>
public sealed record EconomyRules( bool Staff, bool Research, bool Challenges, bool ThemeParkInc )
{
	/// <summary>The Theme Park World economy, everything on (what every park used before the edition split).</summary>
	public static EconomyRules Full { get; } = new( true, true, true, false );

	/// <summary>The Theme Park Inc money slice.</summary>
	public static EconomyRules ThemeParkIncMoney { get; } = new( false, false, false, true );

	/// <summary>The money rules of the capability policy of the running edition.</summary>
	public static EconomyRules For( EditionCapabilities capabilities ) => new( capabilities.Staff, capabilities.Research, capabilities.Challenges, ThemeParkInc: GameEditions.IsThemeParkInc );

	/// <summary>The features a mode offers, less the ones this economy's refused systems take with them.</summary>
	public ParkModeFeatures Narrow( ParkModeFeatures features ) => this == Full ? features : features with
	{
		GoldenTickets = features.GoldenTickets && Challenges,
		Challenges = features.Challenges && Challenges,
		ResearchPanel = features.ResearchPanel && Research,
		// an upgrade is installed by a mechanic (tp.exe 0x004E0050 calls one; the cost is debited when the job ends), so it needs the staff
		Upgrades = features.Upgrades && Staff
	};
}
