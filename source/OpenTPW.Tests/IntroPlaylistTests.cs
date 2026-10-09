using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class IntroPlaylistTests
{
	[TestMethod]
	public void TrailerIsChosenByDayOfMonthModuloEight()
	{
		// Table order of the original: bub, buc, grav, jug, mir, plan, roc, roll.
		Assert.AreEqual( "buc", IntroPlaylist.TrailerForDay( 1 ) );
		Assert.AreEqual( "roll", IntroPlaylist.TrailerForDay( 7 ) );
		Assert.AreEqual( "bub", IntroPlaylist.TrailerForDay( 8 ) );
		Assert.AreEqual( "bub", IntroPlaylist.TrailerForDay( 16 ) );
		Assert.AreEqual( "roll", IntroPlaylist.TrailerForDay( 31 ) );
		Assert.AreEqual( "roc", IntroPlaylist.TrailerForDay( 30 ) );
	}

	[TestMethod]
	public void EveryDayMapsToOneOfTheEightTrailersAndNeverTheLogo()
	{
		for ( var day = 1; day <= 31; day++ )
		{
			var name = IntroPlaylist.TrailerForDay( day );
			CollectionAssert.Contains( (System.Collections.ICollection)IntroPlaylist.Trailers, name );
			Assert.AreNotEqual( IntroPlaylist.Logo, name );
		}
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => IntroPlaylist.TrailerForDay( 0 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => IntroPlaylist.TrailerForDay( 32 ) );
	}

	[TestMethod]
	public void SequenceIsLogoThenTheTrailerOfTheLocalDate()
	{
		CollectionAssert.AreEqual( new[] { "bf", "mir" }, new System.Collections.Generic.List<string>( IntroPlaylist.For( new DateTime( 2026, 10, 12 ) ) ) );
	}

	[TestMethod]
	public void SkipGateIgnoresInputHeldAtLaunchUntilItIsReleased()
	{
		var gate = new IntroSkipGate();
		Assert.IsFalse( gate.Poll( true ) );
		Assert.IsFalse( gate.Poll( true ) );
		Assert.IsFalse( gate.Poll( false ) );
		Assert.IsTrue( gate.Poll( true ), "a fresh press skips" );
		Assert.IsTrue( gate.Poll( true ), "input still held also skips the next movie, as in the original" );
		Assert.IsFalse( gate.Poll( false ) );
	}

	[TestMethod]
	public void GainScalesPcmLinearly()
	{
		short[] samples = [1000, -1000, 32767, -32768];
		MoviePlayback.ApplyGain( samples, 1f );
		CollectionAssert.AreEqual( new short[] { 1000, -1000, 32767, -32768 }, samples );
		MoviePlayback.ApplyGain( samples, 0.5f );
		CollectionAssert.AreEqual( new short[] { 500, -500, 16383, -16384 }, samples );
		MoviePlayback.ApplyGain( samples, 0f );
		CollectionAssert.AreEqual( new short[4], samples );
	}
}
