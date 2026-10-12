using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The attraction score, the choice loop and the ride outcome (docs/reverse/ORACLE.md F24). Expected values are results of the original routines
/// executed in the PowerPC oracle (<c>attraction-score</c>, <c>ride-outcome</c>, <c>attraction-choice</c>); the oracle suite itself compares this code
/// with them over 578, 561 and 515 inputs.
/// </summary>
[TestClass]
public class AttractionScoreTests
{
	private static readonly int[] Empty = { 0, 0, 0, 0 };

	/// <summary>A ride next to the guest, matching the guest's excitement, an empty queue, no needs, a ride that is not new: the oracle's first edge.</summary>
	private static AttractionScoreInput Perfect( Func<AttractionScoreInput, AttractionScoreInput>? change = null )
	{
		var input = new AttractionScoreInput( 10, 10, 10, 10, 0, 0, 3, 60, 60, true, 0, 0, 0, 0, 0, 0, false, 100, false, false, 0, 0, 7, false, Empty, Empty );
		return change?.Invoke( input ) ?? input;
	}

	private static uint Score( AttractionScoreInput input ) => AttractionScore.Compute( input, DecisionWeights.Shipped, DecisionVariables.Shipped );

	[TestMethod]
	public void ASatisfiedMatchScoresItsShareOfTheWeightSum()
	{
		// distance 100 + queue 100 + excitement 100 over all seven weights (1 + 1 + 1 + 2 + 2 + 2 + 2 = 11): 300 / 11. A guest without needs still divides by
		// the need weights, so even a perfect ride scores far below 100.
		Assert.AreEqual( 27u, Score( Perfect() ) );
	}

	[TestMethod]
	public void DistanceIsASquaredDisplacementAndTheQueueCountsOnlyNearby()
	{
		Assert.AreEqual( 100u, AttractionScore.DistanceMatch( 10, 10, 10, 10, 0 ) );
		Assert.AreEqual( 98u, AttractionScore.DistanceMatch( 10, 10, 13, 10, 0 ), "nine squares: 100 · 9 / 450 = 2" );
		Assert.AreEqual( 0u, AttractionScore.DistanceMatch( 0, 0, 127, 127, 0 ), "capped at 100" );
		Assert.AreEqual( 25u, AttractionScore.DistanceMatch( 10, 10, 10, 10, 4 ), "a nonzero cell modifier divides the match" );
		Assert.AreEqual( 0u, AttractionScore.DistanceMatch( 10, 10, 10, 10, -1 ), "a negative modifier is an unsigned divisor" );
		// 3 columns away is nine squares: past the queue range of 8, so neither the queue match nor its weight counts: (98 + 100) / 10
		Assert.AreEqual( 19u, Score( Perfect( input => input with { RideX = 13 } ) ) );
		// 8 squares away: still in range, a queue of 6 in 3 cells is half full: (99 + 50 + 100) / 11
		Assert.AreEqual( 22u, Score( Perfect( input => input with { RideX = 12, RideY = 12, QueueCount = 6 } ) ) );
		Assert.AreEqual( 20u, Score( Perfect( input => input with { CellModifier = 4 } ) ) );
		Assert.AreEqual( 18u, Score( Perfect( input => input with { CellModifier = -1 } ) ) );
	}

	[TestMethod]
	public void AQueueAboveFourPerCellWrapsUnclamped()
	{
		// the original does not clamp 100 - 100 · 9 / 4 = -125 (4294967171 as an unsigned word); the whole sum wraps with it
		Assert.AreEqual( 4294967171u, AttractionScore.QueueMatch( 9, 1 ) );
		Assert.AreEqual( 6u, Score( Perfect( input => input with { QueueCount = 9, QueueCells = 1 } ) ), "(100 - 125 + 100) / 11" );
		Assert.AreEqual( 92u, AttractionScore.QueueMatch( 1, 3 ), "100 - 100 · 1 / 12 = 92" );
	}

