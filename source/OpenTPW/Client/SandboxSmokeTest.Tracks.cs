using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// Water-track check of the native smoke test (docs/TRACKS.md): Splish Splash is placed on a site with room for a small closed circuit, the water
/// track tool lays three lines from synthetic cell clicks (each tile charged), the walk validates the circuit, the ride's script opens, launches a boat
/// that runs, and the renderer draws the tiles and the boat; then the ride is removed and its track goes with it.
/// </summary>
internal sealed partial class SandboxSmokeTest
{
	private bool waterTrackVerified;
	private bool waterTrackDone;
	private OriginalObject? waterStation;
	private System.Diagnostics.Stopwatch? waterWait;
	private (int X, int Y) waterBoatStart;
	private bool waterBoatSeen;

	/// <summary>Cell offsets from the footprint anchor (rotation 0) of the circuit's three corner clicks: right of the station, above it, back down the left side.</summary>
	private static readonly (int X, int Y)[] WaterCircuit = { (4, 0), (4, 4), (-2, 4), (-2, 0) };

	private void VerifyHudWaterTrack()
	{
		waterTrackVerified = true;
		if ( hud == null || hudContext == null || level.OriginalPark == null || level.WaterTracks == null )
		{
			waterTrackDone = true;
			return;
		}
		var entry = level.Objects.Catalog.Buildable.Where( item => item.Settings.GetInt( "Bumper.WhichTrackType" ) == 2 ).OrderBy( item => item.InfoId ).FirstOrDefault();
		Require( entry != null, "the theme catalogue has a water ride" );
		var canvas = hudContext.Canvas;
		var viewport = new System.Numerics.Vector2( canvas.Width, canvas.Height );
		bool OnScreen( (int X, int Y) cell ) =>
			level.TryProjectCell( cell.X, cell.Y, viewport, out var point ) && point.X > canvas.Width * 0.05f && point.X < canvas.Width * 0.95f && point.Y > canvas.Height * 0.05f && point.Y < canvas.Height * 0.95f;
		var grid = level.Objects.Grid;
		bool Free( int x, int y ) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height
			&& level.Objects.FindAt( x, y ) == null && grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed
			&& level.Guests!.Grid.Cells.RawTypeAt( x, y ) == 0;
		(int X, int Y)? anchor = null;
		for ( var index = 0; index < grid.Width * grid.Height && anchor == null; index++ )
		{
			var cell = (X: index % grid.Width, Y: index / grid.Width);
			var candidate = Level.GetCentredAnchor( entry!, cell.X, cell.Y, 0 );
			if ( level.Objects.Check( entry!, candidate.X, candidate.Y, 0 ) != OriginalPlacementResult.Allowed || !OnScreen( cell ) )
				continue;
			// the footprint rows, the loop tiles around it (x from −2 to +5, y from 0 to 5) and the queue's front cell must be free
			var cells = Enumerable.Range( candidate.X - 2, 9 ).SelectMany( x => Enumerable.Range( candidate.Y - 1, 8 ).Select( y => (X: x, Y: y) ) );
			if ( cells.All( loopCell => Free( loopCell.X, loopCell.Y ) && OnScreen( loopCell ) ) )
				anchor = candidate;
		}
		Require( anchor != null, "find an on-screen site for a water ride and its circuit" );
		var economy = level.Park?.Economy;
		var centre = (X: anchor!.Value.X + 1, Y: anchor.Value.Y + 1);
		var station = level.PlaceObject( entry!, centre.X, centre.Y, 0 ) ?? level.Objects.TryPlace( entry!, anchor.Value.X, anchor.Value.Y, 0, out _ );
		Require( station?.Runtime.Bump is not null, "place a water ride with its controller" );
		waterStation = station;
		var layout = level.TrackStations[station!];
		Require( layout.First == (anchor.Value.X, anchor.Value.Y) && layout.Heading == TrackHeading.PosX, "the station tiles sit on the footprint's first tile column" );
		Require( !station!.Runtime.Bump!.IsTrackValid && !level.WaterTracks.Tracks.Cells().Any( item => item.Cell.Type == TrackCellTypes.WaterOrigin ), "no track yet: the circuit is invalid" );
		var balance = economy?.Balance ?? 0;
		Require( level.EnterTrackTool( station ) && level.CellTool.IsActive && level.CellTool.Mode == CellToolMode.WaterTrack && level.CellTool.Start == layout.Exit,
			"the water track tool starts at the tile after the station" );
		var corners = WaterCircuit.Select( offset => (X: anchor.Value.X + offset.X, Y: anchor.Value.Y + offset.Y) ).ToArray();
		var tiles = 0;
		foreach ( var corner in corners.Skip( 1 ) )
		{
			var ghost = level.CellTool.Hover( corner );
			Require( ghost is { Completed: true } && ghost.Built.Count > 0, $"the tool previews the line to {corner}" );
			var committed = level.CellTool.Click( corner );
			Require( committed is { Completed: true }, $"the click lays the line to {corner}" );
			tiles += committed!.Built.Count;
			Require( (level.CellTool.IsActive && level.CellTool.Start == committed.SnappedEnd) || station.Runtime.Bump.IsTrackValid, "the tool continues from the line's end" );
		}
		var tool = level.WaterTracks;
		Require( station.Runtime.Bump.IsTrackValid, "the third line closes the circuit and the walk accepts it" );
		Require( !(level.CellTool.IsActive && level.CellTool.Mode == CellToolMode.WaterTrack), "a valid circuit closes the track tool" );
		level.CellTool.Cancel();
		Require( tiles == 8 && tool.Tiles().Count() == 10, $"eight tiles were laid around the two station tiles ({tiles})" );
		if ( economy != null )
			Require( balance - economy.Balance >= tiles * tool.TileCost, "each tile is charged Costs.WaterTrackCell" );
		var bump = station.Runtime.Bump;
		Require( bump.Sections.Count == 10 && bump.Buoys.Count == 9, $"the controller holds the circuit's sections and buoys ({bump.Sections.Count}, {bump.Buoys.Count})" );
		waterWait = System.Diagnostics.Stopwatch.StartNew();
		Log.Trace( $"HUD water track: {entry!.DisplayName} with {tiles} track tiles, {bump.Sections.Count} sections, {bump.Objects.Count} collision objects and {bump.Buoys.Count} buoys." );
	}

