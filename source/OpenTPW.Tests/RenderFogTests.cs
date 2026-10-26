using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Veldrid;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The engine fog colour (docs/UI.md, UI-107): the level's <c>ThemeEngine.FogColour</c> reaches the uniform buffer every fogged shader reads
/// (tp.exe 0x4080E0).
/// </summary>
[TestClass]
public class RenderFogTests
{
	[TestCleanup]
	public void RestoreTheColour()
	{
		RenderFog.WorldColour = RenderFog.ColourOf( RenderFog.LobbyColour );
		RenderFog.SetParkRange( 2 );
	}

	[DataTestMethod]
	[DataRow( "jungle", 4774136, 0x48, 0xD8, 0xF8 )]
	[DataRow( "fantasy", 4774136, 0x48, 0xD8, 0xF8 )]
	[DataRow( "hallow", 1122884, 0x11, 0x22, 0x44 )]
	[DataRow( "space", 16286768, 0xF8, 0x84, 0x30 )]
	public void EachLevelsFogColourReachesTheUniformBuffer( string level, int setting, int r, int g, int b )
	{
		var files = new Dictionary<string, string> { [$"/levels/{level}/Standard.sam"] = setting.ToString(), ["/levels/Standard.sam"] = "4774136" };
		var colour = Sky.FogColour( level, files.ContainsKey, path => files[path] );
		RenderFog.WorldColour = RenderFog.ColourOf( colour );

		var buffer = new ObjectUniformBuffer().WithFog();

		Assert.AreEqual( new SVector3( r / 255f, g / 255f, b / 255f ), buffer.g_vFogColor, level );
	}

	[TestMethod]
	public void ASceneWithoutALevelFadesIntoTheLobbyColour()
	{
		Assert.AreEqual( new RgbaByte( 0x44, 0xDD, 0xFF, 255 ), RenderFog.LobbyColour );
		Assert.AreEqual( new SVector3( 0x44 / 255f, 0xDD / 255f, 1f ), RenderFog.ColourOf( RenderFog.LobbyColour ) );
	}

	[TestMethod]
	public void TheUniformBufferLaysTheFogColourOutLikeTheShadersBlocks()
	{
		Assert.AreEqual( 240, (int)Marshal.OffsetOf<ObjectUniformBuffer>( nameof( ObjectUniformBuffer._padding2 ) ), "g_flViewDistanceScale" );
		Assert.AreEqual( 256, (int)Marshal.OffsetOf<ObjectUniformBuffer>( nameof( ObjectUniformBuffer.g_vFogColor ) ), "a vec3 starts on a 16-byte boundary in std140" );
		Assert.AreEqual( 272, Marshal.SizeOf<ObjectUniformBuffer>() );
	}

	[DataTestMethod]
	[DataRow( 0, 200f, 50f, 60f )]
	[DataRow( 1, 325f, 100f, 110f )]
	[DataRow( 2, 450f, 135f, 150f )]
	[DataRow( 3, 700f, 230f, 250f )]
	public void FirstPersonViewDistanceGivesTheOriginalsRayDistanceAndFogRange( int option, float distance, float start, float end )
	{
		// [BIN:TPW-EXE:0x44DBB0 FIRSTPERSONVIEWDISTANCE] [0x8BD4C0], [0x8BD490] and [0x8BD494] (through 0x583280)
		Assert.AreEqual( (distance, start, end), RenderFog.ViewDistanceOf( option ) );
	}

	[TestMethod]
	public void TheParksRangeIsInViewDepthOfTheProjectionsW()
	{
		// the original's 90 degree projection has w = sin(45 degrees) x the depth and an original unit is 0.2 world units
		RenderFog.SetParkRange( 2 );
		Assert.AreEqual( 135f * 0.2f / MathF.Sin( MathF.PI / 4 ), RenderFog.ViewRange.Start, 1e-4f );
		Assert.AreEqual( 150f * 0.2f / MathF.Sin( MathF.PI / 4 ), RenderFog.ViewRange.End, 1e-4f );
		RenderFog.SetLobbyRange();
		Assert.AreEqual( 50f / MathF.Sin( LobbyCameraMode.IslandFieldOfView * MathF.PI / 360f ), RenderFog.ViewRange.Start, 1e-3f, "the lobby's units are the original's" );
	}

