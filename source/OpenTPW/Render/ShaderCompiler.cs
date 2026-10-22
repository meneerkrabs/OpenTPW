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
		var vertexSpirv = CompileStage( vertexSource, path, ShaderStages.Vertex, compileOptions );
		var fragmentSpirv = CompileStage( fragmentSource, path, ShaderStages.Fragment, compileOptions );
		var compilationResult = SpirvCompilation.CompileVertexFragment( vertexSpirv, fragmentSpirv, target );

		Debug.Assert( HasSpirvHeader( vertexSpirv ) );
		Debug.Assert( HasSpirvHeader( fragmentSpirv ) );
		return (vertexSpirv, fragmentSpirv, compilationResult.Reflection);
	}

	/// <summary>
	/// Where compiled SPIR-V is kept between runs (the user's cache folder, set at the start), or null to compile every time (tests, tools). The GLSL to
	/// SPIR-V compile takes about 30 ms per stage even for a small shader, so a start used to spend some 350 ms (more on a slower PC) compiling the same
	/// sources again, and a park its first frames (docs/ENGINE-PERFORMANCE.md, "Start and build hitches").
	/// </summary>
	internal static string? SpirvCacheDirectory { get; set; }

	/// <summary>Cached files not written for this long are removed when a new one is written (an old install's sources, an old compiler).</summary>
	internal static readonly TimeSpan SpirvCacheLifetime = TimeSpan.FromDays( 30 );

	/// <summary>
	/// The SPIR-V of one stage: kept in <see cref="SpirvCacheDirectory"/> under a digest of everything the compile reads (the preprocessed source, the
	/// stage, the source name the debug information records, the options and the compiler's version), so a kept file is the bytes the compiler would
	/// make now. A missing, unreadable or malformed file is compiled again; the cache never changes what is drawn.
	/// </summary>
	internal static byte[] CompileStage( string source, string path, ShaderStages stage, GlslCompileOptions options )
	{
		var directory = SpirvCacheDirectory;
		var file = directory == null ? null : Path.Combine( directory, SpirvCacheKey( source, path, stage, options ) + ".spv" );
		if ( file != null && ReadCachedSpirv( file ) is { } cached )
		{
			LoadProfile.Count( "spirv cache hits" );
			return cached;
		}
		var spirv = SpirvCompilation.CompileGlslToSpirv( source, path, stage, options ).SpirvBytes;
		LoadProfile.Count( "spirv compiles" );
		if ( file != null )
			WriteCachedSpirv( directory!, file, spirv );
		return spirv;
	}

	internal static string SpirvCacheKey( string source, string path, ShaderStages stage, GlslCompileOptions options )
	{
		var compiler = typeof( SpirvCompilation ).Assembly;
		var compilerVersion = compiler.GetCustomAttributes( typeof( System.Reflection.AssemblyInformationalVersionAttribute ), false ) is [System.Reflection.AssemblyInformationalVersionAttribute informational, ..]
			? informational.InformationalVersion : compiler.GetName().Version?.ToString() ?? "";
		var text = $"opentpw-spirv-cache 1\n{compilerVersion}\n{stage}\n{options.Debug}\n{string.Join( ";", options.Macros?.Select( macro => macro.Name + "=" + macro.Value ) ?? [] )}\n{path}\n{source}";
		return Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( text ) ) ).ToLowerInvariant();
	}

	private static byte[]? ReadCachedSpirv( string file )
	{
		try
		{
			if ( !File.Exists( file ) )
				return null;
			var bytes = File.ReadAllBytes( file );
			// a SPIR-V module is whole words with the magic number first and a five-word header
			return bytes.Length >= 20 && bytes.Length % 4 == 0 && HasSpirvHeader( bytes ) ? bytes : null;
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return null;
		}
	}

	private static void WriteCachedSpirv( string directory, string file, byte[] spirv )
	{
		try
		{
			Directory.CreateDirectory( directory );
			// written beside and moved into place, so a reader never sees half a file
			var temporary = file + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
			File.WriteAllBytes( temporary, spirv );
			File.Move( temporary, file, overwrite: true );
			var expired = DateTime.UtcNow - SpirvCacheLifetime;
			foreach ( var old in Directory.EnumerateFiles( directory ) )
				if ( File.GetLastWriteTimeUtc( old ) < expired )
					File.Delete( old );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			Log?.Trace( $"Shader cache not written ({exception.Message}); the shaders are compiled at every start." );
		}
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
				var shaders = CreateNative( Device.ResourceFactory, program.Vertex, program.Fragment );
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
	/// The native shaders of a program, as Veldrid's <c>CreateFromSpirv</c> makes them. On Direct3D 11 with a cache folder the HLSL bytecode is kept between
	/// runs (<see cref="Direct3D11Bytecode"/>): Veldrid compiles the cross-compiled HLSL of each new shader with the system HLSL compiler, which was the
	/// slowest part of a start on Windows; the same compile is made here once and its bytecode handed to Veldrid, which takes bytecode as it is.
	/// </summary>
	internal static Veldrid.Shader[] CreateNative( ResourceFactory factory, byte[] vertexSpirv, byte[] fragmentSpirv )
	{
		if ( factory.BackendType == GraphicsBackend.Direct3D11 && OperatingSystem.IsWindows() && SpirvCacheDirectory is { } directory )
		{
			var hlsl = SpirvCompilation.CompileVertexFragment( vertexSpirv, fragmentSpirv, CrossCompileTarget.HLSL, new CrossCompileOptions() );
			var vertex = factory.CreateShader( new ShaderDescription( ShaderStages.Vertex, Direct3D11Bytecode.Get( directory, hlsl.VertexShader, ShaderStages.Vertex ), "main" ) );
			try
			{
				var fragment = factory.CreateShader( new ShaderDescription( ShaderStages.Fragment, Direct3D11Bytecode.Get( directory, hlsl.FragmentShader, ShaderStages.Fragment ), "main" ) );
				return [vertex, fragment];
			}
			catch
			{
				vertex.Dispose();
				throw;
			}
		}
		return factory.CreateFromSpirv(
			new ShaderDescription( ShaderStages.Vertex, vertexSpirv, "main" ),
			new ShaderDescription( ShaderStages.Fragment, fragmentSpirv, "main" ) );
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
