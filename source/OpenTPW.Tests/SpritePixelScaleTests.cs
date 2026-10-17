using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// The frame's two pixels-per-unit fields (<see cref="SpriteFrame.PixelsPerUnitX"/>/<see cref="SpriteFrame.PixelsPerUnitY"/>, [BIN:TPW-EXE:0x005890E0]): every sprite quad scales its pixels by 128 / A across and 128 / B
/// down, and with the shipped data (128 in every frame) the quads are what the fixed scale gave before, bit for bit. No original data needed.
/// </summary>
[TestClass]
public class SpritePixelScaleTests
{
	/// <summary>The quad the renderers built before the fields were honoured: one fixed scale for both axes.</summary>
	private static (float X0, float X1, float Top, float Bottom) FixedScaleQuad( GuestAtlasFrame frame, bool mirror, float unit )
	{
		var left = mirror ? -(frame.OriginX + frame.Width) : frame.OriginX;
		return (left * unit, (left + frame.Width) * unit, -frame.OriginY * unit, -(frame.OriginY + frame.Height) * unit);
	}

	[TestMethod]
	public void ABottomAnchoredSlotSitsTheFramesBottomEdgeOnThePoint()
	{
		// a torso-centred swim frame at 2x: 100 x 80 pixels, the hotspot 45 rows below the top (originY -45), so 35 rows of the body hang below it
		var frame = new GuestAtlasFrame( 0, 0, 100, 80, -50, -45, 256, 256 );
		var lift = GuestRenderer.AnchorLift( PoolSwimSlots.BottomAnchored, PoolSwimSlots.Sliding, frame );
		Assert.AreEqual( 35 * GuestRenderer.UnitsPerPixel * 0.5f, lift, 1e-6f );
		var (_, _, top, bottom) = frame.Extents( mirror: false, GuestRenderer.UnitsPerPixel );
		Assert.AreEqual( 0f, bottom + lift, 1e-6f, "the bottom edge is on the point" );
		Assert.AreEqual( 80 * GuestRenderer.UnitsPerPixel * 0.5f, top + lift, 1e-6f, "the whole frame is above it" );
		// the strokes keep their water-line hotspot, standing its feet, and a renderer without the set (the kids) lifts nothing
		Assert.AreEqual( 0f, GuestRenderer.AnchorLift( PoolSwimSlots.BottomAnchored, PoolSwimSlots.Crawl, frame ) );
		Assert.AreEqual( 0f, GuestRenderer.AnchorLift( PoolSwimSlots.BottomAnchored, PoolSwimSlots.Stand, frame ) );
		Assert.AreEqual( 0f, GuestRenderer.AnchorLift( null, PoolSwimSlots.Sliding, frame ) );
		// a frame whose hotspot is already at or below its bottom edge (the feet) is not lowered
		Assert.AreEqual( 0f, GuestRenderer.AnchorLift( PoolSwimSlots.BottomAnchored, PoolSwimSlots.LadderClimb, new GuestAtlasFrame( 0, 0, 40, 60, -20, -60 ) ) );
		CollectionAssert.AreEquivalent( new[] { 3, 4, 5, 7, 14 }, PoolSwimSlots.BottomAnchored.ToArray() );
	}

