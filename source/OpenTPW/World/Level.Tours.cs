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
		tours.ObjectHeight = ( x, y ) => Objects.FindAt( x, y ) is { } item ? tourHeightMaps.Height( item, x, y ) : null;
		tours.MapWidth = field.CellCountX;
		tours.MapHeight = field.CellCountZ;
		tours.CellHeight = ( x, y ) => field.GetCornerHeight( Math.Clamp( x, 0, field.CellCountX ), Math.Clamp( y, 0, field.CellCountZ ) );
	}

	private readonly ObjectHeightMaps tourHeightMaps = new();
	private TourRenderer? tourRenderer;
	private bool tourRenderFailed;

	private ObjectCatalogEntry? TourEntryOf( TourController controller ) => Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.Runtime.AttractionId == controller.AttractionId )?.Entry;

	private Vector2 TourGridOrigin() => Objects.Grid.Origin;

	/// <summary>The tour renderer, or null before the first tour car is drawn (the native smoke test reads it back).</summary>
	internal TourRenderer? TourRenderer => tourRenderer;

	/// <summary>Car instances the tour renderer holds (0 before the first car is drawn).</summary>
	public int TourCarInstances => tourRenderer?.CarCount ?? 0;

	/// <summary>Moves the tour renderer's car instances to the controllers' cars (once per simulation step).</summary>
	private void UpdateTourRendering()
	{
		if ( tourRenderFailed )
			return;
		var tours = Objects.ScriptWorld.Tours;
		if ( tours.Controllers.Count == 0 && tourRenderer == null )
			return;
		try
		{
			tourRenderer ??= new TourRenderer( TourEntryOf, TourGridOrigin );
			tourRenderer.Update( tours.Controllers );
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
