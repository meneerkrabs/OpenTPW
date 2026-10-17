using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// Native smoke test of the experimental Theme Park Inc slice (`--load-original-level science --tpi-smoke-test`):
/// it drives the real HUD through the game loop's injected input — build window, arming a catalogue item, the
/// rotate cycle, the place click through <see cref="Level.UpdateWorldInput"/>, the queue tool a placed HasQueue
/// ride switches to, Escape's ordering (tool → armed item → window → pause menu), the speed control including
/// pause freezing the guests, the remove tool and the pause menu's Quit — and checks the capability gates hold in
/// the running game (the money economy only, no Theme Park World HUD, the experiment banner drawn). macOS TCC blocks
/// synthetic OS input, so clicks and keys are injected as <see cref="GameFlow.InjectedInput"/> frames, like the
/// front-end smoke test; the one input injection cannot reach (the world click reads the real mouse) goes through
/// Level's own world-input path, like the sandbox smoke test. Best run on `science`, the world with prebuilt paths.
/// </summary>
internal sealed class ThemeParkIncSmokeTest : IDisposable
{
	private const int MaximumFrames = 2400;
	private const int MaximumSeconds = 300;
	private const int SmokeQueueLength = 2;

	private readonly GameFlow flow;
	private readonly LinkedList<(string Name, Func<bool> Step)> steps = new();
	private readonly System.Threading.Timer watchdog;
	private int frame;
	private int waitUntil;
	private bool completed;
	/// <summary>True while <see cref="Update"/> runs the head step, so a step that spawns sub-steps inserts them after itself.</summary>
	private bool running;

	private Level Level => flow.Level!;
	private ThemeParkIncHud Hud => flow.IncHud!;
	private UiContext Context => flow.Context;

	// Site state filled by the placement search and asserted by later steps.
	private ObjectCatalogEntry? armedEntry;
	/// <summary>Set when the placed item entered the queue tool (ChooseItem prefers a HasQueue ride).</summary>
	private bool queueToolOn;
	private int chosenIndex;
	private (int X, int Y) site;
	private ObjectAccessPoint entrance;
	private System.Numerics.Vector2 sitePoint;
	private System.Numerics.Vector2 endPoint;
	private int objectsBefore;
	private long cashBeforePlace;
	private (int X, int Y)[] pausedCells = Array.Empty<(int, int)>();
	private int resumedAt;

	public ThemeParkIncSmokeTest( GameFlow flow )
	{
		this.flow = flow;
		flow.PersistSettings = false;
		watchdog = new System.Threading.Timer( _ =>
		{
			Console.Error.WriteLine( $"Theme Park Inc slice smoke test failed: timed out after {MaximumSeconds} s at frame {frame} (stuck at '{(steps.Count > 0 ? steps.First!.Value.Name : "end")}'); display {Render.Metrics}." );
			Environment.Exit( 1 );
		}, null, TimeSpan.FromSeconds( MaximumSeconds ), System.Threading.Timeout.InfiniteTimeSpan );
		Plan();
	}

