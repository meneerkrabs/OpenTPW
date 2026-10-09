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
/// One original object (ride, shop, sideshow, feature, fixed item or track upgrade) of a theme, built from
/// its WAD archive and .sam layers. Values are raw original data; see docs/OBJECTS.md for what is verified.
/// </summary>
public sealed class ObjectCatalogEntry
{
	internal ObjectCatalogEntry( string theme, ObjectCategory category, string archivePath, ObjectSettings settings, ObjectShape shape,
		string modelPath, string? previewModelPath, string? scriptPath, IReadOnlyList<string> scripts, IReadOnlyList<ObjectAnimationFile> animations,
		IReadOnlyList<string> auxiliaryModels, IReadOnlyList<string> allModels )
	{
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
	/// <summary>OBJECT_NAMES text when <see cref="ObjectNameIndex"/> resolved, else <see cref="SettingsName"/>.</summary>
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
	public bool IsFixedItem => Settings.GetBool( "Info.DontApplyOffset" );
	/// <summary>Shapes without occupied cells (Mystery ride, Buy Land, Clear Land) are tools, not objects.</summary>
	public bool IsTool => !Shape.OccupiedCells.Any();
	/// <summary>Can be placed from a build menu: shown in a UI category, not a fixed item or tool. Upgrades attach to a ride's track.</summary>
	public bool IsBuildable => WhichUIType is >= 0 and <= 3 && !IsFixedItem && !IsTool && Category != ObjectCategory.Upgrade;
	/// <summary><c>Info.RideTypeStringIndex</c>: ITEMTYPES.str entry (e.g. 12 "Vertical Drop" for the Totem), or null.</summary>
	public int? RideTypeIndex => Settings.Has( "Info.RideTypeStringIndex" ) ? Settings.GetInt( "Info.RideTypeStringIndex" ) : null;
	public int NumSimultaneousAnimations => Math.Max( 1, Settings.GetInt( "UsageInfo.NumSimultAnims", 1 ) );
	/// <summary>Build cost: <c>Upgrades[0].CostOfUpgrade</c> ("cash cost when buying this item").</summary>
	public int BuildCost => Upgrades.FirstOrDefault( level => level.Level == 0 )?.GetInt( "CostOfUpgrade" ) ?? 0;
	/// <summary>Initial ride capacity: <c>Upgrades[0].InitCapacity</c>.</summary>
	public int InitialCapacity => Upgrades.FirstOrDefault( level => level.Level == 0 )?.GetInt( "InitCapacity" ) ?? 0;

	public ObjectAnimationFile? ResolveAnimation( int animation, int variant ) => ObjectAnimations.Resolve( Animations, animation, variant );

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
	public static ObjectCatalog Load( string theme )
	{
		if ( string.IsNullOrWhiteSpace( theme ) || theme.IndexOfAny( new[] { '/', '\\', '.' } ) >= 0 )
			throw new ArgumentException( "Themes are plain level directory names such as 'jungle'.", nameof( theme ) );
		var key = $"{FileSystem.GetAbsolutePath( "/" )}|{theme}";
		lock ( cache )
		{
			if ( cache.TryGetValue( key, out var cached ) )
				return cached;
			var catalog = new ObjectCatalog( theme, LoadEntries( theme ) );
			ObjectNames.Apply( catalog );
			cache[key] = catalog;
			return catalog;
		}
	}

	private static List<ObjectCatalogEntry> LoadEntries( string theme )
	{
		var entries = new List<ObjectCatalogEntry>();
		foreach ( var (directory, category, defaultsName) in CategoryDirectories )
		{
			var categoryPath = $"/levels/{theme}/{directory}";
			if ( !FileSystem.DirectoryExists( categoryPath ) )
				continue;
			var defaults = FileSystem.GetFiles( categoryPath ).Select( ToRelative )
				.FirstOrDefault( file => string.Equals( Path.GetFileName( file ), defaultsName, StringComparison.OrdinalIgnoreCase ) );
			var defaultLayer = defaults == null ? null : ObjectSettingsFile.Load( defaults );
			foreach ( var archive in FileSystem.GetDirectories( categoryPath ).Select( ToRelative ).OrderBy( path => path, StringComparer.OrdinalIgnoreCase ) )
			{
				var entry = LoadEntry( theme, category, archive, defaultLayer );
				if ( entry != null )
					entries.Add( entry );
			}
		}
		var duplicate = entries.GroupBy( entry => entry.InfoId ).FirstOrDefault( group => group.Count() > 1 );
		if ( duplicate != null )
			throw new InvalidDataException( $"Info.Id {duplicate.Key} is declared by several {theme} archives." );
		return entries;
	}

	private static ObjectCatalogEntry? LoadEntry( string theme, ObjectCategory category, string archive, ObjectSettingsFile? defaults )
	{
		var files = FileSystem.GetFiles( archive ).Select( ToRelative ).ToArray();
		var name = Path.GetFileName( archive );
		ObjectSettingsFile? main = null;
		var shared = new List<ObjectSettingsFile>();
		foreach ( var file in files.Where( file => file.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) ) )
		{
			var fileName = Path.GetFileName( file );
			// Difficulty (Easy_) and online (Online_) overlays are not part of the standard game settings.
			if ( fileName.StartsWith( "Easy_", StringComparison.OrdinalIgnoreCase ) || fileName.StartsWith( "Online_", StringComparison.OrdinalIgnoreCase ) )
				continue;
			var layer = ObjectSettingsFile.Load( file );
			if ( layer.Values.ContainsKey( "Info.Id" ) )
			{
				if ( main != null )
					throw new InvalidDataException( $"{archive} has several .sam files with an Info.Id." );
				main = layer;
			}
			else
				shared.Add( layer );
		}
		if ( main == null )
			return null;
		var layers = new List<ObjectSettingsFile>();
		if ( defaults != null )
			layers.Add( defaults );
		layers.AddRange( shared );
		layers.Add( main );
		var settings = new ObjectSettings( layers );
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
		return new ObjectCatalogEntry( theme, category, archive, settings, shape, model, preview, script, scripts, animations, auxiliary, models );
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
		foreach ( var (entry, index, length) in Match( catalog.Entries, english ) )
		{
			entry.ObjectNameIndex = index;
			entry.ObjectNameLength = length;
			if ( index + length <= localized.Length )
				entry.DisplayName = string.Join( " ", localized.Skip( index ).Take( length ).Where( text => text.Length > 0 ) );
		}
	}

	/// <summary>Pure matching step, exposed for tests.</summary>
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
