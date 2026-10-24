using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The `particles` sprite set of the original game (inconclusive without OPENTPW_GAME_PATH).</summary>
[TestClass]
public class ParticleSpriteAtlasTests
{
	private string dataPath = null!;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original particle sprites." );
		dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "esprites.wad" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks esprites.wad." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	[TestMethod]
	public void SpriteCodesAreBankTimesSixteenPlusAnimationAndMatchTheTemplateFrameCounts()
	{
		var atlas = ParticleSpriteAtlas.Load();
		Assert.IsNotNull( atlas );
		Assert.AreEqual( 0, atlas!.Width & (atlas.Width - 1) );
		Assert.AreEqual( 0, atlas.Height & (atlas.Height - 1) );
		Assert.AreEqual( atlas.Width * atlas.Height * 4, atlas.Pixels.Length );

		var templates = TemplateFile();
		foreach ( var template in templates.Templates.Where( template => template.FrameCount > 0 ) )
		{
			Assert.IsNotNull( atlas.Find( template.SpriteCode, 0 ), $"{template.Name}: sprite code {template.SpriteCode}" );
			var last = atlas.Find( template.SpriteCode, template.FrameCount - 1 );
			Assert.IsNotNull( last, $"{template.Name}: last frame" );
			if ( template.FrameCount > 1 )
				Assert.AreNotEqual( atlas.Find( template.SpriteCode, 0 ), last, $"{template.Name}: the frames differ" );
		}
		// the animation lengths of SPR_PA (docs/PARTICLES.md): code 11 and 12 are 16 frames, 5 is 14, 3 is 8, 13 is 6, 14 is 9
		foreach ( var (code, frames) in new[] { (11, 16), (12, 16), (5, 14), (3, 8), (13, 6), (14, 9) } )
			Assert.AreEqual( frames, templates.Templates.First( template => template.SpriteCode == code && template.FrameCount > 0 ).FrameCount, $"code {code}" );
		Assert.IsNotNull( atlas.Find( 16, 0 ), "bank 1 is SPR_PB" );
		Assert.IsNull( atlas.Find( 99, 0 ), "no such bank" );
	}

	[TestMethod]
	public void WhiteBlockIsOpaqueWhiteForUntexturedParticles()
	{
		var atlas = ParticleSpriteAtlas.Load()!;
		var white = atlas.White;
		var x = (int)(((white.U0 + white.U1) / 2) * atlas.Width);
		var y = (int)(((white.V0 + white.V1) / 2) * atlas.Height);
		var offset = (y * atlas.Width + x) * 4;
		CollectionAssert.AreEqual( new byte[] { 255, 255, 255, 255 }, atlas.Pixels[offset..(offset + 4)] );
	}

	private ParticleTemplateFile TemplateFile()
	{
		using var stream = FileSystem.OpenRead( "/Particle/Tp2.plb" );
		return new ParticleTemplateFile( stream );
	}
}
