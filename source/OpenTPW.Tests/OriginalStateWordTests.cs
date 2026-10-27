using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Pins the decoder bits of tp.exe's state word (0x567620) that the sprite quads and the model faces use.</summary>
[TestClass]
public class OriginalStateWordTests
{
	[TestMethod]
	public void SpriteQuadStateBlendsTestsAndTestsDepth()
	{
		// 0x58A26C: every sprite quad (control text, sprites) is drawn with this word.
		var state = new OriginalStateWord( OriginalStateWord.SpriteQuad );
		Assert.AreEqual( 0x0706_0556u, state.Word );
		Assert.IsTrue( state.AlphaTest );
		Assert.AreEqual( 0x10, state.AlphaReference );
		Assert.IsTrue( state.AlphaBlend );
		Assert.AreEqual( 5, state.SourceBlend );
		Assert.AreEqual( 6, state.DestinationBlend );
		Assert.IsTrue( state.DepthTest );
		Assert.IsTrue( state.DepthWrite );
		Assert.IsTrue( state.MagnifyPoint );
		Assert.IsTrue( state.MipNone );
		Assert.IsFalse( state.Wrap );
		Assert.IsFalse( state.MinifyLinear );
		Assert.AreEqual( 4, state.ColorOperation );
	}

	[TestMethod]
	public void FaceStatesFromTheAlphaTypeDecodeAsDocumented()
	{
		var opaque = new OriginalStateWord( 0x1256 );
		Assert.IsFalse( opaque.AlphaTest );
		Assert.IsFalse( opaque.AlphaBlend );
		Assert.IsTrue( opaque.DepthWrite );
		var cutout = new OriginalStateWord( 0x2756 );
		Assert.IsTrue( cutout.AlphaTest );
		Assert.AreEqual( 0xF0, cutout.AlphaReference );
		Assert.IsTrue( cutout.AlphaBlend );
		var additive = new OriginalStateWord( 0x20_0552 );
		Assert.AreEqual( 5, additive.SourceBlend );
		Assert.AreEqual( 2, additive.DestinationBlend );
	}

	[TestMethod]
	public void DepthBitsAndTheInertMaterialBit()
	{
		Assert.IsFalse( new OriginalStateWord( 0x556 | 0x800 ).DepthWrite );
		Assert.IsFalse( new OriginalStateWord( 0x556 | 0x8000 ).DepthTest );
		// 0x581790 turns material bit 0x2 into 0x800000; the decoder has no case for it, so it changes no state.
		var plain = new OriginalStateWord( 0x556 );
		var marked = new OriginalStateWord( 0x556 | 0x80_0000 );
		Assert.AreEqual( plain.AlphaBlend, marked.AlphaBlend );
		Assert.AreEqual( plain.DepthWrite, marked.DepthWrite );
		Assert.AreEqual( plain.Wrap, marked.Wrap );
		Assert.AreEqual( plain.AlphaReference, marked.AlphaReference );
	}

	[TestMethod]
	public void OptionLabelsAreDrawnInTheControlColourExactly()
	{
		// 0x4a441e, 0x4a44fe: the label colour (0,0,0,255); the sprite quads modulate the white-coverage texels by it (stage 0 MODULATE, 0x566840), so the RGB is black.
		Assert.AreEqual( new Veldrid.RgbaByte( 0, 0, 0, 255 ), OpenTPW.UI.Original.UiColors.OptionText );
	}

	[TestMethod]
	public void GlyphCoverageBecomesTexelAlphaAndPassesTheTestFromTheFirstLevel()
	{
		Assert.AreEqual( 0, OriginalTextTexel.Alpha( 0 ) );
		Assert.AreEqual( 17, OriginalTextTexel.Alpha( 1 ) );
		Assert.AreEqual( 255, OriginalTextTexel.Alpha( 15 ) );
		Assert.IsFalse( OriginalTextTexel.PassesAlphaTest( OriginalTextTexel.Alpha( 0 ), 255 ) );
		for ( var coverage = 1; coverage <= 15; coverage++ )
			Assert.IsTrue( OriginalTextTexel.PassesAlphaTest( OriginalTextTexel.Alpha( coverage ), 255 ), $"coverage {coverage}" );
		// the blend alpha of a full-colour-alpha pixel is the coverage over 15
		for ( var coverage = 0; coverage <= 15; coverage++ )
			Assert.AreEqual( coverage / 15f, OriginalTextTexel.BlendAlpha( OriginalTextTexel.Alpha( coverage ), 255 ), 1e-6f );
	}

	[TestMethod]
	public void TheTitleShadowCopyIsHalfAsOpaqueAndDropsTheFirstLevel()
	{
		// 0x491db7: the shadow colour is (128,128,128) with alpha 0x80; 0x6b26d0 scales the coverage by it and rounds down.
		Assert.AreEqual( 0, OriginalTextTexel.Alpha( 1, 0x80 ) );
		Assert.AreEqual( 17, OriginalTextTexel.Alpha( 2, 0x80 ) );
		Assert.AreEqual( 7 * 17, OriginalTextTexel.Alpha( 15, 0x80 ) );
	}
}
