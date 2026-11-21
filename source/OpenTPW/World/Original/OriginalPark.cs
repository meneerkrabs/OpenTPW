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
	private static readonly string[] ObjectDirectories = { "rides", "shops", "sideshow", "features", "upgrades", "obstacles" };
	private static readonly System.Text.RegularExpressions.Regex InfoIdPattern = new( @"^\s*Info\.Id\s+(\d+)", System.Text.RegularExpressions.RegexOptions.Multiline );
	private static readonly System.Text.RegularExpressions.Regex InfoNamePattern = new( "^\\s*Info\\.Name\\s+\"([^\"\\r\\n]*)\"", System.Text.RegularExpressions.RegexOptions.Multiline );

	/// <summary>The name of the lake model <c>terrain.wad</c> of Theme Park Inc's Science world holds; the level draws the lake as its obstacle objects instead (<see cref="LakeModel"/>).</summary>
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
	/// <summary>A <c>terrain/dlake.MD2</c> of a level without an <c>obstacles</c> folder, or null: Theme Park World has no lake model, and Theme Park Inc's lake is the obstacle 1703 (or 1704 once drained).</summary>
	public ModelFile? LakeModel { get; }
	public ModelHeightfield Heightfield { get; }
	public OriginalParkImport? Save { get; }
	public IReadOnlyDictionary<int, OriginalObjectInfo> Catalog { get; }

	public string TerrainDirectory => $"/levels/{LevelName}/terrain";

	public string DescribeObject( int infoId ) => Catalog.TryGetValue( infoId, out var info ) ? info.Name : $"Info.Id {infoId}";

	/// <param name="readShippedSave">False starts the level without its shipped save (a new Full Simulation park, <see cref="ParkStart.ReadsShippedSave"/>).</param>
	/// <param name="savePayload">The decoded payload of a player's original park save of this level (an <c>autosave.TPWS</c> or a named park, <c>restart.INTS</c>) to start from instead of the level's shipped <c>Easymode.TPWI</c>.</param>
	public static OriginalPark Load( string levelName, bool readShippedSave = true, ReadOnlyMemory<byte>? savePayload = null ) => LoadSteps( levelName, readShippedSave, savePayload ).Last()!;

	/// <summary>
	/// <see cref="Load"/> in steps, for a park built over frames (docs/BROWSER-PERFORMANCE.md): null after each part of the work,
	/// then the park.
	/// </summary>
	internal static IEnumerable<OriginalPark?> LoadSteps( string levelName, bool readShippedSave = true, ReadOnlyMemory<byte>? savePayload = null )
	{
		if ( string.IsNullOrWhiteSpace( levelName ) || levelName.IndexOfAny( new[] { '/', '\\', '.' } ) >= 0 )
			throw new ArgumentException( "Original level names are plain directory names such as 'jungle'.", nameof( levelName ) );
		var map = new MapFile( $"/levels/{levelName}/terrain/base.map" );
		yield return null;
		var model = new ModelFile( $"/levels/{levelName}/terrain/base.MD2" );
		yield return null;
		// [BIN:TPI-EXE:0x647BF0 obstacle placement] Theme Park Inc's lake is the mission obstacle 1703 (obstacles/lake.wad, lake.MD2) and after its opening 1704 (obstacles/dlake.wad, dlake.MD2), objects
		// of the world (docs/reverse/TPI-MISSIONS.md §6); terrain.wad's own dlake.MD2 is another file (119,268 bytes against the obstacle's 139,256), and no code of the executable names a lake
		// model, so a level with an obstacles folder draws its lake as those objects only
		var lakePath = GameEditions.IsThemeParkInc && FileSystem.DirectoryExists( $"/levels/{levelName}/obstacles" ) ? null
			: FileSystem.GetFiles( $"/levels/{levelName}/terrain" ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), LakeModelName, StringComparison.OrdinalIgnoreCase ) );
		var lake = lakePath == null ? null : new ModelFile( ToRelative( lakePath ) );
		var catalog = new Dictionary<int, OriginalObjectInfo>();
		using ( LoadProfile.Measure( "object-catalog" ) )
		{
			foreach ( var _ in LoadCatalogSteps( levelName, catalog ) )
				yield return null;
		}
		OriginalParkImport? save = null;
		// [DATA:PreBuilt/prebuilt.TPWS:cell grid] The sole shipped Theme Park Inc prebuilt save matches science/base.map in every cell.
		var tpiPrebuilt = GameEditions.IsThemeParkInc && levelName.Equals( "science", StringComparison.OrdinalIgnoreCase );
		var saveDirectory = tpiPrebuilt ? "/PreBuilt" : $"/levels/{levelName}";
		var saveName = tpiPrebuilt ? "prebuilt.TPWS" : SaveFileName;
		var savePath = !readShippedSave || (GameEditions.IsThemeParkInc && !tpiPrebuilt) || !FileSystem.DirectoryExists( saveDirectory )
			? null : FileSystem.GetFiles( saveDirectory )
				.FirstOrDefault( path => string.Equals( Path.GetFileName( path ), saveName, StringComparison.OrdinalIgnoreCase ) );
		// a player's own save of a Theme Park World level replaces the shipped one (Theme Park World only: the Theme Park Inc viewer has its prebuilt save)
		if ( savePayload is { } playerPayload && !GameEditions.IsThemeParkInc )
		{
			yield return null;
			using var playerProfile = LoadProfile.Measure( "player-save" );
			var playerSave = ImportOriginalSave( levelName, playerPayload, map );
			foreach ( var item in playerSave.PlacedObjects.Select( item => item.Record ).Concat( playerSave.FixedItems ).Concat( playerSave.UnresolvedObjects ) )
			{
				if ( !catalog.ContainsKey( item.InfoId ) )
					throw new InvalidDataException( $"Original save object {item.Index} has Info.Id {item.InfoId}, which no {levelName} object archive declares." );
			}
			yield return new OriginalPark( levelName, map, model, lake, playerSave, catalog );
			yield break;
		}
		// [BIN:STP-PPC:0x10137600 player save setup] the level's easymode park is copied into a player's saves only for Instant Action players (0x1013741C passes the mode flag)
		if ( savePath != null )
		{
			yield return null;
			using var saveProfile = LoadProfile.Measure( "shipped-save" );
			using var stream = FileSystem.OpenRead( $"{saveDirectory}/{Path.GetFileName( savePath )}" );
			using var reader = new SaveReader( stream );
			var payload = reader.ReadFile();
			save = tpiPrebuilt ? OriginalParkImport.ImportThemeParkInc( payload, map ) : ImportOriginalSave( levelName, payload, map );
			// [DATA:PreBuilt/prebuilt.TPWS:SYSG] the five science scene records at the sentinel cell (0, 0) (1702, 1701, 1700, 1750, 1798) are the obstacles and the camera path of levels/science/obstacles
			foreach ( var item in save.PlacedObjects.Select( item => item.Record ).Concat( save.FixedItems ).Concat( save.UnresolvedObjects ) )
			{
				if ( !catalog.ContainsKey( item.InfoId ) )
					throw new InvalidDataException( $"Original save object {item.Index} has Info.Id {item.InfoId}, which no {levelName} object archive declares." );
			}
		}
		yield return new OriginalPark( levelName, map, model, lake, save, catalog );
	}

	/// <summary>The level's shipped <c>Easymode.TPWI</c> alone (Theme Park World levels; null for Theme Park Inc or a level without one), for a resumed park's legacy repairs.</summary>
	internal static OriginalParkImport? ReadShippedSave( string levelName, MapFile map )
	{
		try
		{
			var directory = $"/levels/{levelName}";
			if ( GameEditions.IsThemeParkInc || !FileSystem.DirectoryExists( directory ) )
				return null;
			var path = FileSystem.GetFiles( directory ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), SaveFileName, StringComparison.OrdinalIgnoreCase ) );
			if ( path == null )
				return null;
			using var stream = FileSystem.OpenRead( $"{directory}/{Path.GetFileName( path )}" );
			using var reader = new SaveReader( stream );
			return ImportOriginalSave( levelName, reader.ReadFile(), map );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException )
		{
			// This is optional repair input, not the player's park. Mandatory imports still report their errors.
			Log.Trace( $"[EXT:legacy-save-queue-cells] {levelName}: shipped save unavailable for repair ({exception.GetType().Name})." );
			return null;
		}
	}

	/// <summary>
	/// Imports a Theme Park World save of <paramref name="levelName"/> with the resolvers the game's data gives the reader: the coaster train
	/// layouts and each object's animation control count, <c>max(1, UsageInfo.NumSimultAnims)</c> of its .sam (the count tp.exe's type entry
	/// holds, 0x4629d0), which the RideSystem block does not store. The object catalogue is read once, on the first record that asks.
	/// </summary>
	internal static OriginalParkImport ImportOriginalSave( string levelName, ReadOnlyMemory<byte> payload, MapFile map )
	{
		var objects = new Lazy<ObjectCatalog>( () => ObjectCatalog.Load( levelName ) );
		return OriginalParkImport.Import( payload, map, infoId => CoasterTrainLayout( levelName, infoId ),
			infoId => objects.Value.Find( infoId )?.NumSimultaneousAnimations );
	}

	/// <summary>
	/// The train layout of the coaster ride <paramref name="infoId"/> for the original save's Coasters block: the train slots (<c>sCoasterType.uiMaxTrains</c>, type +0x2E8)
	/// and the cars of a train (<c>sTrainType.uiMaxCars</c>, type +0x2F4) the loader 0x437B60 loops over. Null for an object that is no track ride. The object catalogue is read
	/// only when a save holds a coaster.
	/// </summary>
	private static SaveCoasterTrainLayout? CoasterTrainLayout( string levelName, int infoId )
	{
		var entry = ObjectCatalog.Load( levelName ).Find( infoId );
		if ( entry?.SharedSettings == null )
			return null;
		var definition = CoasterDefinition.FromEntry( entry );
		return new SaveCoasterTrainLayout( definition.MaxTrains, definition.CarCount );
	}

	/// <summary>Reads Info.Id and Info.Name from every object archive's .sam under the level.</summary>
	public static IReadOnlyDictionary<int, OriginalObjectInfo> LoadCatalog( string levelName )
	{
		var catalog = new Dictionary<int, OriginalObjectInfo>();
		foreach ( var _ in LoadCatalogSteps( levelName, catalog ) ) { }
		return catalog;
	}

	/// <summary><see cref="LoadCatalog"/> into <paramref name="catalog"/>, one object archive per step.</summary>
	private static IEnumerable<bool> LoadCatalogSteps( string levelName, Dictionary<int, OriginalObjectInfo> catalog )
	{
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
				yield return true;
			}
		}
	}

	// Entries inside archives come back data-root-relative, loose entries absolute.
	private static string ToRelative( string entry ) => FileSystem.GetRelativePath( FileSystem.GetAbsolutePath( entry ) );
}
