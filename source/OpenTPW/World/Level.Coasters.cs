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
		coasterRenderer = new CoasterRenderer( () => Coasters, () => Objects.Grid.Origin, ObjectPlacement.ModelScale );
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
	public void ExitCoasterTool() => CoasterToolStation = null;

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
