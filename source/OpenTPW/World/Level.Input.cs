namespace OpenTPW;

/// <summary>
/// The level's tool layer: the park click, the remove tool, the Move tool's pick-up and the queue edit click. [EXT:online-coop-commands] Each edit leaves here as a
/// <see cref="ParkCommand"/> through <see cref="Commands"/>; what stays here is the tools' own state (the armed entry, the build angle, the line tool's stack), which no
/// command reads. The command layer's static test keeps every park mutator out of this file (docs/COOP.md).
/// </summary>
public partial class Level
{
	internal enum WorldClickAction { None, PlacePrototype, UseTool }

	/// <summary>World input stays suspended in ride and walking views, including tools armed through the HUD.</summary>
	internal static WorldClickAction WorldClick( bool riding, bool leftDown, bool previousLeftDown, bool mouseCaptured, bool placing ) =>
		riding || !leftDown || previousLeftDown || mouseCaptured ? WorldClickAction.None
			: placing ? WorldClickAction.PlacePrototype : WorldClickAction.UseTool;

	/// <param name="modifierMask">The modifier mask held at the click (<see cref="Input.ModifierMask"/>: Shift 3, Ctrl 0xc, Alt 0x30); exactly Ctrl keeps the build tool armed after a placement.</param>
	internal void UpdateWorldInput( bool leftDown, bool mouseCaptured, Vector2 mousePosition, Vector2 viewportSize, int modifierMask = 0 )
	{
		clickModifierMask = modifierMask;
		switch ( WorldClick( RideView != null || FreeWalk != null, leftDown, wasMouseDown, mouseCaptured, IsPlacing ) )
		{
			case WorldClickAction.PlacePrototype:
				if ( TryGetPlacementPosition( mousePosition, viewportSize, out var position ) )
					Commands.Issue( new PlacePrototypeCommand( position.X, position.Y, position.Z ) );
				break;
			case WorldClickAction.UseTool:
				HandleObjectClick( mousePosition, viewportSize );
				break;
		}
		// Track the button in both cameras: a held click must not build when the player leaves a ride.
		wasMouseDown = leftDown;
	}

	private void HandleObjectClick( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize )
	{
		if ( IsReadOnlyVisit )
			return;
		if ( (BuildEntry == null && !IsRemovingObjects && !CellTool.IsActive && !IsCoasterToolActive) || !TryGetGridCell( mousePosition, viewportSize, out var x, out var y ) )
			return;
		// The remove tool comes first, so a path or queue cell under it is removed rather than built on (ParkHud stands aside for it too).
		if ( IsRemovingObjects )
			Commands.Issue( new RemoveAtCommand( x, y ) );
		else if ( IsCoasterToolActive && BuildEntry == null )
			CoasterClick( x, y );
		else if ( CellTool.IsActive && BuildEntry == null )
			ClickCellTool( (x, y), clickModifierMask );
		else
		{
			KeepBuildToolOnPlace = KeepsBuildTool( clickModifierMask );
			try
			{
				// a track add-on goes to its hover position on its ride's track, at the angle of the tile's flow (TrackAddOns.HoverTarget); anything else stays at the cell
				var entry = BuildEntry!;
				var (targetX, targetY, rotation) = BuildTarget( entry, x, y );
				SetBuildRotation( rotation );
				Commands.Issue( BuildCommand( entry, targetX, targetY, rotation ) );
			}
			finally
			{
				KeepBuildToolOnPlace = false;
			}
		}
	}

	// A live track click's continuation choice is local tool state, never a command or world input.
	private bool keepTrackToolAfterLine;

	internal SegmentResult? ClickCellTool( (int X, int Y) cell, int modifierMask = 0 )
	{
		var previous = keepTrackToolAfterLine;
		keepTrackToolAfterLine = KeepsBuildTool( modifierMask );
		try
		{
			return CellTool.Click( cell );
		}
		finally
		{
			keepTrackToolAfterLine = previous;
		}
	}

	/// <summary>
	/// The command the build tool's click issues for <paramref name="entry"/> at a build target (<see cref="BuildTarget"/>): the Move tool's placement while it is armed, a track
	/// add-on onto <see cref="BuildAddOnRide"/>'s track, else an ordinary placement (<see cref="PlaceBuildTarget"/>'s choice).
	/// </summary>
	internal PlaceObjectCommand BuildCommand( ObjectCatalogEntry entry, int x, int y, int rotation ) =>
		new( ParkCommandCodec.EntryKey( entry ), x, y, rotation, IsMoving,
			BuildAddOnRide is { } ride && TrackAddOns.IsTrackAddOn( entry ) ? ride.Visitors.AttractionId : -1 );

