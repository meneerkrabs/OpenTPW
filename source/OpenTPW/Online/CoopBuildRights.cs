namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-permissions] A shared park's rights (docs/COOP.md, "Building by every player"; the user's decision Q7: the host sets rights per player). The host's seat 0 may issue
/// every command; a guest's seat may issue the building the host granted it (<see cref="SetBuildGrantCommand"/>, kept in the park on every peer) and nothing else. The building is an
/// allowlist over a command's kind, its fields and its target, the commands of the guests' tools that wait for the host or send and go on:
/// <list type="bullet">
/// <item>C3a: path and queue lines; an object placed (not a moving placement, not a track add-on, whatever ride the command names, not a ride whose own tool follows its placement: a
/// coaster, a kart, water or bumper ride, a slide tower); the remove click on an ordinary object (not a fixed item of the park, not a slide tower, not a coaster's or a track ride's
/// station, whose edits need claims);</item>
/// <item>C3b: land; the area clear, which leaves standing what the guest may not remove (<see cref="AllowsRemoval"/>); the remove click on a path or queue cell; Backspace over a
/// path or queue cell; the sale of an ordinary object; the queue's edit; the Move tool's pick-up of an object it may remove and place again, its placement, and its end, which every
/// guest seat may issue, granted or not.</item>
/// </list>
/// The queue of a claimed ride (a coaster's or a track ride's station) stays the host's in all of these until claims come (C3c): its lines, its edit, its cells' removal and Backspace
/// over them, a line over its cells, and the path cell its back joins, which no guest's removal, area clear or queue line takes.
/// Everything else stays the host's: speed and pause (Q8), cheats (Q17), the developer kinds, money, staff and the park's management (C4), undo lines (C5), the pool tools, tracks and
/// coasters (C3b, C3c), the grants and the release of a departed seat. Asked at execution on every peer, of the same park, the answer is the same everywhere.
/// </summary>
internal sealed class CoopBuildRights( Level level ) : IParkCommandRights
{
	public bool Allows( int player, ParkCommandKind kind ) => player == ParkCommands.LocalPlayer;

	public bool Allows( int player, ParkCommand command )
	{
		if ( player == ParkCommands.LocalPlayer )
			return true;
		// [EXT:online-coop-commands] a Move tool's end only drops its own seat's carry, and a Move must end even when the grant went meanwhile (C3b-2)
		if ( player > 0 && command is EndMoveCommand )
			return true;
		return player > 0 && level.HasBuildGrant( player ) && Builds( command );
	}