	[TestMethod]
	public void ExcitementMatchesTheBytesAndNeedsMatchThroughTheQuadraticCurve()
	{
		Assert.AreEqual( 100, AttractionScore.ExcitementMatch( 60, 60 ) );
		Assert.AreEqual( 0, AttractionScore.ExcitementMatch( 0, 100 ), "the difference caps at 50" );
		Assert.AreEqual( 40, AttractionScore.ExcitementMatch( 50, 80 ), "2 · (50 - 30)" );
		// PPC-guests: need 50 with effect 50 gives 17, need 100 with effect 100 gives 100 (not a linear quarter of the need)
		Assert.AreEqual( 17, AttractionScore.NeedMatch( 50f, 50 ) );
		Assert.AreEqual( 100, AttractionScore.NeedMatch( 100f, 100 ) );
		Assert.AreEqual( 0, AttractionScore.NeedMatch( 100f, 0 ), "a shop without that effect never matches" );
		Assert.AreEqual( 31, AttractionScore.NeedMatch( 100f, 10 ) );
		Assert.AreEqual( 2, AttractionScore.ReliefMatch( 50f, true ) );
		Assert.AreEqual( 100, AttractionScore.ReliefMatch( 100f, true ) );
		Assert.AreEqual( 0, AttractionScore.ReliefMatch( 100f, false ), "only a relief object matches the toilet or illness meter" );
	}

	[TestMethod]
	public void NeedsScoreOnlyShopsThatServeThem()
	{
		var drinks = Perfect( input => input with { Thirst = 50f, ThirstEffect = 50, ExcitementGated = false, Excitement = 0 } );
		Assert.AreEqual( 23u, Score( drinks ) );
		Assert.AreEqual( 40u, Score( drinks with { Thirst = 100f, ThirstEffect = 100 } ) );
		Assert.AreEqual( 40u, Score( drinks with { Thirst = 0f, ThirstEffect = 0, Hunger = 100f, HungerEffect = 100 } ) );
		var toilet = drinks with { Thirst = 0f, ThirstEffect = 0, Toilet = 50f, Relief = true };
		Assert.AreEqual( 20u, Score( toilet ) );
		Assert.AreEqual( 60u, Score( toilet with { Toilet = 100f, Illness = 100f } ) );
		Assert.AreEqual( 20u, Score( toilet with { Toilet = 100f, Relief = false } ), "a guest in need of a toilet does not score a shop for it" );
	}

	[TestMethod]
	public void AnExcitementLevelOfZeroDropsTheExcitementWeightButKeepsTheOthers()
	{
		Assert.AreEqual( 20u, Score( Perfect( input => input with { ExcitementGated = false } ) ), "(100 + 100) / 10: the excitement weight leaves the sum" );
		Assert.IsFalse( AttractionScore.TryCompute( Perfect( input => input with { ExcitementGated = false } ), new DecisionWeights( 0, 0, 5, 0, 0, 0, 0 ), DecisionVariables.Shipped, out _ ),
			"all weights gone: the original's divwu by zero is undefined" );
		Assert.IsFalse( AttractionScore.TryCompute( Perfect(), new DecisionWeights( 0, 0, 0, 0, 0, 0, 0 ), DecisionVariables.Shipped, out _ ) );
		Assert.AreEqual( 0u, AttractionScore.Compute( Perfect(), new DecisionWeights( 0, 0, 0, 0, 0, 0, 0 ), DecisionVariables.Shipped ) );
	}

	[TestMethod]
	public void NewRidesIndoorRidesInRainAndCostlyRidesScaleTheScore()
	{
		Assert.AreEqual( 135u, Score( Perfect( input => input with { AgeDays = 7 } ) ), "up to DecisionVariable1 = 7 days old: times 5" );
		Assert.AreEqual( 27u, Score( Perfect( input => input with { AgeDays = 8 } ) ) );
		Assert.AreEqual( 135u, Score( Perfect( input => input with { Raining = true, Indoors = true } ) ), "indoor in rain: times DecisionVariable3 = 5" );
		Assert.AreEqual( 27u, Score( Perfect( input => input with { Raining = false, Indoors = true } ) ) );
		Assert.AreEqual( 27u, Score( Perfect( input => input with { Raining = true, Indoors = false } ) ) );
		Assert.AreEqual( 32u, Score( Perfect( input => input with { GoldenTicketCost = 1 } ) ), "27 · (1 + 2 / 10)" );
		Assert.AreEqual( 35u, Score( Perfect( input => input with { GoldenTicketCost = 2, UpgradeCost = 6000 } ) ), "a golden ticket cost wins over the upgrade cost: 27 · 1.3" );
		Assert.AreEqual( 32u, Score( Perfect( input => input with { UpgradeCost = 6000 } ) ), "27 · (1 + 2 / 10)" );
		Assert.AreEqual( 27u, Score( Perfect( input => input with { UpgradeCost = 3000 } ) ), "q = 1 is not above 1" );
		Assert.AreEqual( 29u, Score( Perfect( input => input with { UpgradeCost = 3001 } ) ) );
	}

