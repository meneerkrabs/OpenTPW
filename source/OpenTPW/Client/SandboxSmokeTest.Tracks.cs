using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// Track-ride checks of the native smoke test (docs/TRACKS.md). Water phase: Splish Splash is placed on a site with room for a small closed circuit, the water
/// track tool lays three lines from synthetic cell clicks (each tile charged), the walk validates the circuit, the ride's script opens, launches a boat
/// that runs, and the renderer draws the tiles and the boat; then the ride is removed and its track goes with it. Kart phase: the same for the go-kart
/// ride (Dino Karts on the Jungle level): the kart tool lays the circuit, two karts are loaded and park on the grid, the race starts, one lap runs to the
/// race-over and the unload, a frame is captured with the karts on the track, and the ride and its track are removed.
/// </summary>
internal sealed partial class SandboxSmokeTest
{
	/// <summary>One track ride's smoke state: the placed station and what the phase has seen so far.</summary>
	private sealed class TrackSmokeRun
	{
		public bool Verified;
		public bool Done;
		public OriginalObject? Station;
		public System.Diagnostics.Stopwatch? Wait;
		public (int X, int Y) VehicleStart;
		public bool VehicleSeen;
		public bool Raced;
		public bool Captured;
		public int Phase;
		public int PeakKarts;
	}

	private readonly TrackSmokeRun waterSmoke = new();
	private readonly TrackSmokeRun kartSmoke = new();
	private readonly TrackSmokeRun bumperSmoke = new();
	private bool waterTrackVerified => waterSmoke.Verified;
	private bool waterTrackDone => waterSmoke.Done;

	/// <summary>Whether every track phase that started has finished (the smoke test completes after them).</summary>
	private bool TrackSmokesDone => (waterSmoke.Done || !waterSmoke.Verified) && (kartSmoke.Done || !kartSmoke.Verified) && (bumperSmoke.Done || !bumperSmoke.Verified);

	/// <summary>Cell offsets from the footprint anchor (rotation 0) of the circuit's three corner clicks: right of the station, above it, back down the left side.</summary>
	private static readonly (int X, int Y)[] WaterCircuit = { (4, 0), (4, 4), (-2, 4), (-2, 0) };

	private void VerifyHudWaterTrack()
	{
		waterSmoke.Verified = true;
		if ( hud == null || hudContext == null || level.OriginalPark == null || level.WaterTracks == null )
		{
			waterSmoke.Done = true;
			return;
		}
		StartTrackSmoke( waterSmoke, trackType: 2 );
	}

