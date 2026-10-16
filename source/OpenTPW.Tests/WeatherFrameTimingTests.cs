using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>The rain and snow redraw their random jitter once per 60 Hz reference frame ([EXT:frame-driven-at-60hz]).</summary>
[TestClass, DoNotParallelize]
public class WeatherFrameTimingTests
{
	[TestMethod]
	public void TheSnowJitterAndTheBoltFlickerAreRedrawnOnlyOnADrawFrame()
	{
		// [BIN:TPW-EXE:0x57f490] a draw of the original is a rendered frame; OpenTPW redraws per 60 Hz reference frame
		var state = new WeatherVisualState();
		var source = new Source();
		var eye = WeatherCamera.Looking( System.Numerics.Vector3.Zero );
		var bolt = state.AddBolt( new System.Numerics.Vector3( 0, 300, 0 ), 2f );
		state.Update( 0.01f, source, eye, drawFrame: true );
		var jitter = Enumerable.Range( 0, 64 ).Select( state.JitterOf ).ToArray();
		var flicker = bolt.Flicker;
		state.Update( 0.01f, source, eye, drawFrame: false );
		CollectionAssert.AreEqual( jitter, Enumerable.Range( 0, 64 ).Select( state.JitterOf ).ToArray(), "no draw, no new jitter" );
		Assert.AreEqual( flicker, bolt.Flicker );
		Assert.IsTrue( bolt.Age > 0.019f, "the bolt still ages by the real time" );
		var changed = false;
		for ( var frame = 0; frame < 5 && !changed; frame++ )
		{
			state.Update( 0.01f, source, eye, drawFrame: true );
			changed = !jitter.SequenceEqual( Enumerable.Range( 0, 64 ).Select( state.JitterOf ) );
		}
		Assert.IsTrue( changed, "a draw frame redraws the jitter" );
	}

	private sealed class Source : IWeatherVisualSource
	{
		public int DropCount => 64;
		public bool Snow => true;
		public float BoltLifetimeSeconds => 2f;
		public IReadOnlyList<WeatherStrike> TakeStrikes() => System.Array.Empty<WeatherStrike>();
	}
}
