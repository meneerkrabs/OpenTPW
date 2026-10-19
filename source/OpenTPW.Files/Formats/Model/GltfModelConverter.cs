using System.Text.Json;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>An image of the converted scene and the texture name (without extension) the model refers to it by.</summary>
public sealed record GltfConvertedTexture( string Name, GltfImage Image );

/// <summary>The result of <see cref="GltfModelConverter.Convert"/>: the model to write, the textures it names and what the converter changed or could not carry over.</summary>
public sealed class GltfModelConversion
{
	internal GltfModelConversion( ModelBuilder model, IReadOnlyList<GltfConvertedTexture> textures, IReadOnlyList<string> warnings )
	{
		Model = model;
		Textures = textures;
		Warnings = warnings;
	}

	public ModelBuilder Model { get; }
	/// <summary>The images the model's materials use, one per texture name; each is written as <c>textures/&lt;Name&gt;.tga</c>.</summary>
	public IReadOnlyList<GltfConvertedTexture> Textures { get; }
	public IReadOnlyList<string> Warnings { get; }
}

public sealed class GltfConversionOptions
{
	/// <summary>The name of the dummy root added when the scene has several roots; made unique against the node names.</summary>
	public string RootName { get; set; } = "root";
	/// <summary>The 20-byte source name of the model header.</summary>
	public string SourceName { get; set; } = "";
}

/// <summary>
/// Converts a <see cref="GltfScene"/> (what the Blender exporter writes, tools/blender) into a <see cref="ModelBuilder"/>.
/// <para>
/// <b>Conventions</b> (evidence in docs/CONTENT-KIT.md, "Converter conventions"). glTF is right-handed, Y up, X right, V = 0 at the top of the
/// image, counter-clockwise front faces. MD2 is a <b>left-handed</b> Y-up system whose texture V runs bottom-up (V = 1 is the top row of the
/// decoded image) and whose outward triangles wind so that <c>cross(p1 - p0, p2 - p0)</c> points outward. The converter therefore maps
/// <c>(x, y, z)</c> to <c>(x, y, -z)</c> (Blender's <c>(x, y, z)</c> becomes MD2 <c>(x, z, y)</c>: the footprint of docs/OBJECTS.md, text read
/// from above, comes out un-mirrored), reverses every triangle's winding, conjugates node matrices by the same reflection and stores
/// <c>V = 1 - v</c>. Normals are reflected like positions.
/// </para>
/// <para>
/// Structure: one MD2 root (a dummy named <see cref="GltfConversionOptions.RootName"/> when the scene has several), a node per glTF node
/// (mesh nodes for nodes with a mesh, dummies otherwise) with its parent-relative transform, one MD2 mesh per mesh node, one MD2 material per
/// glTF material used by the node's primitives (primitives of the same material merge), corners de-duplicated per material. Material extras
/// <c>opentpw_wrap</c> (default true) and <c>opentpw_self_lit</c> (default false) set the slot wrap flags and material word +0xC bit 0x1; node
/// extras <c>opentpw_head</c> make head records (default type 0xB1).
/// </para>
/// Everything the MD2 format cannot hold is refused with the node, material or image named: no UVs, a material without a texture, more
/// than 65535 corners, positions or faces in a mesh, texture names over 15 characters, non-ASCII or duplicate node names, non-finite numbers,
/// head ids that are not 1..n.
/// </summary>
public static class GltfModelConverter
{
	public const string HeadExtra = "opentpw_head";
	/// <summary>[EXT:object-lights] Node extra: the light number of a <c>KHR_lights_punctual</c> point light's dummy (an integer from 0); a point light without it is skipped with a warning.</summary>
	public const string LightExtra = "opentpw_light";
	/// <summary>[EXT:object-lights] Node extra: the radius of that light in model units (1 to 65535), for exporters that cannot write a glTF <c>range</c> (Blender's lights have none).</summary>
	public const string LightRadiusExtra = "opentpw_light_radius";
	public const string WrapExtra = "opentpw_wrap";
	public const string SelfLitExtra = "opentpw_self_lit";
	private const int MaximumCount = ushort.MaxValue;

