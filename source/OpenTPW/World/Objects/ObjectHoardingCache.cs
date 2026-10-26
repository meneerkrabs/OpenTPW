namespace OpenTPW;

/// <summary>
/// A level's parts that every placed object of it shares: each type's hoarding boards (docs/OBJECTS.md, "Hoarding") and floor skirt (<see cref="ObjectSkirt"/>, "Floors"), built once over the level's
/// terrain, and the level's four hoarding texture sets, loaded once. Owned by the level and cleared with it.
/// </summary>
internal sealed class ObjectHoardingCache
{
	private readonly Dictionary<ObjectCatalogEntry, ObjectHoardingGeometry?> geometries = new( ReferenceEqualityComparer.Instance );
	private readonly Dictionary<ObjectCatalogEntry, ObjectSkirt?> skirts = new( ReferenceEqualityComparer.Instance );
	private readonly Dictionary<(ObjectCatalogEntry, string), ObjectFloorBase.SlotArt?> floorArt = new();
	private Texture[]? textures;
	private string? texturesLevel;

	/// <summary>The art of <paramref name="entry"/>'s model texture <paramref name="name"/> as the floor composites read it (decoded once per type and name; null when the data lacks it).</summary>
	public ObjectFloorBase.SlotArt? FloorArt( ObjectCatalogEntry entry, string name )
	{
		if ( !floorArt.TryGetValue( (entry, name), out var art ) )
			floorArt[(entry, name)] = art = OriginalObject.DecodeSlotArt( entry, name );
		return art;
	}

	/// <summary>The boards of <paramref name="entry"/>'s type over <paramref name="field"/>, or null when it has none; built on the first object of the type.</summary>
	public ObjectHoardingGeometry? Geometry( ObjectCatalogEntry entry, ModelFile model, ModelHeightfield field )
	{
		if ( !geometries.TryGetValue( entry, out var geometry ) )
			geometries[entry] = geometry = ObjectHoardingGeometry.Build( entry.Settings, model, HoardingTerrain.Of( field ) );
		return geometry;
	}

	/// <summary>The skirt of <paramref name="entry"/>'s type over <paramref name="field"/>, or null for a fixed item; read on the first object of the type (the type loader's work, once per type).</summary>
	public ObjectSkirt? Skirt( ObjectCatalogEntry entry, ModelFile model, ModelHeightfield field )
	{
		if ( !skirts.TryGetValue( entry, out var skirt ) )
			skirts[entry] = skirt = ObjectSkirt.Build( entry.Settings, model, HoardingTerrain.Of( field ) );
		return skirt;
	}

	/// <summary>Whether the boards of <paramref name="entry"/>'s type were built (tests).</summary>
	internal bool Holds( ObjectCatalogEntry entry ) => geometries.ContainsKey( entry );

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

	/// <summary>Drops the decoded floor art (128 × 128 texels per slot): the composites are made once per placement, so it is held only while the level's objects are built.</summary>
	public void ReleaseFloorArt() => floorArt.Clear();

	/// <summary>Drops the shared parts (the level is torn down; path textures stay in the texture cache as every path texture does).</summary>
	public void Clear()
	{
		geometries.Clear();
		skirts.Clear();
		floorArt.Clear();
		textures = null;
		texturesLevel = null;
	}
}
