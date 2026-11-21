using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public sealed class ThemeParkIncForecastQueueTests
{
	[DataTestMethod]
	[DataRow( 1, 0x154, 642, 763, 62, 127 )]
	[DataRow( 2, 0x157, 645, 766, 65, 130 )]
	[DataRow( 3, 0x15B, 649, 938, 71, 156 )]
	[DataRow( 4, 0x15A, 648, 769, 68, 133 )]
	public void TheForecastResolvesTheNativeResponseAndResources( int kind, int message, int response, int speech, int tag, int text )
	{
		Assert.AreEqual( new ThemeParkIncForecastLine( kind, message, response, (uint)speech, tag, text ), ThemeParkIncForecastLine.For( kind ) );
		Assert.IsNull( ThemeParkIncForecastLine.For( 0 ) );
	}

	[TestMethod]
	public void PendingDuplicatesPlayAtTheExactPreviousSpeechWindowBoundary()
	{
		var queue = new ThemeParkIncForecastQueue( new( 10, 5, 5, 5, 5 ) );
		Assert.IsTrue( queue.Post( 2, 200, true ) );
		Assert.IsTrue( queue.Post( 2, 200, true ) );
		var submitted = new List<int>();
		uint Present( ThemeParkIncForecastLine line ) { submitted.Add( line.Response ); return 2_750; }
		Assert.AreEqual( 645, queue.Tick( 1_200, 200, true, Present )?.Response );
		Assert.IsNull( queue.Tick( 4_949, 214, true, Present ) );
		Assert.AreEqual( 645, queue.Tick( 4_950, 215, true, Present )?.Response );
		Assert.IsNull( queue.Tick( 8_700, 215, true, Present ) );
		CollectionAssert.AreEqual( new[] { 645, 645 }, submitted );
	}

	[TestMethod]
	public void RepeatsUseWorldTimeRatherThanThePresentationClock()
	{
		var queue = new ThemeParkIncForecastQueue( new( 10, 5, 13, 29, 43 ) );
		Assert.IsTrue( queue.Post( 2, 100, true ) );
		Assert.IsNotNull( queue.Tick( 40_000, 100, true, _ => 0 ) );
		Assert.IsFalse( queue.Post( 2, 112, true ) );
		Assert.IsTrue( queue.Post( 2, 113, true ) );
		Assert.IsNotNull( queue.Tick( 41_000, 113, true, _ => 0 ) );
		Assert.IsFalse( queue.Post( 2, 141, true ) );
		Assert.IsTrue( queue.Post( 2, 142, true ) );
	}

	[TestMethod]
	public void AnEqualScoreCannotReplaceAFullSlotAndTheFirstSlotWins()
	{
		var queue = new ThemeParkIncForecastQueue( new( 10, 0, 0, 0, 0 ) );
		Assert.IsTrue( queue.Post( 1, 100, true ) );
		for ( var index = 1; index < 8; index++ )
			Assert.IsTrue( queue.Post( 2, 100, true ) );
		Assert.IsTrue( queue.Post( 4, 100, true ) );
		Assert.AreEqual( 642, queue.Tick( 1_000, 100, true, _ => 0 )?.Response );
		for ( uint index = 2; index <= 8; index++ )
			Assert.AreEqual( 645, queue.Tick( index * 1_000, 100, true, _ => 0 )?.Response );
		Assert.IsNull( queue.Tick( 9_000, 100, true, _ => 0 ) );
	}

	[TestMethod]
	public void APositiveScoreReplacesTheFirstLowestScoreOnAFullQueue()
	{
		var queue = new ThemeParkIncForecastQueue( new( 10, 0, 0, 0, 0 ) );
		Assert.IsTrue( queue.Post( 2, 100, false ) );
		for ( var index = 1; index < 8; index++ )
			Assert.IsTrue( queue.Post( 2, 100, true ) );
		Assert.IsTrue( queue.Post( 4, 100, true ) );
		Assert.AreEqual( 648, queue.Tick( 1_000, 100, true, _ => 0 )?.Response );
	}

	[TestMethod]
	public void TheQueueStateRoundTripsAndRejectsForeignSlots()
	{
		var queue = new ThemeParkIncForecastQueue( new( 10, 5, 13, 29, 43 ) );
		Assert.IsTrue( queue.Post( 2, 100, true ) );
		Assert.IsTrue( queue.Post( 4, 100, false ) );
		Assert.IsNotNull( queue.Tick( 40_000, 100, true, _ => 0 ) );
		var restored = new ThemeParkIncForecastQueue( queue.Timing );
		restored.Restore( queue.Capture() );
		Assert.IsNull( restored.Tick( 41_000, 100, true, _ => 0 ), "the restored -1 slot exists but never plays" );
		Assert.IsTrue( restored.Post( 1, 100, true ) );
		Assert.AreEqual( 642, restored.Tick( 42_000, 100, true, _ => 0 )?.Response );
		Assert.IsFalse( restored.Post( 2, 112, true ), "the repeat history survives the round trip" );
		Assert.IsTrue( restored.Post( 2, 113, true ) );

		Assert.IsTrue( ThemeParkIncForecastQueue.IsValid( null ), "a save without forecast advice is valid" );
		var foreign = queue.Capture();
		foreign.Slots[0] = new( 9, 10_000 );
		Assert.IsFalse( ThemeParkIncForecastQueue.IsValid( foreign ) );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => restored.Restore( foreign ) );
		foreign = queue.Capture();
		foreign.Slots[0] = new( 2, 12_000 );
		Assert.IsFalse( ThemeParkIncForecastQueue.IsValid( foreign ) );
	}

	[TestMethod]
	public void ScoreFloorAndRecheckedEligibilityAreSeparateGates()
	{
		var queue = new ThemeParkIncForecastQueue( new( 10_000, 0, 0, 0, 0 ) );
		Assert.IsTrue( queue.Post( 2, 100, true ) );
		Assert.IsNull( queue.Tick( 1_200, 100, true, _ => throw new InvalidOperationException() ) );

		queue = new ThemeParkIncForecastQueue( new( 10, 0, 0, 0, 0 ) );
		Assert.IsTrue( queue.Post( 2, 100, false ) );
		Assert.IsNull( queue.Tick( 1_200, 100, true, _ => throw new InvalidOperationException() ) );
		Assert.IsFalse( queue.Post( 0, 100, true ) );
		Assert.IsTrue( queue.Post( 2, 100, true ) );
		Assert.IsNull( queue.Tick( 1_200, 100, false, _ => throw new InvalidOperationException() ) );
		Assert.IsNull( queue.Tick( 1_200, 100, true, _ => throw new InvalidOperationException() ) );
	}
}
