namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-commands] The kinds of park edit command (docs/COOP.md, "The command layer"). Each value is the command's tag in its serialization
/// (<see cref="ParkCommandCodec"/>): a value is never reused or renumbered, and a new command takes a new value.
/// </summary>
public enum ParkCommandKind : byte
{
	/// <summary>Builds a catalogue object at a build target (<see cref="PlaceObjectCommand"/>).</summary>
	PlaceObject = 1,
	/// <summary>The remove tool's click: the object on the cell, else a queue cell, else a path cell (<see cref="RemoveAtCommand"/>).</summary>
	RemoveAt = 2,
	/// <summary>Demolishes the object that covers a cell (<see cref="RemoveObjectAtCommand"/>).</summary>
	RemoveObjectAt = 3,
	/// <summary>Sells one placed object or the prototype ride by its attraction id (<see cref="RemoveAttractionCommand"/>).</summary>
	RemoveAttraction = 4,
	/// <summary>The Move tool's pick-up: demolishes the object so it can be placed again (<see cref="PickUpObjectCommand"/>).</summary>
	PickUpObject = 5,
	/// <summary>The Move tool's end: the player no longer carries the type it picked up (<see cref="EndMoveCommand"/>).</summary>
	EndMove = 6,
	/// <summary>Commits one snapped line of the path or queue tool (<see cref="LayLineCommand"/>).</summary>
	LayLine = 10,
	/// <summary>Takes back one committed line of the path or queue tool (<see cref="UndoLineCommand"/>).</summary>
	UndoLine = 11,
	/// <summary>Removes one path cell (<see cref="RemovePathCellCommand"/>).</summary>
	RemovePathCell = 12,
	/// <summary>Removes a queue cell and the cells behind it (<see cref="RemoveQueueCellCommand"/>).</summary>
	RemoveQueueCell = 13,
	/// <summary>Backspace over a path or queue cell with no tool on (<see cref="BacktrackCommand"/>).</summary>
	Backtrack = 14,
	/// <summary>A click on a queue cell that starts editing the ride's queue (<see cref="EditQueueCommand"/>).</summary>
	EditQueue = 15,
	QueueSegment = 16,
	UndoQueueSegment = 17,
	/// <summary>Places the developer prototype ride (<see cref="PlacePrototypeCommand"/>).</summary>
	PlacePrototype = 20,
	/// <summary>Removes the developer prototype ride (<see cref="RemovePrototypeCommand"/>).</summary>
	RemovePrototype = 21,
	/// <summary>Opens or closes the developer prototype ride (<see cref="SetPrototypeRunningCommand"/>).</summary>
	SetPrototypeRunning = 22,
	/// <summary>The winning screen's exit: removes the Instant Action research finale (<see cref="DismissFinaleCommand"/>).</summary>
	DismissFinale = 23,
	/// <summary>The developer object panel's open-all and close-all (<see cref="SetAllObjectsOpenCommand"/>).</summary>
	SetAllObjectsOpen = 25,
	// the park's settings (C1 part 2, ParkSettingCommands.cs)
	/// <summary>Opens or closes the park's door (<see cref="SetParkOpenCommand"/>).</summary>
	SetParkOpen = 30,
	/// <summary>The entrance fee (<see cref="SetEntranceFeeCommand"/>).</summary>
	SetEntranceFee = 31,
	/// <summary>Opens or closes an attraction (<see cref="SetAttractionOpenCommand"/>).</summary>
	SetAttractionOpen = 32,
	/// <summary>The ride window's speed, capacity and duration (<see cref="SetRideSettingsCommand"/>).</summary>
	SetRideSettings = 33,
	/// <summary>A shop's or sideshow's price (<see cref="SetPriceCommand"/>).</summary>
	SetPrice = 34,
	/// <summary>A winnable sideshow's price, prize and chance of losing (<see cref="SetGamePrizeCommand"/>).</summary>
	SetGamePrize = 35,
	/// <summary>A shop's quality of goods and ingredient amount (<see cref="SetShopSettingsCommand"/>).</summary>
	SetShopSettings = 36,
	/// <summary>A staff role's monthly training budget (<see cref="SetTrainingBudgetCommand"/>).</summary>
	SetTrainingBudget = 37,
	/// <summary>The research effort of a category (<see cref="SetResearchEffortCommand"/>).</summary>
	SetResearchEffort = 38,
	/// <summary>The research lab's workload (<see cref="SetResearchWorkloadCommand"/>).</summary>
	SetResearchWorkload = 39,
	/// <summary>Steps a category's current research project (<see cref="MoveResearchCommand"/>).</summary>
	MoveResearch = 40,
	/// <summary>Takes a loan (<see cref="TakeLoanCommand"/>).</summary>
	TakeLoan = 41,
	/// <summary>Pays off a loan (<see cref="RepayLoanCommand"/>).</summary>
	RepayLoan = 42,
	/// <summary>Asks for a ride's next upgrade (<see cref="BuyUpgradeCommand"/>).</summary>
	BuyUpgrade = 43,
	/// <summary>Cancels a ride's waiting upgrade (<see cref="CancelUpgradeCommand"/>).</summary>
	CancelUpgrade = 44,
	/// <summary>Calls a mechanic or handyman, or cancels the call (<see cref="RequestMaintenanceCommand"/>).</summary>
	RequestMaintenance = 45,
	/// <summary>Accepts or declines the challenge on offer (<see cref="AnswerChallengeCommand"/>).</summary>
	AnswerChallenge = 46,
	/// <summary>Renames an employee (<see cref="RenameStaffCommand"/>).</summary>
	RenameStaff = 47,
	/// <summary>Dismisses an employee (<see cref="FireStaffCommand"/>).</summary>
	FireStaff = 48,
	/// <summary>Sets or clears an employee's patrol area (<see cref="SetPatrolAreaCommand"/>).</summary>
	SetPatrolArea = 49,
	/// <summary>Renames the park (<see cref="RenameParkCommand"/>).</summary>
	RenamePark = 50,
	/// <summary>The game speed (<see cref="SetGameSpeedCommand"/>).</summary>
	SetGameSpeed = 51,
	/// <summary>Queues a cheat for the next park turn (<see cref="QueueCheatCommand"/>).</summary>
	QueueCheat = 52,
	/// <summary>The loans dialog's list build: every offer's credit test is stored (<see cref="RefreshLoanOffersCommand"/>).</summary>
	RefreshLoanOffers = 53,
	// the park's tools (C1 part 3, ParkToolCommands.cs)
	/// <summary>Buys the land between two fence posts (<see cref="BuyLandCommand"/>).</summary>
	BuyLand = 54,
	/// <summary>The hire button: a candidate is being placed (<see cref="BeginStaffHireCommand"/>).</summary>
	BeginStaffHire = 60,
	/// <summary>Ends a hire's placement without hiring (<see cref="CancelStaffHireCommand"/>).</summary>
	CancelStaffHire = 61,
	/// <summary>Hires the candidate being placed onto a cell (<see cref="PlaceStaffCommand"/>).</summary>
	PlaceStaff = 62,
	/// <summary>Picks an employee up into the player's hand (<see cref="PickUpStaffCommand"/>).</summary>
	PickUpStaff = 63,
	/// <summary>Puts the employee in the player's hand down (<see cref="DropStaffCommand"/>).</summary>
	DropStaff = 64,
	/// <summary>Paints one pool cell (<see cref="PaintPoolCellCommand"/>).</summary>
	PaintPoolCell = 70,
	/// <summary>Erases one pool cell (<see cref="ErasePoolCellCommand"/>).</summary>
	ErasePoolCell = 71,
	/// <summary>Takes back a pool stroke (<see cref="UndoPoolStrokeCommand"/>).</summary>
	UndoPoolStroke = 72,
	/// <summary>A wave machine's settings (<see cref="SetPoolWavesCommand"/>).</summary>
	SetPoolWaves = 73,
	/// <summary>Lays one piece of a pool slide (<see cref="LayPoolSlidePieceCommand"/>).</summary>
	LayPoolSlidePiece = 74,
	/// <summary>Takes back a pool slide's last piece (<see cref="UndoPoolSlidePieceCommand"/>).</summary>
	UndoPoolSlidePiece = 75,
	/// <summary>Builds a pool bridge (<see cref="PlacePoolBridgeCommand"/>).</summary>
	PlacePoolBridge = 76,
	/// <summary>Takes a pool bridge down (<see cref="RemovePoolBridgeCommand"/>).</summary>
	RemovePoolBridge = 77,
	/// <summary>Lays one line of a kart or water ride's track (<see cref="LayTrackLineCommand"/>).</summary>
	LayTrackLine = 80,
	/// <summary>Takes back one line of a kart or water ride's track (<see cref="UndoTrackLineCommand"/>).</summary>
	UndoTrackLine = 81,
	/// <summary>Cuts a kart or water ride's closed circuit between two tiles (<see cref="CutTrackCommand"/>).</summary>
	CutTrack = 82,
	/// <summary>Opens a kart or water ride's line tool at its track's open end (<see cref="OpenTrackLineToolCommand"/>).</summary>
	OpenTrackLineTool = 83,
	/// <summary>The coaster tool's entry on a station (<see cref="BeginCoasterToolCommand"/>).</summary>
	BeginCoasterTool = 90,
	/// <summary>The coaster tool's exit (<see cref="EndCoasterToolCommand"/>).</summary>
	EndCoasterTool = 91,
	/// <summary>A click of the coaster tool's build buttons (<see cref="CoasterClickCommand"/>).</summary>
	CoasterClick = 92,
	/// <summary>Backspace in the coaster tool (<see cref="RemoveLastCoasterPylonCommand"/>).</summary>
	RemoveLastCoasterPylon = 93,
	/// <summary>The press of a coaster panel drag (<see cref="BeginCoasterNodeEditCommand"/>).</summary>
	BeginCoasterNodeEdit = 94,
	/// <summary>One step of a coaster panel drag (<see cref="EditCoasterNodeCommand"/>).</summary>
	EditCoasterNode = 95,
	/// <summary>The release of a coaster panel drag (<see cref="EndCoasterNodeEditCommand"/>).</summary>
	EndCoasterNodeEdit = 96,
	/// <summary>The coaster panel's stack buttons (<see cref="SwapCoasterStackCommand"/>).</summary>
	SwapCoasterStack = 97,
	/// <summary>The coaster panel's demolish button (<see cref="CutCoasterCommand"/>).</summary>
	CutCoaster = 98,
	// a co-op session's own commands (C2b)
	/// <summary>[EXT:online-coop-lockstep] The session's clock, the civil time the ride scripts read (<see cref="SetSessionClockCommand"/>).</summary>
	SetSessionClock = 100,
}

