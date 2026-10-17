using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace OpenTPW.Tests;

/// <summary>A small builder for hand-made binary glTF files: meshes of triangle primitives, materials with textures and extras, embedded PNGs and a node tree.</summary>
internal sealed class GlbBuilder
{
	private readonly List<byte> binary = new();
	private readonly List<string> bufferViews = new();
	private readonly List<string> accessors = new();
	private readonly List<string> images = new();
	private readonly List<string> textures = new();
	private readonly List<string> materials = new();
	private readonly List<string> meshes = new();
	private readonly List<string> nodes = new();

	private static string F( float value ) => value.ToString( "R", CultureInfo.InvariantCulture );

	private int AddView( byte[] bytes )
	{
		while ( binary.Count % 4 != 0 )
			binary.Add( 0 );
		bufferViews.Add( $"{{ \"buffer\": 0, \"byteOffset\": {binary.Count}, \"byteLength\": {bytes.Length} }}" );
		binary.AddRange( bytes );
		return bufferViews.Count - 1;
	}

	private int AddAccessor( float[] values, string type, int count )
	{
		var bytes = values.SelectMany( BitConverter.GetBytes ).ToArray();
		accessors.Add( $"{{ \"bufferView\": {AddView( bytes )}, \"componentType\": 5126, \"count\": {count}, \"type\": \"{type}\" }}" );
		return accessors.Count - 1;
	}

	private int AddIndices( int[] indices )
	{
		var bytes = indices.SelectMany( index => BitConverter.GetBytes( (uint)index ) ).ToArray();
		accessors.Add( $"{{ \"bufferView\": {AddView( bytes )}, \"componentType\": 5125, \"count\": {indices.Length}, \"type\": \"SCALAR\" }}" );
		return accessors.Count - 1;
	}

