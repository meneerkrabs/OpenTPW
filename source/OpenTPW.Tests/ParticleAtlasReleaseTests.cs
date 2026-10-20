using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The particle atlas drops its CPU pixels once a renderer has uploaded them (docs/BROWSER-PERFORMANCE.md, "Transfer and memory").</summary>
[TestClass]
public class ParticleAtlasReleaseTests
{
	[TestMethod]
	public void ReleasingThePixelsKeepsTheFramesAndEmptiesTheArray()
	{
		var atlas = new ParticleSpriteAtlas( new List<(SpriteBankFile, SpriteAnimationFile)>() );
		Assert.AreEqual( atlas.Width * atlas.Height * 4, atlas.Pixels.Length );
		var white = atlas.White;
		var whiteTexel = atlas.Pixels[((2 * atlas.Width) + 2) * 4 + 3];
		Assert.AreEqual( 255, whiteTexel, "the white block is opaque before the release" );

		atlas.ReleasePixels();

		Assert.AreEqual( 0, atlas.Pixels.Length );
		Assert.AreEqual( white, atlas.White );
		Assert.AreEqual( 1024, atlas.Width );
		atlas.ReleasePixels();   // twice is harmless
		Assert.AreEqual( 0, atlas.Pixels.Length );
	}
}
