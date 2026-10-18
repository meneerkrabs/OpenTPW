using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>A won sideshow play in the use routine (tp.exe <c>0x004FE1E0</c>): the prize into the purse and the tail's logarithmic happiness gain.</summary>
[TestClass]
public class SideshowWinTests
{
	[TestMethod]
	public void TheGainIsTheMediumStepTimesTheBaseTwoLogarithmOfPrizeOverPrice()
	{
		Assert.AreEqual( 15, SideshowWin.HappinessGain( 80, 10, 5 ), "log2 8 = 3 exactly" );
		Assert.AreEqual( 5, SideshowWin.HappinessGain( 20, 10, 5 ) );
		Assert.AreEqual( 0, SideshowWin.HappinessGain( 10, 10, 5 ) );
		Assert.AreEqual( -5, SideshowWin.HappinessGain( 5, 10, 5 ), "a prize below the price lowers it" );
		Assert.AreEqual( 7, SideshowWin.HappinessGain( 30, 10, 5 ), "trunc(5 × log2 3 = 7.92)" );
	}

	[TestMethod]
	public void AnInfiniteOrZeroQuotientStoresTheIndefiniteQuadwordAndGivesZero()
	{
		Assert.AreEqual( 0, SideshowWin.HappinessGain( 10, 0, 5 ), "a free play: the division is infinite, fistp stores 0x8000000000000000 and __ftol returns its low dword" );
		Assert.AreEqual( 0, SideshowWin.HappinessGain( 0, 10, 5 ), "a prize of 0: log of 0 is -infinity" );
		Assert.AreEqual( 0, SideshowWin.HappinessGain( 0, 0, 5 ) );
		Assert.AreEqual( 0, SideshowWin.HappinessGain( 10, 10, 0 ) );
	}

	[TestMethod]
	public void FtolKeepsTheLowDwordOfTheTruncatedQuadword()
	{
		Assert.AreEqual( -3, SideshowWin.Ftol( -3.9 ) );
		Assert.AreEqual( 7, SideshowWin.Ftol( 7.99 ) );
		Assert.AreEqual( int.MinValue, SideshowWin.Ftol( 2147483648d ), "2^31 truncated to a quadword has the low dword 0x80000000" );
		Assert.AreEqual( 0, SideshowWin.Ftol( 4294967296d ), "2^32 has the low dword 0" );
		Assert.AreEqual( 0, SideshowWin.Ftol( double.PositiveInfinity ) );
		Assert.AreEqual( 0, SideshowWin.Ftol( double.NaN ) );
	}

	[TestMethod]
	public void TheStepIsTheLowByteOfTheSetting()
	{
		Assert.AreEqual( SideshowWin.HappinessGain( 40, 10, 5 ), SideshowWin.HappinessGain( 40, 10, 0x105 ) );
	}

	[TestMethod]
	public void AWonPlayPutsThePrizeInThePurseAndRaisesTheHappiness()
	{
		var rig = new QueueTests.Rig( takesGuests: true, capacity: 1, kind: RideVisitorKind.Sideshow );
		rig.Ride.Sideshow = new SideshowTerms( 80, 10, 20 );
		var guest = rig.Send();
		var money = guest.Money;
		var happiness = guest.Happiness;
		((IRideVisitorHost)rig.Simulation).OnVisitorReleased( rig.Ride, guest.Id );
		Assert.AreEqual( money + 80, guest.Money, "the prize is the cost of goods" );
		Assert.IsTrue( guest.Happiness > happiness );
	}
}
