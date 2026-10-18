using System.Buffers.Binary;
using System.Numerics;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using System.Text;
using System.Text.Json;

namespace OpenTPW;

/// <summary>One node of a <see cref="GltfScene"/>: its parent-relative transform, optional mesh and the string, number and boolean <c>extras</c>.</summary>
/// <param name="Local">Parent-relative transform, row-vector convention (<c>world = local × parent world</c>, as MD2 nodes).</param>
/// <param name="Light">The <c>KHR_lights_punctual</c> light on this node, or null.</param>
public sealed record GltfNode( int Index, string Name, int Parent, IReadOnlyList<int> Children, Matrix4x4 Local, int Mesh,
	IReadOnlyDictionary<string, JsonElement> Extras, GltfLight? Light = null );

/// <summary>
/// A <c>KHR_lights_punctual</c> light: its <c>type</c> (<c>point</c>, <c>spot</c> or <c>directional</c>), linear colour (default white), <c>intensity</c> (the file's unit, candela or lux, kept as written)
/// and <c>range</c> in scene units (null when the file gives none, which glTF reads as unbounded).
/// </summary>
public sealed record GltfLight( string Type, NumericsVector3 Colour, float Intensity, float? Range );

/// <summary>One triangle list of a mesh, with per-vertex position, normal and first UV set.</summary>
public sealed record GltfPrimitive( NumericsVector3[] Positions, NumericsVector3[]? Normals, NumericsVector2[]? TexCoords, int[] Indices, int Material );

public sealed record GltfMesh( string Name, IReadOnlyList<GltfPrimitive> Primitives );

/// <summary>A material's base colour texture (image index or -1), its <c>extras</c> and whether the file marks it double-sided (the glTF default is false).</summary>
public sealed record GltfMaterial( string Name, int Image, IReadOnlyDictionary<string, JsonElement> Extras, bool DoubleSided = false );

/// <summary>An embedded image: its name, MIME type and encoded bytes (PNG or JPEG, as the exporter wrote them).</summary>
public sealed record GltfImage( string Name, string MimeType, byte[] Data );

/// <summary>
/// Bounded reader for binary glTF 2.0 (<c>.glb</c>) files as the Blender exporter writes them for the content kit
/// (tools/blender): one embedded buffer, triangle primitives, float positions/normals/UVs, embedded images and
/// <c>extras</c>. It reads only what an MD2 object needs; skins, morph targets, animations, sparse accessors and
/// external files are refused rather than ignored.
/// </summary>
public sealed class GltfScene
{
	public const int MaximumFileBytes = 64 * 1024 * 1024;
	/// <summary>The one glTF extension the reader accepts, as optional or required: point lights on nodes.</summary>
	public const string LightsExtension = "KHR_lights_punctual";
	private const uint GlbMagic = 0x46546C67; // "glTF"
	private const uint JsonChunk = 0x4E4F534A;
	private const uint BinaryChunk = 0x004E4942;

	public IReadOnlyList<GltfNode> Nodes { get; }
	public IReadOnlyList<int> Roots { get; }
	public IReadOnlyList<GltfMesh> Meshes { get; }
	public IReadOnlyList<GltfMaterial> Materials { get; }
	public IReadOnlyList<GltfImage> Images { get; }

	private GltfScene( IReadOnlyList<GltfNode> nodes, IReadOnlyList<int> roots, IReadOnlyList<GltfMesh> meshes,
		IReadOnlyList<GltfMaterial> materials, IReadOnlyList<GltfImage> images )
	{
		Nodes = nodes;
		Roots = roots;
		Meshes = meshes;
		Materials = materials;
		Images = images;
	}

	public static GltfScene Read( string path ) => Read( File.ReadAllBytes( path ) );