/// <summary>
/// [EXT:online-coop-commands] One park edit as a serializable value: what a HUD window or a tool asks the park to change, keyed on cells, catalogue entries,
/// rotations and attraction ids, never on screen positions or the camera. A command applies before a stated simulation tick (<see cref="ParkCommands"/>); applying it is a
/// function of the world state and the command alone, so a recorded log replays to the same world (docs/COOP.md).
/// </summary>
public abstract record ParkCommand
{
	/// <summary>The issuing player's session slot (<see cref="ParkCommands.LocalPlayer"/> in single player): the hook for the host's per-player rights (<see cref="IParkCommandRights"/>).</summary>
	public int Player { get; init; }

	/// <summary>The command's kind and serialization tag.</summary>
	public abstract ParkCommandKind Kind { get; }

	/// <summary>Writes the command's own fields (after the envelope) in their fixed order.</summary>
	internal abstract void WriteFields( ParkCommandWriter writer );

	/// <summary>Whether the command's fields are in range (rotations, modes, coordinates); an out-of-range command is rejected before it touches the park.</summary>
	internal virtual bool IsWellFormed => true;

	/// <summary>Whether the cell is a coordinate pair a park map could hold (non-negative and below the codec's cell bound).</summary>
	internal static bool IsCell( int x, int y ) => x is >= 0 and < ParkCommandCodec.MaximumCoordinate && y is >= 0 and < ParkCommandCodec.MaximumCoordinate;

