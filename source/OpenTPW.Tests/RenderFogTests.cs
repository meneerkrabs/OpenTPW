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
	public void RestoreTheColour() => RenderFog.WorldColour = RenderFog.ColourOf( RenderFog.LobbyColour );

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

	private static string ShaderPath( string shader )
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "content", "shaders", shader ) ) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, "Repository shader assets must be available for this test." );
		return Path.Combine( directory!.FullName, "content", "shaders", shader );
	}
}
