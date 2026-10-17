namespace OpenTPW.Mods;

/// <summary>Where a mod folder was found, in discovery precedence order.</summary>
public enum ModSource
{
	/// <summary><c>--mod-dir &lt;folder&gt;</c> (repeatable): one mod folder each.</summary>
	CommandLine,
	/// <summary><c>OPENTPW_MOD_DIRS</c>: mod folders separated by the platform's path separator.</summary>
	Environment,
	/// <summary><c>&lt;config&gt;/mods/&lt;id&gt;/</c>.</summary>
	Config,
	/// <summary><c>&lt;program&gt;/content/mods/&lt;id&gt;/</c>: data-only content shipped with OpenTPW.</summary>
	Program,
}

/// <summary>A mod folder found at startup: its manifest, or the reason it was refused.</summary>
public sealed record DiscoveredMod( string Id, string Directory, ModSource Source, ModManifest? Manifest, string? Error, byte[]? ManifestBytes = null )
{
	public bool IsValid => Manifest != null && Error == null;
}

/// <summary>
/// Finds files-based mods (docs/MODS.md): <c>--mod-dir</c>, then <c>OPENTPW_MOD_DIRS</c>, then
/// <c>&lt;config&gt;/mods/&lt;id&gt;/</c>, then <c>&lt;program&gt;/content/mods/&lt;id&gt;/</c>. The first folder with an id
/// wins; later folders with the same id are reported as shadowed. Nothing is loaded or executed: discovery only reads
/// manifests. The browser build discovers only the program folder: the mods shipped with the page are embedded and written below it at start (docs/WEB.md).
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
public static class ModDiscovery
{
	public const string EnvironmentVariable = "OPENTPW_MOD_DIRS";
	public const string FolderName = "mods";
	public const int MaximumFoldersPerRoot = 256;

	public static string ConfigModsDirectory( string configDirectory ) => Path.Combine( configDirectory, FolderName );

	public static string ProgramModsDirectory( string programDirectory ) => Path.Combine( programDirectory, "content", FolderName );

	/// <summary>The folders named by <c>OPENTPW_MOD_DIRS</c>.</summary>
	public static IReadOnlyList<string> SplitEnvironment( string? value ) => string.IsNullOrWhiteSpace( value )
		? Array.Empty<string>()
		: value.Split( Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );

	public static IReadOnlyList<DiscoveredMod> Discover( IEnumerable<string> commandLineFolders, IEnumerable<string> environmentFolders,
		string? configDirectory, string? programDirectory )
	{
		var found = new List<DiscoveredMod>();
		foreach ( var folder in commandLineFolders )
			found.Add( ReadNamed( folder, ModSource.CommandLine ) );
		foreach ( var folder in environmentFolders )
			found.Add( ReadNamed( folder, ModSource.Environment ) );
		if ( configDirectory != null )
			found.AddRange( ReadRoot( ConfigModsDirectory( configDirectory ), ModSource.Config ) );
		if ( programDirectory != null )
			found.AddRange( ReadRoot( ProgramModsDirectory( programDirectory ), ModSource.Program ) );

		// The first folder of an id wins (command line over environment over config over program).
		var result = new List<DiscoveredMod>();
		var owners = new Dictionary<string, DiscoveredMod>( StringComparer.Ordinal );
		foreach ( var mod in found )
		{
			if ( mod.IsValid && owners.TryGetValue( mod.Id, out var owner ) )
			{
				result.Add( mod with { Error = $"shadowed by the same id in {owner.Directory} ({owner.Source})" } );
				continue;
			}
			if ( mod.IsValid )
				owners[mod.Id] = mod;
			result.Add( mod );
		}
		return result;
	}

	private static IEnumerable<DiscoveredMod> ReadRoot( string root, ModSource source )
	{
		if ( !Directory.Exists( root ) )
			yield break;
		string? error = null;
		List<string> folders;
		try
		{
			folders = Directory.EnumerateDirectories( root ).Order( StringComparer.Ordinal ).ToList();
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			folders = new List<string>();
			error = exception.Message;
		}
		if ( error != null )
		{
			yield return new DiscoveredMod( Path.GetFileName( root ), root, source, null, $"the mods folder could not be read: {error}" );
			yield break;
		}
		if ( folders.Count > MaximumFoldersPerRoot )
		{
			yield return new DiscoveredMod( Path.GetFileName( root ), root, source, null, $"more than {MaximumFoldersPerRoot} mod folders; none were read" );
			yield break;
		}
		foreach ( var folder in folders )
			yield return Read( folder, source, requireFolderName: true );
	}

	/// <summary>A folder named on the command line or in the environment; a malformed path is a refused entry, not a crash.</summary>
	private static DiscoveredMod ReadNamed( string folder, ModSource source )
	{
		string full;
		try
		{
			full = Path.GetFullPath( folder );
		}
		catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException )
		{
			return new DiscoveredMod( folder, folder, source, null, $"is not a usable folder path ({exception.Message})" );
		}
		return Read( full, source, requireFolderName: false );
	}

	private static DiscoveredMod Read( string folder, ModSource source, bool requireFolderName )
	{
		var name = Path.GetFileName( folder.TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar ) );
		if ( !Directory.Exists( folder ) )
			return new DiscoveredMod( name, folder, source, null, "the folder does not exist" );
		ModManifest manifest;
		byte[] manifestBytes;
		try
		{
			// the bytes are kept: resolving checks that the file it hashes is the one that was parsed here
			manifestBytes = ModFiles.ReadBounded( Path.Combine( folder, ModManifest.FileName ), ModManifest.MaximumBytes, ModManifest.FileName );
			manifest = ModManifest.Parse( manifestBytes );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException )
		{
			return new DiscoveredMod( name, folder, source, null, exception.Message );
		}
		if ( requireFolderName && name != manifest.Id )
			return new DiscoveredMod( manifest.Id, folder, source, manifest, $"the folder must be named after its id '{manifest.Id}'", manifestBytes );
		if ( manifest.Category == ModCategory.Official && source != ModSource.Program )
			return new DiscoveredMod( manifest.Id, folder, source, manifest, $"'{ModManifest.OfficialPrefix}' ids are reserved for content shipped with OpenTPW", manifestBytes );
		return new DiscoveredMod( manifest.Id, folder, source, manifest, null, manifestBytes );
	}
}
