using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The per-draw uniform buffer is as large as the largest block a draw uploads and a shader declares (<see cref="Material.ScratchBufferBytes"/>): on Direct3D 11
/// every partial write into it is widened to the whole buffer (DynamicBufferUpload), so its size is what every draw uploads there.
/// </summary>
[TestClass]
public class MaterialScratchBufferTests
{
	[TestMethod]
	public void TheBufferHoldsTheLitBlockWithEveryPointLight()
	{
		Assert.AreEqual( 784u, Material.ScratchBufferBytes, "272-byte block + 16 lights x 32 bytes" );
		Assert.AreEqual( LitObjectUniformBuffer.UploadSize( PointLightSet.MaxPerDraw ), Material.ScratchBufferBytes );
		Assert.AreEqual( (uint)System.Runtime.CompilerServices.Unsafe.SizeOf<LitObjectUniformBuffer>(), Material.ScratchBufferBytes, "the lit block's own size" );
	}

	[TestMethod]
	public void TheLargestShaderUniformBlockFillsTheBufferExactly()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !Directory.Exists( Path.Combine( directory.FullName, "content", "shaders" ) ) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, "content/shaders" );
		var blocks = 0;
		var largest = 0u;
		foreach ( var path in Directory.EnumerateFiles( Path.Combine( directory.FullName, "content", "shaders" ), "*.shader", SearchOption.AllDirectories ) )
		{
			// comments go first, so a brace or a semicolon inside one cannot end a block or a member early
			var source = Regex.Replace( Regex.Replace( File.ReadAllText( path ), @"/\*.*?\*/", "", RegexOptions.Singleline ), @"//[^\n]*", "" );
			foreach ( Match block in Regex.Matches( source, @"uniform\s+\w+\s*\{(.*?)\}", RegexOptions.Singleline ) )
			{
				blocks++;
				var size = Std140Size( block.Groups[1].Value, path );
				largest = Math.Max( largest, size );
				Assert.IsTrue( size <= Material.ScratchBufferBytes, $"{Path.GetFileName( path )} declares a {size}-byte uniform block" );
			}
		}
		Assert.IsTrue( blocks > 0, "the shaders declare their uniform blocks" );
		Assert.AreEqual( Material.ScratchBufferBytes, largest, "the largest block (the lit shaders') fills the buffer exactly" );
	}

	/// <summary>The std140 size of a block of scalar, vector, matrix and vec4-array members (the kinds the shaders use).</summary>
	private static uint Std140Size( string body, string path )
	{
		uint offset = 0;
		var code = Regex.Replace( Regex.Replace( body, @"/\*.*?\*/", "", RegexOptions.Singleline ), @"//[^\n]*", "" );
		foreach ( var raw in code.Split( ';' ) )
		{
			var line = raw.Trim();
			if ( line.Length == 0 )
				continue;
			var member = Regex.Match( line, @"(\w+)\s+\w+\s*(?:\[(\d+)\])?$" );
			Assert.IsTrue( member.Success, $"{Path.GetFileName( path )}: unread uniform member '{line}'" );
			var (size, alignment) = member.Groups[1].Value switch
			{
				"mat4" => (64u, 16u),
				"vec4" or "ivec4" => (16u, 16u),
				"vec3" => (12u, 16u),
				"vec2" => (8u, 8u),
				"float" or "int" or "uint" => (4u, 4u),
				var other => throw new AssertFailedException( $"{Path.GetFileName( path )}: unknown uniform type {other}" )
			};
			if ( member.Groups[2].Success )
			{
				// std140 arrays: every element on a 16-byte stride
				size = Math.Max( size, 16u ) * uint.Parse( member.Groups[2].Value );
				alignment = 16;
			}
			offset = (offset + alignment - 1) / alignment * alignment + size;
		}
		return offset;
	}
}
