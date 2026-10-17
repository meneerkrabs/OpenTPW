using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>How the PC player presents movies: the volume curve and the 16-bit display packing (tp.exe, docs/TGQ-MOVIES.md).</summary>
[TestClass]
public class MoviePresentationTests
{
	[DataTestMethod]
	[DataRow( 100, 1023, 0 )]
	[DataRow( 75, 767, -2503 )]
	[DataRow( 50, 511, -5005 )]
	[DataRow( 1, 10, -9903 )]
	[DataRow( 0, 0, -10000 )]
	public void VolumePercentageBecomesDeviceVolumeThenDirectSoundAttenuation( int percent, int device, int hundredthsDb )
	{
		Assert.AreEqual( device, MovieVolumeCurve.DeviceVolume( percent, true ) );
		Assert.AreEqual( hundredthsDb, MovieVolumeCurve.HundredthsDb( device ) );
	}

	[TestMethod]
	public void SwitchOffPlaysTheDeviceMinimumAndSilence()
	{
		Assert.AreEqual( 0, MovieVolumeCurve.DeviceVolume( 100, false ) );
		Assert.AreEqual( 0f, MovieVolumeCurve.Gain( 100, false ) );
		Assert.AreEqual( 0f, MovieVolumeCurve.Gain( 0, true ), "−10000 is DirectSound's silence" );
	}

	[TestMethod]
	public void FullVolumeIsUnattenuatedAndTheCurveFollowsDecibels()
	{
		Assert.AreEqual( 1f, MovieVolumeCurve.Gain( 100, true ) );
		Assert.AreEqual( MathF.Pow( 10f, -2503 / 2000f ), MovieVolumeCurve.Gain( 75, true ), 1e-6f );
		Assert.AreEqual( 0.0562f, MovieVolumeCurve.Gain( 75, true ), 1e-3f, "75 % is about −25 dB" );
		var previous = -1f;
		for ( var percent = 0; percent <= 100; percent++ )
		{
			var gain = MovieVolumeCurve.Gain( percent, true );
			Assert.IsTrue( gain >= previous, "the curve never falls as the slider rises" );
			previous = gain;
		}
	}

	[TestMethod]
	public void TheOptionsMovieGainUsesTheCurve()
	{
		var options = new GameOptions { MovieVolume = 75, MovieOn = true };
		Assert.AreEqual( MovieVolumeCurve.Gain( 75, true ), options.MovieGain );
		options.MovieOn = false;
		Assert.AreEqual( 0f, options.MovieGain );
	}
}
