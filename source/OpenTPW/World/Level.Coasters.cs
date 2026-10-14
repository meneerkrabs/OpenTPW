namespace OpenTPW;

/// <summary>Player-built roller coasters (docs/COASTERS.md): the coaster tool, the builders of the level's stations and the track renderer.</summary>
public partial class Level
{
	private readonly Dictionary<OriginalObject, CoasterBuilder> coasterBuilders = new();
	private CoasterRenderer? coasterRenderer;

	/// <summary>Every coaster controller of the park.</summary>
	public IReadOnlyList<CoasterRide> Coasters => Objects.ScriptWorld.Coasters.Rides;
	/// <summary>The station whose track the coaster tool edits, or null when the tool is off.</summary>
	public OriginalObject? CoasterToolStation { get; private set; }
	/// <summary>The builder the coaster tool drives, or null.</summary>
	public CoasterBuilder? CoasterTool => CoasterToolStation is { } station ? GetCoasterBuilder( station ) : null;
	public bool IsCoasterToolActive => CoasterToolStation != null;
	/// <summary>Triangles the coaster renderer drew in the last frame.</summary>
	public int CoasterTrianglesDrawn => coasterRenderer?.DrawnTriangles ?? 0;

	private void SetupCoasters()
	{
		Objects.ObjectPlaced += OnCoasterStationPlaced;
		Objects.ObjectRemoved += OnCoasterStationRemoved;
		foreach ( var item in Objects.Objects )
			OnCoasterStationPlaced( item );
		coasterSite = CoasterSite.FromGrid( Objects.Grid );
		coasterRenderer = new CoasterRenderer( () => Coasters, () => Objects.Grid.Origin, ObjectPlacement.ModelScale, CoasterEntry,
			ride => CoasterToolStation?.Runtime.Coaster == ride ? DemoPreview ?? CoasterPreview : null );
	}

	/// <summary>The build cursor the coaster tool shows: the track is drawn through a ghost pylon there. Set by the HUD each frame, null when the tool is off.</summary>
	public CoasterPreview? CoasterPreview { get; set; }

	/// <summary>A build cursor the debug demo holds (it wins over the HUD's cursor, so a capture does not depend on the mouse).</summary>
	internal CoasterPreview? DemoPreview { get; set; }

	/// <summary>The pylon cells of the demo circuit, as offsets from the station's '&gt;' cell (the circuit of the native smoke test, which passes every window).</summary>
	private static readonly (int X, int Y)[] DemoCircuit = { (4, 0), (7, 4), (4, 8), (-4, 8), (-8, 4), (-7, 0) };