	/// <summary>The glTF-to-MD2 position map: the mirror that turns the right-handed glTF frame into MD2's left-handed one.</summary>
	public static NVector3 ToMd2Position( NVector3 gltf ) => new( gltf.X, gltf.Y, -gltf.Z );

	/// <summary>A glTF V coordinate as MD2 stores it (V runs bottom-up).</summary>
	public static NVector2 ToMd2TexCoord( NVector2 gltf ) => new( gltf.X, 1 - gltf.Y );

	/// <summary>A parent-relative row-vector matrix conjugated by the position map (<c>R M R</c> with <c>R = diag(1, 1, -1)</c>): the entries that mix Z with X, Y or the translation change sign.</summary>
	public static NMatrix4x4 ToMd2Matrix( NMatrix4x4 m ) => new(
		m.M11, m.M12, -m.M13, m.M14,
		m.M21, m.M22, -m.M23, m.M24,
		-m.M31, -m.M32, m.M33, -m.M34,
		m.M41, m.M42, -m.M43, m.M44 );

	public static GltfModelConversion Convert( GltfScene scene, GltfConversionOptions? options = null )
	{
		ArgumentNullException.ThrowIfNull( scene );
		options ??= new GltfConversionOptions();
		if ( scene.Nodes.Count == 0 || scene.Roots.Count == 0 )
			throw new InvalidDataException( "The glTF scene has no nodes." );

		var warnings = new List<string>();
		var names = CheckNodeNames( scene );
		var heads = CheckHeads( scene );
		var lights = CheckLights( scene, heads, warnings );
		var textures = new TextureTable( scene );

		var model = new ModelBuilder { SourceName = options.SourceName };
		ModelNodeBuilder? rootParent = null;
		if ( scene.Roots.Count > 1 )
		{
			var rootName = options.RootName;
			for ( var suffix = 1; names.Contains( rootName ); suffix++ )
				rootName = options.RootName + suffix;
			ModelBuilder.CheckPrintable( rootName, "Root name", 255 );
			rootParent = model.AddDummy( rootName );
		}

		var warnedMaterials = new HashSet<int>();
		void Add( GltfNode node, ModelNodeBuilder? parent )
		{
			CheckFinite( node.Local, $"Node '{node.Name}' has a transform that is not finite." );
			var local = ToMd2Matrix( node.Local );
			ModelNodeBuilder built;
			if ( node.Mesh >= 0 )
			{
				built = model.AddMesh( node.Name, parent, local );
				BuildMesh( scene, node, built, textures, warnedMaterials, warnings );
			}
			else
			{
				built = model.AddDummy( node.Name, parent, local );
			}
			if ( heads.TryGetValue( node.Index, out var head ) )
				built.AddHead( head );
			if ( lights.TryGetValue( node.Index, out var light ) )
				built.AddLight( light.Number, light.Colour, light.RadiusModelUnits );
			foreach ( var child in node.Children )
				Add( scene.Nodes[child], built );
		}
		foreach ( var root in scene.Roots )
			Add( scene.Nodes[root], rootParent );

		return new GltfModelConversion( model, textures.Used, warnings );
	}

