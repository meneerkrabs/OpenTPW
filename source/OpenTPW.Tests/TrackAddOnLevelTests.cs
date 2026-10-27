using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A track add-on against a real <see cref="Level"/> (docs/TRACKS.md "Add-ons"), asset-free: the build tool's target puts a jump on the ride's straight, the walk after the
/// placement links the add-on section, a click on the add-on opens its ride's window, and selling the add-on walks the circuit back to plain sections. The level is assembled
/// without a GPU from its own track set-up, with the footprint handlers wired after it as the level wires them.
/// </summary>
[TestClass, DoNotParallelize]
public partial class TrackAddOnLevelTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private string root = null!;
	private BaseFileSystem previousFiles = null!;
	private List<Entity> previousEntities = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Initialize()
	{
		previousFiles = FileSystem; previousEntities = Entity.All; previousLog = Log;
		Entity.All = new(); Log = new Logger();
		root = Path.Combine( Path.GetTempPath(), "opentpw-addon-level-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( Path.Combine( root, "synthetic" ) );
		var model = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 );
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		File.WriteAllBytes( Path.Combine( root, "synthetic/model.MD2" ), model );
		File.WriteAllBytes( Path.Combine( root, "synthetic/script.RSE" ), new RideVMTests.Asm().I( Opcode.END ).Build( Enum.GetNames<RideVariables>() ) );
		FileSystem = new BaseFileSystem( root );
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var entity in Entity.All.ToArray() ) entity.Delete();
		Entity.All = previousEntities; FileSystem = previousFiles; Log = previousLog;
		Directory.Delete( root, true );
	}

	private ObjectCatalogEntry Entry( ObjectCategory category, string sam, params string[] shape ) =>
		new( "jungle", category, "/synthetic", new ObjectSettings( new[] { new ObjectSettingsFile( sam, "synthetic.sam" ) } ), new ObjectShape( shape ), "/synthetic/model.MD2", null,
			"/synthetic/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), FileSystem, null );

	/// <summary>A go-kart ride (the Dino Karts' controller type −4) and its jump (the Dino Karts Jump's footprint and Bumper.*Adjust).</summary>
	private (ObjectCatalogEntry Ride, ObjectCatalogEntry Jump) Entries( bool hasQueue = false ) =>
		(Entry( ObjectCategory.Ride, "Info.Id 1150\nInfo.Name SyntheticKarts\nInfo.WhichUIType 0\nInfo.IsChoosable 1\nBumper.WhichTrackType 1\nBumper.BumperType -4\n" + (hasQueue ? "Info.HasQueue 1\n" : ""), "*" ),
		Entry( ObjectCategory.Upgrade, "Info.Id 1500\nInfo.Name SyntheticJump\nInfo.WhichUIType 0\nAddOn.UpgradesId 1150\nAddOn.UpgradeType 1\nBumper.WhichTrackType 1\n"
			+ "Bumper.NorthXAdjust 1\nBumper.NorthYAdjust 0\nBumper.EastXAdjust 0\nBumper.EastYAdjust 0\nBumper.SouthXAdjust 0\nBumper.SouthYAdjust -1\nBumper.WestXAdjust 1\nBumper.WestYAdjust -1\n", "++", "++" ));

	private static void Set( object target, string field, object? value ) => typeof( Level ).GetField( field, Private )!.SetValue( target, value );

	/// <summary>
	/// The level with the ride placed at (10, 9) (its station tiles (10, 10) and (12, 10)) and the long kart loop of <see cref="TrackAddOnTests"/>: the straights
	/// (14, 10) … (28, 10) and (28, 14) … (10, 14) between the corners (30, 10), (30, 14), (8, 14) and (8, 10). Without <paramref name="laid"/> the station stands alone;
	/// <paramref name="extra"/> joins the catalogue.
	/// </summary>
	private (Level Level, OriginalObject Ride, ObjectCatalogEntry Jump) World( bool hasQueue = false, bool laid = true, ObjectCatalogEntry? extra = null )
	{
		var (rideEntry, jump) = Entries( hasQueue );
		var entries = extra == null ? new[] { rideEntry, jump } : new[] { rideEntry, jump, extra };
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), Private, null, new object[] { "jungle", entries }, null )!;
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		var objects = new ParkObjects( catalog, new SandboxParkGrid() );
		var guests = new GuestSimulation( new GuestPathGrid( 48, 32 ), GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		Set( level, "<Objects>k__BackingField", objects );
		Set( level, "<Guests>k__BackingField", guests );
		Set( level, "<LastActionMessage>k__BackingField", "" );
		Set( level, "<CellTool>k__BackingField", new CellBuildTool() );
		Set( level, "trackStations", new Dictionary<OriginalObject, TrackStation>() );
		Set( level, "trackRenderers", new Dictionary<TrackFamily, TrackRenderer>() );
		Set( level, "trackPieceAnimations", new TrackPieceAnimations() );
		Set( level, "waterPieceHandlers", new Dictionary<OriginalObject, Action<bool>>() );
		Set( level, "addOnSurfaces", new Dictionary<ObjectCatalogEntry, TrackSurface?>() );
		typeof( Level ).GetMethod( "SetupTracks", Private )!.Invoke( level, null );
		// the footprint handlers the level wires after its track set-up (Level.ConnectObjectsToGuests), in that order
		var cells = guests.Grid.Cells;
		objects.ObjectPlaced += item => cells.WriteFootprint( item.Cells, Level.OwnerWord( item ), item.Placement.Rotation );
		objects.ObjectRemoved += item => cells.ClearFootprint( item.Cells );
		var ride = objects.TryPlace( rideEntry, 10, 9, 0, out _ );
		Assert.IsNotNull( ride?.Runtime.Bump, "the kart ride and its controller" );
		var station = level.TrackStations[ride!];
		Assert.AreEqual( (10, 10), station.First );
		if ( !laid )
			return (level, ride!, jump);
		var writer = new TrackLineWriter( level.KartTracks!, null, level.KartTracks!.OwnerOf( station ) );
		writer.Commit( (14, 10), (30, 10) );
		writer.Commit( (30, 10), (30, 14) );
		writer.Commit( (30, 14), (8, 14) );
		writer.Commit( (8, 14), (8, 10) );
		Assert.IsTrue( level.RewalkTrack( ride!, TrackWalkCause.Line ) is { IsValid: true }, "the loop closes" );
		return (level, ride!, jump);
	}

	private static TrackSection SectionAt( OriginalObject ride, int x, int y ) =>
		ride.Runtime.Bump!.Sections.Single( section => (section.X / TrackSections.Cell, section.Y / TrackSections.Cell) == (x, y) );

	[TestMethod]
	public void AJumpPlacedFromTheToolIsLinkedOpensItsRidesWindowAndItsSaleWalksTheCircuitBack()
	{
		var (level, ride, jump) = World();
		Assert.IsTrue( level.BeginAddOn( jump, ride ) );
		Assert.AreSame( ride, level.BuildAddOnRide );
		// the pointer on the far cell of the straight (16, 10), which flows +X: the hover turns the jump along it (0x005236B0)
		var (x, y, rotation) = level.BuildTarget( jump, 17, 11 );
		Assert.AreEqual( 270, rotation );
		Assert.AreEqual( OriginalPlacementResult.Allowed, level.CheckBuildTarget( jump, x, y, rotation ) );
		var addOn = (OriginalObject?)typeof( Level ).GetMethod( "PlaceAddOnUnpaid", Private )!.Invoke( level, new object[] { jump, x, y, rotation, ride } );
		Assert.IsNotNull( addOn );
		CollectionAssert.AreEquivalent( TrackStation.TileCellsOf( 16, 10 ).ToArray(), addOn!.Cells.Select( cell => (cell.X, cell.Y) ).ToArray(), "the jump covers the hovered tile" );
		var bump = ride.Runtime.Bump!;
		Assert.IsTrue( bump.IsTrackValid );
		// the walk after the placement (0x00524D0A): the flagged straight builds kind 12 and the section points at the jump
		var section = SectionAt( ride, 16, 10 );
		Assert.AreEqual( 12, section.Kind );
		Assert.AreEqual( 1500, section.AddOn?.TypeId );
		Assert.AreEqual( 9, SectionAt( ride, 18, 10 ).Kind );
		// a click on the add-on opens its ride's window (0x00486920)
		Assert.AreSame( ride, level.WindowTarget( addOn ) );
		Assert.AreSame( ride, level.WindowTarget( ride ) );

		// the sale: the flags come off and the circuit is walked at once ([EXT:addon-sale-walk]), whichever removal handler runs first
		level.Objects.Remove( addOn );
		foreach ( var (cx, cy) in TrackStation.TileCellsOf( 16, 10 ) )
			Assert.AreEqual( 0, level.KartTracks!.Tracks.Get( cx, cy ).Flags & TrackCell.FlagAddOn, $"({cx}, {cy}) loses the add-on flag" );
		Assert.IsTrue( bump.IsTrackValid );
		Assert.IsFalse( bump.Sections.Any( candidate => candidate.Kind is 12 or 13 || candidate.AddOn != null ), "no section keeps the sold add-on" );
		Assert.AreEqual( 9, SectionAt( ride, 16, 10 ).Kind );
		Assert.IsNull( level.WindowTarget( addOn ), "a removed add-on opens nothing" );
	}

	[TestMethod]
	public void AnAddOnOffItsRidesTrackIsRefusedAndOpensNoWindow()
	{
		var (level, ride, jump) = World();
		Assert.IsTrue( level.BeginAddOn( jump, ride ) );
		Assert.AreEqual( OriginalPlacementResult.Blocked, level.CheckBuildTarget( jump, 20, 20, 0 ), "free ground holds none of the ride's track" );
		Assert.AreEqual( OriginalPlacementResult.Blocked, level.CheckBuildTarget( jump, 30, 10, 0 ), "a corner tile" );
		var stray = level.Objects.TryPlace( jump, 20, 20, 0, out _ );
		Assert.IsNotNull( stray, "placed without the add-on rule, as an ordinary object on free ground" );
		Assert.IsNull( level.WindowTarget( stray ), "an add-on on no ride's track opens nothing" );
	}

	// ---- TRACK-018: the same real-level fixture exercises the track edit door and the live tool ingress. ----

	private static ParkObjectState AttachTrackEconomy( Level level, OriginalObject ride )
	{
		var economy = EconomyTestData.Park();
		var state = economy.RegisterExisting( 1100 );
		Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) );
		Set( level, "economyInstances", new Dictionary<OriginalObject, int> { [ride] = state.Id } );
		state.IsOpen = false;
		ride.Close();
		return state;
	}

	[DataTestMethod]
	[DataRow( TrackWalkCause.Line, true )]
	[DataRow( TrackWalkCause.Edit, true )]
	[DataRow( TrackWalkCause.Restore, false )]
	[DataRow( TrackWalkCause.Placement, false )]
	[DataRow( TrackWalkCause.AddOn, false )]
	public void OnlyExplicitLineAndEditWalksReopenAValidCircuit( TrackWalkCause cause, bool opens )
	{
		var (level, ride, _) = World();
		var state = AttachTrackEconomy( level, ride );
		Assert.IsTrue( level.RewalkTrack( ride, cause ) is { IsValid: true } );
		Assert.AreEqual( opens, ride.IsOpen, "the model door follows the qualifying operation cause" );
		Assert.AreEqual( opens, ride.Visitors.IsOpen, "the visitor bridge reads the same door and accepted circuit" );
		Assert.AreEqual( opens, state.IsOpen, "the economy is synchronized before another simulation tick or save" );
	}

	[DataTestMethod]
	[DataRow( "broken" )]
	[DataRow( "condemned" )]
	[DataRow( "upgrading" )]
	[DataRow( "maintenance" )]
	[DataRow( "script-broken" )]
	[DataRow( "disconnected-queue" )]
	public void AValidTrackEditHonoursTheOrdinaryAttractionReopenGuards( string blocker )
	{
		var (level, ride, _) = World();
		var state = AttachTrackEconomy( level, ride );
		if ( blocker == "condemned" ) state.LifeGauge = 0;
		if ( blocker is "broken" or "condemned" ) state.IsBrokenDown = true;
		if ( blocker == "upgrading" ) state.PendingLevel = state.Level + 1;
		if ( blocker == "maintenance" ) state.MaintenanceRequested = true;
		// the script's VAR_BROKEN made the ride update enter state 1: the guard reads the state word (0xde654)
		if ( blocker == "script-broken" ) { ride.Runtime.SetVariable( nameof( RideVariables.VAR_BROKEN ), 1 ); state.StateWord.Set( RideObjectStates.BrokenDown ); }
		if ( blocker == "disconnected-queue" )
		{
			ride.Visitors.Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 );
			ride.Visitors.QueueFrontCell = ride.Visitors.EntranceCell = (4, 4);
			level.Guests!.Grid.Cells.SetType( 4, 4, ParkCellType.Queue );
		}
		Assert.IsTrue( level.RewalkTrack( ride, TrackWalkCause.Edit ) is { IsValid: true } );
		Assert.IsFalse( ride.IsOpen || ride.Visitors.IsOpen || state.IsOpen, blocker );
	}

	[TestMethod]
	public void CuttingARealLevelSeedsBackspaceAndMendingAfterUndoSynchronizesTheDoor()
	{
		var (level, ride, _) = World();
		var state = AttachTrackEconomy( level, ride );
		ride.Open();
		state.IsOpen = true;
		Assert.IsTrue( level.EnterTrackTool( ride ) );
		Assert.AreEqual( CellToolMode.KartEdit, level.CellTool.Mode );
		level.CellTool.Click( (24, 14) );
		level.CellTool.Click( (22, 14) );
		Assert.AreEqual( CellToolMode.KartTrack, level.CellTool.Mode );
		Assert.AreEqual( (26, 14), level.CellTool.Start );
		Assert.IsTrue( level.CellTool.SegmentCount > 0, "the surviving old corners are already undoable after a cut" );
		Assert.IsFalse( ride.IsOpen || ride.Visitors.IsOpen || state.IsOpen, "a broken circuit is closed immediately" );
		var beforeUndo = level.Park!.Economy.Balance;
		Assert.IsTrue( level.CellTool.Undo(), "Backspace uses the live editor and its level refund hook" );
		Assert.AreEqual( (30, 14), level.CellTool.Start );
		Assert.AreEqual( beforeUndo + 2 * level.KartTracks!.TileCost, level.Park.Economy.Balance );
		Assert.IsFalse( ride.IsOpen || ride.Visitors.IsOpen || state.IsOpen );
		var mend = level.CellTool.Click( (20, 14) );
		Assert.IsTrue( mend?.Completed == true );
		Assert.IsTrue( ride.Runtime.Bump!.IsTrackValid );
		Assert.IsTrue( ride.IsOpen && ride.Visitors.IsOpen && state.IsOpen );
		Assert.IsFalse( level.CellTool.IsActive, "this queue-less synthetic ride has no next queue tool" );
	}

	[TestMethod]
	public void ADeliberatelyClosedRideStaysClosedThroughTrackSnapshotRestore()
	{
		var (level, ride, _) = World();
		var state = AttachTrackEconomy( level, ride );
		var saved = level.CaptureTracksSnapshot();
		Assert.IsNotNull( saved );
		level.RestoreTracksSnapshot( saved );
		Assert.IsTrue( ride.Runtime.Bump!.IsTrackValid );
		Assert.IsFalse( ride.IsOpen || ride.Visitors.IsOpen || state.IsOpen );
	}

	[DataTestMethod]
	[DataRow( 0xc, true )]
	[DataRow( 0, false )]
	[DataRow( 0xf, false )]
	[DataRow( 0x3c, false )]
	public void OnlyExactlyCtrlKeepsACompletedTrackLineWithoutAStart( int modifiers, bool stays )
	{
		var (level, ride, _) = World();
		AttachTrackEconomy( level, ride );
		CutForLiveMend( level, ride );
		var writer = level.CellTool.Writer;
		var previousCommit = level.CellTool.LastCommit;
		level.CellTool.Hover( (20, 14) );
		Assert.IsNotNull( level.CellTool.Ghost );
		Assert.IsTrue( level.ClickCellTool( (20, 14), modifiers )?.Completed == true );
		Assert.IsTrue( ride.Runtime.Bump!.IsTrackValid );
		Assert.AreEqual( stays, level.CellTool.IsActive );
		Assert.IsNull( level.CellTool.Start );
		Assert.IsNull( level.CellTool.Ghost );
		if ( stays )
			Assert.IsNull( level.CellTool.LastCommit, "the just-finished segment must not refill a reset tool" );
		else
			Assert.AreSame( previousCommit, level.CellTool.LastCommit, "ordinary End retains its existing last-commit behavior" );
		Assert.AreEqual( 0, level.CellTool.SegmentCount );
		Assert.IsFalse( level.CellTool.Undo(), "no stale line remains undoable" );
		if ( stays )
		{
			Assert.AreSame( writer, level.CellTool.Writer, "same-writer reset is intentional" );
			Assert.AreSame( ride, level.TrackToolStation );
			Assert.IsNull( level.ClickCellTool( (20, 14) ), "the next click only picks a new start" );
			Assert.AreEqual( (20, 14), level.CellTool.Start );
			Assert.AreEqual( 0, level.CellTool.SegmentCount );
		}
	}

	/// <summary>
	/// The HUD's own tool click (<c>ParkHud.UpdateCellTool</c>, the click a player makes) carries the frame's modifiers to the line's continuation, as the level's click does
	/// (<see cref="OnlyExactlyCtrlKeepsACompletedTrackLineWithoutAStart"/>): a kart line that closes its circuit with exactly Ctrl held and no queue start pending keeps the line tool
	/// without a start (tp.exe 0x527146), any other modifier ends it (0x527156).
	/// </summary>
	[DataTestMethod]
	[DataRow( true, false, true )]
	[DataRow( false, false, false )]
	[DataRow( true, true, false )]
	public void TheHudsToolClickCarriesItsModifiersToTheLinesContinuation( bool ctrl, bool shift, bool stays )
	{
		var (level, ride, _) = World();
		AttachTrackEconomy( level, ride );
		var hud = new Hud.ParkHud( level, OriginalUiTests.FakeStrings(), new Hud.EconomyParkStatus( () => level.Park!.Economy ), new NoBuildItems(), new Hud.HudHost() );
		CutForLiveMend( level, ride );
		var held = new List<Veldrid.Key>();
		if ( ctrl )
			held.Add( Veldrid.Key.ControlLeft );
		if ( shift )
			held.Add( Veldrid.Key.ShiftLeft );
		HudToolClick.Click( hud, (20, 14), held.ToArray() );
		Assert.IsTrue( ride.Runtime.Bump!.IsTrackValid, "the click closes the circuit" );
		Assert.AreEqual( stays, level.CellTool.IsActive );
		Assert.IsNull( level.CellTool.Start );
		Assert.AreEqual( stays ? ride : null, level.TrackToolStation );
	}

	/// <summary>
	/// The line tool's help 0xCC (204) through the HUD's own tool click: the line tool that a close with exactly Ctrl held kept without a start (tp.exe 0x527146) posts it for the
	/// next click, which stores the start (0x526D41, the queue tool's post 0x527629); the click that kept the tool and the clicks of a line tool with a start post none. A ride
	/// with a queue, as every shipped kart and water ride has, gets there too when no placement's queue start is pending (the track button's line tool).
	/// </summary>
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TheClickThatGivesTheKeptLineToolItsStartPostsHelp204( bool hasQueue )
	{
		var (level, ride, _) = World( hasQueue );
		AttachTrackEconomy( level, ride );
		if ( hasQueue )
			WithQueue( ride );
		var posted = new List<int>();
		var hud = new Hud.ParkHud( level, OriginalUiTests.FakeStrings(), new Hud.EconomyParkStatus( () => level.Park!.Economy ), new NoBuildItems(), new Hud.HudHost { HelpPosted = posted.Add } );
		CutForLiveMend( level, ride );
		HudToolClick.Click( hud, (20, 14), Veldrid.Key.ControlLeft );
		Assert.IsTrue( ride.Runtime.Bump!.IsTrackValid && level.CellTool.IsActive && level.CellTool.Start == null, "the close kept the line tool without a start" );
		Assert.AreEqual( 0, posted.Count, "the click that closes the circuit posts nothing (0x527146 returns)" );
		HudToolClick.Click( hud, (22, 14) );
		Assert.AreEqual( (22, 14), level.CellTool.Start );
		CollectionAssert.AreEqual( new[] { 204 }, posted, "the click that stores the start" );
		HudToolClick.Click( hud, (26, 14) );
		CollectionAssert.AreEqual( new[] { 204 }, posted, "a click with a start posts nothing" );
	}

	/// <summary>
	/// A click of the kept line tool on a tile its ghost refuses ends the tool and posts no help: tp.exe tests the refused-ghost latch before the mode dispatch
	/// (0x524A63), and the line modes go to SetMode(0, 0) (0x5266A3) before the start's store and its help 0xCC (0x526CB3).
	/// </summary>
	[TestMethod]
	public void ARefusedTileEndsTheKeptLineToolWithoutHelp204()
	{
		var (level, ride, _) = World();
		AttachTrackEconomy( level, ride );
		var posted = new List<int>();
		var hud = new Hud.ParkHud( level, OriginalUiTests.FakeStrings(), new Hud.EconomyParkStatus( () => level.Park!.Economy ), new NoBuildItems(), new Hud.HudHost { HelpPosted = posted.Add } );
		CutForLiveMend( level, ride );
		HudToolClick.Click( hud, (20, 14), Veldrid.Key.ControlLeft );
		Assert.IsTrue( level.CellTool.IsActive && level.CellTool.Start == null );
		// (21, 14) is an extension cell of the tile at (20, 14): the tile with its origin there is refused (Occupied)
		Assert.AreEqual( CellBuildResult.Occupied, level.KartTracks!.CheckTile( 21, 14 ) );
		HudToolClick.Click( hud, (21, 14) );
		Assert.IsFalse( level.CellTool.IsActive, "the refused single-tile ghost ends the tool" );
		Assert.AreEqual( 0, posted.Count );
	}

	[TestMethod]
	public void TheLineToolOfAnOpenCircuitStartsAtItsOpenEndAndPostsNoHelp204()
	{
		var (level, ride, _) = World( laid: false );
		AttachTrackEconomy( level, ride );
		var posted = new List<int>();
		var hud = new Hud.ParkHud( level, OriginalUiTests.FakeStrings(), new Hud.EconomyParkStatus( () => level.Park!.Economy ), new NoBuildItems(), new Hud.HudHost { HelpPosted = posted.Add } );
		Assert.IsTrue( level.EnterTrackTool( ride ) );
		Assert.AreEqual( CellToolMode.KartTrack, level.CellTool.Mode );
		Assert.IsNotNull( level.CellTool.Start, "the open end is the start (0x530830, then 0x52F580 keeps it)" );
		HudToolClick.Click( hud, (20, 10) );
		Assert.AreEqual( 0, posted.Count );
	}

	private sealed class NoBuildItems : Hud.IBuildCatalog
	{
		public IReadOnlyList<Hud.BuildItem> GetItems( Hud.BuildCategory category ) => Array.Empty<Hud.BuildItem>();
	}

	private static void CutForLiveMend( Level level, OriginalObject ride )
	{
		Assert.IsTrue( level.EnterTrackTool( ride ) );
		level.ClickCellTool( (24, 14) );
		level.ClickCellTool( (22, 14) );
		Assert.AreEqual( CellToolMode.KartTrack, level.CellTool.Mode );
		Assert.IsTrue( level.CellTool.Undo() );
		Assert.AreEqual( (30, 14), level.CellTool.Start );
	}

	[DataTestMethod]
	[DataRow( TrackWalkCause.Edit )]
	[DataRow( TrackWalkCause.Restore )]
	[DataRow( TrackWalkCause.Placement )]
	public void ALaterNonLineWalkDoesNotInheritTheClickModifier( TrackWalkCause cause )
	{
		var (level, ride, _) = World();
		AttachTrackEconomy( level, ride );
		CutForLiveMend( level, ride );
		level.ClickCellTool( (20, 14), 0xc );
		Assert.IsTrue( level.CellTool.IsActive );
		Assert.IsTrue( level.RewalkTrack( ride, cause ) is { IsValid: true } );
		Assert.IsFalse( level.CellTool.IsActive );
	}

	[TestMethod]
	public void HeadlessBuildAfterACtrlClickDoesNotInheritItsContinuation()
	{
		var (level, ride, _) = World();
		AttachTrackEconomy( level, ride );
		CutForLiveMend( level, ride );
		level.ClickCellTool( (20, 14), 0xc );
		Assert.IsTrue( level.CellTool.IsActive );
		Assert.IsTrue( level.BuildTrack( ride, (22, 14), (20, 14) )?.Completed == true );
		Assert.IsFalse( level.CellTool.IsActive );
	}

	[TestMethod]
	public void AThrowingClickRestoresTheModifierScope()
	{
		var (level, ride, _) = World();
		level.CellTool.Enter( new ThrowingLineWriter(), (0, 0) );
		Assert.ThrowsException<InvalidOperationException>( () => level.ClickCellTool( (2, 0), 0xc ) );
		Assert.AreEqual( false, typeof( Level ).GetField( "keepTrackToolAfterLine", Private )!.GetValue( level ) );
		CutForLiveMend( level, ride );
		Assert.IsTrue( level.BuildTrack( ride, (30, 14), (20, 14) )?.Completed == true );
		Assert.IsFalse( level.CellTool.IsActive );
	}

	private sealed class ThrowingLineWriter : ICellLineWriter
	{
		public CellToolMode Mode => CellToolMode.KartTrack;
		public SegmentResult Preview( (int X, int Y) start, (int X, int Y) end ) => throw new InvalidOperationException();
		public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end ) => throw new InvalidOperationException();
		public void Undo( SegmentResult segment ) => throw new InvalidOperationException();
	}

}
