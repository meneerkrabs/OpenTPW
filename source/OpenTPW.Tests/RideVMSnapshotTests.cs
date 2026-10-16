using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Park-resume round-trips of the script world (synthetic scripts; original-corpus continuation lives in
/// <see cref="OriginalObjectRuntimeSnapshotTests"/>): capture a world mid-run, rebuild its roots as fresh
/// candidates and restore, then advance the interrupted and the restored worlds side by side — every canonical
/// script state, the clocks, the allocator frontiers (holes included) and the seed streams must match tick for
/// tick, and corrupt snapshots must fail validation without touching a live world.
/// </summary>
[TestClass]
public class RideVMSnapshotTests
{
	internal const double Tick = 1d / 60;

	private static RideVM CreateRoot( byte[] script, RideScriptWorld world, int? seed, string sourceName, Func<string, RideScriptFile?>? resolver )
		=> new( new RideScriptFile( new MemoryStream( script ) ), new RideVMOptions { Seed = seed, World = world, SourceName = sourceName, ResolveScript = resolver } );

	private static ulong Hash( RideVM vm )
	{
		var hash = new StateHasher();
		vm.AddCanonicalState( hash );
		return hash.Value;
	}

	private static IReadOnlyList<RideVM> Roots( RideScriptWorld world ) => world.Scripts.Where( vm => vm.Parent == null ).OrderBy( vm => vm.ScriptId ).ToArray();

	private static void AdvanceWorld( RideScriptWorld world, int ticks )
	{
		foreach ( var root in Roots( world ) )
			for ( var tick = 0; tick < ticks; tick++ )
				root.Advance( Tick );
	}

	// ---- Continuation ----

	[TestMethod]
	public void WaitingChildAndSoundChildContinueIdenticallyAfterRestore()
	{
		var child = new RideVMTests.Asm()
			.I( Opcode.NAME, RideVMTests.S( 0 ) )
			.I( Opcode.COPY, RideVMTests.V( 0 ), 5 )
			.I( Opcode.WAIT, 500 )
			.I( Opcode.COPY, RideVMTests.V( 1 ), 9 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "C0", "C1" }, "Child" );
		var sound = new RideVMTests.Asm()
			.I( Opcode.NAME, RideVMTests.S( 0 ) )
			.I( Opcode.COPY, RideVMTests.V( 0 ), 33 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "S0" }, "Sound" );
		// Strings: "Parent" at 0, "child.rse" at 7, "sound.rse" at 17.
		byte[] Parent() => new RideVMTests.Asm()
			.I( Opcode.NAME, RideVMTests.S( 0 ) )
			.I( Opcode.SPAWNSOUND, RideVMTests.S( 17 ) )
			.I( Opcode.SPAWNCHILD, RideVMTests.S( 7 ) )
			.Label( "loop" ).I( Opcode.GETVARINCHILD, RideVMTests.V( 0 ), 0 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Build( new[] { "V0", "V1", "V2", "V3" }, "Parent", "child.rse", "sound.rse" );
		RideScriptFile? Resolve( string name ) => name switch
		{
			"child.rse" => new RideScriptFile( new MemoryStream( child ) ),
			"sound.rse" => new RideScriptFile( new MemoryStream( sound ) ),
			_ => null
		};

		var world = new RideScriptWorld();
		var root = CreateRoot( Parent(), world, seed: 42, "parent.rse", Resolve );
		AdvanceWorld( world, 12 );
		Assert.AreEqual( RideVMState.Waiting, root.Child!.State, "captured mid-wait" );
		var snapshot = world.CaptureSnapshot();
		var rootId = root.ScriptId;

		var restored = new RideScriptWorld();
		var candidate = CreateRoot( Parent(), restored, seed: 42, "parent.rse", Resolve );
		restored.RestoreSnapshot( snapshot, new Dictionary<int, RideVM> { [rootId] = candidate } );

		Assert.AreEqual( rootId, candidate.ScriptId, "the saved script id is rebound" );
		Assert.AreEqual( root.Child.ScriptId, candidate.Child!.ScriptId );
		Assert.AreEqual( root.SoundChild!.ScriptId, candidate.SoundChild!.ScriptId );
		Assert.AreEqual( candidate, candidate.Child.Parent );
		Assert.AreEqual( candidate, candidate.SoundChild.Parent );
		Assert.AreEqual( "Child", candidate.Child.ScriptName );
		Assert.AreEqual( "Sound", candidate.SoundChild.ScriptName );
		Assert.AreEqual( root.Child.SourceName, candidate.Child.SourceName );
		Assert.AreEqual( Hash( root ), Hash( candidate ) );
		Assert.AreEqual( Hash( root.Child ), Hash( candidate.Child ) );
		Assert.AreEqual( Hash( root.SoundChild ), Hash( candidate.SoundChild ) );
		Assert.AreEqual( 3, restored.Scripts.Count );

		for ( var tick = 0; tick < 150; tick++ )
		{
			root.Advance( Tick );
			candidate.Advance( Tick );
			Assert.AreEqual( Hash( root ), Hash( candidate ), $"root diverged at tick {tick}" );
			Assert.AreEqual( Hash( root.Child ), Hash( candidate.Child ), $"child diverged at tick {tick}" );
			Assert.AreEqual( Hash( root.SoundChild ), Hash( candidate.SoundChild ), $"sound child diverged at tick {tick}" );
			Assert.AreEqual( world.ClockMilliseconds, restored.ClockMilliseconds );
			Assert.AreEqual( root.Child.Variables[1], candidate.Child.Variables[1] );
		}
		Assert.AreEqual( 9, candidate.Child.Variables[1], "the wait completed on the restored side too" );
		Assert.AreEqual( world.NextAttractionId, restored.NextAttractionId );
	}

	[TestMethod]
	public void CallStackAndProgramCounterSurviveARestoreMidWait()
	{
		byte[] Program() => new RideVMTests.Asm()
			.I( Opcode.COPY, RideVMTests.V( 0 ), 1 )
			.I( Opcode.JSR, "outer" )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Label( "outer" ).I( Opcode.ADD, RideVMTests.V( 1 ), 5 ).I( Opcode.WAIT, 300 ).I( Opcode.JSR, "inner" ).I( Opcode.ADD, RideVMTests.V( 2 ), 9 ).I( Opcode.RETURN )
			.Label( "inner" ).I( Opcode.WAIT, 100 ).I( Opcode.RETURN )
			.Build( new[] { "V0", "V1", "V2", "V3" } );
		var world = new RideScriptWorld();
		var vm = CreateRoot( Program(), world, seed: 5, "calls.rse", null );
		for ( var tick = 0; tick < 8; tick++ )
			vm.Advance( Tick );
		Assert.AreEqual( RideVMState.Waiting, vm.State );
		Assert.AreEqual( 1, vm.CallDepth, "captured inside the outer routine" );
		var snapshot = world.CaptureSnapshot();
		var id = vm.ScriptId;

		var restored = new RideScriptWorld();
		var candidate = CreateRoot( Program(), restored, seed: 5, "calls.rse", null );
		restored.RestoreSnapshot( snapshot, new Dictionary<int, RideVM> { [id] = candidate } );
		Assert.AreEqual( 1, candidate.CallDepth );
		Assert.AreEqual( Hash( vm ), Hash( candidate ) );

		for ( var tick = 0; tick < 60; tick++ )
		{
			vm.Advance( Tick );
			candidate.Advance( Tick );
			Assert.AreEqual( Hash( vm ), Hash( candidate ), $"diverged at tick {tick}" );
		}
		CollectionAssert.AreEqual( vm.Variables, candidate.Variables );
		Assert.AreEqual( 0, candidate.CallDepth );
		Assert.AreEqual( 9, candidate.Variables[2], "both returns unwound the stack" );
		Assert.AreEqual( vm.SliceCount, candidate.SliceCount );
		Assert.AreEqual( vm.ExecutedInstructions, candidate.ExecutedInstructions );
	}

	[TestMethod]
	public void FindScriptRandPicksTheSameScriptOfSeveralSameNamedAfterRestore()
	{
		byte[] Named() => new RideVMTests.Asm()
			.I( Opcode.NAME, RideVMTests.S( 0 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "V0" }, "Shared" );
		// Strings: "Shared" at 0, "Nothing" at 7.
		byte[] Finder() => new RideVMTests.Asm()
			.Label( "loop" )
			.I( Opcode.FINDSCRIPTRAND, RideVMTests.S( 0 ), RideVMTests.V( 0 ) )
			.I( Opcode.RAND, RideVMTests.V( 1 ), 9 )
			.I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Build( new[] { "F0", "F1" }, "Shared", "Nothing" );

		var world = new RideScriptWorld();
		var shared = new[] { CreateRoot( Named(), world, seed: 11, "a.rse", null ), CreateRoot( Named(), world, seed: 22, "b.rse", null ), CreateRoot( Named(), world, seed: 33, "c.rse", null ) };
		var finder = CreateRoot( Finder(), world, seed: 7, "f.rse", null );
		AdvanceWorld( world, 25 );
		var snapshot = world.CaptureSnapshot();

		// Candidates in a different construction order, so their temporary ids cannot line up with the saved ones.
		var restored = new RideScriptWorld();
		var candidates = new Dictionary<int, RideVM>();
		foreach ( var (name, seed) in new[] { ("c.rse", 33), ("f.rse", 7), ("a.rse", 11), ("b.rse", 22) } )
		{
			var candidate = CreateRoot( name == "f.rse" ? Finder() : Named(), restored, seed, name, null );
			candidates[world.Scripts.Single( vm => vm.SourceName == name ).ScriptId] = candidate;
		}
		restored.RestoreSnapshot( snapshot, candidates );
		var restoredFinder = candidates[finder.ScriptId];
		CollectionAssert.AreEqual( shared.Select( vm => vm.ScriptId ).OrderBy( id => id ).ToArray(), restored.Scripts.Where( vm => vm.ScriptName == "Shared" ).Select( vm => vm.ScriptId ).OrderBy( id => id ).ToArray() );

		for ( var tick = 0; tick < 40; tick++ )
		{
			finder.Advance( Tick );
			restoredFinder.Advance( Tick );
			Assert.AreEqual( finder.Variables[0], restoredFinder.Variables[0], $"FINDSCRIPTRAND chose differently at tick {tick}" );
			Assert.AreEqual( finder.Variables[1], restoredFinder.Variables[1], $"RAND drew differently at tick {tick}" );
			Assert.AreEqual( Hash( finder ), Hash( restoredFinder ) );
			CollectionAssert.AreEqual( shared.Select( Hash ).ToArray(), restored.Scripts.Where( vm => vm.ScriptName == "Shared" ).OrderBy( vm => vm.ScriptId ).Select( Hash ).ToArray() );
		}
		var chosen = restoredFinder.Variables[0];
		Assert.IsTrue( shared.Any( vm => vm.ScriptId == chosen ), "the pick is one of the same-named scripts" );
	}

	[TestMethod]
	public void AllocatorsKeepTheirHolesAndTheSeedStreamContinuesEqually()
	{
		byte[] Idle() => new RideVMTests.Asm()
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "V0" } );
		var world = new RideScriptWorld();
		var first = CreateRoot( Idle(), world, seed: null, "stream.rse", null );
		AdvanceWorld( world, 3 );
		var demolished = CreateRoot( Idle(), world, seed: null, "gone.rse", null );
		demolished.Stop();
		var snapshot = world.CaptureSnapshot();
		Assert.AreEqual( 1, snapshot.Scripts.Count );
		Assert.AreEqual( 3, snapshot.NextScriptId, "the stopped script's id stays a hole below the frontier" );
		var firstId = first.ScriptId;

		var restored = new RideScriptWorld();
		var candidate = CreateRoot( Idle(), restored, seed: first.Seed, "stream.rse", null );
		restored.RestoreSnapshot( snapshot, new Dictionary<int, RideVM> { [firstId] = candidate } );
		Assert.AreEqual( snapshot.RandomState, restored.RandomState, "the restore drew nothing from the seed stream" );
		Assert.AreEqual( world.NextAttractionId, restored.NextAttractionId );
		Assert.AreEqual( world.World.NextWord(), restored.World.NextWord(), "the world generator continues identically" );
		Assert.AreEqual( world.AllocateAttractionId(), restored.AllocateAttractionId() );

		var nextOriginal = CreateRoot( Idle(), world, seed: null, "next.rse", null );
		var nextRestored = CreateRoot( Idle(), restored, seed: null, "next.rse", null );
		Assert.AreEqual( nextOriginal.ScriptId, nextRestored.ScriptId, "the hole is not reused" );
		Assert.AreEqual( nextOriginal.Seed, nextRestored.Seed );
		Assert.AreEqual( 2, restored.Scripts.Count );
	}

	[TestMethod]
	public void FaultedScriptWithTheNativePcSentinelRoundTrips()
	{
		byte[] Program() => new RideVMTests.Asm()
			.I( Opcode.ADDOBJ, 3, 0xFFFF, 73, 5 )
			.I( Opcode.TEST, RideVMTests.V( 1 ) )
			.I( Opcode.COPY, 0, RideVMTests.V( 0 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "V0", "V1" } );
		var world = new RideScriptWorld();
		var vm = CreateRoot( Program(), world, seed: 9, "fault.rse", null );
		vm.Variables[0] = 123;
		vm.Variables[1] = -5;
		for ( var tick = 0; tick < 2; tick++ )
			vm.Advance( Tick );
		Assert.AreEqual( RideVMState.Faulted, vm.State );
		Assert.AreEqual( -10000, vm.ProgramCounter );
		Assert.AreEqual( 1, vm.UnimplementedEffects[Opcode.ADDOBJ] );
		var snapshot = world.CaptureSnapshot();
		var id = vm.ScriptId;

		var restored = new RideScriptWorld();
		var candidate = CreateRoot( Program(), restored, seed: 9, "fault.rse", null );
		restored.RestoreSnapshot( snapshot, new Dictionary<int, RideVM> { [id] = candidate } );
		Assert.AreEqual( RideVMState.Faulted, candidate.State );
		Assert.AreEqual( -10000, candidate.ProgramCounter );
		Assert.AreEqual( vm.FaultMessage, candidate.FaultMessage );
		CollectionAssert.AreEqual( vm.Variables, candidate.Variables );
		CollectionAssert.AreEqual( vm.UnimplementedEffects.ToDictionary( pair => pair.Key, pair => pair.Value ), candidate.UnimplementedEffects.ToDictionary( pair => pair.Key, pair => pair.Value ) );
		Assert.AreEqual( Hash( vm ), Hash( candidate ) );
		candidate.Advance( Tick );
		Assert.AreEqual( RideVMState.Faulted, candidate.State, "a restored fault stays put" );
	}

	// ---- Deep graphs: child and sound-child chains of any nesting depth ----

	[TestMethod]
	public void ThreeLevelChildAndSoundGraphRestoresAndContinues()
	{
		var grandchild = new RideVMTests.Asm()
			.I( Opcode.NAME, RideVMTests.S( 0 ) )
			.I( Opcode.WAIT, 400 )
			.I( Opcode.COPY, RideVMTests.V( 0 ), 7 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "G0", "G1" }, "Grand" );
		var sound = new RideVMTests.Asm()
			.I( Opcode.NAME, RideVMTests.S( 0 ) )
			.I( Opcode.COPY, RideVMTests.V( 0 ), 44 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "S0" }, "Sound" );
		// Strings: "Child" at 0, "grand.rse" at 6 — the child spawns the grandchild as its SOUND child.
		var child = new RideVMTests.Asm()
			.I( Opcode.NAME, RideVMTests.S( 0 ) )
			.I( Opcode.SPAWNSOUND, RideVMTests.S( 6 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "C0" }, "Child", "grand.rse" );
		// Strings: "Parent" at 0, "child.rse" at 7, "sound.rse" at 17.
		byte[] Parent() => new RideVMTests.Asm()
			.I( Opcode.NAME, RideVMTests.S( 0 ) )
			.I( Opcode.SPAWNSOUND, RideVMTests.S( 17 ) )
			.I( Opcode.SPAWNCHILD, RideVMTests.S( 7 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "P0", "P1", "P2", "P3" }, "Parent", "child.rse", "sound.rse" );
		RideScriptFile? Resolve( string name ) => name switch
		{
			"child.rse" => new RideScriptFile( new MemoryStream( child ) ),
			"grand.rse" => new RideScriptFile( new MemoryStream( grandchild ) ),
			"sound.rse" => new RideScriptFile( new MemoryStream( sound ) ),
			_ => null
		};

		var world = new RideScriptWorld();
		var root = CreateRoot( Parent(), world, seed: 42, "parent.rse", Resolve );
		AdvanceWorld( world, 12 );
		Assert.AreEqual( RideVMState.Waiting, root.Child!.SoundChild!.State, "captured with the grandchild mid-wait" );
		var snapshot = world.CaptureSnapshot();
		var rootId = root.ScriptId;

		var restored = new RideScriptWorld();
		var candidate = CreateRoot( Parent(), restored, seed: 42, "parent.rse", Resolve );
		restored.RestoreSnapshot( snapshot, new Dictionary<int, RideVM> { [rootId] = candidate } );

		Assert.AreEqual( 4, restored.Scripts.Count );
		Assert.AreEqual( root.Child.ScriptId, candidate.Child!.ScriptId );
		Assert.AreEqual( root.SoundChild!.ScriptId, candidate.SoundChild!.ScriptId );
		Assert.AreEqual( root.Child.SoundChild.ScriptId, candidate.Child.SoundChild!.ScriptId, "the grandchild keeps its saved id" );
		Assert.AreEqual( candidate.Child, candidate.Child.SoundChild.Parent, "the grandchild hangs under the restored child" );
		Assert.AreEqual( "Grand", candidate.Child.SoundChild.ScriptName );
		Assert.AreEqual( root.Child.SoundChild.SourceName, candidate.Child.SoundChild.SourceName );
		Assert.AreEqual( Hash( root ), Hash( candidate ) );
		Assert.AreEqual( Hash( root.Child ), Hash( candidate.Child ) );
		Assert.AreEqual( Hash( root.SoundChild ), Hash( candidate.SoundChild ) );
		Assert.AreEqual( Hash( root.Child.SoundChild ), Hash( candidate.Child.SoundChild ) );

		for ( var tick = 0; tick < 80; tick++ )
		{
			root.Advance( Tick );
			candidate.Advance( Tick );
			Assert.AreEqual( Hash( root ), Hash( candidate ), $"root diverged at tick {tick}" );
			Assert.AreEqual( Hash( root.Child ), Hash( candidate.Child ), $"child diverged at tick {tick}" );
			Assert.AreEqual( Hash( root.SoundChild ), Hash( candidate.SoundChild ), $"sound child diverged at tick {tick}" );
			Assert.AreEqual( Hash( root.Child.SoundChild ), Hash( candidate.Child.SoundChild ), $"grandchild diverged at tick {tick}" );
			Assert.AreEqual( world.ClockMilliseconds, restored.ClockMilliseconds );
		}
		Assert.AreEqual( 7, candidate.Child.SoundChild.Variables[0], "the grandchild's wait completed on the restored side too" );
		Assert.AreEqual( 44, candidate.SoundChild.Variables[0] );
	}

	[TestMethod]
	public void DeepRestoreRejectsAGrandchildThatResolvesToDifferentBytes()
	{
		var grandchild = new RideVMTests.Asm()
			.I( Opcode.WAIT, 1000 )
			.I( Opcode.COPY, RideVMTests.V( 0 ), 7 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "G0" } );
		var other = new RideVMTests.Asm()
			.I( Opcode.WAIT, 1000 )
			.I( Opcode.COPY, RideVMTests.V( 0 ), 8 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "G0" } );
		// Strings: "Child" at 0, "grand.rse" at 6.
		var child = new RideVMTests.Asm()
			.I( Opcode.SPAWNCHILD, RideVMTests.S( 6 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "C0" }, "Child", "grand.rse" );
		// Strings: "Parent" at 0, "child.rse" at 7.
		byte[] Parent() => new RideVMTests.Asm()
			.I( Opcode.SPAWNCHILD, RideVMTests.S( 7 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "P0" }, "Parent", "child.rse" );
		RideScriptFile? ResolveOriginal( string name ) => name switch
		{
			"child.rse" => new RideScriptFile( new MemoryStream( child ) ),
			"grand.rse" => new RideScriptFile( new MemoryStream( grandchild ) ),
			_ => null
		};
		RideScriptFile? ResolveOther( string name ) => name switch
		{
			"child.rse" => new RideScriptFile( new MemoryStream( child ) ),
			"grand.rse" => new RideScriptFile( new MemoryStream( other ) ),
			_ => null
		};

		var world = new RideScriptWorld();
		var root = CreateRoot( Parent(), world, seed: 42, "parent.rse", ResolveOriginal );
		AdvanceWorld( world, 3 );
		Assert.AreEqual( RideVMState.Waiting, root.Child!.Child!.State );
		var snapshot = world.CaptureSnapshot();

		var restored = new RideScriptWorld();
		var candidate = CreateRoot( Parent(), restored, seed: 42, "parent.rse", ResolveOther );
		var exception = Assert.ThrowsException<InvalidDataException>( () => restored.RestoreSnapshot( snapshot, new Dictionary<int, RideVM> { [root.ScriptId] = candidate } ) );
		StringAssert.Contains( exception.Message, "does not match the saved script identity" );
		Assert.AreEqual( 1, restored.Scripts.Count, "the failed preflight constructed nothing" );
		candidate.Advance( Tick );
		Assert.AreEqual( RideVMState.Running, candidate.State, "the rejected candidate still runs" );
	}

	[TestMethod]
	public void DeepRestoreRejectsAGrandchildMissingFromTheResolver()
	{
		var grandchild = new RideVMTests.Asm()
			.I( Opcode.WAIT, 1000 )
			.I( Opcode.COPY, RideVMTests.V( 0 ), 7 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "G0" } );
		// Strings: "Child" at 0, "grand.rse" at 6.
		var child = new RideVMTests.Asm()
			.I( Opcode.SPAWNCHILD, RideVMTests.S( 6 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "C0" }, "Child", "grand.rse" );
		// Strings: "Parent" at 0, "child.rse" at 7.
		byte[] Parent() => new RideVMTests.Asm()
			.I( Opcode.SPAWNCHILD, RideVMTests.S( 7 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "P0" }, "Parent", "child.rse" );
		RideScriptFile? Resolve( string name ) => name switch
		{
			"child.rse" => new RideScriptFile( new MemoryStream( child ) ),
			"grand.rse" => new RideScriptFile( new MemoryStream( grandchild ) ),
			_ => null
		};
		RideScriptFile? ResolveWithoutGrandchild( string name ) => name switch
		{
			"child.rse" => new RideScriptFile( new MemoryStream( child ) ),
			_ => null
		};

		var world = new RideScriptWorld();
		var root = CreateRoot( Parent(), world, seed: 42, "parent.rse", Resolve );
		AdvanceWorld( world, 3 );
		var snapshot = world.CaptureSnapshot();

		var restored = new RideScriptWorld();
		var candidate = CreateRoot( Parent(), restored, seed: 42, "parent.rse", ResolveWithoutGrandchild );
		var exception = Assert.ThrowsException<InvalidDataException>( () => restored.RestoreSnapshot( snapshot, new Dictionary<int, RideVM> { [root.ScriptId] = candidate } ) );
		StringAssert.Contains( exception.Message, "was not found by the parent's resolver" );
		Assert.AreEqual( 1, restored.Scripts.Count, "the failed preflight constructed nothing" );
	}

	[TestMethod]
	public void DeepRestoreRejectsATamperedGrandchildSourceName()
	{
		var grandchild = new RideVMTests.Asm()
			.I( Opcode.WAIT, 1000 )
			.I( Opcode.COPY, RideVMTests.V( 0 ), 7 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "G0" } );
		// Strings: "Child" at 0, "grand.rse" at 6.
		var child = new RideVMTests.Asm()
			.I( Opcode.SPAWNCHILD, RideVMTests.S( 6 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "C0" }, "Child", "grand.rse" );
		// Strings: "Parent" at 0, "child.rse" at 7.
		byte[] Parent() => new RideVMTests.Asm()
			.I( Opcode.SPAWNCHILD, RideVMTests.S( 7 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "P0" }, "Parent", "child.rse" );
		RideScriptFile? Resolve( string name ) => name switch
		{
			"child.rse" => new RideScriptFile( new MemoryStream( child ) ),
			"grand.rse" => new RideScriptFile( new MemoryStream( grandchild ) ),
			_ => null
		};

		var world = new RideScriptWorld();
		var root = CreateRoot( Parent(), world, seed: 42, "parent.rse", Resolve );
		AdvanceWorld( world, 3 );
		var snapshot = world.CaptureSnapshot();
		var grandId = root.Child!.Child!.ScriptId;
		var tampered = snapshot with
		{
			Scripts = snapshot.Scripts.Select( script => script.ScriptId == grandId ? script with { SourceName = "parent.rse > child.rse > elsewhere.rse" } : script ).ToArray()
		};

		var restored = new RideScriptWorld();
		var candidate = CreateRoot( Parent(), restored, seed: 42, "parent.rse", Resolve );
		var exception = Assert.ThrowsException<InvalidDataException>( () => restored.RestoreSnapshot( tampered, new Dictionary<int, RideVM> { [root.ScriptId] = candidate } ) );
		StringAssert.Contains( exception.Message, "constructed source" );
	}

	// ---- Rejections ----

	[TestMethod]
	public void RestoreRejectsACandidateWhoseScriptIdentityDiffers()
	{
		byte[] One() => new RideVMTests.Asm().I( Opcode.COPY, RideVMTests.V( 0 ), 1 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( new[] { "V0" } );
		byte[] Other() => new RideVMTests.Asm().I( Opcode.COPY, RideVMTests.V( 0 ), 2 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( new[] { "V0" } );
		var world = new RideScriptWorld();
		var vm = CreateRoot( One(), world, seed: 1, "one.rse", null );
		AdvanceWorld( world, 2 );
		var snapshot = world.CaptureSnapshot();

		var restored = new RideScriptWorld();
		var candidate = CreateRoot( Other(), restored, seed: 1, "one.rse", null );
		var exception = Assert.ThrowsException<InvalidDataException>( () => restored.RestoreSnapshot( snapshot, new Dictionary<int, RideVM> { [vm.ScriptId] = candidate } ) );
		StringAssert.Contains( exception.Message, "script identity" );
		Assert.AreEqual( 1, restored.Scripts.Count, "the candidate keeps its own registration" );
		candidate.Advance( Tick );
		Assert.AreEqual( RideVMState.Running, candidate.State, "the rejected candidate still runs" );
	}

	[TestMethod]
	public void RestoreRejectsMissingAndUnknownRootCandidates()
	{
		byte[] Idle() => new RideVMTests.Asm().Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( new[] { "V0" } );
		var world = new RideScriptWorld();
		var first = CreateRoot( Idle(), world, seed: 1, "a.rse", null );
		var second = CreateRoot( Idle(), world, seed: 2, "b.rse", null );
		AdvanceWorld( world, 2 );
		var snapshot = world.CaptureSnapshot();

		var partial = new RideScriptWorld();
		var onlyFirst = CreateRoot( Idle(), partial, seed: 1, "a.rse", null );
		var missing = Assert.ThrowsException<InvalidDataException>( () => partial.RestoreSnapshot( snapshot, new Dictionary<int, RideVM> { [first.ScriptId] = onlyFirst } ) );
		StringAssert.Contains( missing.Message, "no candidate root script" );

		var extra = new RideScriptWorld();
		var eFirst = CreateRoot( Idle(), extra, seed: 1, "a.rse", null );
		var eSecond = CreateRoot( Idle(), extra, seed: 2, "b.rse", null );
		var eThird = CreateRoot( Idle(), extra, seed: 3, "c.rse", null );
		var unknown = Assert.ThrowsException<InvalidDataException>( () => extra.RestoreSnapshot( snapshot, new Dictionary<int, RideVM> { [first.ScriptId] = eFirst, [second.ScriptId] = eSecond, [eThird.ScriptId] = eThird } ) );
		StringAssert.Contains( unknown.Message, "are not snapshot roots" );
	}

	// ---- Pure validation of corrupt snapshots ----

	[TestMethod]
	public void CorruptWorldSnapshotsFailValidationBeforeAnyWorldIsTouched()
	{
		byte[] Idle() => new RideVMTests.Asm().Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( new[] { "V0", "V1" } );
		var world = new RideScriptWorld();
		var vm = CreateRoot( Idle(), world, seed: 1, "idle.rse", null );
		AdvanceWorld( world, 2 );
		var snapshot = world.CaptureSnapshot();
		snapshot.ValidateSnapshot();

		var script = snapshot.Scripts.Single();
		static InvalidDataException Throws( RideScriptWorldSnapshot corrupt ) => Assert.ThrowsException<InvalidDataException>( corrupt.ValidateSnapshot );
		StringAssert.Contains( Throws( snapshot with { Scripts = new[] { script, script with { } } } ).Message, "appears twice" );
		StringAssert.Contains( Throws( snapshot with { NextScriptId = script.ScriptId } ).Message, "allocator frontier" );
		StringAssert.Contains( Throws( snapshot with { ClockMilliseconds = -1 } ).Message, "not finite and non-negative" );
		StringAssert.Contains( Throws( snapshot with { Scripts = new[] { script with { ParentScriptId = 99, ScriptFileName = "x.rse" } } } ).Message, "does not contain" );
		var cycleFirst = script with { ChildScriptId = 2 };
		var cycleSecond = script with { ScriptId = 2, ParentScriptId = 1, ScriptFileName = "x.rse", ChildScriptId = 1 };
		StringAssert.Contains( Throws( snapshot with { NextScriptId = 3, Scripts = new[] { cycleFirst, cycleSecond } } ).Message, "cycle" );
		var childEqualsSound = script with { ChildScriptId = 2, SoundChildScriptId = 2 };
		StringAssert.Contains( Throws( snapshot with { NextScriptId = 3, Scripts = new[] { childEqualsSound, cycleSecond with { ChildScriptId = 0 } } } ).Message, "same child and sound child" );
	}

	[TestMethod]
	public void CorruptDeepGraphsFailBothWayLinkValidationBeforeAnyRestore()
	{
		var grandchild = new RideVMTests.Asm()
			.I( Opcode.WAIT, 1000 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "G0" } );
		// Strings: "Child" at 0, "grand.rse" at 6.
		var child = new RideVMTests.Asm()
			.I( Opcode.SPAWNCHILD, RideVMTests.S( 6 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "C0" }, "Child", "grand.rse" );
		// Strings: "Parent" at 0, "child.rse" at 7.
		byte[] Parent() => new RideVMTests.Asm()
			.I( Opcode.SPAWNCHILD, RideVMTests.S( 7 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "P0" }, "Parent", "child.rse" );
		var kid = new RideVMTests.Asm()
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "K0" } );
		// Strings: "Root4" at 0, "kid.rse" at 6.
		byte[] SecondRoot() => new RideVMTests.Asm()
			.I( Opcode.SPAWNCHILD, RideVMTests.S( 6 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "R0" }, "Root4", "kid.rse" );
		RideScriptFile? Resolve( string name ) => name switch
		{
			"child.rse" => new RideScriptFile( new MemoryStream( child ) ),
			"grand.rse" => new RideScriptFile( new MemoryStream( grandchild ) ),
			"kid.rse" => new RideScriptFile( new MemoryStream( kid ) ),
			_ => null
		};

		var world = new RideScriptWorld();
		var root = CreateRoot( Parent(), world, seed: 42, "parent.rse", Resolve );
		CreateRoot( SecondRoot(), world, seed: 43, "second.rse", Resolve );
		AdvanceWorld( world, 3 );
		var snapshot = world.CaptureSnapshot();
		snapshot.ValidateSnapshot();
		var childId = root.Child!.ScriptId;
		var grandId = root.Child.Child.ScriptId;
		var otherRootId = world.Scripts.Single( vm => vm.SourceName == "second.rse" ).ScriptId;
		var otherKidId = world.Scripts.Single( vm => vm.SourceName == "second.rse > kid.rse" ).ScriptId;

		// A grandchild re-parented to the root level is no longer the child's child: the child's link
		// now aims at a script that does not name it as parent.
		var adopted = snapshot with { Scripts = snapshot.Scripts.Select( script => script.ScriptId == grandId ? script with { ParentScriptId = 0, ScriptFileName = null } : script ).ToArray() };
		// A child link aimed at another script's child.
		var crossLinked = snapshot with { Scripts = snapshot.Scripts.Select( script => script.ScriptId == grandId ? script with { ChildScriptId = otherKidId } : script ).ToArray() };
		static InvalidDataException Throws( RideScriptWorldSnapshot corrupt ) => Assert.ThrowsException<InvalidDataException>( corrupt.ValidateSnapshot );
		StringAssert.Contains( Throws( adopted ).Message, $"links child {grandId}, whose parent is 0" );
		StringAssert.Contains( Throws( crossLinked ).Message, $"whose parent is {otherRootId}" );

		// The restore refuses the same corruption during validation, before any candidate is examined.
		var restored = new RideScriptWorld();
		var candidate = CreateRoot( Parent(), restored, seed: 42, "parent.rse", Resolve );
		var exception = Assert.ThrowsException<InvalidDataException>( () => restored.RestoreSnapshot( adopted, new Dictionary<int, RideVM> { [root.ScriptId] = candidate } ) );
		StringAssert.Contains( exception.Message, "whose parent is" );
	}

	[TestMethod]
	public void NullArraysFailValidationInsteadOfThrowingReferenceExceptions()
	{
		byte[] Idle() => new RideVMTests.Asm().Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( new[] { "V0", "V1" } );
		var world = new RideScriptWorld();
		CreateRoot( Idle(), world, seed: 1, "idle.rse", null );
		AdvanceWorld( world, 2 );
		var snapshot = world.CaptureSnapshot();
		var script = snapshot.Scripts.Single();

		static InvalidDataException Throws( RideScriptWorldSnapshot corrupt ) => Assert.ThrowsException<InvalidDataException>( corrupt.ValidateSnapshot );
		StringAssert.Contains( Throws( snapshot with { Scripts = null! } ).Message, "no script array" );
		StringAssert.Contains( Throws( snapshot with { Scripts = new RideVMSnapshot[] { null! } } ).Message, "entry 0 is null" );
		static InvalidDataException ThrowsScript( RideVMSnapshot corrupt ) => Assert.ThrowsException<InvalidDataException>( corrupt.ValidateSnapshot );
		StringAssert.Contains( ThrowsScript( script with { CallStack = null! } ).Message, "null array" );
		StringAssert.Contains( ThrowsScript( script with { Variables = null! } ).Message, "null array" );
		StringAssert.Contains( ThrowsScript( script with { RandomState = null! } ).Message, "null array" );
		StringAssert.Contains( ThrowsScript( script with { UnimplementedEffects = null! } ).Message, "null array" );
		StringAssert.Contains( ThrowsScript( script with { RandomState = script.RandomState with { Table = null! } } ).Message, "no table" );
	}

	[TestMethod]
	public void CorruptScriptSnapshotsFailValidation()
	{
		byte[] Idle() => new RideVMTests.Asm().Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( new[] { "V0", "V1" } );
		var world = new RideScriptWorld();
		var vm = CreateRoot( Idle(), world, seed: 1, "idle.rse", null );
		AdvanceWorld( world, 2 );
		var script = world.CaptureSnapshot().Scripts.Single();
		script.ValidateSnapshot();

		static InvalidDataException Throws( RideVMSnapshot corrupt ) => Assert.ThrowsException<InvalidDataException>( corrupt.ValidateSnapshot );
		StringAssert.Contains( Throws( script with { ScriptId = 0 } ).Message, "not positive" );
		StringAssert.Contains( Throws( script with { ProgramCounter = -10001 } ).Message, "outside" );
		StringAssert.Contains( Throws( script with { ProgramCounter = -10000 } ).Message, "faulted-only" );
		StringAssert.Contains( Throws( script with { Flags = (RideVM.VMFlags)4 } ).Message, "besides Sign and Zero" );
		StringAssert.Contains( Throws( script with { SliceBudget = 0 } ).Message, "not positive" );
		StringAssert.Contains( Throws( script with { TimeMilliseconds = double.NaN } ).Message, "not finite" );
		StringAssert.Contains( Throws( script with { CallStack = new[] { -1 } } ).Message, "call-stack address" );
		StringAssert.Contains( Throws( script with { RandomState = script.RandomState with { Table = new int[10] } } ).Message, "table words" );
		StringAssert.Contains( Throws( script with { UnimplementedEffects = new Dictionary<Opcode, int> { [Opcode.ADDOBJ] = 0 } } ).Message, "has count 0" );
		StringAssert.Contains( Throws( script with { FaultMessage = "x" } ).Message, "fault message" );
		StringAssert.Contains( Throws( script with { ScriptFileName = "child.rse" } ).Message, "a root has no file name" );
	}
}
