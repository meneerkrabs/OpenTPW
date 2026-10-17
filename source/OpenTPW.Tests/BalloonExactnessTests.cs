using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The balloon's colour, the kid set, the bob phase and the burst's fade as tp.exe draws them (0x00541FD0, 0x004FA030, script 0x0074F4C0). Rules only.</summary>
[TestClass]
public class BalloonExactnessTests
{
	[TestMethod]
	public void ABalloonsColourAndAKidsSetFollowFromTheGuestsIdAlone()
	{
		var rng = new WorldRng( 0 );
		for ( var id = 0; id < 200; id++ )
		{
			rng.Reseed( (uint)id );
			var kid = (int)((rng.NextWord() >> 2) % 8);
			Assert.AreEqual( kid, GuestAppearance.KidSet( id, 8 ), $"guest {id}: the constructor's draw after the reseed (0x004FB19E)" );
			rng.Reseed( (uint)id );
			rng.NextWord();
			Assert.AreEqual( (int)((rng.NextWord() >> 2) % 4), GuestAppearance.BalloonColour( id, 4 ), $"guest {id}: the second draw after the reseed (0x00541FD0)" );
		}
		Assert.AreEqual( 0, GuestAppearance.BalloonColour( 5, 0 ) );
		Assert.IsTrue( Enumerable.Range( 0, 200 ).Select( id => GuestAppearance.KidSet( id, 8 ) ).Distinct().Count() == 8, "every kid set gets guests" );
		Assert.IsTrue( Enumerable.Range( 0, 200 ).Select( id => GuestAppearance.BalloonColour( id, 4 ) ).Distinct().Count() == 4, "every colour appears" );
	}

	[TestMethod]
	public void TheBobPhaseStepsOnTheEleventhPlacement()
	{
		var (phase, accumulator) = (0, 0f);
		var stepsAt = new System.Collections.Generic.List<int>();
		for ( var placement = 1; placement <= 33; placement++ )
		{
			var before = phase;
			(phase, accumulator) = BalloonRenderer.AdvanceBob( phase, accumulator );
			if ( phase != before )
				stepsAt.Add( placement );
		}
		CollectionAssert.AreEqual( new[] { 11, 22, 33 }, stepsAt, "ten placements add 0.1 each (the float sum reaches 1.0), the next steps the phase" );
		Assert.AreEqual( 3, phase );
		Assert.AreEqual( 0, BalloonRenderer.AdvanceBob( 19, 1f ).Phase, "the phase wraps at the 20 table entries" );
	}

	[TestMethod]
	public void TheBurstFadesFromAlpha250ByTwentyAStepForThirteenSteps()
	{
		Assert.AreEqual( 250, BalloonRenderer.BurstAlpha( 0 ) );
		Assert.AreEqual( 230, BalloonRenderer.BurstAlpha( 1 ) );
		Assert.AreEqual( 10, BalloonRenderer.BurstAlpha( 12 ), "the thirteenth and last frame" );
		Assert.IsNull( BalloonRenderer.BurstAlpha( 13 ), "the register is -10 then: the loop ends and the sprite goes" );
		Assert.IsNull( BalloonRenderer.BurstAlpha( -1 ) );
		Assert.AreEqual( 0.062f, BalloonRenderer.ScriptStepSeconds, 1e-6f, "the 62 ms interval a sprite is created with" );
	}
}
