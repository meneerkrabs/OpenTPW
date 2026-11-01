using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// TOUR-007: WALKOFF takes <c>ftol(the distance between the walker's two slot nodes) x 100</c> ms (100 ms for 0, tp.exe 0x005571A0); the slot is the one
/// WALKON stored (its fourth and fifth operands name the nodes, the sixth the walk action, 0x00556F40).
/// </summary>
[TestClass]
public class WalkOffTimingTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	private sealed class Host : IRideVisitorHost
	{
		public readonly List<(string Event, double Time)> Events = new();
		public Func<double> Clock = () => 0;
		public void OnVisitorOffered( IRideVisitorBridge ride, int guestId ) => Events.Add( ($"offer {guestId}", Clock()) );
		public void OnVisitorBoarded( IRideVisitorBridge ride, int guestId ) => Events.Add( ($"board {guestId}", Clock()) );
		public void OnVisitorReleased( IRideVisitorBridge ride, int guestId ) => Events.Add( ($"release {guestId}", Clock()) );
		public void OnVisitorTurnedAway( IRideVisitorBridge ride, int guestId ) => Events.Add( ($"away {guestId}", Clock()) );
	}

	/// <summary>Runs a ride whose script walks a guest on with the slot nodes 3 and 4 (action 2) and walks it off at once; returns the milliseconds from boarding to the release.</summary>
	private static (double Walk, RideVisitorBridge Bridge) Walk( Func<int, int, int, float?>? distance, int fallback, int guests = 1, Action<int>? effect = null, int action = 2 )
	{
		var on = V( (int)RideVariables.VAR_LETMEON );
		var off = V( (int)RideVariables.VAR_LETMEOFF );
		var temp = V( 6 );
		var asm = new Asm()
			.Label( "loop" )
			.I( Opcode.TEST, on )
			.I( Opcode.BRANCH_Z, "release" )
			.I( Opcode.COPY, temp, on )
			.I( Opcode.WALKON, on, 1, 2, 3, 4, action, 1 )
			.I( Opcode.COPY, on, 0 )
			.I( Opcode.WALKOFF, temp )
			.Label( "release" )
			.I( Opcode.WALKGET, off )
			.I( Opcode.ENDSLICE )
			.I( Opcode.BRANCH, "loop" );
		var bridge = new RideVisitorBridge( 5, "Walk", RideVisitorKind.Shop, 1, 0, 10 ) { WalkOffMilliseconds = fallback, WalkNodeDistance = distance, WalkOffEffect = effect };
		var vm = new RideVM( new RideScriptFile( new MemoryStream( asm.Build( GuestTests.ShopVariables ) ) ), new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } );
		bridge.Attach( vm, () => true );
		var host = new Host { Clock = () => vm.TimeMilliseconds };
		bridge.Host = host;
		foreach ( var guest in Enumerable.Range( 1, guests ) )
			bridge.TryJoinQueue( guest );
		for ( var tick = 0; tick < 5 * 60; tick++ )
		{
			bridge.HostStep();
			vm.Advance( Tick );
			bridge.WalkUpdate();
		}
		var boarded = host.Events.First( item => item.Event == "board 1" ).Time;
		var released = host.Events.First( item => item.Event == "release 1" ).Time;
		return (released - boarded, bridge);
	}

	[TestMethod]
	public void AWalkOffWithWalkAction2CreatesItsEffectAtTheStartNodeOnce()
	{
		// tp.exe 0x5572EB: only walk action 2 creates effect 0x13, at the slot's start node (operand u3)
		var started = new List<int>();
		Walk( ( _, _, _ ) => 10f, 1000, effect: started.Add );
		CollectionAssert.AreEqual( new[] { 3 }, started );
		var other = new List<int>();
		Walk( ( _, _, _ ) => 10f, 1000, effect: other.Add, action: 4 );
		Assert.AreEqual( 0, other.Count, "the head action creates no effect" );
	}

	[TestMethod]
	public void TheDurationIsTheTruncatedDistanceTimesOneHundredMilliseconds()
	{
		Assert.AreEqual( 1000, RideVisitorBridge.WalkOffDuration( 10.03f ) );
		Assert.AreEqual( 1000, RideVisitorBridge.WalkOffDuration( 10f ) );
		Assert.AreEqual( 1800, RideVisitorBridge.WalkOffDuration( 18.3f ) );
		Assert.AreEqual( 100, RideVisitorBridge.WalkOffDuration( 0.9f ), "a distance below one unit truncates to 0, which the original turns into 100 ms" );
		Assert.AreEqual( 100, RideVisitorBridge.WalkOffDuration( 0f ) );
	}

	[TestMethod]
	public void WalkOffMeasuresTheNodesTheWalkOnStored()
	{
		var asked = new List<(int Mode, int From, int To)>();
		var (walk, bridge) = Walk( ( mode, from, to ) =>
		{
			asked.Add( (mode, from, to) );
			return (mode, from, to) == (2, 3, 4) ? 3.9f : 40f;
		}, fallback: 1000 );
		CollectionAssert.AreEqual( new[] { (2, 3, 4) }, asked, "the slot's nodes are its third and fourth walk operands and the walk action" );
		Assert.IsTrue( walk >= 300 && walk < 400, $"ftol(3.9) x 100 = 300 ms, got {walk}" );
		Assert.AreEqual( 1, bridge.ReleasedTotal );
	}

	[TestMethod]
	public void AMissingNodeFallsBackToTheRidesDefaultWalk()
	{
		var (walk, _) = Walk( ( _, _, _ ) => null, fallback: 1000 );
		Assert.IsTrue( walk >= 1000 && walk < 1100, $"got {walk}" );
		var (plain, _) = Walk( null, fallback: 1000 );
		Assert.IsTrue( plain >= 1000 && plain < 1100, $"without a node resolver the default applies, got {plain}" );
	}

	[TestMethod]
	public void ASlotSurvivesAParkSave()
	{
		var on = V( (int)RideVariables.VAR_LETMEON );
		var temp = V( 6 );
		var asm = new Asm()
			.Label( "loop" )
			.I( Opcode.TEST, on )
			.I( Opcode.BRANCH_Z, "idle" )
			.I( Opcode.COPY, temp, on )
			.I( Opcode.WALKON, on, 1, 2, 3, 4, 2, 1 )
			.I( Opcode.COPY, on, 0 )
			.Label( "idle" )
			.I( Opcode.ENDSLICE )
			.I( Opcode.BRANCH, "loop" );
		RideVisitorBridge Create( out RideVM vm )
		{
			var bridge = new RideVisitorBridge( 5, "Walk", RideVisitorKind.Shop, 1, 0, 10 ) { WalkOffMilliseconds = 1000, WalkNodeDistance = ( _, _, _ ) => 2f };
			vm = new RideVM( new RideScriptFile( new MemoryStream( asm.Build( GuestTests.ShopVariables ) ) ), new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } );
			bridge.Attach( vm, () => true );
			return bridge;
		}
		var bridge = Create( out var vm );
		bridge.TryJoinQueue( 1 );
		for ( var tick = 0; tick < 120; tick++ )
		{
			bridge.HostStep();
			vm.Advance( Tick );
			bridge.WalkUpdate();
		}
		var snapshot = bridge.CaptureSnapshot();
		CollectionAssert.AreEqual( new[] { (1, 3, 4, 2) }, snapshot.WalkSlots, "the aboard walker's slot is saved" );
		Assert.AreEqual( 0, RideVisitorBridge.ValidateSnapshot( snapshot ).Count( error => error.Contains( "walk slot" ) ) );
		// a save from before the walk table (no table member) is checked through its two older parts
		Assert.IsTrue( RideVisitorBridge.ValidateSnapshot( snapshot with { WalkTable = null, WalkSlots = new[] { (2, 3, 4, 2) } } ).Any( error => error.Contains( "walk slot" ) ), "a slot of a guest that is not aboard is refused" );
		Assert.IsTrue( RideVisitorBridge.ValidateSnapshot( snapshot with { WalkTable = null, WalkSlots = new[] { (1, 3, 4, 2), (1, 1, 2, 2) } } ).Any( error => error.Contains( "repeat" ) ) );
		Assert.IsTrue( RideVisitorBridge.ValidateSnapshot( snapshot with { WalkTable = new[] { snapshot.WalkTable![0] with { Guest = 2 } } } ).Any( error => error.Contains( "walk table guest 2" ) ), "a table entry of a guest that is not aboard is refused" );
		Assert.IsTrue( RideVisitorBridge.ValidateSnapshot( snapshot with { WalkTable = new[] { snapshot.WalkTable![0], snapshot.WalkTable[0] } } ).Any( error => error.Contains( "twice" ) ) );
		Assert.IsTrue( RideVisitorBridge.ValidateSnapshot( snapshot with { WalkTable = new[] { snapshot.WalkTable![0] with { State = 5 } } } ).Any( error => error.Contains( "state 5" ) ) );
		// the part survives the file codec, and a snapshot without it (a save from before the part) leaves no member in the JSON and loads with none
		var round = ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( snapshot ) );
		CollectionAssert.AreEqual( snapshot.WalkSlots, round.WalkSlots, "the slots survive the JSON round trip" );
		CollectionAssert.AreEqual( snapshot.WalkTable, round.WalkTable, "the table survives the JSON round trip" );
		var none = snapshot with { WalkSlots = null, WalkTable = null };
		Assert.IsFalse( System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( none ) ).Contains( "WalkSlots" ) || System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( none ) ).Contains( "WalkTable" ), "an absent part writes no member" );
		Assert.IsNull( ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( none ) ).WalkSlots );
	}
}