	private void StartTrackSmoke( TrackSmokeRun run, int trackType )
	{
		run.Verified = true;
		var water = trackType == 2;
		var kind = water ? "water" : "kart";
		var entry = level.Objects.Catalog.Buildable.Where( item => item.Settings.GetInt( "Bumper.WhichTrackType" ) == trackType ).OrderBy( item => item.InfoId ).FirstOrDefault();
		Require( entry != null, $"the theme catalogue has a {kind} ride" );
		var canvas = hudContext!.Canvas;
		var viewport = new System.Numerics.Vector2( canvas.Width, canvas.Height );
		bool OnScreen( (int X, int Y) cell ) =>
			level.TryProjectCell( cell.X, cell.Y, viewport, out var point ) && point.X > canvas.Width * 0.05f && point.X < canvas.Width * 0.95f && point.Y > canvas.Height * 0.05f && point.Y < canvas.Height * 0.95f;
		var grid = level.Objects.Grid;
		bool Free( int x, int y ) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height
			&& level.Objects.FindAt( x, y ) == null && grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed
			&& level.Guests!.Grid.Cells.RawTypeAt( x, y ) == 0 && !level.Guests!.Grid.Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );
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
		Require( anchor != null, $"find an on-screen site for a {kind} ride and its circuit" );
		var economy = level.Park?.Economy;
		var centre = Level.GetClickCell( entry!, anchor!.Value.X, anchor.Value.Y, 0 );
		var station = level.PlaceObject( entry!, centre.X, centre.Y, 0 ) ?? level.Objects.TryPlace( entry!, anchor.Value.X, anchor.Value.Y, 0, out _ );
		Require( station?.Runtime.Bump is not null, $"place a {kind} ride with its controller" );
		run.Station = station;
		var layout = level.TrackStations[station!];
		Require( layout.First == (anchor.Value.X, anchor.Value.Y) && layout.Heading == TrackHeading.PosX, "the station tiles sit on the footprint's first tile column" );
		var builder = level.BuilderOf( station! )!;
		var originType = water ? TrackCellTypes.WaterOrigin : TrackCellTypes.KartOrigin;
		var mode = water ? CellToolMode.WaterTrack : CellToolMode.KartTrack;
		Require( !station!.Runtime.Bump!.IsTrackValid && !builder.Tracks.Cells().Any( item => item.Cell.Type == originType ), "no track yet: the circuit is invalid" );
		var balance = economy?.Balance ?? 0;
		Require( level.EnterTrackTool( station ) && level.CellTool.IsActive && level.CellTool.Mode == mode && level.CellTool.Start == layout.Exit,
			$"the {kind} track tool starts at the tile after the station" );
		var corners = WaterCircuit.Select( offset => (X: anchor.Value.X + offset.X, Y: anchor.Value.Y + offset.Y) ).ToArray();
		var tiles = 0;
		foreach ( var corner in corners.Skip( 1 ) )
		{
			var ghost = level.CellTool.Hover( corner );
			Require( ghost is { Completed: true } && ghost.Built.Count > 0, $"the tool previews the line to {corner} (stopped by {ghost?.StoppedBy} at {ghost?.StoppedAt}, {ghost?.Built.Count} tiles)" );
			var committed = level.CellTool.Click( corner );
			Require( committed is { Completed: true }, $"the click lays the line to {corner}" );
			tiles += committed!.Built.Count;
			Require( (level.CellTool.IsActive && level.CellTool.Start == committed.SnappedEnd) || station.Runtime.Bump.IsTrackValid, "the tool continues from the line's end" );
		}
		Require( station.Runtime.Bump.IsTrackValid, "the third line closes the circuit and the walk accepts it" );
		Require( !(level.CellTool.IsActive && level.CellTool.Mode == mode), "a valid circuit closes the track tool" );
		level.CellTool.Cancel();
		Require( tiles == 8 && builder.Tiles().Count() == 10, $"eight tiles were laid around the two station tiles ({tiles})" );
		if ( economy != null )
			Require( balance - economy.Balance >= tiles * builder.TileCost, $"each tile is charged Costs.{(water ? "Water" : "Kart")}TrackCell" );
		var bump = station.Runtime.Bump;
		var expectedBuoys = water ? 9 : 17;
		Require( bump.Sections.Count == 10 && bump.Buoys.Count == expectedBuoys, $"the controller holds the circuit's sections and buoys ({bump.Sections.Count}, {bump.Buoys.Count})" );
		run.Wait = System.Diagnostics.Stopwatch.StartNew();
		Log.Trace( $"HUD {kind} track: {entry!.DisplayName} with {tiles} track tiles, {bump.Sections.Count} sections, {bump.Objects.Count} collision objects and {bump.Buoys.Count} buoys." );
	}

	/// <summary>Per frame after the build: runs the water phase, then the kart phase.</summary>
	private void UpdateWaterTrackPhase()
	{
		UpdateWaterPhase();
		if ( waterSmoke.Done && !kartSmoke.Verified && frame >= 6 )
		{
			if ( hud == null || hudContext == null || level.OriginalPark == null || level.KartTracks == null || !level.Objects.Catalog.Buildable.Any( item => item.Settings.GetInt( "Bumper.WhichTrackType" ) == 1 ) )
				kartSmoke.Verified = kartSmoke.Done = true;
			else
				StartTrackSmoke( kartSmoke, trackType: 1 );
		}
		UpdateKartPhase();
		if ( kartSmoke.Done && !bumperSmoke.Verified && frame >= 6 )
			StartBumperSmoke();
		UpdateBumperPhase();
	}

	/// <summary>
	/// The bumper-car phase: a bumper ride is placed on an on-screen site (no track, no tool), the script makes its cars, the smoke loads two riders through the
	/// controller's own commands (the script waits for real guests), starts a short session, captures the cars wandering the arena with their riders' heads, lets
	/// the session run out, checks both riders were handed back, and removes the ride.
	/// </summary>
	private void StartBumperSmoke()
	{
		var run = bumperSmoke;
		run.Verified = true;
		var entry = level.Objects.Catalog.Buildable.Where( item => BumperTemplates.ForRide( item.Settings.GetInt( "Bumper.WhichTrackType" ), item.Settings.GetInt( "Bumper.BumperType" ) ) is { IsBumper: true } ).OrderBy( item => item.InfoId ).FirstOrDefault();
		if ( hudContext == null || level.OriginalPark == null || level.Guests == null || entry == null )
		{
			run.Done = true;
			return;
		}
		var canvas = hudContext.Canvas;
		var viewport = new System.Numerics.Vector2( canvas.Width, canvas.Height );
		bool OnScreen( (int X, int Y) cell ) =>
			level.TryProjectCell( cell.X, cell.Y, viewport, out var point ) && point.X > canvas.Width * 0.05f && point.X < canvas.Width * 0.95f && point.Y > canvas.Height * 0.05f && point.Y < canvas.Height * 0.95f;
		var grid = level.Objects.Grid;
		bool Free( int x, int y ) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height
			&& level.Objects.FindAt( x, y ) == null && grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed
			&& level.Guests!.Grid.Cells.RawTypeAt( x, y ) == 0 && !level.Guests!.Grid.Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );
		(int X, int Y)? anchor = null;
		for ( var index = 0; index < grid.Width * grid.Height && anchor == null; index++ )
		{
			var cell = (X: index % grid.Width, Y: index / grid.Width);
			var candidate = Level.GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			if ( level.Objects.Check( entry, candidate.X, candidate.Y, 0 ) != OriginalPlacementResult.Allowed || !OnScreen( cell ) )
				continue;
			if ( Enumerable.Range( candidate.X - 1, 7 ).All( x => Enumerable.Range( candidate.Y - 1, 8 ).All( y => Free( x, y ) && OnScreen( (x, y) ) ) ) )
				anchor = candidate;
		}
		Require( anchor != null, "find an on-screen site for a bumper ride" );
		var click = Level.GetClickCell( entry, anchor!.Value.X, anchor.Value.Y, 0 );
		var station = level.PlaceObject( entry, click.X, click.Y, 0 ) ?? level.Objects.TryPlace( entry, anchor.Value.X, anchor.Value.Y, 0, out _ );
		Require( station?.Runtime.Bump is { Template.IsBumper: true }, "place a bumper ride with its controller" );
		var bump = station!.Runtime.Bump!;
		Require( !level.TrackStations.ContainsKey( station ) && bump.IsTrackValid && bump.Objects.Count == 1 && bump.Buoys.Count == 8, "a bumper ride has an arena, no station tiles and a valid ride" );
		var (arenaX, arenaY) = Level.BumperArenaCentre( station );
		Require( bump.StationX == arenaX && bump.StationY == arenaY, "the arena sits on the ride's footprint" );
		run.Station = station;
		run.Wait = System.Diagnostics.Stopwatch.StartNew();
		Log.Trace( $"HUD bumper ride: {entry.DisplayName} (type {bump.Type}) placed with an arena of radius {bump.Objects[0].Extent} and {bump.Buoys.Count} buoys." );
	}

	private void UpdateBumperPhase()
	{
		var run = bumperSmoke;
		if ( run.Station == null || run.Done )
			return;
		var bump = run.Station.Runtime.Bump!;
		Require( run.Wait!.Elapsed.TotalSeconds < 180, $"the bumper session runs (phase {bump.Phase}, cars {bump.VehicleCount}, riders {bump.RiderCount}, stage {run.Phase})" );
		switch ( run.Phase )
		{
			case 0:
				// the script opens the ride and makes the cars up to its capacity
				if ( bump.Phase != BumpController.PhaseLoading || bump.VehicleCount < 2 )
					return;
				Require( bump.Command( 1, 7101 ) == 1 && bump.Command( 12, 0 ) == 1, "the first rider takes a car" );
				Require( bump.Command( 1, 7102 ) == 1 && bump.Command( 12, 0 ) == 1, "the second rider takes a car" );
				Require( level.TrackInstances.Boats == bump.VehicleCount, $"the renderer holds every car ({level.TrackInstances})" );
				bump.Command( 13, 12 );
				bump.Command( 3, 0 );
				Require( bump.Phase == BumpController.PhaseRunning && bump.RiderCount == 2, "BUMP 3 starts the session with both riders" );
				run.VehicleStart = bump.Vehicles.Where( car => car.Riders.Count > 0 ).Select( car => (car.X, car.Y) ).First();
				run.Wait.Restart();
				run.Phase = 1;
				return;
			case 1:
				var moving = bump.Vehicles.FirstOrDefault( car => car.Riders.Count > 0 );
				if ( !run.Captured && moving != null && Math.Abs( moving.X - run.VehicleStart.X ) + Math.Abs( moving.Y - run.VehicleStart.Y ) > 3000 )
				{
					run.Captured = true;
					CaptureFrame( "original-bumper-cars.png" );
					Require( bump.Vehicles.All( car => BumpMath.Distance( car.X - bump.StationX, car.Y - bump.StationY ) <= bump.Objects[0].Extent + 512 ), "the cars stay in the arena" );
				}
				if ( bump.RiderCount != 0 )
					return;
				Require( run.Captured, "the cars were seen driving before the session ended" );
				Require( bump.Phase == BumpController.PhaseLoading, "the ride loads again once the riders left" );
				Require( bump.Command( 2, 0 ) != 0 && bump.Command( 2, 0 ) != 0 && bump.Command( 2, 0 ) == 0, "both riders were handed back" );
				var ride = run.Station;
				level.Objects.Remove( ride );
				Require( !level.Objects.ScriptWorld.Bumpers.Controllers.Contains( bump ), "removing the ride removes its controller" );
				Log.Trace( $"HUD bumper ride: {bump.VehicleCount} cars wandered the arena with two riders, the session ended, both riders were handed back and the ride was removed." );
				run.Station = null;
				run.Done = true;
				return;
		}
	}

	/// <summary>Waits for the script to launch a boat, checks that the tiles and the boat are drawn and the boat runs, then removes the ride.</summary>
	private void UpdateWaterPhase()
	{
		var run = waterSmoke;
		if ( run.Station == null || run.Done )
			return;
		var bump = run.Station.Runtime.Bump!;
		Require( run.Wait!.Elapsed.TotalSeconds < 120, $"a boat launches and runs (phase {bump.Phase}, boats {bump.VehicleCount}, runs {bump.CompletedRuns})" );
		if ( bump.VehicleCount == 0 && !run.VehicleSeen )
			return;
		var boat = bump.Vehicles.FirstOrDefault();
		if ( !run.VehicleSeen )
		{
			run.VehicleSeen = true;
			run.VehicleStart = boat == null ? (0, 0) : (boat.X, boat.Y);
			run.Wait.Restart();
			Require( level.TrackInstances.Tiles > 0 && level.TrackInstances.Boats > 0, $"the renderer holds the track tiles and the boat ({level.TrackInstances})" );
			return;
		}
		// a few seconds later the boat has moved along the circuit (or has finished its lap)
		var moved = boat == null || Math.Abs( boat.X - run.VehicleStart.X ) + Math.Abs( boat.Y - run.VehicleStart.Y ) > 6000;
		if ( !moved )
			return;
		Require( level.TrackInstances.Tiles >= 8, "the track tiles are drawn" );
		CaptureFrame( "original-water-track.png" );
		var tilesBefore = level.WaterTracks!.Tracks.Count;
		var ride = run.Station;
		level.Objects.Remove( ride );
		Require( level.WaterTracks.Tracks.Count == 0 && tilesBefore > 0, "removing the ride clears its station and track tiles" );
		Require( level.Objects.ScriptWorld.Bumpers.Controllers.Count == 0, "removing the ride removes its controller" );
		Log.Trace( $"HUD water track: a boat ran ({(boat == null ? "lap complete" : $"{boat.X},{boat.Y}")}), the tiles and the boat were drawn, the ride and its track were removed." );
		run.Station = null;
		run.Done = true;
	}

	/// <summary>
	/// The kart phase: two karts are loaded through the controller's own commands (the script waits for a real guest, so the smoke plays the script's part), park on
	/// the grid, race one lap, end the race and unload; the frame with both karts on the track is captured, then the ride and its track are removed.
	/// </summary>
	private void UpdateKartPhase()
	{
		var run = kartSmoke;
		if ( run.Station == null || run.Done )
			return;
		var bump = run.Station.Runtime.Bump!;
		Require( run.Wait!.Elapsed.TotalSeconds < 180, $"the kart race runs (phase {bump.Phase}, karts {bump.VehicleCount}, runs {bump.CompletedRuns}, stage {run.Phase})" );
		switch ( run.Phase )
		{
			case 0:
				// the script has opened the ride for loading once the circuit is valid
				if ( bump.Phase != BumpController.PhaseLoading )
					return;
				Require( bump.Command( 1, 7001 ) == 1 && bump.Command( 4, 0 ) == 1, "the first kart is created with its rider" );
				Require( bump.Command( 1, 7002 ) == 1 && bump.Command( 4, 0 ) == 1, "the second kart is created with its rider" );
				Require( bump.VehicleCount == 2 && bump.Vehicles[0].TargetBuoy == 0 && bump.Vehicles[1].TargetBuoy == 1, "the karts aim at the first two grid buoys" );
				run.PeakKarts = bump.VehicleCount;
				run.Wait.Restart();
				run.Phase = 1;
				return;
			case 1:
				// both karts park on the grid
				if ( !bump.Vehicles.All( kart => (kart.Flags & BumpVehicle.Slowed) != 0 ) )
					return;
				Require( level.TrackInstances.Tiles >= 8 && level.TrackInstances.Boats == 2, $"the renderer holds the track tiles and both karts ({level.TrackInstances})" );
				bump.Command( 14, 1 );
				bump.Command( 3, 0 );
				Require( bump.Phase == BumpController.PhaseRunning, "BUMP 3 starts the race" );
				run.VehicleStart = (bump.Vehicles[0].X, bump.Vehicles[0].Y);
				run.Wait.Restart();
				run.Phase = 2;
				return;
			case 2:
				// a few seconds into the race the karts are on the circuit: capture them
				if ( !run.Captured && Math.Abs( bump.Vehicles.FirstOrDefault()?.X - run.VehicleStart.X ?? 0 ) + Math.Abs( bump.Vehicles.FirstOrDefault()?.Y - run.VehicleStart.Y ?? 0 ) > 8000 )
				{
					run.Captured = true;
					CaptureFrame( "original-kart-track.png" );
				}
				if ( bump.Phase != BumpController.PhaseLoading || bump.VehicleCount != 0 )
					return;
				Require( run.Captured, "the karts were seen on the circuit before the race ended" );
				Require( bump.CompletedRuns == 2, $"both karts finished and unloaded ({bump.CompletedRuns})" );
				Require( bump.Command( 2, 0 ) != 0 && bump.Command( 2, 0 ) != 0 && bump.Command( 2, 0 ) == 0, "both riders were handed back" );
				var tilesBefore = level.KartTracks!.Tracks.Count;
				var ride = run.Station;
				level.Objects.Remove( ride );
				Require( level.KartTracks.Tracks.Count == 0 && tilesBefore > 0, "removing the ride clears its station and track tiles" );
				Require( level.Objects.ScriptWorld.Bumpers.Controllers.Count == 0, "removing the ride removes its controller" );
				Log.Trace( $"HUD kart track: {run.PeakKarts} karts parked on the grid, raced a lap, ended the race and unloaded; the tiles and the karts were drawn, the ride and its track were removed." );
				run.Station = null;
				run.Done = true;
				return;
		}
	}
}
