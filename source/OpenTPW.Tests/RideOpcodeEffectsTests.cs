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

	/// <summary>
	/// Records every effect call (opcode plus resolved operand values) and returns scripted results. It also owns
	/// animation channels whose AnimID the test sets (<see cref="AnimationId"/>), so TRIGWAITANIM can resume.
	/// </summary>
	private sealed class RecordingEffects : IRideScriptEffects, IRideAnimationQuery
	{
		public readonly List<string> Calls = new();
		public Func<RideEffectCall, int> Result = _ => 0;
		public int AnimationId = ObjectAnimator.NoAnimation;

		public int Perform( RideEffectCall call )
		{
			Calls.Add( call.ToString() );
			return Result( call );
		}

		public int GetAnimationId( RideVM vm, int channel ) => AnimationId;
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
		Assert.AreEqual( 1, vm.Variables[2], "TRIGANIM_CH set the WAIT4ANIM deadline, but LOOPANIM_CH zeroed it (0xb03fc), so WAIT4ANIM continues" );
		Assert.AreEqual( RideVMState.Running, vm.State );
	}

	/// <summary>
	/// TRIGANIM writes max(ret − 300, 300) and sets the one WAIT4ANIM deadline (now + out); FLUSHANIM keeps it; WAITANIM
	/// waits max(unsigned(ret − 300), 300) and zeroes the deadline, so a later WAIT4ANIM continues at once.
	/// </summary>
	[TestMethod]
	public void PlainAnimationTriggersSetTheDeadlineThatWait4AnimWaitsFor()
	{
		var effects = new RecordingEffects { Result = call => call.Opcode switch { Opcode.TRIGANIM => 1000, Opcode.WAITANIM => 900, _ => 0 } };
		var vm = Load( new Asm()
			.I( Opcode.TRIGANIM, 5, 0, V( 0 ) )
			.I( Opcode.FLUSHANIM )
			.I( Opcode.WAIT4ANIM )
			.I( Opcode.COPY, V( 1 ), 1 )
			.I( Opcode.TRIGANIM, 5, 0, V( 0 ) )
			.I( Opcode.WAITANIM, 6, 0 )
			.I( Opcode.COPY, V( 2 ), 1 )
			.I( Opcode.WAIT4ANIM )
			.I( Opcode.COPY, V( 3 ), 1 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		Run( vm, 1 );
		var start = vm.TimeMilliseconds;
		Assert.AreEqual( 700, vm.Variables[0], "max(1000 − 300, 300)" );
		Assert.AreEqual( RideVMState.Waiting, vm.State );
		Assert.AreEqual( start + 700, vm.WakeTimeMilliseconds, 1e-9, "FLUSHANIM keeps the deadline" );
		var ticks = 1;
		while ( vm.Variables[2] == 0 && ticks < 200 )
		{
			vm.Advance( Tick );
			++ticks;
		}
		Assert.AreEqual( 1, vm.Variables[1] );
		Assert.IsTrue( vm.TimeMilliseconds >= start + 700 + 600, "WAITANIM waited max(900 − 300, 300) = 600 ms" );
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[3], "WAITANIM zeroed the deadline TRIGANIM set" );
		CollectionAssert.AreEqual( new[] { "TRIGANIM 5 0 0", "FLUSHANIM", "TRIGANIM 5 0 700", "WAITANIM 6 0" }, effects.Calls );
	}

	/// <summary>
	/// TRIGANIMSPEED writes max(ret − 300, 300) and sets the deadline now + out × 1000 / operand (0xb00c4, 0xb00dc).
	/// TRIGWAITANIM writes max(ret − 300, 300), overwrites that single deadline with now + out, and then waits for
	/// channel 0 to show the triggered AnimID (not for a duration); WAIT4ANIM waits for the overwritten deadline.
	/// </summary>
	[TestMethod]
	public void TrigWaitAnimWaitsForItsAnimIdAndTrigAnimSpeedFeedsWait4Anim()
	{
		var effects = new RecordingEffects { Result = call => call.Opcode switch { Opcode.TRIGWAITANIM => 600, Opcode.TRIGANIMSPEED => 3000, _ => 0 }, AnimationId = 6 };
		var speed = Load( new Asm()
			.I( Opcode.TRIGANIMSPEED, 6, 0, V( 0 ), 4000 )
			.I( Opcode.WAIT4ANIM )
			.I( Opcode.COPY, V( 2 ), 1 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		Run( speed, 1 );
		var start = speed.TimeMilliseconds;
		Assert.AreEqual( 2700, speed.Variables[0], "TRIGANIMSPEED writes max(3000 − 300, 300)" );
		Assert.AreEqual( start + 675, speed.WakeTimeMilliseconds, 1e-9, "WAIT4ANIM waits 2700 × 1000 / 4000 ms" );

		effects.Calls.Clear();
		var vm = Load( new Asm()
			.I( Opcode.TRIGANIMSPEED, 6, 0, V( 0 ), 4000 )
			.I( Opcode.TRIGWAITANIM, 4, 2, V( 1 ) )
			.I( Opcode.COPY, V( 2 ), 1 )
			.I( Opcode.WAIT4ANIM )
			.I( Opcode.COPY, V( 2 ), 2 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		Run( vm, 1 );
		start = vm.TimeMilliseconds;
		Assert.AreEqual( 2700, vm.Variables[0] );
		Assert.AreEqual( 300, vm.Variables[1], "TRIGWAITANIM writes max(600 − 300, 300)" );
		Assert.AreEqual( 5, vm.TriggerWaitToken, "it waits for AnimID 4 (token 4 + 1)" );
		Run( vm, 30 );
		Assert.AreEqual( 0, vm.Variables[2], "channel 0 still shows AnimID 6, whatever the clip length" );
		effects.AnimationId = 4;
		Run( vm, 1 );
		Assert.AreEqual( 2, vm.Variables[2], "the channel shows AnimID 4, and TRIGWAITANIM's deadline (now + 300) already passed, so WAIT4ANIM continues in the same slice" );
		CollectionAssert.AreEqual( new[] { "TRIGANIMSPEED 6 0 0 4000", "TRIGWAITANIM 4 2 0" }, effects.Calls );
	}

	/// <summary>The single deadline: TRIGWAITANIM's now + 300 replaces TRIGANIMSPEED's now + 675, so WAIT4ANIM ends at 300 ms.</summary>
	[TestMethod]
	public void TrigWaitAnimOverwritesTheWait4AnimDeadline()
	{
		var effects = new RecordingEffects { Result = call => call.Opcode switch { Opcode.TRIGWAITANIM => 600, Opcode.TRIGANIMSPEED => 3000, _ => 0 }, AnimationId = 4 };
		var vm = Load( new Asm()
			.I( Opcode.TRIGANIMSPEED, 6, 0, V( 0 ), 4000 )
			.I( Opcode.TRIGWAITANIM, 4, 2, V( 1 ) )
			.I( Opcode.WAIT4ANIM )
			.I( Opcode.COPY, V( 2 ), 1 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), effects );
		Run( vm, 1 );
		Assert.AreEqual( RideVMState.Waiting, vm.State, "TRIGWAITANIM found AnimID 4 at once (no yield), then WAIT4ANIM waits" );
		Assert.AreEqual( vm.TimeMilliseconds + 300, vm.WakeTimeMilliseconds, 1e-9 );
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
