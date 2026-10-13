using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Park-resume round-trips of a whole <see cref="OriginalObjectRuntime"/> against the original Jungle data
/// (inconclusive without OPENTPW_GAME_PATH): a running Totem is captured mid-ride, rebuilt through
/// <see cref="OriginalObjectRuntime.CreateForRestore"/> with its saved attraction id, and continued side by side
/// with the interrupted original — script, animator, head slots, counters and world allocators must agree tick
/// for tick, and mismatches must fail explicitly without touching the candidate.
/// </summary>
[TestClass]
public class OriginalObjectRuntimeSnapshotTests
{
	internal const double Tick = FixedStepClock.TickDuration;
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the runtime snapshot corpus." );
		originalFileSystem = previous;
		initialized = true;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
	}

	internal static ObjectCatalogEntry Totem() => ObjectCatalog.Load( "jungle" ).Get( 1110 );

	/// <summary>First Jungle entry matching <paramref name="where"/> that has a script, or null.</summary>
	internal static ObjectCatalogEntry? ScriptedEntry( Func<ObjectCatalogEntry, bool> where )
		=> ObjectCatalog.Load( "jungle" ).Entries.FirstOrDefault( entry => entry.ScriptPath != null && where( entry ) );

	private static ulong ScriptHash( RideVM? vm )
	{
		var hash = new StateHasher();
		vm!.AddCanonicalState( hash );
		return hash.Value;
	}

	private static ulong AnimatorHash( OriginalObjectRuntime runtime )
	{
		var hash = new StateHasher();
		runtime.Animator.AddCanonicalState( hash );
		return hash.Value;
	}

	[TestMethod]
	public void ARunningTotemContinuesIdenticallyAfterARestore()
	{
		const int seed = 99;
		var world = new RideScriptWorld();
		var runtime = new OriginalObjectRuntime( Totem(), world, seed: seed, open: true );
		for ( var tick = 0; tick < 90; tick++ )
			runtime.Simulate( Tick );
		Assert.AreNotEqual( RideVMState.Faulted, runtime.Script!.State, runtime.Script.FaultMessage );
		var runtimeSnapshot = runtime.CaptureSnapshot();
		var worldSnapshot = world.CaptureSnapshot();
		var rootId = runtime.Script.ScriptId;

		// A fresh world and a fresh runtime carrying the saved attraction id, seed and open flag.
		var restored = new RideScriptWorld();
		var candidate = OriginalObjectRuntime.CreateForRestore( Totem(), restored, seed: seed, open: true, attractionId: runtimeSnapshot.AttractionId );
		var gaps = candidate.RestoreSnapshot( runtimeSnapshot );
		Assert.AreEqual( 0, gaps.Count, string.Join( "; ", gaps ) );
		restored.RestoreSnapshot( worldSnapshot, new Dictionary<int, RideVM> { [rootId] = candidate.Script! } );

		Assert.AreEqual( runtime.AttractionId, candidate.AttractionId );
		Assert.AreEqual( rootId, candidate.Script!.ScriptId );
		Assert.AreEqual( runtime.Script.State, candidate.Script.State );
		Assert.AreEqual( ScriptHash( runtime.Script ), ScriptHash( candidate.Script ) );
		Assert.AreEqual( AnimatorHash( runtime ), AnimatorHash( candidate ) );
		Assert.AreEqual( runtime.State, candidate.State );
		Assert.AreEqual( runtime.CompletedCycles, candidate.CompletedCycles );
		Assert.AreEqual( world.NextAttractionId, restored.NextAttractionId );

		for ( var tick = 0; tick < 180; tick++ )
		{
			runtime.Simulate( Tick );
			candidate.Simulate( Tick );
			Assert.AreEqual( runtime.State, candidate.State, $"ride state diverged at tick {tick}" );
			Assert.AreEqual( AnimatorHash( runtime ), AnimatorHash( candidate ), $"animator diverged at tick {tick}" );
			Assert.AreEqual( ScriptHash( runtime.Script ), ScriptHash( candidate.Script ), $"script diverged at tick {tick}" );
			Assert.AreEqual( runtime.Animator.ActiveChannels, candidate.Animator.ActiveChannels );
			for ( var channel = 0; channel < 4; channel++ )
			{
				Assert.AreEqual( runtime.Animator.GetAnimationId( channel ), candidate.Animator.GetAnimationId( channel ), $"channel {channel} AnimID at tick {tick}" );
				Assert.AreEqual( runtime.Animator.GetTick( channel ), candidate.Animator.GetTick( channel ), $"channel {channel} tick at tick {tick}" );
			}
			Assert.AreEqual( world.ClockMilliseconds, restored.ClockMilliseconds, $"world clock diverged at tick {tick}" );
		}
		Assert.AreEqual( runtime.CompletedCycles, candidate.CompletedCycles, "cycle counting continues identically" );
		CollectionAssert.AreEqual( runtime.UnresolvedAnimations.ToArray(), candidate.UnresolvedAnimations.ToArray() );
		Assert.AreEqual( runtime.AnimationsPlayed, candidate.AnimationsPlayed );
	}

	[TestMethod]
	public void VertexAndTexturePosesContinueElementWiseAfterARestore()
	{
		var world = new RideScriptWorld();
		var runtime = new OriginalObjectRuntime( Totem(), world, seed: 7, open: true );
		for ( var tick = 0; tick < 60; tick++ )
			runtime.Simulate( Tick );
		var runtimeSnapshot = runtime.CaptureSnapshot();
		var worldSnapshot = world.CaptureSnapshot();

		var restored = new RideScriptWorld();
		var candidate = OriginalObjectRuntime.CreateForRestore( Totem(), restored, seed: 7, open: true, attractionId: runtimeSnapshot.AttractionId );
		_ = candidate.RestoreSnapshot( runtimeSnapshot );
		restored.RestoreSnapshot( worldSnapshot, new Dictionary<int, RideVM> { [runtime.Script!.ScriptId] = candidate.Script! } );

		var meshes = runtime.Animator.GetVertexPositions( 0 ) == null && runtime.Animator.GetTexCoords( 0 ) == null
			? new[] { 0 }
			: new[] { 0, 1 };
		for ( var tick = 0; tick < 120; tick++ )
		{
			runtime.Simulate( Tick );
			candidate.Simulate( Tick );
			foreach ( var mesh in meshes )
			{
				var expected = runtime.Animator.GetVertexPositions( mesh );
				var actual = candidate.Animator.GetVertexPositions( mesh );
				Assert.AreEqual( expected == null, actual == null, $"mesh {mesh} pose presence at tick {tick}" );
				if ( expected != null )
					for ( var vertex = 0; vertex < expected.Count; vertex++ )
						Assert.AreEqual( expected[vertex], actual![vertex], $"mesh {mesh} vertex {vertex} at tick {tick}" );
				Assert.AreEqual( runtime.Animator.GetVertexVersion( mesh ), candidate.Animator.GetVertexVersion( mesh ), $"mesh {mesh} version at tick {tick}" );
				Assert.AreEqual( runtime.Animator.HiddenVersion, candidate.Animator.HiddenVersion );
			}
		}
	}

	[TestMethod]
	public void RestoreRejectsACandidateOfAnotherEntry()
	{
		var world = new RideScriptWorld();
		var runtime = new OriginalObjectRuntime( Totem(), world, seed: 1, open: true );
		var snapshot = runtime.CaptureSnapshot();
		var other = ScriptedEntry( entry => entry.InfoId != Totem().InfoId && !CoasterDefinition.IsCoaster( entry.Settings ) );
		Assert.IsNotNull( other, "another scripted Jungle object exists" );

		var mismatch = Assert.ThrowsException<InvalidDataException>( () =>
		{
			var candidate = OriginalObjectRuntime.CreateForRestore( other!, new RideScriptWorld(), seed: 1, open: true, attractionId: snapshot.AttractionId );
			_ = candidate.RestoreSnapshot( snapshot );
		} );
		StringAssert.Contains( mismatch.Message, "belongs to" );
	}

	[TestMethod]
	public void RestoreRejectsAWrongAttractionIdCandidate()
	{
		var world = new RideScriptWorld();
		var runtime = new OriginalObjectRuntime( Totem(), world, seed: 1, open: true );
		var snapshot = runtime.CaptureSnapshot();
		var wrongId = Assert.ThrowsException<InvalidDataException>( () =>
		{
			// A normally-constructed runtime in a world that already spent id 1 allocates a different id.
			var candidateWorld = new RideScriptWorld();
			_ = candidateWorld.AllocateAttractionId();
			var candidate = new OriginalObjectRuntime( Totem(), candidateWorld, seed: 1, open: true );
			_ = candidate.RestoreSnapshot( snapshot );
		} );
		StringAssert.Contains( wrongId.Message, "attraction id" );
	}

	[TestMethod]
	public void AStoppedRuntimeTearsDownAndKeepsItsCountersOnRestore()
	{
		var world = new RideScriptWorld();
		var runtime = new OriginalObjectRuntime( Totem(), world, seed: 3, open: true );
		for ( var tick = 0; tick < 30; tick++ )
			runtime.Simulate( Tick );
		runtime.Stop();
		var runtimeSnapshot = runtime.CaptureSnapshot();
		var worldSnapshot = world.CaptureSnapshot();
		Assert.IsTrue( runtimeSnapshot.Stopped );
		Assert.AreEqual( 0, worldSnapshot.Scripts.Count, "the stopped runtime's script left the world" );

		var restored = new RideScriptWorld();
		var candidate = OriginalObjectRuntime.CreateForRestore( Totem(), restored, seed: 3, open: true, attractionId: runtimeSnapshot.AttractionId );
		_ = candidate.RestoreSnapshot( runtimeSnapshot );
		restored.RestoreSnapshot( worldSnapshot, new Dictionary<int, RideVM>() );
		Assert.IsNull( restored.Find( candidate.Script!.ScriptId ), "the stopped script stays out of the restored world" );
		Assert.AreEqual( runtime.CompletedCycles, candidate.CompletedCycles );
		candidate.Simulate( Tick );
		Assert.AreEqual( 0, candidate.Animator.ActiveChannels, "a stopped runtime stays stopped" );
	}

	[TestMethod]
	public void AStoppedFaultedRuntimeKeepsItsFaultAcrossARestore()
	{
		var world = new RideScriptWorld();
		var runtime = new OriginalObjectRuntime( Totem(), world, seed: 4, open: true );
		// Force a fault with a host write the script cannot digest: an out-of-range variable write is not one, so
		// fault through the script itself is not reachable cheaply — stop a faulted script instead by faulting the VM.
		var script = runtime.Script!;
		script.Variables[0] = 0;
		script.Abort( "diagnostic fault" );
		runtime.Stop();
		var snapshot = runtime.CaptureSnapshot();
		Assert.AreEqual( "diagnostic fault", snapshot.StoppedScriptFault );

		var candidate = OriginalObjectRuntime.CreateForRestore( Totem(), new RideScriptWorld(), seed: 4, open: true, attractionId: snapshot.AttractionId );
		_ = candidate.RestoreSnapshot( snapshot );
		Assert.AreEqual( RideVMState.Faulted, candidate.Script!.State );
		Assert.AreEqual( "diagnostic fault", candidate.Script.FaultMessage );
	}

	[TestMethod]
	public void AttractionIdsKeepTheirHolesAcrossARestore()
	{
		var totem = Totem();
		var second = ScriptedEntry( e => e.InfoId != totem.InfoId && !CoasterDefinition.IsCoaster( e.Settings ) );
		var third = second == null ? null : ScriptedEntry( e => e.InfoId != totem.InfoId && e.InfoId != second.InfoId && !CoasterDefinition.IsCoaster( e.Settings ) );
		if ( second == null || third == null )
			Assert.Inconclusive( "The Jungle data lacks three scripted non-coaster objects." );
		var world = new RideScriptWorld();
		var firstRuntime = new OriginalObjectRuntime( totem, world, seed: 1, open: true );  // attraction 1
		var demolished = new OriginalObjectRuntime( second, world, seed: 2, open: true );  // attraction 2
		var thirdRuntime = new OriginalObjectRuntime( third, world, seed: 3, open: true ); // attraction 3
		demolished.Stop();
		for ( var tick = 0; tick < 20; tick++ )
		{
			firstRuntime.Simulate( Tick );
			thirdRuntime.Simulate( Tick );
		}
		var saved = new[]
		{
			( Entry: totem, Seed: 1, Snapshot: firstRuntime.CaptureSnapshot() ),
			( Entry: third, Seed: 3, Snapshot: thirdRuntime.CaptureSnapshot() )
		};
		var worldSnapshot = world.CaptureSnapshot();
		Assert.AreEqual( 4, worldSnapshot.NextAttractionId );
		Assert.AreEqual( 2, worldSnapshot.Scripts.Count( script => script.ParentScriptId == 0 ) );

		// Rebuild in reverse order: the saved ids must not depend on construction order, and the roots are matched
		// by script identity, not by construction sequence.
		var restored = new RideScriptWorld();
		var candidates = new Dictionary<int, RideVM>();
		foreach ( var (entry, seed, snapshot) in saved.Reverse() )
		{
			var candidate = OriginalObjectRuntime.CreateForRestore( entry, restored, seed: seed, open: true, attractionId: snapshot.AttractionId );
			_ = candidate.RestoreSnapshot( snapshot );
			var root = worldSnapshot.Scripts.Single( script => script.ScriptIdentityHash == snapshot.ScriptIdentityHash );
			candidates[root.ScriptId] = candidate.Script!;
		}
		restored.RestoreSnapshot( worldSnapshot, candidates );
		Assert.AreEqual( 1, saved[0].Snapshot.AttractionId );
		Assert.AreEqual( 3, saved[1].Snapshot.AttractionId );
		Assert.AreEqual( 4, restored.NextAttractionId, "the demolished id 2 stays a hole" );
		Assert.AreEqual( world.AllocateAttractionId(), restored.AllocateAttractionId(), "the next allocation matches the original world" );
	}

	[TestMethod]
	public void AHeldScreamIsReportedAsAnExplicitRestoreGap()
	{
		var world = new RideScriptWorld();
		var runtime = new OriginalObjectRuntime( Totem(), world, seed: 5, open: true );
		var snapshot = runtime.CaptureSnapshot() with { HeldScream = true, OwnedScreamCount = 2 };
		var candidate = OriginalObjectRuntime.CreateForRestore( Totem(), new RideScriptWorld(), seed: 5, open: true, attractionId: snapshot.AttractionId );
		var gaps = candidate.RestoreSnapshot( snapshot );
		Assert.AreEqual( 2, gaps.Count, string.Join( "; ", gaps ) );
		StringAssert.Contains( gaps[0], "STARTSCREAM" );
		StringAssert.Contains( gaps[1], "2 owned scream" );
	}

	[TestMethod]
	public void ACoasterStationCapturesItsRuntimeAndWorld()
	{
		// The coaster slice: a station's runtime and script world capture with a live coaster controller — its
		// track, trains and riders are the envelope's coaster part (CoasterSave), not this snapshot's.
		var entry = ScriptedEntry( entry => CoasterDefinition.IsCoaster( entry.Settings ) );
		if ( entry == null )
			Assert.Inconclusive( "No coaster station in the Jungle data for the capture check." );
		var world = new RideScriptWorld();
		var runtime = new OriginalObjectRuntime( entry, world, seed: 1, open: true );
		var snapshot = runtime.CaptureSnapshot();
		Assert.AreEqual( runtime.AttractionId, snapshot.AttractionId );
		Assert.IsNotNull( runtime.Coaster );
		var worldSnapshot = world.CaptureSnapshot();
		Assert.AreEqual( runtime.AttractionId + 1, worldSnapshot.NextAttractionId );
	}

	[TestMethod]
	public void HeadSlotTableAndItsDrawContinueAfterARestore()
	{
		// A head-carrying ride model (the Totem has head dummies): drive ADDHEAD/DELHEAD through the bridge's
		// effects, then the restored head space must keep taking the same slots from the same draw state.
		var world = new RideScriptWorld();
		var runtime = new OriginalObjectRuntime( Totem(), world, seed: 8, open: true );
		var heads = runtime.Visitors.Heads;
		if ( heads == null )
			Assert.Inconclusive( "The Totem model has no head space." );
		var slots = new List<int>();
		for ( var visitor = 1; visitor <= Math.Min( 4, heads.Count ); visitor++ )
			slots.Add( heads.Add( visitor * 100 ) );
		CollectionAssert.AllItemsAreUnique( slots );
		var runtimeSnapshot = runtime.CaptureSnapshot();
		var worldSnapshot = world.CaptureSnapshot();

		var restored = new RideScriptWorld();
		var candidate = OriginalObjectRuntime.CreateForRestore( Totem(), restored, seed: 8, open: true, attractionId: runtimeSnapshot.AttractionId );
		_ = candidate.RestoreSnapshot( runtimeSnapshot );
		restored.RestoreSnapshot( worldSnapshot, new Dictionary<int, RideVM> { [runtime.Script!.ScriptId] = candidate.Script! } );

		var restoredHeads = candidate.Visitors.Heads!;
		Assert.AreEqual( heads.Count, restoredHeads.Count );
		for ( var slot = 0; slot < heads.Count; slot++ )
			Assert.AreEqual( heads.VisitorAt( slot ), restoredHeads.VisitorAt( slot ), $"slot {slot}" );
		Assert.AreEqual( Hash( runtime.Visitors.Heads! ), Hash( candidate.Visitors.Heads! ) );

		// Both continue their draws identically: free the first slot, then draw again.
		heads.Remove( slots[0] );
		restoredHeads.Remove( slots[0] );
		for ( var draw = 0; draw < 10; draw++ )
		{
			var expectedSlot = heads.Add( 500 + draw );
			var actualSlot = restoredHeads.Add( 500 + draw );
			Assert.AreEqual( expectedSlot, actualSlot, $"slot draw {draw}" );
		}
	}

	private static ulong Hash( RideHeadSpace heads )
	{
		var hash = new StateHasher();
		foreach ( var visitor in heads.CaptureSnapshot().Visitors )
			hash.Add( visitor );
		return hash.Value;
	}
}
