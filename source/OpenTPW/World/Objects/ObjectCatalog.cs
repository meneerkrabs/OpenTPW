using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>Object archive directory of a theme; <c>Info.WhichUIType</c> is kept separately.</summary>
public enum ObjectCategory
{
	Ride,
	Shop,
	Sideshow,
	Feature,
	Upgrade
}

/// <summary>Raw values of one <c>Upgrades[n]</c> level (cost, capacity, duration, ...), as the .sam layers give them.</summary>
public sealed record ObjectUpgradeLevel( int Level, IReadOnlyDictionary<string, string> Values )
{
	public int GetInt( string field, int fallback = 0 ) =>
		Values.TryGetValue( field, out var value ) && int.TryParse( value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result ) ? result : fallback;

	public float GetFloat( string field, float fallback = 0 ) =>
		Values.TryGetValue( field, out var value ) && float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result ) ? result : fallback;
}

/// <summary>
/// Economy-relevant raw .sam values: build cost (<c>Upgrades[0].CostOfUpgrade</c>), initial price per use and
/// cost of goods (shops/sideshows), sideshow chance of losing, and every upgrade level (CostOfUpgrade,
/// CostOfResearch, InitCapacity, InitDuration, WearRate, QueueWaitTimeConstant, ...). No upkeep field exists
/// in the object files; running costs must come from other evidence.
/// </summary>
public sealed record ObjectEconomyInfo( int BuildCost, int? PricePerUse, int? CostOfGoods, int? ChanceOfLosing, IReadOnlyList<ObjectUpgradeLevel> Upgrades );

