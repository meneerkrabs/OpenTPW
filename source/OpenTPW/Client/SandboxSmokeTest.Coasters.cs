using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>Coaster-tool check of the native smoke test: the park HUD builds, closes and completes a small coaster from synthetic clicks.</summary>
internal sealed partial class SandboxSmokeTest
{
	/// <summary>Pylon offsets from the station's '&gt;' cell (the circuit of CoasterTests, which passes every window).</summary>
	private static readonly (int X, int Y)[] SmokeCircuit = { (4, 0), (7, 4), (4, 8), (-4, 8), (-8, 4), (-7, 0) };
	private bool coasterToolVerified;
	private int coasterBuiltFrame;
	private OriginalObject? smokeStation;
	private float smokeTrainStart;

	/// <summary>
	/// Original levels with the HUD: a coaster station is placed on a site whose circuit cells are free and on screen,
	/// the coaster tool starts on it, and HUD clicks place six pylons (each charged at once), close the circuit on the
	/// station end and complete it; the track is rated and drawn. A later frame checks that a train moved.
	/// </summary>
	private void VerifyHudCoasterTool()
	{
		coasterToolVerified = true;
		if ( hud == null || hudContext == null || level.OriginalPark == null )
			return;
		var entry = level.Objects.Catalog.Buildable.Where( item => CoasterDefinition.IsCoaster( item.Settings ) ).OrderBy( item => item.InfoId ).FirstOrDefault();
		Require( entry != null, "the theme catalogue has a coaster station" );
		var canvas = hudContext.Canvas;
		bool Clickable( (int X, int Y) cell, out System.Numerics.Vector2 point ) => TryClickPoint( cell, out point );
		var grid = level.Objects.Grid;
		(int X, int Y)? anchor = null;
		// nearest to the paths' centre first, where the camera looked before it scrolls to a site
		var focusCell = OriginalParkPlacement.TryGetCell( level.OriginalPark.Heightfield, level.PathFocus.X, level.PathFocus.Y, out var focusX, out var focusY ) ? (X: focusX, Y: focusY) : (X: 0, Y: 0);
		foreach ( var candidate in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - focusCell.X ) + Math.Abs( cell.Y - focusCell.Y ) ) )
		{
			if ( anchor != null )
				break;
			if ( level.Objects.CheckFree( entry!, candidate.X, candidate.Y, 0 ) != OriginalPlacementResult.Allowed )
				continue;
			if ( CoasterBuilder.StationEnds( entry!.Shape, candidate.X, candidate.Y, 0 ) is not { } ends )
				continue;
			var cells = SmokeCircuit.Select( offset => (ends.Depart.X + offset.X, ends.Depart.Y + offset.Y) ).Append( ends.Return ).ToArray();
			if ( !cells.All( cell => level.Objects.FindAt( cell.Item1, cell.Item2 ) == null && level.Objects.Grid.CheckTerrain( cell.Item1, cell.Item2 ) == OriginalPlacementResult.Allowed
				&& !level.Guests!.Grid.Cells.FlagsAt( cell.Item1, cell.Item2 ).HasFlag( ParkCellFlags.Unowned ) ) )
				continue;
			// the circuit sits in the middle of the view, clear of the tool panel that opens at the bottom left; the test only reads the view (it scrolls nothing):
			// a site already centred in the current view passes as is, otherwise a player scrolls to the circuit's middle and the site is judged again there
			bool Centred( (int, int) cell ) => TryClickPointInView( cell, out var at ) && at.X > canvas.Width * 0.3f && at.Y < canvas.Height * 0.7f;
			if ( Camera.Mode is ParkCameraMode parkCamera && !cells.All( Centred ) )
			{
				var middle = OriginalParkPlacement.GetCellCenter( level.OriginalPark.Heightfield, (int)cells.Average( cell => cell.Item1 ), (int)cells.Average( cell => cell.Item2 ) );
				parkCamera.SetPose( new Vector3( middle.X, middle.Y, 0 ), parkCamera.Height, parkCamera.Yaw );
				Camera.Update();
			}
			if ( cells.All( Centred ) )
				anchor = candidate;
		}
		Require( anchor != null, "find an on-screen site for a coaster and its circuit" );
		var economy = level.Park?.Economy;
		var click = Level.GetClickCell( entry!, anchor!.Value.X, anchor.Value.Y, 0 );
		var station = level.PlaceObject( entry!, click.X, click.Y, 0 ) is { } bought && bought.Placement.X == anchor.Value.X ? bought : null;
		// Without the coaster researched the station is placed without a purchase; the track is still charged.
		station ??= level.Objects.TryPlace( entry!, anchor.Value.X, anchor.Value.Y, 0, out _ );
		Require( station?.Runtime.Coaster is not null, "place a coaster station with its controller" );
		smokeStation = station;
		Require( level.EnterCoasterTool( station! ) && level.IsCoasterToolActive, "the coaster tool starts on the station" );
		Require( !station!.IsOpen && station.Runtime.Coaster!.State == CoasterControllerState.Incomplete, "the tool closes the ride and an unfinished track holds controller state 1 (0x447520)" );
		var builder = level.CoasterTool!;
		var depart = builder.Track.DepartCell!.Value;
		var balance = economy?.Balance ?? 0;
		void Press( System.Numerics.Vector2 point ) => SmokeClick( point );
		var panel = level.CoasterPanel!;
		Require( panel.Selected == CoasterToolButton.Build && panel.IsEnabled( CoasterToolButton.Build ) && !panel.IsEnabled( CoasterToolButton.Demolish ), "the panel opens on the build button of an unfinished track (0x4455c0, 0x445910)" );
		foreach ( var (dx, dy) in SmokeCircuit )
		{
			Clickable( (depart.X + dx, depart.Y + dy), out var point );
			hud.Update( hudContext, UiInput.Idle( point ) );
			Require( hud.CoasterGhost is { Result: CoasterPlaceResult.Ok }, $"the coaster ghost accepts ({depart.X + dx}, {depart.Y + dy})" );
			var nodes = builder.Track.Nodes.Count;
			Press( point );
			Require( builder.Track.Nodes.Count == nodes + 1, "a HUD click places a pylon" );
		}
		if ( economy != null )
			Require( balance - economy.Balance == builder.Ride.PaidCost && builder.Ride.PaidCost > 0, "each pylon is charged trunc(cost) when placed" );
		Clickable( builder.Track.ReturnCell!.Value, out var stationPoint );
		Press( stationPoint );
		Require( builder.Track.IsClosed && !builder.Track.IsComplete, "a click on the station end closes the circuit" );
		Press( stationPoint );
		Require( builder.Track.IsRunnable && builder.Ride.IsOperational && !level.IsCoasterToolActive, "a second click completes the coaster" );
		Require( station!.IsOpen && builder.Ride.State == CoasterControllerState.Built, "leaving the tool opens the ride again; a finished coaster is built (state 2) until its script opens it" );
		Require( builder.Track.Nodes.Count == CoasterTrack.StationNodeCount + SmokeCircuit.Length + 1 && builder.Track.Nodes[^1].IsStationEnd, "the station's three nodes, the pylons and the end node" );
		var rating = builder.Ride.Rating!.Value;
		Require( rating.Excitement is >= 0 and <= 100 && rating.Trains >= 1, "the completed coaster is rated" );
		coasterBuiltFrame = frame;
		smokeTrainStart = builder.Ride.Trains[0].Position;
		Log.Trace( $"HUD coaster tool: {entry!.DisplayName} with {builder.Track.PylonCount} pylons for ${builder.Ride.PaidCost}, {builder.Ride.Samples.Count} samples, excitement {rating.Excitement:F1}, sickness {rating.Sickness:F1}, {rating.Trains} train(s)." );
	}

	/// <summary>The screen point of a cell's centre when the pointer there reaches the park: on screen, under no HUD element, picking that very cell.</summary>
	private bool TryClickPoint( (int X, int Y) cell, out System.Numerics.Vector2 point )
	{
		if ( TryClickPointInView( cell, out point ) )
			return true;
		// a player scrolls to the cell they want to click
		if ( level.OriginalPark != null && Camera.Mode is ParkCameraMode parkCamera )
		{
			var centre = OriginalParkPlacement.GetCellCenter( level.OriginalPark.Heightfield, cell.X, cell.Y );
			// a window may cover the view's middle: look slightly past the cell until it is free of the interface
			foreach ( var (dx, dy) in new[] { (0f, 0f), (8f, 0f), (-8f, 0f), (0f, 8f), (0f, -8f), (14f, 0f), (-14f, 0f), (0f, 14f), (0f, -14f), (8f, 8f), (-8f, -8f), (8f, -8f), (-8f, 8f) } )
			{
				parkCamera.SetPose( new Vector3( centre.X + dx, centre.Y + dy, 0 ), parkCamera.Height, parkCamera.Yaw );
				Camera.Update();
				if ( TryClickPointInView( cell, out point ) )
					return true;
			}
			return false;
		}
		return false;
	}

	private bool TryClickPointInView( (int X, int Y) cell, out System.Numerics.Vector2 point )
	{
		point = default;
		if ( hudContext == null || hud == null )
			return false;
		var canvas = hudContext.Canvas;
		var viewport = new System.Numerics.Vector2( canvas.Width, canvas.Height );
		return level.TryProjectCell( cell.X, cell.Y, viewport, out point )
			&& point.X > canvas.Width * 0.05f && point.X < canvas.Width * 0.95f && point.Y > canvas.Height * 0.05f && point.Y < canvas.Height * 0.95f
			&& !hud.Stack.Covers( canvas, point )
			&& level.TryGetGridCell( point, viewport, out var x, out var y ) && (x, y) == cell;
	}

	/// <summary>A left click of the HUD at a point: the press, then the release.</summary>
	private void SmokeClick( System.Numerics.Vector2 point )
	{
		hud!.Update( hudContext!, new UiInput( point, true, true, false, false, UiKeys.None ) );
		hud.Update( hudContext!, new UiInput( point, false, false, true, false, UiKeys.None ) );
	}

	/// <summary>A few seconds later: a train left the station and the renderer drew the track; then the track and station are removed.</summary>
	private void VerifyCoasterRuns()
	{
		var ride = smokeStation!.Runtime.Coaster!;
		var train = ride.Trains[0];
		Require( train.Position != smokeTrainStart, $"a coaster train left the station (position {train.Position:F1}, speed {train.Speed:F1})" );
		Require( level.CoasterTrianglesDrawn > 0, "the coaster track is drawn" );
		CaptureFrame( "original-coaster.png" );
		Require( ride.State is CoasterControllerState.Open or CoasterControllerState.Closed, "the station script opened the built coaster (COAST 5)" );
		// The way back into the tool in normal play: select the built coaster, press the ride window's track button, cut the circuit back at a pylon through the panel, build the pylons
		// again, close on the station and complete; nobody boarded is lost.
		hud!.SelectObject( smokeStation );
		hud.Update( hudContext!, UiInput.Idle( new System.Numerics.Vector2( -1, -1 ) ) );
		var trackButton = (UiButton)hud.Screen.Find( "editTrack" )!;
		Require( trackButton.Visible && trackButton.Enabled, "the selected built coaster shows its track button" );
		var buttonRect = trackButton.ScreenRect( hudContext!.Canvas );
		SmokeClick( new System.Numerics.Vector2( buttonRect.X + buttonRect.Width / 2, buttonRect.Y + buttonRect.Height / 2 ) );
		Require( level.IsCoasterToolActive && level.CoasterPanel is { Selected: CoasterToolButton.Height }, "the track button opens the coaster tool on the finished track (height button)" );
		var routeTrack = level.CoasterTool!.Track;
		var depart = routeTrack.DepartCell!.Value;
		var cutIndex = CoasterTrack.StationNodeCount + 2;
		var removed = SmokeCircuit.Skip( 3 ).Select( offset => (X: depart.X + offset.X, Y: depart.Y + offset.Y) ).ToArray();
		Require( level.CoasterPanel!.Select( CoasterToolButton.Demolish ), "the demolish button is enabled on the finished circuit" );
		foreach ( var cell in new[] { (routeTrack.Nodes[cutIndex].X, routeTrack.Nodes[cutIndex].Y), (routeTrack.Nodes[0].X, routeTrack.Nodes[0].Y) } )
		{
			Require( TryClickPoint( cell, out var cutPoint ), $"the pylon cell ({cell.Item1}, {cell.Item2}) can be clicked" );
			hud.Update( hudContext!, UiInput.Idle( cutPoint ) );
			SmokeClick( cutPoint );
		}
		Require( !routeTrack.IsComplete && routeTrack.Nodes.Count == cutIndex + 1 && ride.State == CoasterControllerState.Incomplete && level.CoasterPanel.Selected == CoasterToolButton.Build, "the cut to S0 reopened the circuit at the third pylon" );
		foreach ( var cell in removed )
		{
			Require( TryClickPoint( cell, out var buildPoint ), $"the cell ({cell.X}, {cell.Y}) can be clicked" );
			hud.Update( hudContext!, UiInput.Idle( buildPoint ) );
			var count = routeTrack.Nodes.Count;
			SmokeClick( buildPoint );
			Require( routeTrack.Nodes.Count == count + 1, "a click places a pylon again" );
		}
		Require( TryClickPoint( routeTrack.ReturnCell!.Value, out var closePoint ), "the station end can be clicked" );
		SmokeClick( closePoint );
		Require( routeTrack.IsClosed, "a click on the station end closes the extended circuit" );
		SmokeClick( closePoint );
		Require( routeTrack.IsRunnable && ride.IsOperational && !level.IsCoasterToolActive, "a second click completes it again" );
		Require( ride.State is CoasterControllerState.Built or CoasterControllerState.Open or CoasterControllerState.Closed && smokeStation.IsOpen, "the extended coaster is back in service" );
		// Taking a finished coaster into the tool again closes the ride and puts it out of service without removing its trains; leaving it opens the ride.
		Require( level.EnterCoasterTool( smokeStation ) && !smokeStation.IsOpen && ride.State == CoasterControllerState.Built && ride.Trains.Count > 0 && ride.RidersOnBoard == 0, "the tool on a finished coaster closes the ride (state 2, trains stay, no riders)" );
		Require( ride.BeingEdited && !ride.AcceptsGuests, "the coaster being edited refuses guests while its track still runs (0x441970 against 0x790fec)" );
		level.ExitCoasterTool();
		Require( smokeStation.IsOpen && !ride.BeingEdited, "leaving the tool opens the ride again and it is no longer the coaster being edited" );
		var economy = level.Park?.Economy;
		var balance = economy?.Balance ?? 0;
		// The track is taken out through the panel: the tool on a finished coaster opens on the height button with the demolish button enabled; two clicks of the demolish button on the station's
		// departing and arriving pylons cut every pylon out and leave the unfinished station.
		Require( level.EnterCoasterTool( smokeStation ) && level.CoasterPanel != null, "the tool starts again on the finished coaster" );
		var panel = level.CoasterPanel!;
		Require( panel.Selected == CoasterToolButton.Height && panel.IsEnabled( CoasterToolButton.Demolish ) && !panel.IsEnabled( CoasterToolButton.Build ), "a finished circuit opens the panel on the height button and enables the demolish button" );
		Require( panel.Select( CoasterToolButton.Demolish ), "the demolish button is selected" );
		var builder = level.CoasterTool!;
		var track = builder.Track;
		var stationCells = new[] { (track.Nodes[2].X, track.Nodes[2].Y), (track.Nodes[0].X, track.Nodes[0].Y) };
		foreach ( var cell in stationCells )
		{
			Require( TryClickPoint( cell, out var point ), $"the station's pylon cell ({cell.Item1}, {cell.Item2}) can be clicked" );
			hud!.Update( hudContext!, UiInput.Idle( point ) );
			SmokeClick( point );
		}
		Require( !track.IsComplete && track.Nodes.Count == CoasterTrack.StationNodeCount && ride.State == CoasterControllerState.Incomplete && ride.Trains.Count == 0 && !ride.IsOperational,
			"the cut through the panel removed the pylons and the trains and left the unfinished station" );
		Require( panel.Selected == CoasterToolButton.Build, "the cut reselects the build button" );
		level.ExitCoasterTool();
		Require( (economy?.Balance ?? 0) == balance, "cutting the track refunds nothing" );
		level.Objects.Remove( smokeStation );
		Require( !level.Coasters.Contains( ride ), "removing the station removes its coaster" );
		smokeStation = null;
		Log.Trace( "HUD coaster: a train ran, the track was drawn, deleted and the station removed." );
	}

	/// <summary>Per frame after the build: waits (up to 60 s) for a train to leave the station, then checks and removes the coaster.</summary>
	private void UpdateCoasterPhase()
	{
		// the coaster checks enter the coaster tool (which ends a staff tool) and may move the camera: they wait while the staff phase runs, also between its
		// tools (after the placement, before the pick-up), whose drop point was projected from the camera when the phase began
		if ( smokeStation == null || coasterBuiltFrame == 0 || level.StaffPlacement is { IsActive: true } || staffStage is > 0 and < 20 )
			return;
		var ride = smokeStation.Runtime.Coaster!;
		var train = ride.Trains[0];
		// A built coaster (state 2) moves its trains at once; the station script opens it (COAST 5) a moment later.
		if ( train.Position == smokeTrainStart || ride.State == CoasterControllerState.Built )
		{
			coasterWait ??= System.Diagnostics.Stopwatch.StartNew();
			Require( coasterWait.Elapsed.TotalSeconds < 60, train.Position == smokeTrainStart ? "a coaster train leaves the station" : "the station script opens the built coaster (COAST 5)" );
			return;
		}
		VerifyCoasterRuns();
	}

	private System.Diagnostics.Stopwatch? coasterWait;
}
