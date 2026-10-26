namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-commands] The level's command layer (docs/COOP.md): <see cref="Commands"/> is the one funnel the HUD and the tools hand their edits to, and
/// <see cref="ExecuteCommand"/> runs one command on the park. The executor reads only the command and the world, never a tool's state (the armed entry, the build angle,
/// the move flag), so the same command does the same on every peer and in a replay.
/// </summary>
public partial class Level
{
	private ParkCommands? commands;
	private ParkCommandRecorder? commandRecorder;
	/// <summary>
	/// The catalogue entry each player picked up with the Move tool (<see cref="PickUpObjectCommand"/>), until its tool ends (<see cref="EndMoveCommand"/>): a moving placement must name it (the Move
	/// tool's purchase skips research and the balance test). A placement keeps it, as the tool may stay for another of the type (a click holding exactly Ctrl).
	/// </summary>
	private Dictionary<int, string>? carriedEntries;
	/// <summary>While a <see cref="PlaceObjectCommand"/> runs: whether it is the Move tool's placement (<see cref="PlaceObject(ObjectCatalogEntry, int, int, int)"/> reads it instead of <see cref="IsMoving"/>).</summary>
	private bool? commandPlacementMoving;

	/// <summary>The park's edit funnel; every edit of a HUD window or tool goes through it.</summary>
	public ParkCommands Commands => commands ??= new ParkCommands( () => simulationClock?.TickCount ?? 0, ExecuteCommand, () => IsReadOnlyVisit );

	/// <summary>Unit-test seam: replaces the command funnel so a fixture records what a click issues, without running the park's rules.</summary>
	internal void UseCommandsForTests( ParkCommands replacement ) => commands = replacement;

	/// <summary>The recording of this park's commands (<c>--record-commands</c>), or null.</summary>
	public ParkCommandRecorder? CommandRecorder => commandRecorder;

	/// <summary>Starts recording the park's commands and its hash checkpoints from now on (<c>--record-commands</c>); a recording already running is kept.</summary>
	public ParkCommandRecorder StartCommandRecording() =>
		commandRecorder ??= new ParkCommandRecorder( Commands, LevelName ?? "", Seed.Value, simulationClock?.TickCount ?? 0, ComputeStateHash() );

	/// <summary>The command log recorded so far, with the world hash now as its final checkpoint; null while nothing records.</summary>
	public ParkCommandLogFile? CommandLog() =>
		commandRecorder?.ToLog( (simulationClock?.TickCount ?? 0, ComputeStateHash()) );

	/// <summary>The start of every simulation tick: the commands held for this tick apply first (a replayed log, later a session's frames).</summary>
	private void ApplyDueCommands() => commands?.ApplyDue( simulationClock.TickCount );

	/// <summary>The end of every simulation tick (<paramref name="ticks"/> run so far): a recording takes its hash checkpoint.</summary>
	private void AfterCommandTick( long ticks ) => commandRecorder?.AfterTick( ticks, ComputeStateHash );

	/// <summary>The catalogue entry a command names (<see cref="ParkCommandCodec.EntryKey"/>): one of the theme's catalogue, or an entry an object of the park was built from.</summary>
	internal ObjectCatalogEntry? ResolveCommandEntry( string key )
	{
		if ( Objects?.Catalog?.Entries is { } entries )
			foreach ( var entry in entries )
				if ( string.Equals( ParkCommandCodec.EntryKey( entry ), key, StringComparison.Ordinal ) )
					return entry;
		return Objects?.Objects.Select( item => item.Entry ).FirstOrDefault( entry => string.Equals( ParkCommandCodec.EntryKey( entry ), key, StringComparison.Ordinal ) );
	}

	/// <summary>The placed object (or the prototype ride) whose attraction id is <paramref name="attraction"/>, or null.</summary>
	internal OriginalObject? ResolveCommandObject( int attraction )
	{
		if ( PlacedRide is { IsDeleted: false } prototype && prototype.Visitors.AttractionId == attraction )
			return prototype;
		return Objects?.Objects.FirstOrDefault( item => !item.IsDeleted && item.Visitors.AttractionId == attraction );
	}

