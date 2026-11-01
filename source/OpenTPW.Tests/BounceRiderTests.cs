using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// BOUNCESETNODE (tp.exe 0x005555D9) and BOUNCESETBASE (0x00555618) store the node base (+0x70) and the base height (+0x6E) of the script; the per-frame script pass 0x00557AB0 reads them to draw the
/// riders of the BOUNCE table (docs/RSE-VM.md, "Bounce opcodes"; docs/GUESTS.md, "Bouncing riders").
/// </summary>
[TestClass]
[DoNotParallelize]
public class BounceRiderTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	private static (RideVisitorBridge Bridge, RideVM Vm) Run( Action<Asm> program, int ticks = 4 )
	{
		var asm = new Asm();
		program( asm );
		asm.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" );
		var bridge = new RideVisitorBridge( 5, "Bounce", RideVisitorKind.Shop, 1, 0, 10 );
		var vm = new RideVM( new RideScriptFile( new MemoryStream( asm.Build( GuestTests.ShopVariables ) ) ), new RideVMOptions { Effects = new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Seed = 1 } );
		bridge.Attach( vm, () => true );
		for ( var tick = 0; tick < ticks; tick++ )
			vm.Advance( Tick );
		return (bridge, vm);
	}

	[TestMethod]
	public void TheOpcodesStoreTheirOperandsAndAreNoLongerUnimplementedEffects()
	{
		var fresh = new RideVisitorBridge( 5, "Bounce", RideVisitorKind.Shop, 1, 0, 10 );
		Assert.AreEqual( (1, (short)0), (fresh.BounceNodeBase, fresh.BounceBaseHeight), "script creation 0x00558C45 starts the node base at 1, the base height at 0" );

		var (bridge, vm) = Run( asm => asm.I( Opcode.BOUNCESETNODE, 3 ).I( Opcode.BOUNCESETBASE, 8 ) );
		Assert.AreEqual( 3, bridge.BounceNodeBase );
		Assert.AreEqual( (short)8, bridge.BounceBaseHeight );
		Assert.AreEqual( 0, vm.UnimplementedEffects.Count, "the values are recorded: neither opcode reaches the unimplemented effect hook" );
		Assert.AreEqual( RideVMState.Running, vm.State );
	}

	[TestMethod]
	public void TheBaseHeightKeepsTheLowSixteenBitsAndTheNodeBaseTheRawWord()
	{
		// 0x555653: a variable operand's value goes in as a word; 0x555665: a literal is sign-extended from 16 bits (0xFFFF is -1)
		var (bridge, _) = Run( asm => asm.I( Opcode.COPY, V( 0 ), 0x7FFF ).I( Opcode.ADD, V( 0 ), 2 ).I( Opcode.BOUNCESETBASE, V( 0 ) ) );
		Assert.AreEqual( unchecked( (short)0x8001 ), bridge.BounceBaseHeight, "0x8001 as a signed word" );
		Assert.AreEqual( (short)-1, Run( asm => asm.I( Opcode.BOUNCESETBASE, 0xFFFF ) ).Bridge.BounceBaseHeight );
		// BOUNCESETNODE stores the unresolved code word: a variable operand is 0x40000000 plus its index
		Assert.AreEqual( 0x4000_0002, Run( asm => asm.I( Opcode.BOUNCESETNODE, V( 2 ) ) ).Bridge.BounceNodeBase );
	}

	[TestMethod]
	public void TheValuesSurviveASnapshotAndOnlyAppearInItWhenTheyDifferFromTheCreationValues()
	{
		var fresh = new RideVisitorBridge( 5, "Bounce", RideVisitorKind.Shop, 1, 0, 10 ).CaptureSnapshot();
		Assert.IsNull( fresh.BounceNodeBase );
		Assert.IsNull( fresh.BounceBaseHeight );
		Assert.IsFalse( System.Text.Json.JsonSerializer.Serialize( fresh ).Contains( "BounceNodeBase" ), "a ride that never bounces keeps its saved form" );

		var (bridge, _) = Run( asm => asm.I( Opcode.BOUNCESETNODE, 3 ).I( Opcode.BOUNCESETBASE, 12 ) );
		var snapshot = bridge.CaptureSnapshot();
		Assert.AreEqual( 3, snapshot.BounceNodeBase );
		Assert.AreEqual( (short)12, snapshot.BounceBaseHeight );
		var restored = new RideVisitorBridge( 5, "Bounce", RideVisitorKind.Shop, 1, 0, 10 );
		var vm = new RideVM( new RideScriptFile( new MemoryStream( new Asm().Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( GuestTests.ShopVariables ) ) ), new RideVMOptions { Seed = 1 } );
		restored.Attach( vm, () => true );
		restored.RestoreSnapshot( snapshot with { QueueCells = new[] { (0, 0) } } );
		Assert.AreEqual( (3, (short)12), (restored.BounceNodeBase, restored.BounceBaseHeight) );
	}

	[TestMethod]
	public void ARidersNodeIsStoredWhenBounceFillsItsSlotAndALaterBaseDoesNotMoveIt()
	{
		// tp.exe 0x555742: BOUNCE stores script +0x70 plus the entry's index in the entry; BOUNCESETNODE afterwards changes the nodes of later riders only
		var (bridge, _) = Run( asm => asm.I( Opcode.COPY, V( 0 ), 44 ).I( Opcode.COPY, V( 1 ), 45 )
			.I( Opcode.BOUNCESETNODE, 3 ).I( Opcode.BOUNCE, V( 0 ), 5 )
			.I( Opcode.BOUNCESETNODE, 10 ).I( Opcode.BOUNCE, V( 1 ), 5 ), ticks: 6 );
		var riders = new List<(int Guest, int Node, double Start)>();
		bridge.CollectBouncing( riders );
		CollectionAssert.AreEqual( new[] { (44, 3), (45, 11) }, riders.Select( rider => (rider.Guest, rider.Node) ).ToArray(), "slot 0 keeps 3 + 0, slot 1 takes 10 + 1" );
		Assert.AreEqual( 10, bridge.BounceNodeBase );

		// the stored nodes differ from base + index, so the snapshot carries them, and a restore keeps them
		var snapshot = bridge.CaptureSnapshot();
		CollectionAssert.AreEqual( new[] { 3, 11 }, snapshot.BounceNodes );
		var restored = new RideVisitorBridge( 5, "Bounce", RideVisitorKind.Shop, 1, 0, 10 );
		var vm = new RideVM( new RideScriptFile( new MemoryStream( new Asm().Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( GuestTests.ShopVariables ) ) ), new RideVMOptions { Seed = 1 } );
		restored.Attach( vm, () => true );
		restored.RestoreSnapshot( snapshot with { QueueCells = new[] { (0, 0) } } );
		var again = new List<(int Guest, int Node, double Start)>();
		restored.CollectBouncing( again );
		CollectionAssert.AreEqual( new[] { (44, 3), (45, 11) }, again.Select( rider => (rider.Guest, rider.Node) ).ToArray() );

		// a snapshot whose node count does not match the table is refused
		Assert.IsTrue( RideVisitorBridge.ValidateSnapshot( snapshot with { QueueCells = new[] { (0, 0) }, BounceNodes = new[] { 3 } } ).Any( problem => problem.Contains( "stored nodes" ) ) );
	}

	[TestMethod]
	public void BounceTakesTheFirstFreeEntryForAnyGuestAboardOrNotAndTwice()
	{
		// [BIN:TPW-EXE:0x00555736 BOUNCE entry] tp.exe tests neither whether the guest is aboard nor whether it bounces already: guest 44 (aboard or not, the host only marks it boarded) fills entries 0 and 1, guest 45 entry 2
		var (bridge, _) = Run( asm => asm.I( Opcode.COPY, V( 0 ), 44 ).I( Opcode.COPY, V( 1 ), 45 )
			.I( Opcode.BOUNCESETNODE, 3 ).I( Opcode.BOUNCE, V( 0 ), 5 ).I( Opcode.BOUNCE, V( 0 ), 7 ).I( Opcode.BOUNCE, V( 1 ), 5 ), ticks: 8 );
		var riders = new List<(int Guest, int Node, double Start)>();
		bridge.CollectBouncing( riders );
		CollectionAssert.AreEqual( new[] { (44, 3), (44, 4), (45, 5) }, riders.Select( rider => (rider.Guest, rider.Node) ).ToArray() );
	}

	[TestMethod]
	public void WhenEveryNodeIsTheBasePlusItsIndexTheSnapshotKeepsItsSavedForm()
	{
		var (bridge, _) = Run( asm => asm.I( Opcode.COPY, V( 0 ), 44 ).I( Opcode.BOUNCESETNODE, 3 ).I( Opcode.BOUNCE, V( 0 ), 5 ), ticks: 6 );
		var snapshot = bridge.CaptureSnapshot();
		Assert.IsNull( snapshot.BounceNodes );
		Assert.IsFalse( System.Text.Json.JsonSerializer.Serialize( snapshot ).Contains( "BounceNodes" ) );
		var riders = new List<(int Guest, int Node, double Start)>();
		bridge.CollectBouncing( riders );
		Assert.AreEqual( 3, riders.Single().Node );
	}

	// ---- the rule of the per-frame script pass 0x00557AB0 -----------------------------------------

	[TestMethod]
	public void TheSineTableHas4096EntriesOfOneTurn()
	{
		Assert.AreEqual( 0f, BounceRiders.SineAt( 0 ) );
		Assert.AreEqual( 1f, BounceRiders.SineAt( 1024 ), 1e-6f );
		Assert.AreEqual( 0f, BounceRiders.SineAt( 2048 ), 1e-6f );
		Assert.AreEqual( -1f, BounceRiders.SineAt( 3072 ), 1e-6f );
	}

	[TestMethod]
	public void TheBouncePhaseRunsThroughHalfATurnOnceASecondOfScaledTime()
	{
		// the speed bias 50 scales the time by 0.8 + 0.005 x 50: with the float constant -0.005f the factor is 1.04999999944, so 1000 ms of ride time truncates to 1049, not 1050
		Assert.AreEqual( 0, BounceRiders.TableIndex( 0, 50 ) );
		// 1000 ms: scaled 1049, modulo 1000 = 49, angle 49 x 0.003125 = 0.153125 rad, index round(0.153125 x 4096 / 2 pi) = round(99.8) = 100
		Assert.AreEqual( 100, BounceRiders.TableIndex( 1000, 50 ) );
		var indexes = Enumerable.Range( 0, 5000 ).Select( ms => BounceRiders.TableIndex( ms, 50 ) ).ToList();
		Assert.IsTrue( indexes.Max() is >= 2030 and <= 2037, $"the half turn ends at 3.125 rad: index {indexes.Max()}" );
		// the sine over the whole half turn is not negative: the rider only ever bounces up from its base
		Assert.IsTrue( indexes.All( index => BounceRiders.SineAt( index ) >= 0 ), "a hump, never a dip" );
		// a faster script (higher bias word) bounces faster
		Assert.IsTrue( BounceRiders.TableIndex( 300, 100 ) > BounceRiders.TableIndex( 300, 0 ) );
	}

	[TestMethod]
	public void TheHeightIsTheBounceAboveTheRideAndTheBaseButNeverBelowTheNode()
	{
		// peak of the hump (angle 1.5708 rad); visitor 3 hops (3 mod 7 + 12) = 15 units
		var peakMs = Enumerable.Range( 0, 1000 ).MaxBy( ms => BounceRiders.SineAt( BounceRiders.TableIndex( ms, 50 ) ) );
		var peak = BounceRiders.SineAt( BounceRiders.TableIndex( peakMs, 50 ) );
		Assert.IsTrue( peak > 0.9999f, $"the hump peaks near 1: {peak}" );
		Assert.AreEqual( 15f * peak + 100f + 8f, BounceRiders.Height( 3, peakMs, 50, nodeHeight: 90f, rideHeight: 100f, baseHeight: 8 ), 1e-3f );
		Assert.AreEqual( 15f * peak + 100f + 8f, BounceRiders.Height( 10, peakMs, 50, 0f, 100f, 8 ), 1e-3f, "visitor 10 is 3 mod 7 as well" );
		Assert.AreEqual( 18f * peak + 100f + 8f, BounceRiders.Height( 6, peakMs, 50, 0f, 100f, 8 ), 1e-3f, "the amplitude is (visitor mod 7) + 12, at most 18" );
		// at the start of the second the sine is 0: the rider is at the ride's height plus the base, or on its node when that is higher
		Assert.AreEqual( 108f, BounceRiders.Height( 3, 0, 50, 90f, 100f, 8 ) );
		Assert.AreEqual( 130f, BounceRiders.Height( 3, 0, 50, 130f, 100f, 8 ), "the node is higher than the hump: the rider sits on the node" );
	}

	[TestMethod]
	public void TheHeadingTurnsOncePerHalfSecondWhileBouncingAndIsTheVisitorModuloSevenOnABrokenRide()
	{
		CollectionAssert.AreEqual( new[] { 0, 0, 1, 1, 2, 7, 0 }, new[] { 0, 499, 500, 999, 1000, 3999, 4000 }.Select( BounceRiders.BouncingSector ).ToArray() );
		Assert.AreEqual( 4, BounceRiders.BrokenSector( 11 ) );
		var nodeEngine = new System.Numerics.Vector3( 10, 20, 30 );
		var running = BounceRiders.Pose( 9, 5, nodeEngine, 5f, 500, 50, 8, broken: false );
		Assert.AreEqual( (10f, 20f), (running.Feet.X, running.Feet.Y), "the rider keeps the node's horizontal position" );
		Assert.AreEqual( (1, false), (running.Sector, running.Broken) );
		var broken = BounceRiders.Pose( 9, 5, nodeEngine, 5f, 500, 50, 8, broken: true );
		Assert.AreEqual( nodeEngine, broken.Feet, "a broken ride's riders stand on their nodes" );
		Assert.AreEqual( (2, true), (broken.Sector, broken.Broken) );
	}

	[TestMethod]
	public void TheEngineHeightIsTheOriginalsRuleInOriginalUnits()
	{
		var scale = ObjectPlacement.ModelScale;
		var ride = 40f * scale;
		var node = new System.Numerics.Vector3( 1, 2, 38f * scale );
		var pose = BounceRiders.Pose( 4, 1, node, ride, 700, 50, 6, false );
		var expected = BounceRiders.Height( 4, 700, 50, 38f, 40f, 6 ) * scale;
		Assert.AreEqual( expected, pose.Feet.Z, 1e-4f );
		Assert.IsTrue( pose.Feet.Z >= node.Z );
	}

	// ---- the shipped scripts and models -----------------------------------------------------------

	[TestMethod]
	public void ThePureRuleReadsTheShippedBounceRidesNodes()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the bounce corpus." );
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			var bouncers = 0;
			var bases = new SortedSet<int>();
			foreach ( var theme in new[] { "jungle", "hallow", "space", "fantasy" } )
				foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( x => x.ScriptPath != null && x.ModelPath != null ).OrderBy( x => x.InfoId ) )
				{
					var script = new RideScriptFile( entry.FileSystem.OpenRead( entry.ScriptPath! ) );
					var instructions = script.Instructions.ToList();
					var firstBounce = instructions.FindIndex( x => x.Opcode == (ushort)Opcode.BOUNCE );
					if ( firstBounce < 0 )
						continue;
					bouncers++;
					var setNode = instructions.Where( x => x.Opcode == (ushort)Opcode.BOUNCESETNODE ).ToList();
					var setBase = instructions.Where( x => x.Opcode == (ushort)Opcode.BOUNCESETBASE ).ToList();
					Assert.AreEqual( 1, setBase.Count, entry.DisplayName );
					Assert.IsTrue( instructions.IndexOf( setBase[0] ) < firstBounce, $"{entry.DisplayName}: the base is set before the first BOUNCE" );
					Assert.AreEqual( RideScriptOperandKind.Literal, setBase[0].Operands[0].Kind, entry.DisplayName );
					bases.Add( (short)setBase[0].Operands[0].Value );
					// only Jelly Bounce moves the node base (to 3); the other three keep the creation value 1 (0x00558C45)
					Assert.AreEqual( entry.DisplayName == "Jelly Bounce" ? 1 : 0, setNode.Count, entry.DisplayName );
					if ( setNode.Count > 0 )
					{
						Assert.IsTrue( instructions.IndexOf( setNode[0] ) < firstBounce, $"{entry.DisplayName}: the node base is set before the first BOUNCE, so a slot's node is the base plus its index at any time (HEADS-008)" );
						Assert.AreEqual( (RideScriptOperandKind.Literal, 3), (setNode[0].Operands[0].Kind, (int)setNode[0].Operands[0].Value), entry.DisplayName );
					}
					var expectedNodeBase = setNode.Count > 0 ? 3 : RideVisitorBridge.DefaultBounceNodeBase;

					var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), 1 );
					Assert.IsTrue( runtime.Visitors.RideHandlesSprite, $"{entry.DisplayName}: inherited boarding retains the pre-bounce body" );
					for ( var tick = 0; tick < 60 && runtime.Visitors.BounceBaseHeight == 0; tick++ )
						runtime.Simulate( Tick );
					Assert.AreEqual( expectedNodeBase, runtime.Visitors.BounceNodeBase, entry.DisplayName );
					Assert.AreEqual( (short)setBase[0].Operands[0].Value, runtime.Visitors.BounceBaseHeight, entry.DisplayName );
					Assert.AreEqual( 0, runtime.Script!.UnimplementedEffects.Keys.Count( x => x is Opcode.BOUNCESETNODE or Opcode.BOUNCESETBASE ), $"{entry.DisplayName}: the bounce setup opcodes are recorded, not unimplemented" );

					// every slot of the table has its node, and the rule puts a rider above the node at some time of every second
					Assert.IsTrue( script.BounceSize > 0, entry.DisplayName );
					for ( var slot = 0; slot < script.BounceSize; slot++ )
					{
						var node = runtime.FindDummyNode( 0x800, runtime.Visitors.BounceNodeBase + slot );
						// Jelly Bounce's base 3 puts its slots 7 to 9 on nodes 10 to 12, which its model lacks (nodes 1 to 9): 0x0044B220 finds none and the pass skips such a rider, as FlatRideBounceRiders does
						if ( node == null )
						{
							Assert.IsTrue( entry.DisplayName == "Jelly Bounce" && slot >= 7, $"{entry.DisplayName}: slot {slot} bounces on node {runtime.Visitors.BounceNodeBase + slot}" );
							continue;
						}
						var nodeHeight = ObjectPlacement.SwapAxes( runtime.Animator.NodeTransforms[node.Value] ).Translation.Z;
						var heights = Enumerable.Range( 0, 2000 ).Select( ms => BounceRiders.Height( 7, ms, (short)runtime.Script.SpeedBias, nodeHeight, 0f, runtime.Visitors.BounceBaseHeight ) ).ToList();
						Assert.IsTrue( heights.All( h => h >= nodeHeight ), $"{entry.DisplayName}: a rider is never below its node" );
						var rest = BounceRiders.Height( 7, 0, (short)runtime.Script.SpeedBias, float.MinValue, 0f, runtime.Visitors.BounceBaseHeight );
						Assert.AreEqual( runtime.Visitors.BounceBaseHeight, rest, 1e-4f, $"{entry.DisplayName}: the hump starts on the base height above the ride" );
					}
					runtime.Stop();
				}
			Assert.AreEqual( 4, bouncers, "Bouncy (jungle, space), Brain Buster and Jelly" );
			CollectionAssert.AreEqual( new[] { 6, 8, 12, 16 }, bases.ToArray(), "the four base heights of the shipped scripts" );
		}
		finally
		{
			FileSystem = previous!;
			ObjectCatalog.BonusDataRoot = null;
		}
	}

	/// <summary>The park source's per-ride step places each rider of a bounce table on its slot's node, as the rule says, and stands them on their nodes while the ride is broken.</summary>
	[TestMethod]
	public void TheParkSourcePosesTheRidersOfAPlacedBounceRide()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the bounce corpus." );
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			var entry = ObjectCatalog.Load( "fantasy" ).Entries.Single( x => x.DisplayName == "Jelly Bounce" );
			var ride = new OriginalObjectRuntime( entry, new RideScriptWorld(), 1 );
			System.Numerics.Vector3 NodeWorld( int node ) => ObjectPlacement.SwapAxes( ride.Animator.NodeTransforms[node] ).Translation;
			for ( var tick = 0; tick < 60 && ride.Visitors.BounceBaseHeight == 0; tick++ )
				ride.Simulate( Tick );
			Assert.AreEqual( (3, (short)12), (ride.Visitors.BounceNodeBase, ride.Visitors.BounceBaseHeight), "Jelly Bounce: BOUNCESETNODE 3, BOUNCESETBASE 12" );

			var script = ride.Script!;
			var table = (List<(int Guest, double Until, double Start)>)typeof( RideVisitorBridge ).GetField( "bouncing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( ride.Visitors )!;
			table.Add( (11, 1e9, script.TimeMilliseconds) );
			table.Add( (0, 0, 0) );
			table.Add( (12, 1e9, script.TimeMilliseconds - 300) );
			table.Add( (13, 1e9, script.TimeMilliseconds - 300) );
			for ( var index = 0; index < 4; index++ )
				table.Add( (0, 0, 0) );
			table.Add( (14, 1e9, script.TimeMilliseconds) );   // slot 8: node 11, which the model lacks
			var scratch = new List<(int Guest, int Node, double Start)>();
			var poses = new Dictionary<int, BounceRiderPose>();
			FlatRideBounceRiders.Collect( poses, ride, NodeWorld, 0f, scratch );
			CollectionAssert.AreEquivalent( new[] { 11, 12, 13 }, poses.Keys.ToArray(), "slots 0, 2 and 3 are drawn; slot 1 is free and slot 8 has no node" );

			var now = script.TimeMilliseconds;
			const float rideHeight = 0f;
			foreach ( var (guest, slot, start) in new[] { (11, 0, now), (12, 2, now - 300), (13, 3, now - 300) } )
			{
				var node = ride.FindDummyNode( 0x800, 3 + slot )!.Value;
				var expected = BounceRiders.Pose( guest, ride.Visitors.AttractionId, NodeWorld( node ), rideHeight, (int)(now - start), (short)script.SpeedBias, 12, false );
				Assert.AreEqual( expected, poses[guest], $"rider {guest} on node {3 + slot}" );
				Assert.AreEqual( NodeWorld( node ).X, poses[guest].Feet.X, 1e-4f );
				Assert.IsTrue( poses[guest].Feet.Z >= NodeWorld( node ).Z, "never below the node" );
			}

			ride.SetVariable( nameof( RideVariables.VAR_BROKEN ), 1 );
			poses.Clear();
			FlatRideBounceRiders.Collect( poses, ride, NodeWorld, 0f, scratch );
			foreach ( var (guest, slot) in new[] { (11, 0), (12, 2), (13, 3) } )
			{
				var node = ride.FindDummyNode( 0x800, 3 + slot )!.Value;
				Assert.AreEqual( NodeWorld( node ), poses[guest].Feet, $"rider {guest} stands on its node while the ride is broken" );
				Assert.IsTrue( poses[guest].Broken );
				Assert.AreEqual( guest % 7, poses[guest].Sector );
			}
			ride.Stop();
		}
		finally
		{
			FileSystem = previous!;
			ObjectCatalog.BonusDataRoot = null;
		}
	}
}