	private static HashSet<string> CheckNodeNames( GltfScene scene )
	{
		var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var node in scene.Nodes )
		{
			Check( () => ModelBuilder.CheckPrintable( node.Name, "Node name", 255 ), $"Node {node.Index}" );
			if ( node.Name.Length == 0 )
				throw new InvalidDataException( $"Node {node.Index} has no name." );
			if ( !names.Add( node.Name ) )
				throw new InvalidDataException( $"Two nodes are named '{node.Name}' (names are compared without case); MD2 node names must be unique." );
		}
		return names;
	}

	/// <summary>The head id of each node with an <c>opentpw_head</c> extra; ids must be unique integers running from 1.</summary>
	private static Dictionary<int, int> CheckHeads( GltfScene scene )
	{
		var heads = new Dictionary<int, int>();
		foreach ( var node in scene.Nodes )
		{
			if ( !node.Extras.TryGetValue( HeadExtra, out var value ) )
				continue;
			if ( value.ValueKind != JsonValueKind.Number || !value.TryGetInt32( out var id ) || id < 1 )
				throw new InvalidDataException( $"Node '{node.Name}': {HeadExtra} must be an integer of 1 or more." );
			heads[node.Index] = id;
		}
		var ids = heads.Values.OrderBy( id => id ).ToArray();
		for ( var index = 0; index < ids.Length; index++ )
		{
			if ( ids[index] != index + 1 )
				throw new InvalidDataException( $"Head ids must be unique and run from 1 without a gap (the ride script counts them from 1 until one is missing); found {string.Join( ", ", ids )}." );
		}
		return heads;
	}

	private readonly record struct LightDummy( int Number, NVector3 Colour, int RadiusModelUnits );

	/// <summary>
	/// [EXT:object-lights] The point lights of the scene as light dummies (docs/LIGHTS.md). Only a point light on a node with the extra <c>opentpw_light</c> (the light number, an integer from 0) becomes a dummy:
	/// an exporter writes every light of a scene (Blender's default scene has one) and most are lights for looking at the model, not lights of the object. A point light without the extra is skipped with a
	/// warning. The glTF colour is the base colour, the <c>range</c> (or the extra <c>opentpw_light_radius</c>) the radius in model units (one scene unit is one model unit), none meaning the default of 100. The
	/// glTF intensity is not carried (its unit, candela or lux, differs between exporters): scale the colour, and a script's SETLIGHT dims it. Spot and directional lights have no counterpart and are skipped
	/// with a warning.
	/// </summary>
	private static Dictionary<int, LightDummy> CheckLights( GltfScene scene, Dictionary<int, int> heads, List<string> warnings )
	{
		var result = new Dictionary<int, LightDummy>();
		var used = new HashSet<int>();
		var intensityWarned = false;
		foreach ( var node in scene.Nodes )
		{
			var hasNumber = node.Extras.TryGetValue( LightExtra, out var value );
			if ( node.Light is not { } light )
			{
				if ( hasNumber )
					throw new InvalidDataException( $"Node '{node.Name}': {LightExtra} needs a KHR_lights_punctual point light on the node." );
				continue;
			}
			if ( !string.Equals( light.Type, "point", StringComparison.Ordinal ) )
			{
				warnings.Add( $"Node '{node.Name}': a {light.Type} light is skipped; only point lights become light dummies." );
				continue;
			}
			if ( !hasNumber )
			{
				warnings.Add( $"Node '{node.Name}': a point light without the extra {LightExtra} is skipped; give it the light number (an integer from 0) to make it a light of the object." );
				continue;
			}
			if ( value.ValueKind != JsonValueKind.Number || !value.TryGetInt32( out var number ) || number < 0 || number > short.MaxValue )
				throw new InvalidDataException( $"Node '{node.Name}': {LightExtra} must be an integer from 0 to 32767." );
			if ( !used.Add( number ) )
				throw new InvalidDataException( $"Light number {number} is used by two nodes; light numbers must be unique." );
			if ( heads.ContainsKey( node.Index ) )
				throw new InvalidDataException( $"Node '{node.Name}' is a rider head and a light; a node has one attribute record." );
			var radius = 0;
			if ( node.Extras.TryGetValue( LightRadiusExtra, out var radiusValue ) )
			{
				if ( radiusValue.ValueKind != JsonValueKind.Number || !radiusValue.TryGetInt32( out radius ) || radius < 1 || radius > ushort.MaxValue )
					throw new InvalidDataException( $"Node '{node.Name}': {LightRadiusExtra} must be an integer from 1 to 65535 (model units)." );
			}
			else if ( light.Range is { } range )
			{
				if ( range < 1 )
					warnings.Add( $"Node '{node.Name}': the light range {range} is below one model unit; the default radius of 100 model units is used." );
				else
					radius = (int)Math.Clamp( MathF.Round( range ), 1f, ushort.MaxValue );
			}
			var colour = new NVector3( Math.Clamp( light.Colour.X, 0f, 1f ), Math.Clamp( light.Colour.Y, 0f, 1f ), Math.Clamp( light.Colour.Z, 0f, 1f ) );
			if ( colour != light.Colour )
				warnings.Add( $"Node '{node.Name}': the light colour is outside 0..1 and is clamped (a model's base colour has channels from 0 to 1; a script's SETLIGHT and COLOURLIGHT can go beyond)." );
			if ( !intensityWarned && light.Intensity != 1f )
			{
				intensityWarned = true;
				warnings.Add( "The glTF light intensity is not carried (its unit differs between exporters); the colour is the light's base colour and a script's SETLIGHT dims it." );
			}
			result[node.Index] = new LightDummy( number, colour, radius );
		}
		return result;
	}

	private static void BuildMesh( GltfScene scene, GltfNode node, ModelNodeBuilder built, TextureTable textures, HashSet<int> warnedMaterials, List<string> warnings )
	{
		var mesh = scene.Meshes[node.Mesh];
		if ( mesh.Primitives.Count == 0 )
			throw new InvalidDataException( $"Node '{node.Name}': its mesh has no primitives." );

		var materialOrder = new List<int>();
		foreach ( var primitive in mesh.Primitives )
		{
			if ( primitive.Material < 0 )
				throw new InvalidDataException( $"Node '{node.Name}': a primitive has no material. Every primitive needs a material with a base colour texture." );
			if ( !materialOrder.Contains( primitive.Material ) )
				materialOrder.Add( primitive.Material );
		}

		var positions = new HashSet<(int, int, int)>();
		var corners = 0;
		var faces = 0;
		foreach ( var materialIndex in materialOrder )
		{
			var gltfMaterial = scene.Materials[materialIndex];
			if ( gltfMaterial.Image < 0 )
				throw new InvalidDataException( $"Material '{gltfMaterial.Name}' (node '{node.Name}') has no base colour texture. Every material needs one; use a small image for a flat colour." );
			if ( gltfMaterial.DoubleSided && warnedMaterials.Add( materialIndex ) )
				warnings.Add( $"Material '{gltfMaterial.Name}' is double-sided; OpenTPW culls back faces, so only the front side of each triangle is drawn. Turn on backface culling for the material, or model the back side." );
			var wrap = BoolExtra( gltfMaterial, WrapExtra, true );
			var material = built.Mesh!.AddMaterial( textures.NameOf( gltfMaterial.Image ), wrap );
			material.SelfLit = BoolExtra( gltfMaterial, SelfLitExtra, false );

			var cornerOf = new Dictionary<(int, int, int, int, int, int, int, int), int>();
			foreach ( var primitive in mesh.Primitives.Where( primitive => primitive.Material == materialIndex ) )
			{
				if ( primitive.TexCoords == null )
					throw new InvalidDataException( $"Node '{node.Name}': a primitive has no UV coordinates (TEXCOORD_0). Unwrap the mesh in Blender." );
				for ( var vertex = 0; vertex < primitive.Positions.Length; vertex++ )
				{
					if ( !IsFinite( primitive.Positions[vertex] ) || primitive.Normals != null && !IsFinite( primitive.Normals[vertex] ) || !IsFinite( primitive.TexCoords[vertex] ) )
						throw new InvalidDataException( $"Node '{node.Name}': vertex {vertex} has a position, normal or UV that is not finite." );
				}

				int Corner( NVector3 position, NVector3 normal, NVector2 uv )
				{
					var key = (Bits( position.X ), Bits( position.Y ), Bits( position.Z ), Bits( normal.X ), Bits( normal.Y ), Bits( normal.Z ), Bits( uv.X ), Bits( uv.Y ));
					if ( !cornerOf.TryGetValue( key, out var corner ) )
					{
						corner = material.AddVertex( position, normal, uv );
						cornerOf[key] = corner;
						corners++;
						positions.Add( (key.Item1, key.Item2, key.Item3) );
					}
					return corner;
				}

				for ( var triangle = 0; triangle + 2 < primitive.Indices.Length; triangle += 3 )
				{
					// The winding is reversed: the mirror turns a counter-clockwise glTF triangle into a clockwise one, which is MD2's front.
					var order = new[] { primitive.Indices[triangle], primitive.Indices[triangle + 2], primitive.Indices[triangle + 1] };
					var p = order.Select( index => ToMd2Position( primitive.Positions[index] ) ).ToArray();
					var flat = NVector3.Cross( p[1] - p[0], p[2] - p[0] );
					flat = flat.LengthSquared() > 0 ? NVector3.Normalize( flat ) : NVector3.UnitY;
					var ids = new int[3];
					for ( var corner = 0; corner < 3; corner++ )
					{
						var normal = primitive.Normals != null ? ToMd2Position( primitive.Normals[order[corner]] ) : flat;
						ids[corner] = Corner( p[corner], normal, ToMd2TexCoord( primitive.TexCoords[order[corner]] ) );
					}
					material.AddTriangle( ids[0], ids[1], ids[2] );
					faces++;
				}
			}
			if ( cornerOf.Count == 0 )
				throw new InvalidDataException( $"Node '{node.Name}': material '{gltfMaterial.Name}' has no triangles." );
		}
		if ( corners > MaximumCount )
			throw new InvalidDataException( $"Node '{node.Name}' has {corners} vertices once split by material and normal/UV seams; an MD2 mesh holds at most {MaximumCount}. Split it into several objects." );
		if ( positions.Count > MaximumCount )
			throw new InvalidDataException( $"Node '{node.Name}' has {positions.Count} distinct positions; an MD2 mesh holds at most {MaximumCount}. Split it into several objects." );
		if ( faces > MaximumCount )
			throw new InvalidDataException( $"Node '{node.Name}' has {faces} triangles; an MD2 mesh holds at most {MaximumCount}. Split it into several objects." );
	}

	private static bool BoolExtra( GltfMaterial material, string key, bool fallback )
	{
		if ( !material.Extras.TryGetValue( key, out var value ) )
			return fallback;
		return value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Number when value.TryGetInt32( out var number ) && number is 0 or 1 => number == 1,
			_ => throw new InvalidDataException( $"Material '{material.Name}': {key} must be true or false." )
		};
	}

	private static void Check( Action check, string context )
	{
		try
		{
			check();
		}
		catch ( InvalidOperationException exception )
		{
			throw new InvalidDataException( $"{context}: {exception.Message}" );
		}
	}

	private static int Bits( float value ) => BitConverter.SingleToInt32Bits( value == 0 ? 0f : value );
	private static bool IsFinite( NVector3 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );
	private static bool IsFinite( NVector2 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y );

	private static void CheckFinite( NMatrix4x4 matrix, string message )
	{
		foreach ( var value in new[] { matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44 } )
		{
			if ( !float.IsFinite( value ) )
				throw new InvalidDataException( message );
		}
	}

	/// <summary>The texture names of the scene's images: the image name without a known image extension, checked once, unique without case, created on first use.</summary>
	private sealed class TextureTable
	{
		private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".tga", ".wct", ".bmp" };
		private readonly GltfScene scene;
		private readonly Dictionary<int, string> names = new();
		private readonly Dictionary<string, int> owners = new( StringComparer.OrdinalIgnoreCase );
		private readonly List<GltfConvertedTexture> used = new();

		public TextureTable( GltfScene scene ) => this.scene = scene;

		public IReadOnlyList<GltfConvertedTexture> Used => used;

		public string NameOf( int image )
		{
			if ( names.TryGetValue( image, out var known ) )
				return known;
			var source = scene.Images[image];
			if ( source.MimeType is not ("image/png" or "image/jpeg") )
				throw new InvalidDataException( $"Image '{source.Name}' is {(source.MimeType.Length == 0 ? "of unknown type" : source.MimeType)}; the content kit reads PNG and JPEG images." );
			var name = source.Name;
			foreach ( var extension in Extensions )
			{
				if ( name.EndsWith( extension, StringComparison.OrdinalIgnoreCase ) )
				{
					name = name[..^extension.Length];
					break;
				}
			}
			try
			{
				name = ModelBuilder.TextureStem( name );
			}
			catch ( InvalidOperationException exception )
			{
				throw new InvalidDataException( $"Image '{source.Name}': {exception.Message} Rename the image (at most {ModelBuilder.MaximumTextureNameLength} characters, printable ASCII, no path)." );
			}
			if ( owners.TryGetValue( name, out var other ) && other != image )
				throw new InvalidDataException( $"Images '{scene.Images[other].Name}' and '{source.Name}' both become the texture '{name}'; rename one." );
			owners[name] = image;
			names[image] = name;
			used.Add( new GltfConvertedTexture( name, source ) );
			return name;
		}
	}
}
