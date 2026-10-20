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
	/// <summary>The catalogue entry each player picked up with the Move tool (<see cref="PickUpObjectCommand"/>): a moving placement must name it (the Move tool's purchase skips research and the balance test).</summary>
	private Dictionary<int, string>? carriedEntries;
	/// <summary>While a <see cref="PlaceObjectCommand"/> runs: whether it is the Move tool's placement (<see cref="PlaceObject(ObjectCatalogEntry, int, int, int)"/> reads it instead of <see cref="IsMoving"/>).</summary>
	private bool? commandPlacementMoving;

	/// <summary>The park's edit funnel; every edit of a HUD window or tool goes through it.</summary>
	public ParkCommands Commands => commands ??= new ParkCommands( () => simulationClock?.TickCount ?? 0, ExecuteCommand, () => IsReadOnlyVisit );

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

	/// <summary>The line writer a command's line is laid and taken back through: the path builder, or the queue writer of the ride.</summary>
	private ICellLineWriter? CommandLineWriter( CellToolMode mode, int ride ) => mode switch
	{
		CellToolMode.Path => Paths,
		CellToolMode.Queue when ResolveCommandRide( ride ) is { } bridge && Guests != null => CreateQueueWriter( bridge ),
		_ => null
	};

	/// <summary>Runs one well-formed command on the park (<see cref="ParkCommands"/> has checked the visit and the player's rights).</summary>
	private ParkCommandResult ExecuteCommand( ParkCommand command ) => command switch
	{
		PlaceObjectCommand place => ExecutePlaceObject( place ),
		RemoveAtCommand remove => Outcome( RemoveAt( remove.X, remove.Y ) ),
		RemoveObjectAtCommand remove => Outcome( RemoveObjectAt( remove.X, remove.Y ) ),
		RemoveAttractionCommand remove => ResolveCommandObject( remove.Attraction ) is { } item
			? Outcome( RemoveAttraction( item ), item )
			: ParkCommandResult.Rejected( $"No attraction {remove.Attraction}." ),
		PickUpObjectCommand pickUp => ExecutePickUp( pickUp ),
		LayLineCommand line => CommandLineWriter( line.Mode, line.Ride ) is { } writer
			? ParkCommandResult.Ok( writer is QueueLineWriter legacyQueue ? legacyQueue.CommitLegacy( (line.StartX, line.StartY), (line.EndX, line.EndY) ) : writer.Commit( (line.StartX, line.StartY), (line.EndX, line.EndY) ) )
			: ParkCommandResult.Rejected( $"No {line.Mode} line can be laid here." ),
		QueueSegmentCommand line => ExecuteQueueSegment( line ),
		UndoQueueSegmentCommand undo => ExecuteQueueSegmentUndo( undo ),
		UndoLineCommand undo => ExecuteUndoLine( undo ),
		RemovePathCellCommand remove => RemovePathCell( remove.X, remove.Y ) is var result && result == CellBuildResult.Ok ? ParkCommandResult.Ok( result ) : new ParkCommandResult( false, $"No path cell removed: {result}.", result ),
		RemoveQueueCellCommand remove => RemoveQueueCell( remove.X, remove.Y ) is var count and > 0 ? ParkCommandResult.Ok( count ) : ParkCommandResult.Rejected( "No queue cell here." ),
		BacktrackCommand backtrack => Outcome( BacktrackAt( backtrack.X, backtrack.Y ) ),
		EditQueueCommand edit => ExecuteEditQueue( edit ),
		PlacePrototypeCommand prototype => Outcome( PlaceRide( new Vector3( prototype.X, prototype.Y, prototype.Z ) ) ),
		RemovePrototypeCommand => ExecuteRemovePrototype(),
		DismissFinaleCommand => DismissResearchFinale() ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "No research finale to dismiss." ),
		SetPrototypeRunningCommand running => ExecuteSetPrototypeRunning( running ),
		ParkSettingCommand setting => ParkSettingCommand.Run( this, setting ),
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

	private ParkCommandResult ExecuteQueueSegment( QueueSegmentCommand line )
	{
		if ( ResolveCommandRide( line.Ride ) is not { } ride || Guests == null ) return ParkCommandResult.Rejected( "No queue ride." );
		var writer = CreateQueueWriter( ride );
		if ( !writer.SupportsNativeStart( (line.StartX, line.StartY) ) || !writer.SupportsNative( (line.StartX, line.StartY), (line.EndX, line.EndY) ) ) return ParkCommandResult.Rejected( "Unsupported native queue domain." );
		// Even a partial type failure applied subsequent passes and must remain in the command log.
		return ParkCommandResult.Ok( writer.CommitNative( (line.StartX, line.StartY), (line.EndX, line.EndY) ) );
	}

	private ParkCommandResult ExecuteQueueSegmentUndo( UndoQueueSegmentCommand undo )
	{
		if ( ResolveCommandRide( undo.Ride ) is not { } ride || Guests == null ) return ParkCommandResult.Rejected( "No queue ride." );
		var writer = CreateQueueWriter( ride );
		if ( !writer.SupportsNativeUndo( (undo.StartX, undo.StartY), (undo.EndX, undo.EndY) ) ) return ParkCommandResult.Rejected( "Unsupported native queue undo domain." );
		writer.UndoNative( new( (undo.StartX, undo.StartY), (undo.EndX, undo.EndY) ) );
		return ParkCommandResult.Ok();
	}

	private ParkCommandResult ExecuteUndoLine( UndoLineCommand undo )
	{
		if ( CommandLineWriter( undo.Mode, undo.Ride ) is not { } writer )
			return ParkCommandResult.Rejected( $"No {undo.Mode} line can be taken back here." );
		writer.Undo( new SegmentResult( (undo.StartX, undo.StartY), undo.Built, 0, CellBuildResult.Ok, false ) { Start = (undo.StartX, undo.StartY), Existing = undo.Existing } );
		return ParkCommandResult.Ok();
	}

	private ParkCommandResult ExecuteEditQueue( EditQueueCommand edit )
	{
		if ( Guests == null || QueueRideAt( Guests.Grid, Guests.Attractions, edit.X, edit.Y ) is not { } ride || ride.QueueFrontCell == null )
			return ParkCommandResult.Rejected( "No queue to edit here." );
		ride.RecomputeQueue( Guests.Grid );
		// [BIN:TPW-EXE:0x00530379 corner walk link clear] the walk the click runs also takes the link between the queue's back cell and the path it ends on off both cells, and a cancel restores nothing (QueuePaths.DetachFromPath)
		QueuePaths.DetachFromPath( Guests.Grid, ride );
		return ParkCommandResult.Ok( ride );
	}

	private ParkCommandResult ExecuteRemovePrototype()
	{
		if ( PlacedRide == null )
			return ParkCommandResult.Rejected( "No prototype ride is placed." );
		RemoveRide();
		return ParkCommandResult.Ok();
	}

	private ParkCommandResult ExecuteSetPrototypeRunning( SetPrototypeRunningCommand running )
	{
		if ( PlacedRide == null )
			return ParkCommandResult.Rejected( "No prototype ride is placed." );
		if ( running.Running )
			PlacedRide.Start();
		else
			PlacedRide.Stop();
		return ParkCommandResult.Ok();
	}
}
