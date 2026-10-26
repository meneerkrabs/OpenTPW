namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-permissions] A shared park's rights (docs/COOP.md, "Building by every player"; the user's decision Q7: the host sets rights per player). The host's seat 0 may issue
/// every command but an edit of a ride another player claims; a guest's seat may issue the building the host granted it (<see cref="SetBuildGrantCommand"/>, kept in the park on every
/// peer) and nothing else. The building is an allowlist over a command's kind, its fields and its target, the commands of the guests' tools that wait for the host or send and go on:
/// <list type="bullet">
/// <item>C3a: path and queue lines; an object placed (not a moving placement, not a track add-on, whatever ride the command names, not a ride whose own tool follows its placement: a
/// coaster, a kart or water ride); the remove click on an ordinary object (not a fixed item of the park);</item>
/// <item>C3b: land; the area clear, which leaves standing what the guest may not remove (<see cref="AllowsRemoval"/>); the remove click on a path or queue cell; Backspace over a
/// path or queue cell; the sale of an ordinary object; the queue's edit; the Move tool's pick-up of an object it may remove and place again, its placement, and its end, which every
/// guest seat may issue, granted or not;</item>
/// <item>C3b-3: the pool tools' batches of a stroke (<see cref="PoolCellsCommand"/>, <see cref="LayPoolSlidePiecesCommand"/>), a bridge, the last piece of an unfinished slide back,
/// Backspace over a bridge or a pool cell, and slide towers, which a guest now places and removes as any object;</item>
/// <item>C3c-1: a track ride's claim (<see cref="ClaimRideCommand"/>) and the track commands of the ride whose claim the guest holds; the release of its own claim
/// (<see cref="ReleaseRideCommand"/>), which every guest seat may issue, granted or not; bumper rides, which a guest now places (their queue tool follows).</item>
/// </list>
/// [EXT:online-coop-claims] A claim guards its ride against every other player, the host included (<see cref="LeavesClaimsAlone"/>): its track, its sale, its removal and its pick-up,
/// its demolition by a placement, an add-on on it, its queue's lines, edit and cells, and the path cell its back joins. For a guest a coaster counts as claimed until a guest's coaster
/// tool claims (C3c-2). The host's cleanup leaves the claims alone by itself: its release of any claim, a departed seat's release and the grants.
/// Everything else stays the host's: speed and pause (Q8), cheats (Q17), the developer kinds, money, staff and the park's management (C4) with a wave machine's settings, undo lines and
/// pool strokes (C5), the pools' single-cell kinds no guest's tool issues, coasters and track add-ons (C3c-2), the grants and the release of a departed seat. Asked at execution on every
/// peer, of the same park, the answer is the same everywhere.
/// </summary>
internal sealed class CoopBuildRights( Level level ) : IParkCommandRights
{
	public bool Allows( int player, ParkCommandKind kind ) => player == ParkCommands.LocalPlayer;

	public bool Allows( int player, ParkCommand command )
	{
		// [EXT:online-coop-claims] the host issues everything but an edit of a ride another player claims, and its cleanup always (C3c-1)
		if ( player == ParkCommands.LocalPlayer )
			return IsHostCleanup( command ) || LeavesClaimsAlone( player, command );
		// [EXT:online-coop-commands] a Move tool's end only drops its own seat's carry, and a Move must end even when the grant went meanwhile (C3b-2); [EXT:online-coop-claims] a release
		// only drops its own seat's claim, which a track tool lets go once it left its ride, the grant taken back or not (C3c-1)
		if ( player > 0 && command is EndMoveCommand or ReleaseRideCommand )
			return true;
		return player > 0 && level.HasBuildGrant( player ) && Builds( command ) && HoldsClaimFor( player, command ) && LeavesClaimsAlone( player, command );
	}