	/// <summary>
	/// Places a coaster station with a small circuit near the park's paths and points the park camera at it (the debug flag <c>--debug-coaster</c>, for captures):
	/// <paramref name="finished"/> closes and finishes the track, otherwise it stays open after the fourth pylon with the build cursor on the fifth cell.
	/// <paramref name="infoId"/> picks the coaster (its <c>Info.Id</c>), the first buildable one by default. Null when no site has room.
	/// </summary>
	// [EXT:debug-coaster] developer/screenshot helper, not original behaviour: it builds the track for the player without the tool and without charging money
	internal OriginalObject? BuildDemoCoaster( bool finished, int? infoId, int height = 40 )
	{
		if ( IsReadOnlyVisit || Guests == null || OriginalPark == null )
			return null;
		var entries = Objects.Catalog.Buildable.Where( item => CoasterDefinition.IsCoaster( item.Settings ) ).OrderBy( item => item.InfoId ).ToArray();
		var entry = infoId is int id ? entries.FirstOrDefault( item => item.InfoId == id ) : entries.FirstOrDefault();
		if ( entry == null )
			return null;
		var grid = Objects.Grid;
		bool Free( int x, int y ) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height && Objects.FindAt( x, y ) == null
			&& grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed && Guests.Grid.Cells.RawTypeAt( x, y ) == 0;
		var near = (X: Guests.Grid.CountX / 2, Y: Guests.Grid.CountY / 2);
		var path = Enumerable.Range( 0, Guests.Grid.CountX * Guests.Grid.CountY ).Select( index => (X: index % Guests.Grid.CountX, Y: index / Guests.Grid.CountX) ).Where( cell => Guests.Grid.IsWalkable( cell.X, cell.Y ) ).ToArray();
		if ( path.Length > 0 )
			near = (path.Sum( cell => cell.X ) / path.Length, path.Sum( cell => cell.Y ) / path.Length);
		foreach ( var cell in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - near.X ) + Math.Abs( cell.Y - near.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			if ( Objects.Check( entry, cell.X, cell.Y, 0 ) != OriginalPlacementResult.Allowed || CoasterBuilder.StationEnds( entry.Shape, cell.X, cell.Y, 0 ) is not { } ends )
				continue;
			var cells = DemoCircuit.Select( offset => (X: ends.Depart.X + offset.X, Y: ends.Depart.Y + offset.Y) ).Append( ends.Return ).ToArray();
			if ( !cells.All( target => Free( target.X, target.Y ) ) )
				continue;
			var station = Objects.TryPlace( entry, cell.X, cell.Y, 0, out _ );
			if ( station?.Runtime.Coaster == null || GetCoasterBuilder( station ) is not { } builder )
				return null;
			builder.Height = height;
			var count = finished ? DemoCircuit.Length : 4;
			for ( var index = 0; index < count; index++ )
				builder.Place( ends.Depart.X + DemoCircuit[index].X, ends.Depart.Y + DemoCircuit[index].Y );
			if ( finished )
			{
				builder.Place( ends.Return.X, ends.Return.Y );
				builder.Complete();
			}
			else
			{
				EnterCoasterTool( station );
				DemoPreview = new CoasterPreview( ends.Depart.X + DemoCircuit[4].X, ends.Depart.Y + DemoCircuit[4].Y, true, height, false );
			}
			var centre = OriginalParkPlacement.GetCellCenter( OriginalPark.Heightfield, ends.Depart.X - 2, ends.Depart.Y + 4 );
			(Camera.Mode as ParkCameraMode)?.SetPose( new Vector3( centre.X, centre.Y, 0 ), 28, -20 );
			Log.Trace( $"--debug-coaster: {entry.DisplayName} at ({cell.X}, {cell.Y}), {(finished ? "finished" : "open")} with {builder.Track.PylonCount} pylons; camera on engine ({centre.X:0.#}, {centre.Y:0.#})." );
			return station;
		}
		return null;
	}

	/// <summary>The catalogue entry (archive) of a coaster's station object, or null when the station left.</summary>
	private ObjectCatalogEntry? CoasterEntry( CoasterRide ride )
	{
		foreach ( var item in Objects.Objects )
		{
			if ( !item.IsDeleted && ReferenceEquals( item.Runtime.Coaster, ride ) )
				return item.Entry;
		}
		return null;
	}

	private void OnCoasterStationPlaced( OriginalObject item )
	{
		if ( item.Runtime.Coaster == null || GetCoasterBuilder( item ) is not { } builder )
			return;
		if ( CoasterBuilder.StationEnds( item.Entry.Shape, item.Placement.X, item.Placement.Y, item.Placement.Rotation ) is { } ends )
			builder.SetStation( ends.Depart, ends.Return );
	}

	private void OnCoasterStationRemoved( OriginalObject item )
	{
		if ( CoasterToolStation == item )
			CoasterToolStation = null;
		if ( coasterBuilders.Remove( item, out var builder ) )
			builder.Delete();
	}

	/// <summary>The headless build API of a coaster station of this level (null for other objects).</summary>
	public CoasterBuilder? GetCoasterBuilder( OriginalObject station )
	{
		if ( station.Runtime.Coaster is not { } ride || station.IsDeleted )
			return null;
		if ( !coasterBuilders.TryGetValue( station, out var builder ) )
		{
			builder = new CoasterBuilder( ride, CoasterSite, () => Park?.Economy, () => GetEconomyInstance( station ) );
			coasterBuilders[station] = builder;
		}
		return builder;
	}

	/// <summary>The level's cells for coaster tracks: grid bounds, the no-ground cells as they were when the park started, ground heights in original units.</summary>
	public CoasterSite CoasterSite => coasterSite ??= CoasterSite.FromGrid( Objects.Grid );
	private CoasterSite? coasterSite;

	/// <summary>Starts the coaster tool on a station: park clicks place pylons, a click on a station end closes the circuit, the next one completes the track.</summary>
	public bool EnterCoasterTool( OriginalObject station )
	{
		if ( IsReadOnlyVisit || GetCoasterBuilder( station ) is not { } builder )
			return false;
		if ( builder.Track.IsComplete && !builder.Reopen() )
		{
			LastActionMessage = "Cannot edit the coaster while riders are on board.";
			return false;
		}
		StaffPlacement?.Cancel();
		CellTool.Cancel();
		CoasterToolStation = station;
		return true;
	}

