namespace OpenTPW;

/// <summary>Name and archive of an original object, resolved by the Info.Id in its .sam file.</summary>
public readonly record struct OriginalObjectInfo( int InfoId, string Name, string ArchivePath );

/// <summary>
/// Read-only original level data: the terrain MAP, the base.MD2 terrain model and heightfield,
/// and, when the level ships one, the park state imported from <c>Easymode.TPWI</c>
/// (docs/TPWS-PAYLOAD.md). Nothing is written back to the original files.
/// </summary>
public sealed class OriginalPark
{
	public const string SaveFileName = "Easymode.TPWI";
	private static readonly string[] ObjectDirectories = { "rides", "shops", "sideshow", "features", "upgrades" };
	private static readonly System.Text.RegularExpressions.Regex InfoIdPattern = new( @"^\s*Info\.Id\s+(\d+)", System.Text.RegularExpressions.RegexOptions.Multiline );
	private static readonly System.Text.RegularExpressions.Regex InfoNamePattern = new( "^\\s*Info\\.Name\\s+\"([^\"\\r\\n]*)\"", System.Text.RegularExpressions.RegexOptions.Multiline );

	private OriginalPark( string levelName, MapFile map, ModelFile terrainModel, OriginalParkImport? save, IReadOnlyDictionary<int, OriginalObjectInfo> catalog )
	{
		LevelName = levelName;
		Map = map;
		TerrainModel = terrainModel;
		Heightfield = terrainModel.Heightfield ?? throw new InvalidDataException( $"The {levelName} terrain model has no heightfield block." );
		Save = save;
		Catalog = catalog;
	}

	public string LevelName { get; }
	public MapFile Map { get; }
	public ModelFile TerrainModel { get; }
	public ModelHeightfield Heightfield { get; }
	public OriginalParkImport? Save { get; }
	public IReadOnlyDictionary<int, OriginalObjectInfo> Catalog { get; }

	public string TerrainDirectory => $"/levels/{LevelName}/terrain";

	public string DescribeObject( int infoId ) => Catalog.TryGetValue( infoId, out var info ) ? info.Name : $"Info.Id {infoId}";

	/// <param name="includeEasymodePark">Import the level's Easymode park. Off for Full Simulation, which starts
	/// without it.</param>
	public static OriginalPark Load( string levelName, bool includeEasymodePark = true )
	{
		if ( string.IsNullOrWhiteSpace( levelName ) || levelName.IndexOfAny( new[] { '/', '\\', '.' } ) >= 0 )
			throw new ArgumentException( "Original level names are plain directory names such as 'jungle'.", nameof( levelName ) );
		var map = new MapFile( $"/levels/{levelName}/terrain/base.map" );
		var model = new ModelFile( $"/levels/{levelName}/terrain/base.MD2" );
		var catalog = LoadCatalog( levelName );
		OriginalParkImport? save = null;
		var savePath = FileSystem.GetFiles( $"/levels/{levelName}" )
			.FirstOrDefault( path => string.Equals( Path.GetFileName( path ), SaveFileName, StringComparison.OrdinalIgnoreCase ) );
		// [BIN:STP-PPC:0x10137600 player save setup] the level's easymode park is copied into a player's saves only for Instant Action players (0x1013741C passes the mode flag)
		if ( savePath != null && includeEasymodePark )
		{
			using var stream = FileSystem.OpenRead( $"/levels/{levelName}/{Path.GetFileName( savePath )}" );
			using var reader = new SaveReader( stream );
			save = OriginalParkImport.Import( reader.ReadFile(), map );
			foreach ( var item in save.PlacedObjects.Select( item => item.Record ).Concat( save.FixedItems ).Concat( save.UnresolvedObjects ) )
			{
				if ( !catalog.ContainsKey( item.InfoId ) )
					throw new InvalidDataException( $"Original save object {item.Index} has Info.Id {item.InfoId}, which no {levelName} object archive declares." );
			}
		}
		return new OriginalPark( levelName, map, model, save, catalog );
	}

	/// <summary>Reads Info.Id and Info.Name from every object archive's .sam under the level.</summary>
	public static IReadOnlyDictionary<int, OriginalObjectInfo> LoadCatalog( string levelName )
	{
		var catalog = new Dictionary<int, OriginalObjectInfo>();
		foreach ( var category in ObjectDirectories )
		{
			var categoryPath = $"/levels/{levelName}/{category}";
			if ( !FileSystem.DirectoryExists( categoryPath ) )
				continue;
			foreach ( var archive in FileSystem.GetDirectories( categoryPath ) )
			{
				var archivePath = ToRelative( archive );
				foreach ( var file in FileSystem.GetFiles( archivePath ).Where( file => file.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) ) )
				{
					var text = FileSystem.ReadAllText( ToRelative( file ) );
					var idMatch = InfoIdPattern.Match( text );
					if ( !idMatch.Success || !int.TryParse( idMatch.Groups[1].Value, out var id ) )
						continue;
					var nameMatch = InfoNamePattern.Match( text );
					var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension( file );
					catalog.TryAdd( id, new OriginalObjectInfo( id, name, archivePath ) );
				}
			}
		}
		return catalog;
	}

	// Entries inside archives come back data-root-relative, loose entries absolute.
	private static string ToRelative( string entry ) => FileSystem.GetRelativePath( FileSystem.GetAbsolutePath( entry ) );
}
