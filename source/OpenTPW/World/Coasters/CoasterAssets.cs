namespace OpenTPW;

/// <summary>
/// The presentation assets of one coaster type (one catalogue entry): its cross sections and track textures, the pylon and car models read from its archive and the
/// textures they use. Everything loads through the entry's file system, as the other objects' models do, so the browser build reads the same archive members.
/// </summary>
internal sealed class CoasterAssets : IDisposable
{
	private readonly Dictionary<string, Texture> textures = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<bool, CoasterPylonRig?> pylons = new();
	private readonly Dictionary<int, CoasterCarRig?> cars = new();
	private readonly Dictionary<CoasterCarRig, Model[]> carModels = new();
	private bool disposed;

	public CoasterAssets( ObjectCatalogEntry entry, CoasterDefinition definition )
	{
		Entry = entry;
		Definition = definition;
		Sections = CoasterCrossSections.FromSettings( entry.CoasterSettings );
		CarTypes = CoasterCarTypes.FromSettings( entry.CoasterSettings );
	}

	public ObjectCatalogEntry Entry { get; }
	public CoasterDefinition Definition { get; }
	public CoasterCrossSections Sections { get; }
	public CoasterCarTypes CarTypes { get; }
	/// <summary>The 16 texture slots of the track mesh: the <c>asTextureData</c> files, then the marker textures (<c>m_erase</c>, <c>red</c>, <c>blue</c> of the dynamic texture folder).</summary>
	public Texture[]? TrackSlots { get; private set; }

	/// <summary>The track textures, loaded on first use.</summary>
	public Texture[] GetTrackSlots()
	{
		if ( TrackSlots != null )
			return TrackSlots;
		var slots = new Texture[16];
		Array.Fill( slots, Texture.Missing );
		var own = Math.Min( Sections.Textures.Count, slots.Length );
		for ( var index = 0; index < own; index++ )
		{
			if ( Sections.Textures[index].FileName.Length > 0 )
				slots[index] = Load( Sections.Textures[index].FileName );
		}
		// [BIN:TPW-EXE:0x42f390 marker textures] the three marker textures follow the coaster's own; the dynamic texture table holds them in Data/Generic/Dynamic/Textures
		for ( var marker = 0; marker < CoasterTrackMesh.MarkerTextureNames.Length && own + marker < slots.Length; marker++ )
			slots[own + marker] = markers[marker] ??= BlueprintMarkers.LoadDynamicTexture( CoasterTrackMesh.MarkerTextureNames[marker] );
		return TrackSlots = slots;
	}

	private readonly Texture?[] markers = new Texture?[3];

