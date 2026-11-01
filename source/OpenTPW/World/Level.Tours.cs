namespace OpenTPW;

/// <summary>Tour rides (docs/TOURS.md): the flying-car controllers' ground, their park-resume part and the drawing of their cars.</summary>
public partial class Level
{
	/// <summary>Gives the tour module the park's ground: the map size in cells, the terrain height of a cell corner and the height maps of the objects on the cells.</summary>
	private void SetupTours()
	{
		var tours = Objects.ScriptWorld.Tours;
		if ( OriginalPark?.Heightfield is not { } field )
			return;
		// the cars also clear the height map of the object that stands on a cell (TOUR-005)
		tours.ObjectHeight = TourObjectGround;
		tours.MapWidth = field.CellCountX;
		tours.MapHeight = field.CellCountZ;
		tours.CellHeight = ( x, y ) => field.GetCornerHeight( Math.Clamp( x, 0, field.CellCountX ), Math.Clamp( y, 0, field.CellCountZ ) );
	}

	private readonly ObjectHeightMaps tourHeightMaps = new();

	/// <summary>The ground a cell's mesh instance gives the tour cars: the second layer's piece instance first (a track tile), else the object that owns the cell (TOUR-005).</summary>
	// [BIN:TPW-EXE:0x00452AE0 cell ground] the second-layer mesh instance (0x0053BF30) answers before the cell's owner object does
	private float? TourObjectGround( int x, int y )
	{
		if ( TrackPieceGround( x, y ) is { } piece )
			return piece;
		return Objects.FindAt( x, y ) is { } item ? tourHeightMaps.Height( item, x, y ) : null;
	}

	/// <summary>
	/// The raise of a cell's marker in a typed park (BLUEPRINT-001): 10 x ((int)H + 9) / 10 of the cell's ground for a cell that holds an object, a track piece, a post or no terrain (<see cref="BlueprintHeights"/>), 0 for every other cell.
	/// A world whose cells carry no original types has none: an OpenTPW harness world ([EXT:untyped-harness-world]) and a Theme Park Inc layout (BLUEPRINT-001's open residual); a read-only online visit builds nothing, so it shows no marker.
	/// </summary>
	// [EXT:untyped-harness-world] an OpenTPW harness world has no cell type or record to raise a marker by, so its markers stay on the terrain (a Theme Park Inc world is the same code path but not an extension)
	private float? BlueprintHeightOffset( int x, int y )
	{
		if ( !TypesFootprints || Guests?.Grid.Cells is not { } cells || OriginalPark?.Heightfield is not { } field ) return null;
		return BlueprintHeights.Sample( cells, x, y, ( cx, cy ) => BlueprintGround( cells, field, cx, cy ) );
	}

	/// <summary>
	/// The cell lookup <c>0x452AE0(x, y, 0)</c> as the ghost calls it: the second-layer piece instance's height map first, else the map of the object whose footprint owns the cell (a type 4, 9 or 10 cell and the
	/// class-3 thing its owner word names, 0x527E80), and the terrain at the cell corner when neither answers or the instance has no map.
	/// </summary>
	// [BIN:TPW-EXE:0x00452AE0 cell ground] with no out-pointer the lookup asks 0x53BF30 for the record's instance, else the owner thing's instance (+0x20), reads its map (+0xCC) and falls back to the terrain sampler 0x4527F0 at (10 x, 10 y) at 0x452C7F; the fence posts' instances have no mesh in OpenTPW (TOUR-005), so their cells read the terrain
	private float BlueprintGround( ParkCellMap cells, ModelHeightfield field, int x, int y )
	{
		if ( TrackPieceGround( x, y ) is { } piece )
			return piece;
		if ( ParkCellTypes.IsObjectCell( cells.RawTypeAt( x, y ) ) && Objects.FindAt( x, y ) is { IsDeleted: false } item && cells.OwnerAt( x, y ) == OwnerWord( item ) && ObjectGround( item, x, y ) is { } ground )
			return ground;
		return field.GetCornerHeight( x, y );
	}

	/// <summary>The ground of the second-layer piece instance on a cell (a kart or water track tile), which the cell lookup 0x00452AE0 asks before the cell's owner; null without one.</summary>
	private float? TrackPieceGround( int x, int y )
	{
		if ( waterTracks?.Tracks is not { } layer )
			return null;
		foreach ( var family in TrackFamilies )
		{
			if ( family != TrackFamily.Bumper && TourGroundOf( family ) is { } renderer && renderer.PieceGround( layer, x, y ) is { } piece )
				return piece;
		}
		return null;
	}

	/// <summary>The height the cell lookup 0x00452AE0 gives the object that owns a cell, in model units, or null when it names none (the object has no height map).</summary>
	private float? ObjectGround( OriginalObject item, int x, int y ) => tourHeightMaps.Height( item, x, y );

