namespace OpenTPW;

/// <summary>
/// The level's tool layer: the park click, the remove tool, the Move tool's pick-up and the queue edit click. [EXT:online-coop-commands] Each edit leaves here as a
/// <see cref="ParkCommand"/> through <see cref="Commands"/>; what stays here is the tools' own state (the armed entry, the build angle, the line tool's stack), which no
/// command reads. The command layer's static test keeps every park mutator out of this file (docs/COOP.md).
/// </summary>
public partial class Level
{
	internal bool WorldPointerRouted { get; set; }

	// Unit-test seams: the pointer fixtures mark which tool or view the level is in without building a renderer, a ride or a walk.
	internal void UseCoasterPanelForTests( CoasterToolPanel? panel ) => CoasterPanel = panel;
	internal void UseRideViewForTests( RideCameraMode? view ) => RideView = view;
	internal void UseFreeWalkForTests( FreeWalkCameraMode? walk ) => FreeWalk = walk;

	internal enum WorldClickAction { None, PlacePrototype, UseTool }

	/// <summary>World input stays suspended in ride and walking views, including tools armed through the HUD.</summary>
	internal static WorldClickAction WorldClick( bool riding, bool leftDown, bool previousLeftDown, bool mouseCaptured, bool placing ) =>
		riding || !leftDown || previousLeftDown || mouseCaptured ? WorldClickAction.None
			: placing ? WorldClickAction.PlacePrototype : WorldClickAction.UseTool;

