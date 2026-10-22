namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-snapshot-join] The native join smoke's water ride (docs/COOP.md, "The game's join"): a closed circuit with its track add-on placed, sold and placed again before the
/// park is shared, as the park-resume smoke's add-on fixture builds it (docs/TRACKS.md, "Add-ons"). The first cut finds the track cut open by the edit tool beside the add-on, with a line
/// taken back by Backspace; the second finds it mended, the add-on's sections walked again; the add-on is sold while the guest follows. Every edit in the session goes through the track
/// tools' commands.
/// </summary>
internal sealed partial class CoopJoinSmokeTest
{
	/// <summary>The far side of the circuit: the loop is a tile deeper than the plain one, with a cell of margin, so its straights are clear of the station.</summary>
	private const int WaterDepth = 6;
	private const int WaterMargin = 1;

	private OriginalObject? waterRide, waterAddOn;
	/// <summary>The track tiles the first cut's edit leaves (two cut, one laid and taken back).</summary>
	private int cutTiles = -1;

	private (int X, int Y) WaterAt( int x, int y )
	{
		var station = level.TrackStations[waterRide!];
		return (station.First.X + x, station.First.Y + y);
	}

	/// <summary>Before the park is shared: the water ride, researched and bought, its closed circuit laid by the track tool's clicks, and its add-on bought, sold and bought again.</summary>
	private void BuildWaterRide( HashSet<(int, int)> used )
	{
		var economy = Economy;
		var entry = level.Objects.Catalog.Buildable.Where( candidate => candidate.Settings.GetInt( "Bumper.WhichTrackType" ) == 2 ).OrderBy( candidate => candidate.InfoId ).FirstOrDefault();
		Require( entry != null && level.WaterTracks != null, "the theme's catalogue has a water ride and the level a track layer" );
		Research( entry!.InfoId );
		Require( economy.CheckBuild( entry.InfoId ) == ParkEconomy.PurchaseResult.Ok, "the researched water ride can be bought" );
		var grid = level.Objects.Grid;
		var cells = level.Guests!.Grid.Cells;
		bool Free( int x, int y ) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height && level.Objects.FindAt( x, y ) == null && !used.Contains( (x, y) )
			&& grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed && cells.RawTypeAt( x, y ) == 0 && !cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );
		var focus = OriginalParkPlacement.TryGetCell( level.OriginalPark!.Heightfield, level.PathFocus.X, level.PathFocus.Y, out var focusX, out var focusY ) ? (X: focusX, Y: focusY) : (X: 64, Y: 64);
		(int X, int Y)? centre = null;
		(int X, int Y) site = default;
		foreach ( var cell in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - focus.X ) + Math.Abs( cell.Y - focus.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			var anchor = Level.GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			if ( level.Objects.CheckFree( entry, anchor.X, anchor.Y, 0 ) != OriginalPlacementResult.Allowed )
				continue;
			if ( Enumerable.Range( anchor.X - 2 - WaterMargin, 9 + 2 * WaterMargin ).All( x => Enumerable.Range( anchor.Y - 1 - WaterMargin, WaterDepth + 4 + 2 * WaterMargin ).All( y => Free( x, y ) ) ) )
			{
				centre = cell;
				site = anchor;
				break;
			}
		}
		Require( centre != null, "find a free site for a water ride and its circuit" );
		var ride = level.PlaceObject( entry, centre!.Value.X, centre.Value.Y, 0 );
		Require( ride?.Runtime.Bump != null, "buy and place the water ride with its controller" );
		// a built ride with a queue starts closed (RIDES-015, 0x4DB712): the fixture opens it as the player would
		ride!.Open();
		waterRide = ride;
		Require( level.EnterTrackTool( ride ), "the water track tool starts" );
		foreach ( var (x, y) in new (int X, int Y)[] { (4, WaterDepth), (-2, WaterDepth), (-2, 0) } )
			Require( level.CellTool.Click( WaterAt( x, y ) ) is { Completed: true }, $"the water track click at offset ({x}, {y}) lays its line" );
		Require( ride.Runtime.Bump!.IsTrackValid, "the water ride's circuit is closed" );
		level.CellTool.Cancel();
		BuyAddOnTwice( ride );
		for ( var x = site.X - 2 - WaterMargin - 2; x < site.X + 7 + WaterMargin + 2; x++ )
			for ( var y = site.Y - 1 - WaterMargin - 2; y < site.Y + WaterDepth + 3 + WaterMargin + 2; y++ )
				used.Add( (x, y) );
		Log.Trace( $"Co-op join smoke: water ride {entry.DisplayName} #{ride.Visitors.AttractionId} with {level.BuilderOf( ride )!.Tiles().Count()} track tiles and its add-on "
			+ $"{waterAddOn!.Entry.DisplayName} (sold once and bought again)." );
	}

	/// <summary>
	/// The ride's track add-on placed by the build tool as an add-on row arms it, sold and placed again, as the park-resume smoke's add-on fixture does it, on a tile the first cut leaves.
	/// No Instant Action player can buy one: its upgrade list lists nothing (<see cref="Level.UpgradeOffer"/>, UITEXT 27), and that list is the one way to an add-on row. The stand-in is
	/// sound for the join: the flag gates the list, the level purchase and the advice only, and the placement, the walk, the save and the restore do not read the start kind.
	/// </summary>
	private void BuyAddOnTwice( OriginalObject ride )
	{
		var entry = level.Objects.Catalog.Entries.Where( candidate => TrackAddOns.IsTrackAddOn( candidate ) && candidate.Settings.GetInt( "AddOn.UpgradesId" ) == ride.Entry.InfoId )
			.OrderBy( candidate => candidate.InfoId ).FirstOrDefault();
		Require( entry != null, $"the theme has a track add-on of {ride.Entry.DisplayName}" );
		Research( entry!.InfoId );
		Require( level.UpgradeOffer( ride ).Rows.Count == 0, "Instant Action's upgrade list lists nothing" );
		OriginalObject? Buy()
		{
			Require( level.BeginAddOn( entry, ride ), "the build tool is armed with the add-on for the ride" );
			try
			{
				foreach ( var (x, y) in level.BuilderOf( ride )!.Tiles().Where( tile => !CutTile( tile ) ).ToArray() )
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
		var addOn = Buy();
		Require( addOn != null && AddOnSections( ride ) > 0, $"buy and place {entry.DisplayName} on the ride's track" );
		level.Objects.Remove( addOn! );
		Require( AddOnSections( ride ) == 0 && ride.Runtime.Bump!.IsTrackValid, "selling the add-on walks the circuit back to plain sections" );
		waterAddOn = Buy();
		Require( waterAddOn != null && AddOnSections( ride ) > 0, $"buy {entry.DisplayName} again" );
	}

	/// <summary>The two tiles of the circuit's left straight the first cut takes (<see cref="CutWaterTrack"/>), by their origin cells: the add-on needs the far straight.</summary>
	private bool CutTile( (int X, int Y) tile ) => tile == WaterAt( -2, 2 ) || tile == WaterAt( -2, 4 );

	/// <summary>The two ends of the left straight's gap: the corner at the far side and the one before the station.</summary>
	private (int X, int Y) FarEnd => WaterAt( -2, WaterDepth );
	private (int X, int Y) NearEnd => WaterAt( -2, 0 );

	/// <summary>The ride's sections that carry an add-on (kind 12 or 13 with a linked add-on).</summary>
	private static int AddOnSections( OriginalObject ride ) => ride.Runtime.Bump!.Sections.Count( section => section.Kind is 12 or 13 && section.AddOn != null );

	/// <summary>
	/// Before the first cut, by the track tools' commands: the edit tool cuts the two tiles of the left straight (the add-on stands on the far one), and the line tool, going on from an open
	/// end, lays a tile into the gap and takes it back with Backspace. The circuit stays open over the cut, with the add-on on it.
	/// </summary>
	private void CutWaterTrack()
	{
		var ride = waterRide!;
		var bump = ride.Runtime.Bump!;
		var builder = level.BuilderOf( ride )!;
		var tiles = builder.Tiles().Count();
		Require( level.EnterTrackTool( ride ) && level.CellTool.Mode == CellToolMode.WaterEdit && level.CellTool.Start == null, "the track button on the closed circuit opens the edit tool" );
		Require( level.CellTool.Click( WaterAt( -1, 5 ) ) == null && level.CellTool.Start == WaterAt( -1, 5 ), "a click on the left straight picks its tile" );
		var cut = level.CellTool.Click( WaterAt( -2, 2 ) );
		Require( cut is { Completed: true } && cut.Built.Count == 2 && !bump.IsTrackValid, "the second click cuts two tiles and opens the circuit" );
		Require( level.CellTool.IsActive && level.CellTool.Mode == CellToolMode.WaterTrack && level.CellTool.Start is { } end && (end == FarEnd || end == NearEnd),
			$"the tool goes on as the line tool from an end of the gap ({level.CellTool.Start})" );
		var line = level.CellTool.Click( level.CellTool.Start == FarEnd ? WaterAt( -2, 4 ) : WaterAt( -2, 2 ) );
		Require( line is { Completed: true } && !bump.IsTrackValid && builder.Tiles().Count() == tiles - 1, "the line tool lays a tile into the gap" );
		Require( level.CellTool.Undo() && builder.Tiles().Count() == tiles - 2 && !bump.IsTrackValid, "Backspace takes the line back" );
		cutTiles = builder.Tiles().Count();
		level.CellTool.Cancel();
		Require( waterAddOn is { IsDeleted: false }, "the add-on stands on the track cut open" );
	}

	/// <summary>After the first cut: the line tool, opened on the open circuit, closes it again across the gap, and the walk links the add-on's sections again.</summary>
	private void MendWaterTrack()
	{
		var ride = waterRide!;
		Require( level.EnterTrackTool( ride ) && level.CellTool.Mode == CellToolMode.WaterTrack && level.CellTool.Start != null, "the track button on the open circuit opens the line tool" );
		var across = level.CellTool.Start == FarEnd ? NearEnd : FarEnd;
		Require( level.CellTool.Click( across ) is { Completed: true } && ride.Runtime.Bump!.IsTrackValid, "the line across the gap closes the circuit again" );
		level.CellTool.Cancel();
		Require( AddOnSections( ride ) > 0, "the mended circuit's sections carry the add-on again" );
	}

	/// <summary>After the second cut, while the guest follows: the remove tool's command demolishes the add-on, and the circuit is walked back to plain sections.</summary>
	private void SellAddOnOffTheTrack()
	{
		Require( waterAddOn is { IsDeleted: false }, "the add-on is still on the track to be sold" );
		var (x, y, _) = waterAddOn!.Cells.First();
		Issue( new RemoveObjectAtCommand( x, y ), "the add-on is sold off the water ride's track" );
		Require( waterAddOn.IsDeleted && AddOnSections( waterRide! ) == 0 && waterRide!.Runtime.Bump!.IsTrackValid, "the mended circuit runs without its add-on" );
	}

	/// <summary>The water ride's staged state at a cut, in <paramref name="park"/>.</summary>
	private IEnumerable<string> WaterStagedAtCut( Level park )
	{
		if ( waterRide is not { IsDeleted: false } ride || park.BuilderOf( ride ) is not { } builder || ride.Runtime.Bump is not { } bump )
			yield break;
		if ( !bump.IsTrackValid && builder.Tiles().Count() == cutTiles )
			yield return "water track cut open after a Backspace";
		if ( waterAddOn is { IsDeleted: false } && !bump.IsTrackValid )
			yield return "add-on on a track cut open";
		if ( waterAddOn is { IsDeleted: false } && bump.IsTrackValid && AddOnSections( ride ) > 0 )
			yield return "add-on on a mended track";
	}
}
