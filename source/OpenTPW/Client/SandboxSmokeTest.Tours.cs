namespace OpenTPW;

/// <summary>
/// The tour-ride phase of the native smoke test (docs/TOURS.md): the Jungle's Jurassic Tours is placed on a free on-screen site, the real <c>TourRide.RSE</c> opens it
/// and creates its controller, two real guests are sent to it and board through TOUR 3, and when a car is in flight with riders the simulation is frozen, the camera
/// looks at the car and the pixels are read back with the cars shown and hidden (they must differ); then the ride is closed, the simulation runs on until the car has
/// landed and the riders were handed back through TOUR 4, and the ride is removed.
/// </summary>
internal sealed partial class SandboxSmokeTest
{
	private sealed class TourSmokeRun
	{
		public bool Started;
		public bool Done;
		public int Stage;
		public OriginalObject? Ride;
		public System.Diagnostics.Stopwatch? Wait;
		public List<Guest> Guests = new();
		public Vector3 Target;
		public int Attempt;
		public byte[]? Visible;
		public byte[]? Hidden;
		public CameraMode? BeforeCamera;
		public Action? BeforeUpdate;
		public Action? Freeze;
		public int Departures;
		public bool Closed;
		public double ClosedAt;
	}

	/// <summary>The tour ride's state for a failure message: the bridge's and the controller's rider counts, every live car and the script's visitor variables.</summary>
	private static string TourDiagnostics( OriginalObject ride )
	{
		var tour = ride.Runtime.Tour;
		var vm = ride.Runtime.Script!;
		int Var( string name ) => vm.GetVariableIndex( name ) is var index and >= 0 ? vm.Variables[index] : -999;
		var cars = tour == null ? "none" : string.Join( ",", tour.Cars.Where( car => car.State != TourCar.StateFree ).Select( car => $"state {car.State} riders {car.RiderCount} timer {car.Timer}{(car.Leaving ? " leaving" : "")}" ) );
		return $"guests boarded {ride.Visitors.BoardedTotal} released {ride.Visitors.ReleasedTotal}; controller boarded {tour?.Boarded} released {tour?.Released} aboard {tour?.Riders} departures {tour?.Departures} ready {tour?.Ready} open {tour?.IsOpen} cars {tour?.CarCount} landing reserved {tour?.LandingReserved} [{cars}]; script at {vm.ProgramCounter} {vm.State}{(vm.InCriticalSection ? " critical" : "")}, VAR_RIDECLOSED {Var( "VAR_RIDECLOSED" )} VAR_LETMEON {Var( "VAR_LETMEON" )} VAR_LETMEOFF {Var( "VAR_LETMEOFF" )} VAR_INCAR {Var( "VAR_INCAR" )}";
	}

	private readonly TourSmokeRun tourSmoke = new();

	/// <summary>Whether the tour phase is finished or never started (the smoke test completes after it).</summary>
	private bool TourSmokeSettled => tourSmoke.Done || level.OriginalPark == null || (!tourSmoke.Started && bumperSmoke.Done && TourEntry() == null);

	private ObjectCatalogEntry? TourEntry() => level.Objects.Catalog.Entries.Where( item => item.Category == ObjectCategory.Ride && item.ScriptPath != null
		&& item.Settings.GetInt( "Bumper.WhichTrackType" ) == 0 && item.ScriptPath.Contains( "tour", StringComparison.OrdinalIgnoreCase ) ).OrderBy( item => item.InfoId ).FirstOrDefault();

