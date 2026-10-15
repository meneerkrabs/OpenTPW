using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// RSE animation opcodes against the Feral Mac handlers (docs/reverse/ANIM-plan.md §8): trigger results become
/// the out variable and the single WAIT4ANIM deadline, WAITANIM's unsigned conversion, the LOOPANIM token,
/// TRIGWAITANIM's AnimID wait, TRIGANIMSPEED's per-mille speed, FLUSHANIM and GETANIM.
/// </summary>
[TestClass]
public class AnimationOpcodeTests
{
	private static readonly string[] Vars = { "V0", "V1", "V2", "V3" };

	/// <summary>An animation host: records calls, answers each trigger with <see cref="Result"/> and reports AnimIDs per channel.</summary>
	private sealed class Host : IRideScriptEffects, IRideAnimationQuery
	{
		public readonly List<string> Calls = new();
		public Func<RideEffectCall, int> Result = _ => 1000;
		public readonly Dictionary<int, int> AnimationIds = new();
		public int Queries;

		public int Perform( RideEffectCall call )
		{
			Calls.Add( call.ToString() );
			return Result( call );
		}

		public int GetAnimationId( RideVM vm, int channel )
		{
			Queries++;
			return AnimationIds.TryGetValue( channel, out var id ) ? id : ObjectAnimator.NoAnimation;
		}
	}

	private static RideVM Load( Asm asm, IRideScriptEffects? effects = null, RideScriptWorld? world = null ) =>
		new( new RideScriptFile( new MemoryStream( asm.Build( Vars ) ) ), new RideVMOptions { Seed = 1, Effects = effects, World = world } );

	private static void Run( RideVM vm, int ticks )
	{
		for ( var tick = 0; tick < ticks; ++tick )
			vm.Advance( Tick );
	}

	private static Asm Idle( Asm asm ) => asm.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" );

	[DataTestMethod]
	[DataRow( 0, 300 )]
	[DataRow( 299, 300 )]
	[DataRow( 300, 300 )]
	[DataRow( 599, 300 )]
	[DataRow( 600, 300 )]
	[DataRow( 601, 301 )]
	[DataRow( 1000, 700 )]
	public void TriggerOutputIsTheReturnLess300AtLeast300( int returned, int expected )
	{
		Assert.AreEqual( expected, RideAnimationTiming.AdjustTriggerReturn( returned ) );
		foreach ( var opcode in new[] { Opcode.TRIGANIM, Opcode.TRIGANIM_CH, Opcode.TRIGANIMSPEED, Opcode.TRIGWAITANIM } )
		{
			var asm = opcode switch
			{
				Opcode.TRIGANIM => new Asm().I( opcode, 5, 0, V( 0 ) ),
				Opcode.TRIGWAITANIM => new Asm().I( opcode, 5, 0, V( 0 ) ),
				_ => new Asm().I( opcode, 5, 0, V( 0 ), opcode == Opcode.TRIGANIMSPEED ? 1000 : 1 )
			};
			var host = new Host { Result = _ => returned };
			host.AnimationIds[0] = 5;
			host.AnimationIds[1] = 5;
			var vm = Load( Idle( asm ), host );
			Run( vm, 1 );
			Assert.AreEqual( expected, vm.Variables[0], opcode.ToString() );
			Assert.AreEqual( vm.TimeMilliseconds + expected, vm.Wait4AnimDeadline, 1e-9, opcode.ToString() );
		}
	}

