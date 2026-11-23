namespace OpenTPW;

public sealed partial class ParkEconomy
{
	/// <summary>The park the award checker measures (a level's cells, staff and attractions); null in a headless economy, which checks no award.</summary>
	internal IThemeParkIncAwardPark? TpiAwardPark { get; set; }

	/// <summary>The latest <see cref="MaxTpiAwardNotices"/> awards the checker decided, for the HUD (the advisor's award messages are not posted). Not saved.</summary>
	internal List<ThemeParkIncAwardNotice> TpiAwardNotices { get; } = new();

	/// <summary>The award notices kept.</summary>
	internal const int MaxTpiAwardNotices = 32;

	/// <summary>
	/// The park awards' check of a world tick (<c>0x5D6600</c>): every 100th tick the checker decides at most one award (<see cref="ThemeParkIncParkAwards.Check"/>) and the event follows.
	/// </summary>
	// [BIN:TPI-EXE:0x5D6600 award gate] the world update's tail (0x5D65E0) runs it after the hiring pool (0x631B40) and the calendar (0x6083D0), in every world state
	// [EXT:headless-economy] a headless economy has no park to measure (no level's cells, staff things or attractions): it checks no award
	private void CheckThemeParkIncAwards( long turn )
	{
		if ( TpiCareer is not { } career || TpiAwardPark is not { } park || !ThemeParkIncParkAwards.IsCheckTick( turn ) )
			return;
		if ( ThemeParkIncParkAwards.Check( career, park, Settings.ThemeParkIncAwards ) is not { } notice )
			return;
		ThemeParkIncAwardEvent( notice );
	}

	/// <summary>
	/// The award event (<c>0x606EC0</c>): the advisor's won or moved message and the awards panel's refresh in the original; the HUD's notice here.
	/// </summary>
	// [BIN:TPI-EXE:0x606EC0 award event] logs "Won a Global golden Award (type - %d, num - %d)"; result 1 (won) by the jump table 0x607AB0, result 2 (moved) by 0x607A90: 0x4313D0(0), the advice
	// 0x71A930(id), a message object 0x4355D0 posted to 0xA09728 through 0x436DD0; any other result logs "CParkAwardControl::TellTheWorld (Global) is being passed an unrecognised type"; then the
	// Park Awards panel's refresh 0x561E30 (presentation); no money and no draw
	// [EXT:tpi-capability-gates] the advice (0xBF, 0x13A..0x13E, 0xC0, 0xC1 won; 0xC3, 0x142..0x146, 0xC4, 0xC5 moved) goes to the advisor's queue, whose descriptor and response rows for these
	// ids OpenTPW does not read (it carries the forecasts only): the award is shown as a note instead ([EXT:tpi-text-hud])
	private void ThemeParkIncAwardEvent( ThemeParkIncAwardNotice notice )
	{
		Log?.Trace( $"Won a Global golden Award (type - {(notice.Moved ? 2 : 1)}, num - {(int)notice.Award})" );
		if ( TpiAwardNotices.Count >= MaxTpiAwardNotices )
			TpiAwardNotices.RemoveAt( 0 );
		TpiAwardNotices.Add( notice );
		Log?.Trace( $"You have {TpiCareer!.AwardsHeld} total Park Awards" );
	}

	/// <summary>The shares game's start-up for the park's career (<c>0x620930</c>, every world build at <c>0x648676</c>; the Global Master Save's load ends with the same steps).</summary>
	// [BIN:TPI-EXE:0x648676 shares init] the new-world init calls 0x620930 on 0xAA0128 after the research lab's 0x61BBD0 and before 0x693270
	private void InitializeThemeParkIncShares( ThemeParkIncCareer career ) => career.Shares.Initialize( Settings.ThemeParkIncShares );
}