	/// <summary>An opaque PNG of a flat colour (decodable by stb) as an embedded image.</summary>
	public int AddImage( string name, int width = 4, int height = 4, byte red = 200, byte green = 100, byte blue = 50, byte alpha = 255 )
	{
		var pixels = new byte[width * height * 4];
		for ( var index = 0; index < pixels.Length; index += 4 )
			(pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]) = (red, green, blue, alpha);
		using var stream = new MemoryStream();
		PngWriter.Write( stream, pixels, width, height );
		return AddImageBytes( name, stream.ToArray(), "image/png" );
	}

	public int AddImageBytes( string? name, byte[] bytes, string mime )
	{
		var view = AddView( bytes );
		images.Add( $"{{ {(name == null ? "" : $"\"name\": \"{name}\", ")}\"mimeType\": \"{mime}\", \"bufferView\": {view} }}" );
		return images.Count - 1;
	}

	/// <summary>A material with a base colour texture of <paramref name="image"/> (or none for -1) and the OpenTPW extras that are not null.</summary>
	public int AddMaterial( string name, int image, bool? wrap = null, bool? selfLit = null, bool doubleSided = false, string? rawExtras = null )
	{
		var extras = new List<string>();
		if ( wrap != null )
			extras.Add( $"\"opentpw_wrap\": {(wrap.Value ? "true" : "false")}" );
		if ( selfLit != null )
			extras.Add( $"\"opentpw_self_lit\": {(selfLit.Value ? "true" : "false")}" );
		if ( rawExtras != null )
			extras.Add( rawExtras );
		string texture = "";
		if ( image >= 0 )
		{
			textures.Add( $"{{ \"source\": {image} }}" );
			texture = $", \"pbrMetallicRoughness\": {{ \"baseColorTexture\": {{ \"index\": {textures.Count - 1} }} }}";
		}
		materials.Add( $"{{ \"name\": \"{name}\"{texture}, \"doubleSided\": {(doubleSided ? "true" : "false")}, \"extras\": {{ {string.Join( ", ", extras )} }} }}" );
		return materials.Count - 1;
	}

	/// <summary>One primitive's data; <c>null</c> normals or UVs leave the attribute out.</summary>
	public sealed record Primitive( float[] Positions, float[]? Normals, float[]? Uvs, int[] Indices, int Material );

	public int AddMesh( string name, params Primitive[] primitives )
	{
		var text = new List<string>();
		foreach ( var primitive in primitives )
		{
			var count = primitive.Positions.Length / 3;
			var attributes = new List<string> { $"\"POSITION\": {AddAccessor( primitive.Positions, "VEC3", count )}" };
			if ( primitive.Normals != null )
				attributes.Add( $"\"NORMAL\": {AddAccessor( primitive.Normals, "VEC3", count )}" );
			if ( primitive.Uvs != null )
				attributes.Add( $"\"TEXCOORD_0\": {AddAccessor( primitive.Uvs, "VEC2", count )}" );
			var material = primitive.Material >= 0 ? $", \"material\": {primitive.Material}" : "";
			text.Add( $"{{ \"attributes\": {{ {string.Join( ", ", attributes )} }}, \"indices\": {AddIndices( primitive.Indices )}{material} }}" );
		}
		meshes.Add( $"{{ \"name\": \"{name}\", \"primitives\": [ {string.Join( ", ", text )} ] }}" );
		return meshes.Count - 1;
	}

	/// <summary>A node with an optional mesh, translation, rotation quaternion (x, y, z, w), children and raw extras JSON members.</summary>
	public int AddNode( string name, int mesh = -1, Vector3? translation = null, Quaternion? rotation = null, int[]? children = null, string? extras = null )
	{
		var parts = new List<string> { $"\"name\": \"{name}\"" };
		if ( mesh >= 0 )
			parts.Add( $"\"mesh\": {mesh}" );
		if ( translation is { } t )
			parts.Add( $"\"translation\": [ {F( t.X )}, {F( t.Y )}, {F( t.Z )} ]" );
		if ( rotation is { } r )
			parts.Add( $"\"rotation\": [ {F( r.X )}, {F( r.Y )}, {F( r.Z )}, {F( r.W )} ]" );
		if ( children is { Length: > 0 } )
			parts.Add( $"\"children\": [ {string.Join( ", ", children )} ]" );
		if ( extras != null )
			parts.Add( $"\"extras\": {{ {extras} }}" );
		nodes.Add( $"{{ {string.Join( ", ", parts )} }}" );
		return nodes.Count - 1;
	}

	public byte[] Build( params int[] roots )
	{
		var sections = new List<string>
		{
			"\"asset\": { \"version\": \"2.0\" }",
			$"\"scene\": 0, \"scenes\": [ {{ \"nodes\": [ {string.Join( ", ", roots )} ] }} ]",
			$"\"nodes\": [ {string.Join( ", ", nodes )} ]",
			$"\"buffers\": [ {{ \"byteLength\": {binary.Count} }} ]",
			$"\"bufferViews\": [ {string.Join( ", ", bufferViews )} ]",
		};
		if ( meshes.Count > 0 )
			sections.Add( $"\"meshes\": [ {string.Join( ", ", meshes )} ]" );
		if ( accessors.Count > 0 )
			sections.Add( $"\"accessors\": [ {string.Join( ", ", accessors )} ]" );
		if ( materials.Count > 0 )
			sections.Add( $"\"materials\": [ {string.Join( ", ", materials )} ]" );
		if ( textures.Count > 0 )
			sections.Add( $"\"textures\": [ {string.Join( ", ", textures )} ]" );
		if ( images.Count > 0 )
			sections.Add( $"\"images\": [ {string.Join( ", ", images )} ]" );
		return GltfSceneTests.Glb( "{ " + string.Join( ", ", sections ) + " }", binary.ToArray() );
	}

	/// <summary>
	/// A quad facing +Z (counter-clockwise seen from +Z, X to the right, Y up), 4 wide and 2 tall, with the texture's top at the quad's top
	/// (glTF V = 0 at the top of the image): the reference shape of the convention tests.
	/// </summary>
	public static Primitive Quad( int material, float width = 4, float height = 2 ) => new(
		new float[] { 0, 0, 0, width, 0, 0, width, height, 0, 0, height, 0 },
		new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 },
		new float[] { 0, 1, 1, 1, 1, 0, 0, 0 },
		new[] { 0, 1, 2, 0, 2, 3 }, material );
}