	/// <summary>Each trigger overwrites the one deadline (the later clip's end is not kept); WAIT4ANIM waits for the last trigger only.</summary>
	[TestMethod]
	public void Wait4AnimWaitsForTheLastTriggerOnlyAndClearsTheDeadline()
	{
		var host = new Host { Result = call => call.Argument( 1 ) == 0 ? 3000 : 1000 };
		var vm = Load( Idle( new Asm()
			.I( Opcode.TRIGANIM, 5, 0, V( 0 ) )   // 2700 ms
			.I( Opcode.TRIGANIM, 5, 1, V( 1 ) )   // 700 ms, overwrites
			.I( Opcode.WAIT4ANIM )
			.I( Opcode.COPY, V( 2 ), 1 )
			.I( Opcode.WAIT4ANIM )
			.I( Opcode.COPY, V( 3 ), 1 ) ), host );
		Run( vm, 1 );
		var start = vm.TimeMilliseconds;
		Assert.AreEqual( (2700, 700), (vm.Variables[0], vm.Variables[1]) );
		Assert.AreEqual( start + 700, vm.Wait4AnimDeadline, 1e-9 );
		Assert.AreEqual( RideVMState.Waiting, vm.State );
		while ( vm.Variables[2] == 0 )
			Run( vm, 1 );
		Assert.IsTrue( vm.TimeMilliseconds >= start + 700 && vm.TimeMilliseconds < start + 700 + 17, vm.TimeMilliseconds.ToString() );
		Assert.AreEqual( 0, vm.Wait4AnimDeadline, "reaching the deadline clears it" );
		Assert.AreEqual( 1, vm.Variables[3], "a cleared deadline does not block" );
	}

	[TestMethod]
	public void WaitAnimAndLoopAnimZeroTheDeadline()
	{
		var clearing = new Func<Asm, Asm>[]
		{
			asm => asm.I( Opcode.WAITANIM, 5, 1 ), asm => asm.I( Opcode.WAITANIM_CH, 5, 1, 2 ),
			asm => asm.I( Opcode.LOOPANIM, 5, 1 ), asm => asm.I( Opcode.LOOPANIM_CH, 5, 1, 2 )
		};
		foreach ( var clear in clearing )
		{
			var host = new Host();
			var vm = Load( Idle( clear( new Asm().I( Opcode.TRIGANIM, 5, 0, V( 0 ) ) ) ), host );
			Run( vm, 1 );
			Assert.AreEqual( 0, vm.Wait4AnimDeadline, host.Calls[^1] );
		}
	}

	[DataTestMethod]
	[DataRow( 1000, 700 )]
	[DataRow( 600, 300 )]
	[DataRow( 300, 300 )]
	[DataRow( 301, 300 )]
	[DataRow( 601, 301 )]
	[DataRow( 299, int.MaxValue )]
	[DataRow( 0, int.MaxValue )]
	[DataRow( 266, int.MaxValue )]
	public void WaitAnimConvertsUnsignedSoShortClipsWrap( int returned, int expected )
	{
		Assert.AreEqual( expected, RideAnimationTiming.WaitAnimDelay( returned, RideVM.DefaultScriptSpeed ) );
		var vm = Load( Idle( new Asm().I( Opcode.WAITANIM, 5, 0 ).I( Opcode.COPY, V( 0 ), 1 ) ), new Host { Result = _ => returned } );
		Run( vm, 1 );
		Assert.AreEqual( vm.TimeMilliseconds + expected, vm.WakeTimeMilliseconds, 1e-6 );
	}