	/// <summary>Ends the coaster tool (the track keeps its pylons; an incomplete track takes no riders).</summary>
	public void ExitCoasterTool()
	{
		CoasterToolStation = null;
		CoasterPreview = null;
	}

	/// <summary>
	/// A park click in the coaster tool: on the station's return end it completes the track, otherwise it places a
	/// pylon at the tool's height.
	/// </summary>
	public CoasterPlaceResult CoasterClick( int x, int y )
	{
		if ( CoasterTool is not { } builder )
			return CoasterPlaceResult.NotEditable;
		var track = builder.Track;
		if ( track.IsChainStart( x, y ) && track.IsClosed )
			return CompleteCoaster();
		var closing = track.IsChainStart( x, y );
		var count = track.Nodes.Count;
		var result = builder.Place( x, y );
		LastActionMessage = closing && track.IsClosed ? "Closed the circuit; click the station end again to complete the coaster."
			: track.Nodes.Count == count ? $"Cannot build a pylon here: {result}."
			: result == CoasterPlaceResult.Ok ? $"Placed a pylon (track cost so far ${builder.BuildCost})."
			: $"Placed a pylon marked invalid: {result}.";
		return result;
	}

	/// <summary>Completes the edited (closed) track: rates it, starts its trains and ends the tool.</summary>
	public CoasterPlaceResult CompleteCoaster()
	{
		if ( CoasterTool is not { } builder )
			return CoasterPlaceResult.NotEditable;
		var result = builder.Complete();
		if ( result != CoasterPlaceResult.Ok )
		{
			LastActionMessage = $"Cannot complete the track: {result}.";
			return result;
		}
		var rating = builder.Ride.Rating!.Value;
		LastActionMessage = $"Coaster complete (track ${builder.Ride.PaidCost}): excitement {rating.Excitement:F0}, sickness {rating.Sickness:F0}, {rating.Trains} train(s).";
		var station = CoasterToolStation!;
		CoasterToolStation = null;
		if ( station.Entry.HasQueue && station.Runtime.IsAttraction && station.Visitors.HasCells )
			EnterQueueTool( station.Visitors );
		return result;
	}

	/// <summary>Removes the track of a station (no refund); the station stays.</summary>
	public bool DeleteCoasterTrack( OriginalObject station )
	{
		if ( IsReadOnlyVisit || GetCoasterBuilder( station ) is not { } builder )
			return false;
		builder.Delete();
		LastActionMessage = "Removed the coaster track.";
		return true;
	}

	// ---- Park-resume snapshot -----------------------------------------------------------------------

	/// <summary>
	/// The park-resume coaster part: every live coaster station's controller joined to its attraction id, or null when
	/// the park holds none (such a save carries no coaster part). Call between simulation ticks.
	/// </summary>
	internal CoasterWorldSaveData? CaptureCoastersSnapshot() => CoasterSave.Capture( Objects.ScriptWorld.Coasters, Objects );

	/// <summary>
	/// Restores the saved coasters of a resume snapshot into this candidate level, after the objects were placed (each
	/// station's controller exists, reset to its station node) and their runtimes restored: one entry per live station,
	/// matched by attraction id, in saved (creation) order. A stopped station holds no entry — its controller was torn
	/// down at capture and the runtime restore already replayed that. Throws <see cref="InvalidDataException"/> and
	/// leaves the candidate unrestored on any mismatch.
	/// </summary>
	internal void RestoreCoastersSnapshot( CoasterWorldSaveData? saved )
	{
		if ( saved == null )
			return;
		var live = Coasters.ToHashSet();
		foreach ( var entry in saved.Rides )
		{
			var station = Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.Runtime.AttractionId == entry.AttractionId );
			if ( station?.Runtime.Coaster is not { } ride )
				throw new InvalidDataException( $"The save holds a coaster for attraction {entry.AttractionId}, which this park does not place as a coaster station." );
			if ( !live.Contains( ride ) )
				throw new InvalidDataException( $"Attraction {entry.AttractionId}'s coaster controller is not live; a stopped station holds no coaster state." );
			ride.RestoreSnapshot( entry );
		}
		if ( Coasters.Count != saved.Rides.Count )
			throw new InvalidDataException( $"The save holds {saved.Rides.Count} coasters, but this park places {Coasters.Count} live coaster stations." );
	}
}
