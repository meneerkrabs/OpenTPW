using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The umbrellas' drawing rules (docs/UMBRELLAS.md, phase U3): which states show, the bubble height, the pull, the hand lookup and the shipped tables.</summary>
// [EXT:umbrellas-draw] presentation only
[TestClass]
public class UmbrellaDrawingTests
{
	private static string Folder
	{
		get
		{
			for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
			{
				var candidate = Path.Combine( directory.FullName, "content", "umbrellas" );
				if ( File.Exists( Path.Combine( candidate, "anchors-original.json" ) ) )
					return candidate;
			}
			throw new FileNotFoundException( "content/umbrellas not found" );
		}
	}

	[TestMethod]
	public void OnlyOpenAndClosingUmbrellasAreDrawnAndTheBubbleRises()
	{
		for ( var state = 0; state <= 4; state++ )
			Assert.AreEqual( state is 4 or 1, UmbrellaRenderer.ShowsState( state ), $"state {state}" );
		Assert.AreEqual( 6.0f, UmbrellaRenderer.BubbleHeight( true ) );
		Assert.AreEqual( 2.5f, UmbrellaRenderer.BubbleHeight( false ) );
		Assert.AreEqual( ThoughtRenderer.Lift, UmbrellaRenderer.BubbleHeight( false ) );
	}

	[TestMethod]
	public void ThePullIsAShareOfTheSightLineAndCoversMoreThanTheGuestsOwn()
	{
		var eye = new System.Numerics.Vector3( 0, 0, 0 );
		var forward = new System.Numerics.Vector3( 0, 1, 0 );
		var point = new System.Numerics.Vector3( 1, 100, 2 );
		var pulled = UmbrellaRenderer.PullToEye( point, eye, forward, UmbrellaRenderer.CameraPull );
		Assert.AreEqual( 0.9f * point, pulled );
		Assert.AreEqual( new System.Numerics.Vector3( 0, -5, 0 ), UmbrellaRenderer.PullToEye( new System.Numerics.Vector3( 0, -5, 0 ), eye, forward, 0.1f ), "a point behind the eye stays" );
		var guestShare = SpriteScale.PulledDepthShare( 100f / ObjectPlacement.ModelScale );
		Assert.IsTrue( 1f - UmbrellaRenderer.CameraPull < guestShare, "the umbrella is nearer the camera than the guest it covers" );
	}

	[TestMethod]
	public void AHandIsReadPerSlotDirectionAndStepAndMirroredWithX()
	{
		const string json = """{"schema":1,"pixelsPerUnit":128,"sets":{"SPR_X":{"stand":{"slot":0,"frames":[[[4,-8]],[[3,-7]],[[1,-6]],[[-3,-7]],[[-4,-8]]]},"walk":{"slot":1,"frames":[[[2,-10],[4,-6]],[[1,-1],[1,-1]],[[1,-1],[1,-1]],[[1,-1],[1,-1]],[[1,-1],[1,-1]]]}}}}""";
		var anchors = UmbrellaAnchors.Parse( json, 128 );
		Assert.AreEqual( (4f, -8f), anchors.Hand( "SPR_X", 128, 0, 0, 0, false, false ) );
		Assert.AreEqual( (-3f, -7f), anchors.Hand( "SPR_X", 128, 0, 3, 0, false, false ) );
		Assert.AreEqual( (3f, -7f), anchors.Hand( "SPR_X", 128, 0, 3, 0, true, false ) );
		Assert.AreEqual( (4f, -6f), anchors.Hand( "SPR_X", 128, 1, 0, 3, false, false ), "step 3 of 2 wraps to 1" );
		Assert.AreEqual( (3f, -8f), anchors.Hand( "SPR_X", 128, 1, 0, 0, false, true ), "steady: the mean of the steps" );
		Assert.AreEqual( anchors.Hand( "SPR_X", 128, 1, 0, 0, false, false ), anchors.Hand( "SPR_X", 128, 2, 0, 0, false, false ), "running follows walking" );
		Assert.AreEqual( anchors.Hand( "SPR_X", 128, 0, 1, 0, false, false ), anchors.Hand( "SPR_X", 128, 14, 1, 0, false, false ), "no wait table: standing" );
		Assert.IsNull( anchors.Hand( "SPR_X", 256, 0, 0, 0, false, false ), "another resolution is another table" );
		Assert.IsNull( anchors.Hand( "SPR_Y", 128, 0, 0, 0, false, false ) );
	}

	[TestMethod]
	public void TheShippedTablesCoverEveryKidAndCostumeSetAtItsOwnResolution()
	{
		var anchors = UmbrellaAnchors.Load( Folder );
		Assert.AreEqual( 8 + 8 + 4, anchors.Count );
		foreach ( var name in new[] { "SPR_BE", "SPR_BI", "SPR_CH", "SPR_FR", "SPR_KI", "SPR_SA", "SPR_SU", "SPR_TA" } )
		{
			Assert.IsNotNull( anchors.Hand( name, 256, 1, 2, 5, false, false ), name + " 2x" );
			Assert.IsNotNull( anchors.Hand( name, 128, 14, 4, 1, true, true ), name + " 1x" );
		}
		foreach ( var name in new[] { "SPR_TI", "SPR_BK", "SPR_HK", "SPR_SK" } )
			Assert.IsNotNull( anchors.Hand( name, 128, 0, 0, 0, false, false ), name );
		Assert.IsNull( anchors.Hand( "SPR_TI", 256, 0, 0, 0, false, false ) );
	}
}
