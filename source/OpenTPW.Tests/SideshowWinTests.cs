using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>A sideshow play is won when the draw mod 100 is at most the win byte 100 − ChanceOfLosing (tp.exe <c>0x004E2670</c>).</summary>
[TestClass]
public class SideshowWinTests
{
	[TestMethod]
	public void TheWinByteItselfStillWins()
	{
		Assert.IsTrue( ParkEconomy.SideshowWins( 30, 70 ), "R mod 100 == byte wins (0x004E26E2: sbb/inc)" );
		Assert.IsFalse( ParkEconomy.SideshowWins( 30, 71 ) );
		Assert.IsTrue( ParkEconomy.SideshowWins( 30, 170 ), "the draw is taken mod 100" );
		Assert.AreEqual( 71, Enumerable.Range( 0, 100 ).Count( draw => ParkEconomy.SideshowWins( 30, draw ) ), "(byte + 1) % of the draws" );
	}

	[TestMethod]
	public void ACertainLossStillWinsOneDrawInAHundred()
	{
		Assert.IsTrue( ParkEconomy.SideshowWins( 100, 0 ), "a win byte of 0 wins on R mod 100 == 0" );
		Assert.AreEqual( 1, Enumerable.Range( 0, 100 ).Count( draw => ParkEconomy.SideshowWins( 100, draw ) ) );
		Assert.AreEqual( 100, Enumerable.Range( 0, 100 ).Count( draw => ParkEconomy.SideshowWins( 1, draw ) ), "a byte of 99 always wins" );
		Assert.AreEqual( 100, Enumerable.Range( 0, 100 ).Count( draw => ParkEconomy.SideshowWins( 0, draw ) ) );
	}

	[TestMethod]
	public void PlaysDrawOnceFromTheWorldGeneratorAndUseTheByteTest()
	{
		var park = EconomyTestData.Park();
		var sideshow = park.RegisterExisting( 1303 );
		park.SetPrices( sideshow.Id, sideshow.Price, sideshow.CostOfGoods, 100 );
		var expected = new WorldRng( park.World.State );
		var wins = 0;
		for ( var play = 0; play < 400; play++ )
		{
			Assert.IsTrue( park.PlaySideshow( sideshow.Id, 1000, out _, out var won ) );
			Assert.AreEqual( expected.Next( 100u ) == 0, won );
			wins += won ? 1 : 0;
		}
		Assert.IsTrue( wins > 0, "a 100 % chance of losing still pays out on a zero draw" );
		Assert.AreEqual( expected.State, park.World.State );
	}
}
