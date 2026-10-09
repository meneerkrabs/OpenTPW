namespace OpenTPW;

/// <summary>Native acceptance of immutable shared visits, without running the sandbox build/save smoke.</summary>
internal sealed class VisitSmokeTest : IDisposable
{
	private readonly Level level;
	private readonly System.Threading.Timer watchdog;
	private int frame;
	private bool completed;

	public VisitSmokeTest( Level level )
	{
		this.level = level;
		Require( level.IsReadOnlyVisit && level.Park == null, "shared visit has no economy" );
		var originalObjects = level.Objects.Objects.ToArray();
		var originalRide = level.PlacedRide;
		var entry = level.Objects.Catalog.Buildable.FirstOrDefault();
		Require( entry != null, "local catalog contains a buildable object" );
		Require( level.PlaceObject( entry!, 0, 0, 0 ) == null, "object placement is rejected" );
		foreach ( var item in originalObjects )
			Require( !level.RemoveObjectAt( item.Placement.X, item.Placement.Y ), "existing object removal is rejected" );
		Require( !level.RemoveObjectAt( 0, 0 ), "remove tool is rejected" );
		Require( originalObjects.SequenceEqual( level.Objects.Objects ), "objects are unchanged" );
		Require( !level.PlaceRide( Vector3.Zero ), "prototype placement is rejected" );
		level.RemoveRide();
		Require( ReferenceEquals( originalRide, level.PlacedRide ), "prototype removal is rejected" );
		Reject( level.SaveSandbox, "saving is rejected" );
		Reject( level.LoadSandbox, "loading is rejected" );
		Reject( () => ParkSharing.ExportLevel( level, "Visit", "OpenTPW" ), "exporting a visited park is rejected" );
		watchdog = new System.Threading.Timer( _ =>
		{
			Console.Error.WriteLine( "Read-only visit smoke timed out before rendering." );
			Environment.Exit( 1 );
		}, null, TimeSpan.FromSeconds( 60 ), System.Threading.Timeout.InfiniteTimeSpan );
	}

	public void Update()
	{
		if ( ++frame < 8 || completed )
			return;
		Require( level.Park == null, "economy stays absent after simulation frames" );
		var capture = SandboxSmokeTest.CaptureFrame( "online-visit.png" );
		Require( capture.Width > 0 && capture.Height > 0, "visit renders a real framebuffer" );
		completed = true;
		Log.Trace( $"Read-only visit smoke passed: {frame} frames, {level.Objects.Objects.Count} immutable local objects, no economy, build/remove/save/load/export rejected, GPU capture {capture.Width}x{capture.Height}." );
		Render.Window.SdlWindow.Close();
	}

	private static void Reject( Action action, string step )
	{
		try { action(); }
		catch ( InvalidOperationException ) { return; }
		throw new InvalidOperationException( "Read-only visit smoke failed: " + step );
	}

	private static void Require( bool condition, string step )
	{
		if ( !condition )
			throw new InvalidOperationException( "Read-only visit smoke failed: " + step );
	}

	public void VerifyCompleted() => Require( completed, "rendering completed" );

	public void Dispose()
	{
		watchdog.Dispose();
		Render.PostUpdate -= Update;
	}
}
