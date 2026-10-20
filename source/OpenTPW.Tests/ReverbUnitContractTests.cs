using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Characterization contracts for <see cref="ReverbUnit"/> at the current head. The signals are synthetic and the sample rate is supplied directly to the
/// constructor; nothing here claims that the network, its constants or its output matches the original audio device or backend. The whole-versus-partitioned
/// additions below require exact-head CI; this revision was not executed in the audit environment.
/// </summary>
[TestClass]
public class ReverbUnitContractTests
{
	private const int SampleRate = 44100;
	private static readonly int[] BlockCuts = { 0, 1, 17, 255, 256, 257, 1115, 1116, 1139, 1140, 4096, 8192 };

	private static uint Bits( float value ) => BitConverter.SingleToUInt32Bits( value );

	/// <summary>A 13-word record in the file's word order, with only the fields these contracts vary made explicit.</summary>
	private static ReverbSettings Preset(
		float size = 7.5f,
		float diffusion = 0.65f,
		int room = 0,
		float decay = 4f,
		float decayHf = 1f,
		int reflections = 0,
		float reflectionsDelay = 0f,
		int reverb = 600,
		float reverbDelay = 0f )
	{
		var record = new uint[]
		{
			0u, Bits( size ), Bits( diffusion ), unchecked((uint)room), 0u,
			Bits( decay ), Bits( decayHf ), unchecked((uint)reflections),
			Bits( reflectionsDelay ), unchecked((uint)reverb), Bits( reverbDelay ),
			Bits( 0f ), Bits( -5f ),
		};
		return ReverbSettings.FromRecord( record )!.Value;
	}

	/// <summary>Renders one unit with a synthetic impulse in both send channels at frame zero.</summary>
	private static float[] Render( ReverbSettings settings, int frames )
	{
		var unit = new ReverbUnit( SampleRate );
		unit.Set( settings );
		var send = new float[frames * 2];
		send[0] = send[1] = 10000f;
		var output = new float[frames * 2];
		unit.Process( send, output, frames );
		return output;
	}

	private static ReverbUnit ImpulseUnit( ReverbSettings settings )
	{
		var unit = new ReverbUnit( SampleRate );
		unit.Set( settings );
		var impulse = new float[2];
		impulse[0] = impulse[1] = 10000f;
		unit.Process( impulse, new float[2], 1 );
		return unit;
	}

	private static void AssertZeroInputMatchesFreshAfterSwitch( ReverbSettings source, ReverbSettings target, int frames )
	{
		var unit = ImpulseUnit( source );
		unit.Set( target );
		var after = new float[frames * 2];
		unit.Process( new float[frames * 2], after, frames );

		var fresh = new ReverbUnit( SampleRate );
		fresh.Set( target );
		var expected = new float[frames * 2];
		fresh.Process( new float[frames * 2], expected, frames );

		Assert.IsTrue( expected.All( value => value == 0f ), "the fresh zero-input reference is silent" );
		for ( var index = 0; index < after.Length; index++ )
			Assert.AreEqual( expected[index], after[index], 0f, $"post-change sample {index}" );
	}

	private static void AssertSwitchKeepsTail( ReverbSettings source, ReverbSettings target )
	{
		var unit = ImpulseUnit( source );
		unit.Set( target );
		var output = new float[10000 * 2];
		unit.Process( new float[10000 * 2], output, 10000 );
		Assert.IsTrue( output.Any( value => value != 0f ), "the held tail survives" );
	}

	private static double Energy( float[] output, int firstFrame, int lastFrame )
	{
		var sum = 0d;
		for ( var frame = firstFrame; frame < lastFrame; frame++ )
		{
			var left = (double)output[frame * 2];
			var right = (double)output[frame * 2 + 1];
			sum += left * left + right * right;
		}
		return sum;
	}

