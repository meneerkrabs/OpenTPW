namespace OpenTPW;

/// <summary>Name and archive of an original object, resolved by the Info.Id in its .sam file.</summary>
public readonly record struct OriginalObjectInfo( int InfoId, string Name, string ArchivePath );

/// <summary>
/// Read-only original level data: the terrain MAP, the base.MD2 terrain model and heightfield,
/// and, when available, the park state imported from <c>Easymode.TPWI</c> or
/// Theme Park Inc's science <c>PreBuilt/prebuilt.TPWS</c>. Nothing is written back.
/// </summary>
public sealed class OriginalPark
{
	public const string SaveFileName = "Easymode.TPWI";
	private static readonly string[] ObjectDirectories = { "rides", "shops", "sideshow", "features", "upgrades" };
	private static readonly System.Text.RegularExpressions.Regex InfoIdPattern = new( @"^\s*Info\.Id\s+(\d+)", System.Text.RegularExpressions.RegexOptions.Multiline );
	private static readonly System.Text.RegularExpressions.Regex InfoNamePattern = new( "^\\s*Info\\.Name\\s+\"([^\"\\r\\n]*)\"", System.Text.RegularExpressions.RegexOptions.Multiline );

	/// <summary>The finished lake of a Theme Park Inc world; it fills the heightfield holes that <c>base.MD2</c> leaves open.</summary>
	public const string LakeModelName = "dlake.MD2";

	private OriginalPark( string levelName, MapFile map, ModelFile terrainModel, ModelFile? lakeModel, OriginalParkImport? save, IReadOnlyDictionary<int, OriginalObjectInfo> catalog )
	{
		LevelName = levelName;
		LakeModel = lakeModel;
		Map = map;
		TerrainModel = terrainModel;
		Heightfield = terrainModel.Heightfield ?? throw new InvalidDataException( $"The {levelName} terrain model has no heightfield block." );
		Save = save;
		Catalog = catalog;
	}

	public string LevelName { get; }
	public MapFile Map { get; }
	public ModelFile TerrainModel { get; }
	/// <summary>Theme Park Inc's <c>dlake.MD2</c> (lake surface and bed), or null: Theme Park World has no lake model.</summary>
	public ModelFile? LakeModel { get; }
	public ModelHeightfield Heightfield { get; }
	public OriginalParkImport? Save { get; }
	public IReadOnlyDictionary<int, OriginalObjectInfo> Catalog { get; }

	public string TerrainDirectory => $"/levels/{LevelName}/terrain";

	public string DescribeObject( int infoId ) => Catalog.TryGetValue( infoId, out var info ) ? info.Name : $"Info.Id {infoId}";

	/// <param name="readShippedSave">False starts the level without its shipped save (a new Full Simulation park, <see cref="ParkStart.ReadsShippedSave"/>).</param>
	public static OriginalPark Load( string levelName, bool readShippedSave = true )
	{
		if ( string.IsNullOrWhiteSpace( levelName ) || levelName.IndexOfAny( new[] { '/', '\\', '.' } ) >= 0 )
			throw new ArgumentException( "Original level names are plain directory names such as 'jungle'.", nameof( levelName ) );
		var map = new MapFile( $"/levels/{levelName}/terrain/base.map" );
		var model = new ModelFile( $"/levels/{levelName}/terrain/base.MD2" );
		// [APPROX:COMPAT-019] Theme Park Inc ships its lake as a separate model; the finished lake (dlake.MD2, not lake.MD2 with its building site) is drawn
		var lakePath = FileSystem.GetFiles( $"/levels/{levelName}/terrain" ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), LakeModelName, StringComparison.OrdinalIgnoreCase ) );
		var lake = lakePath == null ? null : new ModelFile( ToRelative( lakePath ) );
		var catalog = LoadCatalog( levelName );
		OriginalParkImport? save = null;
		// [DATA:PreBuilt/prebuilt.TPWS:cell grid] The sole shipped Theme Park Inc prebuilt save matches science/base.map in every cell.
		var tpiPrebuilt = GameEditions.IsThemeParkInc && levelName.Equals( "science", StringComparison.OrdinalIgnoreCase );
		var saveDirectory = tpiPrebuilt ? "/PreBuilt" : $"/levels/{levelName}";
		var saveName = tpiPrebuilt ? "prebuilt.TPWS" : SaveFileName;
		var savePath = !readShippedSave || (GameEditions.IsThemeParkInc && !tpiPrebuilt) || !FileSystem.DirectoryExists( saveDirectory )
			? null : FileSystem.GetFiles( saveDirectory )
				.FirstOrDefault( path => string.Equals( Path.GetFileName( path ), saveName, StringComparison.OrdinalIgnoreCase ) );
		// [BIN:STP-PPC:0x10137600 player save setup] the level's easymode park is copied into a player's saves only for Instant Action players (0x1013741C passes the mode flag)
		if ( savePath != null )
		{
			using var stream = FileSystem.OpenRead( $"{saveDirectory}/{Path.GetFileName( savePath )}" );
			using var reader = new SaveReader( stream );
			var payload = reader.ReadFile();
			save = tpiPrebuilt ? OriginalParkImport.ImportThemeParkInc( payload, map ) : OriginalParkImport.Import( payload, map );
			foreach ( var item in save.PlacedObjects.Select( item => item.Record ).Concat( save.FixedItems ).Concat( save.UnresolvedObjects ) )
			{
				// [DATA:PreBuilt/prebuilt.TPWS:SYSG] Five science scene records have no archive and use the sentinel cell (0, 0).
				if ( tpiPrebuilt && item.IsFixedItem && item.X == 0 && item.Y == 0
					&& item.InfoId is 1702 or 1701 or 1700 or 1750 or 1798 )
					continue;
				if ( !catalog.ContainsKey( item.InfoId ) )
					throw new InvalidDataException( $"Original save object {item.Index} has Info.Id {item.InfoId}, which no {levelName} object archive declares." );
			}
		}
		return new OriginalPark( levelName, map, model, lake, save, catalog );
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
