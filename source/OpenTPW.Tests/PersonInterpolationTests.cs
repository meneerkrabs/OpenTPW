using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The people are drawn at the sub-turn point between the position the turn started at and the current one (tp.exe 0x004F9F00, 0x004FA030, 0x004FA870).</summary>
[TestClass]
public class PersonInterpolationTests
{
	[TestMethod]
	public void ThePointIsTheStartPlusTheFactorTimesTheDisplacementTruncatedTowardZero()
	{
		Assert.AreEqual( (100, 200), PersonInterpolation.Point( 100, 200, 300, 600, 0f, true ) );
		Assert.AreEqual( (300, 600), PersonInterpolation.Point( 100, 200, 300, 600, 1f, true ) );
		Assert.AreEqual( (200, 400), PersonInterpolation.Point( 100, 200, 300, 600, 0.5f, true ) );
		// 0 + 3 × 0.5 = 1.5 and 3 − 3 × 0.5 = 1.5 both truncate to 1 (_ftol)
		Assert.AreEqual( 1, PersonInterpolation.Axis( 0, 3, 0.5f ) );
		Assert.AreEqual( 1, PersonInterpolation.Axis( 3, 0, 0.5f ) );
		Assert.AreEqual( -1, PersonInterpolation.Axis( 0, -3, 0.5f ), "toward zero, not down" );
	}

	[TestMethod]
	public void TheFlagHoldsTheFactorToZeroToOneAndItsAbsenceToMinusOneToTwo()
	{
		Assert.AreEqual( (100, 200), PersonInterpolation.Point( 100, 200, 300, 600, -0.4f, true ) );
		Assert.AreEqual( (300, 600), PersonInterpolation.Point( 100, 200, 300, 600, 1.7f, true ) );
		Assert.AreEqual( (500, 1000), PersonInterpolation.Point( 100, 200, 300, 600, 2.5f, false ), "past the end up to twice the step" );
		Assert.AreEqual( (-100, -200), PersonInterpolation.Point( 100, 200, 300, 600, -3f, false ), "before the start down to one step back" );
		Assert.AreEqual( (200, 400), PersonInterpolation.Point( 100, 200, 300, 600, 0.5f, false ) );
	}

	[TestMethod]
	public void TheProductAndTheSumAreRoundedToTwentyFourBits()
	{
		// a start above 2^24 is not exact in a single: the sum of 0 and 20,000,001 rounds to the even neighbour before the truncation, as the 24-bit x87 does (the double sum would keep the odd value)
		Assert.AreEqual( 20_000_000, PersonInterpolation.Axis( 20_000_001, 20_000_001, 0.5f ) );
		Assert.AreEqual( 20_000_004, PersonInterpolation.Axis( 20_000_003, 20_000_003, 0.5f ), "a tie goes to the even mantissa" );
		// the product of an exact integer and a single is rounded once: 16,777,217 × 1 would need 25 bits
		Assert.AreEqual( 16_777_216, PersonInterpolation.Axis( 0, 16_777_217, 1f ), "the product of the difference and t rounds to even" );
		// along a long step the factor's own rounding shows: 3 / 7 is not exact in a single
		var t = 3f / 7f;
		var expected = (int)(float)((double)(float)(1_000_001L * (double)t) + 5);
		Assert.AreEqual( expected, PersonInterpolation.Axis( 5, 1_000_006, t ) );
	}

	[TestMethod]
	public void TheStaffWorldDrawsAMemberBetweenTheTurnStartAndHisPositionOverTheTurn()
	{
		var park = EconomyTestData.Park();
		var grid = GuestTests.CreateCross();
		var simulation = new GuestSimulation( grid, GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, _ => null, (2, 5) ) { Guests = simulation };
		var member = EconomyTestData.HireBest( park, StaffType.Handyman );
		var walk = new GuestNavigation { X = GuestNavigation.Centre( (2, 5) ).X, Y = GuestNavigation.Centre( (2, 5) ).Y };
		member.Navigation = walk;
		member.TurnTick = simulation.TickCount;
		(member.TurnFromX, member.TurnFromY) = (walk.X, walk.Y);
		walk.X += 1 << 15;   // half a cell in the turn's step
		var draws = new System.Collections.Generic.List<float>();
		for ( var tick = 0; tick < 16; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			world.Tick( GuestSnapshotTests.Tick );
			draws.Add( member.DrawX );
		}
		Assert.AreEqual( walk.X - (1 << 15), draws[0] * 65536f, 1 << 12, "the first tick of the turn is near the start" );
		for ( var index = 1; index < draws.Count; index++ )
			Assert.IsTrue( draws[index] >= draws[index - 1], "the point only moves forward over the turn" );
		Assert.AreEqual( walk.X / 65536f, draws[^1], 1e-6f, "after the 248 ms the member stands at his position" );
	}
}
