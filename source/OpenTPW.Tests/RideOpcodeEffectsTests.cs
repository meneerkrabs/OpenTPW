using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// Asset-free unit tests for the hooked effect opcodes that <see cref="RideOpcodeTestMap"/> names for the
/// vm.opcodes row (docs/M4-GATE.md): objects, channel animations, visitor heads/limbo/bounce/walk, screams,
/// atmosphere and the park clock. Every test runs a synthetic script on a <see cref="RideVM"/> with a recording
/// effects double and asserts the observable result: the effect calls with their resolved arguments, destination
/// writes, flags and wait behaviour. Semantics come from docs/RSE-VM.md; where upstream is unknown the tests
/// pin the current reviewed behaviour.
/// </summary>
[TestClass]
public class RideOpcodeEffectsTests
{
	private const double Tick = 1d / 60;

	private static readonly string[] Vars = { "V0", "V1", "V2", "V3" };

	/// <summary>Records every effect call (opcode plus resolved operand values) and returns scripted results.</summary>
	private sealed class RecordingEffects : IRideScriptEffects
	{
		public readonly List<string> Calls = new();
		public Func<RideEffectCall, int> Result = _ => 0;

		public int Perform( RideEffectCall call )
		{
			Calls.Add( call.ToString() );
			return Result( call );
		}
	}

	private static RideVM Load( Asm asm, IRideScriptEffects effects )
		=> new( new RideScriptFile( new MemoryStream( asm.Build( Vars ) ) ), new RideVMOptions { Seed = 1, Effects = effects } );

	private static void Run( RideVM vm, int ticks )
	{
		for ( var tick = 0; tick < ticks; ++tick )
			vm.Advance( Tick );
	}

