namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-commands] A command of the park's tools (docs/COOP.md, "Tools"): what the land, staff, pool and track tools write into the world (C1 part 3). The tool keeps
/// its own state between clicks (a stored corner, a stroke, the employee in hand, a tile picked for a cut) and issues one command for each write it made before the command layer,
/// at the same moment, so a single-player run is unchanged. The command checks what its write would refuse, then makes it through the park's own call; it never reads or changes
/// a tool, so the same command does the same on every peer and in a replay. The issuer moves its tool on (ends it, continues it) from the command's result.
/// </summary>
public abstract record ParkToolCommand : ParkCommand
{
	/// <summary>Runs the command on <paramref name="level"/>: accepted, or rejected without a change.</summary>
	internal abstract ParkCommandResult Execute( Level level );

	/// <summary>
	/// Runs <paramref name="command"/> for <see cref="Level.ExecuteCommand"/>. A park call that throws (a rule the command's own checks missed, for example a hostile peer's or a
	/// damaged log's command) rejects the command rather than end the game loop; the throw is the same on every peer, so they stay together.
	/// </summary>
	internal static ParkCommandResult Run( Level level, ParkToolCommand command )
	{
		try
		{
			return command.Execute( level );
		}
		catch ( Exception error ) when ( error is InvalidOperationException or ArgumentException or KeyNotFoundException )
		{
			return ParkCommandResult.Rejected( $"The park refused the {command.Kind} command: {error.Message}" );
		}
	}

	/// <summary>Reads the fields of the tool command of <paramref name="kind"/> (<see cref="ParkCommandCodec.Decode"/>), or null for a kind that is not a tool command.</summary>
	internal static ParkToolCommand? Read( ParkCommandKind kind, ParkCommandReader reader ) => kind switch
	{
		ParkCommandKind.BuyLand => new BuyLandCommand( reader.Int(), reader.Int(), reader.Int(), reader.Int() ),
		ParkCommandKind.BeginStaffHire => new BeginStaffHireCommand( reader.Int() ),
		ParkCommandKind.CancelStaffHire => new CancelStaffHireCommand(),
		ParkCommandKind.PlaceStaff => new PlaceStaffCommand( reader.Int(), reader.Int(), reader.Int() ),
		ParkCommandKind.PickUpStaff => new PickUpStaffCommand( reader.Int() ),
		ParkCommandKind.DropStaff => new DropStaffCommand( reader.Int(), reader.Int(), reader.Int() ),
		ParkCommandKind.PaintPoolCell => new PaintPoolCellCommand( reader.Int(), reader.Int(), reader.Bool() ),
		ParkCommandKind.ErasePoolCell => new ErasePoolCellCommand( reader.Int(), reader.Int() ),
		ParkCommandKind.UndoPoolStroke => new UndoPoolStrokeCommand( reader.Bool(), reader.Cells(), reader.Cells() ),
		ParkCommandKind.SetPoolWaves => new SetPoolWavesCommand( reader.Int(), reader.Bool(), reader.Int(), reader.Int() ),
		ParkCommandKind.LayPoolSlidePiece => new LayPoolSlidePieceCommand( reader.Int(), reader.Int(), reader.Int(), reader.Int() ),
		ParkCommandKind.UndoPoolSlidePiece => new UndoPoolSlidePieceCommand( reader.Int(), reader.Int() ),
		ParkCommandKind.PlacePoolBridge => new PlacePoolBridgeCommand( reader.Int(), reader.Int(), reader.Int() ),
		ParkCommandKind.RemovePoolBridge => new RemovePoolBridgeCommand( reader.Int(), reader.Int() ),
		ParkCommandKind.PoolCells => new PoolCellsCommand( reader.Bool(), reader.Bool(), reader.Cells() ),
		ParkCommandKind.LayPoolSlidePieces => new LayPoolSlidePiecesCommand( reader.Int(), reader.Int(), reader.Cells() ),
		ParkCommandKind.LayTrackLine => new LayTrackLineCommand( reader.Int(), reader.Int(), reader.Int(), reader.Int(), reader.Int() ),
		ParkCommandKind.UndoTrackLine => new UndoTrackLineCommand( reader.Int(), reader.Int(), reader.Int(), reader.Int(), reader.Int() ),
		ParkCommandKind.CutTrack => new CutTrackCommand( reader.Int(), reader.Int(), reader.Int(), reader.Int(), reader.Int() ),
		ParkCommandKind.OpenTrackLineTool => new OpenTrackLineToolCommand( reader.Int() ),
		ParkCommandKind.BeginCoasterTool => new BeginCoasterToolCommand( reader.Int() ),
		ParkCommandKind.EndCoasterTool => new EndCoasterToolCommand( reader.Int() ),
		ParkCommandKind.CoasterClick => new CoasterClickCommand( reader.Int(), reader.Int(), reader.Int(), reader.Float(), reader.Bool(), reader.Int() ),
		ParkCommandKind.RemoveLastCoasterPylon => new RemoveLastCoasterPylonCommand( reader.Int() ),
		ParkCommandKind.BeginCoasterNodeEdit => new BeginCoasterNodeEditCommand( reader.Int(), reader.Int(), (CoasterDrag)reader.Byte() ),
		ParkCommandKind.EditCoasterNode => new EditCoasterNodeCommand( reader.Int(), (CoasterNodeField)reader.Byte(), reader.Float(), reader.Int(), reader.Int() ),
		ParkCommandKind.EndCoasterNodeEdit => new EndCoasterNodeEditCommand( reader.Int() ),
		ParkCommandKind.SwapCoasterStack => new SwapCoasterStackCommand( reader.Int(), reader.Int(), reader.Bool() ),
		ParkCommandKind.CutCoaster => new CutCoasterCommand( reader.Int(), reader.Int(), reader.Int() ),
		_ => null
	};
}

