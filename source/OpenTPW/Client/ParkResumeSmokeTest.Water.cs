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

	/// <summary>
	/// With the water variant, <c>OPENTPW_SMOKE_ADDON=1</c> (both phases) also buys the ride's track add-on from its upgrade list, sells it and buys it again before the boats
	/// run, so the save holds the add-on's sections and the restore walks them back (docs/TRACKS.md "Add-ons").
	/// </summary>
	internal static bool AddOnVariant => WaterVariant && Environment.GetEnvironmentVariable( "OPENTPW_SMOKE_ADDON" ) is "1" or "true";

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
			&& grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed && level.Guests!.Grid.Cells.RawTypeAt( x, y ) == 0 && !level.Guests!.Grid.Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );
		// the add-on variant's tunnel needs a straight of two tiles clear of the station with free ground beside it: the loop is a tile deeper, with a cell of margin
		var (depth, margin) = AddOnVariant ? (6, 1) : (4, 0);
		(int X, int Y)? centre = null;
		foreach ( var cell in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - near.X ) + Math.Abs( cell.Y - near.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			var anchor = Level.GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			if ( level.Objects.CheckFree( entry, anchor.X, anchor.Y, 0 ) != OriginalPlacementResult.Allowed )
				continue;
			if ( Enumerable.Range( anchor.X - 2 - margin, 9 + 2 * margin ).All( x => Enumerable.Range( anchor.Y - 1 - margin, depth + 4 + 2 * margin ).All( y => Free( x, y ) ) ) )
			{
				centre = cell;
				break;
			}
		}
		Require( centre != null, "find a free site for a water ride and its circuit near the spine" );
		var ride = level.PlaceObject( entry, centre!.Value.X, centre.Value.Y, 0 );
		Require( ride?.Runtime.Bump != null, "buy and place the water ride with its controller" );
		// a built ride with a queue starts closed (RIDES-015, 0x4DB712): the fixture opens it as the player would
		ride!.Open();
		var station = level.TrackStations[ride!];
		Require( level.EnterTrackTool( ride, justPlaced: true ), "the water track tool starts" );
		foreach ( var offset in new (int X, int Y)[] { (4, depth), (-2, depth), (-2, 0) } )
		{
			var click = level.CellTool.Click( (station.First.X + offset.X, station.First.Y + offset.Y) );
			Require( click is { Completed: true }, $"the water track click at offset {offset} lays its line" );
		}
		Require( ride.Runtime.Bump!.IsTrackValid, "the circuit is valid" );
		level.CellTool.Cancel();
		Log.Trace( $"Park-resume fixture: water ride {entry.DisplayName} #{ride.Visitors.AttractionId} with {level.WaterTracks!.Tiles().Count()} track tiles at {station.First}." );
		if ( AddOnVariant )
			PlaceAddOnFixture( ride );
	}

	/// <summary>The ride's sections that carry an add-on (kind 12 or 13 with a linked add-on).</summary>
	private static int AddOnSections( OriginalObject ride ) => ride.Runtime.Bump!.Sections.Count( section => section.Kind is 12 or 13 && section.AddOn != null );

	/// <summary>The add-on variant: the ride's track add-on is researched, bought from its upgrade list, sold and bought again, as the player's tool does it.</summary>
	private void PlaceAddOnFixture( OriginalObject ride )
	{
		var economy = level.Park!.Economy;
		var entry = level.Objects.Catalog.Entries.Where( candidate => TrackAddOns.IsTrackAddOn( candidate ) && candidate.Settings.GetInt( "AddOn.UpgradesId" ) == ride.Entry.InfoId )
			.OrderBy( candidate => candidate.InfoId ).FirstOrDefault();
		Require( entry != null, $"the theme has a track add-on of {ride.Entry.DisplayName}" );
		var research = economy.Research;
		research.Restore( research.Completed.Append( (entry!.InfoId, 0) ).Distinct().ToList(),
			research.Items.Select( item => ((item.InfoId, item.Level), research.GetProgress( item )) ).Where( pair => pair.Item2 != 0 ).ToList(), research.Effort );
		Require( level.UpgradeOffer( ride ).Rows.Any( row => row.Kind == RideUpgradeRowKind.AddOn && row.AddOn == entry ), "the ride's upgrade list offers its researched add-on" );
		OriginalObject? Buy()
		{
			Require( level.BeginAddOn( entry, ride ), "the add-on row arms the build tool for the ride" );
			try
			{
				foreach ( var (x, y) in level.BuilderOf( ride )!.Tiles().ToArray() )
				{
					// the pointer on the tile's far cell: the hover counts even cells from the ride's anchor (TrackAddOns.HoverTarget)
					var (targetX, targetY, rotation) = level.BuildTarget( entry, x + 1, y + 1 );
					if ( level.CheckBuildTarget( entry, targetX, targetY, rotation ) == OriginalPlacementResult.Allowed && level.PlaceAddOn( entry, targetX, targetY, rotation, ride ) is { } placed )
						return placed;
				}
				return null;
			}
			finally
			{
				level.BuildEntry = null;
			}
		}
		var cash = economy.Balance;
		var addOn = Buy();
		Require( addOn != null && economy.Balance < cash, $"buy and place {entry.DisplayName} on the ride's track" );
		Require( ride.Runtime.Bump!.IsTrackValid && AddOnSections( ride ) > 0, "the walk after the placement links the add-on's sections" );
		Require( level.WindowTarget( addOn ) == ride, "a click on the add-on opens its ride's window" );
		// the sale is the demolition 0x527EE0, whose pass 2 clears the record under every cell of the add-on's shape (docs/reverse/ORACLE.md, X37): the tile the add-on stood on goes with it and the
		// circuit is open; the fixture lays that tile again, as the player would, and the circuit closes before the add-on is bought again
		var builder = level.BuilderOf( ride )!;
		var origin = addOn!.Cells.Select( cell => builder.OriginOf( cell.X, cell.Y ) ).First( found => found != null )!.Value;
		var tile = builder.Tracks.Get( origin.X, origin.Y );
		level.Objects.Remove( addOn! );
		Require( builder.Tracks.Get( origin.X, origin.Y ).IsEmpty && !ride.Runtime.Bump.IsTrackValid, "selling the add-on takes the tile under it with it and opens the circuit" );
		Require( builder.PlaceTile( origin.X, origin.Y, tile.Owner ) == CellBuildResult.Ok, "the tile is laid again" );
		foreach ( var heading in TrackHeading.All.Where( heading => (tile.Mask & heading) != 0 ) )
			Require( builder.Link( origin, (origin.X + TrackStation.TileCells * TrackHeading.Dx( heading ), origin.Y + TrackStation.TileCells * TrackHeading.Dy( heading )) ), "the laid tile joins its neighbours again" );
		Require( level.RewalkTrack( ride, TrackWalkCause.Line ) is { IsValid: true } && ride.Runtime.Bump.Sections.All( section => section.Kind is not (12 or 13) ), "the circuit closes again with plain sections" );
		addOn = Buy();
		Require( addOn != null && AddOnSections( ride ) > 0, $"buy {entry.DisplayName} again" );
		Log.Trace( $"Park-resume fixture: add-on {entry.DisplayName} on {ride.Entry.DisplayName}'s track ({AddOnSections( ride )} add-on sections), sold once and bought again." );
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
		if ( AddOnVariant )
		{
			Require( level.Objects.Objects.Count( item => !item.IsDeleted && TrackAddOns.IsTrackAddOn( item.Entry ) ) == 1 && AddOnSections( ride ) > 0,
				"the resumed park holds the add-on and its walked sections point at it" );
			Log.Trace( $"Park-resume restore: the add-on came back with {AddOnSections( ride )} add-on sections." );
		}
		Log.Trace( $"Park-resume restore: the water ride came back with {counts.Tiles} track tiles, {counts.Boats} boat(s) and a valid circuit." );
	}
}