	private void Plan()
	{
		Wait( "slice starts", 10 );
		Do( "the slice runs under its capability gates", () =>
		{
			var level = flow.Level;
			Require( flow.IncHud != null && flow.Hud == null && level != null, "the Theme Park Inc slice runs with its own HUD, not the Theme Park World one" );
			Require( level!.OriginalPark != null && level.Park is { Economy.Rules.ThemeParkInc: true } && level.IsThemeParkIncSlice, "the world loads with the Theme Park Inc money economy" );
			Require( GameEditions.Capabilities.Money && !GameEditions.Capabilities.Staff && !GameEditions.Capabilities.Research && !GameEditions.Capabilities.Challenges && GameEditions.Capabilities.ExperimentalHud && !GameEditions.Capabilities.ParkSaves, "the capability policy runs the money and refuses staff, research, challenges and saves at runtime" );
			Require( Context.Batch.TextDraws.Any( draw => draw.Text.StartsWith( "Cash $", StringComparison.Ordinal ) ), "the money line is drawn" );
			Require( LabelText( "title" ).Contains( "EXPERIMENTAL", StringComparison.Ordinal ), "the HUD names the experiment on screen" );
			Require( LabelText( "limits" ) == GameEditions.Capabilities.ExperimentalNote, "the HUD's limits line is the capability policy's note" );
			Require( Context.Batch.TextDraws.Any( draw => draw.Text.StartsWith( "Guests in park:", StringComparison.Ordinal ) ), "the guest counter is drawn" );
		} );
		Do( "open the build window", () => Click( Hud.Screen, "build" ) );
		Wait( "build window opens", 2 );
		Do( "choose a catalogue item", ChooseItem );
		Do( "arm the item through the list", ArmThroughList );
		Wait( "item arms", 2 );
		Do( "the item is armed and the window closed", () =>
		{
			Require( Level.BuildEntry == armedEntry && armedEntry != null, "activating the row arms the item's placement" );
			Require( Hud.Screen.Find( "buildList" )!.Visible == false, "arming closes the build window" );
			Require( Hud.Messages.Any( message => message.Contains( "click the park to place", StringComparison.Ordinal ) ), "the HUD says how to place" );
		} );
		for ( var turn = 1; turn <= 4; turn++ )
		{
			var expected = turn * 90 % 360;
			Do( $"rotate to {expected}", () => Click( Hud.Screen, "rotate" ) );
			Wait( "rotation applies", 1 );
			Do( $"rotation is {expected}", () => Require( Level.BuildRotation == expected, $"the Rotate button turns the build to {expected} degrees" ) );
		}
		Do( "find a site beside the paths", FindSite );
		Do( "focus the camera on the site", FocusSite );
		Wait( "camera settles", 3 );
		Do( "the site is clickable", () =>
		{
			Require( Clickable( site, out sitePoint ), "the site projects to a clickable point on screen" );
			if ( armedEntry!.HasQueue )
			{
				var (dx, dy) = (entrance.OutsideX - entrance.X, entrance.OutsideY - entrance.Y);
				var end = (X: entrance.OutsideX + dx * SmokeQueueLength, Y: entrance.OutsideY + dy * SmokeQueueLength);
				Require( Clickable( end, out endPoint ), "the queue line's end projects to a clickable point on screen" );
			}
		} );
		// The ghost follows the live hover, so it is asserted in the frame the injected hover runs; the next
		// real-mouse frame (no injection pending) moves the cursor off the site and clears it, as for a player.
		Do( "hover the site", () => flow.InjectedInput = UiInput.Idle( sitePoint ) );
		Do( "the ghost marks the footprint", () =>
		{
			var anchor = Level.GetCentredAnchor( armedEntry!, site.X, site.Y, 0 );
			var footprint = ObjectFootprint.GetCells( armedEntry!.Shape, anchor.X, anchor.Y, 0 ).Count();
			Require( Level.Blueprint is { } blueprint && blueprint.Cells.Count( cell => cell.Marker is BlueprintMarker.Allowed or BlueprintMarker.Front ) == footprint && blueprint.Cells.All( cell => cell.Marker != BlueprintMarker.Refused ),
				"hovering the site shows its footprint as allowed blueprint markers" );
		} );
		Do( "place through the world click", () =>
		{
			cashBeforePlace = Level.Park!.Economy.Balance;
			var viewport = Viewport;
			Level.UpdateWorldInput( false, false, sitePoint, viewport );
			Level.UpdateWorldInput( true, false, sitePoint, viewport );
		} );
		Wait( "placement settles", 2 );
		Do( "the object is placed and the queue tool follows", () =>
		{
			var placed = Level.Objects.FindAt( entrance.X, entrance.Y );
			Require( placed?.Entry == armedEntry && Level.Objects.Objects.Count == objectsBefore + 1, "the world click places the armed item" );
			Require( Level.LastActionMessage.StartsWith( "Built", StringComparison.Ordinal ) && Hud.Messages.Any( message => message.StartsWith( "Built", StringComparison.Ordinal ) ),
				"the level and the HUD both report the build" );
			Require( Level.Park is { } park && park.Economy.Catalog.TryGet( armedEntry!.InfoId, out var priced ) && priced.PurchaseCost > 0
				&& cashBeforePlace - park.Economy.Balance == priced.PurchaseCost, $"Level.PlaceObject debited exactly the purchase price once ({cashBeforePlace} -> {Level.Park?.Economy.Balance})" );
			queueToolOn = armedEntry!.HasQueue;
			if ( queueToolOn )
				Require( Level.CellTool.IsActive && Level.CellTool.Mode == CellToolMode.Queue && Level.CellTool.Start == (entrance.OutsideX, entrance.OutsideY),
					"placing a HasQueue ride starts the queue tool at its entrance" );
		} );
		// The queue tool steps are planned unconditionally and decide at run time (Plan() runs before the item is
		// chosen, so it cannot gate on the entry yet); ChooseItem prefers a HasQueue ride, so they normally run.
		Do( "hover the queue line", () => { if ( queueToolOn ) flow.InjectedInput = UiInput.Idle( endPoint ); } );
		Wait( "queue ghost shows", 2 );
		Do( "lay the queue", () => { if ( queueToolOn ) flow.InjectedInput = new UiInput( endPoint, false, true, false, false, UiKeys.None ); } );
		Wait( "queue lays", 2 );
		Do( "the queue cells are laid", () =>
		{
			if ( !queueToolOn )
				return;
			var (stepX, stepY) = (entrance.OutsideX - entrance.X, entrance.OutsideY - entrance.Y);
			var grid = Level.Guests!.Grid;
			Require( Enumerable.Range( 0, SmokeQueueLength + 1 ).All( step => grid.IsQueue( entrance.OutsideX + stepX * step, entrance.OutsideY + stepY * step ) ),
				"the tool click lays the queue cells out of the entrance" );
			Require( Level.Objects.FindAt( entrance.X, entrance.Y )!.Visitors.QueueCells.Count >= SmokeQueueLength + 1, "the ride owns its queue cells" );
		} );
		Do( "Escape ends the queue tool", () => { if ( queueToolOn ) flow.InjectedInput = UiInput.Key( UiKeys.Back ); } );
		Wait( "tool ends", 2 );
		Do( "the tool ended", () =>
		{
			if ( queueToolOn )
				Require( !Level.CellTool.IsActive && Level.Blueprint!.Cells.Count == 0, "Escape puts the queue tool away" );
		} );
		// Escape's ordering: the armed item first, then the open window, then the pause menu (arming closes the window).
		Do( "reopen the build window", () => Click( Hud.Screen, "build" ) );
		Wait( "window opens", 2 );
		Do( "arm the item again through the list", ArmThroughList );
		Wait( "armed", 2 );
		Do( "the item is armed again", () => Require( Level.BuildEntry == armedEntry, "the row activates again" ) );
		Do( "Escape disarms", () => flow.InjectedInput = UiInput.Key( UiKeys.Back ) );
		Wait( "disarmed", 2 );
		Do( "the item is disarmed", () => Require( Level.BuildEntry == null && !Hud.MenuOpen && !Hud.Screen.Find( "buildList" )!.Visible, "Escape puts the armed item away without opening anything" ) );
		Do( "open the window on its own", () => Click( Hud.Screen, "build" ) );
		Wait( "window opens", 2 );
		Do( "Escape closes the window", () => flow.InjectedInput = UiInput.Key( UiKeys.Back ) );
		Wait( "window closed", 2 );
		Do( "the window is closed", () => Require( !Hud.Screen.Find( "buildList" )!.Visible && !Hud.MenuOpen, "Escape closes the build window without opening the menu" ) );
		Do( "Escape opens the pause menu", () => flow.InjectedInput = UiInput.Key( UiKeys.Back ) );
		Wait( "menu opens", 2 );
		Do( "the pause menu names the experiment", () =>
		{
			Require( Hud.MenuOpen && Hud.Stack.Top?.Name == "tpiMenu", "the pause menu stands on the HUD" );
			Require( ( (UiLabel)Hud.Stack.Top!.Find( "limits" )! ).Text().Length > 0, "the pause menu repeats the slice's limits" );
		} );
		Do( "Escape resumes", () => flow.InjectedInput = UiInput.Key( UiKeys.Back ) );
		Wait( "menu closed", 2 );
		Do( "the menu is gone", () => Require( !Hud.MenuOpen, "Escape leaves the pause menu" ) );
		// Guests: the counter reads the live simulation, pause freezes it, normal speed runs it again.
		Do( "spawn guests near the ride", SpawnGuests );
		Wait( "guests appear", 2 );
		Do( "the counter counts them", () => Require( LabelText( "guests" ) == $"Guests in park: {Level.Guests!.GetStatistics().InPark}", "the guest counter reads the live guest simulation" ) );
		Do( "the speed button runs fast", () => Click( Hud.Screen, "speed" ) );
		Wait( "speed applies", 2 );
		Do( "double speed runs", () => Require( Hud.TimeScale == 2 && Level.SimulationTimeScale == 2, "the speed button doubles the simulation's time scale" ) );
		Do( "pause the game", () => flow.InjectedInput = UiInput.Key( UiKeys.Pause ) );
		Wait( "pause applies", 2 );
		Do( "pause freezes the guests", () =>
		{
			Require( Hud.TimeScale == 0 && Level.SimulationTimeScale == 0, "the paused speed scales the level's time to zero" );
			pausedCells = Level.Guests!.Guests.Select( guest => (guest.Cell.X, guest.Cell.Y) ).ToArray();
			Require( pausedCells.Length > 0, "the spawned guests are in the park" );
		} );
		Wait( "paused frames", 20 );
		Do( "guests stand still while paused", () =>
		{
			Require( Level.SimulationTimeScale == 0, "the level stays paused" );
			Require( Level.Guests!.Guests.Select( guest => (guest.Cell.X, guest.Cell.Y) ).SequenceEqual( pausedCells ), "no guest moves while paused" );
		} );
		Do( "resume normal speed", () => flow.InjectedInput = UiInput.Key( UiKeys.Pause ) );
		Wait( "speed applies", 2 );
		Do( "note the resume frame", () => resumedAt = frame );
		steps.AddLast( ("guests walk again", () =>
		{
			if ( Level.Guests!.Guests.Select( guest => (guest.Cell.X, guest.Cell.Y) ).SequenceEqual( pausedCells ) )
			{
				Require( frame - resumedAt < 300, "guests walk again within five seconds of resuming" );
				return false;
			}
			return true;
		}) );
		// Scripts that reach opcodes without handlers (WAITABS/MULT gained handlers on October 13, 2026;
		// RSE-VM.md's gap-opcode section) fault gracefully: the VM stops them with a logged message and
		// the slice keeps running.
		Do( "survey faulted scripts", () =>
		{
			var faulted = FaultedScripts().ToArray();
			Require( faulted.All( script => !string.IsNullOrEmpty( script.FaultMessage ) ), "every faulted script carries its fault message" );
			Log.Trace( $"Theme Park Inc slice smoke: {faulted.Length} of {Level.Objects.Objects.Count} object scripts stand faulted (WAITABS/MULT-class, logged by the VM); the park keeps running." );
		} );
		Wait( "scripts advance", 60 );
		Do( "faults stay graceful", () => Require( flow.IncHud != null && frame > 0, "script faults never take the slice down" ) );
		// The remove tool: arm it through the HUD, click the placed object away through the world click.
		Do( "arm the remove tool", () => Click( Hud.Screen, "remove" ) );
		Wait( "remove arms", 2 );
		Do( "the remove tool is on", () => Require( Level.IsRemovingObjects && Level.BuildEntry == null, "the remove tool is armed and disarms any build" ) );
		Do( "click the placed object away", () =>
		{
			var viewport = Viewport;
			Level.UpdateWorldInput( false, false, sitePoint, viewport );
			Level.UpdateWorldInput( true, false, sitePoint, viewport );
		} );
		Wait( "removal settles", 2 );
		Do( "the object is removed", () =>
		{
			Require( Level.Objects.Objects.Count == objectsBefore && Level.Objects.FindAt( entrance.X, entrance.Y ) == null, "the remove click takes the placed object away" );
			Require( Level.LastActionMessage.StartsWith( "Removed", StringComparison.Ordinal ), "the removal is reported" );
		} );
		Do( "cancel the tools", () => Click( Hud.Screen, "cancel" ) );
		Wait( "tools down", 2 );
		Do( "the tools are away", () => Require( !Level.IsRemovingObjects && !Level.CellTool.IsActive, "Cancel puts the remove tool away" ) );
		// The exit a player uses: the pause menu's Quit closes the window and ends the run.
		Do( "open the pause menu", () => flow.InjectedInput = UiInput.Key( UiKeys.Back ) );
		Wait( "menu opens", 2 );
		Do( "quit through the menu", () =>
		{
			Require( Hud.Stack.Top?.Name == "tpiMenu", "the pause menu is open" );
			completed = true;
			Click( Hud.Stack.Top!, "quit" );
		} );
	}

