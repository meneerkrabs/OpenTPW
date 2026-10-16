namespace OpenTPW;

/// <summary>
/// The opt-in tour-ride variant of the park-resume fixture (<c>OPENTPW_SMOKE_TOUR=1</c> in both phases, docs/TOURS.md): the Jungle's Jurassic Tours is researched,
/// bought and placed near the path spine, and the populate stage runs until a car is in flight. The save holds the controller, its cars and their riders; the restore
/// phase checks the same cars came back and continues the same ticks, whose canonical hashes (the tour module included) must equal the capture phase's.
/// </summary>
internal sealed partial class ParkResumeSmokeTest
{
	/// <summary>Both phases must agree: <c>OPENTPW_SMOKE_TOUR=1</c> adds a bought tour ride with a car in flight.</summary>
	internal static bool TourVariant => Environment.GetEnvironmentVariable( "OPENTPW_SMOKE_TOUR" ) is "1" or "true";

	/// <summary>The tour ride's cars, flying cars and riders as the save boundary held them (capture phase; the restore phase compares).</summary>
	private SmokeTour savedTour = new();

	private SmokeTour TourCounts()
	{
		var tour = level.Objects.Objects.Where( item => !item.IsDeleted ).Select( item => item.Runtime.LiveTour ).FirstOrDefault( controller => controller != null );
		if ( tour == null )
			return new SmokeTour();
		return new SmokeTour { Cars = tour.CarCount, Flying = tour.Cars.Count( car => car.State == TourCar.StateTour ), Riders = tour.Riders, Departures = tour.Departures };
	}

	/// <summary>The variant's populate condition: a car is on its tour (always true without the variant).</summary>
	private bool TourReady() => !TourVariant || TourCounts().Flying > 0;

	private void PlaceTourFixture( (int X, int Y) near )
	{
		var economy = level.Park!.Economy;
		var entry = level.Objects.Catalog.Buildable.Where( candidate => candidate.Settings.GetInt( "Bumper.WhichTrackType" ) == 0 && candidate.ScriptPath != null
			&& candidate.ScriptPath.Contains( "tour", StringComparison.OrdinalIgnoreCase ) ).OrderBy( candidate => candidate.InfoId ).FirstOrDefault()
			?? level.Objects.Catalog.Entries.Where( candidate => candidate.ScriptPath != null && candidate.ScriptPath.Contains( "tour", StringComparison.OrdinalIgnoreCase ) ).OrderBy( candidate => candidate.InfoId ).FirstOrDefault();
		Require( entry != null, "the theme catalogue has a tour ride" );
		var research = economy.Research;
		research.Restore( research.Completed.Append( (entry!.InfoId, 0) ).Distinct().ToList(),
			research.Items.Select( item => ((item.InfoId, item.Level), research.GetProgress( item )) ).Where( pair => pair.Item2 != 0 ).ToList(), research.Effort );
		// the tour ride is paid for with a golden ticket: the park holds one, as a park that earned it does
		var objectives = economy.Objectives;
		objectives.Restore( objectives.NextOfferDay, objectives.NextListPosition, objectives.Current, objectives.Declines, objectives.Finished, objectives.GoldenTickets.Append( GoldenTicketKind.Visitors ).ToList() );
		Require( economy.CheckBuild( entry.InfoId ) == ParkEconomy.PurchaseResult.Ok, $"the researched tour ride can be bought ({entry.InfoId}: {economy.CheckBuild( entry.InfoId )}, balance {economy.Balance})" );
		var grid = level.Objects.Grid;
		bool Free( int x, int y ) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height && level.Objects.FindAt( x, y ) == null
			&& grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed && level.Guests!.Grid.Cells.RawTypeAt( x, y ) == 0 && !level.Guests!.Grid.Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );
		(int X, int Y)? centre = null;
		foreach ( var cell in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - near.X ) + Math.Abs( cell.Y - near.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			var anchor = Level.GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			if ( level.Objects.CheckFree( entry, anchor.X, anchor.Y, 0 ) != OriginalPlacementResult.Allowed )
				continue;
			if ( Enumerable.Range( anchor.X - 1, 7 ).All( x => Enumerable.Range( anchor.Y - 1, 7 ).All( y => Free( x, y ) ) ) )
			{
				centre = cell;
				break;
			}
		}
		Require( centre != null, "find a free site for a tour ride near the spine" );
		var ride = level.PlaceObject( entry, centre!.Value.X, centre.Value.Y, 0 );
		Require( ride != null && ride.Runtime.UsesTour, "buy and place the tour ride" );
		// a built ride with a queue starts closed (RIDES-015, 0x4DB712): the fixture opens it as the player would
		ride!.Open();
		Log.Trace( $"Park-resume fixture: tour ride {entry.DisplayName} #{ride!.Visitors.AttractionId} at {centre.Value}." );
	}

	/// <summary>The restore phase's proof that the saved tour came back: the same cars in the same states with the same riders.</summary>
	private void VerifyTourRestored()
	{
		if ( !TourVariant )
			return;
		var counts = savedTour = TourCounts();
		Require( counts == capture!.TourAtSave, $"the resumed park holds the saved tour cars and riders ({counts} vs {capture.TourAtSave})" );
		Require( capture.TourAtSave.Cars > 0 && capture.TourAtSave.Flying > 0, "the captured park had a car in flight" );
		Log.Trace( $"Park-resume restore: the tour ride came back with {counts.Cars} car(s), {counts.Flying} in flight and {counts.Riders} rider(s)." );
	}
}