/// <summary>
/// One original object (ride, shop, sideshow, feature, fixed item or track upgrade) of a theme, built from
/// its WAD archive and .sam layers. Values are raw original data; see docs/OBJECTS.md for what is verified.
/// </summary>
public sealed class ObjectCatalogEntry
{
	internal ObjectCatalogEntry( string theme, ObjectCategory category, string archivePath, ObjectSettings settings, ObjectShape shape,
		string modelPath, string? previewModelPath, string? scriptPath, IReadOnlyList<string> scripts, IReadOnlyList<ObjectAnimationFile> animations,
		IReadOnlyList<string> auxiliaryModels, IReadOnlyList<string> allModels, BaseFileSystem fileSystem, int? bonusNumber, ContentRoot? root = null )
	{
		FileSystem = fileSystem;
		BonusNumber = bonusNumber;
		Root = root ?? ( bonusNumber != null ? ContentRoot.Bonus : ContentRoot.Data );
		Theme = theme;
		Category = category;
		ArchivePath = archivePath;
		Settings = settings;
		Shape = shape;
		ModelPath = modelPath;
		PreviewModelPath = previewModelPath;
		ScriptPath = scriptPath;
		Scripts = scripts;
		Animations = animations;
		AuxiliaryModels = auxiliaryModels;
		Models = allModels;
		// [DATA:<object>.sam:Info.Id] [DATA:<object>.sam:Info.Name]
		InfoId = settings.GetInt( "Info.Id", -1 );
		SettingsName = settings["Info.Name"] ?? ArchiveName;
		DisplayName = SettingsName;
		var levels = new SortedDictionary<int, Dictionary<string, string>>();
		foreach ( var (key, value) in settings.Values )
		{
			var match = UpgradeKey.Match( key );
			if ( !match.Success )
				continue;
			var level = int.Parse( match.Groups[1].Value, CultureInfo.InvariantCulture );
			if ( !levels.TryGetValue( level, out var fields ) )
				levels.Add( level, fields = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase ) );
			fields[match.Groups[2].Value] = value;
		}
		Upgrades = levels.Select( pair => new ObjectUpgradeLevel( pair.Key, pair.Value ) ).ToList().AsReadOnly();
	}

	private static readonly Regex UpgradeKey = new( @"^Upgrades\[(\d+)\]\.(.+)$", RegexOptions.CultureInvariant );

	/// <summary>File system the archive is read from: the game data or the bonus-content root.</summary>
	public BaseFileSystem FileSystem { get; }
	/// <summary>The N of a bonus archive <c>_name_N</c> (official bonus content), else null.</summary>
	public int? BonusNumber { get; }
	/// <summary>The content root the archive comes from (game data, official bonus content or a mod); it prefixes the entry's asset-manifest keys.</summary>
	// [EXT:mods] OpenTPW's own mod system; the original game had no mods
	public ContentRoot Root { get; }
	public bool IsBonus => Root.IsBonus;
	public string Theme { get; }
	public ObjectCategory Category { get; }
	/// <summary>Data-root-relative archive path, e.g. <c>/levels/jungle/rides/totem</c>.</summary>
	public string ArchivePath { get; }
	public string ArchiveName => Path.GetFileName( ArchivePath );
	public ObjectSettings Settings { get; }
	public ObjectShape Shape { get; }
	public int InfoId { get; }
	/// <summary>The designers' English <c>Info.Name</c> from the .sam.</summary>
	public string SettingsName { get; }
	/// <summary>
	/// OBJECT_NAMES text when <see cref="ObjectNameIndex"/> resolved; for bonus objects the <c>NAME</c> of the
	/// archive's &lt;language&gt;.txt (English as fallback); else <see cref="SettingsName"/>.
	/// </summary>
	public string DisplayName { get; internal set; }
	/// <summary>
	/// The original's info-record name (+0x410) that the sign generator draws for an object built from the catalogue: the
	/// joined OBJECT_NAMES entries of the object's Info.Id row, or the empty string when the id is not in the table (or its
	/// row names the empty entry). Null where OpenTPW has no such binding (Theme Park Inc, no OBJECT_NAMES, bonus objects),
	/// and then <see cref="DisplayName"/> stands in.
	/// </summary>
	public string? SignName { get; internal set; }
	/// <summary>Index of the first OBJECT_NAMES entry of this object (names may span two entries), or null.</summary>
	public int? ObjectNameIndex { get; internal set; }

	/// <summary>
	/// The archive's shared (non-Info.Id) .sam files on their own (the TrackMaster <c>coaster.sam</c> of the 12 Theme Park World track rides, and Theme Park Inc's <c>bumper.sam</c>), or null when it has none.
	/// They are kept apart from <see cref="Settings"/> as the original's object loader does (<see cref="CoasterDefinition.FromEntry"/> reads them).
	/// </summary>
	public ObjectSettings? SharedSettings { get; internal set; }

	/// <summary>The settings the coaster loader reads (definition, cross sections, pylons, car types): coaster.sam alone, or the object settings for an entry without shared files (synthetic ones).</summary>
	public ObjectSettings CoasterSettings => SharedSettings ?? Settings;
	/// <summary>Number of OBJECT_NAMES entries the name uses (1 or 2).</summary>
	public int ObjectNameLength { get; internal set; }
	public string ModelPath { get; }
	/// <summary><c>P&lt;model&gt;.MD2</c>: the build-menu preview model (with <c>Info.PreviewAnimType/Num</c>), when present.</summary>
	public string? PreviewModelPath { get; }
	/// <summary>Main RSE script (named like the archive), or null for objects without a script.</summary>
	public string? ScriptPath { get; }
	public IReadOnlyList<string> Scripts { get; }
	/// <summary>Animation members of the main model (see <see cref="ObjectAnimations"/>).</summary>
	public IReadOnlyList<ObjectAnimationFile> Animations { get; }
	/// <summary>Other geometry members (vehicles, track pieces, pylons, preview model).</summary>
	public IReadOnlyList<string> AuxiliaryModels { get; }
	/// <summary>All .MD2 members of the archive.</summary>
	public IReadOnlyList<string> Models { get; }
	public IReadOnlyList<ObjectUpgradeLevel> Upgrades { get; }

	/// <summary><c>Info.WhichUIType</c>: 0 rides, 1 shops, 2 sideshows, 3 features, 4 not shown (fixed items).</summary>
	public int WhichUIType => Settings.GetInt( "Info.WhichUIType", -1 );
	public bool IsChoosable => Settings.GetBool( "Info.IsChoosable" );
	/// <summary><c>Info.OverwritePriority</c> ("Overwrite priority order"): an object may be built over objects of a lower priority, which it demolishes (rides, shops and sideshows 5, small scenery 1, other features 0).</summary>
	// [BIN:TPW-EXE:0x745020 sam schema] OverwritePriority follows WhichUIType (+0x4C) and DontDeformBase (+0x50) at +0x54, the field the validator 0x535670 compares (0x535785) and the hover 0x5234D0 loads into 0x818C10
	public int OverwritePriority => Settings.GetInt( "Info.OverwritePriority" );
	public bool HasQueue => Settings.GetBool( "Info.HasQueue" );
	/// <summary><c>Info.DontApplyOffset 1</c>: "a fixed item whose animation should be played relative to world (0,0)" (gates, bus, lights, ferry, seaplane, end).</summary>
	// [DATA:<object>.sam:Info.DontApplyOffset]
	public bool IsFixedItem => Settings.GetBool( "Info.DontApplyOffset" );
	/// <summary>Shapes without occupied cells (Mystery ride, Buy Land, Clear Land) are tools, not objects.</summary>
	public bool IsTool => !Shape.OccupiedCells.Any();
	/// <summary>Can be placed from a build menu: shown in a UI category, not a fixed item or tool. Upgrades attach to a ride's track.</summary>
	// [BIN:TPW-EXE:0x004AAF70 build menu filler] the build-menu filler lists a definition when its model loaded (def+0x508 == 0), its WhichUIType (def+0x4AC, copied from +0x4C at the end of 0x413410) equals the tab, AddOn.UpgradesId (def+0x284) is 0 and the control record 0x4D3D10(id)+0x10 is non-zero (availability or research); tools and fixed items are not filtered by name, their data has WhichUIType 4, and every upgrade-category object has AddOn.UpgradesId != 0, which gives this rule on the whole corpus
	public bool IsBuildable => WhichUIType is >= 0 and <= 3 && !IsFixedItem && !IsTool && Category != ObjectCategory.Upgrade;
	/// <summary><c>Info.RideTypeStringIndex</c>: ITEMTYPES.str entry (e.g. 12 "Vertical Drop" for the Totem), or null.</summary>
	public int? RideTypeIndex => Settings.Has( "Info.RideTypeStringIndex" ) ? Settings.GetInt( "Info.RideTypeStringIndex" ) : null;
	// [BIN:TPW-EXE:0x745020 sam schema] CreateParticleEffect, DestroyParticleEffect, RepairParticleEffect and UpgradeParticleEffect follow each other in the type record (+0x60, +0x64, +0x68, +0x6C, matched by schema order and by role); 0 or an absent key starts nothing
	// [DATA:<object>.sam:Info.CreateParticleEffect] jungle rides 79/75/51/89, shops 81/77, sideshows and upgrades 80/76, features 82/78 (no repair or upgrade key)
	/// <summary><c>Info.CreateParticleEffect</c>: the effect started when the object is built (0: none).</summary>
	public int CreateParticleEffect => Settings.GetInt( "Info.CreateParticleEffect", 0 );
	/// <summary><c>Info.DestroyParticleEffect</c>: the effect started when the object is demolished (0: none).</summary>
	public int DestroyParticleEffect => Settings.GetInt( "Info.DestroyParticleEffect", 0 );
	/// <summary><c>Info.RepairParticleEffect</c>: the effect the script's REPAIREFFECT switches (0: none).</summary>
	public int RepairParticleEffect => Settings.GetInt( "Info.RepairParticleEffect", 0 );
	/// <summary><c>Info.UpgradeParticleEffect</c>: the effect started when an upgrade is installed (0: none).</summary>
	public int UpgradeParticleEffect => Settings.GetInt( "Info.UpgradeParticleEffect", 0 );
	public int NumSimultaneousAnimations => Math.Max( 1, Settings.GetInt( "UsageInfo.NumSimultAnims", 1 ) );
	/// <summary>Build cost: <c>Upgrades[0].CostOfUpgrade</c> ("cash cost when buying this item").</summary>
	// [DATA:<object>.sam:Upgrades[0].CostOfUpgrade]
	public int BuildCost => Upgrades.FirstOrDefault( level => level.Level == 0 )?.GetInt( "CostOfUpgrade" ) ?? 0;
	/// <summary>Initial ride capacity: <c>Upgrades[0].InitCapacity</c>.</summary>
	public int InitialCapacity => Upgrades.FirstOrDefault( level => level.Level == 0 )?.GetInt( "InitCapacity" ) ?? 0;

	public ObjectAnimationFile? ResolveAnimation( int animation, int variant ) => ObjectAnimations.Resolve( Animations, animation, variant );

	/// <summary>
	/// Build-menu preview clip: <c>Info.PreviewAnimType</c> ("m" = main loop) and <c>Info.PreviewAnimNum</c> on the
	/// preview model (<c>Ptotemm</c>, <c>Pspiderm1</c>, <c>PlookoutM</c>), or null.
	/// </summary>
	public string? PreviewAnimationPath
	{
		get
		{
			if ( PreviewModelPath == null )
				return null;
			var type = Settings["Info.PreviewAnimType"];
			var letter = string.IsNullOrEmpty( type ) ? 'm' : char.ToLowerInvariant( type[0] );
			var animation = ObjectAnimations.GetAnimation( letter );
			if ( animation == null )
				return null;
			var clips = ObjectAnimations.Find( Path.GetFileNameWithoutExtension( PreviewModelPath ), Models );
			return ObjectAnimations.Resolve( clips, animation.Value, Math.Max( 0, Settings.GetInt( "Info.PreviewAnimNum", 1 ) - 1 ) )?.Path;
		}
	}

	/// <summary>Raw economy values for the economy slice (no rules are applied here).</summary>
	public ObjectEconomyInfo Economy => new(
		BuildCost,
		Settings.Has( "UsageInfo.InitPricePerUse" ) ? Settings.GetInt( "UsageInfo.InitPricePerUse" ) : null,
		Settings.Has( "UsageInfo.InitCostOfGoods" ) ? Settings.GetInt( "UsageInfo.InitCostOfGoods" ) : null,
		Settings.Has( "UsageInfo.InitChanceOfLoosing" ) ? Settings.GetInt( "UsageInfo.InitChanceOfLoosing" ) : null,
		Upgrades );

	public override string ToString() => $"{InfoId} {DisplayName} ({Theme}/{Category}/{ArchiveName})";
}