	/// <summary>
	/// [EXT:online-coop-claims] The reason these rights give when another player's claim is what refuses <paramref name="command"/> of <paramref name="player"/> (<see cref="Level.ClaimedReason"/>,
	/// which the HUD says in its own words); null for any other refusal (a guest's building the host has not granted, what only the host does), which keeps the generic reason.
	/// </summary>
	public string? RefusalReason( int player, ParkCommand command ) =>
		(player == ParkCommands.LocalPlayer ? !IsHostCleanup( command ) : player > 0 && level.HasBuildGrant( player ) && Builds( command )) && !LeavesClaimsAlone( player, command )
			? Level.ClaimedReason : null;

	/// <summary>
	/// [EXT:online-coop-claims] What a refusal of <paramref name="command"/> by these rights says to its player, from the refusal's <paramref name="reason"/> as the rights gave it where the command
	/// was refused (<see cref="RefusalReason"/>): another player's claim, building the host has not granted, or what only the host does.
	/// </summary>
	internal CoopRefusal RefusalOf( ParkCommand command, string? reason ) =>
		reason == Level.ClaimedReason ? CoopRefusal.Claimed : Builds( command ) ? CoopRefusal.NotGranted : CoopRefusal.HostOnly;

	/// <summary>[EXT:online-coop-claims] The host's cleanup, which no claim refuses: its release of any claim, a departed seat's release and the grants.</summary>
	private static bool IsHostCleanup( ParkCommand command ) => command is ReleaseRideCommand or ReleaseSeatCommand or SetBuildGrantCommand;

	/// <summary>
	/// Whether <paramref name="command"/> is building a granted guest may do (C3a to C3c-1): its kind, its fields and its target, before the claims (<see cref="LeavesClaimsAlone"/>) and a
	/// track command's claim (<see cref="HoldsClaimFor"/>), which depend on the player.
	/// </summary>
	internal bool Builds( ParkCommand command ) => command switch
	{
		LayLineCommand or QueueSegmentCommand => true,
		// an object placed, or the Move tool's placement (C3b-2: the command itself checks that its player carries the type); a track add-on belongs to the track tools, which come later,
		// as do the rides whose own tool follows their placement; a track add-on's entry is refused whatever ride the command names, so a request without one cannot place it as an
		// ordinary object
		PlaceObjectCommand place => place.AddOnRide < 0 && level.ResolveCommandEntry( place.Entry ) is { } entry && GuestMayPlace( entry ),
		// the remove click takes an ordinary object, else the queue or path cell under it (C3b)
		RemoveAtCommand remove => level.Objects?.FindAt( remove.X, remove.Y ) is not { IsDeleted: false } item || GuestMayRemove( item.Entry ),
		// C3b: land, the area clear (which asks AllowsRemoval of each object it would take), Backspace over a path, queue, pool bridge or pool water cell, and the queue's edit
		BuyLandCommand or ClearAreaCommand or EditQueueCommand => true,
		BacktrackCommand => level.Paths != null,
		// the sale of an ordinary placed object (never the developer prototype; a sale of an object a later record removed is the park's to refuse, as the host's is)
		RemoveAttractionCommand sale => level.ResolveCommandObject( sale.Attraction ) is not { IsDeleted: false } item
			|| (!ReferenceEquals( item, level.PlacedRide ) && GuestMayRemove( item.Entry )),
		// C3b-2: the Move tool's pick-up of an object the guest may remove and place again (never the developer prototype); a missing object is the park's to refuse
		PickUpObjectCommand pick => level.ResolveCommandObject( pick.Attraction ) is not { IsDeleted: false } item
			|| (!ReferenceEquals( item, level.PlacedRide ) && GuestMayRemove( item.Entry ) && GuestMayPlace( item.Entry )),
		// C3b-3: a guest's pool strokes go in batches (the pool builders refuse every cell that is not theirs: a path, a queue, an object), its bridges, and the last piece of a slide that is
		// not finished (a finished slide takes riders: its pieces are the host's, or go with its tower; a slide with no piece to take back is the park's to refuse, quietly, as the host's
		// Backspace is)
		PoolCellsCommand or LayPoolSlidePiecesCommand or PlacePoolBridgeCommand => true,
		UndoPoolSlidePieceCommand undo => level.PoolSlides?.Find( PoolSlide.KeyOf( undo.TowerX, undo.TowerY ) ) is not { IsComplete: true },
		// [EXT:online-coop-claims] C3c-1: a ride's claim (never the developer prototype or a coaster, whose tool is the host's until C3c-2; a missing ride or one that is no track ride
		// is the park's to refuse), and the track commands, of the ride whose claim the guest holds
		ClaimRideCommand claim => level.ResolveCommandObject( claim.Ride ) is not { IsDeleted: false } item
			|| (!ReferenceEquals( item, level.PlacedRide ) && item.Runtime?.Coaster == null),
		OpenTrackLineToolCommand or LayTrackLineCommand or UndoTrackLineCommand or CutTrackCommand => true,
		_ => false
	};

