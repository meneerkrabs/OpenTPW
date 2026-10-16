using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>The lobby's camera steps once per 60 Hz reference frame ([EXT:frame-driven-at-60hz]), as the original steps it once per rendered frame (0x5d5c50 -> 0x5e0470).</summary>
[TestClass, DoNotParallelize]
public class LobbyFrameTimingTests
{
	[TestMethod]
	public void TheLobbyCameraGlidesAndSpinsAtTheReferenceRateAtAnyDisplayRate()
	{
		var delta = Time.Delta;
		var saved = (LobbyCameraMode.Radius, LobbyCameraMode.VerticalOffset, LobbyCameraMode.SpinSpeed, LobbyCameraMode.IslandFieldOfView, LobbyCameraMode.Target);
		try
		{
			LobbyCameraMode.Radius = 70;
			LobbyCameraMode.VerticalOffset = 20;
			LobbyCameraMode.SpinSpeed = 0.02f;
			LobbyCameraMode.Target = new Vector3( 400, 400, 12.5f );
			var results = new List<(float Angle, Vector3 Eye)>();
			foreach ( var rate in new[] { 30, 60, 144 } )
			{
				Time.Delta = 1f / rate;
				var camera = new LobbyCameraMode();
				// a new island is selected: the eye and the target glide to it a share per frame
				LobbyCameraMode.Target = new Vector3( 500, 300, 12.5f );
				for ( var frame = 0; frame < rate; frame++ )
					camera.Update();
				results.Add( (camera.Angle, camera.Eye) );
				LobbyCameraMode.Target = new Vector3( 400, 400, 12.5f );
			}
			// one second is 60 steps of a sixth of a tenth of a second each: the angle gains 0.02 x 10
			foreach ( var (angle, _) in results )
				Assert.AreEqual( 0.2f, angle, 0.01f );
			for ( var index = 1; index < results.Count; index++ )
				Assert.IsTrue( (results[index].Eye - results[0].Eye).Length < 0.5f, $"the glide after a second agrees across display rates: {results[index].Eye} against {results[0].Eye}" );
		}
		finally
		{
			Time.Delta = delta;
			(LobbyCameraMode.Radius, LobbyCameraMode.VerticalOffset, LobbyCameraMode.SpinSpeed, LobbyCameraMode.IslandFieldOfView, LobbyCameraMode.Target) = saved;
		}
	}
}
