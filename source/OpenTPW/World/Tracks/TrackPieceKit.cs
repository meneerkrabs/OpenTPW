namespace OpenTPW;

/// <summary>
/// The track pieces of one ride archive: the 25 (water) or 20 (kart) table entries with the <c>wr_trck*</c> / <c>gk_trck*</c> MD2
/// meshes and <c>.hmp</c> height profiles they name (docs/TRACKS.md). A piece shares its mesh with the entries that carry the
/// same stem, so the kit parses each file once. Missing files leave the entry without a mesh (reported by <see cref="Missing"/>).
/// </summary>
public sealed class TrackPieceKit
{
	/// <summary>File stem prefix of the water pieces in a water ride's archive.</summary>
	public const string WaterPrefix = "wr_trck";
	/// <summary>File stem prefix of the kart pieces in a kart ride's archive.</summary>
	public const string KartPrefix = "gk_trck";

	private readonly Dictionary<string, ModelFile?> models = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, HmpFile?> profiles = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<(string Stem, int Animation), (ModelAnimation Clip, string Name)?> clips = new();
	private readonly Dictionary<string, string> files = new( StringComparer.OrdinalIgnoreCase );
	private readonly BaseFileSystem fileSystem;
	private readonly string directory;

	private TrackPieceKit( BaseFileSystem fileSystem, string directory, TrackFamily family )
	{
		this.fileSystem = fileSystem;
		this.directory = directory;
		Family = family;
		Prefix = family == TrackFamily.Water ? WaterPrefix : KartPrefix;
		Entries = TrackPieceSelector.PieceTable( family );
		foreach ( var path in fileSystem.GetFiles( directory ) )
			files[Path.GetFileName( path )] = path;
	}

	public TrackFamily Family { get; }
	public string Prefix { get; }
	/// <summary>The family's piece table (20 or 25 entries).</summary>
	public IReadOnlyList<TrackPieceEntry> Entries { get; }

	/// <summary>Loads the kit of <paramref name="family"/> from the archive directory of its ride (the folder holding <c>wr_trck*.md2</c>).</summary>
	public static TrackPieceKit Load( BaseFileSystem fileSystem, string directory, TrackFamily family )
	{
		ArgumentNullException.ThrowIfNull( fileSystem );
		if ( family is not (TrackFamily.Kart or TrackFamily.Water) )
			throw new ArgumentOutOfRangeException( nameof( family ) );
		return new TrackPieceKit( fileSystem, directory, family );
	}

	/// <summary>The file stem (<c>wr_trckS</c>) of a piece, or null for the empty piece.</summary>
	public string? StemOf( int piece ) => piece <= 0 || piece >= Entries.Count ? null : Prefix + Entries[piece].Name;