	/// <summary>The HasQueue ride whose attraction id is <paramref name="attraction"/>, or null.</summary>
	private RideVisitorBridge? ResolveCommandRide( int attraction ) =>
		Guests?.Attractions.OfType<RideVisitorBridge>().FirstOrDefault( ride => ride.HasQueue && ride.AttractionId == attraction );

	/// <summary>
	/// <see cref="ResolveCommandRide"/> for a queue line command, which goes ahead once its ride is found, its queue brought up to date first (as the queue edit's is): the queue tool's
	/// entry only reads the queue (<see cref="EnterQueueTool"/>), so a grid edit since the last recompute is taken in here, on every peer alike.
	/// </summary>
	private RideVisitorBridge? ResolveQueueCommandRide( int attraction )
	{
		if ( ResolveCommandRide( attraction ) is not { } ride || Guests == null )
			return null;
		ride.RecomputeQueue( Guests.Grid );
		return ride;
	}

	/// <summary>The line writer a command's line is laid and taken back through: the path builder, or the queue writer of the ride.</summary>
	private ICellLineWriter? CommandLineWriter( CellToolMode mode, int ride ) => mode switch
	{
		CellToolMode.Path => Paths,
		CellToolMode.Queue when ResolveQueueCommandRide( ride ) is { } bridge => CreateQueueWriter( bridge ),
		_ => null
	};

	/// <summary>Test seam: runs before every command the park executes; a throw makes the command errored, as a rule its own checks missed would (the co-op stream's checks).</summary>
	internal Action<ParkCommand>? CommandFault { get; set; }

	/// <summary>Runs one well-formed command on the park (<see cref="ParkCommands"/> has checked the visit and the player's rights).</summary>
	private ParkCommandResult ExecuteCommand( ParkCommand command )
	{
		CommandFault?.Invoke( command );
		return ExecuteParkCommand( command );
	}