	/// <summary>[EXT:online-coop-claims] Whether <paramref name="player"/> holds the claim a track command needs: a guest edits the track of the ride whose claim it took (C3c-1).</summary>
	private bool HoldsClaimFor( int player, ParkCommand command ) => TrackRide( command ) is not { } ride || level.ClaimOf( ride ) == player;

	/// <summary>The ride a track command edits (its attraction id), or null for any other command.</summary>
	private static int? TrackRide( ParkCommand command ) => command switch
	{
		OpenTrackLineToolCommand open => open.Ride,
		LayTrackLineCommand line => line.Ride,
		UndoTrackLineCommand undo => undo.Ride,
		CutTrackCommand cut => cut.Ride,
		_ => null
	};

	/// <summary>
	/// [EXT:online-coop-claims] Whether <paramref name="command"/> of <paramref name="player"/> leaves alone every ride claimed against that player (<see cref="ClaimedAgainst"/>): the ride itself
	/// (its track, its sale, its removal, its pick-up, its demolition by a placement, an add-on on it), its queue's lines, their undo, its edit and its cells, and the path cell its back
	/// joins, which no removal, area clear, line or placement takes. Asked of every player, the host included, so a claim's holder edits its ride alone (C3c-1); while nothing is claimed
	/// against the player it asks nothing.
	/// </summary>
	internal bool LeavesClaimsAlone( int player, ParkCommand command ) => !CanBeClaimedAgainst( player ) || command switch
	{
		// a claimed ride's queue lines and their undo; no line crosses its cells (a path line paves a queue end), and no queue line takes the path cell its back joins (the queue writers
		// lay queue over path); an undo takes the cells its line built off again and counts down the ones it crossed
		LayLineCommand line => (line.Mode != CellToolMode.Queue || !ClaimedRide( player, line.Ride ))
			&& !CrossesClaimedQueue( player, line.StartX, line.StartY, line.EndX, line.EndY, joins: line.Mode == CellToolMode.Queue, endOnPathLinks: line.Mode == CellToolMode.Queue ),
		UndoLineCommand undo => (undo.Mode != CellToolMode.Queue || !ClaimedRide( player, undo.Ride ))
			&& !TouchesClaimedQueue( player, undo.Built.Concat( undo.Existing ).ToArray(), joins: true ),
		QueueSegmentCommand line => !ClaimedRide( player, line.Ride ) && !CrossesClaimedQueue( player, line.StartX, line.StartY, line.EndX, line.EndY, joins: true ),
		UndoQueueSegmentCommand undo => !ClaimedRide( player, undo.Ride ) && !CrossesClaimedQueue( player, undo.StartX, undo.StartY, undo.EndX, undo.EndY, joins: true ),
		// no add-on on a claimed ride, nor a placement that demolishes what is claimed or writes over a claimed ride's queue or the path cell its back joins
		PlaceObjectCommand place => (place.AddOnRide < 0 || !ClaimedRide( player, place.AddOnRide ))
			&& (level.ResolveCommandEntry( place.Entry ) is not { } entry || PlacementLeavesClaimsAlone( player, entry, place.X, place.Y, place.Rotation )),
		RemoveAtCommand remove => level.Objects?.FindAt( remove.X, remove.Y ) is { IsDeleted: false } item ? !ClaimedAgainst( player, item )
			: !ClaimedQueueAt( player, remove.X, remove.Y ) && !JoinsClaimedQueue( player, remove.X, remove.Y ),
		RemoveObjectAtCommand remove => level.Objects?.FindAt( remove.X, remove.Y ) is not { IsDeleted: false } item || !ClaimedAgainst( player, item ),
		RemovePathCellCommand cell => !JoinsClaimedQueue( player, cell.X, cell.Y ),
		RemoveQueueCellCommand cell => !ClaimedQueueAt( player, cell.X, cell.Y ),
		BacktrackCommand back => level.Paths is not { } paths || Level.BacktrackTargetAt( paths.Cells, back.X, back.Y, level.PoolBridges ) switch
		{
			// C3b-3: a pool bridge, unless an end of it is a claimed queue's or the path cell it joins; a pool water cell
			Level.BacktrackTarget.PoolBridge => level.PoolBridges?.At( back.X, back.Y ) is not { } standing || !BridgeTouchesClaimedQueue( player, standing ),
			Level.BacktrackTarget.PoolWater => true,
			Level.BacktrackTarget.Queue => !ClaimedQueueAt( player, back.X, back.Y ),
			_ => !JoinsClaimedQueue( player, back.X, back.Y )
		},
		RemoveAttractionCommand sale => level.ResolveCommandObject( sale.Attraction ) is not { IsDeleted: false } item || !ClaimedAgainst( player, item ),
		EditQueueCommand edit => !ClaimedQueueAt( player, edit.X, edit.Y ),
		PickUpObjectCommand pick => level.ResolveCommandObject( pick.Attraction ) is not { IsDeleted: false } item || !ClaimedAgainst( player, item ),
		PlacePoolBridgeCommand bridge => !BridgeTouchesClaimedQueue( player, new PoolBridge( bridge.X, bridge.Y, bridge.Direction, 0 ) ),
		RemovePoolBridgeCommand bridge => level.PoolBridges?.At( bridge.X, bridge.Y ) is not { } standing || !BridgeTouchesClaimedQueue( player, standing ),
		RemovePrototypeCommand => level.PlacedRide is not { IsDeleted: false } prototype || !ClaimedAgainst( player, prototype ),
		// the track commands of a ride another seat claims
		_ => TrackRide( command ) is not { } ride || !level.ClaimedAgainst( player, ride )
	};

