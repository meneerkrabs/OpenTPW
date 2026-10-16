using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// AUDIO-033: the distance law of a positioned voice is 1 inside the minimum distance 2, ((far - d) / (far - 2))^0.2 up to the far end (the range, at
/// least 2.1) and 0 beyond; 0.2 is the rolloff the sound system object returns when the QMixer driver provides the 3D (tp.exe 0x6b9bb0, 0x6b9006, 0x5d1400).
/// </summary>
[TestClass]
public class AudioRolloffTests
{
	[TestMethod]
	public void TheExponentIsOneFifth()
	{
		Assert.AreEqual( 0.2f, AudioListener.Rolloff );
		// tp.exe 0x70a624 holds 0.2f bit for bit
		Assert.AreEqual( 0x3e4ccccdu, BitConverter.SingleToUInt32Bits( AudioListener.Rolloff ) );
	}

	[TestMethod]
	public void TheGainFallsAsTheFractionToTheFifthRoot()
	{
		Assert.AreEqual( 1f, AudioListener.RangeGain( 100, 2 ) );
		Assert.AreEqual( 1f, AudioListener.RangeGain( 100, 0 ) );
		Assert.AreEqual( MathF.Pow( 0.5f, 0.2f ), AudioListener.RangeGain( 100, 51 ), 1e-6f, "half way between the minimum 2 and the far end 100" );
		Assert.AreEqual( 0.8705506f, AudioListener.RangeGain( 100, 51 ), 1e-5f );
		Assert.AreEqual( MathF.Pow( 0.1f, 0.2f ), AudioListener.RangeGain( 100, 90.2f ), 1e-5f, "nine tenths of the way out" );
		Assert.AreEqual( 0f, AudioListener.RangeGain( 100, 100 ) );
		Assert.AreEqual( 0f, AudioListener.RangeGain( 100, 150 ) );
	}

	[TestMethod]
	public void ARangeOfTwoOrLessEndsAtTwoPointOne()
	{
		Assert.AreEqual( 1f, AudioListener.RangeGain( 2, 2 ) );
		Assert.AreEqual( MathF.Pow( 0.5f, 0.2f ), AudioListener.RangeGain( 2, 2.05f ), 1e-5f );
		Assert.AreEqual( 0f, AudioListener.RangeGain( 2, 2.1f ) );
	}

	[TestMethod]
	public void ARangeOfZeroIsAlwaysFullGain()
	{
		Assert.AreEqual( 1f, AudioListener.RangeGain( 0, 500 ) );
	}
}