	/// <param name="modifierMask">The modifier mask held at the click (<see cref="Input.ModifierMask"/>: Shift 3, Ctrl 0xc, Alt 0x30); exactly Ctrl keeps the build tool armed after a placement.</param>
	internal void UpdateWorldInput( bool leftDown, bool mouseCaptured, Vector2 mousePosition, Vector2 viewportSize, int modifierMask = 0 )
	{
		if ( WorldPointerRouted )
		{
			wasMouseDown = leftDown;
			return;
		}
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

	/// <summary>An admitted park-view down, delivered once by the HUD router rather than reconstructed from final held state.</summary>
	internal void DispatchRoutedWorldPress( Vector2 position, Vector2 viewport, int modifierMask )
	{
		if ( RideView != null || FreeWalk != null ) return;
		clickModifierMask = modifierMask;
		if ( IsPlacing )
		{
			if ( TryGetPlacementPosition( position, viewport, out var placed ) )
				Commands.Issue( new PlacePrototypeCommand( placed.X, placed.Y, placed.Z ) );
		}
		else HandleObjectClick( position, viewport );
	}

	private void HandleObjectClick( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize )
	{
		if ( IsReadOnlyVisit )
			return;
		if ( (BuildEntry == null && !IsRemovingObjects && !CellTool.IsActive && !IsCoasterToolActive) || !TryGetGridCell( mousePosition, viewportSize, out var x, out var y ) )
			return;
		// The remove tool comes first, so a path or queue cell under it is removed rather than built on (ParkHud stands aside for it too).
		if ( IsRemovingObjects )
			ClickRemoveTool( x, y );
		else if ( IsCoasterToolActive && BuildEntry == null )
			CoasterClick( x, y );
		else if ( CellTool.IsActive && BuildEntry == null )
			ClickCellTool( (x, y), clickModifierMask );
		else
			ClickBuildTool( x, y, clickModifierMask );
	}

	/// <summary>[EXT:online-coop-commands] A guest's placement sent to the host (C3a-2): its request, the placed entry and Ctrl at the click (<see cref="KeepBuildToolOnPlace"/>, which the click resets).</summary>
	internal sealed record PlacementRequest( CoopGuestCommands.Request Request, ObjectCatalogEntry Entry, bool KeepTool );

	/// <summary>
	/// [EXT:online-coop-commands] The guest's placement that waits for the host, or null (C3a-2): the build tool takes no other click until its outcome, and the HUD finishes the
	/// placement from it (<c>ParkHud.FinishPlacement</c>). Arming another entry or leaving the build tool drops it (<see cref="BuildEntry"/>): its outcome is then only a message.
	/// </summary>
	internal PlacementRequest? WaitingPlacement { get; private set; }

	/// <summary>
	/// [EXT:online-coop-commands] The guest's removal that waits for the host, or null (C3a-2): the remove tool takes no other click until its outcome; leaving the tool drops it
	/// (<see cref="IsRemovingObjects"/>).
	/// </summary>
	internal CoopGuestCommands.Request? WaitingRemoval { get; private set; }

	/// <summary>
	/// [EXT:online-coop-commands] The guest's queue edit that waits for the host, or null (C3b): no tool is on yet, and the queue tool starts once the edit comes back, from what its record
	/// read (<see cref="CompleteQueueEdit"/>). Escape, a right click, <see cref="EndBuildTools"/> and any tool that is on by then drop it: its outcome is then only a message.
	/// </summary>
	internal CoopGuestCommands.Request? WaitingQueueEdit { get; private set; }

	/// <summary>[EXT:online-coop-commands] The guest's Backspace that waits for the host, or null (C3b): the key's presses and repeats send nothing more until its outcome.</summary>
	internal CoopGuestCommands.Request? WaitingBacktrack { get; private set; }

	/// <summary>
	/// [EXT:online-coop-commands] The guest's sales that wait for the host, by attraction id, or null before the first (C3b): the sell buttons take no second sale of the same object
	/// meanwhile.
	/// </summary>
	private Dictionary<int, CoopGuestCommands.Request>? waitingSales;

	/// <summary>[EXT:online-coop-commands] Whether a sale of the object with attraction id <paramref name="attraction"/> waits for the host.</summary>
	internal bool IsSaleWaiting( int attraction ) => waitingSales?.ContainsKey( attraction ) == true;

	/// <summary>[EXT:online-coop-commands] The guest's sale of <paramref name="attraction"/> went to the host as <paramref name="request"/>.</summary>
	internal void WaitSale( int attraction, CoopGuestCommands.Request request ) => (waitingSales ??= new())[attraction] = request;

	/// <summary>[EXT:online-coop-commands] Whether <paramref name="request"/> is a sale that waits for the host.</summary>
	private bool IsWaitingSale( CoopGuestCommands.Request request ) =>
		request.Command is RemoveAttractionCommand sale && waitingSales?.TryGetValue( sale.Attraction, out var waiting ) == true && waiting == request;

	/// <summary>[EXT:online-coop-commands] The guest's Backspace went to the host as <paramref name="request"/>.</summary>
	internal void WaitBacktrack( CoopGuestCommands.Request request ) => WaitingBacktrack = request;

	/// <summary>[EXT:online-coop-commands] Escape, a right click or another tool leaves the queue edit that waits for the host: its outcome is only a message then.</summary>
	internal void DropQueueEditWait() => WaitingQueueEdit = null;

	/// <summary>[EXT:online-coop-commands] How many clicks a tool refused while its request waited for the host (C3a-2): the HUD asks the player to wait when it changes.</summary>
	internal int WaitingClicks { get; private set; }

	/// <summary>[EXT:online-coop-commands] A tool refused a click while its request waits for the host.</summary>
	internal void NoteWaitingClick() => WaitingClicks++;

	/// <summary>[EXT:online-coop-commands] The HUD took the outcome of <paramref name="request"/>: the placement or the removal that waited for it waits no more, and its tool takes clicks again.</summary>
	internal void EndWaiting( CoopGuestCommands.Request request )
	{
		if ( WaitingPlacement?.Request == request )
			WaitingPlacement = null;
		if ( WaitingRemoval == request )
			WaitingRemoval = null;
		if ( WaitingQueueEdit == request )
			WaitingQueueEdit = null;
		if ( WaitingBacktrack == request )
			WaitingBacktrack = null;
		if ( IsWaitingSale( request ) )
			waitingSales!.Remove( ((RemoveAttractionCommand)request.Command).Attraction );
	}

	/// <summary>[EXT:online-coop-commands] Whether <paramref name="request"/> is one this level's tools or buttons wait for (C3a-2, C3b).</summary>
	internal bool IsWaitingFor( CoopGuestCommands.Request request ) =>
		WaitingPlacement?.Request == request || WaitingRemoval == request || WaitingQueueEdit == request || WaitingBacktrack == request || IsWaitingSale( request );

	/// <summary>
	/// [EXT:online-coop-commands] The session ended with requests open (C3a-2, C3b): the placement, the removal, the queue edit, the Backspace and the sales that waited wait no more,
	/// and a line that waited ends its tool, since what became of it is not known here.
	/// </summary>
	internal void EndGuestWaits()
	{
		if ( (CellBuildTool?)CellTool is { Waiting: not null } tool )
			tool.Cancel();
		WaitingPlacement = null;
		WaitingRemoval = null;
		WaitingQueueEdit = null;
		WaitingBacktrack = null;
		waitingSales = null;
	}

	/// <summary>The remove tool's click on cell (<paramref name="x"/>, <paramref name="y"/>): [EXT:online-coop-commands] a guest's removal waits for the host, taking no other click until its outcome (C3a-2).</summary>
	internal void ClickRemoveTool( int x, int y )
	{
		if ( WaitingRemoval != null )
		{
			NoteWaitingClick();
			return;
		}
		if ( Commands.Issue( new RemoveAtCommand( x, y ) ).Request is { } request )
			WaitingRemoval = request;
	}

	/// <summary>The build tool's (or the Move tool's) click on cell (<paramref name="x"/>, <paramref name="y"/>): the armed <see cref="BuildEntry"/> goes to its build target there.</summary>
	internal void ClickBuildTool( int x, int y, int modifierMask = 0 )
	{
		// [EXT:online-coop-commands] a guest's placement that waits for the host: the build tool takes no other click until its outcome (C3a-2)
		if ( WaitingPlacement != null )
		{
			NoteWaitingClick();
			return;
		}
		KeepBuildToolOnPlace = KeepsBuildTool( modifierMask );
		try
		{
			// a track add-on goes to its hover position on its ride's track, at the angle of the tile's flow (TrackAddOns.HoverTarget); anything else stays at the cell
			var entry = BuildEntry!;
			var (targetX, targetY, rotation) = BuildTarget( entry, x, y );
			SetBuildRotation( rotation );
			// [BIN:TPW-EXE:0x00524B53 queue start] the build tool's click (and the Move tool's, 0x5253F3) zeroes the pending queue start before it places; a click on a refused ghost
			// never gets there: the placement preview 0x528A70 runs the object's cells through 0x532FC0(0x104), whose validator refusal sets the latch 0x816D48 (0x533933), and the click
			// handler tests the latch before the mode dispatch (0x524A63), which sends modes 4 and 0x3B to 0x52765B. OpenTPW reads that refusal from the ghost the HUD draws
			if ( BuildGhostAllowed( entry, CheckBuildTarget( entry, targetX, targetY, rotation ) ) )
				placedQueueStart = null;
			// [EXT:online-coop-commands] a guest's placement went to the host: the HUD finishes it once its outcome comes back, with the entry and the Ctrl of this click
			if ( Commands.Issue( BuildCommand( entry, targetX, targetY, rotation ) ).Request is { } request )
				WaitingPlacement = new PlacementRequest( request, entry, KeepBuildToolOnPlace );
		}
		finally
		{
			KeepBuildToolOnPlace = false;
		}
	}

	// A live track click's continuation choice is local tool state, never a command or world input.
	private bool keepTrackToolAfterLine;

	/// <summary>
	/// The ride whose queue start the last placement left pending (tp.exe <c>0x8186C0</c>): the queue tool follows a kart or water circuit's close only while one is
	/// pending, from that ride's queue (<see cref="ContinueTrackTool"/>), and the close takes it. Local tool state of the issuer, never a command, saved or hashed.
	/// </summary>
	// [BIN:TPW-EXE:0x0052544E queue start] a placement of a type with an entrance stores the record of its queue's start cell (0x52A050, the cell's coordinates in 0x7639B8/0x7639BC; 0x524DD7, 0x525269, 0x52544E and the Move tool's 0x5258AC); every build or Move click on an accepted ghost zeroes it before it places (0x524B53, 0x5253F3), the Move tool's pick-up in mode 0x33 zeroes it (0x525E5A), a path or queue commit zeroes it (0x527483), and the queue tool that follows a ride's placement, the coaster tool's click or a kart or water circuit's close takes it (0x5252EA, 0x526680, 0x527056)
	private OriginalObject? placedQueueStart;

	internal SegmentResult? ClickCellTool( (int X, int Y) cell, int modifierMask = 0 )
	{
		// [EXT:online-coop-commands] a guest's line that waits for the host takes no other click (C3a-2)
		if ( CellTool.Waiting != null )
		{
			NoteWaitingClick();
			return null;
		}
		var previous = keepTrackToolAfterLine;
		keepTrackToolAfterLine = KeepsBuildTool( modifierMask );
		var mode = CellTool.Mode;
		try
		{
			var result = CellTool.Click( cell );
			// [BIN:TPW-EXE:0x00527483 queue start] the path and queue handler zeroes the pending queue start after its commit LayLine (0x527472); a click on a refused ghost returns before
			// it (0x524A63), and a line whose command the park refused ([EXT:online-coop-commands]) laid nothing either
			if ( mode is CellToolMode.Path or CellToolMode.Queue && result is { GhostRefused: false, CommandRefused: false, Waiting: null } )
				placedQueueStart = null;
			return result;
		}
		finally
		{
			keepTrackToolAfterLine = previous;
		}
	}

	/// <summary>
	/// [EXT:online-coop-commands] The outcome of the guest's line that waits for the host (<see cref="CellBuildTool.CompleteWaiting"/>, C3a-2): the path and queue tool's commit zeroes
	/// the pending queue start once the line was laid, as <see cref="ClickCellTool"/> does for a line laid at once.
	/// </summary>
	internal SegmentResult? CompleteCellTool( ParkCommandResult outcome )
	{
		var mode = CellTool.Mode;
		var result = CellTool.CompleteWaiting( outcome );
		// [BIN:TPW-EXE:0x00527483 queue start] the path and queue handler zeroes the pending queue start after its commit LayLine, here once the guest's commit came back laid
		if ( mode is CellToolMode.Path or CellToolMode.Queue && result is { GhostRefused: false, CommandRefused: false } )
			placedQueueStart = null;
		return result;
	}

	/// <summary>
	/// The track tool a kart or water ride the player just placed opens (<see cref="EnterTrackTool"/>): a ride with a queue leaves its queue start pending, so the close of
	/// its circuit hands over to the queue tool (<see cref="ContinueTrackTool"/>).
	/// </summary>
	internal bool EnterPlacedTrackTool( OriginalObject item )
	{
		ArgumentNullException.ThrowIfNull( item );
		if ( !EnterTrackTool( item ) )
			return false;
		placedQueueStart = item.Entry.HasQueue && item.Runtime.IsAttraction && item.Visitors.HasCells ? item : null;
		return true;
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
		// [BIN:TPW-EXE:0x00525E5A queue start] the pick-up's click in mode 0x33 zeroes the pending queue start
		placedQueueStart = null;
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
		// [EXT:online-coop-commands] a guest's edit goes to the host first: no tool is on while it waits, and the tool starts from what its record read once it comes back (C3b,
		// CompleteQueueEdit); the click starts nothing here
		if ( Commands.SendsRequests )
		{
			if ( WaitingQueueEdit != null )
				NoteWaitingClick();
			else if ( Commands.Issue( new EditQueueCommand( x, y ) ).Request is { } request )
				WaitingQueueEdit = request;
			return false;
		}
		// the tool takes the queue's corners as they are before the edit takes the link off, as the original's walk does
		if ( !EnterQueueTool( ride ) )
			return false;
		if ( Commands.Issue( new EditQueueCommand( x, y ) ).Accepted )
			return true;
		CellTool.Cancel();
		return false;
	}

	/// <summary>
	/// [EXT:online-coop-commands] The guest's queue edit came back accepted (C3b): the queue tool starts from what the edit's record read before it took the link off, as the host's click
	/// starts it, if the park view is still without a tool and no later record changed the ride's queue since (its cells and their links): else nothing starts, and the park shows what
	/// happened. True when the tool started.
	/// </summary>
	internal bool CompleteQueueEdit( QueueEditStart edit )
	{
		ArgumentNullException.ThrowIfNull( edit );
		// the ride resolves again by its id, as a placed object a later record did not sell
		if ( IsReadOnlyVisit || Guests == null || !NoToolActive || ResolveCommandObject( edit.Ride ) is not { IsDeleted: false } item
			|| !ReferenceEquals( item.Visitors, ResolveQueueRide( edit.Ride ) ) || item.Visitors.QueueFrontCell == null )
			return false;
		var ride = item.Visitors;
		if ( !QueueLinks( ride ).SequenceEqual( edit.Queue ) )
			return false;
		StaffPlacement?.Cancel();
		return EnterQueueTool( ride, edit.Tool );
	}

	/// <summary>[EXT:online-coop-commands] Whether no tool of the park view is on (the view a queue click starts the queue tool from).</summary>
	internal bool NoToolActive => !CellTool.IsActive && BuildEntry == null && !IsRemovingObjects && !IsPlacing && !IsCoasterToolActive
		&& BuyLandTool is not { IsActive: true } && AreaClearTool is not { IsActive: true } && PoolTool is not { IsActive: true }
		&& StaffPlacement is not { IsActive: true } && PatrolTool is not { IsActive: true };
}