	/// <summary>Renders one input as one block and as the same cuts on a second fresh unit, then checks the current block-boundary invariant.</summary>
	private static float[] AssertWholeMatchesPartitioned( ReverbSettings settings, float[] send, int frames )
	{
		var originalSend = (float[])send.Clone();
		var whole = new float[frames * 2];
		var wholeUnit = new ReverbUnit( SampleRate );
		wholeUnit.Set( settings );
		wholeUnit.Process( send, whole, frames );

		var guarded = new float[frames * 2 + 4];
		guarded[0] = guarded[1] = 123.5f;
		guarded[guarded.Length - 2] = guarded[guarded.Length - 1] = -123.5f;
		var partitioned = guarded.AsSpan( 2, frames * 2 );
		var partitionedUnit = new ReverbUnit( SampleRate );
		partitionedUnit.Set( settings );
		for ( var index = 0; index < BlockCuts.Length - 1; index++ )
		{
			var start = BlockCuts[index];
			var length = BlockCuts[index + 1] - start;
			partitionedUnit.Process( send.AsSpan( start * 2, length * 2 ), partitioned.Slice( start * 2, length * 2 ), length );
		}
		for ( var index = 0; index < whole.Length; index++ )
			Assert.AreEqual( whole[index], partitioned[index], 0f, $"sample {index}" );
		Assert.AreEqual( 123.5f, guarded[0], 0f, "guarded prefix" );
		Assert.AreEqual( 123.5f, guarded[1], 0f, "guarded prefix" );
		Assert.AreEqual( -123.5f, guarded[guarded.Length - 2], 0f, "guarded suffix" );
		Assert.AreEqual( -123.5f, guarded[guarded.Length - 1], 0f, "guarded suffix" );
		for ( var index = 0; index < send.Length; index++ )
			Assert.AreEqual( originalSend[index], send[index], 0f, $"send sample {index}" );
		return whole;
	}

	/// <summary>Two settings that clamp to the same values must produce the same samples from the same synthetic input.</summary>
	private static void AssertSameOutput( ReverbSettings first, ReverbSettings second, string because )
	{
		var firstOutput = Render( first, SampleRate );
		var secondOutput = Render( second, SampleRate );
		for ( var index = 0; index < firstOutput.Length; index++ )
			Assert.AreEqual( firstOutput[index], secondOutput[index], 0f, $"{because} (sample {index})" );
	}

	[TestMethod]
	public void AnImpulseIsSilentBeforeTheDelayLinesAndThenDecays()
	{
		var output = Render( Preset( decay: 1f, decayHf: 1f, reverb: 600 ), SampleRate * 2 );

		Assert.IsTrue( output.Take( 1000 * 2 ).All( value => value == 0f ), "there is no zero-delay path and no early reflection is configured" );
		var early = Energy( output, 1000, 5000 );
		var late = Energy( output, SampleRate, SampleRate + 4410 );
		Assert.IsTrue( early > 0, "the impulse reaches the output through the delay network" );
		Assert.IsTrue( late < early * 0.1, $"a 1 s decay leaves much less energy after 1 s: {late} against {early}" );
	}

	[TestMethod]
	public void TheReverbDelayShiftsTheWholeTail()
	{
		const int delayFrames = 882; // 0.02 s at 44,100 Hz
		var immediate = Render( Preset( decay: 1f, decayHf: 1f, reverbDelay: 0f ), SampleRate * 2 );
		var delayed = Render( Preset( decay: 1f, decayHf: 1f, reverbDelay: 0.02f ), SampleRate * 2 );

		for ( var frame = 0; frame < delayFrames; frame++ )
		{
			Assert.AreEqual( 0f, delayed[frame * 2], 0f, $"the delayed tail has not started at frame {frame}" );
			Assert.AreEqual( 0f, delayed[frame * 2 + 1], 0f, $"the delayed tail has not started at frame {frame}" );
		}
		for ( var frame = delayFrames; frame < SampleRate * 2; frame++ )
		{
			Assert.AreEqual( immediate[(frame - delayFrames) * 2], delayed[frame * 2], 0f, $"left channel at frame {frame}" );
			Assert.AreEqual( immediate[(frame - delayFrames) * 2 + 1], delayed[frame * 2 + 1], 0f, $"right channel at frame {frame}" );
		}
	}

