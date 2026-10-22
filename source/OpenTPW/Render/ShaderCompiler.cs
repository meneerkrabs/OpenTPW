using System.Diagnostics;
using Veldrid;
using Veldrid.SPIRV;

namespace OpenTPW;

internal struct ShaderInfo
{
	public Veldrid.Shader VertexShader { get; set; }
	public Veldrid.Shader FragmentShader { get; set; }
	public SpirvReflection Reflection { get; set; }

	/// <summary>The shared native program this info holds a reference to (<see cref="ShaderCompiler.Release"/>), or null.</summary>
	internal SharedShaderProgram? Owner { get; set; }

	public readonly Veldrid.Shader[] ShaderProgram => [VertexShader, FragmentShader];
}

/// <summary>
/// One compiled native shader pair shared by every user of the same program (<see cref="ShaderCompiler.CompileShader"/>): its native objects are
/// disposed when the last reference is released (<see cref="ShaderCompiler.Release"/>).
/// </summary>
internal sealed class SharedShaderProgram( (string Path, DateTime Modified, GraphicsBackend Backend) key, Veldrid.Shader vertex, Veldrid.Shader fragment )
{
	public (string Path, DateTime Modified, GraphicsBackend Backend) Key { get; } = key;
	public Veldrid.Shader Vertex { get; } = vertex;
	public Veldrid.Shader Fragment { get; } = fragment;
	public int References { get; set; }
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
		using var profile = LoadProfile.Measure( "shader-spirv" );
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
	private static readonly Dictionary<(string Path, DateTime Modified, GraphicsBackend Backend), SharedShaderProgram> natives = new();

	/// <summary>
	/// The native shaders of a shader file, shared by every caller while any holds them: the first call compiles the program (GLSL to SPIR-V, then the
	/// backend's own form) and makes its native shaders, later calls take a reference to the same objects, and each caller gives its reference back with
	/// <see cref="Release"/>. Native shader objects are immutable and every pipeline only reads them, so sharing them draws exactly the same; it saves
	/// the backend compile per material (on Direct3D 11 an HLSL compile of each stage, which made every material of a start or of a newly placed object
	/// cost tens of milliseconds; docs/ENGINE-PERFORMANCE.md, "Start and build hitches"). A changed file (hot reload) is another program.
	/// </summary>
	public static ShaderInfo CompileShader( string path )
	{
		var key = (Path.GetFullPath( path ), File.GetLastWriteTimeUtc( path ), Device.ResourceFactory.BackendType);
		(byte[] Vertex, byte[] Fragment, SpirvReflection Reflection) program;
		SharedShaderProgram? native;
		lock ( compiled )
		{
			if ( !compiled.TryGetValue( key, out program ) )
			{
				program = CompileProgram( path, GetCrossCompileTarget() );
				compiled[key] = program;
			}
			if ( !natives.TryGetValue( key, out native ) )
			{
				using var nativeProfile = LoadProfile.Measure( "shader-native" );
				LoadProfile.Count( "native shader programs" );
				LoadProfile.Count( "native shader programs of " + Path.GetFileName( path ) );
				var shaders = Device.ResourceFactory.CreateFromSpirv(
					new ShaderDescription( ShaderStages.Vertex, program.Vertex, "main" ),
					new ShaderDescription( ShaderStages.Fragment, program.Fragment, "main" ) );
				natives[key] = native = new SharedShaderProgram( key, shaders[0], shaders[1] );
			}
			native.References++;
		}

		return new ShaderInfo()
		{
			VertexShader = native.Vertex,
			FragmentShader = native.Fragment,
			Reflection = program.Reflection,
			Owner = native
		};
	}

	/// <summary>
	/// Gives back the reference <paramref name="info"/> holds (<see cref="CompileShader"/>); the native shaders are disposed after the last frame that may
	/// draw with them once no one holds them. Each reference is given back once.
	/// </summary>
	public static void Release( ShaderInfo info )
	{
		if ( info.Owner is not { } native )
			return;
		lock ( compiled )
		{
			if ( --native.References > 0 )
				return;
			if ( natives.TryGetValue( native.Key, out var current ) && ReferenceEquals( current, native ) )
				natives.Remove( native.Key );
		}
		void Dispose()
		{
			native.Vertex.Dispose();
			native.Fragment.Dispose();
		}
		if ( global::Global.Render is { } renderer )
			renderer.ScheduleDelete( Dispose );
		else
			Dispose();
	}

	/// <summary>How many callers hold the native shaders of <paramref name="info"/> (0 once all gave them back); for tests.</summary>
	internal static int References( ShaderInfo info ) => info.Owner?.References ?? 0;
}