	/// <summary>Whether the rotation is one of the build tool's four angles.</summary>
	internal static bool IsRotation( int rotation ) => rotation is 0 or 90 or 180 or 270;
}

/// <summary>
/// Builds the catalogue object <paramref name="Entry"/> (its content-root key and archive path, <see cref="ParkCommandCodec.EntryKey"/>) with its build target at
/// (<paramref name="X"/>, <paramref name="Y"/>) turned <paramref name="Rotation"/>: the target of the build tool's click (<see cref="Level.BuildTarget"/>), which the
/// placement centres the footprint on. <paramref name="Moving"/> places a picked-up object again (the Move tool's purchase); <paramref name="AddOnRide"/> is the attraction id
/// of the ride whose track takes a track add-on, or -1.
/// </summary>
public sealed record PlaceObjectCommand( string Entry, int X, int Y, int Rotation, bool Moving = false, int AddOnRide = -1 ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.PlaceObject;
	internal override bool IsWellFormed => Entry.Length > 0 && IsCell( X, Y ) && IsRotation( Rotation ) && AddOnRide >= -1;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.String( Entry );
		writer.Int( X );
		writer.Int( Y );
		writer.Int( Rotation );
		writer.Bool( Moving );
		writer.Int( AddOnRide );
	}
}

/// <summary>The remove tool's click on (<paramref name="X"/>, <paramref name="Y"/>): the object there, else a queue cell, else a path cell.</summary>
public sealed record RemoveAtCommand( int X, int Y ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RemoveAt;
	internal override bool IsWellFormed => IsCell( X, Y );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( X );
		writer.Int( Y );
	}
}