	[TestMethod]
	public void AZeroSendProducesNoOutputEvenAtTheLongestDecay()
	{
		var unit = new ReverbUnit( SampleRate );
		unit.Set( Preset( size: 100f, diffusion: 1f, decay: 20f, decayHf: 0.1f, reverb: 1000, reverbDelay: 0.1f ) );
		var frames = SampleRate * 2;
		var send = new float[frames * 2];
		var output = new float[frames * 2];

		unit.Process( send, output, frames );

		Assert.IsTrue( output.All( value => value == 0f ), "an all-zero send must not excite or self-oscillate the network" );
	}

	[TestMethod]
	public void SetNullStopsTheReturnWithoutTouchingTheOutput()
	{
		var unit = new ReverbUnit( SampleRate );
		unit.Set( Preset( decay: 4f, decayHf: 1f ) );
		var frames = 2000;
		var send = new float[frames * 2];
		send[0] = send[1] = 10000f;
		var first = new float[frames * 2];
		unit.Process( send, first, frames );
		Assert.IsTrue( Energy( first, 0, frames ) > 0, "the unit has a tail to stop" );

		unit.Set( null );
		var silent = new float[1000 * 2];
		var output = Enumerable.Repeat( 0.25f, silent.Length ).ToArray();
		unit.Process( silent, output, 1000 );

		Assert.IsTrue( output.All( value => value == 0.25f ), "with no preset Process adds nothing and does not clear the caller's buffer" );
	}

	[TestMethod]
	public void AReenabledPresetKeepsTheTailUnlessTheEnvironmentSizeChanges()
	{
		static ReverbUnit Primed( out float[] output )
		{
			var unit = new ReverbUnit( SampleRate );
			unit.Set( Preset( size: 7.5f, decay: 4f, decayHf: 1f ) );
			const int frames = 2000;
			var send = new float[frames * 2];
			send[0] = send[1] = 10000f;
			output = new float[frames * 2];
			unit.Process( send, output, frames );
			return unit;
		}

		var sameSize = Primed( out var sameFirst );
		Assert.IsTrue( Energy( sameFirst, 1000, 2000 ) > 0, "the tail is still present before the switch" );
		sameSize.Set( null );
		sameSize.Process( new float[1000 * 2], new float[1000 * 2], 1000 );
		sameSize.Set( Preset( size: 7.5f, decay: 4f, decayHf: 1f ) );
		var resumed = new float[4000 * 2];
		sameSize.Process( new float[4000 * 2], resumed, 4000 );
		Assert.IsTrue( resumed.Any( value => value != 0f ), "re-enabling at the same size continues the held tail" );

		var changedSize = Primed( out var changedFirst );
		Assert.IsTrue( Energy( changedFirst, 1000, 2000 ) > 0, "the second unit also has a tail before the switch" );
		changedSize.Set( null );
		changedSize.Process( new float[1000 * 2], new float[1000 * 2], 1000 );
		changedSize.Set( Preset( size: 30f, decay: 4f, decayHf: 1f ) );
		var dropped = new float[4000 * 2];
		changedSize.Process( new float[4000 * 2], dropped, 4000 );
		Assert.IsTrue( dropped.All( value => value == 0f ), "a size change rebuilds the delay lines and drops the old tail" );
	}

	[TestMethod]
	public void AnEffectiveSizeChangeDropsHeldPreDelay()
	{
		AssertZeroInputMatchesFreshAfterSwitch(
			Preset( size: 7.5f, decay: 1f, decayHf: 1f, reverb: 600, reverbDelay: 0.1f ),
			Preset( size: 30f, decay: 1f, decayHf: 1f, reverb: 600, reverbDelay: 0.1f ),
			10000 );
	}

