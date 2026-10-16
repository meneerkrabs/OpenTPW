namespace OpenTPW;

/// <summary>
/// The opt-in bumper-ride variant of the park-resume fixture (<c>OPENTPW_SMOKE_BUMPER=1</c> in both phases, docs/TRACKS.md "Bumper cars"): the theme's bumper ride is
/// researched, bought and placed near the path spine, and the populate stage runs until a session is on with real guests in the cars. The save holds the controller with
/// its cars; the restore phase checks they came back and continues the same ticks, whose canonical hashes (the controller included) must equal the capture phase's.
/// </summary>
internal sealed partial class ParkResumeSmokeTest
{
	/// <summary>Both phases must agree: <c>OPENTPW_SMOKE_BUMPER=1</c> adds a bought bumper ride whose session is running at the save.</summary>
	internal static bool BumperVariant => Environment.GetEnvironmentVariable( "OPENTPW_SMOKE_BUMPER" ) is "1" or "true";

	private OriginalObject? BumperRide() => level.Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.Runtime.Bump is { Template.IsBumper: true } );

	/// <summary>The variant's populate condition: guests ride the cars of a running session (always true without the variant).</summary>
	private bool BumperReady() => !BumperVariant || BumperRide()?.Runtime.Bump is { Phase: BumpController.PhaseRunning, RiderCount: > 0 };

	private void PlaceBumperFixture( (int X, int Y) near )
	{
		var economy = level.Park!.Economy;
		var entry = level.Objects.Catalog.Buildable.Where( candidate => BumperTemplates.ForRide( candidate.Settings.GetInt( "Bumper.WhichTrackType" ), candidate.Settings.GetInt( "Bumper.BumperType" ) ) is { IsBumper: true } )
			.OrderBy( candidate => candidate.InfoId ).FirstOrDefault();
		Require( entry != null, "the theme catalogue has a bumper ride" );
		var research = economy.Research;
		research.Restore( research.Completed.Append( (entry!.InfoId, 0) ).Distinct().ToList(),
			research.Items.Select( item => ((item.InfoId, item.Level), research.GetProgress( item )) ).Where( pair => pair.Item2 != 0 ).ToList(), research.Effort );
		Require( economy.CheckBuild( entry.InfoId ) == ParkEconomy.PurchaseResult.Ok, "the researched bumper ride can be bought" );
		var grid = level.Objects.Grid;
		bool Free( int x, int y ) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height && level.Objects.FindAt( x, y ) == null
			&& grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed && level.Guests!.Grid.Cells.RawTypeAt( x, y ) == 0 && !level.Guests!.Grid.Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );
		(int X, int Y)? centre = null;
		foreach ( var cell in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - near.X ) + Math.Abs( cell.Y - near.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			var anchor = Level.GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			if ( level.Objects.CheckFree( entry, anchor.X, anchor.Y, 0 ) == OriginalPlacementResult.Allowed
				&& Enumerable.Range( anchor.X - 1, 7 ).All( x => Enumerable.Range( anchor.Y - 1, 7 ).All( y => Free( x, y ) ) ) )
			{
				centre = cell;
				break;
			}
		}
		Require( centre != null, "find a free site for a bumper ride near the spine" );
		var ride = level.PlaceObject( entry, centre!.Value.X, centre.Value.Y, 0 );
		Require( ride?.Runtime.Bump is { Template.IsBumper: true }, "buy and place the bumper ride with its controller" );
		Log.Trace( $"Park-resume fixture: bumper ride {entry.DisplayName} #{ride!.Visitors.AttractionId}." );
	}

	/// <summary>The restore phase's proof that the bumper ride came back with its session: the controller, its cars and their riders.</summary>
	private void VerifyBumperRestored()
	{
		if ( !BumperVariant )
			return;
		var bump = BumperRide()?.Runtime.Bump;
		Require( bump != null && bump.VehicleCount > 0 && bump.RiderCount > 0 && bump.Phase == BumpController.PhaseRunning, "the resumed bumper ride holds its running session" );
		Require( bump!.Vehicles.Sum( car => car.Riders.Count ) == bump.RiderCount, "the resumed cars hold the riders the controller counts" );
		Log.Trace( $"Park-resume restore: the bumper ride came back with {bump.VehicleCount} car(s) and {bump.RiderCount} rider(s)." );
	}
}