	/// <summary>Whether <paramref name="command"/> is building a granted guest may do (C3a, C3b): its kind, its fields and its target.</summary>
	internal bool Builds( ParkCommand command ) => command switch
	{
		// a claimed ride's queue (a coaster's or a track ride's station, whose edits need claims, C3c) is the host's: its lines, its edit, its cells' removal and Backspace over them; no
		// line crosses its cells (a path line paves a queue end), and no queue line takes the path cell its back joins (the queue writers lay queue over path)
		LayLineCommand line => (line.Mode != CellToolMode.Queue || !ClaimedRide( line.Ride ))
			&& !CrossesClaimedQueue( line.StartX, line.StartY, line.EndX, line.EndY, joins: line.Mode == CellToolMode.Queue ),
		QueueSegmentCommand line => !ClaimedRide( line.Ride ) && !CrossesClaimedQueue( line.StartX, line.StartY, line.EndX, line.EndY, joins: true ),
		// an object placed, or the Move tool's placement (C3b-2: the command itself checks that its player carries the type); a track add-on belongs to the track tools, which come later,
		// as do the rides whose own tool follows their placement; a track add-on's entry is refused whatever ride the command names, so a request without one cannot place it as an
		// ordinary object
		PlaceObjectCommand place => place.AddOnRide < 0 && level.ResolveCommandEntry( place.Entry ) is { } entry && GuestMayPlace( entry ),
		// the remove click takes an ordinary object, else the queue or path cell under it (C3b)
		RemoveAtCommand remove => level.Objects?.FindAt( remove.X, remove.Y ) is { IsDeleted: false } item ? MayRemove( item )
			: !ClaimedQueueAt( remove.X, remove.Y ) && !JoinsClaimedQueue( remove.X, remove.Y ),
		// C3b: land, the area clear (which asks AllowsRemoval of each object it would take), Backspace over a path or queue cell (a pool bridge or water cell is the pool tools'),
		// the sale of an ordinary placed object (never the developer prototype) and the queue's edit
		BuyLandCommand => true,
		ClearAreaCommand => true,
		BacktrackCommand back => level.Paths is { } paths && Level.BacktrackTargetAt( paths.Cells, back.X, back.Y, level.PoolBridges ) switch
		{
			Level.BacktrackTarget.PoolBridge or Level.BacktrackTarget.PoolWater => false,
			Level.BacktrackTarget.Queue => !ClaimedQueueAt( back.X, back.Y ),
			_ => !JoinsClaimedQueue( back.X, back.Y )
		},
		// (a sale of an object a later record removed is the park's to refuse, as the host's is)
		RemoveAttractionCommand sale => level.ResolveCommandObject( sale.Attraction ) is not { IsDeleted: false } item || (!ReferenceEquals( item, level.PlacedRide ) && MayRemove( item )),
		EditQueueCommand edit => !ClaimedQueueAt( edit.X, edit.Y ),
		// C3b-2: the Move tool's pick-up of an object the guest may remove and place again (never the developer prototype); a missing object is the park's to refuse
		PickUpObjectCommand pick => level.ResolveCommandObject( pick.Attraction ) is not { IsDeleted: false } item
			|| (!ReferenceEquals( item, level.PlacedRide ) && MayRemove( item ) && GuestMayPlace( item.Entry )),
		_ => false
	};

	/// <summary>
	/// Whether <paramref name="player"/> may remove <paramref name="item"/> inside a command that removes what it finds (the area clear): the host anything, a granted guest an ordinary
	/// object (<see cref="MayRemove"/>), any other seat nothing.
	/// </summary>
	public bool AllowsRemoval( int player, OriginalObject item ) => player == ParkCommands.LocalPlayer || (player > 0 && level.HasBuildGrant( player ) && MayRemove( item ));

	/// <summary>
	/// Whether <paramref name="player"/> may clear the path cell (<paramref name="x"/>, <paramref name="y"/>) inside a command that clears what it finds (the area clear): the host any, a
	/// granted guest any but the one a claimed ride's queue joins (<see cref="JoinsClaimedQueue"/>), any other seat none.
	/// </summary>
	public bool AllowsPathRemoval( int player, int x, int y ) => player == ParkCommands.LocalPlayer || (player > 0 && level.HasBuildGrant( player ) && !JoinsClaimedQueue( x, y ));

	/// <summary>Whether a granted guest may remove <paramref name="item"/>: not a fixed item of the park, not a claimed station.</summary>
	private bool MayRemove( OriginalObject item ) => GuestMayRemove( item.Entry ) && !Claimed( item );

	/// <summary>
	/// Whether a granted guest may remove an object of <paramref name="entry"/>: not a fixed item of the park (its gates, its lights, its bus: <see cref="ObjectCatalogEntry.IsFixedItem"/>),
	/// and not a slide tower, whose slide is the pool tools' (C3b-3), as its bridges and water are.
	/// </summary>
	internal static bool GuestMayRemove( ObjectCatalogEntry entry ) => !entry.IsFixedItem && !PoolSlideTowers.IsSlideTower( entry );

	/// <summary>Whether a granted guest may place <paramref name="entry"/>: not a track add-on (the track tools are the host's) and not a ride whose own tool follows its placement.</summary>
	internal static bool GuestMayPlace( ObjectCatalogEntry entry ) => !TrackAddOns.IsTrackAddOn( entry ) && !OwnToolFollows( entry );

