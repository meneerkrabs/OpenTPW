using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using Veldrid.SPIRV;

namespace OpenTPW.Tests;

[TestClass]
public class ShaderTests
{
	[TestMethod]
	public void PreprocessTest()
	{
		Log = new();
		var result = ShaderPreprocessor.PreprocessShader( GetShaderPath( "test.shader" ) );
		Assert.IsTrue( result.VertexShader.Length > 0 && result.FragmentShader.Length > 0 );
	}

	[DataTestMethod]
	[DataRow( CrossCompileTarget.MSL, "3d.shader", "Color" )]
	[DataRow( CrossCompileTarget.HLSL, "3d.shader", "Color" )]
	[DataRow( CrossCompileTarget.GLSL, "3d.shader", "Color" )]
	[DataRow( CrossCompileTarget.MSL, "test.shader", "Color0" )]
	[DataRow( CrossCompileTarget.HLSL, "test.shader", "Color0" )]
	[DataRow( CrossCompileTarget.GLSL, "test.shader", "Color0" )]
	public void NativeCompilationPreservesMaterialBindingNames( CrossCompileTarget target, string shader, string textureName )
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_NATIVE_SHADER_TESTS" ) != "1" )
			Assert.Inconclusive( "Set OPENTPW_NATIVE_SHADER_TESTS=1 with architecture-matching native SPIR-V libraries." );
		Log = new();
		var program = ShaderCompiler.CompileProgram( GetShaderPath( shader ), target );
		Assert.IsTrue( ShaderCompiler.HasSpirvHeader( program.Vertex ) );
		Assert.IsTrue( ShaderCompiler.HasSpirvHeader( program.Fragment ) );
		var names = program.Reflection.ResourceLayouts.SelectMany( layout => layout.Elements ).Select( element => element.Name ).ToArray();
		CollectionAssert.Contains( names, "ObjectUniformBuffer" );
		CollectionAssert.Contains( names, textureName );
		CollectionAssert.Contains( names, "s_Color" );
	}

	[DataTestMethod]
	[DataRow( CrossCompileTarget.MSL )]
	[DataRow( CrossCompileTarget.HLSL )]
	[DataRow( CrossCompileTarget.GLSL )]
	public void NativeTextShaderExposesAtlasBindings( CrossCompileTarget target )
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_NATIVE_SHADER_TESTS" ) != "1" )
			Assert.Inconclusive( "Set OPENTPW_NATIVE_SHADER_TESTS=1 with architecture-matching native SPIR-V libraries." );
		Log = new();
		var program = ShaderCompiler.CompileProgram( GetShaderPath( "text.shader" ), target );
		var names = program.Reflection.ResourceLayouts.Single().Elements.Select( element => element.Name ).ToArray();
		CollectionAssert.AreEqual( new[] { "Atlas", "s_Atlas" }, names );
		Assert.AreEqual( 3, program.Reflection.VertexElements.Length );
	}

	private static string GetShaderPath( string shader )
	{
		var directory = new System.IO.DirectoryInfo( System.AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "content", "shaders", shader ) ) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, "Repository shader assets must be available for this test." );
		return Path.Combine( directory!.FullName, "content", "shaders", shader );
	}
}