	private IEnumerable<RideVM> FaultedScripts() =>
		Level.Objects.Objects.Select( item => item.Runtime.Script ).OfType<RideVM>().Where( script => script.State == RideVMState.Faulted );

	/// <summary>Picks the item the build list arms: the first Rides entry with a queue and a script, else the first such entry of any category.</summary>
	private void ChooseItem()
	{
		var catalog = new OriginalBuildCatalog( Level.Objects.Catalog );
		BuildItem? chosen = null;
		foreach ( var category in new[] { BuildCategory.Rides }.Concat( Enum.GetValues<BuildCategory>() ) )
		{
			var items = catalog.GetItems( category );
			var candidate = items.FirstOrDefault( item => item.Entry is { HasQueue: true, ScriptPath: not null } ) ?? items.FirstOrDefault( item => item.Entry != null );
			if ( candidate?.Entry == null )
				continue;
			if ( category != Hud.Category )
				Click( Hud.Screen, $"category{category}" );
			(chosen, armedEntry, chosenIndex) = (candidate, candidate.Entry, items.ToList().IndexOf( candidate ));
			break;
		}
		Require( chosen != null, "the build window offers items of the world's own catalog" );
		Log.Trace( $"Theme Park Inc slice smoke: arming {armedEntry!.DisplayName} (index {chosenIndex}, queue {armedEntry.HasQueue})." );
	}

