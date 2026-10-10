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
		var viewport = new System.Numerics.Vector2( canvas.Width, canvas.Height );
		bool Clickable( (int X, int Y) cell, out System.Numerics.Vector2 point ) =>
			level.TryProjectCell( cell.X, cell.Y, viewport, out point )
			&& point.X > canvas.Width * 0.05f && point.X < canvas.Width * 0.95f && point.Y > canvas.Height * 0.05f && point.Y < canvas.Height * 0.95f
			&& !hud.Stack.Covers( canvas, point )
			&& level.TryGetGridCell( point, viewport, out var x, out var y ) && (x, y) == cell;
		var grid = level.Objects.Grid;
		(int X, int Y)? anchor = null;
		for ( var index = 0; index < grid.Width * grid.Height && anchor == null; index++ )
		{
			var candidate = (X: index % grid.Width, Y: index / grid.Width);
			if ( level.Objects.Check( entry!, candidate.X, candidate.Y, 0 ) != OriginalPlacementResult.Allowed )
				continue;
			if ( CoasterBuilder.StationEnds( entry!.Shape, candidate.X, candidate.Y, 0 ) is not { } ends )
				continue;
			var cells = SmokeCircuit.Select( offset => (ends.Depart.X + offset.X, ends.Depart.Y + offset.Y) ).Append( ends.Return ).ToArray();
			if ( cells.All( cell => level.Objects.FindAt( cell.Item1, cell.Item2 ) == null && level.Objects.Grid.CheckTerrain( cell.Item1, cell.Item2 ) == OriginalPlacementResult.Allowed && Clickable( cell, out _ ) ) )
				anchor = candidate;
		}
		Require( anchor != null, "find an on-screen site for a coaster and its circuit" );
		var economy = level.Park?.Economy;
		var station = level.PlaceObject( entry!, anchor!.Value.X + 1, anchor.Value.Y + 1, 0 ) is { } bought && bought.Placement.X == anchor.Value.X ? bought : null;
		// Without the coaster researched the station is placed without a purchase; the track is still charged.
		station ??= level.Objects.TryPlace( entry!, anchor.Value.X, anchor.Value.Y, 0, out _ );
		Require( station?.Runtime.Coaster is not null, "place a coaster station with its controller" );
		smokeStation = station;
		Require( level.EnterCoasterTool( station! ) && level.IsCoasterToolActive, "the coaster tool starts on the station" );
		var builder = level.CoasterTool!;
		var depart = builder.Track.DepartCell!.Value;
		var balance = economy?.Balance ?? 0;
		void Press( System.Numerics.Vector2 point )
		{
			hud.Update( hudContext, new UiInput( point, true, true, false, false, UiKeys.None ) );
			hud.Update( hudContext, new UiInput( point, false, false, true, false, UiKeys.None ) );
		}
		// Three wheel notches raise the next pylons by 30 units.
		var height = builder.Height;
		for ( var notch = 0; notch < 3; notch++ )
			hud.Update( hudContext, new UiInput( new System.Numerics.Vector2( -1, -1 ), false, false, false, false, UiKeys.None, 1 ) );
		Require( builder.Height == height + 3 * OpenTPW.Hud.ParkHud.CoasterHeightStep, "the mouse wheel sets the pylon height" );
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
		var rating = builder.Ride.Rating!.Value;
		Require( rating.Excitement is >= 0 and <= 100 && rating.Trains >= 1, "the completed coaster is rated" );
		coasterBuiltFrame = frame;
		smokeTrainStart = builder.Ride.Trains[0].Position;
		Log.Trace( $"HUD coaster tool: {entry!.DisplayName} with {builder.Track.PylonCount} pylons for ${builder.Ride.PaidCost}, {builder.Ride.Samples.Count} samples, excitement {rating.Excitement:F1}, sickness {rating.Sickness:F1}, {rating.Trains} train(s)." );
	}

	/// <summary>A few seconds later: a train left the station and the renderer drew the track; then the track and station are removed.</summary>
	private void VerifyCoasterRuns()
	{
		var ride = smokeStation!.Runtime.Coaster!;
		var train = ride.Trains[0];
		Require( train.State != CoasterTrainState.Boarding || train.Position != smokeTrainStart, $"a coaster train left the station (state {train.State}, position {train.Position:F1})" );
		Require( level.CoasterTrianglesDrawn > 0, "the coaster track is drawn" );
		CaptureFrame( "original-coaster.png" );
		Require( level.DeleteCoasterTrack( smokeStation ) && !ride.IsOperational, "delete the coaster track" );
		level.Objects.Remove( smokeStation );
		Require( !level.Coasters.Contains( ride ), "removing the station removes its coaster" );
		smokeStation = null;
		Log.Trace( "HUD coaster: a train ran, the track was drawn, deleted and the station removed." );
	}

	/// <summary>Per frame after the build: waits (up to 60 s) for a train to leave the station, then checks and removes the coaster.</summary>
	private void UpdateCoasterPhase()
	{
		if ( smokeStation == null || coasterBuiltFrame == 0 )
			return;
		var train = smokeStation.Runtime.Coaster!.Trains[0];
		if ( train.State == CoasterTrainState.Boarding && train.Position == smokeTrainStart )
		{
			coasterWait ??= System.Diagnostics.Stopwatch.StartNew();
			Require( coasterWait.Elapsed.TotalSeconds < 60, "a coaster train leaves the station" );
			return;
		}
		VerifyCoasterRuns();
	}

	private System.Diagnostics.Stopwatch? coasterWait;
}
