using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// The original's join excitement rules (docs/reverse/ORACLE.md F15 and F16, QUEUE-004): the relief predicate, the live excitement with
/// its sideshow, scaled and coaster formulas, the clamped difference and the gate on the type's level byte.
/// </summary>
[TestClass]
public class QueueJoinTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	/// <summary>An open attraction without queue cells (they are not part of the excitement gate).</summary>
	private static RideVisitorBridge Open( RideVisitorKind kind, int level, QueueParameters? parameters = null )
	{
		var bridge = new RideVisitorBridge( 1, "Join", kind, 4, level, 10 ) { Parameters = parameters ?? new QueueParameters( true, false, false, 130, 60, 60, 4, 30 ) };
		bridge.Attach( GuestTests.CreateShopScript( new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ) ), () => true );
		return bridge;
	}

	[TestMethod]
	public void ASideshowScoresItsLiveGoodsPriceAndWinChance()
	{
		// SideShow.sam level 30, a prize 25 above the price and a win chance of 50: 20 + trunc(0.8 × (50 × sqrt(25) / 10)) = 40.
		var sideshow = Open( RideVisitorKind.Sideshow, 30 );
		sideshow.Sideshow = new SideshowTerms( 60, 35, 50 );
		Assert.AreEqual( 40, sideshow.LiveExcitement );
		Assert.AreEqual( -40, sideshow.JoinDifference( 80 ), "the ride is 40 below a guest preferring 80" );
		Assert.AreEqual( QueueJoinResult.Joined, sideshow.JoinQueue( 1, 80 ), "the original lets the guest join (static C# turned it away bored: 80 − 30 = 50)" );
		Assert.AreEqual( QueueJoinResult.NotExcitingEnough, sideshow.JoinQueue( 2, 85 ), "45 below the preference refuses, thought 12" );
		sideshow.Sideshow = new SideshowTerms( 100, 0, 0 );
		Assert.AreEqual( 100, sideshow.LiveExcitement, "win 100 and a margin of 100: 20 + 0.8 × 100" );
		Assert.AreEqual( QueueJoinResult.TooExciting, sideshow.JoinQueue( 3, 50 ), "100 − 50 = 50 refuses, thought 15" );
	}

	[TestMethod]
	public void SideshowExcitementClampsTheMarginAndUsesTheWinByte()
	{
		Assert.AreEqual( 20, QueueJoinRules.SideshowExcitement( new SideshowTerms( 10, 40, 50 ) ), "a price above the goods clamps the margin to 0" );
		Assert.AreEqual( 40, QueueJoinRules.SideshowExcitement( new SideshowTerms( 1000, -5, 75 ) ), "a margin above 100 clamps to 100: 25 × 10 / 10 = 25, times 0.8, plus 20" );
		Assert.AreEqual( 20, QueueJoinRules.SideshowExcitement( new SideshowTerms( 60, 35, 100 ) ), "nobody wins: win 0" );
		Assert.AreEqual( 100, QueueJoinRules.SideshowExcitement( new SideshowTerms( 100, 0, 0 ) ), "win 100, margin 100: 20 + 0.8 × 100" );
		Assert.AreEqual( 0, new RideVisitorBridge( 1, "Plain", RideVisitorKind.Sideshow, 1, 0, 10 ) { Sideshow = new SideshowTerms( 60, 35, 50 ) }.LiveExcitement, "a level of 0 has no excitement at all" );
	}

	[TestMethod]
	public void ASlowRideIsScaledByItsSpeedRatioDownToThreeQuarters()
	{
		// Level 100 at a tenth of its design speed: 100 × clamp(0.1) = 100 × 0.75 = 75, so a guest preferring 50 joins (difference 25).
		var ride = Open( RideVisitorKind.Ride, 100, new QueueParameters( true, false, false, 130, 6, 60, 4, 30 ) );
		Assert.AreEqual( 75, ride.LiveExcitement );
		Assert.AreEqual( 25, ride.JoinDifference( 50 ) );
		Assert.AreEqual( QueueJoinResult.Joined, ride.JoinQueue( 1, 50 ), "the original lets the guest join (static C# turned it away scared: 50 − 100 = −50)" );
		Assert.AreEqual( QueueJoinResult.TooExciting, ride.JoinQueue( 2, 30 ), "75 − 30 = 45 refuses, thought 15" );
		Assert.AreEqual( 125, QueueJoinRules.ScaledExcitement( 100, 600, 60, 30, 30 ), "ten times the design speed clamps to 1.25" );
		Assert.AreEqual( 125, QueueJoinRules.ScaledExcitement( 100, 60, 60, 60, 30 ), "double the duration clamps to 1.25" );
		Assert.AreEqual( 56, QueueJoinRules.ScaledExcitement( 100, 45, 60, 45, 60 ), "0.75 × 0.75 of 100 is 56.25" );
	}

	[TestMethod]
	public void ZeroDivisorsFollowTheOriginalsFloatingPoint()
	{
		Assert.AreEqual( 125, QueueJoinRules.ScaledExcitement( 100, 60, 60, 30, 0 ), "a zero design duration divides to infinity and clamps to 1.25" );
		Assert.AreEqual( 0, QueueJoinRules.ScaledExcitement( 100, 0, 0, 30, 30 ) & 0xFF, "0 / 0 is NaN, which fctiwz truncates to 0x80000000: a zero byte" );
		Assert.AreEqual( int.MinValue, QueueJoinRules.Truncate( double.NaN ) );
		Assert.AreEqual( int.MaxValue, QueueJoinRules.Truncate( 1e10 ) );
		Assert.AreEqual( int.MinValue, QueueJoinRules.Truncate( -1e10 ) );
	}

	[TestMethod]
	public void ACoasterUsesItsRatingAndOnlyAnExcitementLevelGatesIt()
	{
		var coaster = Open( RideVisitorKind.Ride, 90 );
		coaster.ExcitementSource = () => CoasterRating.GuestExcitement( 38 );
		Assert.AreEqual( 69, coaster.LiveExcitement, "trunc(0.5 × 38 + 50)" );
		Assert.AreEqual( -21, coaster.JoinDifference( 90 ) );
		// The level word is read first: a data level of 0 makes the excitement 0 and the gate off, whatever the rating.
		var unrated = Open( RideVisitorKind.Ride, 0 );
		unrated.ExcitementSource = () => 69;
		Assert.AreEqual( (0, null), (unrated.LiveExcitement, unrated.ExcitementRefusal( 90 )) );
	}

	[TestMethod]
	public void TheDifferenceIsClampedOverBytesAndAWornReliefObjectGivesOneHundred()
	{
		Assert.AreEqual( 50, QueueJoinRules.Difference( false, 100, 0 ), "clamp to +50" );
		Assert.AreEqual( -50, QueueJoinRules.Difference( false, 0, 100 ), "clamp to −50" );
		Assert.AreEqual( -44, QueueJoinRules.Difference( false, 100, 400 ), "a preference of 400 is the byte 144: 100 − 144" );
		Assert.AreEqual( 100, QueueJoinRules.Difference( true, 3, 3 ), "while the relief predicate holds nothing else is computed" );
	}

	[TestMethod]
	public void ReliefIsFailedOnlyForARelievingObjectWithAWornGauge()
	{
		Assert.IsFalse( QueueJoinRules.ReliefFailed( false, 0 ), "an ordinary ride never counts, whatever its gauge" );
		Assert.IsFalse( QueueJoinRules.ReliefFailed( true, 100 ) );
		Assert.IsFalse( QueueJoinRules.ReliefFailed( true, 25 ) );
		Assert.IsTrue( QueueJoinRules.ReliefFailed( true, 24.999f ), "the truncated gauge 24 is below 25" );
		Assert.IsTrue( QueueJoinRules.ReliefFailed( true, 0 ) );
		Assert.IsTrue( QueueJoinRules.ReliefFailed( true, 256 ), "the byte wraps: 256 counts as 0" );
		Assert.IsFalse( QueueJoinRules.ReliefFailed( true, 255.5f ) );
		Assert.IsTrue( QueueJoinRules.ReliefFailed( true, float.NaN ), "NaN truncates to 0x80000000, a zero byte" );
		// The bridge reads its own flag and gauge.
		var toilet = Open( RideVisitorKind.Facility, 60 );
		Assert.IsFalse( toilet.ReliefWornOut, "not a relief object" );
		var relief = new RideVisitorBridge( 2, "Toilet", RideVisitorKind.Facility, 4, 60, 10, GuestNeeds.Toilet ) { WearGauge = 10 };
		Assert.IsTrue( relief.ReliefWornOut );
		Assert.AreEqual( 100, relief.JoinDifference( 3 ) );
		Assert.AreEqual( QueueJoinResult.TooExciting, relief.ExcitementRefusal( 3 ), "a gated worn-out toilet is 'too exciting'" );
	}

	[TestMethod]
	public void TheGateFollowsTheLevelByteNotTheKind()
	{
		var shop = Open( RideVisitorKind.Shop, 70 );
		Assert.AreEqual( QueueJoinResult.TooExciting, shop.JoinQueue( 1, 10 ), "a shop with an ExcitementLevel is gated" );
		var ride = Open( RideVisitorKind.Ride, 0 );
		Assert.AreEqual( QueueJoinResult.Joined, ride.JoinQueue( 1, 100 ), "a ride without a level takes everybody" );
		var wide = Open( RideVisitorKind.Ride, 256 );
		Assert.AreEqual( QueueJoinResult.Joined, wide.JoinQueue( 1, 100 ), "only the low byte of the level word decides" );
		var nobody = Open( RideVisitorKind.Ride, 60 );
		Assert.AreEqual( QueueJoinResult.Joined, nobody.JoinQueue( 1 ), "no preferred excitement: no gate" );
	}

	[TestMethod]
	public void TheDurationRatioDividesByTheRawInitialDuration()
	{
		// Brain Buster in the Halloween Rides.sam: InitDuration 3 below its MinDuration 10: DUR 10, 10 / 3 clamps to 1.25, 40 × 1.25 = 50.
		var brainBuster = Open( RideVisitorKind.Ride, 40, new QueueParameters( true, false, false, 130, 60, 60, 4, 10, 3 ) );
		Assert.AreEqual( 50, brainBuster.LiveExcitement );
		Assert.AreEqual( 40, Open( RideVisitorKind.Ride, 40, new QueueParameters( true, false, false, 130, 60, 60, 4, 10 ) ).LiveExcitement, "without a raw value the ratio is 1" );
	}
}
