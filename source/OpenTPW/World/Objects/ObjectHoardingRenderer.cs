namespace OpenTPW;

/// <summary>
/// Draws a placed object's hoarding (docs/OBJECTS.md, "Hoarding"): the boards of its type's <see cref="ObjectHoardingGeometry"/>, set on the terrain at the
/// object's footprint, with the texture set and rise of the object's <see cref="ObjectHoardingState"/>. Presentation only.
/// </summary>
internal sealed class ObjectHoardingRenderer
{
	private const string TextureArrayName = "Color";

	/// <summary>The material flags of the hoarding's textures: the loaded entries carry flag 1 and no wrap bit, so the slot clamps.</summary>
	// [BIN:TPW-EXE:0x00424EF0 hoarding model] the model builder marks every loaded texture entry with flag 1 and adds 2 or 0x30 only for descriptor flags 1 or 2, which the hoarding descriptor's entries (flag word 0) do not have
	private static readonly uint MaterialFlags = TextureSlotAddressing.VertexFlags( 1 );

	private readonly ObjectHoardingGeometry geometry;
	private readonly ObjectHoardingState state;
	private readonly (float BottomA, float BottomB)[] bottoms;
	private readonly Vertex[] vertices;
	private readonly Model model;
	private readonly ModelEntity entity;
	private int shownVersion = -1;

	private ObjectHoardingRenderer( ObjectHoardingGeometry geometry, ObjectHoardingState state, (float, float)[] bottoms, Texture[] textures, System.Numerics.Matrix4x4 placement, string name )
	{
		this.geometry = geometry;
		this.state = state;
		this.bottoms = bottoms;
		vertices = new Vertex[geometry.Strips.Count * 8];
		var indices = new uint[geometry.Strips.Count * 12];
		for ( var strip = 0; strip < geometry.Strips.Count; strip++ )
		{
			var front = (uint)(strip * 8);
			var back = front + 4;
			// [BIN:TPW-EXE:0x00453E3C hoarding faces] strip s has the faces (4s, 4s+1, 4s+2) and (4s+1, 4s+3, 4s+2) with its corners' normal, and (4s+2, 4s+1, 4s) and (4s+2, 4s+3, 4s+1) with the opposite face normal (index 4n + s)
			new uint[] { front, front + 1, front + 2, front + 1, front + 3, front + 2, back + 2, back + 1, back, back + 2, back + 3, back + 1 }.CopyTo( indices, strip * 12 );
		}
		Write( shaped: false );
		var slots = new Texture[16];
		Array.Fill( slots, Texture.Missing );
		textures.CopyTo( slots, 0 );
		var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
		material.Set( TextureArrayName, slots );
		model = new Model( vertices, indices, material, true );
		entity = new ModelEntity { Model = model, Name = $"{name}:hoarding", TransformOverride = placement, Hidden = true };
		entity.Position = new Vector3( placement.M41, placement.M42, placement.M43 );
	}

	/// <summary>
	/// The hoarding of an object of <paramref name="entry"/> placed at <paramref name="placement"/> in the level <paramref name="levelName"/> over <paramref name="field"/>,
	/// or null when its type has none (<see cref="ObjectHoardingGeometry.Build"/>). Binds <paramref name="state"/> to it.
	/// </summary>
	public static ObjectHoardingRenderer? Create( ObjectCatalogEntry entry, ModelFile model, ObjectPlacement placement, ModelHeightfield? field, string? levelName, ObjectHoardingState state,
		ObjectHoardingCache? cache = null )
	{
		if ( field == null || levelName == null )
			return null;
		var geometry = cache != null ? cache.Geometry( entry, model, field ) : ObjectHoardingGeometry.Build( entry.Settings, model, HoardingTerrain.Of( field ) );
		if ( geometry == null )
			return null;
		var bounds = ObjectFootprint.GetBounds( entry.Shape, placement.X, placement.Y, placement.Rotation );
		var bottoms = ObjectHoardingGeometry.Conform( geometry, field, placement.Rotation, bounds, placement.BaseHeight / ObjectPlacement.ModelScale );
		state.Present = true;
		return new ObjectHoardingRenderer( geometry, state, bottoms, cache?.Textures( levelName ) ?? LoadTextures( levelName ), placement.ModelToEngine, entry.ArchiveName );
	}