	/// <summary>The parsed mesh of a piece, or null when the piece draws nothing or its file is absent.</summary>
	public ModelFile? GetModel( int piece )
	{
		if ( StemOf( piece ) is not { } stem )
			return null;
		if ( models.TryGetValue( stem, out var cached ) )
			return cached;
		ModelFile? model = null;
		if ( files.TryGetValue( stem + ".md2", out var path ) )
		{
			try
			{
				model = ObjectAssets.LoadModel( fileSystem, path );
				if ( model.Kind != ModelFileKind.Geometry )
					model = null;
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
			{
				model = null;
			}
		}
		models[stem] = model;
		return model;
	}

	/// <summary>The height profile of a piece, or null when it has none or the file is malformed.</summary>
	// [APPROX:TRACK-002] the height profiles (.hmp) are the original's per-object height map ([BIN:TPW-EXE:0x00451640] loads it from the mesh loader 0x4629D0 and rebuilds it from the mesh with 0x451880 when it is missing or mis-sized); the vehicles' sampler does not read it (0x4511A0 samples faces or the terrain sampler 0x4527F0, TRACK-001) and neither do OpenTPW's vehicles; the map (+0xCC) is read by the cell-height lookup 0x452AE0 (owner's map byte × 100/255 plus its base height; callers 0x45CDC0, 0x532FC0, 0x536CA0, 0x5370E0 and the tour code 0x55ABF0, 0x55C350, 0x55CC00) — evidence needed: whether any other code reads the map
	public HmpFile? GetHeightProfile( int piece )
	{
		if ( StemOf( piece ) is not { } stem )
			return null;
		if ( profiles.TryGetValue( stem, out var cached ) )
			return cached;
		HmpFile? profile = null;
		if ( files.TryGetValue( stem + ".hmp", out var path ) )
		{
			try
			{
				using var stream = fileSystem.OpenRead( path );
				profile = new HmpFile( stream );
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or IOException )
			{
				profile = null;
			}
		}
		profiles[stem] = profile;
		return profile;
	}

	/// <summary>The animation of a piece in the clip numbering of objects (<see cref="ObjectAnimations"/>), the ride script's water animation.</summary>
	public const int WaterAnimation = 5;

	/// <summary>
	/// The clip <paramref name="animation"/> (variant 0) of a piece's mesh, its own letter member (<c>wr_trckSm.md2</c> for animation 5), or null when the
	/// archive has none or it cannot be read.
	/// </summary>
	// [BIN:TPW-EXE:0x00461F10 clip loader] a mesh's clips are its own name plus the animation's letter (C D I L S M E U W B R O) and an optional number, as for every object; the pieces are meshes of the piece table loaded through it
	public (ModelAnimation Clip, string Name)? GetAnimation( int piece, int animation )
	{
		if ( StemOf( piece ) is not { } stem )
			return null;
		if ( clips.TryGetValue( (stem, animation), out var cached ) )
			return cached;
		(ModelAnimation, string)? result = null;
		if ( ObjectAnimations.Resolve( ObjectAnimations.Find( stem, files.Values ), animation, 0 ) is { } file )
		{
			try
			{
				if ( ObjectAssets.LoadModel( fileSystem, file.Path ) is { Kind: ModelFileKind.Animation, Clip: { } clip } )
					result = (clip, file.Name);
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
			{
				result = null;
			}
		}
		clips[(stem, animation)] = result;
		return result;
	}

	/// <summary>
	/// The nodes of a piece's mesh the instancer hides for its arm mask (<see cref="TrackPieceEntry.ArmMask"/>): the node of every dummy record whose third
	/// word (the end cap's arm) shares a bit with the mask, so a straight, end or junction piece shows end caps only on its open arms.
	/// </summary>
	// [BIN:TPW-EXE:0x00522D50 water piece instance] 0x00463060(table handle, 0x33A, position, arm byte of the 12-byte entry at 0x7634D8 + 8, …) (kart: 0x00522B90, table 0x7633E8); the byte reaches 0x00454A10 through 0x004557C0 as its last parameter
	// [BIN:TPW-EXE:0x0044B140 arm mask] (load flags without 0x1000) every dummy record i (20 bytes at model +0x7C, +0x48 of them) whose word +8 has a bit of the mask gets node flags 0x80000010 on node record +0x46 + i (node bit 0x10: drawn hidden, as the clips' node lists set it, ObjectAnimator.IsNodeHidden)
	public static IReadOnlySet<int> ArmMaskedNodes( ModelFile model, byte armMask )
	{
		ArgumentNullException.ThrowIfNull( model );
		var hidden = new HashSet<int>();
		if ( armMask == 0 )
			return hidden;
		for ( var index = 0; index < model.DummyAttributes.Count; index++ )
		{
			var reserved = model.DummyAttributes[index].Reserved;
			if ( reserved.Length < 4 || (BitConverter.ToUInt32( reserved, 0 ) & armMask) == 0 )
				continue;
			var node = model.Counts.Unknown46 + index;
			if ( node < model.Nodes.Count )
				hidden.Add( node );
		}
		return hidden;
	}

	/// <summary>The stems of the table's pieces whose mesh is missing from the archive (an empty list for a complete kit).</summary>
	public IReadOnlyList<string> Missing()
	{
		var missing = new List<string>();
		foreach ( var entry in Entries )
		{
			if ( entry.Index != 0 && GetModel( entry.Index ) == null && StemOf( entry.Index ) is { } stem && !missing.Contains( stem ) )
				missing.Add( stem );
		}
		return missing;
	}
}
