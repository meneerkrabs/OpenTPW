namespace OpenTPW;

/// <summary>
/// A level's hoarding parts that every placed object of it shares (docs/OBJECTS.md, "Hoarding"): each type's boards, built once over the level's terrain,
/// and the level's four texture sets, loaded once. Owned by the level and cleared with it.
/// </summary>
internal sealed class ObjectHoardingCache
{
	private readonly Dictionary<ObjectCatalogEntry, ObjectHoardingGeometry?> geometries = new( ReferenceEqualityComparer.Instance );
	private Texture[]? textures;
	private string? texturesLevel;

	/// <summary>The boards of <paramref name="entry"/>'s type over <paramref name="field"/>, or null when it has none; built on the first object of the type.</summary>
	public ObjectHoardingGeometry? Geometry( ObjectCatalogEntry entry, ModelFile model, ModelHeightfield field )
	{
		if ( !geometries.TryGetValue( entry, out var geometry ) )
			geometries[entry] = geometry = ObjectHoardingGeometry.Build( entry.Settings, model, HoardingTerrain.Of( field ) );
		return geometry;
	}

	/// <summary>The four texture sets of <paramref name="levelName"/> (<see cref="ObjectHoardingRenderer.LoadTextures"/>), loaded on the first hoarding.</summary>
	public Texture[] Textures( string levelName )
	{
		if ( textures == null || texturesLevel != levelName )
		{
			textures = ObjectHoardingRenderer.LoadTextures( levelName );
			texturesLevel = levelName;
		}
		return textures;
	}

	/// <summary>Drops the shared parts (the level is torn down; path textures stay in the texture cache as every path texture does).</summary>
	public void Clear()
	{
		geometries.Clear();
		textures = null;
		texturesLevel = null;
	}
}