	/// <summary>
	/// Whether <paramref name="player"/> may remove <paramref name="item"/> inside a command that removes what it finds (the area clear): the host what no other player claims, a granted
	/// guest an ordinary object no other player claims (<see cref="MayRemove"/>), any other seat nothing.
	/// </summary>
	public bool AllowsRemoval( int player, OriginalObject item ) => player == ParkCommands.LocalPlayer ? !ClaimedAgainst( player, item )
		: player > 0 && level.HasBuildGrant( player ) && MayRemove( player, item );

	/// <summary>
	/// Whether <paramref name="player"/> may clear the path cell (<paramref name="x"/>, <paramref name="y"/>) inside a command that clears what it finds (the area clear): the host and a
	/// granted guest any but the one a ride claimed against them joins (<see cref="JoinsClaimedQueue"/>), any other seat none.
	/// </summary>
	public bool AllowsPathRemoval( int player, int x, int y ) => (player == ParkCommands.LocalPlayer || (player > 0 && level.HasBuildGrant( player ))) && !JoinsClaimedQueue( player, x, y );

	/// <summary>Whether a granted guest (<paramref name="player"/>) may remove <paramref name="item"/>: not a fixed item of the park, nor a ride claimed against it.</summary>
	private bool MayRemove( int player, OriginalObject item ) => GuestMayRemove( item.Entry ) && !ClaimedAgainst( player, item );

