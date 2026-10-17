using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// How the original places its world sprites (tp.exe 0x005890E0 quad placement, 0x00578BE0 projection, 0x004D93B0 litter, 0x004DF8F0 upgrade balloon): the size of a sprite pixel, the flat litter
/// quads and the balloon over an upgraded ride. Rules only; the data-backed checks are inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
public class SpriteWorldTests
{
	private static ModelHeightfield Flat( float height = 0f ) => new( 4, 4, 10, 10, 0, 0, Array.Empty<uint>(), Enumerable.Repeat( height, 25 ).ToArray(), new uint[16] );

	[TestMethod]
	public void ASpritePixelIsTwentyOver128TimesTheProjectionsClipToViewFactor()
	{
		// the projection's half field of view is 45 degrees (90 degrees pushed at 0x0042AA49), so a clip-space extent is 1 / cos(45 degrees) view units
		Assert.AreEqual( MathF.Sqrt( 2f ), SpriteScale.ClipToView, 1e-5f );
		Assert.AreEqual( 20f / 128f * MathF.Sqrt( 2f ), SpriteScale.BillboardPixel, 1e-5f );
		Assert.AreEqual( 0.221f, SpriteScale.BillboardPixel, 1e-3f, "a pixel is 0.221 of the original's ten-per-cell units" );
		Assert.AreEqual( SpriteScale.BillboardPixel * ObjectPlacement.ModelScale, SpriteScale.EngineUnitsPerPixel, 1e-6f );
		Assert.AreEqual( SpriteScale.EngineUnitsPerPixel, GuestRenderer.UnitsPerPixel, 0f, "the kids, staff, balloons and thoughts all draw with it" );
		Assert.AreEqual( 0.0442f, GuestRenderer.UnitsPerPixel, 1e-4f, "the 0.045 fitted by eye was within 2 percent" );
	}

	[TestMethod]
	public void AFlatQuadSpansTheFramesOwnExtentsWithTheDisplayAspectInX()
	{
		// a frame 10 x 7 with the hotspot at (0, -8): x from 0 to 10 / 128 x 20 x 0.75, z from -8 / 128 x 20 to -1 / 128 x 20 (litter kind 1)
		var (left, right, top, bottom) = SpriteScale.FlatEdges( new GuestAtlasFrame( 0, 0, 10, 7, 0, -8 ) );
		Assert.AreEqual( 0f, left, 1e-6f );
		Assert.AreEqual( 10f / 128f * 20f * 0.75f, right, 1e-5f );
		Assert.AreEqual( -8f / 128f * 20f, top, 1e-5f );
		Assert.AreEqual( -1f / 128f * 20f, bottom, 1e-5f );
		Assert.IsTrue( top < bottom, "the frame's top edge is the lower map y: the picture reads upright with the map's rows running down the screen" );
		Assert.AreEqual( SpriteScale.FlatPixelX, 20f / 128f * 0.75f, 1e-6f );
		Assert.AreEqual( SpriteScale.FlatPixelZ, 20f / 128f, 1e-6f );
	}

