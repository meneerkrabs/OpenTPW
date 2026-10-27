namespace OpenTPW;

/// <summary>
/// The level's tool layer: the park click, the remove tool, the Move tool's pick-up and the queue edit click. [EXT:online-coop-commands] Each edit leaves here as a
/// <see cref="ParkCommand"/> through <see cref="Commands"/>; what stays here is the tools' own state (the armed entry, the build angle, the line tool's stack), which no
/// command reads. The command layer's static test keeps every park mutator out of this file (docs/COOP.md).
/// </summary>
public partial class Level
{
	internal bool WorldPointerRouted { get; set; }

	/// <summary>
	/// Whether the park view's tool clicks (a placement, a line, a removal) act on the left button's release, as tp.exe's do: the mode object's click handler 0x524960 is the release slot (+8) of the
	/// tool object, and its press slot (+4, 0x524790) only sets a latch. The Theme Park World HUD sets it; the Theme Park Inc HUD and the level alone keep acting on the press (the TPI executable's mouse handler was not traced).
	/// </summary>
	// [BIN:TPW-EXE:0x46C610 release slot] the tool object's (vtable 0x6FE9E0) slot +8 calls 0x524960 with the hover cell 0x7B05CC read at the release; slot +4 (0x46C5D0 → 0x524790) sets the press latch 0x8186D8 and, in coaster mode 0x16 only, starts a pick-up
	internal bool WorldClicksOnRelease { get; set; }

	/// <summary>With <see cref="WorldClicksOnRelease"/> and no routed pointer: the park view took the press whose release this frame brings, so the release acts even where the pointer now lies over the interface.</summary>
	// [BIN:TPW-EXE:0x658b7f release recipient] a release goes to the window that took its press, wherever the pointer is
	internal bool ParkOwnsRelease { get; set; }

	// Unit-test seams: the pointer fixtures mark which tool or view the level is in without building a renderer, a ride or a walk.
	internal void UseCoasterPanelForTests( CoasterToolPanel? panel ) => CoasterPanel = panel;
	internal void UseRideViewForTests( RideCameraMode? view ) => RideView = view;
	internal void UseFreeWalkForTests( FreeWalkCameraMode? walk ) => FreeWalk = walk;

	internal enum WorldClickAction { None, PlacePrototype, UseTool }

	/// <summary>World input stays suspended in ride and walking views, including tools armed through the HUD.</summary>
	internal static WorldClickAction WorldClick( bool riding, bool leftDown, bool previousLeftDown, bool mouseCaptured, bool placing, bool onRelease = false, bool parkOwnsRelease = false )
	{
		// on the release the HUD says whether the park view took the press (and no HUD tool used the release): then the release acts wherever the pointer is; any other release is nobody's click
		var edge = onRelease ? !leftDown && previousLeftDown && parkOwnsRelease : leftDown && !previousLeftDown && !mouseCaptured;
		return riding || !edge ? WorldClickAction.None
			: placing ? WorldClickAction.PlacePrototype : WorldClickAction.UseTool;
	}

