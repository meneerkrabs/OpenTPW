using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// TOUR-011: the script's walk table as tp.exe keeps it. WALKON (0x00556F40) takes the first free entry of the header's walk size, WALKOFF (0x005571A0) finds its
/// rider's entry and starts the walk off, WALKGET (0x00557110) hands out the first entry in state 4 and frees it; the frame update 0x00557D80 does the state changes in between.
/// </summary>
[TestClass]
public class WalkTableTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	/// <summary>Runs one straight-line program (every instruction once, then idles) and returns the bridge and the script.</summary>
	private static (RideVisitorBridge Bridge, RideVM Vm) Run( Action<Asm> program, int ticks = 120, Func<int, int, int, float?>? distance = null, Func<int, int, int, int, float?>? onDistance = null, bool frameUpdate = false )
	{
		var asm = new Asm();
		program( asm );
		asm.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" );
		var bridge = new RideVisitorBridge( 5, "Walk", RideVisitorKind.Shop, 1, 0, 10 ) { WalkNodeDistance = distance, WalkNodeClassDistance = onDistance };
		var vm = new RideVM( new RideScriptFile( new MemoryStream( asm.Build( GuestTests.ShopVariables ) ) ), new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } );
		bridge.Attach( vm, () => true );
		for ( var tick = 0; tick < ticks; tick++ )
		{
			vm.Advance( Tick );
			// the frame's walk update (0x00557D80) runs after the substeps, so a WALKGET sees a walk that ended only at a later pass
			if ( frameUpdate )
				bridge.WalkUpdate();
		}
		return (bridge, vm);
	}

	private static uint Result( int index ) => V( (int)RideVariables.VAR_PAD + index );

	[TestMethod]
	public void WalkGetHandsOutTheEntriesInTableOrderNotInTheOrderOfWalkOff()
	{
		var (bridge, vm) = Run( asm => asm
			.I( Opcode.WALKON, 1, 0, 0, 0, 0, 0, 0 ).I( Opcode.WALKON, 2, 0, 0, 0, 0, 0, 0 ).I( Opcode.WALKON, 3, 0, 0, 0, 0, 0, 0 )
			.I( Opcode.WALKOFF, 3 ).I( Opcode.WALKOFF, 1 ).I( Opcode.WALKOFF, 2 )
			.I( Opcode.WALKGET, V( 4 ) ).I( Opcode.WAIT, 100 ).I( Opcode.WALKGET, V( 0 ) ).I( Opcode.WALKGET, V( 1 ) ).I( Opcode.WALKGET, V( 2 ) ).I( Opcode.WALKGET, V( 3 ) ), frameUpdate: true );
		Assert.AreEqual( 0, vm.Variables[4], "a walk that ended in this very pass is not state 4 yet: only the frame's walk update moves it on (0x00557D80)" );
		CollectionAssert.AreEqual( new[] { 1, 2, 3, 0 }, new[] { vm.Variables[0], vm.Variables[1], vm.Variables[2], vm.Variables[3] }, "slot 0, 1 and 2 in turn, then nothing" );
		Assert.AreEqual( 0, bridge.CaptureSnapshot().WalkTable?.Length ?? 0, "WALKGET freed the entries" );
	}

	[TestMethod]
	public void TheTableHoldsAsManyWalkersAsTheHeaderWalkSize()
	{
		// the test header declares 5 entries (RseScriptTests.CreateScript): the sixth WALKON stores nothing, so WALKOFF finds no entry and WALKGET never gives that rider back
		var (bridge, vm) = Run( asm =>
		{
			foreach ( var guest in Enumerable.Range( 1, 6 ) )
				asm.I( Opcode.WALKON, guest, 0, 0, 0, 0, 0, 0 );
			asm.I( Opcode.WALKOFF, 6 ).I( Opcode.WALKGET, V( 0 ) ).I( Opcode.WALKOFF, 4 ).I( Opcode.WAIT, 100 ).I( Opcode.WALKGET, V( 1 ) );
		}, frameUpdate: true );
		Assert.AreEqual( 0, vm.Variables[0], "no entry for the sixth walker" );
		Assert.AreEqual( 4, vm.Variables[1] );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 4 }, bridge.CaptureSnapshot().WalkTable!.Select( entry => entry.Slot ).ToArray(), "riders 1, 2, 3 and 5 keep their entries; rider 4 left through its entry" );
		CollectionAssert.AreEqual( new[] { 1, 2, 3, 5 }, bridge.CaptureSnapshot().WalkTable!.Select( entry => entry.Guest ).ToArray() );
	}

	[TestMethod]
	public void AFreedEntryIsTheFirstFreeOneForTheNextWalker()
	{
		var (bridge, _) = Run( asm => asm
			.I( Opcode.WALKON, 1, 0, 0, 0, 0, 0, 0 ).I( Opcode.WALKON, 2, 0, 0, 0, 0, 0, 0 ).I( Opcode.WALKON, 3, 0, 0, 0, 0, 0, 0 )
			.I( Opcode.WALKOFF, 2 ).I( Opcode.WAIT, 100 ).I( Opcode.WALKGET, V( 0 ) ).I( Opcode.WALKON, 4, 0, 0, 0, 0, 0, 0 ), frameUpdate: true );
		CollectionAssert.AreEqual( new[] { (0, 1), (1, 4), (2, 3) }, bridge.CaptureSnapshot().WalkTable!.Select( entry => (entry.Slot, entry.Guest) ).ToArray() );
	}

	[TestMethod]
	public void WalkOffOfARiderWithoutAnEntryDoesNothing()
	{
		var (bridge, vm) = Run( asm => asm.I( Opcode.WALKON, 1, 0, 0, 0, 0, 0, 0 ).I( Opcode.WALKOFF, 9 ).I( Opcode.WALKGET, V( 0 ) ) );
		Assert.AreEqual( 0, vm.Variables[0] );
		var entry = bridge.CaptureSnapshot().WalkTable!.Single();
		Assert.AreEqual( (1, 1), (entry.Guest, entry.State), "rider 1 keeps its entry untouched, in the walk-on state the WALKON gave it" );
	}

	[TestMethod]
	public void TheWalkOffStartsWhateverTheStateAndWalkGetWaitsForItsEnd()
	{
		// WALKOFF twice (the second while the first still walks) restarts the walk: the entry's end is the second call's time plus the measured walk
		var (bridge, vm) = Run( asm => asm
				.I( Opcode.WALKON, 1, 5, 6, 3, 4, 1, 0 ).I( Opcode.WALKOFF, 1 ).I( Opcode.WALKGET, V( 0 ) ).I( Opcode.WAIT, 400 ).I( Opcode.WALKOFF, 1 )
				.I( Opcode.WAIT, 800 ).I( Opcode.WALKGET, V( 1 ) ).I( Opcode.WAIT, 400 ).I( Opcode.WALKGET, V( 2 ) ),
			ticks: 5 * 60, distance: ( _, _, _ ) => 10f, frameUpdate: true );
		Assert.AreEqual( 0, vm.Variables[0], "the walk takes 1000 ms" );
		Assert.AreEqual( 0, vm.Variables[1], "the restarted walk is not over 800 ms after it started" );
		Assert.AreEqual( 1, vm.Variables[2], "and is 1200 ms after it started" );
		Assert.AreEqual( 0, bridge.CaptureSnapshot().WalkTable?.Length ?? 0 );
	}

	[TestMethod]
	public void TheWalkOnTakesTheMeasuredDistanceTimes100MillisecondsAndTheNodesAreOperandsUOneAndUTwo()
	{
		var asked = new System.Collections.Generic.List<(int, int, int, int)>();
		var (bridge, _) = Run( asm => asm.I( Opcode.WALKON, 7, 5, 6, 3, 4, 4, 0 ), onDistance: ( fromMask, from, toMask, to ) =>
		{
			asked.Add( (fromMask, from, toMask, to) );
			return 2.5f;
		} );
		// the first node is class 0x800, the second class 0x80 for the head action 4 (else 0x800)
		CollectionAssert.AreEqual( new[] { (0x800, 5, 0x80, 6) }, asked );
		var entry = bridge.CaptureSnapshot().WalkTable!.Single();
		Assert.AreEqual( 200, entry.Ends - entry.Began, "ftol(2.5) x 100 ms" );
		Assert.AreEqual( (5, 6, 3, 4, 4), (entry.Via, entry.Target, entry.From, entry.To, entry.Mode), "u1, u2, u3, u4 and the action are the operands 2 to 6" );
	}

	[TestMethod]
	public void TheTableSurvivesASnapshotAndAnOldSaveRestoresItsWalkersIntoTheTable()
	{
		var (bridge, _) = Run( asm => asm.I( Opcode.WALKON, 1, 0, 0, 3, 4, 2, 0 ).I( Opcode.WALKON, 2, 0, 0, 3, 4, 2, 0 ).I( Opcode.WALKOFF, 2 ), distance: ( _, _, _ ) => 20f );
		var snapshot = bridge.CaptureSnapshot();
		Assert.AreEqual( 0, RideVisitorBridge.ValidateSnapshot( snapshot ).Count( error => error.Contains( "walk" ) ) );
		var round = ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( snapshot ) );
		CollectionAssert.AreEqual( snapshot.WalkTable, round.WalkTable );
		CollectionAssert.AreEqual( new[] { 2 }, snapshot.WalkingOff, "the older part lists the riders that walk off" );
		CollectionAssert.AreEqual( new[] { (1, 3, 4, 2) }, snapshot.WalkSlots );
		// a save from before the table: the on-board walker takes slot 0, the walker that leaves slot 1 with the saved end time
		var (fresh, _) = Run( asm => { }, ticks: 0 );
		var grid = new GuestPathGrid( 4, 4 );
		grid.SetPath( 0, 0, true );
		fresh.RecomputeQueue( grid );
		fresh.RestoreSnapshot( snapshot with { WalkTable = null, QueueCells = fresh.CaptureSnapshot().QueueCells } );
		var restored = fresh.CaptureSnapshot().WalkTable!;
		CollectionAssert.AreEqual( new[] { (0, 1, 2), (1, 2, 3) }, restored.Select( entry => (entry.Slot, entry.Guest, entry.State) ).ToArray() );
		Assert.AreEqual( snapshot.WalkingOffReady![0], restored[1].Ends );
	}

	/// <summary>Runs a WALKON (and optionally a WALKOFF after a wait) with host steps in between, as the park does.</summary>
	private static (RideVisitorBridge Bridge, List<(int Node, double Time)> OnEffects, List<double> HeadTimes) RunHeadWalk( int action, float distance, int? walkOffAfterMs )
	{
		var asm = new Asm().I( Opcode.WALKON, 1, 5, 6, 3, 4, action, 1 );
		if ( walkOffAfterMs is { } wait )
			asm.I( Opcode.WAIT, wait ).I( Opcode.WALKOFF, 1 );
		asm.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" );
		var onEffects = new List<(int, double)>();
		var heads = new List<double>();
		var bridge = new RideVisitorBridge( 5, "Walk", RideVisitorKind.Shop, 1, 0, 10 )
		{
			Heads = RideHeadSpace.Of( new[] { 10, 11, 12, 13, 14, 15 }, 1 ),
			WalkNodeClassDistance = ( _, _, _, _ ) => distance,
			WalkNodeDistance = ( _, _, _ ) => distance
		};
		var vm = new RideVM( new RideScriptFile( new MemoryStream( asm.Build( GuestTests.ShopVariables ) ) ), new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } );
		bridge.WalkOnEffect = node => onEffects.Add( (node, vm.TimeMilliseconds) );
		bridge.Attach( vm, () => true );
		for ( var tick = 0; tick < 3 * 60; tick++ )
		{
			bridge.HostStep();
			vm.Advance( Tick );
			bridge.WalkUpdate();
			if ( bridge.HungHeads().Any() && heads.Count == 0 )
				heads.Add( vm.TimeMilliseconds );
		}
		return (bridge, onEffects, heads);
	}

	[TestMethod]
	public void AHeadHangsWhenTheWalkOnEndsNotWhenItStarts()
	{
		var (bridge, _, heads) = RunHeadWalk( 4, 10f, null );
		Assert.AreEqual( 1, heads.Count, "the head hung" );
		Assert.IsTrue( heads[0] >= 1000 && heads[0] < 1100, $"ftol(10) x 100 ms after the WALKON (at about 0 ms), got {heads[0]}" );
		Assert.AreEqual( 2, bridge.CaptureSnapshot().WalkTable!.Single().State, "the entry is on board" );
	}

	[TestMethod]
	public void AWalkOffInsideTheWalkOnTimeNeverGetsTheHead()
	{
		var (bridge, _, heads) = RunHeadWalk( 4, 10f, walkOffAfterMs: 400 );
		Assert.AreEqual( 0, heads.Count, "state 3 skips the head: the entry never reached state 2 (tp.exe 0x005571E4 and 0x00557343)" );
		Assert.AreEqual( 0, bridge.HungHeads().Count() );
	}

	[TestMethod]
	public void TheWalkOnEndOfWalkAction2StartsItsEffectAtNodeUTwoOnce()
	{
		var (_, effects, _) = RunHeadWalk( 2, 10f, null );
		Assert.AreEqual( 1, effects.Count );
		Assert.AreEqual( 6, effects[0].Node, "the effect sits at the second node operand" );
		Assert.IsTrue( effects[0].Time >= 1000 && effects[0].Time < 1100, $"at the end of the walk on, got {effects[0].Time}" );
		Assert.AreEqual( 0, RunHeadWalk( 1, 10f, null ).OnEffects.Count, "only action 2 creates it" );
	}

	[TestMethod]
	public void BounceSetsTheFlagsToOneWhileTheTableHasRoomAndToZeroWhenItIsFull()
	{
		// the test header declares 3 bounce entries; tp.exe 0x555780 / 0x555795 put 1 or 0 in the flag word
		var (_, vm) = Run( asm =>
		{
			for ( var index = 0; index < 4; index++ )
				asm.I( Opcode.BOUNCE, index + 1, 5 ).I( Opcode.BRANCH_Z, $"zero{index}" ).I( Opcode.COPY, V( index ), 1 ).I( Opcode.BRANCH, $"next{index}" )
					.Label( $"zero{index}" ).I( Opcode.COPY, V( index ), 2 ).Label( $"next{index}" );
		}, ticks: 2 );
		CollectionAssert.AreEqual( new[] { 1, 1, 1, 2 }, new[] { vm.Variables[0], vm.Variables[1], vm.Variables[2], vm.Variables[3] } );
	}
}