	[TestMethod]
	public void LitterKindsTakeTheSlotsOfTheOriginalsScripts()
	{
		// the jump table 0x004D95D4: kinds 1-5 the scripts 0x0074F050 + 0x28 x (kind - 1) (slots 0-4), kind 6 and any other kind the "Dodgy litter type" script (slot 5), vomit slot 6, stinkbomb slot 7
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, new[] { LitterType.None + 1, (LitterType)2, (LitterType)3, (LitterType)4, (LitterType)5, LitterType.Dodgy, LitterType.Vomit, LitterType.Stinkbomb }
			.Select( LitterRenderer.Slot ).ToArray() );
		Assert.AreEqual( 5, LitterRenderer.Slot( (LitterType)9 ), "an unknown kind falls to the dodgy script" );
	}

	[TestMethod]
	public void LitterLiesAtTheCellsDrawnOffsetInTenthsOfACell()
	{
		for ( var y = 0; y < 5; y++ )
			for ( var x = 0; x < 5; x++ )
			{
				var (offsetX, offsetZ) = ParkLitter.SpriteOffset( x, y );
				Assert.IsTrue( offsetX is >= 1 and <= 9 && offsetZ is >= 1 and <= 9 );
				Assert.AreEqual( (x * 10 + offsetX, y * 10 + offsetZ), LitterRenderer.Anchor( x, y ) );
			}
		Assert.AreEqual( LitterRenderer.Anchor( 3, 4 ), LitterRenderer.Anchor( 3, 4 ), "the position depends on the cell alone" );
	}

	[TestMethod]
	public void LitterQuadLiesFlatHalfAUnitAboveTheGround()
	{
		var field = Flat( 6f );
		var frame = new GuestAtlasFrame( 0, 0, 17, 14, -6, -16 );
		var (topLeft, topRight, bottomRight, bottomLeft) = LitterRenderer.Corners( field, 2, 3, frame );
		var height = (6f + SpriteScale.FlatLift) * ObjectPlacement.ModelScale;
		foreach ( var corner in new[] { topLeft, topRight, bottomRight, bottomLeft } )
			Assert.AreEqual( height, corner.Z, 1e-5f, "every corner at the sprite's one height: flat" );
		var (left, right, top, bottom) = SpriteScale.FlatEdges( frame );
		Assert.AreEqual( (right - left) * ObjectPlacement.ModelScale, topRight.X - topLeft.X, 1e-5f );
		Assert.AreEqual( (bottom - top) * ObjectPlacement.ModelScale, bottomLeft.Y - topLeft.Y, 1e-5f );
		Assert.AreEqual( topLeft.X, bottomLeft.X, 1e-6f );
		Assert.AreEqual( topLeft.Y, topRight.Y, 1e-6f );
		var (anchorX, anchorZ) = LitterRenderer.Anchor( 2, 3 );
		var origin = OriginalParkPlacement.GetOrigin( field );
		Assert.AreEqual( (anchorX + left) * ObjectPlacement.ModelScale - origin.X, topLeft.X, 1e-4f );
		Assert.AreEqual( (anchorZ + top) * ObjectPlacement.ModelScale - origin.Y, topLeft.Y, 1e-4f );
	}

	[TestMethod]
	public void TheUpgradeBalloonHangsForLevelsOneAndTwoOnly()
	{
		Assert.IsFalse( UpgradeBalloonRenderer.ShowsFor( 0 ) );
		Assert.IsTrue( UpgradeBalloonRenderer.ShowsFor( 1 ) );
		Assert.IsTrue( UpgradeBalloonRenderer.ShowsFor( 2 ) );
		Assert.IsFalse( UpgradeBalloonRenderer.ShowsFor( 3 ), "the completion creates the sprite for the new level 1 or 2 only (0x004DFA2B, 0x004DFA3C)" );
		Assert.AreEqual( (4.5f, 7.5f), UpgradeBalloonRenderer.Anchor( (4, 7) ), "the middle of the front cell: column + 0.5, row + 0.5" );
	}

	[TestMethod]
	public void AnUpgradeReplacesTheRidesOldBalloonAndTheRidesRemovalTakesIt()
	{
		var table = new UpgradeBalloonRenderer.Table();
		var alive = true;
		table.Completed( 7, 1, (4, 7), () => alive );
		Assert.IsTrue( table.Contains( 7 ) );
		Assert.AreEqual( 1, table.Count );
		table.Completed( 7, 2, (5, 7), () => alive );
		Assert.AreEqual( 1, table.Count, "the second upgrade deletes the first balloon (0x004DF96C) and hangs its own" );
		Assert.AreEqual( 5.5f, table.Markers.Single().X );
		table.Completed( 7, 3, (5, 7), () => alive );
		Assert.AreEqual( 0, table.Count, "the third level hangs none, and the old one went" );
		table.Completed( 8, 1, (1, 1), () => alive );
		table.Completed( 9, 2, (2, 2), () => false );
		table.Prune();
		Assert.AreEqual( 1, table.Count, "a removed ride takes its balloon (0x004DD293)" );
		table.Clear();
		Assert.AreEqual( 0, table.Count );
	}
}