/// <summary>
/// Every object of one theme: the archives under <c>/levels/&lt;theme&gt;/{rides,shops,sideshow,features,upgrades}</c>,
/// each identified by the <c>Info.Id</c> of its .sam (the same ids the TPWI save records use).
/// </summary>
public sealed class ObjectCatalog
{
	public static readonly IReadOnlyList<string> Themes = new[] { "jungle", "hallow", "space", "fantasy" };

	private static readonly (string Directory, ObjectCategory Category, string Defaults)[] CategoryDirectories =
	{
		("rides", ObjectCategory.Ride, "Rides.sam"),
		("shops", ObjectCategory.Shop, "Shops.sam"),
		("sideshow", ObjectCategory.Sideshow, "SideShow.sam"),
		("features", ObjectCategory.Feature, "Features.sam"),
		("upgrades", ObjectCategory.Upgrade, "Upgrades.sam")
	};

	private static readonly ObjectCategory[] OriginalScanOrder = { ObjectCategory.Ride, ObjectCategory.Feature, ObjectCategory.Upgrade, ObjectCategory.Shop, ObjectCategory.Sideshow };

	// Keyed by the file system instance, not its root: entries keep the instance they were read from, and
	// a replaced FileSystem (setup, tests) with the same root must not hand out entries of the old one.
	private static readonly ConditionalWeakTable<BaseFileSystem, Dictionary<string, ObjectCatalog>> cache = new();
	private static string? bonusDataRoot;
	private static bool bonusFromEnvironment = true;

	/// <summary>
	/// Optional official bonus-content directory (read-only), from <c>--bonus-data</c>, <c>OPENTPW_BONUS_DATA</c>, the saved setup or <c>&lt;config&gt;/bonus</c> (see <c>BonusContent.ResolveRoot</c>).
	/// It may point at the directory containing <c>levels</c> or any parent up to two levels above it. Its
	/// <c>levels/&lt;theme&gt;/&lt;category&gt;/_name_N.wad</c> archives are merged into the catalogs as if they were
	/// dropped into the game's Data directories.
	/// </summary>
	public static string? BonusDataRoot
	{
		get
		{
			if ( bonusFromEnvironment )
			{
				var value = Environment.GetEnvironmentVariable( "OPENTPW_BONUS_DATA" );
				return string.IsNullOrWhiteSpace( value ) ? null : value;
			}
			return bonusDataRoot;
		}
		set
		{
			bonusDataRoot = string.IsNullOrWhiteSpace( value ) ? null : value;
			bonusFromEnvironment = false;
		}
	}

	/// <summary>Finds the directory that contains <c>levels</c> (case-insensitive) at or below <paramref name="root"/>.</summary>
	public static string? FindBonusLevelsParent( string root )
	{
		if ( !Directory.Exists( root ) )
			return null;
		var pending = new List<string> { root };
		for ( var depth = 0; depth <= 2 && pending.Count > 0; depth++ )
		{
			foreach ( var directory in pending )
			{
				if ( Directory.EnumerateDirectories( directory ).Any( child => string.Equals( Path.GetFileName( child ), "levels", StringComparison.OrdinalIgnoreCase ) ) )
					return directory;
			}
			pending = pending.SelectMany( Directory.EnumerateDirectories ).ToList();
		}
		return null;
	}

	private static BaseFileSystem? OpenBonusFileSystem()
	{
		var root = BonusDataRoot;
		if ( root == null )
			return null;
		var parent = FindBonusLevelsParent( Path.GetFullPath( root ) )
			?? throw new DirectoryNotFoundException( $"Bonus content directory '{root}' does not contain a levels directory." );
		var fileSystem = new BaseFileSystem( parent );
		fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		return fileSystem;
	}
	private readonly Dictionary<int, ObjectCatalogEntry> byId;