	[TestMethod]
	public void WaitAnimDividesBySpeedBeforeTheUnsignedMinimum()
	{
		Assert.AreEqual( 350, RideAnimationTiming.WaitAnimDelay( 1000, 2f ) );
		Assert.AreEqual( 300, RideAnimationTiming.WaitAnimDelay( 800, 2f ), "250 after the division, then at least 300" );
		Assert.AreEqual( 1400, RideAnimationTiming.WaitAnimDelay( 1000, 0.5f ) );
		Assert.AreEqual( 233, RideAnimationTiming.TriggerDelay( 700, 3f ) );
		// A 9-tick clip returns trunc(1000 × 9 / 30) = 300 ms, the shortest that does not wrap; 8 ticks return 266.
		var model = new ModelFile( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) );
		var animator = new ObjectAnimator( model );
		ModelAnimation Clip( ushort ticks ) => new ModelFile( new MemoryStream( Md2VertexAnimationTests.CreateAnimation( 0, ticks ) ) ).Clip!;
		Assert.AreEqual( 300, animator.StartLengthMilliseconds( Clip( 9 ) ) );
		Assert.AreEqual( 266, animator.StartLengthMilliseconds( Clip( 8 ) ) );
		Assert.AreEqual( 1000, animator.StartLengthMilliseconds( Clip( 30 ) ) );
	}

	[TestMethod]
	public void WaitAnimChannelSkipsTheDivisionAndStaysUnsigned()
	{
		Assert.AreEqual( 700u, RideAnimationTiming.WaitAnimChannelDelay( 1000 ) );
		Assert.AreEqual( 300u, RideAnimationTiming.WaitAnimChannelDelay( 500 ) );
		Assert.AreEqual( uint.MaxValue, RideAnimationTiming.WaitAnimChannelDelay( 299 ) );
		var host = new Host();
		var vm = Load( Idle( new Asm().I( Opcode.WAITANIM_CH, 5, 2, 3 ) ), host );
		Run( vm, 1 );
		CollectionAssert.AreEqual( new[] { "WAITANIM_CH 5 2 3" }, host.Calls );
		Assert.AreEqual( vm.TimeMilliseconds + 700, vm.WakeTimeMilliseconds, 1e-6 );
	}

	/// <summary>
	/// WAITANIM_CH adds its unsigned wait to the 32-bit scheduler clock without fctiwz (0xb035c): a 166 ms clip gives
	/// 2³² − 134, so at a clock of 10 s the deadline is 134 ms in the past and the script continues next slice. The
	/// clock is the park's shared scheduler clock, not the script's own, so a script created after 10 s of park time
	/// continues at once too. Only a scheduler clock below 134 ms (a park that has just started, RIDES-044) leaves the
	/// sum unwrapped (a wait of about 49.7 days), and a sum of exactly 0 reads as "not waiting", so the next execution
	/// triggers again. WAITANIM saturates the same clip to INT32_MAX instead.
	/// </summary>
	[TestMethod]
	public void WaitAnimChannelWrapsAShortClipIntoThePast()
	{
		Assert.AreEqual( 4294967162u, RideAnimationTiming.WaitAnimChannelDelay( 166 ) );
		Assert.AreEqual( int.MaxValue, RideAnimationTiming.WaitAnimDelay( 166, RideVM.DefaultScriptSpeed ), "WAITANIM's fctiwz saturates" );

		var host = new Host { Result = _ => 166 };
		var vm = Load( Idle( new Asm().I( Opcode.WAIT, 10000 ).I( Opcode.WAITANIM_CH, 5, 0, 3 ).I( Opcode.COPY, V( 0 ), 1 ) ), host );
		Run( vm, 1 );
		while ( host.Calls.Count == 0 )
			Run( vm, 1 );
		Assert.IsTrue( vm.TimeMilliseconds >= 10000, vm.TimeMilliseconds.ToString() );
		Assert.AreEqual( RideVMState.Running, vm.State, "a deadline in the past only yields" );
		Assert.AreEqual( 0, vm.Variables[0] );
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[0], "the next slice finds the deadline passed and continues" );
		CollectionAssert.AreEqual( new[] { "WAITANIM_CH 5 0 3" }, host.Calls, "one trigger" );

		var late = Load( Idle( new Asm().I( Opcode.WAITANIM_CH, 5, 0, 3 ).I( Opcode.COPY, V( 0 ), 1 ) ), new Host { Result = _ => 166 }, vm.World );
		Assert.AreEqual( 0d, late.TimeMilliseconds );
		Assert.AreEqual( vm.SchedulerMilliseconds, late.ClockOriginMilliseconds, "a later script starts at the shared clock" );
		late.Advance( Tick );
		Assert.AreEqual( RideVMState.Running, late.State, "on the scheduler clock the sum wraps for a script created late as well" );
		late.Advance( Tick );
		Assert.AreEqual( 1, late.Variables[0] );

		var early = Load( Idle( new Asm().I( Opcode.WAITANIM_CH, 5, 0, 3 ).I( Opcode.COPY, V( 0 ), 1 ) ), new Host { Result = _ => 166 } );
		Run( early, 1 );
		Assert.AreEqual( RideVMState.Waiting, early.State, "in a script world whose scheduler clock is at 16 ms the sum does not wrap (RIDES-044: the clock starts at 0 with the park)" );
		Assert.AreEqual( early.TimeMilliseconds + 4294967162d, early.WakeTimeMilliseconds, 1e-3 );

		host = new Host { Result = _ => 50 };
		var zero = Load( Idle( new Asm().I( Opcode.WAITANIM_CH, 5, 0, 3 ).I( Opcode.COPY, V( 0 ), 1 ) ), host );
		zero.Advance( 0.25 );
		Assert.AreEqual( 250u, zero.ClockMilliseconds32 );
		Assert.AreEqual( RideVMState.Running, zero.State );
		zero.Advance( 0.25 );
		Assert.AreEqual( 2, host.Calls.Count, "250 + (2³² − 250) = 0 is \"not waiting\": the next execution triggers again" );
		zero.Advance( 0.25 );
		Assert.AreEqual( 1, zero.Variables[0] );
	}

	/// <summary>
	/// LOOPANIM keeps a script-local token: an equal token is a no-op (also for the deadline), the plain triggers
	/// reset it to 0xFFFF, and the _CH opcodes neither test nor reset it.
	/// </summary>
	[TestMethod]
	public void LoopAnimTokenIsScriptLocalAndResetByThePlainTriggers()
	{
		Assert.AreEqual( 0x30005, RideAnimationTiming.LoopToken( 5, 3 ) );
		var host = new Host();
		var vm = Load( Idle( new Asm()
			.I( Opcode.LOOPANIM, 0, 0 )
			.I( Opcode.LOOPANIM, 0, 0 )
			.I( Opcode.TRIGANIM_CH, 5, 1, V( 0 ), 1 )
			.I( Opcode.LOOPANIM, 0, 0 )
			.I( Opcode.TRIGANIM, 5, 1, V( 0 ) )
			.I( Opcode.LOOPANIM, 0, 0 )
			.I( Opcode.LOOPANIM_CH, 0, 0, 0 )
			.I( Opcode.LOOPANIM_CH, 0, 0, 0 )
			.I( Opcode.LOOPANIM, 0, 0 ) ), host );
		Assert.AreEqual( RideAnimationTiming.NoLoopToken, vm.LoopAnimationToken, "a new script's token matches nothing, so LOOPANIM 0 0 starts" );
		Run( vm, 1 );
		CollectionAssert.AreEqual( new[] { "LOOPANIM 0 0", "TRIGANIM_CH 5 1 0 1", "TRIGANIM 5 1 700", "LOOPANIM 0 0", "LOOPANIM_CH 0 0 0", "LOOPANIM_CH 0 0 0" }, host.Calls );
		Assert.AreEqual( 0, vm.LoopAnimationToken );

		// An equal token returns before the deadline is cleared.
		var keep = Load( Idle( new Asm().I( Opcode.LOOPANIM, 4, 0 ).I( Opcode.TRIGANIM_CH, 5, 1, V( 0 ), 1 ).I( Opcode.LOOPANIM, 4, 0 ) ), new Host() );
		Run( keep, 1 );
		Assert.AreNotEqual( 0, keep.Wait4AnimDeadline );
		foreach ( var reset in new[] { Opcode.WAITANIM, Opcode.TRIGWAITANIM, Opcode.TRIGANIMSPEED } )
		{
			var asm = new Asm().I( Opcode.LOOPANIM, 4, 0 );
			asm = reset switch
			{
				Opcode.WAITANIM => asm.I( reset, 5, 1 ),
				Opcode.TRIGWAITANIM => asm.I( reset, 5, 1, V( 0 ) ),
				_ => asm.I( reset, 5, 1, V( 0 ), 2000 )
			};
			var resetHost = new Host();
			resetHost.AnimationIds[0] = 5;
			var resetVm = Load( Idle( asm ), resetHost );
			Run( resetVm, 1 );
			Assert.AreEqual( RideAnimationTiming.NoLoopToken, resetVm.LoopAnimationToken, reset.ToString() );
		}
	}

	/// <summary>
	/// A visitor-wrapped host still has its object: <see cref="VisitorRideScriptEffects"/> forwards the AnimID query, so
	/// TRIGWAITANIM resumes once the inner host's channel shows the AnimID. Wrapped around effects without channels the
	/// script has no object for the query either, and the wait never ends, as without the wrapper.
	/// </summary>
	[TestMethod]
	public void TrigWaitAnimResumesThroughTheVisitorWrapper()
	{
		var host = new Host();
		host.AnimationIds[0] = ObjectAnimator.NoAnimation;
		var bridge = new RideVisitorBridge( 5, "Wrapped", RideVisitorKind.Shop, 2, 0, 10 );
		var vm = Load( Idle( new Asm().I( Opcode.TRIGWAITANIM, 6, 0, V( 0 ) ).I( Opcode.COPY, V( 1 ), 1 ) ), new VisitorRideScriptEffects( bridge, host ) );
		Run( vm, 2 );
		Assert.AreEqual( 0, vm.Variables[1] );
		Assert.AreEqual( 2, host.Queries, "the wrapper forwards the query" );
		host.AnimationIds[0] = 6;
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[1], "the inner host's channel shows AnimID 6" );
		Assert.AreEqual( 0, vm.TriggerWaitToken );

		var bare = Load( Idle( new Asm().I( Opcode.TRIGWAITANIM, 6, 0, V( 0 ) ).I( Opcode.COPY, V( 1 ), 1 ) ),
			new VisitorRideScriptEffects( new RideVisitorBridge( 6, "Bare", RideVisitorKind.Shop, 2, 0, 10 ), UnimplementedRideScriptEffects.Instance ) );
		Run( bare, 120 );
		Assert.AreEqual( 0, bare.Variables[1], "no object behind the wrapper: the wait never ends (RIDES-037)" );
		Assert.AreEqual( 300, bare.Variables[0], "no object: ret 0 gives 300" );
		Assert.AreEqual( 7, bare.TriggerWaitToken );
	}

	/// <summary>Without an object the wait handlers compare the out operand's raw word + 1 with the token (RIDES-037, PC 0x5535c7): a literal 0x0000vvvv resumes once v is the triggered AnimID, any other literal and every variable never do.</summary>
	[TestMethod]
	public void TrigWaitAnimWithoutObjectResumesOnLiteralOutOperandMatchingTheAnimId()
	{
		RideVM Bare( int literal ) => Load( Idle( new Asm().I( Opcode.TRIGWAITANIM, 6, 0, literal ).I( Opcode.COPY, V( 1 ), 1 ) ),
			new VisitorRideScriptEffects( new RideVisitorBridge( 7, "Bare", RideVisitorKind.Shop, 2, 0, 10 ), UnimplementedRideScriptEffects.Instance ) );
		var matching = Bare( 6 );
		Run( matching, 120 );
		Assert.AreEqual( 1, matching.Variables[1], "literal 6 + 1 equals the token 7" );
		Assert.AreEqual( 0, matching.TriggerWaitToken );
		var other = Bare( 5 );
		Run( other, 120 );
		Assert.AreEqual( 0, other.Variables[1] );
		Assert.AreEqual( 7, other.TriggerWaitToken );
	}

	/// <summary>
	/// TRIGWAITANIM triggers, then executes again at once: it continues as soon as channel 0 shows the triggered
	/// AnimID, not when the clip ends, and yields one slice at a time while the channel shows something else.
	/// </summary>
	[TestMethod]
	public void TrigWaitAnimWaitsForTheChannelToShowTheAnimation()
	{
		var host = new Host { Result = _ => 5000 };
		host.AnimationIds[0] = 5;
		var vm = Load( Idle( new Asm().I( Opcode.TRIGWAITANIM, 5, 0, V( 0 ) ).I( Opcode.COPY, V( 1 ), 1 ) ), host );
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[1], "the channel already shows AnimID 5: no wait for the 5 s clip" );
		Assert.AreEqual( 4700, vm.Variables[0] );
		Assert.AreEqual( 1, host.Queries );
		Assert.AreEqual( 0, vm.TriggerWaitToken );
		Assert.AreEqual( vm.TimeMilliseconds + 4700, vm.Wait4AnimDeadline, 1e-9 );

		// A clip that did not start (no member) leaves the channel on another AnimID: the script waits.
		host = new Host();
		host.AnimationIds[0] = ObjectAnimator.NoAnimation;
		vm = Load( Idle( new Asm().I( Opcode.TRIGWAITANIM, 6, 0, V( 0 ) ).I( Opcode.COPY, V( 1 ), 1 ) ), host );
		Run( vm, 3 );
		Assert.AreEqual( 0, vm.Variables[1] );
		Assert.AreEqual( 7, vm.TriggerWaitToken );
		Assert.AreEqual( 3, host.Queries, "one check per slice" );
		Assert.AreEqual( 1, host.Calls.Count, "triggered once" );
		host.AnimationIds[0] = 6;
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[1] );
		Assert.AreEqual( 0, vm.TriggerWaitToken );

		// The _CH form waits on its channel and keeps the LOOPANIM token.
		host = new Host();
		host.AnimationIds[2] = 9;
		vm = Load( Idle( new Asm().I( Opcode.LOOPANIM, 1, 0 ).I( Opcode.TRIGWAITANIM_CH, 9, 0, V( 0 ), 2 ).I( Opcode.COPY, V( 1 ), 1 ) ), host );
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[1] );
		Assert.AreEqual( RideAnimationTiming.LoopToken( 1, 0 ), vm.LoopAnimationToken );
		CollectionAssert.AreEqual( new[] { "LOOPANIM 1 0", "TRIGWAITANIM_CH 9 0 0 2" }, host.Calls );
	}

	[TestMethod]
	public void TrigAnimSpeedDividesTheDeadlineByThePerMilleOperand()
	{
		Assert.AreEqual( 4f, RideAnimationTiming.SpeedOperand( 1f, 4000 ) );
		Assert.AreEqual( 1.8f, RideAnimationTiming.SpeedOperand( 1f, 1800 ) );
		Assert.AreEqual( 175, RideAnimationTiming.SpeedTriggerDelay( 700, 4000 ) );
		Assert.AreEqual( 388, RideAnimationTiming.SpeedTriggerDelay( 700, 1800 ), "signed divw truncates" );
		var host = new Host();
		var vm = Load( Idle( new Asm().I( Opcode.TRIGANIMSPEED, 6, 0, V( 0 ), 4000 ) ), host );
		Run( vm, 1 );
		Assert.AreEqual( 700, vm.Variables[0], "the out variable is not speed-scaled" );
		Assert.AreEqual( vm.TimeMilliseconds + 175, vm.Wait4AnimDeadline, 1e-9 );
		CollectionAssert.AreEqual( new[] { "TRIGANIMSPEED 6 0 0 4000" }, host.Calls );
		Assert.AreEqual( -175, RideAnimationTiming.SpeedTriggerDelay( 700, -4000 ), "divw is signed: a negative speed puts the deadline before now" );
		var zero = Load( Idle( new Asm().I( Opcode.TRIGANIMSPEED, 6, 0, V( 0 ), 0 ) ), new Host() );
		Run( zero, 1 );
		Assert.AreEqual( RideVMState.Faulted, zero.State, "speed 0 divides by zero (undefined divw)" );
		var negative = Load( Idle( new Asm().I( Opcode.TRIGANIMSPEED, 6, 0, V( 0 ), 0xF060 ) ), new Host() );
		Run( negative, 1 );
		Assert.AreEqual( RideVMState.Faulted, negative.State, "a negative speed is defined in the original, but its channel playback is not traced (RIDES-038)" );
		StringAssert.Contains( negative.FaultMessage, "RIDES-038" );
	}

	/// <summary>FLUSHANIM only asks the host to drop the deferred clip: the deadline stays.</summary>
	[TestMethod]
	public void FlushAnimKeepsTheDeadline()
	{
		var host = new Host();
		var vm = Load( Idle( new Asm().I( Opcode.TRIGANIM, 5, 0, V( 0 ) ).I( Opcode.FLUSHANIM ).I( Opcode.FLUSHANIM_CH, 2 ).I( Opcode.WAIT4ANIM ).I( Opcode.COPY, V( 1 ), 1 ) ), host );
		Run( vm, 1 );
		CollectionAssert.AreEqual( new[] { "TRIGANIM 5 0 0", "FLUSHANIM", "FLUSHANIM_CH 2" }, host.Calls );
		Assert.AreEqual( 0, vm.Variables[1], "WAIT4ANIM still waits for the trigger" );
		Run( vm, 45 );
		Assert.AreEqual( 1, vm.Variables[1] );
	}

	[TestMethod]
	public void GetAnimWritesTheResultAndSetsFlags()
	{
		var host = new Host { Result = call => call.Opcode == Opcode.GETANIM ? -1 : 5 };
		var vm = Load( Idle( new Asm()
			.I( Opcode.GETANIM, V( 0 ) ).I( Opcode.BRANCH_PV, "positive" ).I( Opcode.COPY, V( 1 ), 1 )
			.Label( "positive" ).I( Opcode.GETANIM_CH, V( 2 ), 3 ).I( Opcode.BRANCH_PV, "done" ).I( Opcode.COPY, V( 3 ), 1 )
			.Label( "done" ) ), host );
		Run( vm, 1 );
		Assert.AreEqual( (-1, 1, 5, 0), (vm.Variables[0], vm.Variables[1], vm.Variables[2], vm.Variables[3]) );
		CollectionAssert.AreEqual( new[] { "GETANIM 0", "GETANIM_CH 0 3" }, host.Calls );

		// Without a host the query keeps its destination.
		var none = Load( Idle( new Asm().I( Opcode.COPY, V( 0 ), 4 ).I( Opcode.GETANIM, V( 0 ) ) ) );
		Run( none, 1 );
		Assert.AreEqual( 4, none.Variables[0] );
	}

	/// <summary>
	/// Without an animation host the script has no object: every trigger handler uses ret = 0 (li r3,0 at 0xafb1c,
	/// 0xafc74, 0xafe88, 0xb0078, 0xb01d4, 0xb032c, 0xb0510). The triggers write 300, WAITANIM waits INT32_MAX ms,
	/// WAITANIM_CH wraps like any clip under 300 ms, and TRIGWAITANIM never finds its AnimID.
	/// </summary>
	[TestMethod]
	public void ScriptsWithoutAnObjectTriggerWithZeroMilliseconds()
	{
		var vm = Load( Idle( new Asm().I( Opcode.TRIGANIM, 5, 0, V( 0 ) ).I( Opcode.TRIGANIMSPEED, 5, 0, V( 1 ), 2000 ).I( Opcode.WAITANIM, 5, 0 ).I( Opcode.COPY, V( 2 ), 1 ) ) );
		Run( vm, 1 );
		Assert.AreEqual( (300, 300), (vm.Variables[0], vm.Variables[1]) );
		Assert.AreEqual( RideVMState.Waiting, vm.State );
		Assert.AreEqual( vm.TimeMilliseconds + int.MaxValue, vm.WakeTimeMilliseconds, 1e-3, "WAITANIM waits about 24.8 days" );

		var channel = Load( Idle( new Asm().I( Opcode.WAIT, 1000 ).I( Opcode.WAITANIM_CH, 5, 0, 1 ).I( Opcode.COPY, V( 2 ), 1 ) ) );
		Run( channel, 70 );
		Assert.AreEqual( 1, channel.Variables[2], "WAITANIM_CH's 2³² − 300 wraps into the past once the clock is past 300 ms" );

		var wait = Load( Idle( new Asm().I( Opcode.TRIGWAITANIM, 5, 0, V( 1 ) ).I( Opcode.COPY, V( 2 ), 1 ) ) );
		Run( wait, 120 );
		Assert.AreEqual( 300, wait.Variables[1] );
		Assert.AreEqual( 0, wait.Variables[2], "without an object the token compare never matches" );
		Assert.AreEqual( 6, wait.TriggerWaitToken );
		Assert.AreEqual( RideVMState.Running, wait.State, "it re-executes every slice" );
		Assert.AreEqual( 1, wait.UnimplementedEffects[Opcode.TRIGWAITANIM], "and triggers only once" );
	}

	/// <summary>The deadline, the LOOPANIM token and the TRIGWAITANIM token decide later steps, so the canonical hash reads them.</summary>
	[TestMethod]
	public void AnimationTokensAreHashed()
	{
		ulong Hash( RideVM vm )
		{
			var hash = new StateHasher();
			vm.AddCanonicalState( hash );
			return hash.Value;
		}
		RideVM Fresh() => Load( Idle( new Asm() ), new Host() );
		var baseline = Hash( Fresh() );
		var deadline = Fresh();
		deadline.Wait4AnimDeadline = 5;
		var loop = Fresh();
		loop.LoopAnimationToken = 0;
		var wait = Fresh();
		wait.TriggerWaitToken = 6;
		Assert.AreEqual( 4, new[] { baseline, Hash( deadline ), Hash( loop ), Hash( wait ) }.Distinct().Count() );
	}
}
