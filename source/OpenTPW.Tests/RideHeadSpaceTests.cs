using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The ADDHEAD / DELHEAD slot table (synthetic; the model reader is covered by RideHeadsCorpusTests).</summary>
[TestClass]
public class RideHeadSpaceTests
{
	[TestMethod]
	public void AddTakesAFreeSlotAndRefusesWhenFull()
	{
		var space = RideHeadSpace.Of( new[] { 4, 9 }, seed: 1 );
		var first = space.Add( 7 );
		var second = space.Add( 8 );
		CollectionAssert.AreEquivalent( new[] { 0, 1 }, new[] { first, second } );
		Assert.AreEqual( -1, space.Add( 9 ) );
		CollectionAssert.AreEquivalent( new[] { 7, 8 }, new[] { space.VisitorAt( 0 ), space.VisitorAt( 1 ) } );
	}

	[TestMethod]
	public void AddIgnoresVisitorZeroBecauseZeroMarksAFreeSlot()
	{
		var space = RideHeadSpace.Of( new[] { 4 }, seed: 1 );
		Assert.AreEqual( -1, space.Add( 0 ) );
		Assert.AreEqual( 0, space.VisitorAt( 0 ) );
	}

	[TestMethod]
	public void EmptyTableNeverTakesAVisitor()
	{
		var space = RideHeadSpace.Of( Array.Empty<int>(), seed: 1 );
		Assert.AreEqual( 0, space.Count );
		Assert.AreEqual( -1, space.Add( 3 ) );
		Assert.IsFalse( space.Remove( 3 ) );
	}

	[TestMethod]
	public void RemoveFreesEverySlotHoldingTheVisitor()
	{
		var space = RideHeadSpace.Of( new[] { 4, 9, 2 }, seed: 1 );
		space.Add( 5 );
		space.Add( 5 );
		space.Add( 6 );
		Assert.IsTrue( space.Remove( 5 ) );
		Assert.IsFalse( space.Remove( 5 ) );
		Assert.AreEqual( 1, space.Hung().Count( x => x.Visitor == 6 ) );
		Assert.AreEqual( 1, space.Hung().Count() );
	}

	[TestMethod]
	public void HungListsOnlySlotsWithANode()
	{
		var space = RideHeadSpace.Of( new[] { 11, -1, 13 }, seed: 1 );
		for ( var visitor = 1; visitor <= 3; visitor++ )
			space.Add( visitor );
		var hung = space.Hung().ToArray();
		Assert.AreEqual( 2, hung.Length );
		CollectionAssert.AreEquivalent( new[] { 11, 13 }, hung.Select( x => x.Node ).ToArray() );
		Assert.IsTrue( hung.All( x => x.Visitor is >= 1 and <= 3 ) );
	}

	[TestMethod]
	public void SameSeedDrawsTheSameSlots()
	{
		var first = RideHeadSpace.Of( new[] { 1, 2, 3, 4 }, seed: 42 );
		var second = RideHeadSpace.Of( new[] { 1, 2, 3, 4 }, seed: 42 );
		for ( var visitor = 1; visitor <= 4; visitor++ )
			Assert.AreEqual( first.Add( visitor ), second.Add( visitor ) );
	}
}