	/// <param name="modifierMask">The modifier mask held at the click (<see cref="Input.ModifierMask"/>: Shift 3, Ctrl 0xc, Alt 0x30); exactly Ctrl keeps the build tool armed after a placement.</param>
	internal void UpdateWorldInput( bool leftDown, bool mouseCaptured, Vector2 mousePosition, Vector2 viewportSize, int modifierMask = 0 )
	{
		if ( WorldPointerRouted )
		{
			wasMouseDown = leftDown;
			return;
		}
		clickModifierMask = modifierMask;
		// a release acts only when the park view took its press (ParkOwnsRelease), over the interface or not
		switch ( WorldClick( RideView != null || FreeWalk != null, leftDown, wasMouseDown, mouseCaptured, IsPlacing, WorldClicksOnRelease, ParkOwnsRelease ) )
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

	/// <summary>An admitted park-view click, delivered once by the HUD router rather than reconstructed from final held state: the left release of a Theme Park World park (<see cref="WorldClicksOnRelease"/>), else the press.</summary>
	internal void DispatchRoutedWorldClick( Vector2 position, Vector2 viewport, int modifierMask )
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
	/// [EXT:online-coop-commands] The guest's request whose outcome starts a tool, or null (C3b): a queue edit (the queue tool starts from what its record read,
	/// <see cref="CompleteQueueEdit"/>) or a Move pick-up (the Move tool arms with the type it carries, <see cref="CompleteMove"/>). No tool is on while it waits. Escape, a right click and
	/// another tool drop it (its start, <see cref="EndBuildTools"/>, the HUD's frame with a tool on): its outcome is then only a message, and a pick-up's carry is ended.
	/// </summary>
	internal CoopGuestCommands.Request? WaitingToolStart { get; private set; }

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

	/// <summary>[EXT:online-coop-commands] Escape, a right click or another tool leaves the queue edit or the pick-up that waits for the host: its outcome starts no tool then.</summary>
	internal void DropToolStartWait() => WaitingToolStart = null;

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
		if ( WaitingToolStart == request )
			WaitingToolStart = null;
		if ( WaitingBacktrack == request )
			WaitingBacktrack = null;
		if ( IsWaitingSale( request ) )
			waitingSales!.Remove( ((RemoveAttractionCommand)request.Command).Attraction );
	}

	/// <summary>[EXT:online-coop-commands] Whether <paramref name="request"/> is one this level's tools or buttons wait for (C3a-2, C3b).</summary>
	internal bool IsWaitingFor( CoopGuestCommands.Request request ) =>
		WaitingPlacement?.Request == request || WaitingRemoval == request || WaitingToolStart == request || WaitingBacktrack == request || IsWaitingSale( request );

	/// <summary>
	/// [EXT:online-coop-commands] The session ended with requests open (C3a-2, C3b): the placement, the removal, the queue edit or pick-up, the Backspace and the sales that waited wait
	/// no more, a line that waited ends its tool, since what became of it is not known here, and a guest's Move tool disarms.
	/// </summary>
	internal void EndGuestWaits()
	{
		if ( (CellBuildTool?)CellTool is { Waiting: not null } tool )
			tool.Cancel();
		WaitingPlacement = null;
		WaitingRemoval = null;
		WaitingToolStart = null;
		WaitingBacktrack = null;
		waitingSales = null;
		// [EXT:online-coop-commands] a guest's pool tool ends with its session, and its strokes go without a report (C3b-3)
		if ( Commands.Player > ParkCommands.LocalPlayer )
			PoolTool?.EndGuestStrokes();
		// [EXT:online-coop-commands] a guest's Move tool disarms without an end command: its carry goes with the session's state (C3b-2), and the park is shared no more
		if ( IsMoving && Commands.Player > ParkCommands.LocalPlayer )
			DisarmMoveLocally();
	}

	/// <summary>[EXT:online-coop-commands] The guest's Move tool disarms without an end command (its carry is gone, or goes with the session), and the HUD drops the item it had armed for it.</summary>
	private void DisarmMoveLocally()
	{
		endingMoveLocally = true;
		try
		{
			BuildEntry = null;
		}
		finally
		{
			endingMoveLocally = false;
		}
		// the HUD drops the item it had armed for the Move, as after any end of the build tools
		BuildToolsEnded?.Invoke();
	}