	/// <summary>
	/// Whether a granted guest may remove an object of <paramref name="entry"/>: not a fixed item of the park (its gates, its lights, its bus: <see cref="ObjectCatalogEntry.IsFixedItem"/>).
	/// A slide tower is a guest's to remove since its slide is (C3b-3): its removal takes the slide with it and refunds it.
	/// </summary>
	internal static bool GuestMayRemove( ObjectCatalogEntry entry ) => !entry.IsFixedItem;

	/// <summary>Whether a granted guest may place <paramref name="entry"/>: not a track add-on (the track tools are the host's) and not a ride whose own tool follows its placement.</summary>
	internal static bool GuestMayPlace( ObjectCatalogEntry entry ) => !TrackAddOns.IsTrackAddOn( entry ) && !OwnToolFollows( entry );

	/// <summary>
	/// Whether a placed <paramref name="entry"/> opens a tool of its own that no guest's request carries yet (<c>ParkHud.FinishPlacement</c>): a coaster's track (C3c-2), a kart or water ride's
	/// track (C3c-1b). A bumper ride lays no track, and its queue tool follows its placement (C3c-1); a slide tower's slide mode is a guest's since C3b-3: it lays its pieces in batches.
	/// </summary>
	internal static bool OwnToolFollows( ObjectCatalogEntry entry ) => CoasterDefinition.IsCoaster( entry.Settings )
		|| (entry.Settings.GetInt( "Bumper.WhichTrackType" ) is 1 or 2 && BumperTemplates.ForRide( entry.Settings.GetInt( "Bumper.WhichTrackType" ), entry.Settings.GetInt( "Bumper.BumperType" ) ) != null);

	/// <summary>Whether anything can be claimed against <paramref name="player"/>: for a guest always (a coaster is the host's until C3c-2), for the host while another seat holds a claim.</summary>
	private bool CanBeClaimedAgainst( int player ) => player != ParkCommands.LocalPlayer || level.HasClaimsAgainst( player );

	/// <summary>Whether the ride with attraction id <paramref name="ride"/> is claimed against <paramref name="player"/>.</summary>
	private bool ClaimedRide( int player, int ride ) => level.ResolveCommandObject( ride ) is { IsDeleted: false } item && ClaimedAgainst( player, item );

	/// <summary>Whether the queue cell (<paramref name="x"/>, <paramref name="y"/>) belongs to the queue of a ride claimed against <paramref name="player"/>.</summary>
	private bool ClaimedQueueAt( int player, int x, int y ) => level.QueueOwnerAt( x, y ) is { } owner && ClaimedAgainst( player, owner );

	/// <summary>
	/// Whether a line from (<paramref name="startX"/>, <paramref name="startY"/>) toward (<paramref name="endX"/>, <paramref name="endY"/>), snapped as the line writers snap it, crosses the queue
	/// cell of a ride claimed against <paramref name="player"/> (a path line paves a queue end; a queue line of another ride may take over a cell no owner word marks yet), or, with
	/// <paramref name="joins"/> (a queue line, which may lay queue over path), the path cell such a queue joins. With <paramref name="endOnPathLinks"/> (the queue tool's line) a last cell that
	/// is a path cell only links the queue to that path (<see cref="QueuePaths.Connect"/>), so it takes no joined queue's link off: it is asked for itself alone. The cells' owners are resolved
	/// together (<see cref="Level.QueueOwnersAt"/>).
	/// </summary>
	private bool CrossesClaimedQueue( int player, int startX, int startY, int endX, int endY, bool joins, bool endOnPathLinks = false )
	{
		var start = (startX, startY);
		var cells = ParkPathBuilder.LayLine( start, ParkPathBuilder.SnapEnd( start, (endX, endY) ) ).ToArray();
		if ( endOnPathLinks && joins && cells.Length > 1 && level.Guests?.Grid is { } grid && grid.IsWalkable( cells[^1].X, cells[^1].Y ) )
			return TouchesClaimedQueue( player, cells[..^1], joins: true ) || TouchesClaimedQueue( player, cells[^1..], joins: false );
		return TouchesClaimedQueue( player, cells, joins );
	}