/// <summary>
/// The Buy Land tool's second click (<see cref="BuyLandTool.Click"/>): buys the rectangle between the fence post the first click stored (<paramref name="CornerX"/>,
/// <paramref name="CornerY"/>) and the clicked post (<paramref name="PostX"/>, <paramref name="PostY"/>), both on the fence's post lattice. The validator runs again with the money
/// test (<see cref="ParkLand.CheckPurchase"/>); the value is its verdict, accepted or not.
/// </summary>
public sealed record BuyLandCommand( int CornerX, int CornerY, int PostX, int PostY ) : ParkToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.BuyLand;
	internal override bool IsWellFormed => IsCell( CornerX, CornerY ) && IsCell( PostX, PostY );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( CornerX );
		writer.Int( CornerY );
		writer.Int( PostX );
		writer.Int( PostY );
	}

	internal override ParkCommandResult Execute( Level level ) => level.Guests?.Grid.Cells is { } map && level.LandBarrier is { } barrier
		? Apply( map, level.Park?.Economy, barrier )
		: ParkCommandResult.Rejected( "The park has no land for sale." );

	/// <summary>The purchase on a cell map with its fence, the economy that pays (null: money off) and the fence's barrier cells (<see cref="Level.LandBarrier"/>).</summary>
	internal ParkCommandResult Apply( ParkCellMap map, ParkEconomy? economy, Func<int, int, bool> barrier )
	{
		if ( map.Land is not { } land )
			return ParkCommandResult.Rejected( "The park has no land for sale." );
		// a Theme Park Inc park sells no land (its economy throws): the buy window offers no Buy Land row there
		if ( economy is { Rules.ThemeParkInc: true } )
			return ParkCommandResult.Rejected( "A Theme Park Inc park buys no land." );
		var corner = (CornerX, CornerY);
		var post = (PostX, PostY);
		// the tool snaps both clicks to the fence's post lattice: a cell off it is no post a click could pick
		if ( BuyLandTool.Snap( CornerX, CornerY, land.PostA, map.Width, map.Height ) != corner || BuyLandTool.Snap( PostX, PostY, land.PostA, map.Width, map.Height ) != post )
			return ParkCommandResult.Rejected( "The land tool's corners are fence posts." );
		var check = land.CheckPurchase( map, corner, post, barrier, economy?.CellCost( CellPurchase.Land ) ?? 0, economy?.Balance );
		if ( !check.Allowed || land.Merge( corner, post ) is not { } merged )
			return new ParkCommandResult( false, $"The land cannot be bought: {check.Refusal}.", check );
		// the purchase and its debit in the order the tool's click made them (BuyLandTool.Click)
		map.SetLand( merged );
		merged.ClaimEnclosedCells( map );
		economy?.ChargeLand( check.Cost, check.Cells );
		return ParkCommandResult.Ok( check );
	}
}

