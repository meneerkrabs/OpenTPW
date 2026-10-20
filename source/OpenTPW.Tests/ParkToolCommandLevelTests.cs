using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] The staff and pool tools' writes as commands (docs/COOP.md, "Tools"), on the level <see cref="ParkSettingCommandLevelTests"/> builds without a GPU or game data:
/// each write the tool made leaves it as one command, the tool behaves as before, and the logged commands alone bring a twin park to the same hash.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkToolCommandLevelTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;

	[TestInitialize]
	public void Initialize()
	{
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
	}

	[TestCleanup]
	public void Cleanup()
	{
		fixture?.Cleanup();
		renderer?.Dispose();
	}

	private ParkSettingCommandLevelTests.Park NewPark( string extra = "" ) => ParkSettingCommandLevelTests.NewPark( fixture!, extra );

	/// <summary>Applies <paramref name="log"/> to <paramref name="twin"/> in order, each command as its issuer's result had it.</summary>
	private static void Replay( IEnumerable<ParkCommandLogEntry> log, Level twin, IReadOnlyList<bool> accepted )
	{
		var index = 0;
		foreach ( var entry in log )
			Assert.AreEqual( accepted[index++], twin.Commands.Apply( entry.Command ).Accepted, entry.Command.ToString() );
	}

	private static IEnumerable<(int X, int Y)> Cells( Level level ) =>
		Enumerable.Range( 0, level.Guests!.Grid.Cells.Height ).SelectMany( y => Enumerable.Range( 0, level.Guests.Grid.Cells.Width ).Select( x => (x, y) ) );

	[TestMethod]
	public void TheStaffToolsHireCarryAndDropThroughCommandsThatATwinFollows()
	{
		var park = NewPark();
		var level = park.Level;
		var placement = level.StaffPlacement!;
		var results = new List<bool>();
		level.Commands.Applied += ( _, result ) => results.Add( result.Accepted );
		var valid = Cells( level ).Where( cell => level.StaffHands!.IsValidCell( cell.X, cell.Y ) ).Take( 2 ).ToArray();
		Assert.AreEqual( 2, valid.Length, "the park has cells an employee may stand on" );
		var candidate = park.Economy.Staff.Candidates.First( item => park.Economy.Staff.CanHire( item.Type ) );
		var employed = park.Economy.Staff.Members.Count;

		Assert.IsTrue( placement.BeginHire( candidate.Id ) );
		Assert.AreEqual( candidate.Id, park.Economy.Staff.PendingPlacementId, "the hire marks the candidate as being placed" );
		Assert.AreEqual( employed, park.Economy.Staff.Members.Count, "nobody is employed before the click" );
		Assert.AreEqual( StaffClickResult.Placed, placement.Click( valid[0].X, valid[0].Y ) );
		var member = park.Economy.Staff.Members.Single( item => item.Id == candidate.Id );
		Assert.AreEqual( valid[0], StaffPlacementRules.CellOf( member ) );

		Assert.IsTrue( placement.PickUp( member ) );
		Assert.AreEqual( StaffState.PickedUp, member.State );
		placement.Reconcile();
		Assert.AreEqual( StaffState.PickedUp, member.State, "the frame's reconcile leaves an employee in hand" );
		Assert.AreEqual( StaffClickResult.Dropped, placement.Click( valid[1].X, valid[1].Y ) );
		Assert.AreEqual( valid[1], StaffPlacementRules.CellOf( member ) );
		Assert.IsTrue( placement.PickUp( member ) );
		placement.Cancel();
		Assert.AreEqual( valid[1], StaffPlacementRules.CellOf( member ), "a cancel drops the employee back where it was picked up" );
		Assert.AreEqual( StaffState.Idle, member.State );

		var next = park.Economy.Staff.Candidates.First( item => park.Economy.Staff.CanHire( item.Type ) );
		Assert.IsTrue( placement.BeginHire( next.Id ) );
		placement.Cancel();
		Assert.AreEqual( 0, park.Economy.Staff.PendingPlacementId, "a cancelled hire employs nobody" );

		CollectionAssert.AreEqual( new[] { ParkCommandKind.BeginStaffHire, ParkCommandKind.PlaceStaff, ParkCommandKind.PickUpStaff, ParkCommandKind.DropStaff, ParkCommandKind.PickUpStaff,
			ParkCommandKind.DropStaff, ParkCommandKind.BeginStaffHire, ParkCommandKind.CancelStaffHire }, level.Commands.Log.Select( entry => entry.Command.Kind ).ToArray() );
		Assert.IsTrue( results.All( accepted => accepted ) );

		var twin = NewPark();
		var log = level.Commands.Log.ToArray();
		// the twin's frame between the pick-up and the drop: a pick-up a command made stays picked up with no tool carrying it
		Replay( log.Take( 3 ), twin.Level, results );
		var twinMember = twin.Economy.Staff.Members.Single( item => item.Id == candidate.Id );
		twin.Level.StaffPlacement!.Reconcile();
		Assert.AreEqual( StaffState.PickedUp, twinMember.State );
		Replay( log.Skip( 3 ), twin.Level, results.Skip( 3 ).ToArray() );
		Assert.AreEqual( level.ComputeStateHash(), twin.Level.ComputeStateHash(), "the commands alone hire, carry and drop as the tool did" );
		Assert.IsFalse( twin.Level.StaffPlacement.IsActive, "a peer's or a replayed command moves no tool" );
	}

	[TestMethod]
	public void AStaffCommandRefusesWhatTheToolWouldNot()
	{
		var park = NewPark();
		var level = park.Level;
		var before = level.ComputeStateHash();
		var candidate = park.Economy.Staff.Candidates.First( item => park.Economy.Staff.CanHire( item.Type ) );
		var valid = Cells( level ).First( cell => level.StaffHands!.IsValidCell( cell.X, cell.Y ) );
		// a cell off the map is no cell any employee may stand on
		var invalid = (X: level.Guests!.Grid.Cells.Width + 1, Y: 0);
		var member = park.Economy.Staff.Members.First();
		Assert.IsFalse( level.Commands.Issue( new PlaceStaffCommand( candidate.Id, valid.X, valid.Y ) ).Accepted, "a candidate nobody began to place" );
		Assert.IsFalse( level.Commands.Issue( new DropStaffCommand( member.Id, valid.X, valid.Y ) ).Accepted, "an employee nobody holds" );
		Assert.IsFalse( level.Commands.Issue( new BeginStaffHireCommand( 99_999 ) ).Accepted, "no such candidate" );
		Assert.AreEqual( before, level.ComputeStateHash() );
		Assert.IsTrue( level.Commands.Issue( new BeginStaffHireCommand( candidate.Id ) ).Accepted );
		Assert.IsFalse( level.Commands.Issue( new PlaceStaffCommand( candidate.Id, invalid.X, invalid.Y ) ).Accepted, "a cell no employee may stand on" );
		Assert.AreEqual( candidate.Id, park.Economy.Staff.PendingPlacementId, "the refused click leaves the placement running" );
	}

	private sealed class NoItems : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => System.Array.Empty<BuildItem>();
	}

	/// <summary>The no-data park with two coaster stations.</summary>
	private ParkSettingCommandLevelTests.Park Coaster()
	{
		var park = NewPark( "Bumper.WhichTrackType 3\n" );
		// an uninitialised level has no field initialisers run: the coaster builders the tool keeps
		typeof( Level ).GetField( "coasterBuilders", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( park.Level, new Dictionary<OriginalObject, CoasterBuilder>() );
		Assert.IsNotNull( park.One.Runtime.Coaster );
		return park;
	}

	/// <summary>The coaster tool's entry and exit (the ride closed and opened, the controller's tool state, the being-edited mark that keeps guests off) are commands a twin follows.</summary>
	[TestMethod]
	public void TheCoasterToolsEntryAndExitAreCommandsThatMarkTheCoasterBeingEdited()
	{
		var park = Coaster();
		var level = park.Level;
		Assert.IsTrue( level.EnterCoasterTool( park.One ) );
		Assert.IsTrue( park.One.Runtime.Coaster!.BeingEdited, "no guest boards a coaster being edited" );
		Assert.IsFalse( park.One.IsOpen, "the tool closes the ride" );
		Assert.IsNotNull( level.CoasterPanel );
		Assert.AreNotEqual( CoasterPlaceResult.Ok, level.CoasterClick( 200, 200 ), "a click off the site places nothing (and is logged as refused)" );
		Assert.IsTrue( level.EnterCoasterTool( park.Two ), "another coaster's tool ends this one's first" );
		Assert.IsFalse( park.One.Runtime.Coaster.BeingEdited );
		Assert.IsTrue( park.Two.Runtime.Coaster!.BeingEdited );
		level.ExitCoasterTool();
		Assert.IsFalse( park.Two.Runtime.Coaster.BeingEdited );
		Assert.IsFalse( level.IsCoasterToolActive );
		CollectionAssert.AreEqual( new[] { ParkCommandKind.BeginCoasterTool, ParkCommandKind.CoasterClick, ParkCommandKind.EndCoasterTool, ParkCommandKind.BeginCoasterTool,
			ParkCommandKind.EndCoasterTool }, level.Commands.Log.Select( entry => entry.Command.Kind ).ToArray() );

		var twin = Coaster();
		var results = new List<bool>();
		var marks = new List<(bool One, bool Two)>();
		foreach ( var entry in level.Commands.Log )
		{
			results.Add( twin.Level.Commands.Apply( entry.Command ).Accepted );
			marks.Add( (twin.One.Runtime.Coaster!.BeingEdited, twin.Two.Runtime.Coaster!.BeingEdited) );
		}
		CollectionAssert.AreEqual( new[] { true, false, true, true, true }, results, "the click off the site is refused on the twin too" );
		CollectionAssert.AreEqual( new[] { (true, false), (true, false), (false, false), (false, true), (false, false) }, marks, "the twin marks the coaster being edited as the tool did" );
		Assert.AreEqual( level.ComputeStateHash(), twin.Level.ComputeStateHash(), "the commands alone close, mark and open as the tool did" );
		Assert.IsFalse( twin.Level.IsCoasterToolActive, "a peer's or a replayed command opens no tool" );
		Assert.AreEqual( park.One.IsOpen, twin.One.IsOpen );
		// a panel command needs its coaster in the tool, whose entry let the riders off
		var before = twin.Level.ComputeStateHash();
		Assert.IsFalse( twin.Level.Commands.Apply( new CoasterClickCommand( twin.One.Runtime.AttractionId, 24, 20, 40, false, 0 ) ).Accepted );
		Assert.IsFalse( twin.Level.Commands.Apply( new RemoveLastCoasterPylonCommand( twin.One.Runtime.AttractionId ) ).Accepted );
		Assert.AreEqual( before, twin.Level.ComputeStateHash() );
	}

	/// <summary>
	/// The coaster tool a placed station opens follows the HUD's own placement once its command has returned (<see cref="ParkCommands.AfterIssue"/>): the tool's entry is a command of its own, logged
	/// after the placement, so a twin replaying the log marks the coaster being edited as the issuer's park does. The twin's HUD opens no tool for the replayed placement.
	/// </summary>
	[TestMethod]
	public void APlacedCoasterStationsToolEntryIsACommandOfItsOwn()
	{
		ParkHud Hud( ParkSettingCommandLevelTests.Park park ) =>
			new( park.Level, OriginalUiTests.FakeStrings(), new EconomyParkStatus( () => park.Economy ), new NoItems(), new HudHost() );
		var park = Coaster();
		var level = park.Level;
		var hud = Hud( park );
		var entry = park.One.Entry;
		hud.BeginPlacing( new BuildItem( "coaster", entry.InfoId, BuildCategory.Rides, 0, 0, null, System.Array.Empty<string>(), Entry: entry ) );
		var placed = level.Commands.Issue( level.BuildCommand( entry, 10, 10, 0 ) ).As<OriginalObject>();
		Assert.IsNotNull( placed, level.LastActionMessage );
		Assert.IsTrue( placed.Runtime.Coaster!.BeingEdited, "the HUD entered the coaster tool on the placed station" );
		Assert.AreSame( placed, level.CoasterToolStation );
		CollectionAssert.AreEqual( new[] { ParkCommandKind.PlaceObject, ParkCommandKind.BeginCoasterTool }, level.Commands.Log.Select( entry => entry.Command.Kind ).ToArray(),
			"the tool's entry is logged on its own, after the placement" );

		var twin = Coaster();
		var twinHud = Hud( twin );
		twinHud.BeginPlacing( new BuildItem( "coaster", entry.InfoId, BuildCategory.Rides, 0, 0, null, System.Array.Empty<string>(), Entry: twin.One.Entry ) );
		foreach ( var logged in level.Commands.Log )
			Assert.IsTrue( twin.Level.Commands.Apply( logged.Command ).Accepted, logged.Command.ToString() );
		var twinPlaced = twin.Level.Objects.Objects.Single( item => !item.IsDeleted && item.Placement.X == placed.Placement.X && item.Placement.Y == placed.Placement.Y );
		Assert.IsTrue( twinPlaced.Runtime.Coaster!.BeingEdited, "the twin marks the coaster as the issuer's park does" );
		Assert.IsNull( twin.Level.CoasterToolStation, "the twin's HUD, though it places the same item, opens no tool for a replayed placement" );
		Assert.AreEqual( level.ComputeStateHash(), twin.Level.ComputeStateHash() );
	}

	[TestMethod]
	public void AFullThingPoolRefusesTheHireBeforeItsPlacementEnds()
	{
		var park = NewPark();
		var level = park.Level;
		var placement = level.StaffPlacement!;
		var candidate = park.Economy.Staff.Candidates.First( item => park.Economy.Staff.CanHire( item.Type ) );
		var valid = Cells( level ).First( cell => level.StaffHands!.IsValidCell( cell.X, cell.Y ) );
		Assert.IsTrue( placement.BeginHire( candidate.Id ) );
		// the no-data level's staff have no thing pool; a full one is attached
		var things = new ThingPool();
		park.Economy.Staff.AttachThings( things );
		while ( things.NextId != 0 )
			things.Allocate( SaveThingModel.Attraction );
		var employed = park.Economy.Staff.Members.Count;
		var before = level.ComputeStateHash();
		Assert.IsFalse( level.Commands.Issue( new PlaceStaffCommand( candidate.Id, valid.X, valid.Y ) ).Accepted, "no thing id is left for the employee" );
		Assert.AreEqual( before, level.ComputeStateHash(), "the refusal changed nothing" );
		Assert.AreEqual( candidate.Id, park.Economy.Staff.PendingPlacementId, "the placement still runs" );
		Assert.AreEqual( StaffClickResult.None, placement.Click( valid.X, valid.Y ), "the tool's click is refused, where the hire threw after the placement ended" );
		Assert.IsFalse( placement.IsActive, "a refused hire ends the tool" );
		Assert.AreEqual( 0, park.Economy.Staff.PendingPlacementId );
		Assert.AreEqual( employed, park.Economy.Staff.Members.Count, "nobody was hired" );
		Assert.IsTrue( park.Economy.Staff.Candidates.Any( item => item.Id == candidate.Id ), "the candidate stays in the pool" );
	}

	[TestMethod]
	public void APoolStrokeAndItsUndoAreCommandsThatATwinFollows()
	{
		using var extensions = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var park = NewPark();
		var level = park.Level;
		var results = new List<bool>();
		level.Commands.Applied += ( _, result ) => results.Add( result.Accepted );
		var builder = level.PoolBuilder!;
		var start = Cells( level ).First( cell => Enumerable.Range( 0, 3 ).All( step => builder.Check( cell.X + step, cell.Y ) == PoolBuildResult.Ok ) );
		var tool = level.PoolTool!;
		var balance = park.Economy.Balance;

		Assert.IsTrue( tool.Begin( erase: false ) );
		Assert.AreEqual( PoolBuildResult.Ok, tool.Press( start.X, start.Y ) );
		tool.Hover( start.X + 2, start.Y );
		var stroke = tool.Release()!;
		Assert.AreEqual( 3, stroke.Cells.Count, "the drag paints every cell it crosses" );
		Assert.AreEqual( balance - park.Economy.Balance, stroke.Charged, "the stroke's money is what the commands charged" );
		Assert.IsTrue( tool.Begin( erase: true ) );
		Assert.AreEqual( PoolBuildResult.Ok, tool.Press( start.X + 1, start.Y ) );
		tool.Release();
		Assert.AreNotEqual( ParkCellTypes.PoolWater, builder.Cells.RawTypeAt( start.X + 1, start.Y ), "the erase stroke takes the cell back" );
		Assert.IsTrue( tool.Undo(), "Backspace paints the erased cell again" );
		Assert.AreEqual( ParkCellTypes.PoolWater, builder.Cells.RawTypeAt( start.X + 1, start.Y ) );
		// a cell already of pool water is no write and no refusal
		Assert.IsTrue( tool.Begin( erase: false ) );
		tool.Press( start.X, start.Y );
		tool.Release();

		CollectionAssert.AreEqual( new[] { ParkCommandKind.PaintPoolCell, ParkCommandKind.PaintPoolCell, ParkCommandKind.PaintPoolCell, ParkCommandKind.ErasePoolCell,
			ParkCommandKind.UndoPoolStroke, ParkCommandKind.PaintPoolCell }, level.Commands.Log.Select( entry => entry.Command.Kind ).ToArray() );
		Assert.IsFalse( results[^1], "painting water again changes nothing: the command is refused" );

		var twin = NewPark();
		Replay( level.Commands.Log, twin.Level, results );
		Assert.AreEqual( level.ComputeStateHash(), twin.Level.ComputeStateHash(), "the commands alone paint, erase and undo as the tool did" );
	}
}