	/// <summary>
	/// Whether <paramref name="player"/>'s placement of <paramref name="entry"/> on (<paramref name="x"/>, <paramref name="y"/>) turned <paramref name="rotation"/> leaves alone what is claimed
	/// against that player, as the placement's own check plans it (<see cref="ParkObjects.Check(ObjectCatalogEntry, int, int, int, out ObjectPlacementPlan)"/>, centred as
	/// <see cref="Level.PlaceObject"/> centres it): no object it would demolish (one of a lower overwrite priority) is a ride claimed against the player, nor, for a guest, one it may not
	/// remove (<see cref="MayRemove"/>), and no cell it writes is a claimed ride's queue cell or the path cell its back joins: its footprint, the path cells it clears (beyond a track end too)
	/// and the cell outside a queue ride's entrance, which it lays as queue (taking the links of a path cell there off). The cell outside its other entrance and its exit it lays or links as
	/// path, over a path too, which takes no link off: they are asked for themselves alone (a path paves a queue's end). The placement lays only its last entrance's and its last exit's
	/// (<see cref="ObjectAccessCells.Lay"/>), so only they are asked. A placement the park refuses is the park's to refuse.
	/// </summary>
	internal bool PlacementLeavesClaimsAlone( int player, ObjectCatalogEntry entry, int x, int y, int rotation )
	{
		var anchor = Level.GetCentredAnchor( entry, x, y, rotation );
		if ( level.Objects is not { } objects || objects.Check( entry, anchor.X, anchor.Y, rotation, out var plan ) != OriginalPlacementResult.Allowed )
			return true;
		if ( plan.Overwritten.Any( item => player == ParkCommands.LocalPlayer ? ClaimedAgainst( player, item ) : !MayRemove( player, item ) ) )
			return false;
		var (entrance, exit) = ObjectAccessCells.Find( ObjectFootprint.GetAccessPoints( entry.Shape, anchor.X, anchor.Y, rotation ) );
		var queueEntrance = entry.HasQueue && entrance is { } enter ? new[] { (enter.OutsideX, enter.OutsideY) } : Array.Empty<(int, int)>();
		var written = ObjectFootprint.GetCells( entry.Shape, anchor.X, anchor.Y, rotation ).Select( cell => (cell.X, cell.Y) ).Concat( plan.ClearedPaths ).Concat( queueEntrance )
			.Distinct().ToArray();
		var paths = new[] { entry.HasQueue ? null : entrance, exit }.OfType<ObjectAccessPoint>().Select( point => (point.OutsideX, point.OutsideY) ).Distinct().ToArray();
		return !TouchesClaimedQueue( player, written, joins: true ) && !TouchesClaimedQueue( player, paths, joins: false );
	}