	/// <summary>
	/// The Move tool's pick-up (<c>0x48cfa0</c>): the object is demolished exactly as its window's erase button does (the same click handler 0x524960 in mode 0x33 runs the erase 0x527ee0, with its
	/// scrap credit and its queue and track clearing), then the build tool arms the same type at the object's own angle in mode 0x3b. Nothing else of the object is kept: the state is the new
	/// object's, and a cancel leaves the object gone. [EXT:online-coop-commands] The demolition is a <see cref="PickUpObjectCommand"/>; arming the tool is the issuer's own, and its end, whatever
	/// ends it, an <see cref="EndMoveCommand"/> (<see cref="BuildEntry"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x48cfa0 move pick-up] 0x48c940 holds the thing, 0x5276d0 takes its cell, a type with a footprint (+0x40) saves the queue's corner points (0x530120, 0x52fc30), mode 0x33 and the click 0x524960(cell, 0, 0) erase it (0x527ee0 with the last argument 0: the very call of the window's erase button 0x48cd10), 0x46c5a0(0x3b, type) builds the tool, 0x52f200(0x3b) sets the mode and 0x52f1b0 the angle (+0x10); 0x81b3ac and 0x81b14c are written and read nowhere, so the saved state is never restored
	public bool BeginMove( OriginalObject item )
	{
		ArgumentNullException.ThrowIfNull( item );
		if ( IsReadOnlyVisit || item.IsDeleted )
			return false;
		// a Move tool still carrying ends first, so the end does not take the new pick-up's type
		if ( IsMoving )
			BuildEntry = null;
		if ( Commands.Issue( new PickUpObjectCommand( item.Visitors.AttractionId ) ).As<(ObjectCatalogEntry Entry, int Rotation)>() is not { Entry: { } entry } picked )
			return false;
		IsRemovingObjects = false;
		IsPlacing = false;
		BuildEntry = entry;
		IsMoving = true;
		SetBuildRotation( picked.Rotation );
		return true;
	}

	/// <summary>
	/// Starts the queue tool on the queue that holds (x, y): the original's hover for a queue cell reads
	/// "Left-click to edit this queue" (help 444), and the tool then starts at that queue's back cell
	/// (<see cref="EnterQueueTool"/>). False when the cell is not a queue cell of a HasQueue ride.
	/// [EXT:online-coop-commands] The queue's edit (the walk and the link it takes off) is an <see cref="EditQueueCommand"/>; the tool is the issuer's own.
	/// </summary>
	// [DATA:UIHELPTEXT 444 "Left-click to edit this queue", 445 "Left-click to build the queue, click again to stop building / Click onto path to connect the queue to it"] [BIN:STP-PPC:0x139c64 park-view hover] queue cell → hover state 3 (PATH-plan §4.1)
	// [EXT:queue-click-at-back-cell] a left click on a queue cell starts the queue tool at once at the ride's back cell (EnterQueueTool): an OpenTPW design choice, the first click is the whole edit gesture here
	// [BIN:TPW-EXE:0x004879D0 queue click] the park-view click (hover state 3: a type-3 cell with an owning ride) builds tool 0x14, whose enter 0x0046D5A0 moves the cursor to the clicked cell (0x005234D0) and calls SetMode(0x14, 1) (0x0046D5F6); the next cell click in 0x00524960 (mode 0x14, 0x005260F7) walks the queue from the entrance into the corner list (0x00530120), runs the queue edit 0x004DE1F0 and enters mode 3 (0x0052F580), and 0x00530120 ends by setting the line start 0x81EDE4/0x81EDE8 to the queue's last corner (its tail, 0x00530120's last stores), so the line goes on from the queue's back cell, as EnterQueueTool starts it
	public bool EditQueueAt( int x, int y )
	{
		if ( IsReadOnlyVisit || Guests == null || QueueRideAt( Guests.Grid, Guests.Attractions, x, y ) is not { } ride )
			return false;
		// the tool takes the queue's corners as they are before the edit takes the link off, as the original's walk does
		if ( !EnterQueueTool( ride ) )
			return false;
		if ( Commands.Issue( new EditQueueCommand( x, y ) ).Accepted )
			return true;
		CellTool.Cancel();
		return false;
	}
}