	[TestMethod]
	public void AMultiFrameAnimationHasOneLiftSoItsBodyDoesNotJump()
	{
		// the dive slot of a shipped set: the frames hang 43, 13, 38 and 46 rows below the hotspot; each frame is 100 x 90 pixels at 2x
		var rows = new[] { 43, 13, 38, 46 };
		var frames = rows.Select( below => new GuestAtlasFrame( 0, 0, 100, 90, -50, below - 90, 256, 256 ) ).ToArray();
		var lift = GuestRenderer.AnchorLift( PoolSwimSlots.BottomAnchored, PoolSwimSlots.Dive, frames );
		Assert.AreEqual( 46 * GuestRenderer.UnitsPerPixel * 0.5f, lift, 1e-6f, "the largest rows-below-hotspot of the animation" );
		foreach ( var frame in frames )
		{
			var (_, _, _, bottom) = frame.Extents( mirror: false, GuestRenderer.UnitsPerPixel );
			Assert.IsTrue( bottom + lift >= -1e-6f, "no frame sinks below the point" );
		}
		// the single-frame lifts differ (this was the pop), the animation's is the maximum of them
		var single = frames.Select( frame => GuestRenderer.AnchorLift( PoolSwimSlots.BottomAnchored, PoolSwimSlots.Dive, frame ) ).ToArray();
		Assert.IsTrue( single.Distinct().Count() > 1 );
		Assert.AreEqual( single.Max(), lift );
		Assert.AreEqual( 0f, GuestRenderer.AnchorLift( PoolSwimSlots.BottomAnchored, PoolSwimSlots.Crawl, frames ), "a slot that is not anchored" );
		Assert.AreEqual( 0f, GuestRenderer.AnchorLift( PoolSwimSlots.BottomAnchored, PoolSwimSlots.Dive, Array.Empty<GuestAtlasFrame>() ) );
	}

	[TestMethod]
	public void FramesWith128PixelsPerUnitMakeTheFixedScaleQuadBitForBit()
	{
		var random = new Random( 17 );
		var units = new[] { GuestRenderer.UnitsPerPixel, GuestRenderer.UnitsPerPixel * 0.685f, GuestRenderer.UnitsPerPixel * 1.37f, 0.1f };
		for ( var index = 0; index < 5000; index++ )
		{
			var frame = new GuestAtlasFrame( 0, 0, random.Next( 1, 160 ), random.Next( 1, 160 ), random.Next( -80, 80 ), random.Next( -120, 40 ) );
			Assert.AreEqual( (128, 128), (frame.PixelsPerUnitX, frame.PixelsPerUnitY) );
			foreach ( var unit in units )
				foreach ( var mirror in new[] { false, true } )
					Assert.AreEqual( FixedScaleQuad( frame, mirror, unit ), frame.Extents( mirror, unit ), $"{frame} {unit} {mirror}" );
		}
	}

	[TestMethod]
	public void TheQuadSpansOriginOverAToWidthPlusOriginOverATimesTheScale()
	{
		// 0x5890E0: originX / A · scale to (width + originX) / A · scale, likewise down with B (the engine's scale is the world size of a pixel at 128).
		var frame = new GuestAtlasFrame( 4, 8, 40, 80, -20, -76, 256, 256 );
		var (x0, x1, top, bottom) = frame.Extents( false, 0.045f );
		Assert.AreEqual( -20 / 256f * 128 * 0.045f, x0, 1e-6f );
		Assert.AreEqual( 20 / 256f * 128 * 0.045f, x1, 1e-6f );
		Assert.AreEqual( 76 / 256f * 128 * 0.045f, top, 1e-6f );
		Assert.AreEqual( -4 / 256f * 128 * 0.045f, bottom, 1e-6f );
		// A 2x frame is the same world size as the 1x frame it replaces: half the world size per pixel, twice the pixels.
		var oneX = new GuestAtlasFrame( 0, 0, 20, 40, -10, -38 );
		var (a0, a1, atop, abottom) = oneX.Extents( false, 0.045f );
		var twoX = new GuestAtlasFrame( 0, 0, 40, 80, -20, -76, 256, 256 );
		var (b0, b1, btop, bbottom) = twoX.Extents( false, 0.045f );
		Assert.AreEqual( (a0, a1, atop, abottom), (b0, b1, btop, bbottom) );
	}