	/// <summary>The level's four hoarding textures (Closed, Hoarding, Condemn, Upgrade), the missing texture for one the level lacks.</summary>
	internal static Texture[] LoadTextures( string levelName ) =>
		ObjectHoardingGeometry.TextureNames.Select( name => ResolveTexture( levelName, name ) is { } path ? new Texture( path, TextureFlags.Repeat ) : Texture.Missing ).ToArray();

	/// <summary>The data path of a hoarding texture of a level (any image extension the loader reads: .wct, Theme Park Inc's .fsh), or null.</summary>
	internal static string? ResolveTexture( string levelName, string name )
	{
		string[] files;
		try
		{
			// an archive's directory is listed, not tested: DirectoryExists sees loose folders only
			files = FileSystem.GetFiles( ObjectHoardingGeometry.TextureDirectory( levelName ) );
		}
		catch ( Exception exception ) when ( exception is DirectoryNotFoundException or FileNotFoundException or IOException )
		{
			return null;
		}
		return files.Select( file => FileSystem.GetRelativePath( FileSystem.GetAbsolutePath( file ) ) )
			.FirstOrDefault( file => string.Equals( Path.GetFileNameWithoutExtension( file ), name, StringComparison.OrdinalIgnoreCase )
				&& !file.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>Shows the state's current variant, rise and standing; nothing is rewritten while the state is unchanged.</summary>
	public void Update()
	{
		if ( state.Version == shownVersion )
			return;
		shownVersion = state.Version;
		Write( state.Shaped );
		model.UpdateVertices( vertices );
		entity.Hidden = !state.IsStanding;
	}

	/// <summary>
	/// Writes every strip's corners: the bottoms on the terrain, the tops at the rise of the state's level once the boards were shaped (until then at the full
	/// board height with the texture's whole height, as the builder made them), the variant's texture and both faces.
	/// </summary>
	private void Write( bool shaped )
	{
		var count = geometry.Strips.Count;
		var variant = state.Variant;
		for ( var index = 0; index < count; index++ )
		{
			var strip = geometry.Strips[index];
			var (bottomA, bottomB) = bottoms[index];
			var (height, bottomV) = shaped ? ObjectHoardingGeometry.Rise( index, count, state.Level ) : (ObjectHoardingGeometry.BoardHeight, 0f);
			var normal = ObjectAssets.ConvertAxes( strip.Normal );
			// [BIN:TPW-EXE:0x00453DF8 hoarding texture coordinates] the builder gives corner 4s (0, 1), 4s+1 (1, 1), 4s+2 (0, 0) and 4s+3 (1, 0); the rise rewrites only the bottom corners' V
			WriteCorner( index, 0, strip.Corners[0] with { Y = (float)(bottomA + height) }, normal, new Vector2( 0, 1 ), variant );
			WriteCorner( index, 1, strip.Corners[1] with { Y = (float)(bottomB + height) }, normal, new Vector2( 1, 1 ), variant );
			WriteCorner( index, 2, strip.Corners[2] with { Y = bottomA }, normal, new Vector2( 0, bottomV ), variant );
			WriteCorner( index, 3, strip.Corners[3] with { Y = bottomB }, normal, new Vector2( 1, bottomV ), variant );
		}
	}

	/// <summary>One corner of a strip, written allocation-free on both faces (the back face with the opposite normal).</summary>
	private void WriteCorner( int strip, int corner, System.Numerics.Vector3 modelCorner, System.Numerics.Vector3 normal, Vector2 uv, int variant )
	{
		var position = ObjectAssets.ConvertAxes( modelCorner );
		vertices[strip * 8 + corner] = new Vertex( position, normal, uv ) { TexIndex = variant, MatFlags = MaterialFlags };
		vertices[strip * 8 + 4 + corner] = new Vertex( position, -normal, uv ) { TexIndex = variant, MatFlags = MaterialFlags };
	}

	public void Delete()
	{
		entity.Delete();
		model.ReleaseBuffers();
		model.Material.ReleaseOwnedResources();
	}
}