	/// <summary>Per frame after the build: waits for the script to launch a boat, checks that the tiles and the boat are drawn and the boat runs, then removes the ride.</summary>
	private void UpdateWaterTrackPhase()
	{
		if ( waterStation == null || waterTrackDone )
			return;
		var bump = waterStation.Runtime.Bump!;
		Require( waterWait!.Elapsed.TotalSeconds < 120, $"a boat launches and runs (phase {bump.Phase}, boats {bump.VehicleCount}, runs {bump.CompletedRuns})" );
		if ( bump.VehicleCount == 0 && !waterBoatSeen )
			return;
		var boat = bump.Vehicles.FirstOrDefault();
		if ( !waterBoatSeen )
		{
			waterBoatSeen = true;
			waterBoatStart = boat == null ? (0, 0) : (boat.X, boat.Y);
			waterWait.Restart();
			Require( level.TrackInstances.Tiles > 0 && level.TrackInstances.Boats > 0, $"the renderer holds the track tiles and the boat ({level.TrackInstances})" );
			return;
		}
		// a few seconds later the boat has moved along the circuit (or has finished its lap)
		var moved = boat == null || Math.Abs( boat.X - waterBoatStart.X ) + Math.Abs( boat.Y - waterBoatStart.Y ) > 6000;
		if ( !moved )
			return;
		Require( level.TrackInstances.Tiles >= 8, "the track tiles are drawn" );
		CaptureFrame( "original-water-track.png" );
		var tilesBefore = level.WaterTracks!.Tracks.Count;
		var ride = waterStation;
		level.Objects.Remove( ride );
		Require( level.WaterTracks.Tracks.Count == 0 && tilesBefore > 0, "removing the ride clears its station and track tiles" );
		Require( level.Objects.ScriptWorld.Bumpers.Controllers.Count == 0, "removing the ride removes its controller" );
		Log.Trace( $"HUD water track: a boat ran ({(boat == null ? "lap complete" : $"{boat.X},{boat.Y}")}), the tiles and the boat were drawn, the ride and its track were removed." );
		waterStation = null;
		waterTrackDone = true;
	}
}