	[TestMethod]
	public void HistoryDividesTheScoreByFiveFourThreeAndTwo()
	{
		Assert.AreEqual( 5u, Score( Perfect( input => input with { UsedHistory = new[] { 7, 0, 0, 0 } } ) ) );
		Assert.AreEqual( 6u, Score( Perfect( input => input with { UsedHistory = new[] { 9, 7, 0, 0 } } ) ) );
		Assert.AreEqual( 9u, Score( Perfect( input => input with { UsedHistory = new[] { 9, 8, 7, 0 } } ) ) );
		Assert.AreEqual( 13u, Score( Perfect( input => input with { UsedHistory = new[] { 9, 8, 6, 7 } } ) ) );
		Assert.AreEqual( 5u, Score( Perfect( input => input with { UsedHistory = new[] { 7, 7, 7, 7 } } ) ), "the used history stops at its first match" );
		Assert.AreEqual( 5u, Score( Perfect( input => input with { TemporaryHistory = new[] { 7, 0, 0, 0 } } ) ) );
		Assert.AreEqual( 0u, Score( Perfect( input => input with { TemporaryHistory = new[] { 7, 7, 7, 7 } } ) ), "27 / 5 / 4 / 3 / 2: every match of the temporary history divides" );
		Assert.AreEqual( 3u, Score( Perfect( input => input with { TemporaryHistory = new[] { 1, 7, 0, 7 } } ) ), "27 / 4 / 2" );
		Assert.AreEqual( 0u, Score( Perfect( input => input with { SameTypeAsLastUsed = true } ) ), "an attraction of the type just used scores 0 outright" );
		Assert.AreEqual( 5u, AttractionScore.DivideByHistory( 600, 7, new[] { 0, 0, 0, 0 }, new[] { 7, 7, 7, 7 } ), "PPC-guests: 600 with all four temporary ids equal to 7 gives 5, not 120" );
	}

	[TestMethod]
	public void TheChoiceTakesTheBestScoreAtLeastTenAndBreaksTiesOnOddTurns()
	{
		AttractionChoice.Candidate Make( int id, uint score, bool passes = true, bool choosable = true, bool hasCell = true ) =>
			new( id, choosable, hasCell, score, () => passes );
		Assert.AreEqual( 0, AttractionChoice.Pick( new[] { Make( 1, 9 ) }, 0 ), "below 10 is no candidate" );
		Assert.AreEqual( 1, AttractionChoice.Pick( new[] { Make( 1, 10 ) }, 0 ) );
		Assert.AreEqual( 1, AttractionChoice.Pick( new[] { Make( 1, 50 ), Make( 2, 50 ) }, 0 ), "equal scores keep the first on an even turn" );
		Assert.AreEqual( 2, AttractionChoice.Pick( new[] { Make( 1, 50 ), Make( 2, 50 ) }, 1 ), "and take the later one on an odd turn" );
		Assert.AreEqual( 1, AttractionChoice.Pick( new[] { Make( 1, 50 ), Make( 2, 50, passes: false ) }, 1 ), "a candidate that fails the tests never wins" );
		Assert.AreEqual( 3, AttractionChoice.Pick( new[] { Make( 1, 50 ), Make( 2, 60, passes: false ), Make( 3, 55 ) }, 0 ) );
		Assert.AreEqual( 2, AttractionChoice.Pick( new[] { Make( 1, 0xFFFFFF9Bu ), Make( 2, 50 ) }, 0 ), "a wrapped score compares as a negative best (signed) but not as a candidate below 10 (unsigned)" );
		Assert.AreEqual( 0, AttractionChoice.Pick( new[] { Make( 1, 90, choosable: false ), Make( 2, 90, hasCell: false ) }, 0 ) );
	}

