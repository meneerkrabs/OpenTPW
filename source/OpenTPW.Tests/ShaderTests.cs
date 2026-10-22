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

	/// <summary>The depth-tested preview image's shader (ui-depth.shader) compiles for the native backends with its image bindings.</summary>
	[DataTestMethod]
	[DataRow( CrossCompileTarget.MSL )]
	[DataRow( CrossCompileTarget.HLSL )]
	[DataRow( CrossCompileTarget.GLSL )]
	public void NativeUiDepthShaderCompilesForEveryBackend( CrossCompileTarget target )
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_NATIVE_SHADER_TESTS" ) != "1" )
			Assert.Inconclusive( "Set OPENTPW_NATIVE_SHADER_TESTS=1 with architecture-matching native SPIR-V libraries." );
		Log = new();
		var program = ShaderCompiler.CompileProgram( GetShaderPath( "ui-depth.shader" ), target );
		var compiled = SpirvCompilation.CompileVertexFragment( program.Vertex, program.Fragment, target );
		Assert.IsFalse( string.IsNullOrWhiteSpace( compiled.VertexShader ) );
		Assert.IsFalse( string.IsNullOrWhiteSpace( compiled.FragmentShader ) );
		var names = program.Reflection.ResourceLayouts.SelectMany( layout => layout.Elements ).Select( element => element.Name ).ToArray();
		CollectionAssert.Contains( names, "Image" );
		CollectionAssert.Contains( names, "s_Image" );
	}

	/// <summary>WebGL2 runs GLSL ES 3.00; every shader must cross-compile to it ahead of time (docs/WEB.md).</summary>
	[DataTestMethod]
	[DataRow( "3d.shader" )]
	[DataRow( "blit.shader" )]
	[DataRow( "sprite.shader" )]
	[DataRow( "test.shader" )]
	[DataRow( "text.shader" )]
	[DataRow( "ui-batch.shader" )]
	[DataRow( "ui-depth.shader" )]
	[DataRow( "ui.shader" )]
	[DataRow( "unlit.shader" )]
	[DataRow( "water.shader" )]
	[DataRow( "pool-water.shader" )]
	[DataRow( "pool-deck.shader" )]
	[DataRow( "pool-basin.shader" )]
	public void NativeShadersCrossCompileToWebGl2( string shader )
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_NATIVE_SHADER_TESTS" ) != "1" )
			Assert.Inconclusive( "Set OPENTPW_NATIVE_SHADER_TESTS=1 with architecture-matching native SPIR-V libraries." );
		Log = new();
		var path = GetShaderPath( shader );
		var program = ShaderCompiler.CompileProgram( path, CrossCompileTarget.ESSL );
		var essl = SpirvCompilation.CompileVertexFragment( program.Vertex, program.Fragment, CrossCompileTarget.ESSL );
		foreach ( var source in new[] { essl.VertexShader, essl.FragmentShader } )
		{
			Assert.IsTrue( source.StartsWith( "#version 300 es" ), "{0} is not GLSL ES 3.00: {1}", shader, source.Split( '\n' )[0] );
			Console.WriteLine( $"--- {shader}\n{source}" );
		}
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

	[TestMethod]
	[DoNotParallelize]
	public void NativeShaderProgramsAreSharedUntilTheLastOwnerReleasesThem()
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_NATIVE_SHADER_TESTS" ) != "1" || !OperatingSystem.IsMacOS() )
			Assert.Inconclusive( "Set OPENTPW_NATIVE_SHADER_TESTS=1 on macOS with native Metal/SPIR-V libraries." );
		var previousDevice = global::Global.Device;
		using var device = Veldrid.GraphicsDevice.CreateMetal( new Veldrid.GraphicsDeviceOptions() );
		global::Global.Device = device;
		try
		{
			// issue #754: every material used to compile its own native pair (on Direct3D 11 an HLSL compile per stage and material)
			var path = GetShaderPath( "text.shader" );
			var first = ShaderCompiler.CompileShader( path );
			var second = ShaderCompiler.CompileShader( path );
			Assert.AreSame( first.VertexShader, second.VertexShader, "the second owner takes the same native vertex shader" );
			Assert.AreSame( first.FragmentShader, second.FragmentShader, "the second owner takes the same native fragment shader" );
			Assert.AreEqual( 2, ShaderCompiler.References( second ) );
			ShaderCompiler.Release( first );
			Assert.IsFalse( second.VertexShader.IsDisposed || second.FragmentShader.IsDisposed, "an earlier owner's release keeps the program of a later owner alive" );
			ShaderCompiler.Release( second );
			Assert.IsTrue( second.VertexShader.IsDisposed && second.FragmentShader.IsDisposed, "the last release disposes the program (at once without a renderer)" );
			var third = ShaderCompiler.CompileShader( path );
			try
			{
				Assert.AreNotSame( second.VertexShader, third.VertexShader, "a program made after the last release is a fresh one" );
				Assert.IsFalse( third.VertexShader.IsDisposed || third.FragmentShader.IsDisposed );
			}
			finally
			{
				ShaderCompiler.Release( third );
			}
		}
		finally
		{
			global::Global.Device = previousDevice;
		}
	}

	/// <summary>
	/// Issue #754: Veldrid.SPIRV's HLSL passes a varying interface block (<c>out Name { ... } name;</c>) as its own main() parameter,
	/// so on Direct3D 11 the vertex output and pixel input signatures disagree about register order and the pixel shader reads the
	/// wrong varyings. Stage inputs and outputs must be plain variables or <c>struct</c> variables (uniform blocks are fine).
	/// NativeWorldShaderRenderTests draws the affected shaders on Direct3D 11.
	/// </summary>
	[TestMethod]
	public void NoShaderPassesVaryingsInAnInterfaceBlock()
	{
		var directory = Path.GetDirectoryName( GetShaderPath( "test.shader" ) )!;
		var block = new System.Text.RegularExpressions.Regex( @"\b(in|out)\s+(?!struct\b)[A-Za-z_][A-Za-z0-9_]*\s*\{" );
		var offenders = Directory.GetFiles( directory, "*.*", SearchOption.AllDirectories )
			.Where( path => path.EndsWith( ".shader", StringComparison.Ordinal ) || path.EndsWith( ".vert", StringComparison.Ordinal ) || path.EndsWith( ".frag", StringComparison.Ordinal ) )
			.SelectMany( path => File.ReadLines( path ).Select( ( line, index ) => (path, line, index) ) )
			.Where( entry => block.IsMatch( entry.line.Split( "//" )[0] ) )
			.Select( entry => $"{Path.GetFileName( entry.path )}:{entry.index + 1}: {entry.line.Trim()}" )
			.ToArray();
		Assert.AreEqual( 0, offenders.Length, "Use 'in struct'/'out struct' for varyings: " + string.Join( "; ", offenders ) );
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