	private ParkCommandResult ExecuteParkCommand( ParkCommand command ) => command switch
	{
		PlaceObjectCommand place => ExecutePlaceObject( place ),
		RemoveAtCommand remove => Outcome( RemoveAt( remove.X, remove.Y ) ),
		RemoveObjectAtCommand remove => Outcome( RemoveObjectAt( remove.X, remove.Y ) ),
		ClearAreaCommand clear => ExecuteClearArea( clear ),
		RemoveAttractionCommand remove => ResolveCommandObject( remove.Attraction ) is { } item
			? Outcome( RemoveAttraction( item ), item )
			: ParkCommandResult.Rejected( $"No attraction {remove.Attraction}." ),
		PickUpObjectCommand pickUp => ExecutePickUp( pickUp ),
		EndMoveCommand end => ExecuteEndMove( end ),
		LayLineCommand line => CommandLineWriter( line.Mode, line.Ride ) is { } writer
			? ParkCommandResult.Ok( writer is QueueLineWriter legacyQueue ? legacyQueue.CommitLegacy( (line.StartX, line.StartY), (line.EndX, line.EndY) ) : writer.Commit( (line.StartX, line.StartY), (line.EndX, line.EndY) ) )
			: ParkCommandResult.Rejected( $"No {line.Mode} line can be laid here." ),
		QueueSegmentCommand line => ExecuteQueueSegment( line ),
		UndoQueueSegmentCommand undo => ExecuteQueueSegmentUndo( undo ),
		UndoLineCommand undo => ExecuteUndoLine( undo ),
		RemovePathCellCommand remove => RemovePathCell( remove.X, remove.Y ) is var result && result == CellBuildResult.Ok ? ParkCommandResult.Ok( result ) : new ParkCommandResult( false, $"No path cell removed: {result}.", result ),
		RemoveQueueCellCommand remove => RemoveQueueCell( remove.X, remove.Y ) is var count and > 0 ? ParkCommandResult.Ok( count ) : ParkCommandResult.Rejected( "No queue cell here." ),
		BacktrackCommand backtrack => Backtrack( backtrack.X, backtrack.Y ),
		EditQueueCommand edit => ExecuteEditQueue( edit ),
		PlacePrototypeCommand prototype => Outcome( PlaceRide( new Vector3( prototype.X, prototype.Y, prototype.Z ) ) ),
		RemovePrototypeCommand => ExecuteRemovePrototype(),
		DismissFinaleCommand => DismissResearchFinale() ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "No research finale to dismiss." ),
		SetPrototypeRunningCommand running => ExecuteSetPrototypeRunning( running ),
		ParkSettingCommand setting => ParkSettingCommand.Run( this, setting ),
		ParkToolCommand tool => ParkToolCommand.Run( this, tool ),
		_ => ParkCommandResult.Rejected( $"The park does not take {command.Kind} commands." )
	};

	private ParkCommandResult Outcome( bool done, object? value = null ) =>
		done ? ParkCommandResult.Ok( value ) : ParkCommandResult.Rejected( string.IsNullOrEmpty( LastActionMessage ) ? "Refused." : LastActionMessage );

	private ParkCommandResult ExecutePlaceObject( PlaceObjectCommand place )
	{
		if ( ResolveCommandEntry( place.Entry ) is not { } entry )
			return ParkCommandResult.Rejected( $"No catalogue entry {place.Entry}." );
		if ( place.Moving && (carriedEntries == null || !carriedEntries.TryGetValue( place.Player, out var carried ) || carried != place.Entry) )
			return ParkCommandResult.Rejected( $"Player {place.Player} carries no {entry.DisplayName} to place." );
		OriginalObject? ride = null;
		if ( place.AddOnRide >= 0 && (ride = ResolveCommandObject( place.AddOnRide )) == null )
			return ParkCommandResult.Rejected( $"No ride {place.AddOnRide} for the add-on." );
		commandPlacementMoving = place.Moving;
		try
		{
			// as the build click does (PlaceBuildTarget): a track add-on goes onto its ride's track, anything else is placed as itself
			var placed = ride != null && TrackAddOns.IsTrackAddOn( entry )
				? PlaceAddOn( entry, place.X, place.Y, place.Rotation, ride )
				: PlaceObject( entry, place.X, place.Y, place.Rotation );
			// [BIN:TPW-EXE:0x0052A02E placement selects] the placing player's own selection (0x7C2654) is the new object
			if ( placed != null && place.Player == Commands.Player )
				Selection = placed;
			return Outcome( placed != null, placed );
		}
		finally
		{
			commandPlacementMoving = null;
		}
	}

	private ParkCommandResult ExecutePickUp( PickUpObjectCommand pickUp )
	{
		if ( ResolveCommandObject( pickUp.Attraction ) is not { } item )
			return ParkCommandResult.Rejected( $"No object {pickUp.Attraction}." );
		if ( item.Entry.IsFixedItem || (PlacedRide != null && ReferenceEquals( item, PlacedRide )) || !CanMove( item ) )
			return ParkCommandResult.Rejected( $"{item.Entry.DisplayName} cannot be moved." );
		var entry = item.Entry;
		var rotation = item.Placement.Rotation;
		if ( !RemoveAttraction( item ) )
			return ParkCommandResult.Rejected( $"{entry.DisplayName} cannot be picked up." );
		(carriedEntries ??= new Dictionary<int, string>())[pickUp.Player] = ParkCommandCodec.EntryKey( entry );
		return ParkCommandResult.Ok( (entry, rotation) );
	}

	/// <summary>[EXT:online-coop-commands] Whether <paramref name="player"/>'s Move tool carries a picked-up type in the park (C3b-2).</summary>
	internal bool CarriesEntry( int player ) => carriedEntries?.ContainsKey( player ) == true;

	/// <summary>[EXT:online-coop-commands] The guest seats whose Move tools carry a picked-up type (C3b-2).</summary>
	internal IEnumerable<int> CarryingGuestSeats() => carriedEntries?.Keys.Where( seat => seat > 0 ).OrderBy( seat => seat ) ?? Enumerable.Empty<int>();

	/// <summary>[EXT:online-coop-commands] Releases what guest seat <paramref name="seat"/> holds (<see cref="ReleaseSeatCommand"/>): its carried type. True when it held anything.</summary>
	internal bool ReleaseSeat( int seat )
	{
		if ( seat <= 0 || carriedEntries == null || !carriedEntries.Remove( seat ) )
			return false;
		if ( carriedEntries.Count == 0 )
			carriedEntries = null;
		return true;
	}

	/// <summary>
	/// [EXT:online-coop-commands] A shared park's session starts or ends: the guests' carried types go with it, as their grants do (<see cref="ClearBuildGrants"/>), so a park shared again
	/// starts with nothing in a guest's hand. The host's own Move tool keeps its carry.
	/// </summary>
	internal void ClearGuestCarries()
	{
		foreach ( var seat in CarryingGuestSeats().ToArray() )
			ReleaseSeat( seat );
	}

	private ParkCommandResult ExecuteEndMove( EndMoveCommand end )
	{
		if ( carriedEntries == null || !carriedEntries.Remove( end.Player ) )
			return ParkCommandResult.Rejected( $"Player {end.Player} carries nothing." );
		if ( carriedEntries.Count == 0 )
			carriedEntries = null;
		return ParkCommandResult.Ok();
	}

	private ParkCommandResult ExecuteQueueSegment( QueueSegmentCommand line )
	{
		if ( ResolveCommandRide( line.Ride ) is not { } ride || Guests == null ) return ParkCommandResult.Rejected( "No queue ride." );
		var writer = CreateQueueWriter( ride );
		if ( !writer.SupportsNativeStart( (line.StartX, line.StartY) ) || !writer.SupportsNative( (line.StartX, line.StartY), (line.EndX, line.EndY) ) ) return ParkCommandResult.Rejected( "Unsupported native queue domain." );
		// [EXT:online-coop-lockstep] the checks read the grid alone; the segment goes ahead on the queue as it stands (a rejection leaves the ride as it was)
		ride.RecomputeQueue( Guests.Grid );
		// Even a partial type failure applied subsequent passes and must remain in the command log.
		return ParkCommandResult.Ok( writer.CommitNative( (line.StartX, line.StartY), (line.EndX, line.EndY) ) );
	}

	private ParkCommandResult ExecuteQueueSegmentUndo( UndoQueueSegmentCommand undo )
	{
		if ( ResolveCommandRide( undo.Ride ) is not { } ride || Guests == null ) return ParkCommandResult.Rejected( "No queue ride." );
		var writer = CreateQueueWriter( ride );
		if ( !writer.SupportsNativeUndo( (undo.StartX, undo.StartY), (undo.EndX, undo.EndY) ) ) return ParkCommandResult.Rejected( "Unsupported native queue undo domain." );
		// [EXT:online-coop-lockstep] as the segment: brought up to date only once the undo goes ahead
		ride.RecomputeQueue( Guests.Grid );
		writer.UndoNative( new( (undo.StartX, undo.StartY), (undo.EndX, undo.EndY) ) );
		return ParkCommandResult.Ok();
	}

	private ParkCommandResult ExecuteUndoLine( UndoLineCommand undo )
	{
		if ( CommandLineWriter( undo.Mode, undo.Ride ) is not { } writer )
			return ParkCommandResult.Rejected( $"No {undo.Mode} line can be taken back here." );
		writer.Undo( new SegmentResult( (undo.StartX, undo.StartY), undo.Built, 0, CellBuildResult.Ok, false ) { Start = (undo.StartX, undo.StartY), Existing = undo.Existing } );
		// [BIN:TPW-EXE:0x0052FFEC Back's re-evaluation] the path tool's Back passes no ride (0x40BEB3), so the pop's re-evaluation (mode 1: 0x52FF04) takes the selected ride (0x8186AC), which the issuer's command carries
		if ( undo.Mode == CellToolMode.Path )
			ReevaluateSelectedRide( undo.Ride );
		return ParkCommandResult.Ok();
	}

	/// <summary>
	/// The area clear's rectangle (<see cref="AreaClear.Run"/>) with the level's own removals, in the order tp.exe's clear takes them: an object through <see cref="RemoveObjectAt"/> when the
	/// command's player may remove it ([EXT:online-coop-permissions] <see cref="ParkCommands.AllowsRemoval"/>: a guest's clear leaves a coaster's or a track ride's station and its ride
	/// standing, and still clears the path cells around them), a path cell through the forced clear; a park whose objects do not type their cells follows the object on each cell. A removal
	/// that throws (a rule its checks missed) errs the whole rectangle's command after what it removed, logged as one errored record, on every peer alike.
	/// </summary>
	private ParkCommandResult ExecuteClearArea( ClearAreaCommand clear ) =>
		ParkCommandResult.Ok( AreaClear.Run( Guests?.Grid.Cells, (clear.X0, clear.Y0), (clear.X1, clear.Y1),
			( x, y ) => Objects.FindAt( x, y ) is not { } item || Commands.AllowsRemoval( clear.Player, item ) ? RemoveObjectAt( x, y ) : false,
			( x, y ) => Commands.AllowsPathRemoval( clear.Player, x, y ) ? RemovePathCell( x, y ) : CellBuildResult.Occupied, TypesFootprints ? null : ( x, y ) => Objects.FindAt( x, y ) ) );

	/// <summary>
	/// [EXT:online-coop-commands] What a queue edit's record read (C3b): the queue tool's start before the edit took the link off, as the host's click reads it before its edit, and the
	/// queue's cells with their links after it, by which a guest's tool, entering at the request's completion, checks that no later record changed the queue since.
	/// </summary>
	internal sealed record QueueEditStart( int Ride, QueueToolStart Tool, IReadOnlyList<(int X, int Y, byte Links)> Queue );

	private ParkCommandResult ExecuteEditQueue( EditQueueCommand edit )
	{
		if ( Guests == null || QueueRideAt( Guests.Grid, Guests.Attractions, edit.X, edit.Y ) is not { } ride || ride.QueueFrontCell == null )
			return ParkCommandResult.Rejected( "No queue to edit here." );
		ride.RecomputeQueue( Guests.Grid );
		// [EXT:online-coop-commands] the tool's start as the click reads it before the edit, for a guest's tool that enters once its request comes back (C3b)
		var tool = ReadQueueToolStart( ride, afterEditClick: true )!;
		// [BIN:TPW-EXE:0x00530379 corner walk link clear] the walk the click runs also takes the link between the queue's back cell and the path it ends on off both cells, and a cancel restores nothing (QueuePaths.DetachFromPath)
		// the walk's counter writes come first, as the walk stores them while it goes (QueuePaths.WriteWalkCounts)
		QueuePaths.WriteWalkCounts( Guests.Grid, ride );
		// [BIN:TPW-EXE:0x00526118 queue edit click] 0x4DE1F0 runs after the walk whether or not it cleared a link (DetachFromPath reports its own edit only when it did)
		if ( !QueuePaths.DetachFromPath( Guests.Grid, ride ) )
		{
			ride.RecomputeQueue( Guests.Grid );
			ride.NotifyQueueEdited( Guests.Grid );
		}
		return ParkCommandResult.Ok( new QueueEditStart( ride.AttractionId, tool, QueueLinks( ride ) ) );
	}

	/// <summary>[EXT:online-coop-commands] The cells of <paramref name="ride"/>'s queue, front first, with their link bytes as they stand now.</summary>
	internal IReadOnlyList<(int X, int Y, byte Links)> QueueLinks( RideVisitorBridge ride )
	{
		var grid = Guests!.Grid;
		return ride.QueueView( grid ).QueueCells.Select( cell => (cell.X, cell.Y, grid.Cells.LinksAt( cell.X, cell.Y )) ).ToArray();
	}

	/// <summary>[EXT:online-coop-commands] The HasQueue ride whose attraction id is <paramref name="attraction"/>, or null (a guest's queue edit resolves its ride again at its completion).</summary>
	internal RideVisitorBridge? ResolveQueueRide( int attraction ) => ResolveCommandRide( attraction );

	private ParkCommandResult ExecuteRemovePrototype()
	{
		if ( PlacedRide == null )
			return ParkCommandResult.Rejected( "No prototype ride is placed." );
		RemoveRide();
		return ParkCommandResult.Ok();
	}

	private ParkCommandResult ExecuteSetPrototypeRunning( SetPrototypeRunningCommand running )
	{
		// [EXT:online-coop-lockstep] the developer prototype's run skips the reopening guard: a co-op session refuses it alike on every peer
		if ( Commands.SessionRules )
			return ParkCommandResult.Rejected( "The developer panel is off in a shared park." );
		if ( PlacedRide == null )
			return ParkCommandResult.Rejected( "No prototype ride is placed." );
		if ( running.Running )
			PlacedRide.Start();
		else
			PlacedRide.Stop();
		return ParkCommandResult.Ok();
	}
}