	/// <summary>A texture of the archive by name (cached), or <see cref="Texture.Missing"/>.</summary>
	public Texture Load( string name )
	{
		var key = Path.GetFileNameWithoutExtension( name.TrimEnd( '\0' ) );
		if ( textures.TryGetValue( key, out var texture ) )
			return texture;
		try
		{
			texture = TrackRenderer.LoadTexture( Entry, name );
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
		{
			Log?.Warning( $"A coaster texture could not be read ({name}): {exception.Message}" );
			// An I/O failure is not remembered as a missing texture: the next load tries again.
			if ( exception is IOException )
				return Texture.Missing;
			texture = Texture.Missing;
		}
		textures[key] = texture;
		return texture;
	}

	/// <summary>The 16 slots of a model part's materials.</summary>
	public Texture[] SlotsFor( IReadOnlyList<string> names )
	{
		var slots = new Texture[16];
		Array.Fill( slots, Texture.Missing );
		for ( var index = 0; index < names.Count && index < slots.Length; index++ )
			slots[index] = Load( names[index] );
		return slots;
	}

	/// <summary>Read failures a model gets before it is given up (an I/O error may pass; bad data never does).</summary>
	private const int ReadAttempts = 3;
	private readonly Dictionary<string, int> failures = new();

	/// <summary>
	/// Logs a failed model read. Bad data (<see cref="InvalidDataException"/> and the like) is final; an I/O failure is not cached as an absent model, so the
	/// next request tries again, up to <see cref="ReadAttempts"/> times. Returns whether the failure is final.
	/// </summary>
	private bool Failed( string what, string name, Exception exception )
	{
		var key = what + ":" + name;
		failures.TryGetValue( key, out var count );
		failures[key] = ++count;
		var final = exception is not IOException || count >= ReadAttempts;
		Log?.Warning( $"A coaster {what} model could not be read ({name}): {exception.Message}{(final ? "; it is not drawn" : "; it is tried again")}" );
		return final;
	}

	/// <summary>The pylon rig of a standard or loop pylon, or null when the archive has no such model or it cannot be used.</summary>
	public CoasterPylonRig? GetPylon( bool loop )
	{
		if ( pylons.TryGetValue( loop, out var cached ) )
			return cached;
		CoasterPylonRig? rig = null;
		var name = Definition.Pylon( loop ).MeshName;
		try
		{
			if ( !string.IsNullOrEmpty( name ) && FindModel( name ) is { Kind: ModelFileKind.Geometry } geometry )
			{
				var loft = FindModel( name + "L" )?.Clip;
				var rotate = (FindModel( name + "R" ) ?? FindModel( name + "R1" ))?.Clip;
				var incline = FindModel( name + "I" )?.Clip;
				var bank = FindModel( name + "B" )?.Clip;
				var loopTilt = FindModel( name + "R2" )?.Clip;
				rig = new CoasterPylonRig( geometry, loft, rotate, Definition.Pylon( loop ), incline, bank, loopTilt ) { WrapNames = WrapNames };
				// [BIN:TPW-EXE:0x430590 pylon capabilities] the model's animations by type: loft L, rotate R or R1, incline I, bank B and the second rotate clip R2
				rig.Capabilities = (loft != null ? CoasterPylonCapabilities.Loft : 0) | (rotate != null ? CoasterPylonCapabilities.Rotate : 0)
					| (incline != null ? CoasterPylonCapabilities.Incline : 0) | (bank != null ? CoasterPylonCapabilities.Bank : 0)
					| (loopTilt != null ? CoasterPylonCapabilities.LoopTilt : 0);
			}
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException or ArgumentException )
		{
			return Failed( "pylon", name, exception ) ? pylons[loop] = null : null;
		}
		return pylons[loop] = rig;
	}

	/// <summary>
	/// Poses a pylon for a node. The mesh conversion happens here, after the model loaded, so a vertex the converter refuses (a nonfinite position) is caught
	/// here: the rig is switched off for the level and the pylon is left out.
	/// </summary>
	public IReadOnlyList<CoasterModelPart> PosePylon( bool loop, CoasterPylonRig rig, CoasterPylonPose pose, CoasterStackPlace place )
	{
		try
		{
			return rig.Pose( pose, place );
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or ArgumentException or IndexOutOfRangeException )
		{
			Failed( "pylon", Definition.Pylon( loop ).MeshName, exception );
			pylons[loop] = null;
			return Array.Empty<CoasterModelPart>();
		}
	}

	/// <summary>The car rig of a car type, or null when the archive has no such model or it cannot be used.</summary>
	public CoasterCarRig? GetCar( int typeIndex )
	{
		if ( cars.TryGetValue( typeIndex, out var cached ) )
			return cached;
		CoasterCarRig? rig = null;
		if ( typeIndex >= 0 && typeIndex < CarTypes.Types.Count )
		{
			var name = CarTypes.Types[typeIndex].MeshName;
			try
			{
				if ( !string.IsNullOrEmpty( name ) && FindModel( name ) is { Kind: ModelFileKind.Geometry } geometry )
				{
					rig = new CoasterCarRig( geometry, WrapNames ) { Bend = CarTypes.Types[typeIndex].Bend };
					// [BIN:TPW-EXE:0x43be10 wheel clip] a car of a type without the bend flag starts the model's animation id 5 (the letter m of the animation table: <model>m1, else <model>m), sub 0, looping on channel 0 (0x4732a0)
					if ( !CarTypes.Types[typeIndex].Bend )
						rig.SetWheelClip( FindWheelClip( name ) );
				}
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException or ArgumentException )
			{
				return Failed( "car", name, exception ) ? cars[typeIndex] = null : null;
			}
		}
		return cars[typeIndex] = rig;
	}

	/// <summary>The animation id 5 member of a car model (<c>ObjectAnimations</c>: id 5 is the letter m, sub 0 the first of them), or null.</summary>
	private ModelAnimation? FindWheelClip( string stem )
	{
		string[] files;
		try
		{
			files = Entry.FileSystem.GetFiles( Entry.ArchivePath ).ToArray();
		}
		catch ( Exception exception ) when ( exception is DirectoryNotFoundException or FileNotFoundException )
		{
			return null;
		}
		var members = files.Where( file => file.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) );
		var clip = ObjectAnimations.Resolve( ObjectAnimations.Find( stem, members ), ObjectAnimations.GetAnimation( 'm' ) ?? 5, 0 );
		return clip == null ? null : ObjectAssets.LoadModel( Entry.FileSystem, clip.Path ).Clip;
	}