/// <summary>
/// A command of the hire and the employee in hand (<see cref="StaffPlacement"/>): it acts on the level's <see cref="StaffHands"/>, the park's economy and cells and the employee each player holds.
/// </summary>
public abstract record StaffToolCommand : ParkToolCommand
{
	internal sealed override ParkCommandResult Execute( Level level ) =>
		level.StaffHands is { } hands ? Apply( hands ) : ParkCommandResult.Rejected( "The park employs no staff." );

	/// <summary>Runs the command on <paramref name="hands"/>: accepted, or rejected without a change.</summary>
	internal abstract ParkCommandResult Apply( StaffHands hands );

	/// <summary>The rejection of a command in a park that employs no staff (Theme Park Inc, the generic sandbox).</summary>
	private protected static ParkCommandResult NoStaff => ParkCommandResult.Rejected( "The park employs no staff." );

	/// <summary>The candidate <paramref name="id"/> of the pool when the park may hire it now: not bankrupt, the role not full.</summary>
	private protected static StaffCandidate? Hireable( ParkEconomy economy, int id ) =>
		!economy.IsBankrupt && economy.Staff.Candidates.FirstOrDefault( item => item.Id == id ) is { } candidate && economy.Staff.CanHire( candidate.Type ) ? candidate : null;
}

/// <summary>
/// The hire window's hire button (<see cref="StaffPlacement.BeginHire"/>): the candidate <paramref name="Candidate"/> stays in the pool, marked as being placed (it does not expire), until
/// a <see cref="PlaceStaffCommand"/> hires it or a <see cref="CancelStaffHireCommand"/> ends the placement.
/// </summary>
public sealed record BeginStaffHireCommand( int Candidate ) : StaffToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.BeginStaffHire;
	internal override bool IsWellFormed => Candidate > 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Candidate );

	internal override ParkCommandResult Apply( StaffHands hands )
	{
		if ( hands.Economy is not { Rules.Staff: true } economy )
			return NoStaff;
		if ( Hireable( economy, Candidate ) is not { } candidate )
			return ParkCommandResult.Rejected( $"Candidate {Candidate} cannot be hired." );
		economy.Staff.PendingPlacementId = candidate.Id;
		return ParkCommandResult.Ok( candidate );
	}
}

/// <summary>Ends a hire's placement without hiring (the Place tool's right click, Escape, another tool, a save): the candidate stays in the pool and may expire again.</summary>
public sealed record CancelStaffHireCommand : StaffToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.CancelStaffHire;
	internal override void WriteFields( ParkCommandWriter writer ) { }

	internal override ParkCommandResult Apply( StaffHands hands )
	{
		if ( hands.Economy is not { } economy )
			return NoStaff;
		economy.Staff.EndPlacement( placed: false );
		return ParkCommandResult.Ok();
	}
}