	[TestMethod]
	public void AcrossAndDownScaleIndependentlyAndAMirroredFrameReflectsAboutTheHotspot()
	{
		var frame = new GuestAtlasFrame( 0, 0, 30, 60, -10, -50, 64, 256 );
		Assert.AreEqual( 2f, frame.ScaleX );
		Assert.AreEqual( 0.5f, frame.ScaleY );
		var (x0, x1, top, bottom) = frame.Extents( false, 0.04f );
		Assert.AreEqual( (-10 * 0.08f, 20 * 0.08f, 50 * 0.02f, -10 * 0.02f), (x0, x1, top, bottom) );
		var (m0, m1, mtop, mbottom) = frame.Extents( true, 0.04f );
		Assert.AreEqual( (-20 * 0.08f, 10 * 0.08f), (m0, m1), "mirrored: the span is reflected about the hotspot" );
		Assert.AreEqual( (top, bottom), (mtop, mbottom) );
	}

	[TestMethod]
	public void AFieldOfZeroScalesLikeTheShippedValue()
	{
		var frame = new GuestAtlasFrame( 0, 0, 10, 10, -5, -10, 0, 0 );
		Assert.AreEqual( (1f, 1f), (frame.ScaleX, frame.ScaleY) );
	}

	[TestMethod]
	public void TheReaderNamesTheFieldsAndTheWriterKeepsThem()
	{
		var rgba = new byte[4 * 4 * 4];
		for ( var i = 0; i < 16; i++ )
		{
			rgba[i * 4] = 200;
			rgba[i * 4 + 3] = 255;
		}
		var set = new SpriteSetBuilder( "SPR_ZZ" ) { FullDetail = false }
			.AddAnimation( 0, new[] { new[] { new SpriteRgbaFrame( 4, 4, rgba, -2, -4 ) { PixelsPerUnit = 256 } } } )
			.Build();
		var bank = new SpriteBankFile( new MemoryStream( set.Files[".TPC"] ) );
		Assert.AreEqual( (256, 256), (bank.Frames[0].PixelsPerUnitX, bank.Frames[0].PixelsPerUnitY) );
		CollectionAssert.AreEqual( set.Files[".TPC"], SpriteBankWriter.Write( bank ) );
		var atlas = new GuestSpriteAtlas( "SPR_ZZ", bank, new SpriteAnimationFile( new MemoryStream( set.Files[".ESP"] ) ) );
		Assert.AreEqual( (256, 256), (atlas.Frames[0].PixelsPerUnitX, atlas.Frames[0].PixelsPerUnitY) );
		Assert.AreEqual( 0.5f, atlas.Frames[0].ScaleX );
	}

