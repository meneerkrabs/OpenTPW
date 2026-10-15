using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Veldrid.SPIRV;

namespace OpenTPW.Tests;

/// <summary>
/// The browser build cannot run the native SPIR-V compiler, so every shader is stored precompiled
/// to GLSL ES 3.00 with its reflection in content/shaders/web (docs/WEB.md). This test keeps those
/// files in step with the shader sources; OPENTPW_WRITE_WEB_SHADERS=1 rewrites them.
/// </summary>
[TestClass]
public class WebShaderTests
{
	/// <summary>
	/// WebGL has neither a 0..1 depth range nor a top-left framebuffer origin. Moving depth to
	/// -1..1 and negating Y puts row 0 at the top in memory, as on Metal and Direct3D, so viewports,
	/// scissors and texture uploads need no conversion; the browser flips once when presenting.
	/// </summary>
	private static readonly CrossCompileOptions options = new( fixClipSpaceZ: true, invertVertexOutputY: true );

	[TestMethod]
	public void PrecompiledWebShadersMatchTheSources()
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_NATIVE_SHADER_TESTS" ) != "1" )
			Assert.Inconclusive( "Set OPENTPW_NATIVE_SHADER_TESTS=1 with architecture-matching native SPIR-V libraries." );
		Log = new();
		var root = RepositoryRoot();
		var shaders = Path.Combine( root, "content", "shaders" );
		var output = Path.Combine( shaders, "web" );
		var write = Environment.GetEnvironmentVariable( "OPENTPW_WRITE_WEB_SHADERS" ) == "1";
		if ( write )
			Directory.CreateDirectory( output );

		var sources = Directory.GetFiles( shaders, "*.shader" ).Order( StringComparer.Ordinal ).ToArray();
		Assert.IsTrue( sources.Length > 0 );
		var stale = sources.Select( Path.GetFileNameWithoutExtension ).ToList();
		foreach ( var source in sources )
		{
			var name = Path.GetFileNameWithoutExtension( source );
			var json = Compile( source );
			var target = Path.Combine( output, name + ".json" );
			if ( write )
				File.WriteAllText( target, json );
			else
				Assert.AreEqual( json, File.Exists( target ) ? File.ReadAllText( target ) : null, $"content/shaders/web/{name}.json is out of date; run this test with OPENTPW_WRITE_WEB_SHADERS=1." );
		}
		var extra = Directory.Exists( output ) ? Directory.GetFiles( output, "*.json" ).Select( Path.GetFileNameWithoutExtension ).Except( stale ).ToArray() : [];
		Assert.AreEqual( 0, extra.Length, $"No shader source for content/shaders/web/{string.Join( ", ", extra! )}.json." );
	}

	private static string Compile( string path )
	{
		var program = ShaderCompiler.CompileProgram( path, CrossCompileTarget.ESSL );
		var essl = SpirvCompilation.CompileVertexFragment( program.Vertex, program.Fragment, CrossCompileTarget.ESSL, options );
		var reflection = program.Reflection;
		var document = new JsonObject
		{
			["fixClipSpaceZ"] = options.FixClipSpaceZ,
			["invertVertexOutputY"] = options.InvertVertexOutputY,
			["vertexElements"] = new JsonArray( reflection.VertexElements.Select( element => (JsonNode)new JsonObject
			{
				["name"] = element.Name,
				["semantic"] = element.Semantic.ToString(),
				["format"] = element.Format.ToString(),
				["offset"] = element.Offset
			} ).ToArray() ),
			["resourceLayouts"] = new JsonArray( reflection.ResourceLayouts.Select( layout => (JsonNode)new JsonArray( layout.Elements.Select( element => (JsonNode)new JsonObject
			{
				["name"] = element.Name,
				["kind"] = element.Kind.ToString(),
				["stages"] = element.Stages.ToString(),
				["options"] = element.Options.ToString()
			} ).ToArray() ) ).ToArray() ),
			["vertex"] = essl.VertexShader.Replace( "\r\n", "\n" ),
			["fragment"] = essl.FragmentShader.Replace( "\r\n", "\n" )
		};
		return document.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) + "\n";
	}

	/// <summary>The checkout (not the copy of content/ in the build output): it holds both content/ and source/.</summary>
	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !(Directory.Exists( Path.Combine( directory.FullName, "content", "shaders" ) ) && Directory.Exists( Path.Combine( directory.FullName, "source" ) )) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, "The repository checkout must be available for this test." );
		return directory!.FullName;
	}
}
