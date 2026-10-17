namespace OpenTPW;

/// <summary>
/// The sound categories of a content root that carries another original game's data (the Theme Park Inc install a mod
/// takes objects from). A Theme Park Inc ride script's <c>EVENT</c> ids name events of Theme Park Inc's own
/// <c>cat_rides</c>/<c>cat_ambient</c>/... catalogues, whose ids and banks differ from Theme Park World's (global
/// <c>cat_rides</c> holds 114 events there against 25 here), so the event is looked up in the object's own root.
/// </summary>
// [EXT:cross-edition-objects] OpenTPW's own per-root sound lookup; the original game only ever loads its own data root
public sealed class SoundRoot
{
	private readonly BaseFileSystem fileSystem;
	private readonly Dictionary<string, SoundCategory?> categories = new( StringComparer.OrdinalIgnoreCase );

	internal SoundRoot( BaseFileSystem fileSystem ) => this.fileSystem = fileSystem;

	/// <summary>
	/// The category an RSE <c>EVENT</c> type routes to (3 level rides, 4 level ambient, 5 global rides, 6 global kids, 7 global
	/// staff, 8 global ambient, 9 global UI; GameAudio.RseEventRoutes) for an object whose archive is in <paramref name="sourceTheme"/>
	/// of this root; null for other types, or when the root lacks the category (there is no fallback to the running game's).
	/// </summary>
	public SoundCategory? Category( int type, string? sourceTheme )
	{
		var (directory, name, level) = type switch
		{
			3 => ($"/levels/{sourceTheme}/Sound", "cat_rides", true),
			4 => ($"/levels/{sourceTheme}/Sound", "cat_ambient", true),
			5 => ("/global/sound", "cat_rides", false),
			6 => ("/global/sound", "cat_kids", false),
			7 => ("/global/sound", "cat_staff", false),
			8 => ("/global/sound", "cat_ambient", false),
			9 => ("/global/sound", "cat_ui", false),
			_ => ("", "", false),
		};
		if ( name.Length == 0 || (level && string.IsNullOrEmpty( sourceTheme )) )
			return null;
		var key = $"{directory}|{name}";
		lock ( categories )
		{
			if ( !categories.TryGetValue( key, out var category ) )
				categories[key] = category = SoundCategory.TryLoad( fileSystem, directory, name );
			return category;
		}
	}
}

/// <summary>Where an object's RSE sound events are looked up when it comes from another content root.</summary>
public readonly record struct RseSoundSource( SoundRoot Root, string? Theme )
{
	public SoundCategory? Category( int type ) => Root.Category( type, Theme );
}

/// <summary>
/// The content roots whose objects use their own sound banks (<see cref="SoundRoot"/>). Roots are registered once at startup
/// by the cross-edition objects extension; an object whose file system is not registered (the game's own, and official bonus
/// archives) keeps the running game's categories.
/// </summary>
// [EXT:cross-edition-objects] OpenTPW's own registry; the original game has one data root
public static class ContentSoundRoots
{
	private static readonly Dictionary<BaseFileSystem, SoundRoot> roots = new();

	/// <summary>Registers <paramref name="fileSystem"/> (the Data folder of another original game); registering twice returns the same root.</summary>
	public static SoundRoot Register( BaseFileSystem fileSystem )
	{
		lock ( roots )
		{
			if ( !roots.TryGetValue( fileSystem, out var root ) )
				roots[fileSystem] = root = new SoundRoot( fileSystem );
			return root;
		}
	}

	/// <summary>Whether <paramref name="fileSystem"/> is the registered Data folder of another original game (a mod's Theme Park Inc input with the cross-edition extension on).</summary>
	internal static bool IsRegistered( BaseFileSystem fileSystem )
	{
		lock ( roots )
			return roots.ContainsKey( fileSystem );
	}

	public static void Clear()
	{
		lock ( roots )
			roots.Clear();
	}

	/// <summary>The registered roots, to put back with <see cref="Restore"/> (the mod scopes of tests).</summary>
	internal static IReadOnlyDictionary<BaseFileSystem, SoundRoot> Snapshot()
	{
		lock ( roots )
			return new Dictionary<BaseFileSystem, SoundRoot>( roots );
	}

	internal static void Restore( IReadOnlyDictionary<BaseFileSystem, SoundRoot> snapshot )
	{
		lock ( roots )
		{
			roots.Clear();
			foreach ( var (fileSystem, root) in snapshot )
				roots[fileSystem] = root;
		}
	}

	/// <summary>The sound source of a catalogue entry: its root's banks and the theme folder its archive sits in; null for entries read from the game's own data.</summary>
	public static RseSoundSource? SourceOf( ObjectCatalogEntry entry )
	{
		lock ( roots )
		{
			if ( roots.Count == 0 || !roots.TryGetValue( entry.FileSystem, out var root ) )
				return null;
			return new RseSoundSource( root, ThemeOf( entry.ArchivePath ) );
		}
	}

	/// <summary>The folder after <c>levels</c> in a root-relative archive path (<c>/levels/science/rides/x</c> is <c>science</c>), or null.</summary>
	internal static string? ThemeOf( string archivePath )
	{
		var parts = archivePath.Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries );
		var index = Array.FindIndex( parts, part => string.Equals( part, "levels", StringComparison.OrdinalIgnoreCase ) );
		return index >= 0 && index + 1 < parts.Length ? parts[index + 1] : null;
	}
}
