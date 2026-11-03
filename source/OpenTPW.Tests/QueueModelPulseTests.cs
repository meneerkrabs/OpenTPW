using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>PATH-015: the queue dead end's colour pulse (tp.exe 0x469840, 0x469580 and the loader flag 0x100000 of the two <c>quedead</c> table rows of 0x522900).</summary>
[TestClass]
public class QueueModelPulseTests
{
	[TestMethod]
	public void OnlyTheTwoDeadEndEntriesPulse()
	{
		for ( var key = 0; key < QueueModels.Names.Count; key++ )
			Assert.AreEqual( QueueModels.Names[key] == "quedead", QueueModelPulse.Pulses( key ), $"{key} {QueueModels.Names[key]}" );
		Assert.IsFalse( QueueModelPulse.Pulses( -1 ) );
		Assert.IsFalse( QueueModelPulse.Pulses( 8 ) );
	}

	[TestMethod]
	public void TheFirstFrameIsRedAndThePhaseRisesTwoPerSecond()
	{
		var pulse = new QueueModelPulse();
		pulse.Step( 0 );
		Assert.AreEqual( (0, 0f), (pulse.Level, pulse.Phase), "the phase starts at 0: green and blue 0, the colour 0xFFFF0000" );
		pulse.Step( 250 );
		Assert.AreEqual( 0, pulse.Level, "the colour comes from the phase before the step moves on" );
		Assert.AreEqual( 0.5f, pulse.Phase );
		pulse.Step( 0 );
		Assert.AreEqual( 127, pulse.Level, "ftol truncates 0.5 * 255" );
	}

	[TestMethod]
	public void ThePhaseFoldsAtOneAndWrapsAtTwo()
	{
		var pulse = new QueueModelPulse();
		pulse.Step( 500 );
		pulse.Step( 0 );
		Assert.AreEqual( (255, 1f), (pulse.Level, pulse.Phase), "the top of the pulse is white" );
		pulse.Step( 250 );
		pulse.Step( 0 );
		Assert.AreEqual( (127, 1.5f), (pulse.Level, pulse.Phase), "past 1 the level falls: 2 - 1.5" );
		pulse.Step( 250 );
		Assert.AreEqual( 0f, pulse.Phase, "fmod by 2" );
	}

	[TestMethod]
	public void APausedClockLeavesThePulseWhereItStands()
	{
		var pulse = new QueueModelPulse();
		pulse.Step( 100 );
		var phase = pulse.Phase;
		for ( var frame = 0; frame < 5; frame++ )
			pulse.Step( 0 );
		Assert.AreEqual( phase, pulse.Phase );
	}
}
