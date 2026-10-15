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

	public readonly Veldrid.Shader[] ShaderProgram => [VertexShader, FragmentShader];
}

/// <summary>
/// The browser's answer to the desktop ShaderCompiler: the native SPIR-V compiler does not run here,
/// so each shader comes precompiled to GLSL ES from content/shaders/web (docs/WEB.md).
/// </summary>
internal static class ShaderCompiler
{
	private sealed record Program( string Vertex, string Fragment, string[][] Names, SpirvReflection Reflection );

	private static readonly Dictionary<string, Program> loaded = new();

	public static ShaderInfo CompileShader( string path )
	{
		var full = Path.GetFullPath( path );
		if ( !loaded.TryGetValue( full, out var program ) )
			loaded[full] = program = Load( full );
		var factory = Device.ResourceFactory;
		var vertex = factory.CreateShader( new ShaderDescription( ShaderStages.Vertex, Encoding.UTF8.GetBytes( program.Vertex ), "main" ) );
		var fragment = factory.CreateShader( new ShaderDescription( ShaderStages.Fragment, Encoding.UTF8.GetBytes( program.Fragment ), "main" ) );
		vertex.ResourceNames = fragment.ResourceNames = program.Names;
		return new ShaderInfo { VertexShader = vertex, FragmentShader = fragment, Reflection = program.Reflection };
	}

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
