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

	private static RideVM Load( Asm asm, IRideScriptEffects? effects = null ) =>
		new( new RideScriptFile( new MemoryStream( asm.Build( Vars ) ) ), new RideVMOptions { Seed = 1, Effects = effects } );

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
		Assert.AreEqual( expected, RideAnimationTiming.WaitAnimDelay( returned, RideVM.ScriptSpeed ) );
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
		var zero = Load( Idle( new Asm().I( Opcode.TRIGANIMSPEED, 6, 0, V( 0 ), 0 ) ), new Host() );
		Run( zero, 1 );
		Assert.AreEqual( RideVMState.Faulted, zero.State, "speed 0 has no traced contract" );
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

	/// <summary>Without an animation host a trigger counts as a missing clip (1000 ms), so waits stay finite.</summary>
	[TestMethod]
	public void UnansweredTriggersCountAsMissingClips()
	{
		var vm = Load( Idle( new Asm().I( Opcode.TRIGANIM, 5, 0, V( 0 ) ).I( Opcode.WAITANIM, 5, 0 ).I( Opcode.TRIGWAITANIM, 5, 0, V( 1 ) ).I( Opcode.COPY, V( 2 ), 1 ) ) );
		Run( vm, 1 );
		Assert.AreEqual( 700, vm.Variables[0] );
		Assert.AreEqual( vm.TimeMilliseconds + 700, vm.WakeTimeMilliseconds, 1e-6 );
		Run( vm, 43 );
		Assert.AreEqual( (700, 1), (vm.Variables[1], vm.Variables[2]), "TRIGWAITANIM has no channel to wait for" );
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
