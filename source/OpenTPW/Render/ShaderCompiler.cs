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
		var vertexSpirv = CompileStage( vertexSource, path, ShaderStages.Vertex, compileOptions, readCache: true, out var keptVertex );
		var fragmentSpirv = CompileStage( fragmentSource, path, ShaderStages.Fragment, compileOptions, readCache: true, out var keptFragment );
		VertexFragmentCompilationResult compilationResult;
		try
		{
			compilationResult = SpirvCompilation.CompileVertexFragment( vertexSpirv, fragmentSpirv, target );
		}
		catch ( SpirvCompilationException exception ) when ( keptVertex != null || keptFragment != null )
		{
			// a kept stage the cross-compiler refuses (a file that is whole but not what the compiler made): forget it and compile from the source
			Log?.Trace( $"Shader cache: {Path.GetFileName( path )} was not usable ({exception.Message.Split( '\n' )[0]}); compiled again." );
			LoadProfile.Count( "spirv cache refused" );
			ShaderCache.Delete( keptVertex );
			ShaderCache.Delete( keptFragment );
			vertexSpirv = CompileStage( vertexSource, path, ShaderStages.Vertex, compileOptions, readCache: false, out _ );
			fragmentSpirv = CompileStage( fragmentSource, path, ShaderStages.Fragment, compileOptions, readCache: false, out _ );
			compilationResult = SpirvCompilation.CompileVertexFragment( vertexSpirv, fragmentSpirv, target );
		}

		Debug.Assert( HasSpirvHeader( vertexSpirv ) );
		Debug.Assert( HasSpirvHeader( fragmentSpirv ) );
		return (vertexSpirv, fragmentSpirv, compilationResult.Reflection);
	}

	/// <summary>
	/// Where compiled shaders are kept between runs (the user's cache folder, set at the start), or null to compile every time (tests, tools). The GLSL to
	/// SPIR-V compile takes about 30 ms per stage even for a small shader, so a start used to spend some 350 ms (more on a slower PC) compiling the same
	/// sources again, and a park its first frames (docs/ENGINE-PERFORMANCE.md, "Kept SPIR-V").
	/// </summary>
	internal static string? SpirvCacheDirectory { get; set; }

	/// <summary>
	/// The SPIR-V of one stage: kept in <see cref="SpirvCacheDirectory"/> (<see cref="ShaderCache"/>) under a digest of everything the compile reads (the
	/// preprocessed source, the stage, the source name the debug information records, the options and the compiler's managed and native versions), so a
	/// kept file is the bytes the compiler would make now. A missing, damaged or malformed file is compiled again; one the cross-compiler refuses is removed
	/// and compiled again by <see cref="CompileProgram"/>. <paramref name="kept"/> is the file the bytes came from, or null when they were compiled.
	/// </summary>
	internal static byte[] CompileStage( string source, string path, ShaderStages stage, GlslCompileOptions options, bool readCache, out string? kept )
	{
		kept = null;
		var directory = SpirvCacheDirectory;
		var file = directory == null ? null : Path.Combine( directory, SpirvCacheKey( source, path, stage, options ) + ".spv" );
		// a SPIR-V module is whole words with the magic number first and a five-word header
		if ( file != null && readCache && ShaderCache.Read( file, bytes => bytes.Length >= 20 && bytes.Length % 4 == 0 && HasSpirvHeader( bytes ) ) is { } cached )
		{
			LoadProfile.Count( "spirv cache hits" );
			kept = file;
			return cached;
		}
		var spirv = SpirvCompilation.CompileGlslToSpirv( source, path, stage, options ).SpirvBytes;
		LoadProfile.Count( "spirv compiles" );
		if ( file != null )
			ShaderCache.Write( directory!, file, spirv );
		return spirv;
	}

	/// <summary>The cache file of a stage of the shader file at <paramref name="path"/> as <see cref="CompileProgram"/> keeps it, or null without a cache; for tests.</summary>
	internal static string? SpirvCacheFile( string path, ShaderStages stage )
	{
		if ( SpirvCacheDirectory is not { } directory )
			return null;
		var preprocessed = ShaderPreprocessor.PreprocessShader( path );
		var source = stage == ShaderStages.Vertex ? preprocessed.VertexShader : preprocessed.FragmentShader;
		return Path.Combine( directory, SpirvCacheKey( source, path, stage, new GlslCompileOptions( true ) ) + ".spv" );
	}

	internal static string SpirvCacheKey( string source, string path, ShaderStages stage, GlslCompileOptions options )
	{
		var compiler = typeof( SpirvCompilation ).Assembly;
		var compilerVersion = compiler.GetCustomAttributes( typeof( System.Reflection.AssemblyInformationalVersionAttribute ), false ) is [System.Reflection.AssemblyInformationalVersionAttribute informational, ..]
			? informational.InformationalVersion : compiler.GetName().Version?.ToString() ?? "";
		var text = $"opentpw-spirv-cache 2\n{compilerVersion}\n{NativeCompilerIdentity}\n{stage}\n{options.Debug}\n{string.Join( ";", options.Macros?.Select( macro => macro.Name + "=" + macro.Value ) ?? [] )}\n{path}\n{source}";
		return Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( text ) ) ).ToLowerInvariant();
	}

	private static readonly Lazy<string> nativeCompilerIdentity = new( FindNativeCompilerIdentity );

	/// <summary>The name, size and time of the native compiler library (libveldrid-spirv) beside the game, or empty when it is not found there.</summary>
	internal static string NativeCompilerIdentity => nativeCompilerIdentity.Value;

	private static string FindNativeCompilerIdentity()
	{
		try
		{
			var root = AppContext.BaseDirectory;
			var runtimes = Path.Combine( root, "runtimes" );
			var candidates = Directory.EnumerateFiles( root, "libveldrid-spirv*" )
				.Concat( Directory.Exists( runtimes ) ? Directory.EnumerateFiles( runtimes, "libveldrid-spirv*", SearchOption.AllDirectories ) : [] )
				.OrderBy( candidate => candidate, StringComparer.Ordinal );
			return string.Join( ";", candidates.Select( candidate => new FileInfo( candidate ) ).Select( info => $"{info.Name}|{info.Length}|{info.LastWriteTimeUtc.Ticks}" ) );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return "";
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
			var kept = new List<string>();
			try
			{
				return CreateDirect3D11( factory, directory, hlsl, kept );
			}
			catch ( Exception exception ) when ( exception is not OutOfMemoryException && kept.Count > 0 )
			{
				// kept bytecode the device refuses (a whole file that is not what the compiler made): forget it and compile the HLSL again
				Log?.Trace( $"Shader cache: kept Direct3D 11 bytecode was not usable ({exception.Message}); compiled again." );
				LoadProfile.Count( "hlsl cache refused" );
				foreach ( var file in kept )
					ShaderCache.Delete( file );
				return CreateDirect3D11( factory, directory, hlsl, kept: null );
			}
		}
		return factory.CreateFromSpirv(
			new ShaderDescription( ShaderStages.Vertex, vertexSpirv, "main" ),
			new ShaderDescription( ShaderStages.Fragment, fragmentSpirv, "main" ) );
	}

	[System.Runtime.Versioning.SupportedOSPlatform( "windows" )]
	/// <summary>The program's two shaders from kept or compiled bytecode; <paramref name="kept"/> collects the files read (null: read none, compile both).</summary>
	private static Veldrid.Shader[] CreateDirect3D11( ResourceFactory factory, string directory, VertexFragmentCompilationResult hlsl, List<string>? kept )
	{
		var vertexBytecode = Direct3D11Bytecode.Get( directory, hlsl.VertexShader, ShaderStages.Vertex, kept != null, out var keptVertex );
		var fragmentBytecode = Direct3D11Bytecode.Get( directory, hlsl.FragmentShader, ShaderStages.Fragment, kept != null, out var keptFragment );
		foreach ( var file in new[] { keptVertex, keptFragment } )
			if ( file != null )
				kept!.Add( file );
		var vertex = factory.CreateShader( new ShaderDescription( ShaderStages.Vertex, vertexBytecode, "main" ) );
		try
		{
			var fragment = factory.CreateShader( new ShaderDescription( ShaderStages.Fragment, fragmentBytecode, "main" ) );
			return [vertex, fragment];
		}
		catch
		{
			vertex.Dispose();
			throw;
		}
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
