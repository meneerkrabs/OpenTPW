using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// The original's substep scheduler around the park turn (tp.exe main loop 0x0054F4BF, DET-013): every 31 ms substep ticks the bump module, runs the script pass (0x5516B0: newest script first, one slice per
/// eight passes by script id unless the script's TURBO byte is set), ticks the tour cars on even substeps and every eighth substep is the park turn.
/// </summary>
[TestClass]
public class SubstepSchedulerTests
{
	private static RideVM Counter( RideScriptWorld world, bool turbo = false )
	{
		var asm = new Asm();
		if ( turbo )
			asm.I( Opcode.TURBO, 1 );
		asm.Label( "loop" ).I( Opcode.ADD, V( 0 ), 1 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" );
		return new RideVM( new RideScriptFile( new MemoryStream( asm.Build( new[] { "V0", "V1" } ) ) ), new RideVMOptions { World = world, Seed = 1 } );
	}

	[TestMethod]
	public void TheSubstepCounterIsTheParkTickInThirtyOneMillisecondSteps()
	{
		var substeps = 0;
		var turns = 0;
		for ( long tick = 0; tick < 14880 * 3; tick++ )
		{
			var step = SimulationSubstep.ForTick( tick, running: true );
			Assert.AreEqual( (tick + 1) * 1000 / 1860, step.Pass );
			if ( !step.Due )
				continue;
			substeps++;
			Assert.AreEqual( 1860 * step.Pass <= step.ClockUnits && step.ClockUnits - 1860 * step.Pass < 1000, true, "a substep ends within the tick that crosses its time" );
			// every eighth substep is a park turn: the turn counter of the calendar changes in exactly that tick
			var turn = ParkCalendar.Turn( tick + 1 ) != ParkCalendar.Turn( tick );
			Assert.AreEqual( step.Pass % 8 == 0, turn, $"tick {tick}" );
			if ( turn )
				turns++;
			Assert.AreEqual( step.Pass % 2 == 0, step.TourDue );
		}
		Assert.AreEqual( 14880 * 3 * 1000 / 1860, substeps );
		Assert.AreEqual( 3000, turns, "14880 ticks are 248 s: a thousand turns of 248 ms" );
	}

	[TestMethod]
	public void APausedParkRunsNoSubstepAndKeepsItsCounter()
	{
		var paused = SimulationSubstep.ForTick( 500, running: false );
		Assert.IsFalse( paused.Running );
		Assert.IsFalse( paused.Due );
		Assert.IsFalse( paused.TourDue );
		Assert.AreEqual( 500 * 1000 / 1860, paused.Pass );
	}

	[TestMethod]
	public void AScriptRunsOneSliceInEightPassesOnThePassItsIdSelects()
	{
		var world = new RideScriptWorld( 7 );
		var scripts = Enumerable.Range( 1, 8 ).Select( _ => Counter( world ) ).ToArray();
		CollectionAssert.AreEqual( Enumerable.Range( 1, 8 ).ToArray(), scripts.Select( script => script.ScriptId ).ToArray(), "ids follow creation order from 1 (0x558C0D)" );
		for ( var pass = 1; pass <= 16; pass++ )
		{
			var before = scripts.Select( script => script.Variables[0] ).ToArray();
			world.RunPass( SimulationSubstep.Milliseconds );
			Assert.AreEqual( pass, world.PassCounter );
			for ( var index = 0; index < scripts.Length; index++ )
			{
				var ran = scripts[index].Variables[0] != before[index];
				// ((id xor pass) & 7) == 0: the script with id i runs on the passes congruent to i modulo 8 (id 8 on pass 8 and 16)
				Assert.AreEqual( (scripts[index].ScriptId & 7) == (pass & 7), ran, $"script {scripts[index].ScriptId} on pass {pass}" );
			}
		}
		Assert.IsTrue( scripts.All( script => script.Variables[0] == 2 ), "two slices in sixteen passes" );
		Assert.IsTrue( scripts.All( script => script.TimeMilliseconds == 16 * 31 ), "the script clock gains a substep every pass, slice or not" );
	}

	private sealed class SliceTail : IRideScriptEffects, IRideSliceTail
	{
		public readonly List<(int Script, long Pass, int Bias)> Calls = new();
		public int Perform( RideEffectCall call ) => 0;
		public void AfterSlice( RideVM vm ) => Calls.Add( (vm.ScriptId, vm.World.PassCounter, vm.SpeedBias) );
	}

	/// <summary>
	/// The loop body's tail (0x5517D7: the speed word to the coaster in the script's slot, 0x43B2B0) runs after every slice the cadence selects, whether or not the script had
	/// instructions left to run: a halted script reaches it too.
	/// </summary>
	[TestMethod]
	public void TheSliceTailRunsOnThePassesTheCadenceSelects()
	{
		var world = new RideScriptWorld( 7 );
		var effects = new SliceTail();
		var halted = new Asm().I( Opcode.ENDSLICE );
		var vm = new RideVM( new RideScriptFile( new MemoryStream( halted.Build( new[] { "V0" } ) ) ), new RideVMOptions { World = world, Seed = 1, Effects = effects } );
		for ( var pass = 1; pass <= 24; pass++ )
			world.RunPass( SimulationSubstep.Milliseconds );
		CollectionAssert.AreEqual( new long[] { 1, 9, 17 }, effects.Calls.Select( call => call.Pass ).ToArray(), "script 1 takes its slice and the tail on passes 1, 9 and 17" );
		Assert.IsTrue( effects.Calls.All( call => call.Script == vm.ScriptId && call.Bias == RideVM.DefaultSpeedBias ) );
	}

	[TestMethod]
	public void ATurboScriptRunsInEveryPass()
	{
		var world = new RideScriptWorld( 7 );
		var plain = Counter( world );
		var turbo = Counter( world, turbo: true );
		for ( var pass = 1; pass <= 24; pass++ )
			world.RunPass( SimulationSubstep.Milliseconds );
		Assert.AreEqual( 1, turbo.TurboByte );
		Assert.AreEqual( 3, plain.Variables[0], "one slice in eight passes" );
		Assert.AreEqual( 23, turbo.Variables[0], "the TURBO opcode runs in the first pass, which then runs the script every pass: 23 slices counted after it stored its byte" );
	}

	[TestMethod]
	public void TheScriptPassRunsNewestScriptFirst()
	{
		// ids 1 and 9 share the pass phase (both are 1 modulo 8): the newer script draws first
		var world = new RideScriptWorld( 7 );
		var draw = new Asm().I( Opcode.RAND, V( 0 ), 1000 ).I( Opcode.ENDSLICE );
		RideVM Draw() => new( new RideScriptFile( new MemoryStream( draw.Build( new[] { "V0", "V1" } ) ) ), new RideVMOptions { World = world } );
		var first = Draw();
		for ( var filler = 0; filler < 7; filler++ )
			Counter( world );
		var ninth = Draw();
		Assert.AreEqual( 1, first.ScriptId );
		Assert.AreEqual( 9, ninth.ScriptId );
		var reference = new RideScriptWorld( 7 );
		var expectedFirstDraw = reference.ScriptRand( 1000 );
		var expectedSecondDraw = reference.ScriptRand( 1000 );
		world.RunPass( SimulationSubstep.Milliseconds );
		Assert.AreEqual( expectedFirstDraw, ninth.Variables[0], "the newest script takes the first draw of the pass" );
		Assert.AreEqual( expectedSecondDraw, first.Variables[0] );
	}

	[TestMethod]
	public void TheScriptPassCounterIsSavedAndAnOlderSnapshotRestartsItAtZero()
	{
		var world = new RideScriptWorld( 7 );
		for ( var pass = 0; pass < 11; pass++ )
			world.RunPass( SimulationSubstep.Milliseconds );
		var snapshot = world.CaptureSnapshot();
		Assert.AreEqual( 11, snapshot.PassCounter );
		var json = JsonSerializer.Serialize( snapshot );
		var loaded = JsonSerializer.Deserialize<RideScriptWorldSnapshot>( json )!;
		var resumed = new RideScriptWorld( 7 );
		resumed.RestoreSnapshot( loaded, new Dictionary<int, RideVM>() );
		Assert.AreEqual( 11, resumed.PassCounter );
		var older = new RideScriptWorld( 7 );
		older.RunPass( 31 );
		older.RestoreSnapshot( snapshot with { PassCounter = null }, new Dictionary<int, RideVM>() );
		Assert.AreEqual( 0, older.PassCounter, "a snapshot from before the substep scheduler holds no counter" );
		Assert.ThrowsException<InvalidDataException>( () => new RideScriptWorld( 7 ).RestoreSnapshot( snapshot with { PassCounter = -1 }, new Dictionary<int, RideVM>() ) );
	}

	/// <summary>A level with only the pieces the simulation tick touches (as ThingSweepTests builds it).</summary>
	private static (Level Level, ParkEconomy Economy, GuestSimulation Guests) CreateLevel()
	{
		Log ??= new();
		var economy = EconomyTestData.Park();
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 1 );
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		guests.AttachThings( pool );
		economy.Staff.AttachThings( pool );
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		void Set( string field, object value ) => typeof( Level ).GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, value );
		Set( "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) );
		Set( "<Guests>k__BackingField", guests );
		Set( "<Objects>k__BackingField", new ParkObjects( null!, new SandboxParkGrid() ) );
		Set( "<Seed>k__BackingField", WorldSeed.Default );
		Set( "economyInstances", new Dictionary<OriginalObject, int>() );
		Set( "simulationClock", new FixedStepClock() );
		Set( "things", pool );
		return (level, economy, guests);
	}

