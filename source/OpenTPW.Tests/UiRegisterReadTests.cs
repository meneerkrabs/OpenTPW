using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

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
		// tp.exe 0x4c9c00: sum × 100 / (count × 5); the row sets share × 1024 / 100
		Assert.AreEqual( 0f, ParkHud.TrainingGaugeFill( System.Array.Empty<int>() ) );
		Assert.AreEqual( 409 / 1024f, ParkHud.TrainingGaugeFill( new[] { 2 } ), 1e-6f );
		Assert.AreEqual( 1f, ParkHud.TrainingGaugeFill( new[] { 5 } ), 1e-6f );
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
}