	[TestMethod]
	public void TheAtlasStaysAt1024ForOrdinarySetsAndGrowsWiderInsteadOfTaller()
	{
		SpriteBankFile Bank( int frames, int width, int height )
		{
			var rgba = new byte[width * height * 4];
			for ( var i = 0; i < width * height; i++ )
			{
				rgba[i * 4] = (byte)(i / width % 3 * 90);
				rgba[i * 4 + 3] = 255;
			}
			var list = Enumerable.Range( 0, frames ).Select( _ => new SpriteRgbaFrame( width, height, rgba, 0, -height ) { PixelsPerUnit = 256 } ).ToArray();
			var set = new SpriteSetBuilder( "SPR_ZZ" ) { FullDetail = false }.AddAnimation( 0, new IReadOnlyList<SpriteRgbaFrame>[] { list } ).Build();
			return new SpriteBankFile( new MemoryStream( set.Files[".TPC"] ) );
		}

		var animations = new SpriteAnimationFile( new MemoryStream( new SpriteSetBuilder( "SPR_ZZ" ) { FullDetail = false }
			.AddAnimation( 0, new[] { new[] { new SpriteRgbaFrame( 1, 1, new byte[] { 1, 2, 3, 255 }, 0, 0 ) } } ).Build().Files[".ESP"] ) );
		// 175 frames of a 1x kid (about 36 x 44) need 512 rows at 1024 across: nothing changes for the shipped sets.
		var small = new GuestSpriteAtlas( "SPR_ZZ", Bank( 175, 36, 44 ), animations );
		Assert.AreEqual( (1024, 512), (small.Width, small.Height) );
		// The same set at 2x (72 x 88) needs about 1290 rows at 1024 across (13 frames of 72 x 88 per row with 4 pixels of padding, 14 rows) and fits there, in a 2048 texture.
		var twice = new GuestSpriteAtlas( "SPR_ZZ", Bank( 175, 72, 88 ), animations );
		Assert.AreEqual( (1024, 2048), (twice.Width, twice.Height) );
		// A set that would need more than 2048 rows doubles the width first.
		var wide = new GuestSpriteAtlas( "SPR_ZZ", Bank( 25, 1000, 100 ), animations );
		Assert.AreEqual( (2048, 2048), (wide.Width, wide.Height) );
		Assert.IsTrue( wide.Frames.All( frame => frame.X + frame.Width <= wide.Width && frame.Y + frame.Height <= wide.Height ) );
		// A frame as wide as the row makes the atlas wider instead of wrapping out of it.
		var broad = new GuestSpriteAtlas( "SPR_ZZ", Bank( 1, 1018, 20 ), animations );
		Assert.AreEqual( 2048, broad.Width );
		Assert.IsTrue( broad.Frames[0].X + broad.Frames[0].Width <= broad.Width );
		// Four pixels of padding between frames drawn at more than 128 pixels per unit, so mip levels do not bleed.
		Assert.AreEqual( 4, small.Frames[0].X );
		Assert.AreEqual( 4 + 36 + 4, small.Frames[1].X );
		// A set that fits no 4096 x 4096 atlas is refused.
		Assert.ThrowsException<InvalidDataException>( () => new GuestSpriteAtlas( "SPR_ZZ", Bank( 70, 1000, 250 ), animations ) );
		// No two frames overlap.
		for ( var a = 0; a < wide.Frames.Length; a++ )
			for ( var b = a + 1; b < wide.Frames.Length; b++ )
			{
				var p = wide.Frames[a];
				var q = wide.Frames[b];
				Assert.IsFalse( p.X < q.X + q.Width && q.X < p.X + p.Width && p.Y < q.Y + q.Height && q.Y < p.Y + p.Height, $"{a} and {b} overlap" );
			}
	}

	[TestMethod]
	public void TheWorldPickerUsesTheSameScaledRectangle()
	{
		var frame = new GuestAtlasFrame( 0, 0, 10, 20, 5, -20 );
		var twice = frame with { PixelsPerUnitX = 256, PixelsPerUnitY = 256 };
		var feet = new N.Vector3( 0, 0, .5f );
		bool Hit( GuestAtlasFrame f, float x, float y ) => GuestWorldPicker.TryHit( feet, f, false, N.Matrix4x4.Identity, N.Matrix4x4.Identity, new N.Vector2( 1000 ), new N.Vector2( x, y ), out _ );
		Assert.IsTrue( Hit( frame, 700, 300 ) );
		Assert.IsFalse( Hit( twice, 700, 300 ), "half the width: x 700 is right of the frame" );
		Assert.IsTrue( Hit( frame, 650, 200 ) );
		Assert.IsFalse( Hit( twice, 650, 200 ), "half the height: y 200 is above the frame" );
		Assert.IsTrue( Hit( twice, 600, 300 ) );
	}

	[TestMethod]
	public void TheHireFigureLayoutScalesByTheFactorsAndKeepsTheShippedLayoutAtOne()
	{
		var plain = new UiSpriteFrame( null!, 1024, 512, 100, 50, 20, 40, -8, -38, false );
		var feet = new N.Vector2( 100, 200 );
		var (rect, _, _) = UiSpriteFigure.Layout( plain, feet, 3.2f );
		Assert.AreEqual( (100 - 8 * 3.2f, 200 - 38 * 3.2f, 20 * 3.2f, 40 * 3.2f), (rect.X, rect.Y, rect.Width, rect.Height) );
		var (half, _, _) = UiSpriteFigure.Layout( plain with { ScaleX = 0.5f, ScaleY = 0.25f }, feet, 3.2f );
		Assert.AreEqual( (100 - 8 * 1.6f, 200 - 38 * 0.8f, 20 * 1.6f, 40 * 0.8f), (half.X, half.Y, half.Width, half.Height) );
	}
}
