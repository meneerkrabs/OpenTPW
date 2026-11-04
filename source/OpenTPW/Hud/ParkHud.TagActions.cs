namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>
	/// The panel buttons of a tag: its two action ids name a button art, a hover help and what the click does. An action the HUD has no counterpart for (the three locate and call-mechanic ones, which need the
	/// advisor's thing and cell words resolved to a world place) gets no button.
	/// </summary>
	// [BIN:TPW-EXE:0x48E610 action buttons] the panel's two buttons take their model and help id from the action id: 3 and 4 mb_locate (0x204, 0x203), 9..12 mb_buy (0x209..0x20C), 13..17 mb_buy (0x20D..0x211), 18 b_loan (0x212), 19 mb_resrch (0x213), 20 b_pkinfo (0x214), 21 b_staffcost (0x215), 23 mb_callmech (0x217), 24 b_entry (0xEC), 26 mb_accept, 27 mb_decline; every other id hides the button
	// [BIN:TPW-EXE:0x48E0F0 action handlers] 9..12 call 0x4ACC70 (the buy window with the category button 0x1FB, 0x1FD, 0x1FA, 0x1FC: rides, shops, sideshows, features, the item the context dword's low word names), 13..17 call 0x49BDD0 (the hire window of staff type 0..4), 18 0x49FB30 (loans), 19 0x4AA480 (research), 20 0x4A52A0 (park information), 21 0x4B2750 (staff costs), 24 0x498D80 (entry price), 3 and 4 locate a thing or a cell, 23 calls the mechanic of a ride; all return 2 but the locate ones' 1: the tag is removed (0x100002) and the panel closes
	internal GameMessageAction[] TagActions( MessageTag record )
	{
		if ( record.IsOffer )
			return ChallengeActions( () => level?.Park?.Economy );
		var actions = new List<GameMessageAction>( 2 );
		foreach ( var id in new[] { record.FirstAction, record.SecondAction } )
			if ( TagAction( id ) is { } action )
				actions.Add( action );
		return actions.ToArray();
	}

	private GameMessageAction? TagAction( int id )
	{
		static GameMessageAction Make( GameMessageKind kind, int help, Action run, ParkHud owner ) =>
			new( kind, () => { run(); owner.FinishTagAction(); }, help );
		return id switch
		{
			>= 9 and <= 12 => Make( GameMessageKind.Buy, 0x209 + id - 9, () => OpenBuyForTag( (BuildCategory)(id - 9) ), this ),
			>= 13 and <= 17 => Make( GameMessageKind.Buy, 0x20D + id - 13, () => OpenStaffWindowFor( (StaffType)(id - 13) ), this ),
			18 => Make( GameMessageKind.Loans, 0x212, OpenLoansWindow, this ),
			19 => Make( GameMessageKind.Research, 0x213, OpenResearchWindow, this ),
			20 => Make( GameMessageKind.ParkInfo, 0x214, OpenParkInfoWindow, this ),
			21 => Make( GameMessageKind.StaffCost, 0x215, OpenStaffCostsWindow, this ),
			24 => Make( GameMessageKind.Entry, 0xEC, OpenEntryPriceWindow, this ),
			_ => null
		};
	}

	private void OpenBuyForTag( BuildCategory category )
	{
		OpenBuyWindow();
		if ( BuyWindowOpen )
			SelectCategory( category );
	}

	private void OpenStaffWindowFor( StaffType type )
	{
		OpenStaffWindow();
		staffFilter = type;
	}

	/// <summary>An action's result 2: the tag is removed and the panel closes (0x48e53b-0x48e5e9).</summary>
	private void FinishTagAction()
	{
		if ( openedChallengeRow is { Visible: true } row )
			BeginTubeRemoval( row, hide: false, closePanelNow: true, disarm: false );
		else
			CloseMessagePanel();
	}
}
