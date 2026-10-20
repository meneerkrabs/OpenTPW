using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>UI register items settled by reading tp.exe: the rules the reads gave, without game data.</summary>
[TestClass]
public class UiRegisterReadTests
{
	[TestMethod]
	public void TheBuyScrollBallRunsFromTheTrackTopToABallAboveItsBottomInWholeUnits()
	{
		// track (1743,383)-(1802,803), ball 67 high: 353 units of travel (tp.exe 0x66b088)
		Assert.AreEqual( 383f, ParkHud.BuyScrollKnobRect( 0, 7 ).Y );
		Assert.AreEqual( 803f - 67f, ParkHud.BuyScrollKnobRect( 7, 7 ).Y );
		Assert.AreEqual( 383f + 353 * 3 / 7, ParkHud.BuyScrollKnobRect( 3, 7 ).Y, "integer division, as the original's short arithmetic" );
		Assert.AreEqual( 383f, ParkHud.BuyScrollKnobRect( 0, 0 ).Y );
	}

	[TestMethod]
	public void TheBuyScrollBarShowsOnlyWhileTheListScrolls()
	{
		// tp.exe 0x664495 hides the scroller when the list has no more items than rows
		Assert.IsFalse( ParkHud.BuyScrollShown( 0 ) );
		Assert.IsTrue( ParkHud.BuyScrollShown( 1 ) );
	}

	[TestMethod]
	public void MapOverlayCellsUseTheOriginalGridOnTheThemeImage()
	{
		// tp.exe 0x5f2380: 15 + column × trunc(480 / width), 42 + row × trunc(425 / height)
		var first = ParkMapGeometry.OverlayCellRect( 96, 85, true, 0, 0 );
		var last = ParkMapGeometry.OverlayCellRect( 96, 85, true, 95, 84 );
		Assert.AreEqual( (15f, 42f, 5f, 5f), (first.X, first.Y, first.Width, first.Height) );
		Assert.AreEqual( (495f, 467f), (last.Right, last.Bottom) );
		Assert.AreEqual( 4f, ParkMapGeometry.OverlayCellRect( 96, 100, true, 0, 0 ).Height, "the cell size is truncated" );
		Assert.AreEqual( ParkMapGeometry.CellImageRect( 32, 32, false, 3, 4 ), ParkMapGeometry.OverlayCellRect( 32, 32, false, 3, 4 ), "the sandbox keeps its own grid" );
	}

	[TestMethod]
	public void TheMapStartsWithOnlyTheRidesLayer()
	{
		// the layer word starts as 1, the bit of control 2439 (b_sride)
		CollectionAssert.AreEquivalent( new[] { BuildCategory.Rides }, ParkHud.InitialMapCategories.ToArray() );
	}

	[TestMethod]
	public void ThePlaceToolCarriesTheHiresAdvisorHelp()
	{
		// tp.exe 0x49b9dd posts 0x135 after the hire; the Place tool's end 0x46c890 withdraws it
		Assert.AreEqual( 309, AdvisorUiHelp.MessageFor( ParkHud.StaffPlacementHelp ) );
	}

