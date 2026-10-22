using System.Text;
using System.Text.Json;
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

/// <summary>One native shader pair shared by every user of the same program; disposed when the last reference is released.</summary>
internal sealed class SharedShaderProgram( string key, Veldrid.Shader vertex, Veldrid.Shader fragment )
{
	public string Key { get; } = key;
	public Veldrid.Shader Vertex { get; } = vertex;
	public Veldrid.Shader Fragment { get; } = fragment;
	public int References { get; set; }
}

/// <summary>
/// The browser's answer to the desktop ShaderCompiler: the native SPIR-V compiler does not run here,
/// so each shader comes precompiled to GLSL ES from content/shaders/web (docs/WEB.md).
/// </summary>
internal static class ShaderCompiler
{
	private sealed record Program( string Vertex, string Fragment, string[][] Names, SpirvReflection Reflection );

	private static readonly Dictionary<string, Program> loaded = new();
	private static readonly Dictionary<string, SharedShaderProgram> natives = new();

	/// <summary>
	/// The native shaders of a shader file, shared by every caller while any holds them, as on the desktop (docs/ENGINE-PERFORMANCE.md, "Start and build
	/// hitches"): WebGL compiles and links the GLSL of each new shader object, so one pair per program instead of one per material. Give each reference
	/// back with <see cref="Release"/>.
	/// </summary>
	public static ShaderInfo CompileShader( string path )
	{
		var full = Path.GetFullPath( path );
		if ( !loaded.TryGetValue( full, out var program ) )
			loaded[full] = program = Load( full );
		if ( !natives.TryGetValue( full, out var native ) )
		{
			var factory = Device.ResourceFactory;
			var vertex = factory.CreateShader( new ShaderDescription( ShaderStages.Vertex, Encoding.UTF8.GetBytes( program.Vertex ), "main" ) );
			var fragment = factory.CreateShader( new ShaderDescription( ShaderStages.Fragment, Encoding.UTF8.GetBytes( program.Fragment ), "main" ) );
			vertex.ResourceNames = fragment.ResourceNames = program.Names;
			natives[full] = native = new SharedShaderProgram( full, vertex, fragment );
		}
		native.References++;
		return new ShaderInfo { VertexShader = native.Vertex, FragmentShader = native.Fragment, Reflection = program.Reflection, Owner = native };
	}

	/// <summary>Gives back the reference <paramref name="info"/> holds; the last one disposes the native shaders after the last frame that may use them.</summary>
	public static void Release( ShaderInfo info )
	{
		if ( info.Owner is not { } native || --native.References > 0 )
			return;
		if ( natives.TryGetValue( native.Key, out var current ) && ReferenceEquals( current, native ) )
			natives.Remove( native.Key );
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

	/// <summary>How many callers hold the native shaders of <paramref name="info"/>; for tests.</summary>
	internal static int References( ShaderInfo info ) => info.Owner?.References ?? 0;

	private static Program Load( string shaderPath )
	{
		var path = Path.Combine( Path.GetDirectoryName( shaderPath )!, "web", Path.GetFileNameWithoutExtension( shaderPath ) + ".json" );
		using var document = JsonDocument.Parse( File.ReadAllBytes( path ) );
		var root = document.RootElement;
		var vertexElements = root.GetProperty( "vertexElements" ).EnumerateArray().Select( element => new VertexElementDescription(
			element.GetProperty( "name" ).GetString()!,
			Enum.Parse<VertexElementSemantic>( element.GetProperty( "semantic" ).GetString()! ),
			Enum.Parse<VertexElementFormat>( element.GetProperty( "format" ).GetString()! ),
			element.GetProperty( "offset" ).GetUInt32() ) ).ToArray();
		var layouts = root.GetProperty( "resourceLayouts" ).EnumerateArray().Select( layout => new ResourceLayoutDescription(
			layout.EnumerateArray().Select( element => new ResourceLayoutElementDescription(
				element.GetProperty( "name" ).GetString()!,
				Enum.Parse<ResourceKind>( element.GetProperty( "kind" ).GetString()! ),
				Enum.Parse<ShaderStages>( element.GetProperty( "stages" ).GetString()! ),
				Enum.Parse<ResourceLayoutElementOptions>( element.GetProperty( "options" ).GetString()! ) ) ).ToArray() ) ).ToArray();
		var names = layouts.Select( layout => layout.Elements.Select( element => element.Name ).ToArray() ).ToArray();
		return new Program( root.GetProperty( "vertex" ).GetString()!, root.GetProperty( "fragment" ).GetString()!, names, new SpirvReflection( vertexElements, layouts ) );
	}
}