	/// <summary>
	/// Arms the chosen item with two injected clicks on its row: the first selects the row, the second activates it
	/// (<c>UiScrollList</c> semantics). Keyboard activation is not used — the real mouse keeps hovering between the
	/// injected frames and would take the focus off the list.
	/// </summary>
	private void ArmThroughList()
	{
		var list = (UiScrollList)Hud.Screen.Find( "buildList" )!;
		Require( list.Visible, "the build list is open" );
		list.Scroll( -100000 );
		var rect = list.ScreenRect( Context.Canvas );
		var scale = rect.Height / list.Bounds.Height;
		var visible = list.VisibleRows;
		var first = Math.Clamp( chosenIndex - visible + 1, 0, Math.Max( 0, list.Rows().Count - visible ) );
		list.Scroll( first );
		var row = new System.Numerics.Vector2( rect.X + rect.Width / 2, rect.Y + (list.Margin + (chosenIndex - first + 0.5f) * list.RowHeight) * scale );
		Require( list.RowAt( Context.Canvas, row ) == chosenIndex, "the chosen item's row is on screen" );
		Sub( "select the row", () => flow.InjectedInput = UiInput.Click( row ) );
		Sub( "activate the row", () => flow.InjectedInput = UiInput.Click( row ) );
	}

	/// <summary>A site whose footprint is allowed at rotation 0 and whose entrance looks onto a free straight queue line.</summary>
	private void FindSite()
	{
		objectsBefore = Level.Objects.Objects.Count;
		var entry = armedEntry!;
		var grid = Level.Guests!.Grid;
		for ( var index = 0; index < grid.CountX * grid.CountY; index++ )
		{
			var cell = (X: index % grid.CountX, Y: index / grid.CountX);
			if ( Level.CheckFreeObject( entry, cell.X, cell.Y, 0 ) != OriginalPlacementResult.Allowed )
				continue;
			var anchor = Level.GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			var door = ObjectFootprint.GetAccessPoints( entry.Shape, anchor.X, anchor.Y, 0 ).FirstOrDefault( point => point.Kind == ObjectCellKind.Entrance );
			var (dx, dy) = (door.OutsideX - door.X, door.OutsideY - door.Y);
			if ( door.Kind != ObjectCellKind.Entrance || Math.Abs( dx ) + Math.Abs( dy ) != 1 )
				continue;
			var line = Enumerable.Range( 0, SmokeQueueLength + 1 ).Select( step => (door.OutsideX + dx * step, door.OutsideY + dy * step) ).ToArray();
			if ( !line.All( queueCell => Level.Paths!.CheckCell( queueCell.Item1, queueCell.Item2 ) == CellBuildResult.Ok ) )
				continue;
			(site, entrance) = (cell, door);
			return;
		}
		Require( false, $"find a buildable site for {entry.DisplayName} with room for its queue" );
	}

