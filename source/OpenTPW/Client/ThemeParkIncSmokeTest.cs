using OpenTPW.FrontEnd;
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
	private ScienceSmokeSaves? personnelFileSaves;

	public ThemeParkIncSmokeTest( GameFlow flow, string? restartPhase = null )
	{
		this.flow = flow;
		flow.PersistSettings = false;
		watchdog = new System.Threading.Timer( _ =>
		{
			Console.Error.WriteLine( $"Theme Park Inc slice smoke test failed: timed out after {MaximumSeconds} s at frame {frame} (stuck at '{(steps.Count > 0 ? steps.First!.Value.Name : "end")}'); display {Render.Metrics}." );
			Environment.Exit( 1 );
		}, null, TimeSpan.FromSeconds( MaximumSeconds ), System.Threading.Timeout.InfiniteTimeSpan );
		if ( restartPhase == "restore" )
			PlanColdRestore();
		else
			Plan( captureForRestart: restartPhase == "capture" );
	}

	/// <summary>The mission window (docs/reverse/TPI-MISSIONS.md §7): the Objectives key opens it over mission 0, whose Proceed waits for the forty guests; it pauses nothing.</summary>
	private void PlanMissionWindow()
	{
		Do( "open the mission window with the Objectives key", () => flow.InjectedInput = UiInput.Key( UiKeys.Objectives ) );
		Wait( "mission window opens", 1 );
		Do( "the mission window shows mission 0 with Proceed disabled", () =>
		{
			Require( Hud.MissionWindowOpen && Hud.Stack.Top?.Name == ThemeParkIncMissionWindow.ScreenName, "the Objectives key opens the mission window" );
			var top = Hud.Stack.Top!;
			Require( ((UiLabel)top.Find( "title" )!).Text() == flow.Strings.Entry( ThemeParkIncMissionWindow.TitleText ), "the window's title is UITEXT 461" );
			Require( top.Find( "proceed" ) is { Enabled: false } && Hud.MissionWindow is { ProceedEnabled: false, Page: 0 }, "mission 0's Proceed is disabled at the start" );
			Require( Hud.MissionWindow!.Briefing.Length > 0, "the briefing page shows the mission's text" );
			Require( Hud.TimeScale > 0, "the mission window pauses nothing" );
		} );
		Do( "close the mission window", () => Click( Hud.Stack.Top!, "cancel" ) );
		Wait( "mission window closes", 1 );
		Do( "the mission window is closed", () => Require( !Hud.MissionWindowOpen && !Hud.MenuOpen, "Cancel closes the mission window" ) );
	}

	private void Plan( bool captureForRestart )
	{
		Wait( "slice starts", 10 );
		if ( flow.Level?.LevelName == "science" )
			PlanArrivalFlight();
		Do( "the slice runs under its capability gates", () =>
		{
			var level = flow.Level;
			Require( flow.IncHud != null && flow.Hud == null && level != null, "the Theme Park Inc slice runs with its own HUD, not the Theme Park World one" );
			Require( level!.OriginalPark != null && level.Park is { Economy.Rules.ThemeParkInc: true } && level.IsThemeParkIncSlice, "the world loads with the Theme Park Inc money economy" );
			Require( GameEditions.Capabilities.Money && !GameEditions.Capabilities.Staff && !GameEditions.Capabilities.Research && GameEditions.Capabilities.Challenges && GameEditions.Capabilities.ExperimentalHud && !GameEditions.Capabilities.ParkSaves, "the capability policy runs the money and the Theme Park Inc challenges and refuses staff, research and saves at runtime" );
			Require( Context.Batch.TextDraws.Any( draw => draw.Text == LabelText( "money" ) && draw.Text.Length > 0 ), "the money line is drawn" );
			Require( LabelText( "title" ) == flow.Strings.Extra( OpenTpwText.TpiTitle ) && (GameLanguage.Current.Name != GameLanguage.DefaultLanguage || LabelText( "title" ).Contains( "EXPERIMENTAL", StringComparison.Ordinal )), "the HUD names the experiment on screen, in the selected language" );
			Require( LabelText( "limits" ) == flow.Strings.Extra( OpenTpwText.TpiLimits ) && (GameLanguage.Current.Name != GameLanguage.DefaultLanguage || LabelText( "limits" ) == GameEditions.Capabilities.ExperimentalNote), "the HUD's limits line is the capability policy's note (in the selected language)" );
			Require( Context.Batch.TextDraws.Any( draw => draw.Text == Hud.GuestsText() ), "the guest counter is drawn" );
			if ( level.LevelName == "science" && level.OriginalPark.Save?.ThemeParkIncWorld is { } prebuilt )
			{
				var economy = level.Park!.Economy;
				Require( economy.TpiMissions != null && economy.TpiMissionWorld != null, "the mission controller runs with the level as its world" );
				Require( economy.TpiCareer!.StatusWord( 0, 1 ) == 8 && economy.TpiCareer.StatusWord( 0, 0 ) == 1, "miss00's guest statistic has started" );
				Require( level.ThemeParkIncCurrentObstacle == prebuilt.Variables.CurrentObstacle && prebuilt.FindAttraction( level.ThemeParkIncCurrentObstacle )?.InfoId == 1700,
					"the current obstacle is the prebuilt's 1700 gate" );
				Require( level.Objects.Objects.Any( item => item.ThingId == level.ThemeParkIncCurrentObstacle && item.Entry.InfoId == 1700 )
					&& new[] { 1702, 1701, 1703, 1750, 1798 }.All( type => level.Objects.Objects.Any( item => item.Entry.InfoId == type && item.ThingId == prebuilt.Attractions.Single( attraction => attraction.InfoId == type ).Id ) ),
					"the obstacles and the camera path are things of the world with their saved ids" );
				Require( level.Guests?.Grid.Cells is { } obstacleCells && obstacleCells.ObstacleFlagAt( 41, 48 ) && obstacleCells.RawTypeAt( 41, 48 ) == 8, "the 1700 gate's footprint is a hole with the obstacle flag" );
				// the import, the economy links and Level.SeedThemeParkIncObjectList: the mechanics' object list is the saved chain (FirstObject along mNext), an object with an economy record as
				// its record and any other thing as its negated thing id
				var chain = new List<int>();
				for ( int thing = prebuilt.Variables.FirstObject; thing != 0 && chain.Count <= prebuilt.Attractions.Count; thing = prebuilt.FindAttraction( thing )!.Next )
					chain.Add( thing );
				var listed = economy.ThemeParkIncObjectList.Select( key => key < 0 ? -key : level.Objects.Objects.FirstOrDefault( item => level.GetEconomyInstance( item ) == key ) is { } item
					? level.Objects.SavedThemeParkIncThingOf( item ) : 0 ).ToList();
				Require( listed.SequenceEqual( chain ) && chain.Count == 24 && economy.ThemeParkIncObjectList.Count( key => key < 0 ) == 7,
					$"the mechanics' object list is the saved chain with the seven things without an economy record ({string.Join( ",", economy.ThemeParkIncObjectList )})" );
				// the tool entry 1710 (thing 26) is not rebuilt, and its slot is held as the original keeps the thing (Level.SeedThemeParkIncObjectList)
				Require( level.Objects.Objects.All( item => item.ThingId != 26 ) && level.Things.IsHeld( 26 ) && level.Things.ModelOf( 26 ) == SaveThingModel.Attraction,
					"the prebuilt's unbuilt thing 26 holds its attraction slot" );
				Require( economy.TpiStaff!.Hq.Slots.Count == ThemeParkIncStaffHeadquarters.SlotCount && economy.TpiStaff.Hq.AllFilled, "the staff HQ keeps the prebuilt's twenty empty-requirement slots" );
				Require( economy.TpiResearch is { Count: 50, HasControls: true } research && research.Infos.Count > 0 && !research.IsResearched( 1105 )
					&& Hud.StaffRows().Contains( flow.Strings.Entry( 285 ) ), "the research lab holds the prebuilt's fifty projects, gates the catalogue and has its row in the staff panel" );
			}
		} );
		if ( flow.Level?.LevelName == "science" )
			PlanMissionWindow();
		if ( flow.Level?.LevelName == "science" )
			PlanPrecipitation();
		Do( "open the build window", () => Click( Hud.Screen, "build" ) );
		Wait( "build window opens", 2 );
		Do( "choose a catalogue item", ChooseItem );
		Do( "arm the item through the list", ArmThroughList );
		Wait( "item arms", 2 );
		Do( "the item is armed and the window closed", () =>
		{
			Require( Level.BuildEntry == armedEntry && armedEntry != null, "activating the row arms the item's placement" );
			Require( Hud.Screen.Find( "buildList" )!.Visible == false, "arming closes the build window" );
			Require( Hud.Messages.Any( message => FitsTemplate( message, OpenTpwText.TpiPlaceHint ) ), "the HUD says how to place" );
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
		Do( "the counter counts them", () => Require( LabelText( "guests" ) == string.Format( flow.Strings.Extra( OpenTpwText.TpiGuestsInPark ), Level.Guests!.GetStatistics().InPark ), "the guest counter reads the live guest simulation" ) );
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
		Do( "note the resume frame", () =>
		{
			resumedAt = frame;
			// with Theme Park Inc's native guest states on (--tpi-native-guests, docs/reverse/TPI-GUESTS.md) an idle guest runs only phase P1's leave gate and stands; an exit counter of 0
			// sends it walking to the bus stop, so the walk check runs on the native leaving path
			// TODO(tpi-guests P2): remove this once state 6 (0x614A40) is ported in full: idle guests then wander by themselves
			if ( Level.Guests is { ThemeParkIncNativeGuests: true } native )
				foreach ( var guest in native.Guests )
					guest.ExitLevel = 0;
		} );
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
		PlanHandyman();
		PlanMechanic();
		PlanGardener();
		PlanPerformers();
		if ( flow.Level?.LevelName == "science" )
			PlanFacilities();
		if ( flow.Level?.LevelName == "science" )
			PlanChallenges();
		Do( "round-trip Science state", () =>
		{
			var live = Level;
			if ( live.LevelName != "science" ) return;
			var expectedGuests = live.Guests!.ComputeStateHash();
			var expectedEconomy = ParkSaveFile.Serialize( live.Park!.Economy );
			using var saves = new ScienceSmokeSaves();
			var liveHud = Hud;
			((UiButton)liveHud.Screen.Find( "speed" )!).Activate();
			Require( liveHud.Speed == GameSpeed.Fast, "the Science save starts with a non-default selected speed" );
			((UiButton)liveHud.Screen.Find( "menu" )!).Activate();
			((UiButton)liveHud.Stack.Top!.Find( "savePark" )!).Activate();
			Require( File.Exists( saves.Path ), "the pause menu saves Science in the continuation slot" );
			((UiButton)liveHud.Stack.Top.Find( "loadPark" )!).Activate();
			Require( liveHud.Stack.Top?.Name == "confirmParkLoad", "continuing Science first asks for confirmation" );
			((UiButton)liveHud.Stack.Top!.Find( "choice1" )!).Activate();
			Require( ReferenceEquals( Level, live ) && liveHud.Stack.Top?.Name == "tpiMenu", "cancelling continuation leaves the live park and menu" );
			var savedBytes = File.ReadAllBytes( saves.Path );
			var saved = ParkResumeSaveFile.Deserialize( savedBytes );
			var unsupportedLand = saved with
			{
				Layout = saved.Layout with
				{
					Cells = saved.Layout.Cells with
					{
						Land = new ParkLayoutSnapshot.LandData { PostAX = 0, PostAY = 0, PostBX = 0, PostBY = 0, Ring = new(), Unowned = "" }
					}
				}
			};
			try
			{
				ParkResumeSaveFile.ValidateShape( unsupportedLand );
				throw new InvalidOperationException( "A Science save with land ownership was accepted." );
			}
			catch ( InvalidDataException error )
			{
				Require( error.Message.Contains( "unsupported world state", StringComparison.Ordinal ), "Science rejects land ownership during preflight" );
			}
			// the save keeps the owned land as one bit map; a save without it predates the land and is refused
			Require( saved.Layout.Cells.TpiUnowned is { } unownedBits && saved.Layout.Cells.Cells.All( cell => (cell.Flags & (ushort)ParkCellFlags.Unowned) == 0 )
				&& Convert.FromBase64String( unownedBits ).Sum( bits => System.Numerics.BitOperations.PopCount( bits ) ) == 128 * 128 - ThemeParkIncLand.OwnedCount( live.Guests.Grid.Cells ),
				"the Science save carries its unowned squares as a bit map" );
			try
			{
				ParkResumeSaveFile.ValidateShape( saved with { Layout = saved.Layout with { Cells = saved.Layout.Cells with { TpiUnowned = null } } } );
				throw new InvalidOperationException( "A Science save without its owned land was accepted." );
			}
			catch ( InvalidDataException error )
			{
				Require( error.Message.Contains( ParkResumeSaveFile.ThemeParkIncLandMissing, StringComparison.Ordinal ), "Science refuses a save without its owned land during preflight" );
			}
			File.WriteAllText( saves.Path, "{}" );
			try
			{
				flow.LoadThemeParkIncScience();
				throw new InvalidOperationException( "A malformed Science save was accepted." );
			}
			catch ( InvalidDataException )
			{
				Require( ReferenceEquals( Level, live ) && live.Guests.ComputeStateHash() == expectedGuests,
					"a malformed Science save leaves the running park unchanged" );
			}
			File.WriteAllBytes( saves.Path, savedBytes );
			const int continuationTicks = 180;
			live.RunTicks( continuationTicks );
			var uninterruptedGuests = live.Guests.ComputeStateHash();
			var uninterruptedEconomy = ParkSaveFile.Serialize( live.Park.Economy );
			((UiButton)liveHud.Stack.Top!.Find( "loadPark" )!).Activate();
			((UiButton)liveHud.Stack.Top!.Find( "choice0" )!).Activate();
			var candidate = Level;
			Require( !ReferenceEquals( live, candidate ) && flow.IncHud != null && flow.Hud == null, "continuation activates a new Science level and its TPI HUD" );
			Require( Hud.Speed == GameSpeed.Fast && candidate.SimulationTimeScale == 2, "continuation keeps the selected speed, not the pause menu's temporary pause" );
			Require( candidate.Guests!.ComputeStateHash() == expectedGuests, "the continued Science guests, queues and shared RNG match the live park" );
			var restoredGuests = candidate.Guests.CaptureSnapshot().Guests;
			Require( restoredGuests.Select( guest => (guest.Id, guest.KidSetDraw) ).SequenceEqual( saved.Guests.Guests.Select( guest => (guest.Id, guest.KidSetDraw) ) ),
				"the continued Science guests retain their generation-specific sprite bank draws" );
			Require( ParkSaveFile.Serialize( candidate.Park!.Economy ) == expectedEconomy, "the continued Science park keeps its economy and clock" );
			Require( saved.TpiCurrentObstacle is > 0 && candidate.ThemeParkIncCurrentObstacle == live.ThemeParkIncCurrentObstacle, "the continued Science park keeps its current obstacle" );
			var restoredLayout = ParkLayoutSnapshot.CaptureSnapshot( candidate.Guests.Grid.Cells, candidate.Objects );
			Require( System.Text.Json.JsonSerializer.Serialize( restoredLayout.Cells ) == System.Text.Json.JsonSerializer.Serialize( saved.Layout.Cells ),
				"the continued Science park keeps its exact cell links and routing stamps" );
			var restoredScripts = candidate.Objects.ScriptWorld.CaptureSnapshot();
			Require( System.Text.Json.JsonSerializer.Serialize( restoredScripts ) == System.Text.Json.JsonSerializer.Serialize( saved.Scripts ),
				"the continued Science park keeps its ride script state" );
			candidate.RunTicks( continuationTicks );
			Require( candidate.Guests.ComputeStateHash() == uninterruptedGuests, "Science guest simulation continues as in the uninterrupted park" );
			Require( ParkSaveFile.Serialize( candidate.Park.Economy ) == uninterruptedEconomy, "Science economy continues as in the uninterrupted park" );
		} );
		if ( flow.Level?.LevelName == "science" )
			PlanPersonnelFileContinuation();
		if ( !captureForRestart )
		{
			PlanTagTube();
			PlanWages();
			PlanLetter();
		}
		// The exit a player uses: the pause menu's Quit closes the window and ends the run.
		Do( "open the pause menu", () => flow.InjectedInput = UiInput.Key( UiKeys.Back ) );
		Wait( "menu opens", 2 );
		Do( "quit through the menu", () =>
		{
			if ( Hud.Lost )
			{
				Require( Hud.Stack.Top?.Name == ThemeParkIncHud.LostMenuScreen, "the lost park's menu is open" );
				completed = true;
				Click( Hud.Stack.Top!, "quitGame" );
				return;
			}
			Require( Hud.Stack.Top?.Name == "tpiMenu", "the pause menu is open" );
			if ( captureForRestart )
			{
				Require( Hud.Speed == GameSpeed.Paused, "the restart fixture saves a paused park" );
				((UiButton)Hud.Stack.Top!.Find( "savePark" )!).Activate();
				Require( File.Exists( GameFlow.IncScienceSavePath() ), "the pause menu wrote the restart fixture" );
				using var evidence = new BinaryWriter( new FileStream( RestartEvidencePath, FileMode.CreateNew ) );
				evidence.Write( Level.Guests!.ComputeStateHash() );
				evidence.Write( EconomyDigest( Level.Park!.Economy ) );
			}
			completed = true;
			Click( Hud.Stack.Top!, "quit" );
		} );
	}

	/// <summary>
	/// The tag tube: a skipped mission's "Objective Complete!" letter is filed as an unread row beside the arrival's briefing; the message key opens the window, a selected row is read after
	/// its 2.5 seconds, Delete removes it and Okay closes the window. No row opens the letter window.
	/// </summary>
	private void PlanTagTube()
	{
		var objectiveRows = 0;
		Do( "skip a mission", () =>
		{
			var economy = Level.Park!.Economy;
			objectiveRows = economy.TpiTagTube!.Listed.Count( row => row.Source == ThemeParkIncLetterSource.Objective );
			// what the debug dialog's Skip Mission does (0x532B39, the cheats extension's button, which is off in this run): the completion tick between two turns, whose next update posts the
			// objective letter (0x5EF991)
			economy.TpiMissions!.CompleteMission( economy.Turn );
		} );
		steps.AddLast( ("the controller files the objective letter", () =>
		{
			var economy = Level.Park!.Economy;
			// the park was left paused by the continuation's checks: the pause key lets the turns run, and a flight the completion starts is skipped
			if ( Hud.Speed == GameSpeed.Paused && !Hud.MenuOpen )
				flow.InjectedInput = UiInput.Key( UiKeys.Pause );
			else if ( economy.TpiCutscene is { Flying: true } )
				flow.InjectedInput = UiInput.Key( UiKeys.Back );
			return economy.TpiCutscene is not { Flying: true } && !Hud.FlightActive
				&& economy.TpiTagTube!.Listed.Count( row => row.Source == ThemeParkIncLetterSource.Objective ) > objectiveRows;
		}) );
		Wait( "the HUD settles", 2 );
		Do( "the objective letter is an unread row and opened no window", () =>
		{
			var tube = Level.Park!.Economy.TpiTagTube!;
			var row = tube.Listed.First();
			Require( row.Source == ThemeParkIncLetterSource.Objective && !row.Read && row.Tag == ThemeParkIncTagLetter.PlainTag, "the newest row is the unread objective letter (0x5CEC30)" );
			Require( !Hud.LetterOpen, "a kind-4 letter opens no letter window (0x5CD060)" );
			Require( tube.Unread >= 2, "the briefing and the objective letter are unread" );
		} );
		Do( "close what the HUD has open", () =>
		{
			if ( Hud.MissionWindowOpen )
				Click( Hud.Stack.Top!, "cancel" );
		} );
		Wait( "the HUD is clear", 2 );
		Do( "open the tag tube with the message key", () => flow.InjectedInput = UiInput.Key( UiKeys.Messages ) );
		Wait( "the tag tube opens", 2 );
		Do( "the tag tube lists the letters newest first", () =>
		{
			Require( Hud.TagTubeOpen && Hud.Stack.Top?.Name == ThemeParkIncTagTubeWindow.ScreenName, "the M key opens the tag tube window (0x42EED0)" );
			var window = Hud.TagTubeWindow!;
			Require( window.ListedRows.Count >= 2 && window.ListedRows[0].Source == ThemeParkIncLetterSource.Objective, "the date column sorts the objective letter first" );
			Require( window.ListedRows.Any( row => row.Source == ThemeParkIncLetterSource.Introduction ), "the arrival's briefing is listed" );
			Require( window.Selected == null && window.DeleteEnabled && !window.EmptyShown, "nothing is selected; Delete is on" );
		} );
		Do( "select the objective letter", () => Hud.TagTubeWindow!.SelectRow( 0 ) );
		Wait( "the details show", 2 );
		Do( "the row's details show and it is read after its deadline", () =>
		{
			var window = Hud.TagTubeWindow!;
			var row = window.ListedRows[0];
			Require( window.Details.Subject == Hud.Texts!.Format( ThemeParkIncTable.TagSystem, ThemeParkIncObjectiveLetter.SubjectEntry ), "the subject is \"Objective Complete!\"" );
			Require( window.Details.Body.Length > 0 && window.GotoShown == (row.GotoKind != 0), "the body shows and the go-to follows the row" );
			Require( !Hud.LetterOpen, "selecting a row opens no letter window" );
			// the window's 2.5-second read deadline (0x553480), driven on the window's own clock so the run does not depend on the frame rate
			window.Tick( ThemeParkIncTagTubeWindow.ReadDelay );
			Require( row.Read, "the shown row is read once its deadline has come" );
		} );
		Do( "delete the objective letter", () => Click( Hud.Stack.Top!, "delete" ) );
		Wait( "the delete runs on the next tick", 3 );
		Do( "the objective letter left the list", () =>
		{
			var window = Hud.TagTubeWindow!;
			Require( window.ListedRows.All( row => row.Source != ThemeParkIncLetterSource.Objective || row.Argument != Level.Park!.Economy.TpiCareer!.MissionCounter - 1 ),
				"the deleted row is not listed (0x636A40)" );
			Require( window.Selected == null, "the delete cleared the selection" );
		} );
		Do( "close the tag tube", () => Click( Hud.Stack.Top!, "ok" ) );
		Wait( "the tag tube closes", 2 );
		Do( "the tag tube is closed", () => Require( !Hud.TagTubeOpen, "Okay closes the tag tube window" ) );
	}

	/// <summary>
	/// A wage demand: the day routine files it as an unread row of the tag tube; its go-to opens the wage demand window on the member, OK negotiates the offer (one step above the demand) and the
	/// results dialog's OK raises the salary by it.
	/// </summary>
	private void PlanWages()
	{
		ThemeParkIncStaffMember? member = null;
		var salary = 0;
		Do( "a member demands a rise", () =>
		{
			var economy = Level.Park!.Economy;
			member = economy.TpiStaff!.Members.FirstOrDefault( item => item.Placed );
			Require( member != null, "the park has a placed member" );
			salary = member!.Salary;
			// what the day message does on the member's demand day (0x628CA0): the demand's row, filed as the type-0x19 letter
			economy.FileThemeParkIncWageDemands( new[] { new ThemeParkIncStaff.WageDemand( member, ThemeParkIncTagLetter.WageDemandWithoutStrike, 20 ) } );
			var row = economy.TpiTagTube!.Listed.First();
			Require( row.Source == ThemeParkIncLetterSource.WageDemand && !row.Read && row.Key == member.ThingId && row.GotoArgument == 20, "the demand is the newest unread row (0x5CF3F0)" );
			Require( !Hud.LetterOpen, "a kind-0xB letter opens no letter window (0x5CD060)" );
		} );
		Do( "open the tag tube for the demand", () => flow.InjectedInput = UiInput.Key( UiKeys.Messages ) );
		Wait( "the tag tube opens", 2 );
		Do( "select the demand", () =>
		{
			Require( Hud.TagTubeOpen, "the M key opens the tag tube" );
			var window = Hud.TagTubeWindow!;
			var index = window.ListedRows.ToList().FindIndex( row => row.Source == ThemeParkIncLetterSource.WageDemand );
			Require( index >= 0, "the demand is listed" );
			window.SelectRow( index );
		} );
		Wait( "the details show", 2 );
		Do( "the demand's go-to shows", () =>
		{
			var window = Hud.TagTubeWindow!;
			Require( window.GotoShown, "a demand row's go-to (+0xE10 = 0xE) shows" );
			Require( window.Details.Subject == Hud.Texts!.Format( ThemeParkIncTable.TagSystem, 0xFB ), "the subject is TAG_SYSTEM 0xFB" );
			Click( Hud.Stack.Top!, "goto" );
		} );
		Wait( "the wage window opens", 2 );
		Do( "raise the offer and press OK", () =>
		{
			Require( Hud.WageDemandWindow is { Offer: 20 } && Hud.Stack.Top?.Name == ThemeParkIncWageDemandWindow.ScreenName, "the go-to opens the wage demand window on the demand (0x59ABB0)" );
			Require( !Hud.TagTubeOpen, "the go-to closed the tag tube" );
			Hud.WageDemandWindow!.Step( 1 );
			Click( Hud.Stack.Top!, "ok" );
		} );
		Wait( "the negotiation opens", 2 );
		Do( "the negotiation accepts the offer", () =>
		{
			Require( Hud.NegotiationWindow is { Offer: 21, Result: ThemeParkIncWageNegotiation.Accepted } && Hud.Stack.Top?.Name == ThemeParkIncWageNegotiationWindow.ScreenName,
				"OK opens the results dialog on the offer (0x559C40), which meets the demand" );
			Click( Hud.Stack.Top!, "ok" );
		} );
		Wait( "the dialog closes", 2 );
		Do( "the salary rose and the demand left the tube", () =>
		{
			Require( Hud.NegotiationWindow == null && Hud.WageDemandWindow == null, "both windows are closed" );
			Require( member!.Salary == Math.Max( salary + 21, Level.Park!.Economy.TpiStaff!.Settings.MinWage ), $"the salary is the old one plus the offer (0x62D160): {member.Salary}" );
			Require( Level.Park.Economy.TpiTagTube!.Listed.All( row => row.Source != ThemeParkIncLetterSource.WageDemand || row.Key != member.ThingId ), "0x636C90 deleted the demand" );
		} );
	}

	/// <summary>
	/// The bank's "Game Over" letter: it opens the letter window over the HUD, and closing it with Okay loses the park
	/// (docs/reverse/TPI-BANK-MAIL.md). Planned last, because the park is lost afterwards.
	/// </summary>
	private void PlanLetter()
	{
		Do( "post the bank's letter", () => Level.Park!.Economy.DebugPostThemeParkIncLetter( ThemeParkIncAdviceMail.RedSixMonths ) );
		Wait( "the letter opens", 3 );
		Do( "the letter window shows the letter", () =>
		{
			Require( Hud.LetterOpen && Hud.Stack.Top?.Name == ThemeParkIncLetterWindow.ScreenName, "the letter window stands over the HUD" );
			var subject = ((UiLabel)Hud.Stack.Top!.Find( "subject" )!).Text();
			var tags = GameLanguage.Current.LoadStrings( "TAG_SYSTEM.str" );
			Require( subject.Length > 0 && subject == tags.Entries[ThemeParkIncAdviceMail.SubjectEntry], "the subject is TAG_SYSTEM.str entry 165" );
			Require( ((UiLabel)Hud.Stack.Top!.Find( "body" )!).Text().StartsWith( tags.Entries[166], StringComparison.Ordinal ), "the body is the bank's letter" );
			Require( Context.Batch.TextDraws.Any( draw => draw.Text == subject ), "the subject is drawn" );
			Require( !Level.Park!.Economy.IsBankrupt, "an open letter has not lost the park yet" );
		} );
		Do( "close the letter", () => Click( Hud.Stack.Top!, "ok" ) );
		Wait( "the letter closes", 3 );
		Do( "closing the letter lost the park", () =>
		{
			Require( !Hud.LetterOpen, "the letter window is gone" );
			Require( Level.Park!.Economy.IsBankrupt && Level.Park.Economy.ThemeParkIncLossReason == 0, "the bank's letter lost the park" );
			Require( Hud.LossWindowOpen && Hud.Stack.Top?.Name == ThemeParkIncHud.LossWindowScreen, "the loss window opens" );
			Require( Hud.LossCaption == flow.Strings.Entry( ThemeParkIncHud.BankruptCaption ) && Hud.LossCaption.Length > 0, "its caption is UITEXT 921" );
			Require( Context.Batch.TextDraws.Any( draw => draw.Text == Hud.LossCaption ), "the caption is drawn" );
		} );
		Do( "leave the loss window with Escape", () => flow.InjectedInput = UiInput.Key( UiKeys.Back ) );
		Wait( "the overlay opens", 2 );
		Do( "the hidden-HUD overlay keeps the caption", () =>
		{
			Require( Hud.LossOverlayOpen && Hud.Stack.Top?.Name == ThemeParkIncHud.LossOverlayScreen && Level.LossOverlayActive, "Escape leaves the window for the overlay" );
			Require( Context.Batch.TextDraws.Any( draw => draw.Text == Hud.LossCaption ), "the caption stays" );
		} );
		Do( "open the lost park's menu", () => flow.InjectedInput = UiInput.Key( UiKeys.Back ) );
		Wait( "the menu opens", 2 );
		Do( "the lost park's menu has its four lines and pauses nothing", () =>
		{
			var menu = Hud.Stack.Top;
			Require( menu?.Name == ThemeParkIncHud.LostMenuScreen, "the overlay's Escape opens the lost park's menu" );
			Require( new[] { "loadGame", "resumeGame", "selectNewPlayer", "quitGame" }.All( id => menu!.Find( id ) != null ), "Load Game, Resume Game, Select A New Player and Quit Game" );
			Require( Hud.TimeScale == GameSpeedScale.Of( Hud.Speed ), "the lost park's menu does not pause the park" );
		} );
		Do( "resume from the lost park's menu", () => Click( Hud.Stack.Top!, "resumeGame" ) );
		Wait( "the menu closes", 2 );
		Do( "back on the overlay", () => Require( Hud.Stack.Top?.Name == ThemeParkIncHud.LossOverlayScreen, "Resume Game returns to the overlay" ) );
	}

	private void PlanPrecipitation()
	{
		ThemeParkIncWeatherSnapshot saved = default;
		ThemeParkIncWeatherSpells Weather() => Level.Park!.Economy.TpiWeather!;
		void SetWeather( int kind, int drops )
		{
			var spell = new WeatherSpell( 3, kind, 0, 3 );
			Weather().Restore( new( spell, spell, false, 1, drops ) );
		}

		Do( "pause for precipitation readback", () => flow.InjectedInput = UiInput.Key( UiKeys.Pause ) );
		Do( "present rain without advancing the world", () =>
		{
			Require( Hud.TimeScale == 0f && Level.Weather == null && Level.TpiPrecipitationRenderer != null
				&& Entity.All.OfType<WeatherRenderer>().Count() == 1, "only the TPI precipitation renderer owns this park" );
			saved = Weather().Capture();
			SetWeather( 2, 600 );
		} );
		Wait( "rain renders", 2 );
		Do( "verify rain and request snow", () =>
		{
			Require( Level.TpiPrecipitationRenderer is { DrawnDrops: > 0, DrawsSnow: false }, "rain submits streaks to the desktop renderer" );
			SetWeather( 4, 600 );
		} );
		Wait( "snow renders", 2 );
		Do( "verify snow and restore the initial spell", () =>
		{
			Require( Level.TpiPrecipitationRenderer is { DrawnDrops: > 0, DrawsSnow: true }, "snow submits flakes to the desktop renderer" );
			Weather().Restore( saved );
		} );
		Wait( "the dry field renders", 2 );
		Do( "verify dry weather and unpause", () =>
		{
			Require( Level.TpiPrecipitationRenderer is { DrawnDrops: 0 }, "dry weather submits no precipitation" );
			flow.InjectedInput = UiInput.Key( UiKeys.Pause );
		} );
	}

	private static string RestartEvidencePath => SaveFileSystem.GetAbsolutePath( "opentpw-tpi-restart.evidence" );

	private static string EconomyDigest( ParkEconomy economy ) => Convert.ToHexString( System.Security.Cryptography.SHA256.HashData(
		System.Text.Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( economy ) ) ) );

	private Vector3 flightEye;

	/// <summary>
	/// The zone arrival of a new game's Science park: the camera path 1798 flies (the ride view on its seat, the clock at 1.0, the HUD hidden under the input window), Escape skips it, message 0x29 carries the
	/// trigger, and the mission window's countdown runs out one update later and opens the first briefing.
	/// </summary>
	private void PlanArrivalFlight()
	{
		Do( "the zone arrival flies the camera path", () =>
		{
			var cutscene = Level.Park?.Economy.TpiCutscene;
			Require( cutscene is { Flying: true, Skippable: true, Mode: ThemeParkIncCutscene.ArrivalMode, OverlayOpen: true } && Level.Objects.Objects.Any( item => item.ThingId == cutscene.AnimThing && item.Entry.InfoId == 1798 ),
				"a new game's first day flies the Science camera path, skippable" );
			Require( Level.RideView is { } view && view.Ride.Entry.InfoId == 1798 && ReferenceEquals( Camera.Mode, view ), "the camera rides the camera path's seat" );
			Require( Level.SimulationTimeScale == 1f && Hud.FlightActive, "the clock runs at the flight's rate and the HUD is hidden under the input window" );
			flightEye = Camera.Position;
		} );
		Wait( "the flight moves on", 90 );
		Do( "the camera follows the clip and the HUD stays hidden", () =>
		{
			Require( (Camera.Position - flightEye).Length > 0.01f, "the seat moves with the camera path's M clip" );
			Require( !Context.Batch.TextDraws.Any( draw => draw.Text == LabelText( "money" ) ), "the HUD is not drawn during the flight" );
		} );
		Do( "Escape skips the flyover", () => flow.InjectedInput = UiInput.Key( UiKeys.Back ) );
		Wait( "the next turns end the flight", 90 );
		Do( "the flyover ended with the trigger and the window's countdown ran out", () =>
		{
			var economy = Level.Park!.Economy;
			Require( economy.TpiCutscene is { Flying: false, OverlayOpen: false, ClockRate: null } && Level.RideView == null && Camera.Mode is ParkCameraMode, "the skip ended the flight and left the ride view" );
			Require( economy.TpiCameraEnds.LastOrDefault() is { Result: 1, Mode: ThemeParkIncCutscene.ArrivalMode } && economy.TpiCutscene.MissionWindowRequests == 1, "message 0x29 carried the trigger and the mission window was asked for" );
			Require( economy.TpiTagTube!.Listed.Count( row => row.Source == ThemeParkIncLetterSource.Introduction && !row.Read ) == 1 && economy.TpiCareer!.ObjectiveLetterPosted,
				"the arrival's end filed mission 0's first briefing as an unread tube row" );
		} );
		Wait( "the HUD comes back", 2 );
		Do( "the countdown opened the mission window", () => Require( Hud.MissionWindowOpen && Hud.Stack.Top?.Name == ThemeParkIncMissionWindow.ScreenName, "the arrival's end opens the first briefing (0x5D0369)" ) );
		Do( "close the arrival's mission window", () => Click( Hud.Stack.Top!, "cancel" ) );
		Wait( "the arrival's mission window closes", 1 );
		Do( "the arrival's mission window is closed", () => Require( !Hud.MissionWindowOpen, "Cancel closes the arrival's mission window" ) );
	}

	private void PlanColdRestore()
	{
		ulong expectedGuests;
		string expectedEconomy;
		using ( var evidence = new BinaryReader( File.OpenRead( RestartEvidencePath ) ) )
		{
			Require( evidence.BaseStream.Length == sizeof( ulong ) + 1 + 64, "the previous process wrote the restart evidence" );
			expectedGuests = evidence.ReadUInt64();
			expectedEconomy = evidence.ReadString();
		}
		var saved = ParkResumeSaveFile.Load( GameFlow.IncScienceSavePath() );
		Do( "select Water on the freshly started Personnel File", () =>
		{
			Require( flow.Level == null && flow.IncFrontEnd != null, "the new process starts without a loaded park" );
			Click( flow.IncFrontEnd!.Screen, "zone_water" );
		} );
		Do( "load Science from the previous process", () =>
		{
			Require( flow.IncFrontEnd!.Zone == "water", "Water is selected before continuing Science" );
			Click( flow.IncFrontEnd.Screen, "loadGame" );
		} );
		Wait( "the park loads", 2 );
		Do( "compare the new process with the saved park", () =>
		{
			Require( flow.IncFrontEnd == null && flow.Level is { LevelName: "science" } && flow.Hud == null && Hud.Speed == GameSpeed.Paused,
				"the saved Science park is active and still paused" );
			Require( Level.Guests!.ComputeStateHash() == expectedGuests && EconomyDigest( Level.Park!.Economy ) == expectedEconomy,
				"the saved guests, queues, RNG, economy, clock and weather match the previous process" );
			Require( Level.Guests.CaptureSnapshot().Guests.Select( guest => (guest.Id, guest.KidSetDraw) )
				.SequenceEqual( saved.Guests.Guests.Select( guest => (guest.Id, guest.KidSetDraw) ) ),
				"the guest sprite banks also survive the process restart" );
			completed = true;
			GameFlow.Quit();
		} );
	}

	private void PlanPersonnelFileContinuation()
	{
		ThemeParkIncFrontEnd? personnelFile = null;
		ParkResumeSnapshot? snapshot = null;
		string? expectedEconomy = null;
		ulong expectedGuests = 0;
		Do( "open the Personnel File with no existing slot", () =>
		{
			personnelFileSaves = new ScienceSmokeSaves();
			var live = Level;
			expectedEconomy = ParkSaveFile.Serialize( live.Park!.Economy );
			expectedGuests = live.Guests!.ComputeStateHash();
			// Leave the saved park paused so the first frame can be compared with the capture.
			snapshot = live.CaptureResumeSnapshot( null, "Science", savedUtc: DateTimeOffset.UtcNow ) with { TpiHudSpeed = GameSpeed.Paused };
			flow.ShowThemeParkIncFrontEnd();
			// [BIN:TPI-EXE:0x6A47B0 leave park] leaving the park runs the full save: the player file, then the Science slot
			Require( flow.Level == null && File.Exists( personnelFileSaves.Path ) && File.Exists( GameFlow.ThemeParkIncPlayerFilePath() )
				&& flow.IncFrontEnd!.Screen.Find( "loadGame" ) is UiButton { Enabled: true }, "leaving the park saves it, so Load A Game is enabled" );
			File.Delete( personnelFileSaves.Path );
			flow.ShowThemeParkIncFrontEnd();
			Require( flow.IncFrontEnd!.Screen.Find( "loadGame" ) is UiButton { Enabled: false }, "the Personnel File disables Load A Game when no slot exists" );
		} );
		Do( "open the Personnel File with a corrupt slot", () =>
		{
			File.WriteAllText( personnelFileSaves!.Path, "{}" );
			flow.ShowThemeParkIncFrontEnd();
			personnelFile = flow.IncFrontEnd!;
			Click( personnelFile.Screen, "zone_water" );
		} );
		Do( "try to load the corrupt slot", () =>
		{
			Require( personnelFile!.Zone == "water", "Water is selected in the Personnel File" );
			Click( personnelFile.Screen, "loadGame" );
		} );
		Wait( "corrupt slot is rejected", 2 );
		Do( "dismiss the load error", () =>
		{
			Require( flow.Level == null && ReferenceEquals( flow.IncFrontEnd, personnelFile ) && personnelFile!.Stack.Top?.Name == "tpiLoadFailed",
				"the corrupt slot leaves the Personnel File in place and shows a load error" );
			Click( personnelFile!.Stack.Top!, "ok" );
		} );
		Do( "continue the saved Science park from the Personnel File", () =>
		{
			Require( personnelFile!.Stack.Top == personnelFile.Screen && personnelFile.Zone == "water", "dismissing the error returns to the same selected zone" );
			ParkResumeSaveFile.Save( personnelFileSaves!.Path, snapshot! );
			Click( personnelFile.Screen, "loadGame" );
		} );
		Wait( "the saved park loads", 2 );
		Do( "verify the saved park loaded instead of the selected zone", () =>
		{
			Require( flow.IncFrontEnd == null && flow.Level is { LevelName: "science" } && flow.Hud == null && Hud.Speed == GameSpeed.Paused,
				"Load A Game continues the paused Science park directly, regardless of the selected zone" );
			Require( Level.Guests!.ComputeStateHash() == expectedGuests && ParkSaveFile.Serialize( Level.Park!.Economy ) == expectedEconomy,
				"the Personnel File loads the saved guests, RNG, economy and clock rather than starting a fresh park" );
			personnelFileSaves!.Dispose();
			personnelFileSaves = null;
		} );
	}

	private sealed class ScienceSmokeSaves : IDisposable
	{
		private readonly BaseFileSystem previous = SaveFileSystem;
		private readonly string directory = Directory.CreateTempSubdirectory( "opentpw-tpi-resume-" ).FullName;

		public ScienceSmokeSaves() => SaveFileSystem = new BaseFileSystem( directory );

		public string Path => SaveFileSystem.GetAbsolutePath( "opentpw-tpi-science-world.json" );

		public void Dispose()
		{
			SaveFileSystem = previous;
			Directory.Delete( directory, recursive: true );
		}
	}

	private IEnumerable<RideVM> FaultedScripts() =>
		Level.Objects.Objects.Select( item => item.Runtime.Script ).OfType<RideVM>().Where( script => script.State == RideVMState.Faulted );

	/// <summary>Picks the item the build list arms: the first Rides entry with a queue and a script, else the first such entry of any category.</summary>
	private void ChooseItem()
	{
		// the HUD's own list, mission gate included (ThemeParkIncCatalogue): the row index the smoke clicks is that list's
		var catalog = Hud.Catalog;
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

	private ThemeParkIncStaffMember? hiredHandyman;
	private ThemeParkIncStaffMember? hiredMechanic;
	private ThemeParkIncStaffMember? hiredGardener, hiredEntertainer, hiredGuard;
	private long handymanDropTurn;
	private int staffRow;

	/// <summary>
	/// The Theme Park Inc staff panel's hire: the first Cleaner (handyman) row is activated through the list, the hire is dropped on the first path cell the drop accepts, and the member's
	/// thing is updated by the park's turns; the round trip that follows then carries him.
	/// </summary>
	/// <summary>
	/// The challenge panel: an offer of the Science level's first slot (the hot-dog challenge, a measured type) is listed under its <c>CHALLENGE_TITLES.str</c> title, its row's
	/// accept is refused while the park is closed and accepts it once the park is open; the continuation round trip that follows then carries the accepted challenge.
	/// </summary>
	private void PlanChallenges()
	{
		Do( "open the challenge panel", () => Click( Hud.Screen, "challenges" ) );
		Wait( "challenge panel opens", 2 );
		Do( "offer the hot-dog challenge", () =>
		{
			var economy = Level.Park!.Economy;
			Require( economy.TpiChallenges is { } challenges && challenges.Slots[0].Type == 2 && challenges.Slots[0].ScriptIndex == 2, "the Science level's first slot is the hot-dog challenge" );
			Require( Hud.Screen.Find( "challengePanelList" ) is { Visible: true }, "the challenge list is open" );
			Require( economy.DebugOfferThemeParkIncChallenge( 0 ) == 0, "the slot is offered" );
		} );
		Wait( "the offer note shows", 2 );
		Do( "the offer is listed by its title", () =>
		{
			var title = Hud.Strings.ChallengeTitle( 2 );
			Require( title.Length > 0 && Hud.ChallengeRows()[0] == string.Format( flow.Strings.Extra( OpenTpwText.TpiChallengeOfferedRow ), title ), "the row names CHALLENGE_TITLES.str entry 2" );
			Require( Hud.Messages.Contains( string.Format( flow.Strings.Extra( OpenTpwText.TpiChallengeOfferNote ), title ) ), "the HUD notes the new offer" );
			var economy = Level.Park!.Economy;
			if ( !economy.IsParkOpen )
			{
				Hud.ActivateChallengeRow( 0 );
				Require( economy.TpiChallenges!.Slots[0].Status == ThemeParkIncChallengeStatus.Offered, "a closed park refuses the accept" );
				economy.OpenPark();
			}
			Hud.ActivateChallengeRow( 0 );
			Require( economy.TpiChallenges!.Slots[0].Status == ThemeParkIncChallengeStatus.Running && Hud.ChallengeRows()[0].StartsWith( Hud.ChallengeTitle( economy.TpiChallenges.Slots[0] ), StringComparison.Ordinal ),
				"the open park's accept starts the 60-day timed phase" );
		} );
		Do( "close the challenge panel", () => flow.InjectedInput = UiInput.Key( UiKeys.Back ) );
		Wait( "challenge panel closes", 2 );
		Do( "the challenge panel is closed", () => Require( Hud.Screen.Find( "challengePanelList" ) is { Visible: false } && !Hud.MenuOpen, "Escape closes the challenge panel" ) );
	}

	private void PlanHandyman()
	{
		Do( "open the staff panel", () => Click( Hud.Screen, "staff" ) );
		Wait( "staff panel opens", 2 );
		Do( "choose a cleaner through the list", () =>
		{
			var staff = Level.Park!.Economy.TpiStaff;
			Require( staff != null && Level.TpiStaffWorld != null, "the Theme Park Inc park has its staff and their world" );
			var rows = Hud.StaffRows();
			var candidates = staff!.Slots.Where( slot => slot != null ).ToList();
			staffRow = candidates.FindIndex( candidate => candidate!.Role == ThemeParkIncStaffRole.Handyman );
			Require( staffRow >= 0 && staffRow < rows.Count, "the pool lists a cleaner" );
			var list = (UiScrollList)Hud.Screen.Find( "staffPanelList" )!;
			Require( list.Visible, "the staff list is open" );
			list.Scroll( -100000 );
			var rect = list.ScreenRect( Context.Canvas );
			var scale = rect.Height / list.Bounds.Height;
			var first = Math.Clamp( staffRow - list.VisibleRows + 1, 0, Math.Max( 0, rows.Count - list.VisibleRows ) );
			list.Scroll( first );
			var row = new System.Numerics.Vector2( rect.X + rect.Width / 2, rect.Y + (list.Margin + (staffRow - first + 0.5f) * list.RowHeight) * scale );
			Require( list.RowAt( Context.Canvas, row ) == staffRow, "the cleaner's row is on screen" );
			Sub( "select the cleaner's row", () => flow.InjectedInput = UiInput.Click( row ) );
			Sub( "activate the cleaner's row", () => flow.InjectedInput = UiInput.Click( row ) );
		} );
		Wait( "the hire starts", 2 );
		Do( "the cleaner is in the hand", () =>
		{
			Require( Level.TpiPlacingSlot >= 0 && Level.Park!.Economy.TpiStaff!.IsPlacing( Level.TpiPlacingSlot ), "activating the cleaner's row starts his placement" );
			Require( !Hud.Screen.Find( "staffPanelList" )!.Visible, "and closes the panel" );
		} );
		Do( "drop the cleaner on a linked path", () =>
		{
			var grid = Level.Guests!.Grid;
			var before = Level.Park!.Economy.TpiStaff!.Members.Count;
			handymanDropTurn = Level.Park.Economy.Turn;
			// a path junction about eight cells into the park from the first linked path cell (the entrance's), so the cleaner walks in the open where the camera sees him
			int Links( int x, int y ) => System.Numerics.BitOperations.PopCount( (uint)(grid.Cells.LinksAt( x, y ) & 0x55) );
			var cells = Enumerable.Range( 0, grid.CountX * grid.CountY ).Select( index => (X: index % grid.CountX, Y: index / grid.CountX) )
				.Where( cell => grid.IsWalkable( cell.X, cell.Y ) && Links( cell.X, cell.Y ) >= 2 ).OrderBy( cell => cell.Y ).ThenBy( cell => cell.X ).ToList();
			Require( cells.Count > 0, "the park has linked path cells" );
			var first = cells[0];
			foreach ( var cell in cells.OrderBy( cell => (Links( cell.X, cell.Y ) >= 3 ? 0 : 100) + Math.Abs( Math.Abs( cell.X - first.X ) + Math.Abs( cell.Y - first.Y ) - 8 ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
				if ( (hiredHandyman = Level.DropTpiStaff( cell.X, cell.Y )) != null )
					break;
			Require( hiredHandyman != null && Level.Park.Economy.TpiStaff.Members.Count == before + 1, "the drop makes the member" );
			Require( Level.TpiPlacingSlot < 0 && hiredHandyman!.Placed && Level.Things.IsUsed( hiredHandyman.ThingId ), "the member's thing is in the park's pool" );
		} );
		Wait( "the cleaner works", 150 );
		PlanCleanerView();
		Do( "the cleaner is updated by the park's turns", () =>
		{
			var member = hiredHandyman!;
			Require( member.LastUpdateTurn > handymanDropTurn, $"the thing sweep updated the cleaner (last update {member.LastUpdateTurn}, dropped at {handymanDropTurn})" );
			Require( member.State is ThemeParkIncStaffWorld.Idle or ThemeParkIncStaffWorld.Walking or ThemeParkIncStaffWorld.ToLitter or ThemeParkIncStaffWorld.Sweeping, "in a handyman state" );
			Require( float.IsFinite( member.Energy ) && member.Energy <= 100f, "with his meters" );
			Require( member.Sprite.Output != null && Level.TpiStaffWorld!.SpriteBanks?.BankCount( StaffType.Handyman ) > 0, "the cleaner's sprite source shows a frame of the handymen's bank" );
			Log.Trace( $"Theme Park Inc slice smoke: cleaner {member.ThingId} in state {member.State:X} at {member.Navigation!.Cell}, energy {member.Energy}, last update turn {member.LastUpdateTurn}, walk original {member.Navigation.Original} arrived {member.Navigation.Arrived} target ({member.Navigation.TargetX >> 16}, {member.Navigation.TargetY >> 16}) links {Level.Guests!.Grid.Cells.LinksAt( member.Navigation.Cell.X, member.Navigation.Cell.Y ):X}; {Level.Park!.Economy.Litter.Count} litter item(s) in the park." );
		} );
	}

	private OriginalObject? staffRoom;

	/// <summary>
	/// The facilities tab (the build panel's fifth, Info.WhichUIType 5): it lists the researched facilities mission 0 allows; the staff room is armed through the list and built beside the cleaner,
	/// where it becomes the rest area his tired gate walks him to, and the litter bin is built and takes Standard.sam's construction words.
	/// </summary>
	private void PlanFacilities()
	{
		Do( "open the build window for the facilities", () => Click( Hud.Screen, "build" ) );
		Wait( "build window opens", 2 );
		Do( "choose the facilities tab", () => Click( Hud.Screen, $"category{BuildCategory.Facilities}" ) );
		Wait( "the facilities tab shows", 2 );
		Do( "the tab lists the researched facilities of mission 0", () =>
		{
			Require( Hud.Category == BuildCategory.Facilities, "the fifth tab is the facilities'" );
			var ids = Hud.Catalog.GetItems( BuildCategory.Facilities ).Select( item => item.InfoId ).OrderBy( id => id ).ToArray();
			// the prebuilt's controls research the toilets, the bin and the staff room, not the kiosk (1200) or the camera (1421); the research room waits for mission 1, the training room for mission 2
			Require( ids.SequenceEqual( new[] { 1400, 1422, 1439, 3417 } ), $"the Science facilities of mission 0 are listed ({string.Join( ", ", ids )})" );
			var items = Hud.Catalog.GetItems( BuildCategory.Facilities ).ToList();
			chosenIndex = items.FindIndex( item => item.InfoId == 1400 );
			armedEntry = items[chosenIndex].Entry;
		} );
		Do( "arm the staff room through the list", ArmThroughList );
		Wait( "staff room arms", 2 );
		Do( "build the staff room beside the cleaner", () =>
		{
			Require( Level.BuildEntry == armedEntry && armedEntry!.InfoId == 1400, "the staff room is armed" );
			var cash = Level.Park!.Economy.Balance;
			staffRoom = PlaceFacilityNear( armedEntry!, hiredHandyman!.Navigation!.Cell );
			Require( Level.BuildEntry == null && cash - Level.Park.Economy.Balance == 500, "the staff room costs its Upgrades[0].CostOfUpgrade, 500, and the build disarms" );
			Require( Level.TpiStaffWorld!.Rooms!.RestAreas().Any( area => area.Thing == staffRoom.ThingId && area.CanLoad ), "the open staff room is a rest area" );
		} );
		Do( "the tired cleaner walks to the staff room", () =>
		{
			var economy = Level.Park!.Economy;
			var member = hiredHandyman!;
			member.Energy = economy.TpiStaff!.Settings.RestLevel;
			Level.TpiStaffWorld!.Search( economy, member, economy.Turn );
			Require( member.State == ThemeParkIncStaffWorld.ToRestArea && member.RestArea == staffRoom!.ThingId,
				$"the rest-area finder takes the staff room and the gate sends him there (state {member.State:X}, rest area {member.RestArea})" );
		} );
		Do( "open the build window for the bin", () => Click( Hud.Screen, "build" ) );
		Wait( "build window opens", 2 );
		Do( "choose the litter bin", () =>
		{
			Require( Hud.Category == BuildCategory.Facilities, "the tab stays the facilities'" );
			var items = Hud.Catalog.GetItems( BuildCategory.Facilities ).ToList();
			chosenIndex = items.FindIndex( item => item.InfoId == 1422 );
			armedEntry = items[chosenIndex].Entry;
		} );
		Do( "arm the bin through the list", ArmThroughList );
		Wait( "bin arms", 2 );
		Do( "the built bin takes the construction words", () =>
		{
			Require( Level.BuildEntry == armedEntry && armedEntry!.InfoId == 1422, "the bin is armed" );
			var bin = PlaceFacilityNear( armedEntry!, staffRoom!.ThingCell );
			// [BIN:TPI-EXE:0x5DB7D5 construction words] VAR_CAPACITY, VAR_DIRTY and VAR_FIRERISK from Standard.sam's LitterBin keys (20, 15 and 5 in the retail data)
			var words = ThemeParkIncStandard.ConstructionWords( ThemeParkIncStandard.Load( ParkEconomy.GameCareerFiles.Instance.Read, Level.LevelName )! );
			Require( bin.Runtime.Script is { } script && (script.Variables[3], script.Variables[11], script.Variables[12]) == (words.BinCapacity, words.BinDirty, words.BinFireRisk),
				"the Science bin's script holds the Standard words" );
		} );
	}

	/// <summary>Builds the armed facility on the free cell nearest <paramref name="near"/> whose entrance opens onto a walkable path cell (Level.PlaceObject, the world click's build).</summary>
	private OriginalObject PlaceFacilityNear( ObjectCatalogEntry entry, (int X, int Y) near )
	{
		var grid = Level.Guests!.Grid;
		var cells = Enumerable.Range( 0, grid.CountX * grid.CountY ).Select( index => (X: index % grid.CountX, Y: index / grid.CountX) )
			.OrderBy( cell => Math.Abs( cell.X - near.X ) + Math.Abs( cell.Y - near.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X );
		foreach ( var cell in cells )
		{
			if ( Level.CheckFreeObject( entry, cell.X, cell.Y, 0 ) != OriginalPlacementResult.Allowed )
				continue;
			var anchor = Level.GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			var doors = ObjectFootprint.GetAccessPoints( entry.Shape, anchor.X, anchor.Y, 0 ).Where( point => point.Kind == ObjectCellKind.Entrance ).ToArray();
			if ( doors.Length > 0 && !doors.All( door => grid.IsWalkable( door.OutsideX, door.OutsideY ) ) )
				continue;
			var placed = Level.PlaceObject( entry, cell.X, cell.Y, 0 );
			Require( placed != null && placed.Entry == entry && placed.ThingId > 0, $"{entry.DisplayName} is built with a thing of the park" );
			return placed!;
		}
		Require( false, $"find a site for {entry.DisplayName}" );
		return null!;
	}

	/// <summary>
	/// A mechanic hired from the pool (when the pool holds one) and dropped beside the cleaner: the park's turns update him in a mechanic state, his sprite shows a frame of the mechanics'
	/// bank, and the rides go on wearing by Theme Park Inc's rules (the continuation check later covers him and the gauges).
	/// </summary>
	private void PlanMechanic()
	{
		Do( "hire a mechanic and drop him beside the cleaner", () =>
		{
			var staff = Level.Park!.Economy.TpiStaff!;
			var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).FirstOrDefault( index => staff.Slots[index]?.Role == ThemeParkIncStaffRole.Mechanic, -1 );
			if ( slot < 0 )
			{
				Log.Trace( "Theme Park Inc slice smoke: the pool holds no mechanic; the mechanic step is skipped." );
				return;
			}
			Require( Level.BeginTpiHire( slot ) == null && Level.TpiPlacingSlot == slot, "a mechanic's hire starts" );
			var cell = hiredHandyman!.Navigation!.Cell;
			hiredMechanic = Level.DropTpiStaff( cell.X, cell.Y );
			Require( hiredMechanic is { Role: ThemeParkIncStaffRole.Mechanic, Placed: true } && Level.Things.ModelOf( hiredMechanic.ThingId ) == SaveThingModel.Mechanic,
				"the drop makes the mechanic and his thing" );
		} );
		Wait( "the mechanic works", 60 );
		Do( "the mechanic is updated by the park's turns", () =>
		{
			if ( hiredMechanic is not { } member )
				return;
			Require( member.LastUpdateTurn > handymanDropTurn, "the thing sweep updated the mechanic" );
			Require( member.State is ThemeParkIncStaffWorld.Idle or ThemeParkIncStaffWorld.Walking or ThemeParkIncStaffWorld.ToRide or ThemeParkIncStaffWorld.Repairing, "in a mechanic state" );
			Require( member.Sprite.Output != null && Level.TpiStaffWorld!.SpriteBanks?.BankCount( StaffType.Mechanic ) > 0, "the mechanic's sprite source shows a frame of the mechanics' bank" );
			var rides = Level.Park!.Economy.Objects.Where( item => item.Kind == ParkObjectKind.Ride ).ToList();
			Log.Trace( $"Theme Park Inc slice smoke: mechanic {member.ThingId} in state {member.State} with ride {member.RideId}; ride repair {string.Join( ", ", rides.Select( item => $"{item.InfoId}:{item.Repair:0.##}/{item.LifeGauge:0.##}" ) )}." );
		} );
	}

	/// <summary>
	/// A gardener hired from the pool (when the pool holds one) and dropped beside the cleaner: the park's turns update him in a gardener state and his sprite shows a frame of the gardeners'
	/// bank; every plant of the park runs vegetation.rse (the continuation check later covers him and the plants' scripts).
	/// </summary>
	private void PlanGardener()
	{
		Do( "every plant runs the vegetation script", () =>
		{
			var plants = Level.Objects.Objects.Where( item => !item.IsDeleted && item.Entry.Settings.GetBool( "UsageInfo.RequiresWatering" ) ).ToList();
			Require( plants.All( item => item.Runtime.Script is { ScriptName: "Vegetation Feature" } ), "a Theme Park Inc plant runs data\\levels\\vegetation.rse (0x5DE61A)" );
			Log.Trace( $"Theme Park Inc slice smoke: {plants.Count} plant(s) in the park, conditions {string.Join( ",", plants.Select( item => item.Runtime.Script!.Variables[ParkEconomy.PlantCondition] ) )}." );
		} );
		Do( "hire a gardener and drop him beside the cleaner", () =>
		{
			var staff = Level.Park!.Economy.TpiStaff!;
			var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).FirstOrDefault( index => staff.Slots[index]?.Role == ThemeParkIncStaffRole.Gardener, -1 );
			if ( slot < 0 )
			{
				Log.Trace( "Theme Park Inc slice smoke: the pool holds no gardener; the gardener step is skipped." );
				return;
			}
			Require( Level.BeginTpiHire( slot ) == null && Level.TpiPlacingSlot == slot, "a gardener's hire starts" );
			var cell = hiredHandyman!.Navigation!.Cell;
			hiredGardener = Level.DropTpiStaff( cell.X, cell.Y );
			Require( hiredGardener is { Role: ThemeParkIncStaffRole.Gardener, Placed: true } && Level.Things.ModelOf( hiredGardener.ThingId ) == SaveThingModel.Gardener,
				"the drop makes the gardener and his thing" );
		} );
		Wait( "the gardener works", 60 );
		Do( "the gardener is updated by the park's turns", () =>
		{
			if ( hiredGardener is not { } member )
				return;
			Require( member.LastUpdateTurn > handymanDropTurn, "the thing sweep updated the gardener" );
			Require( member.State is ThemeParkIncStaffWorld.Idle or ThemeParkIncStaffWorld.Walking or ThemeParkIncStaffWorld.ToPlant or ThemeParkIncStaffWorld.Watering, "in a gardener state" );
			Require( member.Sprite.Output != null && Level.TpiStaffWorld!.SpriteBanks?.BankCount( StaffSpriteRules.ThemeParkIncGardener ) > 0, "the gardener's sprite source shows a frame of the gardeners' bank" );
			Log.Trace( $"Theme Park Inc slice smoke: gardener {member.ThingId} in state {member.State} with plant {member.PlantId}." );
		} );
	}

	/// <summary>Hires an entertainer and a guard, drops them beside the cleaner and sees them updated, their cell effects on the grid and their sprites showing a frame.</summary>
	private void PlanPerformers()
	{
		Do( "hire an entertainer and a guard and drop them beside the cleaner", () =>
		{
			var staff = Level.Park!.Economy.TpiStaff!;
			ThemeParkIncStaffMember? Hire( ThemeParkIncStaffRole role )
			{
				var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).FirstOrDefault( index => staff.Slots[index]?.Role == role, -1 );
				if ( slot < 0 )
				{
					Log.Trace( $"Theme Park Inc slice smoke: the pool holds no {role}; its step is skipped." );
					return null;
				}
				Require( Level.BeginTpiHire( slot ) == null && Level.TpiPlacingSlot == slot, $"a {role}'s hire starts" );
				var cell = hiredHandyman!.Navigation!.Cell;
				var member = Level.DropTpiStaff( cell.X, cell.Y );
				Require( member is { Placed: true } && member.Role == role && Level.Things.ModelOf( member.ThingId ) == ThemeParkIncStaffWorld.ThingModelOf( role ), $"the drop makes the {role} and his thing" );
				return member;
			}
			hiredEntertainer = Hire( ThemeParkIncStaffRole.Entertainer );
			hiredGuard = Hire( ThemeParkIncStaffRole.Guard );
		} );
		Wait( "the entertainer and the guard work", 60 );
		Do( "the entertainer and the guard are updated by the park's turns", () =>
		{
			var grid = Level.Park!.Economy.Litter.Environment;
			if ( hiredEntertainer is { } entertainer )
			{
				Require( entertainer.LastUpdateTurn > handymanDropTurn, "the thing sweep updated the entertainer" );
				Require( entertainer.State is ThemeParkIncStaffWorld.Idle or ThemeParkIncStaffWorld.Walking or ThemeParkIncStaffWorld.Performing, "in an entertainer state" );
				Require( entertainer.Sprite.Output != null && Level.TpiStaffWorld!.SpriteBanks?.BankCount( StaffType.Entertainer ) > 0, "the entertainer's sprite source shows a frame of the theme's bank" );
				var cell = ThemeParkIncStaffWorld.CellOfWord( (ushort)entertainer.EffectCell );
				Require( grid.At( cell.X, cell.Y ).Happiness != 0, "the entertainer's row 0 is on his cell" );
				Log.Trace( $"Theme Park Inc slice smoke: entertainer {entertainer.ThingId} in state {entertainer.State}, effect radius {entertainer.EffectRadius}." );
			}
			if ( hiredGuard is { } guard )
			{
				Require( guard.LastUpdateTurn > handymanDropTurn, "the thing sweep updated the guard" );
				Require( guard.State is ThemeParkIncStaffWorld.Idle or ThemeParkIncStaffWorld.Walking, "in a guard state" );
				Require( guard.Sprite.Output != null && Level.TpiStaffWorld!.SpriteBanks?.BankCount( StaffType.Guard ) > 0, "the guard's sprite source shows a frame of the guards' bank" );
				var cell = ThemeParkIncStaffWorld.CellOfWord( (ushort)guard.EffectCell );
				Require( grid.At( cell.X, cell.Y ).Security != 0, "the guard's row 3 is on his cell" );
				Log.Trace( $"Theme Park Inc slice smoke: guard {guard.ThingId} in state {guard.State}, effect radius {guard.EffectRadius}." );
			}
		} );
	}

	/// <summary>The camera close over the cleaner, the staff renderer drawing him, and (with OPENTPW_TPI_STAFF_CAPTURE naming a .png file) the world written for a visual check.</summary>
	private void PlanCleanerView()
	{
		Do( "look at the cleaner", () =>
		{
			var camera = Camera.Mode as ParkCameraMode;
			Require( camera != null, "the slice runs the park camera" );
			var member = hiredHandyman!;
			var focus = OriginalParkPlacement.GetCellCenter( Level.OriginalPark!.Heightfield, (int)member.DrawX, (int)member.DrawY );
			camera!.Focus( focus );
		} );
		Wait( "the camera moves to the cleaner", 2 );
		Do( "zoom in on the cleaner", () =>
		{
			var camera = (ParkCameraMode)Camera.Mode;
			camera.SetPose( camera.Target, 90f, camera.Yaw );
		} );
		Wait( "the camera settles over the cleaner", 20 );
		Do( "the staff renderer draws the cleaner", () =>
		{
			Require( Level.StaffRenderer is { DrawnStaff: >= 1 }, $"the cleaner is drawn (drawn {Level.StaffRenderer?.DrawnStaff})" );
			if ( Environment.GetEnvironmentVariable( "OPENTPW_TPI_STAFF_CAPTURE" ) is { Length: > 0 } path )
				WorldCapture.Write( path );
		} );
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
		var focus = OriginalParkPlacement.GetCellCenter( Level.OriginalPark!.Heightfield, site.X, site.Y );
		if ( armedEntry!.HasQueue )
		{
			// Centre between the site and the end of its queue line: a site near the map edge puts the queue's end off the bottom of the
			// screen when the camera is centred on the site alone.
			var (dx, dy) = (entrance.OutsideX - entrance.X, entrance.OutsideY - entrance.Y);
			var end = OriginalParkPlacement.GetCellCenter( Level.OriginalPark!.Heightfield, entrance.OutsideX + dx * SmokeQueueLength, entrance.OutsideY + dy * SmokeQueueLength );
			focus = (focus + end) / 2;
		}
		camera!.Focus( focus );
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
		// The fourth draw (after base speed, exit variation and within-group variant)
		// gives retail generation rolls of 74 and 94 for these two starting states.
		for ( var index = 0; index < near.Length; index++ )
		{
			Level.Guests.World.Reseed( index == 0 ? 8u : 5u );
			var guest = Level.Guests.SpawnInPark( near[index].X, near[index].Y );
			Require( Level.Guests.TpiGeneration( guest ) == index + 1, "the smoke spawns a parent and a grandparent" );
			var body = Level.GuestRenderer!.SelectSprite( guest, System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitY );
			Require( body.Atlas >= 4 + index * 2 && body.Atlas < 6 + index * 2 && Level.RideHeadRenderer!.AtlasFor( guest ) == body.Atlas,
				"the body and rider head select the same bank within the guest's generation" );
		}
	}

	/// <summary>Whether a HUD message is the selected language's text for <paramref name="key"/> with its {0}, {1} ... filled in.</summary>
	private bool FitsTemplate( string message, OpenTpwText key ) =>
		System.Text.RegularExpressions.Regex.IsMatch( message, "^" + System.Text.RegularExpressions.Regex.Replace( System.Text.RegularExpressions.Regex.Escape( flow.Strings.Extra( key ) ), @"\\\{\d+\}", ".+" ) + "$", System.Text.RegularExpressions.RegexOptions.Singleline );

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

	public void Dispose()
	{
		watchdog.Dispose();
		personnelFileSaves?.Dispose();
	}
}