	/// <summary>
	/// Whether <paramref name="cells"/> hold the queue cell of a ride claimed against <paramref name="player"/>, or, with <paramref name="joins"/>, the path cell such a queue joins. The cells'
	/// owners are resolved together (<see cref="Level.QueueOwnersAt"/>).
	/// </summary>
	private bool TouchesClaimedQueue( int player, IReadOnlyList<(int X, int Y)> cells, bool joins )
	{
		if ( level.Guests?.Grid is not { } grid )
			return false;
		var asked = joins ? cells.Concat( cells.SelectMany( cell => JoinedQueueCells( grid, cell.X, cell.Y ) ) ) : cells;
		var claimed = level.QueueOwnersAt( asked ).Where( cell => cell.Owner is { } owner && ClaimedAgainst( player, owner ) ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
		return claimed.Count > 0 && (cells.Any( claimed.Contains ) || (joins && cells.Any( cell => JoinedQueueCells( grid, cell.X, cell.Y ).Any( claimed.Contains ) )));
	}

	/// <summary>
	/// Whether (<paramref name="x"/>, <paramref name="y"/>) is a path cell the queue of a ride claimed against <paramref name="player"/> joins: a removal of it, or a queue laid over it, would take
	/// the link of the queue's back cell off (<see cref="PathLinkWriter.Unlink"/>).
	/// </summary>
	private bool JoinsClaimedQueue( int player, int x, int y ) => level.Guests?.Grid is { } grid
		&& level.QueueOwnersAt( JoinedQueueCells( grid, x, y ) ).Any( cell => cell.Owner is { } owner && ClaimedAgainst( player, owner ) );

	/// <summary>The queue cells a path cell (<paramref name="x"/>, <paramref name="y"/>) is joined to: its four neighbours that are queue cells, linked to it on either side.</summary>
	private static IEnumerable<(int X, int Y)> JoinedQueueCells( GuestPathGrid grid, int x, int y )
	{
		if ( !grid.Cells.InBounds( x, y ) || grid.Cells.TypeAt( x, y ) != ParkCellType.Path )
			yield break;
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			var (nx, ny) = (x + dx, y + dy);
			if ( !grid.Cells.InBounds( nx, ny ) || !grid.IsQueue( nx, ny ) )
				continue;
			if ( (grid.Cells.LinksAt( x, y ) & GuestPathGrid.LinkValue( direction )) != 0 || (grid.Cells.LinksAt( nx, ny ) & GuestPathGrid.LinkValue( (direction + 2) % 4 )) != 0 )
				yield return (nx, ny);
		}
	}

	/// <summary>
	/// Whether a bridge's cells (its seven and the two outside ends, <see cref="PoolTool.HoverBridgeCells"/>) hold the queue cell of a ride claimed against <paramref name="player"/> or the path
	/// cell its back joins.
	/// </summary>
	private bool BridgeTouchesClaimedQueue( int player, PoolBridge bridge ) =>
		TouchesClaimedQueue( player, Enumerable.Range( -1, PoolBridge.Length + 2 ).Select( bridge.Cell ).ToArray(), joins: true );

	/// <summary>
	/// Whether <paramref name="item"/> is claimed against <paramref name="player"/>: the object window offers neither its track tool nor its queue's tool to that player, and its edits are
	/// refused (<see cref="LeavesClaimsAlone"/>).
	/// </summary>
	internal bool IsClaimedAgainst( int player, OriginalObject item ) => ClaimedAgainst( player, item );

	/// <summary>
	/// [EXT:online-coop-claims] Whether <paramref name="item"/> is claimed against <paramref name="player"/>: a track ride whose claim another seat holds (C3c-1), or, for a guest, a coaster, whose
	/// edits stay the host's until a guest's coaster tool claims (C3c-2).
	/// </summary>
	private bool ClaimedAgainst( int player, OriginalObject item ) => (player != ParkCommands.LocalPlayer && item.Runtime?.Coaster != null)
		|| (level.TrackStations.ContainsKey( item ) && item.Runtime is { } runtime && level.ClaimedAgainst( player, runtime.AttractionId ));
}

/// <summary>[EXT:online-coop-permissions] What a refusal by a shared park's rights says to its player (<see cref="CoopBuildRights.RefusalOf"/>).</summary>
internal enum CoopRefusal
{
	/// <summary>A command only the host may issue.</summary>
	HostOnly,
	/// <summary>Building the host has not granted this player yet.</summary>
	NotGranted,
	/// <summary>[EXT:online-coop-claims] An edit of a ride another player claims (C3c-1).</summary>
	Claimed,
}