	private void FocusSite()
	{
		var camera = Camera.Mode as ParkCameraMode;
		Require( camera != null, "the slice runs the park camera" );
		camera!.Focus( OriginalParkPlacement.GetCellCenter( Level.OriginalPark!.Heightfield, site.X, site.Y ) );
	}

	private void SpawnGuests()
	{
		// SpawnInPark needs walkable path cells (a laid queue cell is not one): the nearest ones to the entrance.
		var grid = Level.Guests!.Grid;
		var near = Enumerable.Range( 0, grid.CountX * grid.CountY )
			.Select( index => (X: index % grid.CountX, Y: index / grid.CountX) )
			.Where( cell => grid.IsWalkable( cell.X, cell.Y ) )
			.OrderBy( cell => Math.Max( Math.Abs( cell.X - entrance.OutsideX ), Math.Abs( cell.Y - entrance.OutsideY ) ) )
			.Take( 2 )
			.ToArray();
		Require( near.Length == 2, "find walkable path cells near the ride for the guests" );
		Level.Guests.SpawnInPark( near[0].X, near[0].Y );
		Level.Guests.SpawnInPark( near[1].X, near[1].Y );
	}

	private bool Clickable( (int X, int Y) cell, out System.Numerics.Vector2 point )
	{
		var viewport = Viewport;
		var canvas = Context.Canvas;
		return Level.TryProjectCell( cell.X, cell.Y, viewport, out point )
			&& point.X > canvas.Width * 0.05f && point.X < canvas.Width * 0.95f
			&& point.Y > canvas.Height * 0.05f && point.Y < canvas.Height * 0.95f
			&& !Hud.Stack.Covers( canvas, point )
			&& Level.TryGetGridCell( point, viewport, out var x, out var y ) && (x, y) == cell;
	}