/// <summary>Demolishes the object that covers (<paramref name="X"/>, <paramref name="Y"/>), sold for its scrap value.</summary>
public sealed record RemoveObjectAtCommand( int X, int Y ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RemoveObjectAt;
	internal override bool IsWellFormed => IsCell( X, Y );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( X );
		writer.Int( Y );
	}
}

/// <summary>Sells the placed object (or the prototype ride) whose attraction id is <paramref name="Attraction"/>: the sell buttons of the object and attraction windows.</summary>
public sealed record RemoveAttractionCommand( int Attraction ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RemoveAttraction;
	internal override bool IsWellFormed => Attraction >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Attraction );
}

/// <summary>The Move tool's pick-up of the object whose attraction id is <paramref name="Attraction"/>: it is demolished as the erase button does; the issuer's build tool then arms its type.</summary>
public sealed record PickUpObjectCommand( int Attraction ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.PickUpObject;
	internal override bool IsWellFormed => Attraction >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Attraction );
}

/// <summary>
/// The end of the issuer's Move tool (<see cref="Level.IsMoving"/>): a placement whose click did not hold exactly Ctrl, the queue or track tool a placed ride opens, a cancel, another tool.
/// The player carries the type it picked up no more, so no moving placement of the type is accepted for it until its next pick-up. Nothing else changes: the picked-up object is gone already.
/// </summary>
public sealed record EndMoveCommand : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.EndMove;
	internal override void WriteFields( ParkCommandWriter writer ) { }
}

/// <summary>
/// One committed line of the path tool (<paramref name="Mode"/> <see cref="CellToolMode.Path"/>) or of a ride's queue tool (<see cref="CellToolMode.Queue"/>, <paramref name="Ride"/> the
/// ride's attraction id, else -1), from <paramref name="StartX"/>, <paramref name="StartY"/> toward <paramref name="EndX"/>, <paramref name="EndY"/> (snapped by the writer).
/// </summary>
public sealed record LayLineCommand( CellToolMode Mode, int Ride, int StartX, int StartY, int EndX, int EndY ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.LayLine;
	internal override bool IsWellFormed => ParkCommandCodec.IsLineMode( Mode, Ride ) && IsCell( StartX, StartY ) && IsCell( EndX, EndY );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Byte( (byte)Mode );
		writer.Int( Ride );
		writer.Int( StartX );
		writer.Int( StartY );
		writer.Int( EndX );
		writer.Int( EndY );
	}
}