	[TestMethod]
	public void ThePackedRangeSurvivesTheShadersDecode()
	{
		foreach ( var option in new[] { 0, 1, 2, 3 } )
		{
			RenderFog.SetParkRange( option );
			foreach ( var scale in new[] { 0.5f, 1f, 2f, 4f } )
			{
				var packed = RenderFog.PackedRange( scale );
				// the shaders' decode
				var startQ = MathF.Floor( packed / 4096f );
				var start = startQ * 0.125f;
				var end = (packed - startQ * 4096f) * 0.125f;
				Assert.AreEqual( RenderFog.ViewRange.Start * scale, start, 0.0625f, $"option {option}, scale {scale}" );
				Assert.AreEqual( RenderFog.ViewRange.End * scale, end, 0.0625f );
			}
		}
		Assert.IsTrue( RenderFog.PackedRange( 1000f ) > 0, "a scale of 1000 (the globe) is held at the largest range, not dropped" );
	}

	[TestMethod]
	public void TheFogGoesIntoTheSlotTheShadersCallViewDistanceScale()
	{
		RenderFog.SetParkRange( 2 );
		var buffer = new ObjectUniformBuffer().WithFog();
		Assert.AreEqual( RenderFog.PackedRange( CompatibilityRuntime.RenderQuality.ViewDistanceScale ), buffer._padding2 );
		Assert.AreEqual( RenderFog.PackedRange( 3f ), new ObjectUniformBuffer { _padding2 = 3f }.WithFog()._padding2, "an entity's own scale stretches the range" );
	}

	/// <summary>Every shader that fogs reads the colour and range from the buffer, in the same order and after the same fields, and keeps no blue of its own, in every stage that declares the block.</summary>
	[DataTestMethod]
	[DataRow( "test.shader" )]
	[DataRow( "water.shader" )]
	[DataRow( "sprite.shader" )]
	[DataRow( "3d.shader" )]
	public void FoggedShadersReadTheFogFromTheBuffer( string shader )
	{
		var text = File.ReadAllText( ShaderPath( shader ) );
		Assert.IsFalse( text.Contains( "0.301" ) || text.Contains( "0.28, 0.88" ), $"{shader} must not keep a fixed fog colour" );
		Assert.IsTrue( text.Contains( "g_oUbo.g_vFogColor" ), $"{shader} fogs from the buffer" );
		var order = new[] { "float g_flViewDistanceScale;", "vec3 g_vFogColor;" };
		var position = 0;
		foreach ( var field in order )
		{
			var next = text.IndexOf( field, position, StringComparison.Ordinal );
			Assert.IsTrue( next >= 0, $"{shader} declares {field} in order" );
			position = next;
		}
	}

	/// <summary>The shaders that draw the park fog with the original's linear range in the projection's depth, not the old exponential curve.</summary>
	[DataTestMethod]
	[DataRow( "test.shader" )]
	[DataRow( "water.shader" )]
	[DataRow( "sprite.shader" )]
	[DataRow( "pool-water.shader" )]
	[DataRow( "pool-deck.shader" )]
	[DataRow( "pool-basin.shader" )]
	public void TheParkShadersFogLinearlyInTheProjectionsDepth( string shader )
	{
		var text = File.ReadAllText( ShaderPath( shader ) );
		Assert.IsTrue( text.Contains( "gl_FragCoord.w" ), $"{shader} measures the depth of the projection" );
		Assert.IsFalse( text.Contains( "exp(" ) || text.Contains( "exp (" ), $"{shader} keeps no exponential fog" );
		Assert.IsTrue( text.Contains( "4096.0" ), $"{shader} decodes the packed range" );
	}

	private static string ShaderPath( string shader )
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "content", "shaders", shader ) ) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, "Repository shader assets must be available for this test." );
		return Path.Combine( directory!.FullName, "content", "shaders", shader );
	}
}