/// <summary>
/// The Place tool's click on a valid cell (<see cref="StaffPlacement.Click"/>): hires the candidate being placed (<see cref="BeginStaffHireCommand"/>) and puts the new employee on the cell
/// (<paramref name="X"/>, <paramref name="Y"/>), patrolling the whole map. The value is the new employee.
/// </summary>
public sealed record PlaceStaffCommand( int Candidate, int X, int Y ) : StaffToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.PlaceStaff;
	internal override bool IsWellFormed => Candidate > 0 && IsCell( X, Y );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Candidate );
		writer.Int( X );
		writer.Int( Y );
	}

	internal override ParkCommandResult Apply( StaffHands hands )
	{
		if ( hands.Economy is not { Rules.Staff: true } economy )
			return NoStaff;
		if ( economy.Staff.PendingPlacementId != Candidate )
			return ParkCommandResult.Rejected( $"Candidate {Candidate} is not being placed." );
		if ( !hands.IsValidCell( X, Y ) )
			return ParkCommandResult.Rejected( "Cannot place staff member here." );
		if ( Hireable( economy, Candidate ) is not { } candidate )
			return ParkCommandResult.Rejected( $"Candidate {Candidate} cannot be hired." );
		// a full thing pool refuses the hire before the placement ends (the hire would throw after it)
		if ( economy.Staff.Things is { NextId: 0 } )
			return ParkCommandResult.Rejected( $"The thing pool is full ({ThingPool.Capacity} things); no staff member can be placed." );
		economy.Staff.EndPlacement( placed: true );
		var member = economy.Hire( candidate.Id );
		// [BIN:TPW-EXE:0x46c8e0 new member] position = cell centre (sub-cell 0x80), happiness and energy 100.0 (the member's own defaults), patrol rectangle = the whole map (+0x20a = 1, +0x20c = 0x4000: PatrolArea's default); each role then decides its first job (state 0 here, the state machines' search runs on the next update)
		member.PatrolArea = (0, 0, ParkLitter.MapSize - 1, ParkLitter.MapSize - 1);
		StaffPlacement.PutDown( member, X, Y );
		return ParkCommandResult.Ok( member );
	}
}

/// <summary>
/// The employee window's pick-up button (<see cref="StaffPlacement.PickUp"/>): the employee <paramref name="Staff"/> leaves its job and is in the issuing player's hand
/// (<see cref="StaffState.PickedUp"/>) until a <see cref="DropStaffCommand"/> puts it down. The value is the cell it was picked up from.
/// </summary>
public sealed record PickUpStaffCommand( int Staff ) : StaffToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.PickUpStaff;
	internal override bool IsWellFormed => Staff > 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Staff );

	internal override ParkCommandResult Apply( StaffHands hands )
	{
		if ( hands.Economy is not { } economy || hands.Cells is not { } map )
			return NoStaff;
		if ( economy.Staff.Members.FirstOrDefault( item => item.Id == Staff ) is not { } member || !StaffPlacementRules.CanBePickedUp( member, map ) )
			return ParkCommandResult.Rejected( $"Employee {Staff} cannot be picked up." );
		if ( hands.Of( Player ) != null )
			return ParkCommandResult.Rejected( $"Player {Player} already holds an employee." );
		var origin = StaffPlacementRules.CellOf( member )!.Value;
		StaffPlacement.ReleaseAssignments( economy, member );
		member.State = StaffState.PickedUp;
		// state 7 takes the thought bubble away (0x005054D0 → 0x0050C0D0)
		member.BubbleThought = 0;
		hands.Carry( Player, member.Id, origin );
		return ParkCommandResult.Ok( origin );
	}
}

/// <summary>
/// Puts the employee <paramref name="Staff"/> in the issuing player's hand down on the cell (<paramref name="X"/>, <paramref name="Y"/>): the Carry tool's click on a valid cell
/// (<see cref="StaffPlacement.Click"/>), or the cell it was picked up from, which the tool's cancel and a save drop it back on without a cell test.
/// </summary>
public sealed record DropStaffCommand( int Staff, int X, int Y ) : StaffToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.DropStaff;
	internal override bool IsWellFormed => Staff > 0 && IsCell( X, Y );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Staff );
		writer.Int( X );
		writer.Int( Y );
	}

	internal override ParkCommandResult Apply( StaffHands hands )
	{
		if ( hands.Economy is not { } economy )
			return NoStaff;
		if ( hands.Of( Player ) is not { } hand || hand.Staff != Staff )
			return ParkCommandResult.Rejected( $"Player {Player} does not hold employee {Staff}." );
		if ( (X, Y) != (hand.X, hand.Y) && !hands.IsValidCell( X, Y ) )
			return ParkCommandResult.Rejected( "Cannot place staff member here." );
		hands.Empty( Player );
		// an employee dismissed while in hand leaves nothing to put down
		if ( economy.Staff.Members.FirstOrDefault( item => item.Id == Staff ) is not { } member )
			return ParkCommandResult.Ok();
		StaffPlacement.ReleaseAssignments( economy, member );
		StaffPlacement.PutDown( member, X, Y );
		return ParkCommandResult.Ok( member );
	}
}
