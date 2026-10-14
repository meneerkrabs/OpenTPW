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
	// [APPROX:TRACK-002] the height profiles (.hmp) are parsed and kept, but nothing consumes them: the instancer reads them with the mesh (0x594C8), the readers of the data are not traced (RIDES-014) — evidence needed: the consumers of the profile bytes and the per-cell maxima
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
