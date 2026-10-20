using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class ParticleOpaqueStateTests
{
	private static ParticleSprite Sprite( int alpha, int flags = 0, bool screen = false, int frames = 0 ) => new()
	{
		Colour = ((uint)alpha << 24) | 0x123456u, RenderFlags = flags, ScreenSpace = screen, FrameCount = frames
	};

	[TestMethod]
	public void OnlyUntexturedWorldAlphaZeroOrAtLeastF0BecomesOpaque()
	{
		foreach ( var alpha in new[] { 0, 1, 15, 16, 239, 240, 255 } )
		{
			var opaque = alpha == 0 || alpha >= 240;
			Assert.AreEqual( opaque ? 6 : 0, ParticleRenderer.RenderKindOf( Sprite( alpha ) ) );
			Assert.AreEqual( opaque ? 7 : 0, ParticleRenderer.RenderKindOf( Sprite( alpha, 0x4000 ) ) );
			Assert.AreEqual( 0, ParticleRenderer.RenderKindOf( Sprite( alpha, frames: 1 ) ), "Textured modes retain their existing branch." );
			Assert.AreEqual( 3, ParticleRenderer.RenderKindOf( Sprite( alpha, screen: true ) ), "Screen behavior is outside the bounded correction." );
		}
	}

	[TestMethod]
	public void AdditiveOverridesOpaqueAndNoWriteFlags()
	{
		foreach ( var alpha in new[] { 0, 240, 255 } )
		foreach ( var noWrite in new[] { 0, 0x4000 } )
		{
			Assert.AreEqual( 1, ParticleRenderer.RenderKindOf( Sprite( alpha, 0x2004 | noWrite ) ) );
			Assert.AreEqual( 2, ParticleRenderer.RenderKindOf( Sprite( alpha, 4 | noWrite ) ) );
			Assert.AreEqual( noWrite == 0 ? 6 : 7, ParticleRenderer.RenderKindOf( Sprite( alpha, 0x2000 | noWrite ) ) );
			Assert.AreEqual( 4, ParticleRenderer.RenderKindOf( Sprite( alpha, 0x2004 | noWrite, screen: true ) ) );
			Assert.AreEqual( 5, ParticleRenderer.RenderKindOf( Sprite( alpha, 4 | noWrite, screen: true ) ) );
		}
	}

	[TestMethod]
	public void OpaqueKindsKeepDepthTestAndOnlyNoWriteDisablesWrites()
	{
		Assert.AreEqual( MaterialFlags.OpaqueBlend | MaterialFlags.DoubleSided, ParticleRenderer.FlagsForKind( 6 ) );
		Assert.AreEqual( MaterialFlags.OpaqueBlend | MaterialFlags.DoubleSided | MaterialFlags.DisableDepthWrite, ParticleRenderer.FlagsForKind( 7 ) );
		Assert.AreEqual( 0, (int)(MaterialFlags.OpaqueBlend & (MaterialFlags.DisableDepth | MaterialFlags.AdditiveBlend | MaterialFlags.OneOneBlend | MaterialFlags.DoubleSided)) );
		for ( var kind = 0; kind < 8; kind++ )
			Assert.AreEqual( kind is >= 3 and <= 5, ParticleRenderer.IsScreenKind( kind ) );
	}

	[TestMethod]
	public void OpaqueAlphaIsAHostCompositionAdaptationOnly()
	{
		foreach ( var alpha in new[] { 0, 239, 240, 255 } )
		for ( var kind = 0; kind < 8; kind++ )
		{
			var colour = Sprite( alpha ).Colour;
			Assert.AreEqual( kind is 6 or 7 ? 0xFF123456u : colour, ParticleRenderer.ColourForKind( colour, kind ) );
		}
	}

	[TestMethod]
	public void CompleteStateRunsNeverReorderOpaqueAndLegacyQuads()
	{
		var runs = new List<(int Kind, int First, int Count)>();
		var kinds = new[] { 6, 6, 7, 1, 6, 3, 7 };
		for ( var index = 0; index < kinds.Length; index++ )
			ParticleRenderer.AddToRuns( runs, kinds[index], index * 6, 6 );
		CollectionAssert.AreEqual( new[] { (6, 0, 12), (7, 12, 6), (1, 18, 6), (6, 24, 6), (3, 30, 6), (7, 36, 6) }, runs );
	}
}