	private ObjectCatalog( string theme, IReadOnlyList<ObjectCatalogEntry> entries )
	{
		Theme = theme;
		Entries = entries;
		// [BIN:TPW-EXE:0x00412E90 definition by id] the id lookup scans the definition list from the start and returns the first whose
		// id word matches (one-entry cache at +0x730/+0x734; 0x00412EF0 likewise), so a later definition with a taken Info.Id stays loaded
		// but every lookup by id finds the first one in load order
		// [BIN:TPW-EXE:0x00413140 category scan order] the categories are scanned Rides, Features, Upgrades, Shops, Sideshow (0x00747B24, 0x00747B94,
		// 0x00747B74, 0x00747B80, 0x00747B88), so a taken id resolves across categories in that order
		byId = new Dictionary<int, ObjectCatalogEntry>();
		foreach ( var entry in entries.OrderBy( entry => Array.IndexOf( OriginalScanOrder, entry.Category ) ) )
			if ( !byId.TryAdd( entry.InfoId, entry ) )
				Log?.Warning( $"{entry.ArchivePath} reuses Info.Id {entry.InfoId}; lookups by that id find {byId[entry.InfoId].ArchivePath}." );
	}

	public string Theme { get; }
	public IReadOnlyList<ObjectCatalogEntry> Entries { get; }
	public IEnumerable<ObjectCatalogEntry> Buildable => Entries.Where( entry => entry.IsBuildable );

	public ObjectCatalogEntry? Find( int infoId ) => byId.TryGetValue( infoId, out var entry ) ? entry : null;

	public ObjectCatalogEntry Get( int infoId ) => Find( infoId ) ?? throw new KeyNotFoundException( $"No {Theme} object archive declares Info.Id {infoId}." );

	/// <summary>Loads (and caches per file system) the catalog of a theme; names come from the English OBJECT_NAMES when present.</summary>
	/// <param name="easy">Instant Action: each object's <c>Easy_&lt;object file&gt;.sam</c> is layered last when present.</param>
	public static ObjectCatalog Load( string theme, bool easy = false )
	{
		lock ( cache )
			return LoadSteps( theme, easy ).Last()!;
	}

	/// <summary>
	/// <see cref="Load"/> in steps, for a park built over frames on the game thread (docs/BROWSER-PERFORMANCE.md): null after each
	/// object archive, then the catalog. Only the cache is locked, not the steps between; a catalog another load stored meanwhile wins.
	/// </summary>
	internal static IEnumerable<ObjectCatalog?> LoadSteps( string theme, bool easy = false )
	{
		if ( string.IsNullOrWhiteSpace( theme ) || theme.IndexOfAny( new[] { '/', '\\', '.' } ) >= 0 )
			throw new ArgumentException( "Themes are plain level directory names such as 'jungle'.", nameof( theme ) );
		var fileSystem = FileSystem;
		// The names come from the language's OBJECT_NAMES: a catalog loaded in another language is not reused.
		// [EXT:mods] the active mods' digest is part of the key: the same files-based mods give the same catalog, other mods another one
		var key = $"{BonusDataRoot}|{theme}|{easy}|{(GameLanguage.IsSelected ? GameLanguage.Current.Name : "")}|{OpenTpwExtensions.Active.Digest}";
		ObjectCatalog? cached;
		lock ( cache )
			cache.GetValue( fileSystem, _ => new Dictionary<string, ObjectCatalog>( StringComparer.OrdinalIgnoreCase ) ).TryGetValue( key, out cached );
		if ( cached != null )
		{
			yield return cached;
			yield break;
		}
		var entries = new List<ObjectCatalogEntry>();
		foreach ( var _ in LoadEntries( theme, fileSystem, OpenBonusFileSystem(), easy, entries ) )
			yield return null;
		ObjectCatalog catalog;
		lock ( cache )
		{
			var catalogs = cache.GetValue( fileSystem, _ => new Dictionary<string, ObjectCatalog>( StringComparer.OrdinalIgnoreCase ) );
			if ( !catalogs.TryGetValue( key, out catalog! ) )
			{
				catalog = new ObjectCatalog( theme, entries );
				RidesApproximations.LogOnce();
				ObjectNames.Apply( catalog );
				catalogs[key] = catalog;
			}
		}
		yield return catalog;
	}

	/// <summary>
	/// Reads the archives of <paramref name="fileSystem"/> and, when given, the bonus archives of <paramref name="bonusFileSystem"/>
	/// into <paramref name="entries"/>, one archive per step. Bonus archives are made to be dropped next to the game's own, so each
	/// category directory is read as one directory: both sets in one name order, the category defaults (Rides.sam, ...) always from
	/// the game data. Directory names are matched case-insensitively.
	/// </summary>
	// [BIN:TPW-EXE:0x00413930 archive scan] each found archive becomes an archive object named by 0x0041EC30 (see ArchiveKey); one whose
	// name equals a listed archive's (0x0041EDE0 over the list at +0x4C, _stricmp 0x00424CF0) is destroyed unread, any other is listed
	// (0x0041EAA0) and then loaded (0x00413C10), so the first archive of a name in scan order wins; definitions with a taken Info.Id
	// load too (see the constructor)
	private static IEnumerable<bool> LoadEntries( string theme, BaseFileSystem fileSystem, BaseFileSystem? bonusFileSystem, bool easy, List<ObjectCatalogEntry> entries )
	{
		foreach ( var (directory, category, defaultsName) in CategoryDirectories )
		{
			// Each category object keeps its own archive list (0x00413930 runs once per category, 0x00413140).
			var listed = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			var categoryPath = CategoryPath( fileSystem, theme, directory );
			var bonusPath = bonusFileSystem == null ? null : CategoryPath( bonusFileSystem, theme, directory );
			if ( categoryPath == null && bonusPath == null )
				continue;
			var defaultsDirectory = categoryPath ?? $"/levels/{theme}/{directory}";
			var defaults = fileSystem.DirectoryExists( defaultsDirectory )
				? fileSystem.GetFiles( defaultsDirectory ).Select( file => Relative( fileSystem, file ) )
					.FirstOrDefault( file => string.Equals( Path.GetFileName( file ), defaultsName, StringComparison.OrdinalIgnoreCase ) )
				: null;
			var defaultLayer = defaults == null ? null : ObjectSettingsFile.Load( fileSystem, defaults );
			var archives = Archives( fileSystem, categoryPath, false ).Concat( Archives( bonusFileSystem, bonusPath, true ) )
				.OrderBy( archive => Path.GetFileName( archive.Path ), StringComparer.OrdinalIgnoreCase ).ThenBy( archive => archive.Bonus );
			foreach ( var (archive, source, bonus) in archives )
			{
				if ( !listed.Add( ArchiveKey( Path.GetFileName( archive ) ) ) )
				{
					Log?.Warning( $"Object archive {archive} is skipped: an archive named {ArchiveKey( Path.GetFileName( archive ) )} is already loaded." );
					yield return true;
					continue;
				}
				ObjectCatalogEntry? entry = null;
				try
				{
					entry = LoadEntry( theme, category, archive, defaultLayer, source, easy );
				}
				catch ( Exception exception ) when ( bonus && exception is InvalidDataException or IOException )
				{
					// A broken bonus archive must not take the game's own catalog down.
					Log?.Warning( $"Bonus object archive {archive} is skipped: {exception.Message}" );
				}
				if ( entry != null )
					entries.Add( entry );
				yield return true;
			}
		}
	}