	/// <summary>
	/// Whether a placed <paramref name="entry"/> opens a tool of its own (<c>ParkHud.FinishPlacement</c>): a coaster's track, a kart, water or bumper ride's track or arena, a slide tower's
	/// slide. Those tools hold state no guest's request carries yet (C3b, C3c).
	/// </summary>
	internal static bool OwnToolFollows( ObjectCatalogEntry entry ) => CoasterDefinition.IsCoaster( entry.Settings )
		|| BumperTemplates.ForRide( entry.Settings.GetInt( "Bumper.WhichTrackType" ), entry.Settings.GetInt( "Bumper.BumperType" ) ) != null
		|| PoolSlideTowers.IsSlideTower( entry );

	/// <summary>Whether the ride with attraction id <paramref name="ride"/> is claimed: its queue is the host's until claims come (C3c).</summary>
	private bool ClaimedRide( int ride ) => level.ResolveCommandObject( ride ) is { IsDeleted: false } item && Claimed( item );

	/// <summary>Whether the queue cell (<paramref name="x"/>, <paramref name="y"/>) belongs to a claimed ride's queue.</summary>
	private bool ClaimedQueueAt( int x, int y ) => level.QueueOwnerAt( x, y ) is { } owner && Claimed( owner );

	/// <summary>
	/// Whether a line from (<paramref name="startX"/>, <paramref name="startY"/>) toward (<paramref name="endX"/>, <paramref name="endY"/>), snapped as the line writers snap it, crosses a
	/// claimed ride's queue cell (a path line paves a queue end; a queue line of another ride may take over a cell no owner word marks yet), or, with <paramref name="joins"/> (a queue
	/// line, which may lay queue over path), the path cell a claimed ride's queue joins. The cells' owners are resolved together (<see cref="Level.QueueOwnersAt"/>).
	/// </summary>
	private bool CrossesClaimedQueue( int startX, int startY, int endX, int endY, bool joins )
	{
		if ( level.Guests?.Grid is not { } grid )
			return false;
		var start = (startX, startY);
		var line = ParkPathBuilder.LayLine( start, ParkPathBuilder.SnapEnd( start, (endX, endY) ) ).ToArray();
		var asked = joins ? line.Concat( line.SelectMany( cell => JoinedQueueCells( grid, cell.X, cell.Y ) ) ) : line;
		var claimed = level.QueueOwnersAt( asked ).Where( cell => cell.Owner is { } owner && Claimed( owner ) ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
		return line.Any( claimed.Contains ) || (joins && line.Any( cell => JoinedQueueCells( grid, cell.X, cell.Y ).Any( claimed.Contains ) ));
	}

	/// <summary>
	/// Whether (<paramref name="x"/>, <paramref name="y"/>) is a path cell a claimed ride's queue joins: a removal of it, or a queue laid over it, would take the link of the queue's back cell
	/// off (<see cref="PathLinkWriter.Unlink"/>).
	/// </summary>
	private bool JoinsClaimedQueue( int x, int y ) => level.Guests?.Grid is { } grid
		&& level.QueueOwnersAt( JoinedQueueCells( grid, x, y ) ).Any( cell => cell.Owner is { } owner && Claimed( owner ) );

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

	/// <summary>Whether <paramref name="item"/> is claimed for a guest (a coaster's or a track ride's station): the object window offers its queue's tool to the host alone.</summary>
	internal bool IsClaimed( OriginalObject item ) => Claimed( item );

	/// <summary>Whether <paramref name="item"/> is a coaster's or a track ride's station, whose edits need a claim (C3c): a guest may not remove it yet.</summary>
	private bool Claimed( OriginalObject item ) => item.Runtime?.Coaster != null || level.TrackStations.ContainsKey( item );
}