	[TestMethod]
	public void AnEffectiveSizeChangeDropsHeldEarlyReflections()
	{
		AssertZeroInputMatchesFreshAfterSwitch(
			Preset( size: 7.5f, decay: 1f, decayHf: 1f, reverb: 600, reflections: 500, reflectionsDelay: 0.05f ),
			Preset( size: 30f, decay: 1f, decayHf: 1f, reverb: 600, reflections: 500, reflectionsDelay: 0.05f ),
			10000 );
	}

	[TestMethod]
	public void AnUnchangedSizeKeepsTheHeldTail()
	{
		var settings = Preset( size: 7.5f, decay: 1f, decayHf: 1f, reverb: 600, reverbDelay: 0.1f );
		AssertSwitchKeepsTail( settings, settings );
	}

	[TestMethod]
	public void ARawSizeChangeThatKeepsTheEffectiveScaleKeepsTheHeldTail()
	{
		var source = Preset( size: 100f, decay: 1f, decayHf: 1f, reverb: 600, reverbDelay: 0.1f );
		AssertSwitchKeepsTail( source, source with { Size = 1000f } );
	}

	[TestMethod]
	public void AUnitStillProcessesNewInputAfterAnEffectiveSizeChange()
	{
		var source = Preset( size: 7.5f, decay: 1f, decayHf: 1f, reverb: 600, reverbDelay: 0.1f );
		var target = Preset( size: 30f, decay: 1f, decayHf: 1f, reverb: 600, reverbDelay: 0.1f );
		var unit = ImpulseUnit( source );
		unit.Set( target );

		var send = new float[8000 * 2];
		send[0] = send[1] = 10000f;
		var after = new float[8000 * 2];
		unit.Process( send, after, 8000 );

		var fresh = new ReverbUnit( SampleRate );
		fresh.Set( target );
		var expected = new float[8000 * 2];
		fresh.Process( send, expected, 8000 );

		for ( var index = 0; index < after.Length; index++ )
			Assert.AreEqual( expected[index], after[index], 0f, $"new-input sample {index}" );
		Assert.IsTrue( after.Any( value => value != 0f ), "the reconfigured unit processes new input" );
	}

	[TestMethod]
	public void TheEnvironmentSizeIsClampedToItsConfiguredRange()
	{
		AssertSameOutput( Preset( size: 0.1f ), Preset( size: 1.5f ), "sizes below 1.5 m behave as 1.5 m" );
		AssertSameOutput( Preset( size: 100f ), Preset( size: 1000f ), "sizes above 100 m behave as 100 m" );
	}

	[TestMethod]
	public void TheDecayTimeIsClampedToItsConfiguredRange()
	{
		AssertSameOutput( Preset( decay: 0.01f ), Preset( decay: 0.1f ), "decay times below 0.1 s behave as 0.1 s" );
		AssertSameOutput( Preset( decay: 20f ), Preset( decay: 100f ), "decay times above 20 s behave as 20 s" );
	}

	[TestMethod]
	public void TheHighFrequencyDecayRatioIsClampedAndSaturatesAtNoDamping()
	{
		AssertSameOutput( Preset( decayHf: 0.01f ), Preset( decayHf: 0.1f ), "ratios below 0.1 behave as 0.1" );
		AssertSameOutput( Preset( decayHf: 1f ), Preset( decayHf: 10f ), "ratios at or above 1 use no damping" );
	}

	[TestMethod]
	public void TheDiffusionIsClampedToItsConfiguredRange()
	{
		AssertSameOutput( Preset( diffusion: -1f ), Preset( diffusion: 0f ), "diffusion below 0 behaves as 0" );
		AssertSameOutput( Preset( diffusion: 1f ), Preset( diffusion: 2f ), "diffusion above 1 behaves as 1" );
	}

	[TestMethod]
	public void TheReverbDelayIsClampedToItsConfiguredRange()
	{
		AssertSameOutput( Preset( reverbDelay: -1f ), Preset( reverbDelay: 0f ), "reverb delays below 0 behave as 0" );
		AssertSameOutput( Preset( reverbDelay: 0.1f ), Preset( reverbDelay: 1f ), "reverb delays above 0.1 s behave as 0.1 s" );
	}