	[TestMethod]
	public void AnActivePlaceToolReportsTheHiresAdvisorHelpOpenUntilTheToolEnds()
	{
		// tp.exe 0x49b9dd posts 0x135 once the hire window is closed; the Place tool's end 0x46c890 withdraws it at 0x46c8d4, placed or cancelled
		static void Set( object target, string field, object? value ) =>
			target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );
		var grid = new GuestPathGrid( 20, 20 );
		for ( var x = 0; x < 16; x++ )
			grid.SetPath( x, 5, true );
		var park = EconomyTestData.Park();
		park.OpenPark();
		park.Litter.Cells = grid.Cells;
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, _ => null, (0, 5) );
		park.StaffWorld = world;
		var placement = StaffPlacementRig.Create( () => park, () => grid.Cells, ( _, _ ) => false );
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "<StaffPlacement>k__BackingField", placement );

		var events = new List<string>();
		var hud = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		Set( hud, "level", level );
		Set( hud, "host", new HudHost { WindowOpened = name => events.Add( "+" + name ), WindowClosed = name => events.Add( "-" + name ) } );
		Set( hud, "reportedScreens", new HashSet<string>( System.StringComparer.Ordinal ) );
		Set( hud, "openScreens", new HashSet<string>( System.StringComparer.Ordinal ) );
		Set( hud, "screenChanges", new List<string>() );
		var hudScreen = new UiScreen( "hud" ) { Modal = false };
		Set( hud, "hud", hudScreen );
		Set( hud, "<Stack>k__BackingField", new UiScreenStack() );
		hud.Stack.Push( hudScreen );
		var report = typeof( ParkHud ).GetMethod( "ReportScreens", BindingFlags.Instance | BindingFlags.NonPublic )!;

		report.Invoke( hud, null );
		Assert.AreEqual( 0, events.Count, "no tool, no help" );

		Assert.IsTrue( placement.BeginHire( park.Staff.Candidates.First( item => item.Type == StaffType.Guard ).Id ) );
		report.Invoke( hud, null );
		report.Invoke( hud, null );
		CollectionAssert.AreEqual( new[] { "+" + ParkHud.StaffPlacementHelp }, events, "the Place tool opens help 309 once" );
		Assert.AreEqual( 309, AdvisorUiHelp.MessageFor( ParkHud.StaffPlacementHelp ) );

		placement.Cancel();
		report.Invoke( hud, null );
		CollectionAssert.AreEqual( new[] { "+" + ParkHud.StaffPlacementHelp, "-" + ParkHud.StaffPlacementHelp }, events, "cancelling the tool withdraws it" );

		events.Clear();
		Assert.IsTrue( placement.BeginHire( park.Staff.Candidates.First( item => item.Type == StaffType.Handyman ).Id ) );
		report.Invoke( hud, null );
		Assert.AreEqual( StaffClickResult.Placed, placement.Click( 7, 9 ) );
		report.Invoke( hud, null );
		CollectionAssert.AreEqual( new[] { "+" + ParkHud.StaffPlacementHelp, "-" + ParkHud.StaffPlacementHelp }, events, "placing the member ends the tool and withdraws it" );
	}

	[TestMethod]
	public void TrainingBudgetsMoveInStepsOf25WithinTheSpinnersRange()
	{
		// tp.exe 0x4b2797: range 0..10000, step 25; 0x66a71c clamps
		Assert.AreEqual( 25, ParkHud.StepTrainingBudget( 0, 1 ) );
		Assert.AreEqual( 0, ParkHud.StepTrainingBudget( 10, -1 ) );
		Assert.AreEqual( 10000, ParkHud.StepTrainingBudget( 9990, 1 ) );
		Assert.AreEqual( 10000, ParkHud.StepTrainingBudget( 12000, -1 ), "a larger saved budget is clamped by the next press" );
	}

	[TestMethod]
	public void TrainingGaugesShowTheMeanGradeInIntegerSteps()
	{
		// tp.exe 0x4c9c00: sum × 100 / (count × 5); the row sets share × 1024 / 100 (0x4b28eb); grades are 0..4
		Assert.AreEqual( 0f, ParkHud.TrainingGaugeFill( System.Array.Empty<int>() ) );
		Assert.AreEqual( 409 / 1024f, ParkHud.TrainingGaugeFill( new[] { 2 } ), 1e-6f );
		Assert.AreEqual( 819 / 1024f, ParkHud.TrainingGaugeFill( new[] { 4 } ), 1e-6f, "the best grade fills 80%: the divisor is 5, one above it" );
		Assert.AreEqual( (33 * 1024 / 100) / 1024f, ParkHud.TrainingGaugeFill( new[] { 1, 2, 2 } ), 1e-6f, "5 × 100 / 15 = 33" );
	}

	[TestMethod]
	public void TheBuyWindowsWorkingLifeGaugeKeepsItsBuildValue()
	{
		// tp.exe 0x4abae0 sets gauge 0x1f0 to 100 of 1024 when the window is built; 0x4abb00 never updates it
		Assert.AreEqual( 100f / 1024f, ParkHud.RideWorkingLifeGauge, 1e-7f );
	}

	[TestMethod]
	public void TheWorkloadKnobSetsSeventyToAHundredWithinTheResearchersLimit()
	{
		// tp.exe 0x4aac80: limit ((100 - c) / 3 + c) × 1024 / (MaxResearchersInPark × top ability); 0x4a96b0: 70 + 30 × value / limit
		Assert.AreEqual( 33 * 1024 / 60, ParkHud.WorkloadKnobLimit( 0, 10, 6 ), "no researchers" );
		Assert.AreEqual( (97 / 3 + 3) * 1024 / 60, ParkHud.WorkloadKnobLimit( 3, 10, 6 ) );
		var limit = ParkHud.WorkloadKnobLimit( 0, 10, 6 );
		Assert.AreEqual( 70, ParkHud.WorkloadFromKnob( 0, limit ) );
		Assert.AreEqual( 100, ParkHud.WorkloadFromKnob( 1023, limit ), "a value above the limit is pulled back to it" );
		Assert.AreEqual( 70 + 300 * 30 / limit, ParkHud.WorkloadFromKnob( 300, limit ) );
		Assert.AreEqual( 15 * limit / 30, ParkHud.KnobFromWorkload( 85, limit ) );
		Assert.AreEqual( 0, ParkHud.KnobFromWorkload( 40, limit ), "a saved workload below 70 shows at the bottom" );
		Assert.AreEqual( 1023, ParkHud.KnobFromWorkload( 100, 2000 ), "the knob stays on its slider" );
		Assert.AreEqual( 97, ParkHud.WorkloadFromKnob( 1023, 1100 ), "a limit beyond the slider cannot reach 100" );
	}

	[TestMethod]
	public void TheWorkloadKnobBallRunsDownwardsWithTheValue()
	{
		// tp.exe 0x66b088 with min 1023, max 0: top + (1023 - value) × 414 / 1023; 0x66afe5 reads it back truncated
		Assert.AreEqual( 220f, ParkHud.WorkloadKnobTop( 1023 ) );
		Assert.AreEqual( 701f - 67f, ParkHud.WorkloadKnobTop( 0 ) );
		Assert.AreEqual( 1023, ParkHud.KnobValueAt( 220 + 33.5f ) );
		Assert.AreEqual( 0, ParkHud.KnobValueAt( 701 - 33.5f ) );
		Assert.AreEqual( 1023, ParkHud.KnobValueAt( 0 ), "the pointer above the track takes the top" );
	}

	[TestMethod]
	public void ADragMovesTheBallByThePointersMovementAndHoldsItInTheTrack()
	{
		// tp.exe 0x66b771: the ball rectangle is translated by the delta and pulled back inside the limits
		var centre = 400f;
		centre = ParkHud.DragBall( centre, 380f, 390f );
		Assert.AreEqual( 410f, centre, "the ball keeps its grab offset: it moves by the 10 units the pointer moved" );
		Assert.AreEqual( 701f - 33.5f, ParkHud.DragBall( centre, 390f, 900f ), "it stops at the bottom of the track" );
		Assert.AreEqual( 220f + 33.5f, ParkHud.DragBall( centre, 390f, -500f ), "and at the top" );
	}
}