	[TestMethod]
	public void TheLevelRunsAScriptOnItsOwnCadenceAndNotOnEveryTick()
	{
		var (level, economy, _) = CreateLevel();
		var script = Counter( level.Objects.ScriptWorld );
		Assert.AreEqual( 1, script.ScriptId );
		const int ticks = 1000;
		for ( var tick = 0; tick < ticks; tick++ )
			level.AdvanceSimulation( FixedStepClock.TickDuration );
		var passes = ticks * 1000 / 1860;
		Assert.AreEqual( passes, level.Objects.ScriptWorld.PassCounter );
		// passes 1, 9, 17, ... of the id-1 script
		Assert.AreEqual( (passes - 1) / 8 + 1, script.Variables[0] );
		Assert.AreEqual( passes * 31.0, script.TimeMilliseconds, "the script clock is the substeps" );
		Assert.AreEqual( economy.Tick, ticks );
	}

	[TestMethod]
	public void APausedParkRunsNoScriptPass()
	{
		var (level, economy, _) = CreateLevel();
		var script = Counter( level.Objects.ScriptWorld );
		for ( var tick = 0; tick < 200; tick++ )
			level.AdvanceSimulation( FixedStepClock.TickDuration );
		var passes = level.Objects.ScriptWorld.PassCounter;
		var clock = script.TimeMilliseconds;
		economy.Speed = GameSpeed.Paused;
		for ( var tick = 0; tick < 200; tick++ )
			level.AdvanceSimulation( FixedStepClock.TickDuration );
		Assert.AreEqual( passes, level.Objects.ScriptWorld.PassCounter, "the substep body does not run while the park is paused (0x54F4D4)" );
		Assert.AreEqual( clock, script.TimeMilliseconds );
		economy.Speed = GameSpeed.Normal;
		for ( var tick = 0; tick < 200; tick++ )
			level.AdvanceSimulation( FixedStepClock.TickDuration );
		Assert.AreEqual( 400 * 1000 / 1860, level.Objects.ScriptWorld.PassCounter, "the counter goes on from the park's own tick" );
	}
}