	[TestMethod]
	public void ReflectionsRequireBothTheirDelayAndLevelThresholds()
	{
		const int frames = 500; // only early taps can appear before the comb lines contribute
		var belowDelay = Render( Preset( reverb: -10000, reflections: 500, reflectionsDelay: 0.0019f ), frames );
		var atDelay = Render( Preset( reverb: -10000, reflections: 500, reflectionsDelay: 0.002f ), frames );
		Assert.IsTrue( belowDelay.All( value => value == 0f ), "no early tap below 2 ms" );
		Assert.IsTrue( atDelay.Any( value => value != 0f ), "the first early tap exists at 2 ms" );

		var atLevelFloor = Render( Preset( reverb: -10000, reflections: -10000, reflectionsDelay: 0.002f ), frames );
		var aboveLevelFloor = Render( Preset( reverb: -10000, reflections: -9999, reflectionsDelay: 0.002f ), frames );
		Assert.IsTrue( atLevelFloor.All( value => value == 0f ), "-10000 mB is not an enabled reflection level" );
		Assert.IsTrue( aboveLevelFloor.Any( value => value != 0f ), "-9999 mB is above the reflection level threshold" );
	}

	[TestMethod]
	public void TheReflectionDelayIsClampedAtItsUpperBound()
	{
		AssertSameOutput( Preset( reflections: 500, reflectionsDelay: 0.3f ), Preset( reflections: 500, reflectionsDelay: 1f ), "reflection delays above 0.3 s behave as 0.3 s" );
	}

	[TestMethod]
	public void TheCombinedReverbLevelHasAMinus10000MillibelFloor()
	{
		AssertSameOutput( Preset( room: -10000, reverb: -10000 ), Preset( room: -20000, reverb: 0 ), "sums at or below -10000 mB are floored" );
		AssertSameOutput( Preset( room: -15000, reverb: -5000 ), Preset( room: -10000, reverb: -10000 ), "the floor applies to the room-plus-reverb sum" );
	}

	[TestMethod]
	public void WholeAndPartitionedBlocksMatchThroughAStereoSyntheticImpulse()
	{
		const int frames = 8192;
		var send = new float[frames * 2];
		send[0] = send[1] = 10000f;
		var output = AssertWholeMatchesPartitioned(
			Preset( size: 7.5f, room: 0, decay: 4f, decayHf: 1f, reflections: 0, reflectionsDelay: 0f, reverb: 0, reverbDelay: 0f ),
			send,
			frames );

		// Mono impulse 10000 * (0.5 / 8) = 625; four fresh allpasses negate four times, and wet gain 0.25 gives 156.25.
		Assert.AreEqual( 156.25f, output[1116 * 2], 0f, "first left output at the shortest left comb" );
		Assert.AreEqual( 156.25f, output[1139 * 2 + 1], 0f, "first right output at the shortest right comb" );
		Assert.AreEqual( 0f, output[1116 * 2 + 1], 0f, "the right side has not arrived at frame 1116" );
		Assert.AreEqual( 0f, output[1139 * 2], 0f, "the left side is silent at frame 1139" );
	}

	[TestMethod]
	public void WholeAndPartitionedBlocksMatchThroughAZeroSendTail()
	{
		const int frames = 8192;
		var send = new float[frames * 2];
		for ( var frame = 0; frame < 256; frame++ )
		{
			send[frame * 2] = (frame % 2 == 0) ? 8000f : -8000f;
			send[frame * 2 + 1] = ((frame / 2) % 2 == 0) ? 6000f : -6000f;
		}
		var output = AssertWholeMatchesPartitioned(
			Preset( size: 7.5f, room: 0, decay: 4f, decayHf: 1f, reflections: 0, reflectionsDelay: 0f, reverb: 0, reverbDelay: 0f ),
			send,
			frames );

		Assert.IsTrue( Energy( output, 256, frames ) > 0, "the zero send after frame 255 leaves a genuine nonzero tail" );
	}
}