	private void UpdateTourPhase()
	{
		var run = tourSmoke;
		if ( run.Done || hud == null || hudContext == null || level.OriginalPark == null || level.Guests == null )
			return;
		if ( !run.Started )
		{
			if ( !bumperSmoke.Done || frame < 6 || TourEntry() == null )
				return;
			StartTourSmoke( run );
			return;
		}
		var ride = run.Ride!;
		var tour = ride.Runtime.Tour;
		Require( run.Wait!.Elapsed.TotalSeconds < 400, $"the tour phase finishes (stage {run.Stage}, {TourDiagnostics( ride )})" );
		var renderer = level.TourRenderer;
		switch ( run.Stage )
		{
			case 0:
			{
				// a car is on its tour with a rider aboard: freeze the world and look at it
				if ( tour == null || renderer == null || !tour.Cars.Any( car => car.State == TourCar.StateTour && car.RiderCount > 0 ) )
					return;
				Require( level.TourCarInstances > 0, $"the renderer holds the flying car ({level.TourCarInstances})" );
				if ( renderer.FlyingCarPosition() is not { } target )
					return;
				run.Target = target;
				run.BeforeCamera = Camera.Mode;
				run.BeforeUpdate = Render.OnUpdate;
				Render.OnUpdate = null;
				var frozen = Time.Now;
				run.Freeze = () => { Time.Now = frozen; Time.Delta = 0; };
				Render.PreUpdate += run.Freeze;
				SetTourCamera( run );
				run.Stage = 1;
				return;
			}
			case 1:
				run.Visible = CaptureFrame( "original-tour-car.png" ).Pixels;
				renderer!.Visible = false;
				run.Stage = 2;
				return;
			case 2:
				run.Hidden = CaptureFrame( "original-tour-car-hidden.png" ).Pixels;
				renderer!.Visible = true;
				var changed = 0;
				for ( var index = 0; index + 2 < run.Visible!.Length && index + 2 < run.Hidden.Length; index += 4 )
					if ( !run.Visible.AsSpan( index, 3 ).SequenceEqual( run.Hidden.AsSpan( index, 3 ) ) )
						changed++;
				if ( changed == 0 && ++run.Attempt < 4 )
				{
					// the car may sit behind scenery from this side: try another view of the same frozen pose
					SetTourCamera( run );
					run.Stage = 1;
					return;
				}
				Require( changed > 0, "the flying car changes GPU pixels on the frozen frame" );
				Log.Trace( $"Tour car GPU readback: {ride.Entry.DisplayName}, {changed} changed pixels with the cars shown against hidden, car at {run.Target}." );
				Render.PreUpdate -= run.Freeze;
				Render.OnUpdate = run.BeforeUpdate;
				Camera.Mode = run.BeforeCamera!;
				Camera.Update();
				run.Departures = tour!.Departures;
				run.Stage = 3;
				return;
			case 3:
				if ( !run.Closed )
				{
					// Close the ride, as a player ends a tour ride's day. While it is open every landing hands a waiting guest to the unloaded car, which leaves again with
					// riders aboard (a full cycle is about 75 s of flight, return, approach and loading), so "everybody released" depends on the queue. Closing ends the flight
					// (TOUR 14 0 zeroes the timers), the car lands, the script unloads it and boards nobody (the loading loop leaves on VAR_RIDECLOSED), and the queue is sent away.
					// A car docked after TOUR 11 but before its lift-off would stay there with its riders (docs/TOURS.md, closing the ride), so wait while one stands there unready.
					if ( tour == null || tour.DockedCar != null && !tour.Ready )
						return;
					ride.Close();
					run.Closed = true;
					run.ClosedAt = run.Wait.Elapsed.TotalSeconds;
				}
				Require( run.Wait.Elapsed.TotalSeconds - run.ClosedAt < 120, $"the closed tour ride lands and releases its riders ({TourDiagnostics( ride )})" );
				// the car lands and every rider is handed back
				if ( tour == null || tour.Boarded == 0 || tour.Riders != 0 || tour.Released != tour.Boarded || ride.Visitors.BoardedTotal != ride.Visitors.ReleasedTotal || !tour.Cars.Any( car => car.State == TourCar.StateDocked ) )
					return;
				Require( ride.Visitors.BoardedTotal == ride.Visitors.ReleasedTotal, $"the guests were released ({ride.Visitors.BoardedTotal}/{ride.Visitors.ReleasedTotal})" );
				// a spawned guest that wandered off to another ride (for example Aztec Mayhem) is legitimately Using that one: only the tour ride's own guests count
				Require( !run.Guests.Any( guest => guest.State == GuestState.Using && guest.AttractionId == ride.Runtime.AttractionId ), "no spawned guest is stuck in Using the tour ride" );
				Require( ride.Runtime.Script!.UnimplementedEffects.GetValueOrDefault( Opcode.TOUR ) == 0, "every TOUR command reached the controller" );
				level.Objects.Remove( ride );
				Require( level.Objects.ScriptWorld.Tours.Controllers.Count == 0, "removing the ride removes its controller" );
				Log.Trace( $"HUD tour ride: {tour.Boarded} rider(s) boarded, flew and were released through TOUR 3 and 4 after {tour.Departures} departure(s), {run.Wait.Elapsed.TotalSeconds - run.ClosedAt:F0} s after the ride closed; the car was drawn and read back from the GPU, the ride was removed." );
				run.Done = true;
				return;
		}
	}

	private void SetTourCamera( TourSmokeRun run )
	{
		var angle = run.Attempt * MathF.PI / 2 + MathF.PI / 4;
		var offset = new Vector3( MathF.Cos( angle ) * 12, MathF.Sin( angle ) * 12, 6 );
		var position = run.Target + offset;
		Camera.Mode = new CameraMode { Position = position, Rotation = Rotation.LookAt( run.Target - position ), FieldOfView = 55 };
		Camera.Update();
	}