	private static string? CategoryPath( BaseFileSystem fileSystem, string theme, string directory ) =>
		FindDirectory( fileSystem, "/levels", theme ) is { } themePath ? FindDirectory( fileSystem, themePath, directory ) : null;

	private static IEnumerable<(string Path, BaseFileSystem Source, bool Bonus)> Archives( BaseFileSystem? fileSystem, string? categoryPath, bool bonus ) =>
		fileSystem == null || categoryPath == null ? []
			: fileSystem.GetDirectories( categoryPath ).Select( file => (Relative( fileSystem, file ), fileSystem, bonus) );

	/// <summary>
	/// The name the original gives an object archive (tp.exe <c>0x0041EC30</c>): the file name without <c>.wad</c>; a name with exactly
	/// two underscores loses everything from the second one (<c>_snake_3</c> is <c>_snake</c>, the name its members use), and then a
	/// leading lower-case <c>p</c>. Archives of one name load once.
	/// </summary>
	// [BIN:TPW-EXE:0x00424D30 extension strip] a name ending in ".wad" (0x007479A0 through 0x0078BB08, compared with _stricmp 0x0067DF40) loses it
	// [BIN:TPW-EXE:0x0041EB90 bonus name] first = find('_'), last = rfind('_'); unless first == last or another '_' lies between them the name becomes Left(last), and a Left(1) equal to "p" (0x0074C778, case-sensitive 0x005F7980) is dropped
	internal static string ArchiveKey( string fileName )
	{
		var name = fileName.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ? fileName[..^4] : fileName;
		var first = name.IndexOf( '_' );
		var last = name.LastIndexOf( '_' );
		if ( first == last || name.IndexOf( '_', first + 1 ) != last )
			return name;
		name = name[..last];
		return name.StartsWith( 'p' ) ? name[1..] : name;
	}