	/// <summary>Set while <see cref="EndGuestWaits"/> disarms a guest's Move tool: the tool's end sends no command.</summary>
	private bool endingMoveLocally;

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
		// [EXT:online-coop-commands] a guest's pick-up goes to the host, and the Move tool arms once it comes back (C3b-2, CompleteMove): one pick-up at a time, so no end of an older
		// carry can reach the host after a newer pick-up
		if ( Commands.SendsRequests && (WaitingToolStart != null || HasOpenPickUp) )
		{
			NoteWaitingClick();
			return false;
		}
		// a Move tool still carrying ends first, so the end does not take the new pick-up's type
		if ( IsMoving )
			BuildEntry = null;
		// [BIN:TPW-EXE:0x00525E5A queue start] the pick-up's click in mode 0x33 zeroes the pending queue start
		placedQueueStart = null;
		var result = Commands.Issue( new PickUpObjectCommand( item.Visitors.AttractionId ) );
		if ( result.Request is { } request )
		{
			WaitingToolStart = request;
			return false;
		}
		if ( result.As<(ObjectCatalogEntry Entry, int Rotation)>() is not { Entry: { } entry } picked )
			return false;
		ArmMove( entry, picked.Rotation );
		return true;
	}

	/// <summary>The Move tool arms with the type its pick-up carries, at the object's own angle (mode 0x3b).</summary>
	private void ArmMove( ObjectCatalogEntry entry, int rotation )
	{
		IsRemovingObjects = false;
		IsPlacing = false;
		BuildEntry = entry;
		IsMoving = true;
		SetBuildRotation( rotation );
	}

	/// <summary>
	/// [EXT:online-coop-commands] A guest's seat that carries a picked-up type in its park while its Move tool is not armed (C3b-2): a pick-up whose tool started no more, an end the host
	/// could not take, a resync's park with a carry its Move tool did not hold (<see cref="TakeMoveToRestore"/> arms one it held). The HUD calls this after taking its requests' outcomes each
	/// frame: it sends the Move's end, at most once a second (by <paramref name="now"/>, kept by the session across a resync) and only while no pick-up or end of this guest is open, so it
	/// never ends a carry whose tool is about to arm (the outcomes armed it first) or reaches the host after a newer pick-up (one is open at a time). True when it sent one.
	/// </summary>
	internal bool EndStrayCarry( double now )
	{
		if ( CoopSession is not { GuestCommands: { } requests } session || !Commands.SendsRequests || IsMoving || !CarriesEntry( Commands.Player ) )
			return false;
		if ( requests.Open.Any( request => request.Command is PickUpObjectCommand or EndMoveCommand ) || (session.LastStrayCarryEnd is { } last && now - last < 1) )
			return false;
		session.LastStrayCarryEnd = now;
		return Commands.Issue( new EndMoveCommand() ).IsPending;
	}

	/// <summary>
	/// [EXT:online-coop-commands] A guest's Move tool that was armed in the park a resync replaced (C3b-2), as the session hands it to the new park: the key of the type it carried, its angle,
	/// and the placement of it that waited for the host then, if one did (its request and Ctrl at its click, <see cref="PlacementRequest"/>).
	/// </summary>
	internal sealed record ResyncedMove( string Entry, int Rotation, CoopGuestCommands.Request? Placement, bool KeepTool );

	/// <summary>[EXT:online-coop-commands] The guest's Move tool as a resync replaces this park (C3b-2), for the new park to arm again (<see cref="TakeMoveToRestore"/>); null when it is not armed.</summary>
	internal ResyncedMove? MoveForResync() => IsMoving && BuildEntry is { } armed
		? new ResyncedMove( ParkCommandCodec.EntryKey( armed ), BuildRotation, WaitingPlacement?.Request, WaitingPlacement?.KeepTool == true )
		: null;

	/// <summary>
	/// [EXT:online-coop-commands] The guest's Move tool that was armed in the park a resync replaced (C3b-2), which the session hands to the new park (<see cref="MoveForResync"/>). The new
	/// park's HUD arms the tool again from the carry the snapshot holds (<see cref="TakeMoveToRestore"/>), as the player left it, instead of ending the carry.
	/// </summary>
	internal ResyncedMove? MoveBeforeResync { get; set; }

	/// <summary>
	/// [EXT:online-coop-commands] The type and angle the guest's Move tool arms with again after a resync (<see cref="MoveBeforeResync"/>), once: when the seat still carries that type in the new
	/// park, no pick-up of its own is open and the view has no tool. A placement of the tool that waited for the host comes along, for the tool to wait for again
	/// (<see cref="WaitForPlacementAgain"/>): its outcome then finishes the tool as it would have without the resync (<c>ParkHud.FinishPlacement</c>: kept for Ctrl at its click, else ended,
	/// with the tool the placed object opens), and a refusal leaves it armed. A placement whose record is inside the resync's cut has no outcome here (its result in the new park is not known):
	/// the tool arms again only when Ctrl at its click keeps it whatever that result was (<see cref="PlacementKeepsBuildTool"/>). Null when it does not arm; a carry then left without its tool
	/// is a stray one (<see cref="EndStrayCarry"/>).
	/// </summary>
	internal (ObjectCatalogEntry Entry, int Rotation, PlacementRequest? Placement)? TakeMoveToRestore()
	{
		if ( MoveBeforeResync is not { } move )
			return null;
		MoveBeforeResync = null;
		if ( !Commands.SendsRequests || IsMoving || !NoToolActive || HasOpenPickUp || carriedEntries == null || !carriedEntries.TryGetValue( Commands.Player, out var carried )
			|| carried != move.Entry || ResolveCommandEntry( carried ) is not { } entry )
			return null;
		if ( move.Placement is not { } placement )
			return (entry, move.Rotation, null);
		if ( placement.State == CoopGuestRequestState.InSnapshot )
			return move.KeepTool && PlacementKeepsBuildTool( entry ) ? (entry, move.Rotation, null) : null;
		return (entry, move.Rotation, new PlacementRequest( placement, entry, move.KeepTool ));
	}

	/// <summary>
	/// [EXT:online-coop-commands] The Move tool armed again after a resync waits for its placement that waited in the park the resync replaced (<see cref="TakeMoveToRestore"/>), as the build
	/// tool waits for a placement it sent (<see cref="WaitingPlacement"/>).
	/// </summary>
	internal void WaitForPlacementAgain( PlacementRequest placement )
	{
		if ( IsMoving && ReferenceEquals( BuildEntry, placement.Entry ) )
			WaitingPlacement = placement;
	}

	/// <summary>
	/// [EXT:online-coop-commands] This park's object for <paramref name="placed"/>, an accepted placement's result, and whether a later record removed it (C3b-2): the object itself, or, for
	/// one placed in the park a resync replaced before that park's HUD took the outcome (the Move tool waits for it again, <see cref="TakeMoveToRestore"/>), this park's object of its
	/// attraction id, which a record before the resync's cut may have removed (the replaced park's own objects are all gone with it).
	/// </summary>
	internal (OriginalObject Item, bool Removed) PlacedInThisPark( OriginalObject placed )
	{
		if ( ReferenceEquals( placed, PlacedRide ) || Objects?.Objects.Contains( placed ) != false )
			return (placed, placed.IsDeleted);
		return ResolveCommandObject( placed.Visitors.AttractionId ) is { } own ? (own, false) : (placed, true);
	}

	/// <summary>
	/// Whether a placement of <paramref name="entry"/> made with exactly Ctrl leaves the build tool armed for the next one, by the type alone (<c>ParkHud.FinishPlacement</c>): not a type
	/// whose placed object opens a tool of its own even then, a coaster's track, a kart or water ride's track or the bumper cars' arena (<see cref="CoasterDefinition.IsCoaster"/>,
	/// <see cref="BumperTemplates.ForRide"/>, which give the object its <see cref="OriginalObjectRuntime.Coaster"/> or <see cref="OriginalObjectRuntime.Bump"/>), or a type with a queue,
	/// whose placed ride opens its queue tool (a slide tower's slide tool follows only a click without Ctrl). A placed ride without queue cells keeps the tool; this test, which cannot see
	/// the placed object, counts it as one that ends it.
	/// </summary>
	internal static bool PlacementKeepsBuildTool( ObjectCatalogEntry entry ) => !entry.HasQueue && !CoasterDefinition.IsCoaster( entry.Settings )
		&& BumperTemplates.ForRide( entry.Settings.GetInt( "Bumper.WhichTrackType" ), entry.Settings.GetInt( "Bumper.BumperType" ) ) == null;

	/// <summary>
	/// [EXT:online-coop-commands] A guest's Move tool armed while its seat carries nothing in its park (C3b-2): the host released the seat's carry (a resume under a new member id, a departure
	/// the host saw), so every placement of the tool would be refused at its record. The HUD calls this after taking its requests' outcomes each frame: the tool disarms without an end command,
	/// as there is no carry to end, as the session's end disarms it. True when it disarmed.
	/// </summary>
	internal bool EndCarrylessMove()
	{
		if ( CoopSession == null || !Commands.SendsRequests || !IsMoving || CarriesEntry( Commands.Player ) || HasOpenPickUp )
			return false;
		DisarmMoveLocally();
		return true;
	}

	/// <summary>[EXT:online-coop-commands] Whether a pick-up of this guest is open in the session's requests, from this park or the one a resync replaced (C3b-2).</summary>
	internal bool HasOpenPickUp => CoopSession?.GuestCommands?.Open.Any( request => request.Command is PickUpObjectCommand ) == true;

	/// <summary>[EXT:online-coop-commands] How many pool tool requests of this guest are open in the session's requests, from this park or the one a resync replaced (C3b-3): the pool tool's budget.</summary>
	internal int OpenPoolRequests => CoopSession?.GuestCommands?.Open.Count( request => request.Command is PoolToolCommand ) ?? 0;

	/// <summary>
	/// [EXT:online-coop-commands] The guest's pick-up came back accepted (C3b-2): the Move tool arms with the type it carries, as <see cref="BeginMove"/> does once its pick-up applied, if
	/// the park view still has no tool. True when it armed; otherwise the carry is ended by the HUD (<see cref="EndStrayCarry"/>), and the object stays sold, as a cancelled Move leaves
	/// it.
	/// </summary>
	internal bool CompleteMove( ObjectCatalogEntry entry, int rotation )
	{
		ArgumentNullException.ThrowIfNull( entry );
		if ( IsReadOnlyVisit || !NoToolActive )
			return false;
		ArmMove( entry, rotation );
		HostToolStarts++;
		return true;
	}

	/// <summary>Whether (<paramref name="x"/>, <paramref name="y"/>) is a queue cell of a ride whose queue the queue tool can edit (<see cref="EditQueueAt"/> would start it).</summary>
	public bool CanEditQueueAt( int x, int y ) => !IsReadOnlyVisit && Guests != null && QueueRideAt( Guests.Grid, Guests.Attractions, x, y ) is not null;

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
			if ( WaitingToolStart != null )
				NoteWaitingClick();
			else if ( Commands.Issue( new EditQueueCommand( x, y ) ).Request is { } request )
				WaitingToolStart = request;
			return false;
		}
		// the tool takes the queue's corners as they are before the edit takes the link off, as the original's walk does
		if ( !EnterQueueTool( ride, afterEditClick: true ) )
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
		if ( !EnterQueueTool( ride, edit.Tool ) )
			return false;
		HostToolStarts++;
		return true;
	}

	/// <summary>[EXT:online-coop-commands] How many tools a guest's request started when the host's answer came back (<see cref="CompleteMove"/>, <see cref="CompleteQueueEdit"/>): the HUD ignores the release of a press made before one started.</summary>
	internal int HostToolStarts { get; set; }

	/// <summary>[EXT:online-coop-commands] Whether no tool of the park view is on (the view a queue click starts the queue tool from).</summary>
	internal bool NoToolActive => !CellTool.IsActive && BuildEntry == null && !IsRemovingObjects && !IsPlacing && !IsCoasterToolActive
		&& BuyLandTool is not { IsActive: true } && AreaClearTool is not { IsActive: true } && PoolTool is not { IsActive: true }
		&& StaffPlacement is not { IsActive: true } && PatrolTool is not { IsActive: true };
}
