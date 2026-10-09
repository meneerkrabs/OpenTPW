using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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
		IReadOnlyList<string> auxiliaryModels, IReadOnlyList<string> allModels, BaseFileSystem fileSystem, int? bonusNumber )
	{
		FileSystem = fileSystem;
		BonusNumber = bonusNumber;
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
	public bool IsBonus => BonusNumber != null;
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
	/// <summary>Index of the first OBJECT_NAMES entry of this object (names may span two entries), or null.</summary>
	public int? ObjectNameIndex { get; internal set; }
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
	public bool HasQueue => Settings.GetBool( "Info.HasQueue" );
	/// <summary><c>Info.DontApplyOffset 1</c>: "a fixed item whose animation should be played relative to world (0,0)" (gates, bus, lights, ferry, seaplane, end).</summary>
	// [DATA:<object>.sam:Info.DontApplyOffset]
	public bool IsFixedItem => Settings.GetBool( "Info.DontApplyOffset" );
	/// <summary>Shapes without occupied cells (Mystery ride, Buy Land, Clear Land) are tools, not objects.</summary>
	public bool IsTool => !Shape.OccupiedCells.Any();
	/// <summary>Can be placed from a build menu: shown in a UI category, not a fixed item or tool. Upgrades attach to a ride's track.</summary>
	// [APPROX:RIDES-017] Buildable = WhichUIType 0–3, not fixed/tool/upgrade (DATA:Info.WhichUIType comment "4 = Not to be shown in UI") — evidence needed: original build-menu contents
	public bool IsBuildable => WhichUIType is >= 0 and <= 3 && !IsFixedItem && !IsTool && Category != ObjectCategory.Upgrade;
	/// <summary><c>Info.RideTypeStringIndex</c>: ITEMTYPES.str entry (e.g. 12 "Vertical Drop" for the Totem), or null.</summary>
	public int? RideTypeIndex => Settings.Has( "Info.RideTypeStringIndex" ) ? Settings.GetInt( "Info.RideTypeStringIndex" ) : null;
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

	private static readonly Dictionary<string, ObjectCatalog> cache = new( StringComparer.OrdinalIgnoreCase );
	private static readonly Regex BonusArchive = new( @"^(?<stem>_.+)_(?<number>\d+)$", RegexOptions.CultureInvariant );
	private static string? bonusDataRoot;
	private static bool bonusFromEnvironment = true;

	/// <summary>
	/// Optional official bonus-content directory (read-only), from <c>--bonus-data</c> or <c>OPENTPW_BONUS_DATA</c>.
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
		byId = entries.ToDictionary( entry => entry.InfoId );
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
		if ( string.IsNullOrWhiteSpace( theme ) || theme.IndexOfAny( new[] { '/', '\\', '.' } ) >= 0 )
			throw new ArgumentException( "Themes are plain level directory names such as 'jungle'.", nameof( theme ) );
		var key = $"{FileSystem.GetAbsolutePath( "/" )}|{BonusDataRoot}|{theme}|{easy}";
		lock ( cache )
		{
			if ( cache.TryGetValue( key, out var cached ) )
				return cached;
			var entries = LoadEntries( theme, FileSystem, null, easy );
			var bonus = OpenBonusFileSystem();
			if ( bonus != null )
			{
				foreach ( var entry in LoadEntries( theme, bonus, FileSystem, easy ) )
				{
					// [APPROX:RIDES-024] Bonus archives merge into the theme catalog; an Info.Id collision skips the bonus entry — evidence needed: original behaviour with dropped-in WADs
					if ( entries.Any( existing => existing.InfoId == entry.InfoId ) )
						Log?.Warning( $"Bonus object {entry.ArchivePath} reuses Info.Id {entry.InfoId}; it is skipped." );
					else
						entries.Add( entry );
				}
			}
			var catalog = new ObjectCatalog( theme, entries );
			RidesApproximations.LogOnce();
			ObjectNames.Apply( catalog );
			cache[key] = catalog;
			return catalog;
		}
	}

	/// <summary>
	/// Reads the archives of <paramref name="fileSystem"/>. Category defaults (Rides.sam, ...) always come from
	/// <paramref name="defaultsFileSystem"/> (the game data) when given: bonus archives are made to be dropped
	/// next to them. Directory names are matched case-insensitively.
	/// </summary>
	private static List<ObjectCatalogEntry> LoadEntries( string theme, BaseFileSystem fileSystem, BaseFileSystem? defaultsFileSystem, bool easy )
	{
		var entries = new List<ObjectCatalogEntry>();
		var defaultsSource = defaultsFileSystem ?? fileSystem;
		foreach ( var (directory, category, defaultsName) in CategoryDirectories )
		{
			var categoryPath = FindDirectory( fileSystem, "/levels", theme ) is { } themePath ? FindDirectory( fileSystem, themePath, directory ) : null;
			if ( categoryPath == null )
				continue;
			var defaultsDirectory = defaultsFileSystem == null ? categoryPath : $"/levels/{theme}/{directory}";
			var defaults = defaultsSource.DirectoryExists( defaultsDirectory )
				? defaultsSource.GetFiles( defaultsDirectory ).Select( file => Relative( defaultsSource, file ) )
					.FirstOrDefault( file => string.Equals( Path.GetFileName( file ), defaultsName, StringComparison.OrdinalIgnoreCase ) )
				: null;
			var defaultLayer = defaults == null ? null : ObjectSettingsFile.Load( defaultsSource, defaults );
			foreach ( var archive in fileSystem.GetDirectories( categoryPath ).Select( file => Relative( fileSystem, file ) ).OrderBy( path => path, StringComparer.OrdinalIgnoreCase ) )
			{
				ObjectCatalogEntry? entry;
				try
				{
					entry = LoadEntry( theme, category, archive, defaultLayer, fileSystem, easy );
				}
				catch ( Exception exception ) when ( defaultsFileSystem != null && exception is InvalidDataException or IOException )
				{
					// A broken bonus archive must not take the game's own catalog down.
					Log?.Warning( $"Bonus object archive {archive} is skipped: {exception.Message}" );
					continue;
				}
				if ( entry != null )
					entries.Add( entry );
			}
		}
		var duplicate = entries.GroupBy( entry => entry.InfoId ).FirstOrDefault( group => group.Count() > 1 );
		if ( duplicate != null )
			throw new InvalidDataException( $"Info.Id {duplicate.Key} is declared by several {theme} archives." );
		return entries;
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
		var name = Path.GetFileName( archive );
		// Official bonus archives are named _name_N (N = bonus number); their members use _name.
		int? bonusNumber = null;
		var bonusMatch = BonusArchive.Match( name );
		if ( bonusMatch.Success && !files.Any( file => string.Equals( Path.GetFileNameWithoutExtension( file ), name, StringComparison.OrdinalIgnoreCase ) && file.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) ) )
		{
			bonusNumber = int.Parse( bonusMatch.Groups["number"].Value, CultureInfo.InvariantCulture );
			name = bonusMatch.Groups["stem"].Value;
		}
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
		// [APPROX:RIDES-009] Shared (non-Info.Id) .sam files sit between the category defaults and the object file — evidence needed: which base file the binary's object loader is given
		var layers = new List<ObjectSettingsFile>();
		if ( defaults != null )
			layers.Add( defaults );
		layers.AddRange( shared );
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
		if ( bonusNumber != null )
			entry.DisplayName = BonusNames.Read( entry ) ?? entry.SettingsName;
		return entry;
	}

	// Entries inside archives come back data-root-relative, loose entries absolute.
	internal static string ToRelative( string entry ) => FileSystem.GetRelativePath( FileSystem.GetAbsolutePath( entry ) );
}

/// <summary>
/// Binds catalog entries to OBJECT_NAMES.str. The table holds one block per theme, each ending with
/// "Traffic Lights"; rides use two entries ("Inca", "Totem"), other objects one. No index field exists in
/// the .sam files, so an entry is bound when its English <c>Info.Name</c> equals one entry or two
/// consecutive entries (ignoring case, spaces and accents) inside the theme's block; the block is the one
/// with most matches. Unmatched objects (e.g. "Loudspeaker", "Gates") keep their .sam name.
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
		foreach ( var (entry, index, length) in Match( catalog.Entries.Where( entry => !entry.IsBonus ).ToList(), english ) )
		{
			entry.ObjectNameIndex = index;
			entry.ObjectNameLength = length;
			if ( index + length <= localized.Length )
				entry.DisplayName = string.Join( " ", localized.Skip( index ).Take( length ).Where( text => text.Length > 0 ) );
		}
	}

	/// <summary>Pure matching step, exposed for tests.</summary>
	// [APPROX:RIDES-010] OBJECT_NAMES index bound by English name equality within the theme block — evidence needed: binary name-index table
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
	// [APPROX:RIDES-025] Bonus name: selected language file, then English, then .sam Info.Name — evidence needed: original lookup of bonus name files
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
