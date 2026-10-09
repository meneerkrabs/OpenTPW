using System.Diagnostics;
using Veldrid;
using Veldrid.SPIRV;

namespace OpenTPW;

internal struct ShaderInfo
{
	public Veldrid.Shader VertexShader { get; set; }
	public Veldrid.Shader FragmentShader { get; set; }
	public SpirvReflection Reflection { get; set; }

	public readonly Veldrid.Shader[] ShaderProgram => [VertexShader, FragmentShader];
}

internal static class ShaderCompiler
{
	private static CrossCompileTarget GetCrossCompileTarget()
	{
		return Device.ResourceFactory.BackendType switch
		{
			GraphicsBackend.Direct3D11 => CrossCompileTarget.HLSL,
			GraphicsBackend.OpenGL => CrossCompileTarget.GLSL,
			GraphicsBackend.Vulkan => CrossCompileTarget.GLSL,
			GraphicsBackend.Metal => CrossCompileTarget.MSL,
			GraphicsBackend.OpenGLES => CrossCompileTarget.ESSL,
			_ => throw new NotImplementedException( $"Unknown cross-compile target" )
		};
	}

	internal static bool HasSpirvHeader( byte[] bytes )
	{
		return bytes.Length > 4
			&& bytes[0] == 0x03
			&& bytes[1] == 0x02
			&& bytes[2] == 0x23
			&& bytes[3] == 0x07;
	}

	internal static (byte[] Vertex, byte[] Fragment, SpirvReflection Reflection) CompileProgram( string path, CrossCompileTarget target )
	{
		var preprocessedShader = ShaderPreprocessor.PreprocessShader( path );
		var vertexSource = preprocessedShader.VertexShader;
		var fragmentSource = preprocessedShader.FragmentShader;

		var compileOptions = new GlslCompileOptions( true );
		var vertexSpirv = SpirvCompilation.CompileGlslToSpirv( vertexSource, path, ShaderStages.Vertex, compileOptions );
		var fragmentSpirv = SpirvCompilation.CompileGlslToSpirv( fragmentSource, path, ShaderStages.Fragment, compileOptions );
		var compilationResult = SpirvCompilation.CompileVertexFragment( vertexSpirv.SpirvBytes, fragmentSpirv.SpirvBytes, target );

		Debug.Assert( HasSpirvHeader( vertexSpirv.SpirvBytes ) );
		Debug.Assert( HasSpirvHeader( fragmentSpirv.SpirvBytes ) );
		return (vertexSpirv.SpirvBytes, fragmentSpirv.SpirvBytes, compilationResult.Reflection);
	}

	private static readonly Dictionary<(string Path, DateTime Modified, GraphicsBackend Backend), (byte[] Vertex, byte[] Fragment, SpirvReflection Reflection)> compiled = new();

	/// <summary>
	/// Reuses compiled SPIR-V and reflection per file version/backend. Each caller owns fresh native
	/// shader objects: disposing a level's text renderer must not invalidate another renderer or a
	/// later level created from the same compiled program.
	/// </summary>
	public static ShaderInfo CompileShader( string path )
	{
		var key = (Path.GetFullPath( path ), File.GetLastWriteTimeUtc( path ), Device.ResourceFactory.BackendType);
		(byte[] Vertex, byte[] Fragment, SpirvReflection Reflection) program;
		lock ( compiled )
		{
			if ( !compiled.TryGetValue( key, out program ) )
			{
				program = CompileProgram( path, GetCrossCompileTarget() );
				compiled[key] = program;
			}
		}

		var shaders = Device.ResourceFactory.CreateFromSpirv(
			new ShaderDescription( ShaderStages.Vertex, program.Vertex, "main" ),
			new ShaderDescription( ShaderStages.Fragment, program.Fragment, "main" ) );

		return new ShaderInfo()
		{
			VertexShader = shaders[0],
			FragmentShader = shaders[1],
			Reflection = program.Reflection
		};
	}
}
