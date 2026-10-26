using System.Linq;
using System.Numerics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The patrol area as a player draws it (tp.exe <c>0x4b5b50</c> the employee window's button, <c>0x524960</c> mode 0x36 the two clicks, <c>0x5234d0</c> the preview, <c>0x4b3640</c> the locator's highlight):
/// the real path from the staff window's button through two park clicks to the rectangle a handyman keeps to.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PatrolAreaFlowTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

	private static Level LevelOf( ParkHud hud ) => (Level)typeof( ParkHud ).GetField( "level", Private )!.GetValue( hud )!;

	/// <summary>A HUD over a park with the given staff and the level's patrol tool, as a level with a park builds it.</summary>
	private static (ParkHud Hud, ParkEconomy Park, PatrolAreaTool Tool) Fixture( params StaffType[] roles )
	{
		var economy = EconomyTestData.Park();
		foreach ( var role in roles )
			EconomyTestData.HireBest( economy, role );
		var hud = StaffWindowTests.Hud( economy );
		var level = LevelOf( hud );
		var tool = new PatrolAreaTool( () => economy, command => level.Commands.Issue( command ) );
		typeof( Level ).GetField( "<PatrolTool>k__BackingField", Private )!.SetValue( level, tool );
		return (hud, economy, tool);
	}

	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new System.IO.FileNotFoundException( name ) ) );

	/// <summary>
	/// One frame of the park view's patrol tool with the pointer over <paramref name="cell"/>: a left release (the tool object's click, tp.exe 0x524960: the press only sets a latch), a right quick click
	/// (the release the park view cancels on, 0x488434) or no button.
	/// </summary>
	private static bool Frame( ParkHud hud, (int X, int Y) cell, bool left = false, bool right = false )
	{
		typeof( ParkHud ).GetField( "cursorX", Private )!.SetValue( hud, cell.X );
		typeof( ParkHud ).GetField( "cursorY", Private )!.SetValue( hud, cell.Y );
		typeof( ParkHud ).GetField( "parkRightClick", Private )!.SetValue( hud, right );
		var input = new UiInput( new Vector2( -1 ), false, false, left, false, UiKeys.None );
		return (bool)typeof( ParkHud ).GetMethod( "UpdatePatrolTool", Private )!.Invoke( hud, new object[] { input, true } )!;
	}

	[TestMethod]
	public void ThePlayerSetsAnAreaWithTheEmployeeWindowsButtonAndTwoMapClicks()
	{
		var (hud, park, tool) = Fixture( StaffType.Handyman );
		var handyman = park.Staff.Members.Single();
		Assert.IsFalse( handyman.HasPatrolArea, "a hired member patrols the whole park" );
		hud.OpenEmployeeWindow( handyman );
		var button = (UiButton)hud.Stack.Top!.Find( "patrol" )!;
		hud.Stack.Top!.Update( Context(), UiInput.Idle( Vector2.Zero ) );
		Assert.IsTrue( button.Enabled, "the patrol button of the employee window is enabled" );
		Assert.AreEqual( "help86", button.CurrentHelp, "its help offers to set an area (UIHELPTEXT 86)" );
		Assert.IsFalse( button.Selected!(), "and it is not checked" );

		button.Activate();
		Assert.IsTrue( tool.IsActive, "0x4b5b50(1) starts the patrol tool (mouse mode 0x36)" );
		Assert.AreEqual( handyman.Id, tool.MemberId );
		Assert.IsFalse( hud.EmployeeWindowOpen, "and closes the employee window" );

		Assert.IsTrue( Frame( hud, (30, 40), left: true ), "the first click is the tool's" );
		Assert.AreEqual( (30, 40), tool.FirstCorner, "it stores the corner cell" );
		Assert.IsFalse( handyman.HasPatrolArea, "and sets nothing yet" );
		Assert.IsTrue( tool.IsActive );

		Assert.IsTrue( Frame( hud, (22, 45), left: true ) );
		Assert.IsFalse( tool.IsActive, "the second click ends the tool (0x52f200(0, 0))" );
		Assert.AreEqual( (22, 40, 30, 45), handyman.PatrolArea, "the corners are ordered, the smaller x and y first, both inclusive" );
		Assert.IsTrue( handyman.HasPatrolArea );
		Assert.IsFalse( hud.EmployeeWindowOpen, "the window does not come back by itself" );

		hud.OpenEmployeeWindow( handyman );
		var again = (UiButton)hud.Stack.Top!.Find( "patrol" )!;
		Assert.IsTrue( again.Selected!(), "the button shows as checked once the member has an area (0x4b5ab0)" );
		Assert.AreEqual( "help87", again.CurrentHelp, "and offers the whole park (UIHELPTEXT 87)" );
		again.Activate();
		Assert.IsFalse( handyman.HasPatrolArea, "pressing it clears the area (0x506e90)" );
		Assert.AreEqual( (0, 0, 127, 127), handyman.PatrolArea, "to the whole map" );
		Assert.IsTrue( hud.EmployeeWindowOpen, "and the window stays" );
	}

	[TestMethod]
	public void TheSameCellTwiceIsAOneCellAreaAndACancelKeepsTheOldArea()
	{
		var (hud, park, tool) = Fixture( StaffType.Guard );
		var guard = park.Staff.Members.Single();
		hud.OpenEmployeeWindow( guard );
		hud.Stack.Top!.Update( Context(), UiInput.Idle( Vector2.Zero ) );
		hud.Stack.Top!.Find( "patrol" )!.Activate();
		Frame( hud, (5, 6), left: true );
		Frame( hud, (5, 6), left: true );
		Assert.AreEqual( (5, 6, 5, 6), guard.PatrolArea, "two clicks on one cell make a one-cell area" );

		hud.OpenEmployeeWindow( guard );
		hud.Stack.Top!.Update( Context(), UiInput.Idle( Vector2.Zero ) );
		hud.Stack.Top!.Find( "patrol" )!.Activate();
		Assert.IsFalse( tool.IsActive, "a member with an area clears it instead of starting the tool" );
		hud.OpenEmployeeWindow( guard );
		hud.Stack.Top!.Update( Context(), UiInput.Idle( Vector2.Zero ) );
		hud.Stack.Top!.Find( "patrol" )!.Activate();
		Assert.IsTrue( tool.IsActive );
		Frame( hud, (9, 9), left: true );
		Frame( hud, (9, 9), right: true );
		Assert.IsFalse( tool.IsActive, "the right click (RMBCancel) leaves the tool" );
		Assert.IsNull( tool.FirstCorner );
		Assert.IsFalse( guard.HasPatrolArea, "and sets nothing" );
	}

	[TestMethod]
	public void ThePreviewMarksTheBorderOfTheOrderedRectangleAndTheHoveredCellBeforeTheFirstCorner()
	{
		// [BIN:TPW-EXE:0x005234d0 patrol tool hover] four LayLine passes; [BIN:TPW-EXE:0x004b3640 patrol area highlight] the same four for the locator
		CollectionAssert.AreEquivalent( new[] { (3, 4) }, PatrolAreaTool.EdgeCells( (3, 4), (3, 4) ).ToArray(), "one cell" );
		CollectionAssert.AreEquivalent( new[] { (3, 4), (4, 4) }, PatrolAreaTool.EdgeCells( (4, 4), (3, 4) ).ToArray(), "two cells in a row: the shortened rows still cover both" );
		var border = PatrolAreaTool.EdgeCells( (6, 2), (3, 5) ).ToArray();
		var expected = Enumerable.Range( 3, 4 ).SelectMany( x => Enumerable.Range( 2, 4 ).Select( y => (x, y) ) ).Where( cell => cell.x is 3 or 6 || cell.y is 2 or 5 ).ToArray();
		CollectionAssert.AreEquivalent( expected, border, "the whole border of the ordered rectangle once" );
		Assert.AreEqual( border.Length, border.Distinct().Count() );
		Assert.AreEqual( 0, PatrolAreaTool.EdgeCells( (-1, 3), (-1, 3) ).Count(), "a cell off the map is not marked (0x4d8300)" );

		var (hud, park, tool) = Fixture( StaffType.Mechanic );
		Assert.AreEqual( 0, tool.PreviewEdge().Count(), "no tool, no preview" );
		Assert.IsTrue( tool.Begin( park.Staff.Members.Single() ) );
		Frame( hud, (10, 10) );
		CollectionAssert.AreEqual( new[] { (10, 10) }, tool.PreviewEdge().ToArray(), "before the first click the hovered cell alone" );
		Frame( hud, (10, 10), left: true );
		Frame( hud, (12, 13) );
		Assert.AreEqual( PatrolAreaTool.EdgeCells( (10, 10), (12, 13) ).Count(), tool.PreviewEdge().Count() );
		Assert.IsFalse( tool.PreviewRefused, "blue without a map (no cell is known to be blocked)" );
	}

	[TestMethod]
	public void ThePreviewIsRedOnlyWhenEveryCellOfTheRectangleIsUnownedOrAFencePost()
	{
		// [BIN:TPW-EXE:0x00532a50 patrol rectangle test] 0x816D50 = every counted map cell is unowned (0x40) or a kind 0x19 record
		var grid = new GuestPathGrid( 20, 20 );
		var economy = EconomyTestData.Park();
		var member = EconomyTestData.HireBest( economy, StaffType.Entertainer );
		var tool = new PatrolAreaTool( () => economy, _ => new ParkCommandResult( true ), () => grid.Cells );
		Assert.IsTrue( tool.Begin( member ) );
		tool.Click( 2, 2 );
		tool.Hover( 3, 3 );
		Assert.IsFalse( tool.PreviewRefused, "owned grass is something to stand on" );
		foreach ( var (x, y) in new[] { (2, 2), (3, 2), (2, 3) } )
			grid.Cells.SetFlags( x, y, ParkCellFlags.Unowned );
		Assert.IsFalse( tool.PreviewRefused, "one usable cell is enough" );
		grid.Cells.SetFlags( 3, 3, ParkCellFlags.Unowned );
		Assert.IsTrue( tool.PreviewRefused, "all four unowned: red" );
		grid.Cells.SetFlags( 3, 3, ParkCellFlags.None );
		Assert.IsFalse( tool.PreviewRefused );
		// the park's land: the rectangle park's fence from post A (21, 11) to post B (13, 11)
		var land = ParkLand.FromClicks( new ParkLandSetup( (21, 11), (13, 11), new (int X, int Y)[] { (0, 0), (4, 0), (4, 10), (-12, 10), (-12, 0), (-8, 0) } ) );
		var post = land.Ring.First();
		var big = new GuestPathGrid( 64, 64 );
		big.Cells.SetLand( land );
		var landTool = new PatrolAreaTool( () => economy, _ => new ParkCommandResult( true ), () => big.Cells );
		Assert.IsTrue( landTool.Begin( member ) );
		landTool.Click( post.X, post.Y );
		landTool.Hover( post.X, post.Y );
		Assert.IsTrue( big.Cells.Land!.IsPost( post.X, post.Y ) );
		Assert.IsTrue( landTool.PreviewRefused, "a rectangle of one fence post has nothing to stand on" );
		landTool.Hover( post.X + 1, post.Y );
		Assert.IsFalse( landTool.PreviewRefused, "the cell beside a post is ground" );
	}

	[TestMethod]
	public void TheSecondClickOnARedRectangleEndsTheToolAndSetsNothing()
	{
		// [BIN:TPW-EXE:0x00524A63 refused ghost] the refusal latch: cue 0xAF, the mode ends (0x5266A3 = 0x52F200(0, 0)), the member keeps its area
		var (hud, park, _) = Fixture( StaffType.Guard );
		var guard = park.Staff.Members.Single();
		var level = LevelOf( hud );
		var grid = new GuestPathGrid( 20, 20 );
		var tool = new PatrolAreaTool( () => park, command => level.Commands.Issue( command ), () => grid.Cells );
		typeof( Level ).GetField( "<PatrolTool>k__BackingField", Private )!.SetValue( level, tool );
		foreach ( var (x, y) in new[] { (2, 2), (3, 2), (2, 3), (3, 3) } )
			grid.Cells.SetFlags( x, y, ParkCellFlags.Unowned );
		Assert.IsTrue( tool.Begin( guard ) );
		Frame( hud, (2, 2), left: true );
		Frame( hud, (3, 3) );
		Assert.IsTrue( tool.PreviewRefused );
		// the press of the click latches (0x524790 sets 0x8186D8); the release is the tool's click
		typeof( ParkHud ).GetMethod( "LatchToolPress", Private )!.Invoke( hud, null );
		Frame( hud, (3, 3), left: true );
		Assert.IsFalse( tool.IsActive, "the tool ends" );
		Assert.IsFalse( guard.HasPatrolArea, "and nothing is set" );
		var refusal = typeof( ParkHud ).GetProperty( "refusalClick", Private )!.GetValue( hud )!;
		Assert.AreEqual( (3, 3), ((PathRefusalClick)refusal).Release(), "the release plays the refusal cue at the clicked cell" );

		Assert.IsTrue( tool.Begin( guard ) );
		Frame( hud, (5, 5), left: true );
		Frame( hud, (6, 6), left: true );
		Assert.AreEqual( (5, 5, 6, 6), guard.PatrolArea, "a rectangle with ground is set" );
		Assert.IsNull( tool.TakeRefusal() );
	}

	[TestMethod]
	public void TheStaffLocatorMarksTheFollowedMembersAreaAndNothingElse()
	{
		var (hud, park, _) = Fixture( StaffType.Handyman );
		var handyman = park.Staff.Members.Single();
		var marked = () => ((System.Collections.Generic.IEnumerable<(int X, int Y, BlueprintMarker Marker)>)typeof( ParkHud ).GetMethod( "LocatedPatrolAreaEdge", Private )!.Invoke( hud, null )!).ToArray();
		Assert.AreEqual( 0, marked().Length, "nothing located" );
		handyman.SetPatrolArea( (4, 4), (8, 6) );
		Assert.AreEqual( 0, marked().Length, "an area of its own is not marked until the locator follows the member" );
		hud.Locator.Open( LocatorMode.Staff, LocatorModel.RoleOf( StaffType.Handyman ), handyman.Id );
		var cells = marked();
		CollectionAssert.AreEquivalent( PatrolAreaTool.EdgeCells( (4, 4), (8, 6) ).ToArray(), cells.Select( cell => (cell.X, cell.Y) ).ToArray() );
		Assert.IsTrue( cells.All( cell => cell.Marker == BlueprintMarker.Allowed ), "blue" );
		hud.Locator.StopFollowing();
		Assert.AreEqual( 0, marked().Length, "the camera flag 0x1000 gone, the highlight is gone" );
		hud.Locator.Open( LocatorMode.Staff, LocatorModel.RoleOf( StaffType.Handyman ), handyman.Id );
		handyman.ClearPatrolArea();
		Assert.AreEqual( 0, marked().Length, "the whole park is no area to draw (0x506eb0)" );
	}
}
