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

	private static readonly Dictionary<(string Path, DateTime Modified, GraphicsBackend Backend), ShaderInfo> compiled = new();

	/// <summary>
	/// Compiles a shader once per file version and backend. Every <see cref="Material"/> creates its own
	/// <see cref="Shader"/>, so scenes with many original object meshes would otherwise recompile the
	/// same GLSL for each mesh; the Veldrid shader objects are never disposed and can be shared.
	/// </summary>
	public static ShaderInfo CompileShader( string path )
	{
		var key = (Path.GetFullPath( path ), File.GetLastWriteTimeUtc( path ), Device.ResourceFactory.BackendType);
		lock ( compiled )
		{
			if ( compiled.TryGetValue( key, out var cached ) )
				return cached;
			var info = CompileShaderUncached( path );
			compiled[key] = info;
			return info;
		}
	}

	private static ShaderInfo CompileShaderUncached( string path )
	{
		var program = CompileProgram( path, GetCrossCompileTarget() );
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