	private static string? FindDirectory( BaseFileSystem fileSystem, string parent, string name )
	{
		if ( fileSystem.DirectoryExists( $"{parent}/{name}" ) )
			return $"{parent}/{name}";
		if ( !fileSystem.DirectoryExists( parent ) )
			return null;
		return fileSystem.GetDirectories( parent ).Select( directory => Relative( fileSystem, directory ) )
			.FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), name, StringComparison.OrdinalIgnoreCase ) );
	}

	private static string Relative( BaseFileSystem fileSystem, string entry ) => fileSystem.GetRelativePath( fileSystem.GetAbsolutePath( entry ) );

	private static ObjectCatalogEntry? LoadEntry( string theme, ObjectCategory category, string archive, ObjectSettingsFile? defaults, BaseFileSystem fileSystem, bool easy )
	{
		var files = fileSystem.GetFiles( archive ).Select( file => Relative( fileSystem, file ) ).ToArray();
		// Official bonus archives are named _name_N (N = bonus number); their members use _name, the archive's name (ArchiveKey).
		var archiveName = Path.GetFileName( archive );
		var name = ArchiveKey( archiveName );
		int? bonusNumber = null;
		var suffix = archiveName[(archiveName.LastIndexOf( '_' ) + 1)..];
		if ( name != archiveName && suffix.Length > 0 && suffix.All( char.IsAsciiDigit ) )
			bonusNumber = int.Parse( suffix, CultureInfo.InvariantCulture );
		ObjectSettingsFile? main = null;
		string? mainFile = null;
		var shared = new List<ObjectSettingsFile>();
		foreach ( var file in files.Where( file => file.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) ) )
		{
			var fileName = Path.GetFileName( file );
			// Difficulty (Easy_) and online (Online_) overlays are applied after the object file, below.
			if ( fileName.StartsWith( "Easy_", StringComparison.OrdinalIgnoreCase ) || fileName.StartsWith( "Online_", StringComparison.OrdinalIgnoreCase ) )
				continue;
			var layer = ObjectSettingsFile.Load( fileSystem, file );
			if ( layer.Values.ContainsKey( "Info.Id" ) )
			{
				if ( main != null )
					throw new InvalidDataException( $"{archive} has several .sam files with an Info.Id." );
				main = layer;
				mainFile = file;
			}
			else
				shared.Add( layer );
		}
		if ( main == null )
			return null;
		// [BIN:TPW-EXE:0x00413C10 object loader] the object's settings stack is the base file the caller names, then <object>.sam, then Easy_<object>.sam in game type 2; a shared (non-Info.Id) file such as coaster.sam is never merged into it: only for track type 3 (0x4141EB) it is loaded into a separate settings object (0x41420B) that goes with the model to the TrackMaster loader 0x42FAD0 (0x4142DE)
		var layers = new List<ObjectSettingsFile>();
		if ( defaults != null )
			layers.Add( defaults );
		layers.Add( main );
		// [BIN:STP-PPC:0x10119328 object loader] In Instant Action (game type 2) Easy_<object file> is layered after the object file when it exists; Online_ files belong to the online game type and are not loaded offline
		var easyFile = easy ? files.FirstOrDefault( file => string.Equals( Path.GetFileName( file ), "Easy_" + Path.GetFileName( mainFile ), StringComparison.OrdinalIgnoreCase ) ) : null;
		if ( easyFile != null )
			layers.Add( ObjectSettingsFile.Load( fileSystem, easyFile ) );
		var settings = new ObjectSettings( layers );
		// [DATA:<object>.sam:Info.Shape]
		var shapeRows = settings.GetBlock( "Info.Shape" );
		var shape = shapeRows == null || shapeRows.All( row => row.Trim().Length == 0 ) ? ObjectShape.Single : new ObjectShape( shapeRows );

		var models = files.Where( file => file.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) ).OrderBy( file => file, StringComparer.OrdinalIgnoreCase ).ToArray();
		var model = models.FirstOrDefault( file => string.Equals( Path.GetFileNameWithoutExtension( file ), name, StringComparison.OrdinalIgnoreCase ) )
			?? throw new InvalidDataException( $"{archive} has no main model {name}.MD2." );
		var modelStem = Path.GetFileNameWithoutExtension( model );
		var animations = ObjectAnimations.Find( modelStem, models );
		var preview = models.FirstOrDefault( file => string.Equals( Path.GetFileNameWithoutExtension( file ), "P" + modelStem, StringComparison.OrdinalIgnoreCase ) );
		// Auxiliary geometry: members that are not an animation of another member.
		var stems = models.Select( file => Path.GetFileNameWithoutExtension( file ) ).ToArray();
		var animationMembers = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var stem in stems )
		{
			foreach ( var animation in ObjectAnimations.Find( stem, models ) )
				animationMembers.Add( animation.Path );
		}
		var auxiliary = models.Where( file => file != model && !animationMembers.Contains( file ) ).ToList().AsReadOnly();

		var scripts = files.Where( file => file.EndsWith( ".rse", StringComparison.OrdinalIgnoreCase ) ).OrderBy( file => file, StringComparer.OrdinalIgnoreCase ).ToList().AsReadOnly();
		var script = scripts.FirstOrDefault( file => string.Equals( Path.GetFileNameWithoutExtension( file ), name, StringComparison.OrdinalIgnoreCase ) );
		if ( script == null )
		{
			var candidates = scripts.Where( file => !string.Equals( Path.GetFileNameWithoutExtension( file ), "EventMap", StringComparison.OrdinalIgnoreCase ) ).ToArray();
			if ( candidates.Length == 1 )
				script = candidates[0];
		}
		var entry = new ObjectCatalogEntry( theme, category, archive, settings, shape, model, preview, script, scripts, animations, auxiliary, models, fileSystem, bonusNumber );
		if ( shared.Count > 0 )
			entry.SharedSettings = new ObjectSettings( shared );
		if ( bonusNumber != null )
			entry.DisplayName = BonusNames.Read( entry ) ?? entry.SettingsName;
		return entry;
	}

	// Entries inside archives come back data-root-relative, loose entries absolute.
	internal static string ToRelative( string entry ) => FileSystem.GetRelativePath( FileSystem.GetAbsolutePath( entry ) );
}

/// <summary>
/// Binds catalog entries to OBJECT_NAMES.str through the original's Info.Id table (<see cref="ObjectNameTable"/>): each row names
/// the first and the second entry of an object ("Inca", "Totem"; index 1 = no second part). An object whose id is not in the table
/// keeps its .sam name here (the original shows an empty name).
/// </summary>
public static class ObjectNames
{
	public const string FileName = "OBJECT_NAMES.str";
	public const string BlockTerminator = "Traffic Lights";

	public static void Apply( ObjectCatalog catalog )
	{
		var english = TryLoad( "English" );
		if ( english == null )
			return;
		var localized = GameLanguage.IsSelected ? TryLoadCurrent() ?? english : english;
		var entries = catalog.Entries.Where( entry => !entry.IsBonus ).ToList();
		// The Info.Id table is tp.exe's; Theme Park Inc ships another OBJECT_NAMES.str (other order and length) and keeps the English-name matcher.
		var tpw = GameEditions.Current == GameEdition.ThemeParkWorld;
		Assign( entries, tpw ? Bind( entries ) : Match( entries, english ), localized, tpw );
	}

	/// <summary>
	/// Pure naming step, exposed for tests: each bound entry gets its OBJECT_NAMES index and (when not empty) its display name;
	/// with the Info.Id table (<paramref name="infoIdTable"/>) every entry also gets its info-record <see cref="ObjectCatalogEntry.SignName"/>,
	/// empty for an entry that is not bound.
	/// </summary>
	internal static void Assign( IReadOnlyList<ObjectCatalogEntry> entries, IReadOnlyList<(ObjectCatalogEntry Entry, int Index, int Length)> bound, IReadOnlyList<string> localized, bool infoIdTable )
	{
		// [BIN:TPW-EXE:0x004146B0 object name getter] an id outside the table 0x7488D8 formats two empty parts with an empty separator ("%s%s%s" 0x74985C) into an empty info name
		if ( infoIdTable )
			foreach ( var entry in entries )
				entry.SignName = "";
		foreach ( var (entry, index, length) in bound )
		{
			entry.ObjectNameIndex = index;
			entry.ObjectNameLength = length;
			var joined = index + length <= localized.Count ? Join( localized.Skip( index ).Take( length ) ) : "";
			if ( infoIdTable )
				entry.SignName = joined;
			// An empty bound name (the four End items, entry 0) keeps the .sam name as display name.
			if ( joined.Length > 0 )
				entry.DisplayName = joined;
		}
	}

