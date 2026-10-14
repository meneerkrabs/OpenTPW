namespace OpenTPW;

/// <summary>Tour rides (docs/TOURS.md): the flying-car controllers' ground, their park-resume part and the drawing of their cars.</summary>
public partial class Level
{
	/// <summary>Gives the tour module the park's terrain: the map size in cells and the ground height of a cell corner.</summary>
	private void SetupTours()
	{
		var tours = Objects.ScriptWorld.Tours;
		if ( OriginalPark?.Heightfield is not { } field )
			return;
		tours.MapWidth = field.CellCountX;
		tours.MapHeight = field.CellCountZ;
		tours.CellHeight = ( x, y ) => field.GetCornerHeight( Math.Clamp( x, 0, field.CellCountX ), Math.Clamp( y, 0, field.CellCountZ ) );
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
	// [APPROX:TOUR-006] a park-resume save from before the tour controllers restores each live tour ride with a fresh controller (one car at the station, ready), as if its script had just run TOUR 1; nobody was aboard in such a save
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
		foreach ( var ride in rides.Where( item => !restored.Contains( item.Runtime.AttractionId ) ) )
			ride.Runtime.CreateTour();
	}
}