/// <summary>
/// Takes back one committed line of the path or queue tool (Backspace inside the tool): the cells the line built (<paramref name="Built"/>, in the order they were laid) and the
/// existing path cells whose placement counter it bumped (<paramref name="Existing"/>). <paramref name="Mode"/> and <paramref name="Ride"/> as for <see cref="LayLineCommand"/>.
/// </summary>
public sealed record UndoLineCommand( CellToolMode Mode, int Ride, int StartX, int StartY, IReadOnlyList<(int X, int Y)> Built, IReadOnlyList<(int X, int Y)> Existing ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.UndoLine;
	internal override bool IsWellFormed => ParkCommandCodec.IsLineMode( Mode, Ride ) && IsCell( StartX, StartY ) && Built.Count <= ParkCommandCodec.MaximumCells
		&& Existing.Count <= ParkCommandCodec.MaximumCells && Built.All( cell => IsCell( cell.X, cell.Y ) ) && Existing.All( cell => IsCell( cell.X, cell.Y ) );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Byte( (byte)Mode );
		writer.Int( Ride );
		writer.Int( StartX );
		writer.Int( StartY );
		writer.Cells( Built );
		writer.Cells( Existing );
	}

	/// <summary>Equal when every field and both cell lists are equal (a record compares lists by reference).</summary>
	public bool Equals( UndoLineCommand? other ) => other is not null && Player == other.Player && Mode == other.Mode && Ride == other.Ride && StartX == other.StartX
		&& StartY == other.StartY && Built.SequenceEqual( other.Built ) && Existing.SequenceEqual( other.Existing );

	public override int GetHashCode() => (int)Mode ^ Ride ^ StartX ^ StartY ^ Built.Count ^ Existing.Count;
}

/// <summary>Removes the path cell at (<paramref name="X"/>, <paramref name="Y"/>) (forced, no refund; the map's fixed initial path stays).</summary>
public sealed record RemovePathCellCommand( int X, int Y ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RemovePathCell;
	internal override bool IsWellFormed => IsCell( X, Y );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( X );
		writer.Int( Y );
	}
}

/// <summary>Removes the queue cell at (<paramref name="X"/>, <paramref name="Y"/>) and every cell behind it, with the remove tool's refund.</summary>
public sealed record RemoveQueueCellCommand( int X, int Y ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RemoveQueueCell;
	internal override bool IsWellFormed => IsCell( X, Y );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( X );
		writer.Int( Y );
	}
}

/// <summary>Backspace in the park view with no build tool on, over (<paramref name="X"/>, <paramref name="Y"/>): one placement off a path cell, or the queue from that cell back.</summary>
public sealed record BacktrackCommand( int X, int Y ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.Backtrack;
	internal override bool IsWellFormed => IsCell( X, Y );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( X );
		writer.Int( Y );
	}
}

/// <summary>A click on the queue cell (<paramref name="X"/>, <paramref name="Y"/>) that edits its ride's queue: the queue is walked again and its link to the path it ends on is taken off.</summary>
public sealed record EditQueueCommand( int X, int Y ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.EditQueue;
	internal override bool IsWellFormed => IsCell( X, Y );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( X );
		writer.Int( Y );
	}
}

/// <summary>[EXT:developer-prototype] Places the developer prototype ride at the engine position (<paramref name="X"/>, <paramref name="Y"/>, <paramref name="Z"/>), stored by bit pattern.</summary>
public sealed record PlacePrototypeCommand( float X, float Y, float Z ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.PlacePrototype;
	internal override bool IsWellFormed => float.IsFinite( X ) && float.IsFinite( Y ) && float.IsFinite( Z );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Float( X );
		writer.Float( Y );
		writer.Float( Z );
	}
}

/// <summary>[EXT:developer-prototype] Removes the developer prototype ride.</summary>
public sealed record RemovePrototypeCommand : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RemovePrototype;
	internal override void WriteFields( ParkCommandWriter writer ) { }
}

/// <summary>[EXT:developer-prototype] Opens (<paramref name="Running"/> true) or closes the developer prototype ride: the developer panel's open and close button.</summary>
public sealed record SetPrototypeRunningCommand( bool Running ) : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.SetPrototypeRunning;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Bool( Running );
}

/// <summary>The winning screen's exit (its Resume Game button and its Escape and right click): removes the Instant Action research finale's End object (<see cref="Level.DismissResearchFinale"/>).</summary>
public sealed record DismissFinaleCommand : ParkCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.DismissFinale;
	internal override void WriteFields( ParkCommandWriter writer ) { }
}