	private void StartTourSmoke( TourSmokeRun run )
	{
		run.Started = true;
		var entry = TourEntry()!;
		var canvas = hudContext!.Canvas;
		var viewport = new Vector2( canvas.Width, canvas.Height );
		bool OnScreen( (int X, int Y) cell ) =>
			level.TryProjectCell( cell.X, cell.Y, viewport, out var point ) && point.X > canvas.Width * 0.05f && point.X < canvas.Width * 0.95f && point.Y > canvas.Height * 0.05f && point.Y < canvas.Height * 0.95f;
		var grid = level.Objects.Grid;
		bool Free( int x, int y ) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height
			&& level.Objects.FindAt( x, y ) == null && grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed
			&& level.Guests!.Grid.Cells.RawTypeAt( x, y ) == 0 && !level.Guests!.Grid.Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );
		(int X, int Y)? centre = null;
		for ( var index = 0; index < grid.Width * grid.Height && centre == null; index++ )
		{
			var cell = (X: index % grid.Width, Y: index / grid.Width);
			var anchor = Level.GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			if ( level.Objects.CheckFree( entry, anchor.X, anchor.Y, 0 ) != OriginalPlacementResult.Allowed || !OnScreen( cell ) )
				continue;
			var cells = Enumerable.Range( anchor.X - 1, 7 ).SelectMany( x => Enumerable.Range( anchor.Y - 1, 7 ).Select( y => (X: x, Y: y) ) );
			if ( cells.All( item => Free( item.X, item.Y ) ) && Level.ResolveVisitorCells( ObjectFootprint.GetAccessPoints( entry.Shape, anchor.X, anchor.Y, 0 ), level.Guests!.Grid ) != null )
				centre = cell;
		}
		Require( centre != null, "find an on-screen site for the tour ride" );
		var ride = level.PlaceObject( entry, centre!.Value.X, centre.Value.Y, 0 ) ?? level.Objects.TryPlace( entry, Level.GetCentredAnchor( entry, centre.Value.X, centre.Value.Y, 0 ).X, Level.GetCentredAnchor( entry, centre.Value.X, centre.Value.Y, 0 ).Y, 0, out _ );
		Require( ride?.Runtime.UsesTour == true, "place the tour ride" );
		// a ride the fixture placed through the fallback has no access cells, which a player's placement lays (ObjectAccessCells): without the entry's link the ride has no front and no guest chooses or joins it (tp.exe 0x4DE040, 0x4FCB84)
		if ( ride!.Visitors.IsZeroFront )
		{
			ObjectAccessCells.Lay( level.Guests!.Grid.Cells, ride.AccessPoints, Level.OwnerWord( ride ), ride.Entry.HasQueue );
			ride.Visitors.RecomputeQueue( level.Guests.Grid );
		}
		// its queue has to end on a path for a guest to reach it (tp.exe 0x4DE4A0): the fixture lays the one path cell the player lays and ends the queue on it
		if ( ride.Visitors.JoinCell == null && !QueuePaths.TryConnect( level.Guests!.Grid, ride.Visitors ) )
		{
			var queueBack = ride.Visitors.QueueBackCell;
			var pathCell = GuestPathGrid.Directions.Select( step => (X: queueBack.X + step.DX, Y: queueBack.Y + step.DY) ).FirstOrDefault( cell => level.Paths!.CheckCell( cell.X, cell.Y ) == CellBuildResult.Ok );
			Require( pathCell != default && level.Paths!.BuildSegment( pathCell, pathCell ).Built.Count > 0, $"lay a path cell beside the tour queue's back {queueBack}" );
			Require( QueuePaths.TryConnect( level.Guests.Grid, ride.Visitors ), "the tour queue ends on the path" );
		}
		// a built ride with a queue starts closed (RIDES-015, 0x4DB712): the fixture opens it as the player would
		ride!.Open();
		run.Ride = ride;
		Require( ride!.Runtime.Tour == null, "no controller before the script ran TOUR 1" );
		var entrance = ride.Visitors.JoinCell ?? ride.Visitors.EntranceCell;
		for ( var count = 0; count < 2; count++ )
		{
			var guest = level.Guests!.SpawnInPark( entrance.X, entrance.Y );
			guest.AttractionId = ride.Visitors.AttractionId;
			guest.State = GuestState.GoingToRide;
			run.Guests.Add( guest );
		}
		run.Wait = System.Diagnostics.Stopwatch.StartNew();
		Log.Trace( $"HUD tour ride: {entry.DisplayName} placed at {centre.Value}, two guests sent to it." );
	}
}
