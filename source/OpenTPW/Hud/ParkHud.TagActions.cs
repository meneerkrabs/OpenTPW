namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>
	/// The panel buttons of a tag: its two action ids name a button art, a hover help and what the click does. The context word and dword of the post are the action's operands (the buy window's item,
	/// the thing or cell to locate). Actions 22 (the window of table 0x7579C8, 0x4B7B00), 23 (call mechanic) and 25 are in no tag record; 25 and every id without a model have no button.
	/// </summary>
	// [BIN:TPW-EXE:0x48E610 action buttons] the panel's two buttons take their model and help id from the action id: 3 and 4 mb_locate (0x204, 0x203), 9..12 mb_buy (0x209..0x20C), 13..17 mb_buy (0x20D..0x211), 18 b_loan (0x212), 19 mb_resrch (0x213), 20 b_pkinfo (0x214), 21 b_staffcost (0x215), 23 mb_callmech (0x217), 24 b_entry (0xEC), 26 mb_accept, 27 mb_decline; every other id hides the button
	// [BIN:TPW-EXE:0x48E0F0 action handlers] 9..12 call 0x4ACC70 (the buy window with the category button 0x1FB, 0x1FD, 0x1FA, 0x1FC: rides, shops, sideshows, features, and the entry whose id is the context dword's low word, 0x4ACD17-0x4ACD3C), 13..17 call 0x49BDD0 (the hire window of staff type 0..4), 18 0x49FB30 (loans), 19 0x4AA480 (research), 20 0x4A52A0 (park information), 21 0x4B2750 (staff costs), 22 0x4B7B00, 24 0x498D80 (entry price): result 2, the tag is removed (0x100002) and the panel closes (0x48E531-0x48E5E9)
	// [BIN:TPW-EXE:0x48E15B call mechanic] 23 calls the ride's mechanic (0x4DFE30) and returns 1: the panel closes and the tag stays; 3 (0x4867B0) and 4 (0x4868D0) return 0: the panel stays open and so does the tag
	internal GameMessageAction[] TagActions( MessageTag record, short contextShort = 0, int contextInt = 0 )
	{
		if ( record.IsOffer )
			return ChallengeActions( () => level?.Park?.Economy );
		var actions = new List<GameMessageAction>( 2 );
		foreach ( var id in new[] { record.FirstAction, record.SecondAction } )
			if ( TagAction( id, contextInt ) is { } action )
				actions.Add( action );
		return actions.ToArray();
	}

	private GameMessageAction? TagAction( int id, int contextInt )
	{
		static GameMessageAction Make( GameMessageKind kind, int help, Action run, ParkHud owner ) =>
			new( kind, () => { run(); owner.FinishTagAction(); }, help );
		return id switch
		{
			3 => new( GameMessageKind.Locate, () => LocateThing( (ushort)contextInt ), 0x204 ),
			4 => new( GameMessageKind.Locate, () => LocateCell( (ushort)contextInt ), 0x203 ),
			>= 9 and <= 12 => Make( GameMessageKind.Buy, 0x209 + id - 9, () => OpenBuyForTag( (BuildCategory)(id - 9), (ushort)contextInt ), this ),
			>= 13 and <= 17 => Make( GameMessageKind.Buy, 0x20D + id - 13, () => OpenStaffWindowFor( (StaffType)(id - 13) ), this ),
			18 => Make( GameMessageKind.Loans, 0x212, OpenLoansWindow, this ),
			19 => Make( GameMessageKind.Research, 0x213, OpenResearchWindow, this ),
			20 => Make( GameMessageKind.ParkInfo, 0x214, OpenParkInfoWindow, this ),
			21 => Make( GameMessageKind.StaffCost, 0x215, OpenStaffCostsWindow, this ),
			24 => Make( GameMessageKind.Entry, 0xEC, OpenEntryPriceWindow, this ),
			_ => null
		};
	}

	/// <summary>Action 3: the context word is a thing id; the camera jumps to the cell the thing stands in (its bytes +5 and +7), a thing that is none does nothing.</summary>
	// [BIN:TPW-EXE:0x4867B0 locate thing] a word of 0 returns; the thing record of the table 0x7CFB90 (20 bytes a slot) gives the cell bytes +5 and +7 and 0x42AAB0 jumps the camera to (x * 10, z * 10) with no zoom or yaw change
	private void LocateThing( int thingId )
	{
		if ( thingId == 0 )
			return;
		var cell = level.Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.ThingId == thingId )?.ThingCell
			?? level.Guests?.Guests.Where( guest => guest.Id == thingId ).Select( guest => ((int)guest.X, (int)guest.Y) ).Cast<(int X, int Y)?>().FirstOrDefault();
		if ( cell is { } found )
			JumpCameraToCell( found.X, found.Y );
	}

	/// <summary>Action 4: the context dword's low word is a 1-based cell index (z * 128 + x + 1); a word of 0 names no cell.</summary>
	// [BIN:TPW-EXE:0x4868D0 locate cell] x = (word - 1) & 0x7F and z = (word - 1) >> 7, then the same jump 0x42AAB0(x * 10, z * 10); the original reads cell -1 for a word of 0, which no post carries
	private void LocateCell( int cellWord )
	{
		if ( cellWord == 0 )
			return;
		JumpCameraToCell( (cellWord - 1) & 0x7F, (cellWord - 1) >> 7 );
	}

	private void JumpCameraToCell( int x, int y )
	{
		if ( Camera.Mode is not ParkCameraMode camera || level.OriginalPark is not { } park )
			return;
		var position = GuestRenderer.CellToEngine( park.Heightfield, x, y );
		camera.Focus( new System.Numerics.Vector3( position.X, position.Y, position.Z ) );
	}

	private void OpenBuyForTag( BuildCategory category, int infoId )
	{
		OpenBuyWindow();
		if ( !BuyWindowOpen )
			return;
		SelectCategory( category );
		// [BIN:TPW-EXE:0x4ACD17 buy item] a context word that is not 0 selects the list entry with that id (0x664D2A, 0x665739)
		if ( infoId != 0 && BuyItems.ToList().FindIndex( item => item.InfoId == infoId ) is >= 0 and var index )
			SelectBuyItem( index );
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