	[TestMethod]
	public void ObjectOpcodesPassSlotsAndParametersAndPreserveFlags()
	{
		var effects = new RecordingEffects();
		var vm = Load( new Asm()
			.I( Opcode.TEST, V( 1 ) )
			.I( Opcode.KILLOBJ, 200 )
			.I( Opcode.BRANCH_NV, "negative" )
			.I( Opcode.COPY, V( 2 ), 0 ).I( Opcode.BRANCH, "objects" )
			.Label( "negative" ).I( Opcode.COPY, V( 2 ), 1 )
			.Label( "objects" )
			.I( Opcode.FADEOBJ, V( 0 ) )
			.I( Opcode.SETOBJPARAM, V( 0 ), 3, 45 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		vm.Variables[0] = 33;
		vm.Variables[1] = -5;
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[2], "KILLOBJ leaves the flags alone (`ADD VAR_COUNT 0xFFFF; KILLOBJ 200; BRANCH_PV`)" );
		CollectionAssert.AreEqual( new[] { "KILLOBJ 200", "FADEOBJ 33", "SETOBJPARAM 33 3 45" }, effects.Calls );
		Assert.AreEqual( RideVMState.Running, vm.State );
		Assert.AreEqual( 0, vm.UnimplementedEffects.Count );
	}

	[TestMethod]
	public void LoopAndChannelAnimationOpcodesPassThroughAndQuery()
	{
		var effects = new RecordingEffects { Result = call => call.Opcode switch { Opcode.LOOPANIM => 4000, Opcode.GETANIM_CH => 7, _ => 0 } };
		var vm = Load( new Asm()
			.I( Opcode.LOOPANIM, 5, 2 )
			.I( Opcode.TRIGANIM_CH, 6, 1, 0, 4 )
			.I( Opcode.LOOPANIM_CH, 2, 3, 4 )
			.I( Opcode.GETANIM_CH, V( 0 ), 3 )
			.I( Opcode.BRANCH_PV, "playing" )
			.I( Opcode.COPY, V( 1 ), 0 ).I( Opcode.BRANCH, "wait" )
			.Label( "playing" ).I( Opcode.COPY, V( 1 ), 1 )
			.Label( "wait" ).I( Opcode.WAIT4ANIM )
			.I( Opcode.COPY, V( 2 ), 1 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		Run( vm, 1 );
		Assert.AreEqual( 7, vm.Variables[0], "GETANIM_CH writes its result to the destination" );
		Assert.AreEqual( 1, vm.Variables[1], "GETANIM_CH sets the flags from its result" );
		CollectionAssert.AreEqual( new[] { "LOOPANIM 5 2", "TRIGANIM_CH 6 1 0 4", "LOOPANIM_CH 2 3 4", "GETANIM_CH 0 3" }, effects.Calls );
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[2], "LOOPANIM reports a duration but loops are not tracked for WAIT4ANIM (docs assumption)" );
		Assert.AreEqual( RideVMState.Running, vm.State );
	}

	[TestMethod]
	public void TrigWaitAnimWaitsItsDurationAndTrigAnimSpeedFeedsWait4Anim()
	{
		var effects = new RecordingEffects { Result = call => call.Opcode switch { Opcode.TRIGWAITANIM => 600, Opcode.TRIGANIMSPEED => 3000, _ => 0 } };
		var vm = Load( new Asm()
			.I( Opcode.TRIGANIMSPEED, 6, 0, V( 0 ), 4000 )
			.I( Opcode.TRIGWAITANIM, 4, 2, V( 1 ) )
			.I( Opcode.COPY, V( 2 ), 1 )
			.I( Opcode.WAIT4ANIM )
			.I( Opcode.COPY, V( 2 ), 2 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		Run( vm, 1 );
		Assert.AreEqual( 600, vm.Variables[1], "TRIGWAITANIM writes the duration to its third operand" );
		Assert.AreEqual( 0, vm.Variables[0], "TRIGANIMSPEED passes its operands through and writes no destination (current behaviour)" );
		Assert.AreEqual( RideVMState.Waiting, vm.State );
		var ticks = 1;
		while ( vm.Variables[2] == 0 && ticks < 80 )
		{
			vm.Advance( Tick );
			++ticks;
		}
		Assert.AreEqual( 1, vm.Variables[2], "TRIGWAITANIM waits for its own duration" );
		Assert.IsTrue( ticks is >= 34 and <= 40, $"woke after {ticks} ticks, expected ~36 for 600 ms" );
		Assert.AreEqual( RideVMState.Waiting, vm.State, "WAIT4ANIM then waits for the tracked TRIGANIMSPEED end time" );
		while ( vm.Variables[2] == 1 && ticks < 300 )
		{
			vm.Advance( Tick );
			++ticks;
		}
		Assert.AreEqual( 2, vm.Variables[2], "WAIT4ANIM waited for the 3000 ms the effects layer reported for TRIGANIMSPEED" );
		Assert.IsTrue( ticks is >= 178 and <= 184, $"WAIT4ANIM released after {ticks} ticks, expected ~181" );
		CollectionAssert.AreEqual( new[] { "TRIGANIMSPEED 6 0 0 4000", "TRIGWAITANIM 4 2 0" }, effects.Calls );
	}

	[TestMethod]
	public void HushHopAndHeadOpcodesPassTheirVisitorOperand()
	{
		var effects = new RecordingEffects { Result = call => call.Opcode switch { Opcode.HOP => 5, _ => 0 } };
		var vm = Load( new Asm()
			.I( Opcode.TEST, V( 2 ) )
			.I( Opcode.HUSH, V( 0 ) )
			.I( Opcode.HOP, V( 1 ) )
			.I( Opcode.BRANCH_NV, "kept" )
			.I( Opcode.COPY, V( 3 ), 0 ).I( Opcode.BRANCH, "heads" )
			.Label( "kept" ).I( Opcode.COPY, V( 3 ), 1 )
			.Label( "heads" )
			.I( Opcode.ADDHEAD, V( 0 ) )
			.I( Opcode.DELHEAD, V( 0 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		vm.Variables[0] = 44;
		vm.Variables[2] = -1;
		Run( vm, 1 );
		Assert.AreEqual( 5, vm.Variables[1], "HOP writes its result to the destination" );
		Assert.AreEqual( 1, vm.Variables[3], "HOP leaves the flags alone (the corpus never branches on it)" );
		CollectionAssert.AreEqual( new[] { "HUSH 44", "HOP 0", "ADDHEAD 44", "DELHEAD 44" }, effects.Calls );
		Assert.AreEqual( RideVMState.Running, vm.State );
	}

	[TestMethod]
	public void ForceUnlimboAndInLimboWriteTheirDestinationAndSetFlags()
	{
		var effects = new RecordingEffects { Result = call => call.Opcode switch { Opcode.FORCEUNLIMBO => 1, Opcode.INLIMBO => 0, _ => 0 } };
		var vm = Load( new Asm()
			.I( Opcode.TEST, V( 2 ) )
			.I( Opcode.FORCEUNLIMBO, V( 0 ) )
			.I( Opcode.BRANCH_NZ, "released" )
			.I( Opcode.COPY, V( 1 ), 0 ).I( Opcode.BRANCH, "waiting" )
			.Label( "released" ).I( Opcode.COPY, V( 1 ), 1 )
			.Label( "waiting" ).I( Opcode.INLIMBO, 0 )
			.I( Opcode.BRANCH_Z, "empty" )
			.I( Opcode.COPY, V( 1 ), 2 ).I( Opcode.BRANCH, "idle" )
			.Label( "empty" ).I( Opcode.COPY, V( 1 ), 3 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		vm.Variables[2] = 9;
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[0], "FORCEUNLIMBO writes its released visitor to the destination" );
		Assert.AreEqual( 3, vm.Variables[1], "INLIMBO set the zero flag from its result" );
		// Words: TEST 0-1, FORCEUNLIMBO 2-3, BRANCH_NZ 4-5, COPY 6-8, BRANCH 9-10, COPY 11-13, INLIMBO 14.
		Assert.AreEqual( Opcode.INLIMBO, vm.Instructions[6].Opcode );
		Assert.AreEqual( 0, vm.Instructions[6].Operands[0].Value, "INLIMBO with a literal destination discards the write (corpus: `INLIMBO 0; BRANCH_Z`)" );
		CollectionAssert.AreEqual( new[] { "FORCEUNLIMBO 0", "INLIMBO 0" }, effects.Calls );
		Assert.AreEqual( RideVMState.Running, vm.State );
	}

	[TestMethod]
	public void BounceOpcodesPassTheirOperandsAndBouncingQueries()
	{
		var effects = new RecordingEffects { Result = call => call.Opcode switch { Opcode.BOUNCING => -3, _ => 0 } };
		var vm = Load( new Asm()
			.I( Opcode.BOUNCESETNODE, 12 )
			.I( Opcode.BOUNCESETBASE, V( 0 ) )
			.I( Opcode.BOUNCE, V( 0 ), 7 )
			.I( Opcode.UNBOUNCE, V( 0 ) )
			.I( Opcode.FORCEUNBOUNCE, V( 0 ) )
			.I( Opcode.BOUNCING, V( 1 ) )
			.I( Opcode.BRANCH_NV, "sign" )
			.I( Opcode.COPY, V( 2 ), 0 ).I( Opcode.BRANCH, "idle" )
			.Label( "sign" ).I( Opcode.COPY, V( 2 ), 1 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		vm.Variables[0] = 44;
		Run( vm, 1 );
		Assert.AreEqual( -3, vm.Variables[1], "BOUNCING writes its result to the destination" );
		Assert.AreEqual( 1, vm.Variables[2], "BOUNCING sets the flags from its result" );
		CollectionAssert.AreEqual( new[] { "BOUNCESETNODE 12", "BOUNCESETBASE 44", "BOUNCE 44 7", "UNBOUNCE 44", "FORCEUNBOUNCE 44", "BOUNCING 0" }, effects.Calls );
		Assert.AreEqual( RideVMState.Running, vm.State );
	}

	[TestMethod]
	public void WalkOnAndFloatOpcodesPassOperandsAndFloatStatQueries()
	{
		var effects = new RecordingEffects { Result = call => call.Opcode switch { Opcode.WALKFLOATSTAT => 4, _ => 0 } };
		var vm = Load( new Asm()
			.I( Opcode.WALKON, V( 0 ), 1, 2, 3, 4, 5, 6 )
			.I( Opcode.WALKST_FLOAT, 9, 8, 7 )
			.I( Opcode.WALKFLOATSTOP )
			.I( Opcode.WALKFLOATSTAT, V( 1 ) )
			.I( Opcode.BRANCH_PV, "floating" )
			.I( Opcode.COPY, V( 2 ), 0 ).I( Opcode.BRANCH, "idle" )
			.Label( "floating" ).I( Opcode.COPY, V( 2 ), 1 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		vm.Variables[0] = 44;
		Run( vm, 1 );
		Assert.AreEqual( 4, vm.Variables[1], "WALKFLOATSTAT writes its result to the destination" );
		Assert.AreEqual( 1, vm.Variables[2], "WALKFLOATSTAT sets the flags from its result" );
		CollectionAssert.AreEqual( new[] { "WALKON 44 1 2 3 4 5 6", "WALKST_FLOAT 9 8 7", "WALKFLOATSTOP", "WALKFLOATSTAT 0" }, effects.Calls );
		Assert.AreEqual( RideVMState.Running, vm.State );
	}

	[TestMethod]
	public void ScreamOpcodesPassVisitorAndLevelOperands()
	{
		var effects = new RecordingEffects();
		var vm = Load( new Asm()
			.I( Opcode.STARTSCREAM, V( 0 ), 2 )
			.I( Opcode.SINGLESCREAM, V( 0 ), 3 )
			.I( Opcode.STOPSCREAM )
			.I( Opcode.SCREAMLEVEL, 65 )
			.I( Opcode.COPY, V( 2 ), 1 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		vm.Variables[0] = 44;
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[2] );
		CollectionAssert.AreEqual( new[] { "STARTSCREAM 44 2", "SINGLESCREAM 44 3", "STOPSCREAM", "SCREAMLEVEL 65" }, effects.Calls );
		Assert.AreEqual( RideVMState.Running, vm.State );
	}

	[TestMethod]
	public void AtmosphereOpcodesPassTheirValues()
	{
		var effects = new RecordingEffects();
		var vm = Load( new Asm()
			.I( Opcode.SETREVERB, 7 )
			.I( Opcode.DIPMUSIC, 1 )
			.I( Opcode.REPAIREFFECT, 0 )
			.I( Opcode.TURBO, V( 0 ) )
			.I( Opcode.SPARK, V( 1 ), 5, 6, 7 )
			.I( Opcode.COPY, V( 2 ), 1 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		vm.Variables[0] = 1;
		vm.Variables[1] = 2;
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[2] );
		CollectionAssert.AreEqual( new[] { "SETREVERB 7", "DIPMUSIC 1", "REPAIREFFECT 0", "TURBO 1", "SPARK 2 5 6 7" }, effects.Calls );
		Assert.AreEqual( RideVMState.Running, vm.State );
	}

	[TestMethod]
	public void ParkClockOpcodesWriteTheReportedComponentAndSetFlags()
	{
		var effects = new RecordingEffects { Result = call => call.Opcode switch { Opcode.HOUR => 9, Opcode.MIN => 0, Opcode.SEC => 12, _ => 0 } };
		var vm = Load( new Asm()
			.I( Opcode.HOUR, V( 0 ) )
			.I( Opcode.BRANCH_PV, "hour" )
			.I( Opcode.COPY, V( 3 ), 0 ).I( Opcode.BRANCH, "minutes" )
			.Label( "hour" ).I( Opcode.COPY, V( 3 ), 1 )
			.Label( "minutes" ).I( Opcode.MIN, 0 )
			.I( Opcode.BRANCH_Z, "onthehour" )
			.I( Opcode.COPY, V( 3 ), 2 ).I( Opcode.BRANCH, "idle" )
			.Label( "onthehour" ).I( Opcode.SEC, V( 2 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		Run( vm, 1 );
		Assert.AreEqual( 9, vm.Variables[0], "HOUR writes the reported hour to the destination" );
		Assert.AreEqual( 12, vm.Variables[2], "SEC writes the reported second to the destination" );
		Assert.AreEqual( 1, vm.Variables[3], "HOUR set the positive flag, MIN the zero flag (`MIN 0; BRANCH_Z` corpus form)" );
		CollectionAssert.AreEqual( new[] { "HOUR 0", "MIN 0", "SEC 0" }, effects.Calls );
		Assert.AreEqual( RideVMState.Running, vm.State );
	}
}
