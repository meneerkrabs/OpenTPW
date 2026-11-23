namespace OpenTPW;

public sealed partial class ThemeParkIncHud
{
	/// <summary><c>UITEXT.str</c> entry 452, "Park Awards - ", which the awards panel puts before an award's name.</summary>
	internal const int ParkAwardsPrefix = 452;
	/// <summary><c>UITEXT.str</c> entry 453, the first award's name ("Highest Coaster"); the eight names follow in the checker's order.</summary>
	internal const int FirstAwardName = 453;
	/// <summary><c>UITEXT.str</c> entries 24, 23 and 25: "Shares Owned :", "Shareholding % :" and "Share Price :".</summary>
	internal const int SharesOwnedLabel = 24, ShareholdingLabel = 23, SharePriceLabel = 25;

	/// <summary>An award's name (<c>UITEXT.str</c> 453 + the award).</summary>
	internal string AwardName( ThemeParkIncAward award ) => strings.Entry( FirstAwardName + (int)award );

	/// <summary>
	/// The career's shares and park awards as one line of the challenge panel: the shares owned, the percentage and the price, then the awards held, each in the game's own words.
	/// </summary>
	// [BIN:TPI-EXE:0x582890 shares panel refresh] the native share portfolio panel (0x581A90, opened from 0x54F73A) shows the holding (0x6220B0), its value (0x6220D0), the price (0x622490), the
	// percentage (0x622100) and the company's name (0x622450), and the Park Awards panel (0x5625D0..) lists UITEXT 453..460 with the awards held
	// [EXT:tpi-text-hud] both native panels are not ported (the share panel's colours draw from the C runtime's rand(), 0x5813A1, 0x581ABE): the text HUD's challenge panel shows their values in a line
	internal string SharesAndAwardsText()
	{
		if ( level.Park?.Economy is not { TpiCareer: { } career } )
			return "";
		var shares = career.Shares;
		var held = Enum.GetValues<ThemeParkIncAward>().Where( award => career.Awards[(int)award] != 0 ).Select( AwardName ).ToArray();
		return $"{strings.Entry( SharesOwnedLabel )} {shares.Owned}   {strings.Entry( ShareholdingLabel )} {shares.PercentOwned}   {strings.Entry( SharePriceLabel )} {shares.Price}   "
			+ strings.Entry( ParkAwardsPrefix ) + string.Join( ", ", held );
	}

	/// <summary>The awards the checker decided, as notes: "Park Awards - " and the award's name (the advisor's award messages stand behind them in the original).</summary>
	// [EXT:tpi-text-hud] the award advice (0x606EC0) is shown as a note in the game's own words, UITEXT 452 and the award's name
	internal void ShowAwardNotices()
	{
		if ( level.Park?.Economy is not { TpiAwardNotices.Count: > 0 } economy )
			return;
		foreach ( var notice in economy.TpiAwardNotices )
			PostMessage( strings.Entry( ParkAwardsPrefix ) + AwardName( notice.Award ) );
		economy.TpiAwardNotices.Clear();
	}
}