	[TestMethod]
	public void TheChoiceTestsOnlyWhatCanWin()
	{
		var tested = new System.Collections.Generic.List<int>();
		AttractionChoice.Candidate Make( int id, uint score ) => new( id, true, true, score, () => { tested.Add( id ); return true; } );
		Assert.AreEqual( 1, AttractionChoice.Pick( new[] { Make( 1, 50 ), Make( 2, 40 ), Make( 3, 50 ) }, 0 ) );
		CollectionAssert.AreEqual( new[] { 1 }, tested, "the counts-test and the path test run only for a score that beats the best" );
	}

	[TestMethod]
	public void ARideGainsHappinessInStrictBandsAndNeverLosesAny()
	{
		static float Gain( int preferred, int excitement ) => RideOutcome.Apply( 50f, 100f, 0f, preferred, excitement, 25, 15, 5, 10 ).Happiness - 50f;
		Assert.AreEqual( 25f, Gain( 60, 60 ) );
		Assert.AreEqual( 25f, Gain( 64, 60 ), "below 5 is perfect" );
		Assert.AreEqual( 15f, Gain( 65, 60 ), "5 is good: the original's bands are strictly below 5, 15 and 40" );
		Assert.AreEqual( 15f, Gain( 74, 60 ) );
		Assert.AreEqual( 5f, Gain( 75, 60 ) );
		Assert.AreEqual( 5f, Gain( 99, 60 ) );
		Assert.AreEqual( 0f, Gain( 100, 60 ), "40 and beyond: nothing - there is no penalty branch" );
		Assert.AreEqual( 0f, Gain( 80, 0 ), "an attraction without live excitement (a shop) does nothing here" );
		Assert.AreEqual( 100f, RideOutcome.Apply( 90f, 100f, 0f, 60, 60, 25, 15, 5, 10 ).Happiness, "clamped to 100" );
	}

	[TestMethod]
	public void ARideMakesAGuestSickInProportionToItsFullStomach()
	{
		// PPC-guests: excitement 70 over divisor 10 gives +35 at hunger 0, +7 at hunger 80 and +0 at hunger 81
		Assert.AreEqual( 35f, RideOutcome.Apply( 50f, 0f, 0f, 70, 70, 25, 15, 5, 10 ).Illness );
		Assert.AreEqual( 7f, RideOutcome.Apply( 50f, 80f, 0f, 70, 70, 25, 15, 5, 10 ).Illness );
		Assert.AreEqual( 0f, RideOutcome.Apply( 50f, 81f, 0f, 70, 70, 25, 15, 5, 10 ).Illness );
		Assert.AreEqual( 100f, RideOutcome.Apply( 50f, 0f, 90f, 70, 70, 25, 15, 5, 10 ).Illness, "clamped to 100, no reset and no penalty" );
		Assert.AreEqual( 0f, RideOutcome.Apply( 50f, 255f, 0f, 70, 70, 25, 15, 5, 10 ).Illness, "a hunger byte above 100 makes the factor negative and the clamp floors it" );
		Assert.ThrowsException<DivideByZeroException>( () => RideOutcome.Apply( 50f, 0f, 0f, 70, 70, 25, 15, 5, 0 ) );
	}

	[TestMethod]
	public void MetersClampLikeTheOriginalAndKeepANaN()
	{
		Assert.AreEqual( 100f, MeterMath.Clamp( 100.5f ) );
		Assert.AreEqual( 0f, MeterMath.Clamp( -0.5f ) );
		Assert.IsTrue( float.IsNaN( MeterMath.Clamp( float.NaN ) ) );
		Assert.AreEqual( 100, MeterMath.Byte( 100.9f ) );
		Assert.AreEqual( 0, MeterMath.Byte( 256.5f ), "a meter above 255 wraps in its low byte" );
	}
}