	/// <summary>
	/// The object's name from its OBJECT_NAMES entries: the texts joined by a space, empty ones dropped. A first entry that
	/// ends in '-' is joined to the next one without the space and the dash (compound words in Danish and similar).
	/// </summary>
	// [BIN:STP-PPC:0x1011A08C object name builder] name = first + (" " when the second is not empty) + second; when the first ends in '-' the dash is cut and the two are concatenated
	// [BIN:TPW-EXE:0x0041474E object name join] the PC builder agrees: a trailing '-' on the first text is cut and the texts are joined with "%s%s", otherwise "%s%s%s" with a space only when the second text is not empty
	public static string Join( IEnumerable<string> texts )
	{
		var parts = texts.ToArray();
		if ( parts.Length == 2 && parts[0].EndsWith( '-' ) )
			return parts[0][..^1] + parts[1];
		return string.Join( " ", parts.Where( text => text.Length > 0 ) );
	}

	/// <summary>Pure matching step, exposed for tests.</summary>
	// [EXT:tpi-object-name-matcher] Theme Park Inc object names: OBJECT_NAMES entries bound by English name equality within the theme block (the Info.Id table is tp.exe's; the TPI executable's own binding is not traced) — the original TPI rule is unknown
	public static IReadOnlyList<(ObjectCatalogEntry Entry, int Index, int Length)> Match( IReadOnlyList<ObjectCatalogEntry> entries, IReadOnlyList<string> names )
	{
		var blocks = new List<(int Start, int End)>();
		var start = 0;
		for ( var index = 0; index < names.Count; index++ )
		{
			if ( names[index] == BlockTerminator )
			{
				blocks.Add( (start, index) );
				start = index + 1;
			}
		}
		if ( blocks.Count == 0 )
			blocks.Add( (0, names.Count - 1) );
		List<(ObjectCatalogEntry, int, int)>? best = null;
		foreach ( var (blockStart, blockEnd) in blocks )
		{
			var matches = new List<(ObjectCatalogEntry, int, int)>();
			foreach ( var entry in entries )
			{
				var wanted = Normalize( entry.SettingsName );
				if ( wanted.Length == 0 )
					continue;
				var found = -1;
				var length = 1;
				for ( var index = blockStart; index <= blockEnd && found < 0; index++ )
				{
					if ( Normalize( names[index] ) == wanted )
						found = index;
				}
				for ( var index = blockStart; index < blockEnd && found < 0; index++ )
				{
					if ( names[index].Length > 0 && Normalize( names[index] + names[index + 1] ) == wanted )
					{
						found = index;
						length = 2;
					}
				}
				if ( found >= 0 )
					matches.Add( (entry, found, length) );
			}
			if ( best == null || matches.Count > best.Count )
				best = matches;
		}
		return best!.AsReadOnly();
	}

	internal static string Normalize( string text )
	{
		var decomposed = text.Normalize( NormalizationForm.FormD );
		var builder = new StringBuilder();
		foreach ( var character in decomposed )
		{
			if ( char.IsLetterOrDigit( character ) && CharUnicodeInfo.GetUnicodeCategory( character ) != UnicodeCategory.NonSpacingMark )
				builder.Append( char.ToLowerInvariant( character ) );
		}
		return builder.ToString();
	}

	/// <summary>
	/// Pure binding step, exposed for tests: the entries whose Info.Id is in the table, with the first OBJECT_NAMES index and the
	/// number of consecutive entries (1 when the second index is the empty string, else 2; every table row's second index follows its first).
	/// </summary>
	public static IReadOnlyList<(ObjectCatalogEntry Entry, int Index, int Length)> Bind( IReadOnlyList<ObjectCatalogEntry> entries )
	{
		var bound = new List<(ObjectCatalogEntry, int, int)>();
		foreach ( var entry in entries )
		{
			if ( ObjectNameTable.TryGet( entry.InfoId, out var first, out var second ) )
				bound.Add( (entry, first, second == ObjectNameTable.EmptyIndex ? 1 : 2) );
		}
		return bound.AsReadOnly();
	}

	private static string[]? TryLoad( string language )
	{
		var path = $"/Language/{language}/{FileName}";
		try
		{
			return FileSystem.FileExists( path ) ? new StringFile( path ).Entries : null;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			return null;
		}
	}

	private static string[]? TryLoadCurrent()
	{
		try
		{
			return GameLanguage.Current.LoadStrings( FileName ).Entries;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or InvalidOperationException )
		{
			return null;
		}
	}
}