	public static GltfScene Read( byte[] data )
	{
		if ( data.Length > MaximumFileBytes )
			throw new InvalidDataException( "glTF file exceeds its size limit." );
		if ( data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian( data ) != GlbMagic )
			throw new InvalidDataException( "Not a binary glTF (.glb) file." );
		if ( BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 4 ) ) != 2 )
			throw new InvalidDataException( "Only glTF 2.0 is supported." );
		if ( BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 8 ) ) != data.Length )
			throw new InvalidDataException( "glTF length field does not match the file length." );

		ReadOnlyMemory<byte> json = default, binary = default;
		var offset = 12;
		while ( offset < data.Length )
		{
			if ( data.Length - offset < 8 )
				throw new InvalidDataException( "glTF chunk header is truncated." );
			var length = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset ) );
			var type = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset + 4 ) );
			if ( length > data.Length - offset - 8 )
				throw new InvalidDataException( "glTF chunk exceeds the file." );
			var chunk = new ReadOnlyMemory<byte>( data, offset + 8, (int)length );
			if ( type == JsonChunk && json.IsEmpty )
				json = chunk;
			else if ( type == BinaryChunk && binary.IsEmpty )
				binary = chunk;
			offset += 8 + (int)length;
		}
		if ( json.IsEmpty )
			throw new InvalidDataException( "glTF has no JSON chunk." );

		using var document = JsonDocument.Parse( json, new JsonDocumentOptions { MaxDepth = 64 } );
		return new Reader( document.RootElement, binary ).Read();
	}

	private sealed class Reader( JsonElement root, ReadOnlyMemory<byte> binary )
	{
		public GltfScene Read()
		{
			Refuse( "skins", "skinned meshes" );
			Refuse( "animations", "animations" );
			// KHR_lights_punctual is read (point lights become light dummies, docs/LIGHTS.md); any other required extension is refused
			if ( root.TryGetProperty( "extensionsRequired", out var required ) && required.EnumerateArray().Any( extension => extension.GetString() != LightsExtension ) )
				throw new InvalidDataException( $"glTF requires extensions OpenTPW does not read: {required}." );
			if ( root.TryGetProperty( "buffers", out var buffers ) )
			{
				if ( buffers.GetArrayLength() != 1 || buffers[0].TryGetProperty( "uri", out _ ) )
					throw new InvalidDataException( "glTF must use exactly one embedded binary buffer." );
				if ( buffers[0].GetProperty( "byteLength" ).GetInt64() > binary.Length )
					throw new InvalidDataException( "glTF buffer is longer than its binary chunk." );
			}

			var images = Array( "images" ).Select( ( image, index ) => ReadImage( image, index ) ).ToList();
			var materials = Array( "materials" ).Select( ( material, index ) => ReadMaterial( material, index, images.Count ) ).ToList();
			var meshes = Array( "meshes" ).Select( ( mesh, index ) => ReadMesh( mesh, index, materials.Count ) ).ToList();
			var lights = ReadLights();
			var nodes = ReadNodes( meshes.Count, lights );
			var roots = ReadRoots( nodes );
			return new GltfScene( nodes, roots, meshes, materials, images );
		}

		private void Refuse( string property, string what )
		{
			if ( root.TryGetProperty( property, out var value ) && value.GetArrayLength() > 0 )
				throw new InvalidDataException( $"glTF contains {what}, which the content kit does not import." );
		}

		private IEnumerable<JsonElement> Array( string property ) =>
			root.TryGetProperty( property, out var value ) ? value.EnumerateArray() : Enumerable.Empty<JsonElement>();

		private static string Name( JsonElement element, string fallback ) =>
			element.TryGetProperty( "name", out var name ) ? name.GetString() ?? fallback : fallback;

		private static IReadOnlyDictionary<string, JsonElement> Extras( JsonElement element )
		{
			var result = new Dictionary<string, JsonElement>( StringComparer.Ordinal );
			if ( element.TryGetProperty( "extras", out var extras ) && extras.ValueKind == JsonValueKind.Object )
				foreach ( var property in extras.EnumerateObject() )
					result[property.Name] = property.Value.Clone();
			return result;
		}

		private GltfImage ReadImage( JsonElement image, int index )
		{
			if ( image.TryGetProperty( "uri", out _ ) || !image.TryGetProperty( "bufferView", out var view ) )
				throw new InvalidDataException( $"glTF image {index} is not embedded in the binary buffer." );
			var mime = image.TryGetProperty( "mimeType", out var type ) ? type.GetString() ?? "" : "";
			return new GltfImage( Name( image, $"image{index}" ), mime, ViewBytes( view.GetInt32() ).ToArray() );
		}

		private GltfMaterial ReadMaterial( JsonElement material, int index, int imageCount )
		{
			var image = -1;
			if ( material.TryGetProperty( "pbrMetallicRoughness", out var pbr ) && pbr.TryGetProperty( "baseColorTexture", out var baseColour ) )
			{
				var texture = Array( "textures" ).ElementAtOrDefault( baseColour.GetProperty( "index" ).GetInt32() );
				if ( texture.ValueKind != JsonValueKind.Object || !texture.TryGetProperty( "source", out var source ) )
					throw new InvalidDataException( $"glTF material {index} names a texture without an image." );
				image = source.GetInt32();
				if ( image < 0 || image >= imageCount )
					throw new InvalidDataException( $"glTF material {index} names image {image}, which does not exist." );
			}
			var doubleSided = material.TryGetProperty( "doubleSided", out var sided ) && sided.ValueKind == JsonValueKind.True;
			return new GltfMaterial( Name( material, $"material{index}" ), image, Extras( material ), doubleSided );
		}

		private GltfMesh ReadMesh( JsonElement mesh, int index, int materialCount )
		{
			var primitives = new List<GltfPrimitive>();
			foreach ( var primitive in mesh.GetProperty( "primitives" ).EnumerateArray() )
			{
				if ( primitive.TryGetProperty( "mode", out var mode ) && mode.GetInt32() != 4 )
					throw new InvalidDataException( $"glTF mesh {index} has a primitive that is not a triangle list." );
				if ( primitive.TryGetProperty( "targets", out _ ) )
					throw new InvalidDataException( $"glTF mesh {index} has morph targets, which the content kit does not import." );
				var attributes = primitive.GetProperty( "attributes" );
				var positions = Vectors3( attributes.GetProperty( "POSITION" ).GetInt32() );
				var normals = attributes.TryGetProperty( "NORMAL", out var normal ) ? Vectors3( normal.GetInt32() ) : null;
				var uvs = attributes.TryGetProperty( "TEXCOORD_0", out var uv ) ? Vectors2( uv.GetInt32() ) : null;
				if ( normals != null && normals.Length != positions.Length || uvs != null && uvs.Length != positions.Length )
					throw new InvalidDataException( $"glTF mesh {index} has attribute arrays of different lengths." );
				var indices = primitive.TryGetProperty( "indices", out var indexAccessor )
					? Indices( indexAccessor.GetInt32() )
					: Enumerable.Range( 0, positions.Length ).ToArray();
				if ( indices.Length % 3 != 0 || indices.Any( i => i < 0 || i >= positions.Length ) )
					throw new InvalidDataException( $"glTF mesh {index} has an index outside its vertices or an incomplete triangle." );
				var material = primitive.TryGetProperty( "material", out var materialIndex ) ? materialIndex.GetInt32() : -1;
				if ( material >= materialCount )
					throw new InvalidDataException( $"glTF mesh {index} names material {material}, which does not exist." );
				primitives.Add( new GltfPrimitive( positions, normals, uvs, indices, material ) );
			}
			return new GltfMesh( Name( mesh, $"mesh{index}" ), primitives );
		}

		/// <summary>The scene's <c>KHR_lights_punctual</c> light definitions, in file order (empty without the extension).</summary>
		private List<GltfLight> ReadLights()
		{
			var lights = new List<GltfLight>();
			if ( !root.TryGetProperty( "extensions", out var extensions ) || !extensions.TryGetProperty( LightsExtension, out var extension ) || !extension.TryGetProperty( "lights", out var list ) )
				return lights;
			foreach ( var light in list.EnumerateArray() )
			{
				var type = light.TryGetProperty( "type", out var typeValue ) ? typeValue.GetString() ?? "" : "";
				var colour = Floats( light, "color", 3, [1, 1, 1] );
				var intensity = light.TryGetProperty( "intensity", out var intensityValue ) ? intensityValue.GetSingle() : 1f;
				float? range = light.TryGetProperty( "range", out var rangeValue ) ? rangeValue.GetSingle() : null;
				if ( type.Length == 0 || colour.Any( channel => !float.IsFinite( channel ) ) || !float.IsFinite( intensity ) || ( range is { } value && !float.IsFinite( value ) ) )
					throw new InvalidDataException( $"glTF light {lights.Count} is malformed (a type and finite colour, intensity and range are needed)." );
				lights.Add( new GltfLight( type, new NumericsVector3( colour[0], colour[1], colour[2] ), intensity, range ) );
			}
			return lights;
		}

		private List<GltfNode> ReadNodes( int meshCount, List<GltfLight> lights )
		{
			var elements = Array( "nodes" ).ToList();
			var parents = Enumerable.Repeat( -1, elements.Count ).ToArray();
			var children = new List<int>[elements.Count];
			for ( var i = 0; i < elements.Count; i++ )
			{
				children[i] = elements[i].TryGetProperty( "children", out var list ) ? list.EnumerateArray().Select( c => c.GetInt32() ).ToList() : new List<int>();
				foreach ( var child in children[i] )
				{
					if ( child < 0 || child >= elements.Count || parents[child] != -1 || child == i )
						throw new InvalidDataException( $"glTF node {i} has an invalid or shared child {child}." );
					parents[child] = i;
				}
			}
			var nodes = new List<GltfNode>();
			for ( var i = 0; i < elements.Count; i++ )
			{
				var element = elements[i];
				var mesh = element.TryGetProperty( "mesh", out var meshIndex ) ? meshIndex.GetInt32() : -1;
				if ( mesh >= meshCount )
					throw new InvalidDataException( $"glTF node {i} names mesh {mesh}, which does not exist." );
				GltfLight? light = null;
				if ( element.TryGetProperty( "extensions", out var nodeExtensions ) && nodeExtensions.TryGetProperty( LightsExtension, out var nodeLight ) )
				{
					var lightIndex = nodeLight.TryGetProperty( "light", out var lightValue ) ? lightValue.GetInt32() : -1;
					if ( lightIndex < 0 || lightIndex >= lights.Count )
						throw new InvalidDataException( $"glTF node {i} names light {lightIndex}, which does not exist." );
					light = lights[lightIndex];
				}
				nodes.Add( new GltfNode( i, Name( element, $"node{i}" ), parents[i], children[i], LocalTransform( element ), mesh, Extras( element ), light ) );
			}
			return nodes;
		}

		private List<int> ReadRoots( List<GltfNode> nodes )
		{
			var scenes = Array( "scenes" ).ToList();
			var sceneIndex = root.TryGetProperty( "scene", out var scene ) ? scene.GetInt32() : 0;
			var roots = scenes.Count > sceneIndex && scenes[sceneIndex].TryGetProperty( "nodes", out var list )
				? list.EnumerateArray().Select( n => n.GetInt32() ).ToList()
				: nodes.Where( n => n.Parent < 0 ).Select( n => n.Index ).ToList();
			if ( roots.Any( r => r < 0 || r >= nodes.Count || nodes[r].Parent >= 0 ) )
				throw new InvalidDataException( "glTF scene names a node that is not a root." );
			// A cycle leaves nodes unreachable from every root.
			var seen = new HashSet<int>();
			var stack = new Stack<int>( roots );
			while ( stack.Count > 0 )
				if ( seen.Add( stack.Peek() ) )
					foreach ( var child in nodes[stack.Pop()].Children )
						stack.Push( child );
				else
					stack.Pop();
			if ( nodes.Any( n => n.Parent < 0 && !roots.Contains( n.Index ) ) || seen.Count != nodes.Count )
				throw new InvalidDataException( "glTF node hierarchy is not a forest reachable from the scene's roots." );
			return roots;
		}

		private static Matrix4x4 LocalTransform( JsonElement node )
		{
			if ( node.TryGetProperty( "matrix", out var matrix ) )
			{
				// glTF stores column-major column vectors; the same 16 numbers read row-major are the row-vector matrix.
				var m = matrix.EnumerateArray().Select( v => v.GetSingle() ).ToArray();
				if ( m.Length != 16 )
					throw new InvalidDataException( "glTF node matrix does not have 16 elements." );
				return new Matrix4x4( m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15] );
			}
			var t = Floats( node, "translation", 3, [0, 0, 0] );
			var r = Floats( node, "rotation", 4, [0, 0, 0, 1] );
			var s = Floats( node, "scale", 3, [1, 1, 1] );
			return Matrix4x4.CreateScale( s[0], s[1], s[2] )
				* Matrix4x4.CreateFromQuaternion( new Quaternion( r[0], r[1], r[2], r[3] ) )
				* Matrix4x4.CreateTranslation( t[0], t[1], t[2] );
		}

		private static float[] Floats( JsonElement node, string property, int count, float[] fallback )
		{
			if ( !node.TryGetProperty( property, out var value ) )
				return fallback;
			var result = value.EnumerateArray().Select( v => v.GetSingle() ).ToArray();
			if ( result.Length != count )
				throw new InvalidDataException( $"glTF node {property} does not have {count} elements." );
			return result;
		}

		private ReadOnlySpan<byte> ViewBytes( int viewIndex )
		{
			var view = Array( "bufferViews" ).ElementAtOrDefault( viewIndex );
			if ( view.ValueKind != JsonValueKind.Object || view.GetProperty( "buffer" ).GetInt32() != 0 )
				throw new InvalidDataException( $"glTF buffer view {viewIndex} does not exist or is not in the binary buffer." );
			var offset = view.TryGetProperty( "byteOffset", out var o ) ? o.GetInt64() : 0;
			var length = view.GetProperty( "byteLength" ).GetInt64();
			if ( offset < 0 || length < 0 || offset + length > binary.Length )
				throw new InvalidDataException( $"glTF buffer view {viewIndex} exceeds the binary buffer." );
			return binary.Span.Slice( (int)offset, (int)length );
		}

		/// <summary>The accessor's elements as raw bytes per element, after checking type, component type and bounds.</summary>
		private (byte[] Bytes, int Count, int ComponentType, int Stride) Accessor( int index, string type )
		{
			var accessor = Array( "accessors" ).ElementAtOrDefault( index );
			if ( accessor.ValueKind != JsonValueKind.Object )
				throw new InvalidDataException( $"glTF accessor {index} does not exist." );
			if ( accessor.TryGetProperty( "sparse", out _ ) || !accessor.TryGetProperty( "bufferView", out var viewIndex ) )
				throw new InvalidDataException( $"glTF accessor {index} is sparse or has no buffer view." );
			if ( accessor.GetProperty( "type" ).GetString() != type )
				throw new InvalidDataException( $"glTF accessor {index} is not of type {type}." );
			var componentType = accessor.GetProperty( "componentType" ).GetInt32();
			var componentSize = componentType switch { 5121 => 1, 5123 => 2, 5125 or 5126 => 4, _ => throw new InvalidDataException( $"glTF accessor {index} has an unsupported component type {componentType}." ) };
			var components = type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, _ => 4 };
			var elementSize = componentSize * components;
			var count = accessor.GetProperty( "count" ).GetInt32();
			var offset = accessor.TryGetProperty( "byteOffset", out var o ) ? o.GetInt32() : 0;
			var view = Array( "bufferViews" ).ElementAt( viewIndex.GetInt32() );
			var stride = view.TryGetProperty( "byteStride", out var s ) ? s.GetInt32() : elementSize;
			var bytes = ViewBytes( viewIndex.GetInt32() );
			if ( count < 0 || offset < 0 || stride < elementSize || count > 0 && (long)offset + (long)(count - 1) * stride + elementSize > bytes.Length )
				throw new InvalidDataException( $"glTF accessor {index} exceeds its buffer view." );
			var result = new byte[count * elementSize];
			for ( var i = 0; i < count; i++ )
				bytes.Slice( offset + i * stride, elementSize ).CopyTo( result.AsSpan( i * elementSize ) );
			return (result, count, componentType, elementSize);
		}

		private NumericsVector3[] Vectors3( int index )
		{
			var (bytes, count, componentType, _) = Accessor( index, "VEC3" );
			if ( componentType != 5126 )
				throw new InvalidDataException( $"glTF accessor {index} is not float." );
			return Enumerable.Range( 0, count ).Select( i => new NumericsVector3( Float( bytes, i * 3 ), Float( bytes, i * 3 + 1 ), Float( bytes, i * 3 + 2 ) ) ).ToArray();
		}

		private NumericsVector2[] Vectors2( int index )
		{
			var (bytes, count, componentType, _) = Accessor( index, "VEC2" );
			if ( componentType != 5126 )
				throw new InvalidDataException( $"glTF accessor {index} is not float." );
			return Enumerable.Range( 0, count ).Select( i => new NumericsVector2( Float( bytes, i * 2 ), Float( bytes, i * 2 + 1 ) ) ).ToArray();
		}

		private int[] Indices( int index )
		{
			var (bytes, count, componentType, _) = Accessor( index, "SCALAR" );
			return componentType switch
			{
				5121 => bytes.Select( b => (int)b ).ToArray(),
				5123 => Enumerable.Range( 0, count ).Select( i => (int)BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( i * 2 ) ) ).ToArray(),
				5125 => Enumerable.Range( 0, count ).Select( i => checked((int)BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( i * 4 ) )) ).ToArray(),
				_ => throw new InvalidDataException( $"glTF index accessor {index} is not an unsigned integer." ),
			};
		}

		private static float Float( byte[] bytes, int index ) => BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( index * 4 ) );
	}
}