	/// <summary>
	/// The piece lookup of a track family for the tour cars' ground. It is the simulation's own (never the drawing's renderer, which exists only once a frame has drawn the tracks), so the cars fly the same
	/// with or without rendering; built from the family's first station when one exists, and never drawn or updated.
	/// </summary>
	private TrackRenderer? TourGroundOf( TrackFamily family )
	{
		if ( tourTrackGround.TryGetValue( family, out var known ) )
			return known;
		var station = trackStations.Keys.FirstOrDefault( item => !item.IsDeleted && item.Runtime.Bump?.Template.Family == family );
		if ( station == null )
			return null;
		TrackRenderer? created = null;
		try
		{
			created = new TrackRenderer( station.Entry, TrackPieceKit.Load( station.Entry.FileSystem, station.Entry.ArchivePath, family ), () => Objects.Grid.Origin, ( x, y ) => Objects.Grid.GetCellHeight( x, y ), ( x, y ) => Objects.Grid.GetCornerHeight( x, y ), ( x, y ) => Objects.Grid.GetTerrainHeight( x, y ), ( x, y ) => Objects.Grid.GetDrapeHeight( x, y ) );
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or IOException or NotSupportedException )
		{
			Log.Warning( $"The tour ground of the {family} tracks reads the objects only: {exception.Message}" );
		}
		tourTrackGround[family] = created;
		return created;
	}

	private readonly Dictionary<TrackFamily, TrackRenderer?> tourTrackGround = new();
	private TourRenderer? tourRenderer;
	private bool tourRenderFailed;

	private ObjectCatalogEntry? TourEntryOf( TourController controller ) => Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.Runtime.AttractionId == controller.AttractionId )?.Entry;

	private Vector2 TourGridOrigin() => Objects.Grid.Origin;

	/// <summary>The tour renderer, or null before the first tour car is drawn (the native smoke test reads it back).</summary>
	internal TourRenderer? TourRenderer => tourRenderer;

	/// <summary>Car instances the tour renderer holds (0 before the first car is drawn).</summary>
	public int TourCarInstances => tourRenderer?.CarCount ?? 0;

	/// <summary>Moves the tour renderer's car instances to the controllers' cars and plays their clips for the step's <paramref name="seconds"/> (once per simulation step).</summary>
	private void UpdateTourRendering( double seconds = 0 )
	{
		if ( tourRenderFailed )
			return;
		var tours = Objects.ScriptWorld.Tours;
		if ( tours.Controllers.Count == 0 && tourRenderer == null )
			return;
		try
		{
			tourRenderer ??= new TourRenderer( TourEntryOf, TourGridOrigin );
			tourRenderer.Update( tours.Controllers, seconds );
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or IOException or NotSupportedException )
		{
			tourRenderFailed = true;
			Log.Warning( $"The tour renderer was disabled: {exception.Message}" );
		}
	}

	/// <summary>The placed rides that carry a live tour controller: the one rule the capture and the restore share.</summary>
	private IEnumerable<OriginalObject> LiveTourRides() => Objects.Objects.Where( item => !item.IsDeleted && item.Runtime.LiveTour != null );

	/// <summary>The park-resume tour part, or null when no ride holds a tour controller. Call between simulation ticks.</summary>
	internal TourWorldSaveData? CaptureToursSnapshot()
	{
		var controllers = LiveTourRides().OrderBy( item => item.Runtime.AttractionId ).Select( item => TourSave.Capture( item.Runtime.LiveTour! ) ).ToList();
		return controllers.Count == 0 ? null : new TourWorldSaveData { Controllers = controllers };
	}

	/// <summary>
	/// Restores the saved tour controllers into this candidate level, after the objects were placed and their runtimes restored: each saved controller goes back on its
	/// ride. A live tour ride the save holds no controller for (a save from before the tour controllers, or a ride whose script had not created it yet) gets a fresh one.
	/// </summary>
	// [EXT:legacy-tour-save] a park-resume save from before the tour controllers restores each live tour ride with a fresh controller (one car at the station, ready), as if its script had just run TOUR 1; nobody was aboard in such a save. The original's save always carries the FLYR block, so this is OpenTPW's save-compatibility decision
	internal void RestoreToursSnapshot( TourWorldSaveData? saved )
	{
		var rides = Objects.Objects.Where( item => !item.IsDeleted && item.Runtime.UsesTour && !item.Runtime.IsStopped ).ToList();
		var restored = new HashSet<int>();
		foreach ( var entry in saved?.Controllers ?? new List<TourControllerSaveData>() )
		{
			var ride = rides.FirstOrDefault( item => item.Runtime.AttractionId == entry.AttractionId )
				?? throw new InvalidDataException( $"The save holds a tour controller for attraction {entry.AttractionId}, which this park does not place as a live tour ride." );
			ride.Runtime.RestoreTour( entry );
			restored.Add( entry.AttractionId );
		}
		// [EXT:online-coop-snapshot-join] a join's tour rides are the host's live ones: one between its script's TOUR 2 and TOUR 1 has no controller, and gets none here
		if ( joinRestore != null )
			return;
		foreach ( var ride in rides )
		{
			if ( restored.Contains( ride.Runtime.AttractionId ) )
				continue;
			// riders the script holds with no car to carry them (a save from before the controllers cannot have any, but a save is not trusted to) are handed out at the station through TOUR 4
			var tour = ride.Runtime.CreateTour();
			if ( tour != null )
				tour.Ejected.AddRange( ride.Visitors.Riders.Where( guest => !tour.Ejected.Contains( guest ) ) );
		}
	}
}
