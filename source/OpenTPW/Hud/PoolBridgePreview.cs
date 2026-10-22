using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Hud;

/// <summary>
/// [EXT:pools-bridges] The buy window's preview of the pool bridge row. The row is not a catalogue object, so it has no preview model of the original game; its preview is the bridge's own
/// committed Blender mesh (<c>content/pools/bridge/bridge.json</c>, the mesh <see cref="PoolBridgeRenderer"/> draws in the park), assembled into a model in memory and drawn by the same
/// <see cref="PreviewIcon"/> as every other row: turning, tilted and fitted into the preview frame, textured with the bridge's three PNG textures. Nothing is written to disk.
/// </summary>
public static class PoolBridgePreview
{
	/// <summary>The bridge's folder below the pools content, or null when the content is missing.</summary>
	private static string? Directory => PoolContent.Directory is { } folder ? Path.Combine( folder, "bridge" ) : null;

	/// <summary>The preview icon of the bridge, or null when its content is missing. <paramref name="directoryOverride"/> points tests at the bridge folder.</summary>
	public static PreviewIcon? Create( string? directoryOverride = null )
	{
		if ( (directoryOverride ?? Directory) is not { } directory || !File.Exists( Path.Combine( directory, "bridge.json" ) ) )
			return null;
		var asset = PoolBridgeAsset.Load( Path.Combine( directory, "bridge.json" ) );
		var textures = asset.Parts.ToDictionary( part => TextureName( part ), part => Path.Combine( directory, part.Texture ), StringComparer.OrdinalIgnoreCase );
		return new PreviewIcon( Model( asset ), _ => null, name => textures.TryGetValue( name, out var path ) && File.Exists( path ) ? UiTexture.HostImage( path ) : null );
	}

	private static string TextureName( PoolBridgeAssetPart part ) => Path.GetFileNameWithoutExtension( part.Texture );

	/// <summary>
	/// The mesh as a model of one node: Blender's axes (along the bridge, across it, up) become the model's (x, up, -across), which keeps the triangles' winding; the units are the mesh's own
	/// (ten to a cell), the preview fits the model to its frame whatever its size.
	/// </summary>
	public static ModelFile Model( PoolBridgeAsset asset )
	{
		var builder = new ModelBuilder { SourceName = "poolbridge" };
		var mesh = builder.AddMesh( "bridge" ).Mesh!;
		foreach ( var part in asset.Parts )
		{
			var material = mesh.AddMaterial( TextureName( part ), wrap: true );
			foreach ( var v in part.Vertices )
				material.AddVertex( new NVector3( v[0], v[2], -v[1] ), new NVector3( v[3], v[5], -v[4] ), new NVector2( v[6], v[7] ) );
			for ( var index = 0; index + 2 < part.Indices.Length; index += 3 )
				material.AddTriangle( part.Indices[index], part.Indices[index + 1], part.Indices[index + 2] );
		}
		return builder.Build();
	}
}