	/// <summary>The drawable models of a car rig's parts, shared by every car of the type.</summary>
	public Model[] GetCarModels( CoasterCarRig rig )
	{
		if ( carModels.TryGetValue( rig, out var cached ) )
			return cached;
		var list = new Model[rig.Parts.Count];
		for ( var index = 0; index < list.Length; index++ )
			list[index] = CreatePartModel( rig, index, dynamicVertices: false );
		return carModels[rig] = list;
	}

	/// <summary>A model of one part of a car rig that the caller owns (a car whose part's texture coordinates move with its wheel clip), with vertices that can be replaced.</summary>
	public Model CreateOwnPartModel( CoasterCarRig rig, int part ) => CreatePartModel( rig, part, dynamicVertices: true );

	private Model CreatePartModel( CoasterCarRig rig, int part, bool dynamicVertices )
	{
		var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
		material.Set( "Color", SlotsFor( rig.Parts[part].Textures ) );
		return new Model( rig.Parts[part].Vertices, rig.Parts[part].Indices, material, dynamicVertices );
	}

	/// <summary>
	/// Reads an MD2 member of the archive by file stem. Null means the archive has no such member (absent); a read failure throws, so the caller can tell the two apart.
	/// </summary>
	/// <summary>
	/// The texture names of this ride's archive whose shared texture object wraps: the texture cache keeps one object per (directory, name) and a slot with flag 0x10 or 0x20 of any model the ride loads sets
	/// the wrap bits on it for good (0x575E10, 0x575C60), so a clamped slot of another model, the pylons and the previews among them, wraps as well.
	/// </summary>
	// [BIN:TPW-EXE:0x575870 texture cache] the cache is keyed by the slot's texture directory string and the frame name and returns the same object to every model; the wrap bits are ORed into it and never cleared (0x590FA0 clears only 0x4000); the models of one ride archive (main and preview models, StdPylon, LoopPylon, cars) share their directory
	public ISet<string> WrapNames => wrapNames ??= ComputeWrapNames();
	private HashSet<string>? wrapNames;

	private HashSet<string> ComputeWrapNames()
	{
		var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		string[] files;
		try
		{
			files = Entry.FileSystem.GetFiles( Entry.ArchivePath ).Where( file => file.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		}
		catch ( Exception exception ) when ( exception is DirectoryNotFoundException or FileNotFoundException )
		{
			return names;
		}
		foreach ( var file in files )
		{
			try
			{
				var model = ObjectAssets.LoadModel( Entry.FileSystem, file );
				if ( model.Kind != ModelFileKind.Geometry )
					continue;
				foreach ( var mesh in model.Meshes )
				{
					foreach ( var material in mesh.Materials )
					{
						if ( TextureSlotAddressing.Wraps( material.Flags ) && !string.IsNullOrEmpty( material.Name ) )
							names.Add( material.Name );
					}
				}
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException or ArgumentException )
			{
			}
		}
		return names;
	}

	private ModelFile? FindModel( string stem )
	{
		string[] files;
		try
		{
			files = Entry.FileSystem.GetFiles( Entry.ArchivePath ).ToArray();
		}
		catch ( Exception exception ) when ( exception is DirectoryNotFoundException or FileNotFoundException )
		{
			return null;
		}
		var path = files.FirstOrDefault( file => file.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) && string.Equals( Path.GetFileNameWithoutExtension( file ), stem, StringComparison.OrdinalIgnoreCase ) );
		return path == null ? null : ObjectAssets.LoadModel( Entry.FileSystem, path );
	}

	/// <summary>Views using these assets; the renderer disposes them when the last view goes.</summary>
	public int Users { get; set; }

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		foreach ( var model in carModels.Values.SelectMany( models => models ) )
		{
			model.ReleaseBuffers();
			model.Material.ReleaseOwnedResources();
		}
		carModels.Clear();
		foreach ( var texture in textures.Values )
			texture.ReleaseOwned();
		foreach ( var texture in markers )
			texture?.ReleaseOwned();
		textures.Clear();
	}
}