	private System.Numerics.Vector2 Viewport => new( Context.Canvas.Width, Context.Canvas.Height );

	private string LabelText( string id ) => ( (UiLabel)Hud.Screen.Find( id )! ).Text();

	private void Click( UiScreen screen, string id )
	{
		var element = screen.Find( id );
		Require( element != null && element.Visible, $"element '{id}' exists on screen '{screen.Name}'" );
		flow.InjectedInput = UiInput.Click( element!.ScreenRect( Context.Canvas ).Center );
	}

	public void Update()
	{
		++frame;
		Require( frame < MaximumFrames, $"finish within {MaximumFrames} frames (stuck at '{(steps.Count > 0 ? steps.First!.Value.Name : "end")}')" );
		running = true;
		while ( steps.Count > 0 && steps.First!.Value.Step() )
		{
			steps.RemoveFirst();
			if ( flow.InjectedInput != null )
				break;
		}
		running = false;
		// Between action frames the HUD would see the real OS mouse (macOS TCC leaves it hovering somewhere over
		// the window): every frame without a step input is a deliberate no-input frame instead, so the run is
		// deterministic and only the steps act.
		flow.InjectedInput ??= UiInput.Idle( new System.Numerics.Vector2( -1, -1 ) );
	}

	public void VerifyCompleted() => Require( completed, "complete the Theme Park Inc slice smoke test" );

	private void Wait( string name, int frames ) => steps.AddLast( (name, () =>
	{
		if ( waitUntil == 0 )
			waitUntil = frame + frames;
		if ( frame < waitUntil )
			return false;
		waitUntil = 0;
		return true;
	}) );

	private void Do( string name, Action action ) => steps.AddLast( (name, () => { action(); return true; }) );

	/// <summary>A step's own sub-steps: while a step runs they follow it, not the rest of the plan.</summary>
	private void Sub( string name, Action action )
	{
		var step = (name, (Func<bool>)(() => { action(); return true; }));
		if ( running && steps.First != null )
			steps.AddAfter( steps.First, step );
		else
			steps.AddLast( step );
	}

	private static void Require( bool condition, string step )
	{
		if ( !condition )
			throw new InvalidOperationException( $"Theme Park Inc slice smoke test: {step}." );
	}

	public void Dispose() => watchdog.Dispose();
}