/// <summary>
/// Names of official bonus objects. They are not in OBJECT_NAMES; each archive ships one UTF-16 text file per
/// language (<c>english.txt</c>, <c>German.txt</c>, ...) with <c>NAME</c>, <c>SIGNA</c> and <c>SIGNB</c>
/// sections (the name and the two sign texts). The selected language is used, then English; a missing or
/// unreadable file yields null and the caller keeps the .sam name.
/// </summary>
public static class BonusNames
{
	// [APPROX:RIDES-025] The original reads only <selected language>.txt (0x41f350, language buffer 0xf7f230) from the bonus archive and falls back to the OBJECT_NAMES id table (empty for bonus ids except 3301) with no English or .sam fallback; this port also tries English and then the .sam Info.Name for a language the archive lacks (OpenTPW extension for third-party content; all 35 shipped bonus archives carry the four shipped languages)
	public static string? Read( ObjectCatalogEntry entry, string? language = null )
	{
		language ??= GameLanguage.IsSelected ? GameLanguage.Current.Name : GameLanguage.DefaultLanguage;
		try
		{
			var files = entry.FileSystem.GetFiles( entry.ArchivePath ).Where( file => file.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase ) ).ToArray();
			foreach ( var wanted in new[] { language, GameLanguage.DefaultLanguage } )
			{
				var file = files.FirstOrDefault( candidate => string.Equals( Path.GetFileNameWithoutExtension( candidate ), wanted, StringComparison.OrdinalIgnoreCase ) );
				if ( file == null )
					continue;
				var bytes = entry.FileSystem.ReadAllBytes( entry.FileSystem.GetRelativePath( entry.FileSystem.GetAbsolutePath( file ) ) );
				var name = Parse( bytes ).GetValueOrDefault( "NAME" );
				if ( !string.IsNullOrWhiteSpace( name ) )
					return name;
			}
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException or DecoderFallbackException )
		{
			Log?.Warning( $"Bonus object {entry.ArchivePath} name could not be read: {exception.Message}" );
		}
		return null;
	}

	/// <summary>The text of an object text key its language file lacks (the default the original sets before parsing, 0x6FD880).</summary>
	public const string MissingString = "MISSING STRING";

	/// <summary>Characters a placed object keeps of each sign line (buffers of 32 characters and a terminator).</summary>
	public const int SignLineCharacters = 32;

	/// <summary>
	/// The <c>SIGNA</c>/<c>SIGNB</c> lines of a bonus archive's selected-language file (no English fallback), or null when the
	/// archive has no file for that language (the caller then draws the display name, not traced for NULL lines). A section the file lacks reads <see cref="MissingString"/>.
	/// Each line is cut to <see cref="SignLineCharacters"/> characters; line 1 ending in "--" loses one of its dashes (<see cref="StripTrailingDash"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x00412C65 object definition constructor] the object text is read from the selected language's file only; without that file the definition's text pointers stay NULL (0x412C65/0x412C6B); what the sign code does with a NULL line is untraced, so OpenTPW's caller (ObjectSigns.Lines) falls back to the object's display name
	// [BIN:TPW-EXE:0x00413600 object text] before parsing, NAME, SIGNA, SIGNB and +0x500 are set to L"MISSING STRING" (0x6FD880, 0x41360D–0x41361D), so a file without a SIGNA or SIGNB key shows that string on the line
	// [BIN:TPW-EXE:0x004E2860 sign line 1] an empty line 1 is formatted from the never-written .bss buffer 0x78A374 (the empty string), so an empty line stays empty
	// [BIN:TPW-EXE:0x004DB6F9 placed object sign lines] a placed object copies line 1 with 0x413020 and line 2 with 0x4130B0, both reading the OBJECT_NAMES row (table 0x7488D8) when the object is listed there and otherwise def+0x4F8 (SIGNA) and def+0x4FC (SIGNB)
	// [BIN:TPW-EXE:0x00413600 object text] the object text file's NAME, SIGNA and SIGNB sections are parsed with the default "MISSING STRING"; SIGNA/SIGNB are copied into each placed object as two 32-character lines (0x413020/0x4130B0, called at 0x4DB6F9/0x4DB70D)
	// [BIN:TPW-EXE:0x00468500 sign rebuild] the sign rebuild of the types with flag 0x8000000 reads line 1 through 0x4E2860 (which drops the last dash of a trailing "--") and line 2 through 0x4E2920
	public static (string Line1, string Line2)? ReadSignLines( ObjectCatalogEntry entry, string? language = null )
	{
		language ??= GameLanguage.IsSelected ? GameLanguage.Current.Name : GameLanguage.DefaultLanguage;
		try
		{
			var files = entry.FileSystem.GetFiles( entry.ArchivePath ).Where( file => file.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase ) ).ToArray();
			var file = files.FirstOrDefault( candidate => string.Equals( Path.GetFileNameWithoutExtension( candidate ), language, StringComparison.OrdinalIgnoreCase ) );
			if ( file != null )
			{
				var sections = Parse( entry.FileSystem.ReadAllBytes( entry.FileSystem.GetRelativePath( entry.FileSystem.GetAbsolutePath( file ) ) ) );
				var first = sections.GetValueOrDefault( "SIGNA" ) ?? MissingString;
				var second = sections.GetValueOrDefault( "SIGNB" ) ?? MissingString;
				return (StripTrailingDash( Truncate( first ) ), Truncate( second ));
			}
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException or DecoderFallbackException )
		{
			Log?.Warning( $"Bonus object {entry.ArchivePath} sign text could not be read: {exception.Message}" );
		}
		return null;
	}

	/// <summary>A line cut to <see cref="SignLineCharacters"/> UTF-16 units, backing off one when the cut would split a surrogate pair.</summary>
	public static string Truncate( string line )
	{
		if ( line.Length <= SignLineCharacters )
			return line;
		var length = char.IsHighSurrogate( line[SignLineCharacters - 1] ) && char.IsLowSurrogate( line[SignLineCharacters] ) ? SignLineCharacters - 1 : SignLineCharacters;
		return line[..length];
	}

	/// <summary>A sign's line 1 as the original's reader returns it: a line of at least two characters that ends in "--" loses its last '-' (one of the two).</summary>
	// [BIN:TPW-EXE:0x004E2860 sign line 1] when the line is longer than one character and its last two characters are '-' (0x2D) (0x4E28A1, 0x4E28AB), it is copied to the buffer 0x7CDC58 and terminated at index length - 1 (0x4E28DB: mov word [esi*2+0x7CDC56], 0), so one dash is dropped: "Super--" reads "Super-"
	public static string StripTrailingDash( string line ) => line.Length > 1 && line.EndsWith( "--", StringComparison.Ordinal ) ? line[..^1] : line;

	/// <summary>Sections of a bonus text file: a section name line followed by its text line(s) up to a blank line.</summary>
	public static IReadOnlyDictionary<string, string> Parse( byte[] bytes )
	{
		var text = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? Encoding.Unicode.GetString( bytes, 2, bytes.Length - 2 )
			: bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF ? Encoding.BigEndianUnicode.GetString( bytes, 2, bytes.Length - 2 )
			: Encoding.Latin1.GetString( bytes );
		var sections = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		string? current = null;
		var lines = new List<string>();
		void Close()
		{
			if ( current != null )
				sections[current] = string.Join( "\n", lines ).Trim();
			lines.Clear();
		}
		foreach ( var raw in text.Replace( "\r\n", "\n" ).Split( '\n' ) )
		{
			var line = raw.TrimEnd( '\r', '\0' );
			if ( current == null || (lines.Count == 0 && line.Length == 0) )
			{
				if ( line.Trim().Length == 0 )
					continue;
				if ( current == null )
				{
					current = line.Trim();
					continue;
				}
			}
			if ( line.Trim().Length == 0 )
			{
				Close();
				current = null;
				continue;
			}
			lines.Add( line.Trim() );
		}
		Close();
		return sections;
	}
}
