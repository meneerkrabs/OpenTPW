using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// WALKON with the head action 4 (Firepit, Inca Totem, Aztec Mayhem, Hover Bot Havoc, Rock 'n Roll) hangs the rider's head on head id u2 once the walk-on ends and
/// WALKOFF takes it down (tp.exe 0x00557D80, 0x005571A0). These rides run no ADDHEAD, so the riders were not drawn at all.
/// </summary>
[TestClass]
public class WalkOnHeadActionTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	private static RideVisitorBridge Run( int action, bool walkOff, int headId = 2 )
	{
		var on = V( (int)RideVariables.VAR_LETMEON );
		var off = V( (int)RideVariables.VAR_LETMEOFF );
		var temp = V( 6 );
		var asm = new Asm()
			.Label( "loop" )
			.I( Opcode.TEST, on )
			.I( Opcode.BRANCH_Z, "idle" )
			.I( Opcode.COPY, temp, on )
			.I( Opcode.WALKON, on, 1, headId, 3, 4, action, 1 )
			.I( Opcode.COPY, on, 0 );
		if ( walkOff )
			asm.I( Opcode.WALKOFF, temp );
		asm.Label( "idle" )
			.I( Opcode.WALKGET, off )
			.I( Opcode.ENDSLICE )
			.I( Opcode.BRANCH, "loop" );
		var bridge = new RideVisitorBridge( 5, "Walk", RideVisitorKind.Shop, 1, 0, 10 ) { Heads = RideHeadSpace.Of( new[] { 10, 11, 12 }, 1 ) };
		var vm = new RideVM( new RideScriptFile( new MemoryStream( asm.Build( GuestTests.ShopVariables ) ) ), new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } );
		bridge.Attach( vm, () => true );
		bridge.TryJoinQueue( 1 );
		for ( var tick = 0; tick < 5 * 60; tick++ )
		{
			bridge.HostStep();
			vm.Advance( Tick );
			bridge.WalkUpdate();
		}
		return bridge;
	}

	[TestMethod]
	public void TheHeadActionHangsTheRidersHeadOnItsHeadId()
	{
		var bridge = Run( action: 4, walkOff: false );
		CollectionAssert.AreEqual( new[] { (1, 11) }, bridge.HungHeads().ToArray(), "head id 2 is slot 1, node 11" );
	}

	[TestMethod]
	public void WalkOffTakesTheHeadDown()
	{
		var bridge = Run( action: 4, walkOff: true );
		Assert.AreEqual( 0, bridge.HungHeads().Count() );
	}

	[TestMethod]
	public void OtherWalkActionsHangNoHead()
	{
		Assert.AreEqual( 0, Run( action: 1, walkOff: false ).HungHeads().Count() );
	}

	[TestMethod]
	public void AHeadIdOutsideTheTableHangsNothing()
	{
		Assert.AreEqual( 0, Run( action: 4, walkOff: false, headId: 9 ).HungHeads().Count() );
	}

	[TestMethod]
	public void TheHeadActionSlotIsSavedSoWalkOffStillKnowsIt()
	{
		var bridge = Run( action: 4, walkOff: false );
		var snapshot = bridge.CaptureSnapshot();
		Assert.IsNotNull( snapshot.WalkSlots );
		Assert.AreEqual( 4, snapshot.WalkSlots![0].Mode );
	}
}
