using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// The opt-in water-ride variant of the park-resume fixture (<c>OPENTPW_SMOKE_WATER=1</c> in both phases, docs/TRACKS.md): Splish Splash is researched,
/// bought and placed near the path spine, a closed circuit is laid with the water track tool's cell clicks, and the populate stage runs until a boat is on
/// the water. The save holds the track layer, the controller and the boat; the restore phase walks the circuit again, checks the tile and boat counts
/// and continues the same ticks, whose canonical hashes (the controller and its boats included) must equal the capture phase's.
/// </summary>
internal sealed partial class ParkResumeSmokeTest
{
	/// <summary>Both phases must agree: <c>OPENTPW_SMOKE_WATER=1</c> adds a bought water ride with a closed circuit and a running boat.</summary>
	internal static bool WaterVariant => Environment.GetEnvironmentVariable( "OPENTPW_SMOKE_WATER" ) is "1" or "true";

	/// <summary>The water ride's track tiles and boats as the save boundary held them (capture phase; the restore phase compares).</summary>
	private (int Tiles, int Boats) savedWater;

	private (int Tiles, int Boats) WaterCounts() =>
		(level.WaterTracks?.Tiles().Count() ?? 0, level.Objects.Objects.Where( item => !item.IsDeleted ).Sum( item => item.Runtime.Bump?.VehicleCount ?? 0 ));

	/// <summary>The variant's populate condition: a boat is running (always true without the variant).</summary>
	private bool WaterReady() => !WaterVariant || WaterCounts().Boats > 0;

	private void PlaceWaterFixture( (int X, int Y) near )
	{
		var economy = level.Park!.Economy;
		var entry = level.Objects.Catalog.Buildable.Where( candidate => candidate.Settings.GetInt( "Bumper.WhichTrackType" ) == 2 ).OrderBy( candidate => candidate.InfoId ).FirstOrDefault();
		Require( entry != null && level.WaterTracks != null, "the theme catalogue has a water ride and the level has a track layer" );
		// The ride is not researched in a fresh park: complete its research the way a saved park records it, so the purchase path stays the player's.
		var research = economy.Research;
		research.Restore( research.Completed.Append( (entry!.InfoId, 0) ).Distinct().ToList(),
			research.Items.Select( item => ((item.InfoId, item.Level), research.GetProgress( item )) ).Where( pair => pair.Item2 != 0 ).ToList(), research.Effort );
		Require( economy.CheckBuild( entry.InfoId ) == ParkEconomy.PurchaseResult.Ok, "the researched water ride can be bought" );
		var grid = level.Objects.Grid;
		bool Free( int x, int y ) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height && level.Objects.FindAt( x, y ) == null
			&& grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed && level.Guests!.Grid.Cells.RawTypeAt( x, y ) == 0 && !level.Guests.Grid.Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );
		(int X, int Y)? centre = null;
		foreach ( var cell in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - near.X ) + Math.Abs( cell.Y - near.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			var anchor = Level.GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			if ( level.Objects.Check( entry, anchor.X, anchor.Y, 0 ) != OriginalPlacementResult.Allowed )
				continue;
			if ( Enumerable.Range( anchor.X - 2, 9 ).All( x => Enumerable.Range( anchor.Y - 1, 8 ).All( y => Free( x, y ) ) ) )
			{
				centre = cell;
				break;
			}
		}
		Require( centre != null, "find a free site for a water ride and its circuit near the spine" );
		var ride = level.PlaceObject( entry, centre!.Value.X, centre.Value.Y, 0 );
		Require( ride?.Runtime.Bump != null, "buy and place the water ride with its controller" );
		var station = level.TrackStations[ride!];
		Require( level.EnterTrackTool( ride ), "the water track tool starts" );
		foreach ( var offset in new (int X, int Y)[] { (4, 4), (-2, 4), (-2, 0) } )
		{
			var click = level.CellTool.Click( (station.First.X + offset.X, station.First.Y + offset.Y) );
			Require( click is { Completed: true }, $"the water track click at offset {offset} lays its line" );
		}
		Require( ride.Runtime.Bump!.IsTrackValid, "the circuit is valid" );
		level.CellTool.Cancel();
		Log.Trace( $"Park-resume fixture: water ride {entry.DisplayName} #{ride.Visitors.AttractionId} with {level.WaterTracks!.Tiles().Count()} track tiles at {station.First}." );
	}

	/// <summary>The restore phase's proof that the saved tracks came back: the same tiles, a valid circuit and the same boats.</summary>
	private void VerifyWaterRestored()
	{
		if ( !WaterVariant )
			return;
		var counts = savedWater = WaterCounts();
		Require( counts.Tiles == capture!.WaterAtSave.Tiles && counts.Boats == capture.WaterAtSave.Boats, $"the resumed park holds the saved track tiles and boats ({counts} vs {capture.WaterAtSave})" );
		Require( capture.WaterAtSave.Tiles > 0 && capture.WaterAtSave.Boats > 0, "the captured park had a track and a boat on it" );
		var ride = level.Objects.Objects.Single( item => item.Runtime.Bump != null );
		Require( ride.Runtime.Bump!.IsTrackValid && ride.Runtime.Bump.Sections.Count > 0, "the resumed circuit is valid with its sections rebuilt" );
		Log.Trace( $"Park-resume restore: the water ride came back with {counts.Tiles} track tiles, {counts.Boats} boat(s) and a valid circuit." );
	}
}
