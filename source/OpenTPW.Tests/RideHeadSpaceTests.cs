using System;
using System.Linq;
using System.IO;
using static OpenTPW.Tests.RideVMTests;
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
	[TestMethod]
	public void ReleasedRiderBoardsAnotherRideWithoutKeepingAnOldHead()
	{
		var grid = new GuestPathGrid( 3, 3 );
		grid.SetPath( 1, 1, true );
		var simulation = new GuestSimulation( grid, GuestTests.CreateSettings(), 1 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 1, 1 );
		(RideVisitorBridge Bridge, RideVM Script) Ride( int attraction )
		{
			var bridge = new RideVisitorBridge( attraction, $"Ride {attraction}", RideVisitorKind.Ride, 2, 25, 30 )
			{
				Heads = RideHeadSpace.Of( new[] { 4, 5 }, 1 ), EntranceCell = (1, 1), ExitCell = (1, 1), HasCells = true
			};
			var bytes = new Asm().I( Opcode.HUSH, guest.Id ).I( Opcode.ADDHEAD, guest.Id )
				.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( GuestTests.ShopVariables );
			var script = new RideVM( new RideScriptFile( new MemoryStream( bytes ) ), new RideVMOptions
			{
				Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1
			} );
			bridge.Attach( script, () => true );
			simulation.Register( bridge );
			return (bridge, script);
		}
		var a = Ride( 1 );
		var b = Ride( 2 );
		a.Script.Advance( Tick );
		Assert.IsTrue( RideHeadRenderer.CanDraw( guest, a.Bridge.AttractionId ) );
		Assert.AreEqual( 1, a.Bridge.Heads!.Hung().Count() );
		// An orphaned table entry is also host-owned and must not survive forced teardown.
		a.Bridge.Heads.Add( 99 );
		a.Script.Stop();
		a.Bridge.HostStep();
		Assert.IsFalse( a.Bridge.Heads.Hung().Any() );
		Assert.IsTrue( Enumerable.Range( 0, a.Bridge.Heads.Count ).All( slot => a.Bridge.Heads.VisitorAt( slot ) == 0 ) );
		b.Script.Advance( Tick );
		Assert.IsTrue( RideHeadRenderer.CanDraw( guest, b.Bridge.AttractionId ) );
		Assert.IsFalse( RideHeadRenderer.CanDraw( guest, a.Bridge.AttractionId ), "a Using rider on B never draws at A" );
		Assert.AreEqual( 1, b.Bridge.Heads!.Hung().Count() );
		Assert.IsTrue( a.Bridge.Heads.Add( guest.Id ) >= 0, "forced release leaves no full old table" );
		Assert.IsFalse( RideHeadRenderer.CanDraw( guest, a.Bridge.AttractionId ), "the attraction guard also rejects an accidentally stale slot" );
	}

	[TestMethod]
	public void NormalReleaseRemovesAHeadWithoutWaitingForDelhead()
	{
		var bridge = new RideVisitorBridge( 1, "Ride", RideVisitorKind.Ride, 1, 25, 30 ) { Heads = RideHeadSpace.Of( new[] { 4 }, 1 ) };
		var script = new RideVM( new RideScriptFile( new MemoryStream( new Asm()
			.I( Opcode.HUSH, 7 ).I( Opcode.ADDHEAD, 7 ).I( Opcode.COPY, V( Array.IndexOf( GuestTests.ShopVariables, nameof( RideVariables.VAR_LETMEOFF ) ) ), 7 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( GuestTests.ShopVariables ) ) ),
			new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } );
		bridge.Attach( script, () => true );
		script.Advance( Tick );
		Assert.AreEqual( 1, bridge.Heads.Hung().Count() );
		bridge.HostStep();
		Assert.AreEqual( 1, bridge.ReleasedTotal );
		Assert.IsFalse( bridge.Heads.Hung().Any() );
		Assert.AreEqual( 0, bridge.Heads.Add( 8 ), "the slot is reusable" );
	}

	[TestMethod]
	public void OnlyAUsingGuestOnTheMatchingAttractionIsDrawable()
	{
		Assert.IsFalse( RideHeadRenderer.CanDraw( null, 1 ) );
		var guest = new Guest( 1, 0 ) { AttractionId = 1, State = GuestState.WalkingAround };
		Assert.IsFalse( RideHeadRenderer.CanDraw( guest, 1 ) );
		guest.State = GuestState.Using;
		Assert.IsTrue( RideHeadRenderer.CanDraw( guest, 1 ) );
		Assert.IsFalse( RideHeadRenderer.CanDraw( guest, 2 ) );
	}

	[TestMethod]
	public void EveryScriptKeepsItsOwnHeadTable()
	{
		// The Mac script init (0x100B2BA0) allocates a head table for every script, root, child and sound alike.
		var childBytes = new Asm().I( Opcode.ADDHEAD, 77 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( GuestTests.ShopVariables );
		var bridge = new RideVisitorBridge( 1, "Ride", RideVisitorKind.Ride, 2, 25, 30 ) { Heads = RideHeadSpace.Of( new[] { 4, 5 }, 1 ) };
		var bytes = new Asm().I( Opcode.HUSH, 7 ).I( Opcode.ADDHEAD, 7 ).I( Opcode.SPAWNCHILD, S( 0 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( GuestTests.ShopVariables, "child.rse" );
		var script = new RideVM( new RideScriptFile( new MemoryStream( bytes ) ), new RideVMOptions
		{
			Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1,
			ResolveScript = name => name == "child.rse" ? new RideScriptFile( new MemoryStream( childBytes ) ) : null
		} );
		bridge.Attach( script, () => true );
		script.Advance( Tick );
		script.Advance( Tick );
		Assert.AreEqual( 1, bridge.Heads!.Hung().Count( x => x.Visitor == 7 ) );
		Assert.AreEqual( 0, bridge.Heads.Hung().Count( x => x.Visitor == 77 ), "the root's table does not take the child's rider" );
		Assert.AreEqual( 1, bridge.ChildHeadTableCount );
		Assert.AreEqual( 2, bridge.HungHeads().Count() );
		Assert.IsTrue( bridge.HungHeads().Any( x => x.Visitor == 77 ) );
		Assert.IsTrue( bridge.ChildHeadsOccupied(), "a snapshot must refuse while a child holds a head" );
		var withChild = new StateHasher();
		bridge.AddCanonicalState( withChild );
		// Stopping (or replacing, or removing) the child takes its table and its heads down.
		script.RemoveChild();
		Assert.AreEqual( 0, bridge.ChildHeadTableCount );
		Assert.IsFalse( bridge.ChildHeadsOccupied() );
		CollectionAssert.AreEqual( new[] { 7 }, bridge.HungHeads().Select( x => x.Visitor ).ToArray() );
		var without = new StateHasher();
		bridge.AddCanonicalState( without );
		Assert.AreNotEqual( withChild.Value, without.Value );
		bridge.ReleaseAll();
		Assert.IsFalse( bridge.HungHeads().Any() );
	}
}
